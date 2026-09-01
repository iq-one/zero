using System.Collections.Immutable;
using System.Text;
using IQOne.Zero.Generators.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IQOne.Zero.Generators.Registration;

/// <summary>
/// Generates a module's declaration and its service registrations.
/// </summary>
/// <remarks>
/// Candidates are filtered at the syntax level; symbols are resolved only for those, so a
/// file with no <c>[ServiceMethod]</c> costs a syntax check and nothing more.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ServiceRegistrationGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var platform = context.AnalyzerConfigOptionsProvider.Select(static (_, _) => ZeroNames.Default);

        var services = context.SyntaxProvider
            .CreateSyntaxProvider(
                // Records too: a request is almost always a record, and missing them would
                // silently leave every request undeclared.
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 }
                         or RecordDeclarationSyntax { BaseList.Types.Count: > 0 },
                transform: static (ctx, _) =>
                    ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is INamedTypeSymbol symbol
                        ? SymbolCollector.DescribeService(symbol, ctx.Node)
                        : null)
            .Where(static c => c is not null)
            .Select(static (c, _) => c!)
            .Collect();

        // A routed type need not implement anything, so the service provider above would
        // never see it -- and a route on a non-request would go unreported. Filtered by
        // attribute name in syntax only; the full type is verified during emission.
        var routed = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => HasRouteAttribute(node),
                transform: static (ctx, _) =>
                    ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is INamedTypeSymbol symbol
                        ? SymbolCollector.DescribeService(symbol, ctx.Node)
                        : null)
            .Where(static c => c is not null)
            .Select(static (c, _) => c!)
            .Collect();

        var moduleInfo = context.CompilationProvider.Select(static (compilation, _) =>
        {
            var references = ImmutableArray.CreateBuilder<string>();
            var modules = ImmutableArray.CreateBuilder<ModuleReference>();

            foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                references.Add(assembly.Name);

                if (assembly.GetTypeByMetadataName($"{assembly.Name}.Module") is not { } moduleType) continue;

                modules.Add(new ModuleReference(
                    moduleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    new EquatableArray<string>([.. moduleType.AllInterfaces.Select(i => i.ToDisplayString())])));
            }

            return new ModuleInfo(
                compilation.AssemblyName ?? "Module",
                new EquatableArray<string>(references.ToImmutable()),
                new EquatableArray<ModuleReference>(modules.ToImmutable()));
        });

        context.RegisterSourceOutput(
            services.Combine(routed).Combine(moduleInfo).Combine(platform),
            static (spc, input) => Emit(
                spc, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    private static readonly string[] RouteAttributeNames = ["Get", "Post", "Put", "Patch", "Delete"];

    private static bool HasRouteAttribute(SyntaxNode node)
        => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 } declaration
        && declaration.AttributeLists.Any(list => list.Attributes.Any(attribute =>
        {
            var name = attribute.Name.ToString();
            var dot = name.LastIndexOf('.');

            if (dot >= 0) name = name.Substring(dot + 1);

            if (name.EndsWith("Attribute", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Attribute".Length);

            return Array.IndexOf(RouteAttributeNames, name) >= 0;
        }));

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<ServiceCandidate> serviceCandidates,
        ImmutableArray<ServiceCandidate> routedCandidates,
        ModuleInfo moduleInfo,
        ZeroNames platform)
    {
        // A project that does not reference the module system is not a module.
        if (!moduleInfo.ReferencedAssemblies.Any(a => a == platform.CoreAssembly)) return;

        var hasError = false;

        void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params object[] args)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location?.ToLocation(), args));
            if (descriptor.DefaultSeverity == DiagnosticSeverity.Error) hasError = true;
        }

        var services = ResolveServices(serviceCandidates, platform, Report);

        hasError |= DetectCaptiveDependencies(services, Report);

        // Dispatch is generated only for an assembly that references messaging; an
        // application that does not use commands and queries pays nothing for them.
        var messaging = moduleInfo.ReferencedAssemblies.Any(a => a == platform.MessagingAssembly);

        var requests = messaging
            ? ResolveRequests(serviceCandidates, platform)
            : new RequestSet([], []);

        var web = moduleInfo.ReferencedAssemblies.Any(a => a == platform.WebAssembly);

        var endpoints = web
            ? ResolveEndpoints(
                [.. serviceCandidates.Concat(routedCandidates)
                    .GroupBy(c => c.ImplementationTypeName, StringComparer.Ordinal)
                    .Select(g => g.First())],
                platform,
                Report)
            : [];

        if (hasError) return;

        var dependencies = moduleInfo.ModuleTypes
            .Where(m => m.Interfaces.Any(i => i == platform.ModuleInterface))
            .Select(m => m.TypeName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        context.AddSource("Module.g.cs", SourceText.From(
            Render(moduleInfo.AssemblyName, dependencies, services, requests, messaging, endpoints, web, platform), Encoding.UTF8));
    }

    /// <summary>Requests declared in this assembly, and the handlers that serve them.</summary>
    private sealed record RequestSet(List<string> Declared, List<RequestDescriptor> Handlers);

    private static RequestSet ResolveRequests(
        ImmutableArray<ServiceCandidate> candidates, ZeroNames platform)
    {
        var declared = new List<string>();
        var handlers = new List<RequestDescriptor>();

        foreach (var candidate in candidates)
        {
            var handled = candidate.ClosedInterfaces.FirstOrDefault(i =>
                i.OpenGenericName == platform.RequestHandlerInterface && i.TypeArguments.Count == 2);

            if (handled is not null && candidate.IsConcrete)
            {
                handlers.Add(new RequestDescriptor(
                    handled.TypeArguments[0],
                    handled.TypeArguments[1],
                    candidate.ImplementationTypeName,
                    candidate.Location));

                continue;
            }

            // A request declares itself so that startup can report one nobody handles.
            // Abstract requests are excluded: only a concrete type is ever dispatched.
            var request = candidate.ClosedInterfaces.FirstOrDefault(i =>
                i.OpenGenericName == platform.RequestInterface && i.TypeArguments.Count == 1);

            if (request is not null && candidate.IsConcrete) declared.Add(candidate.ImplementationTypeName);
        }

        declared.Sort(StringComparer.Ordinal);
        handlers.Sort((a, b) => string.CompareOrdinal(a.RequestTypeName, b.RequestTypeName));

        return new RequestSet(declared, handlers);
    }

    /// <summary>Requests that declare a route, and the endpoint each becomes.</summary>
    private static List<EndpointDescriptor> ResolveEndpoints(
        ImmutableArray<ServiceCandidate> candidates,
        ZeroNames platform,
        Action<DiagnosticDescriptor, LocationInfo?, object[]> report)
    {
        var endpoints = new List<EndpointDescriptor>();

        foreach (var candidate in candidates)
        {
            var route = candidate.Attributes
                .Select(a => (Attribute: a, Method: ZeroNames.RouteAttributes
                    .FirstOrDefault(r => r.Attribute == a.TypeName).Method))
                .FirstOrDefault(x => x.Method is not null);

            if (route.Method is null) continue;

            var request = candidate.ClosedInterfaces.FirstOrDefault(i =>
                i.OpenGenericName == platform.RequestInterface && i.TypeArguments.Count == 1);

            if (request is null)
            {
                report(Diagnostics.RouteOnNonRequest, candidate.Location, [candidate.TypeName]);
                continue;
            }

            var arguments = route.Attribute.Arguments.ToArray();
            var pattern = arguments.FirstOrDefault(a => !a.Contains("=")) ?? string.Empty;

            if (pattern.Length == 0)
            {
                report(Diagnostics.EmptyRoutePattern, candidate.Location, [candidate.TypeName]);
                continue;
            }

            endpoints.Add(new EndpointDescriptor(
                route.Method,
                pattern,
                Named(arguments, "Name") ?? candidate.TypeName,
                Named(arguments, "Tag"),
                Named(arguments, "Policy"),
                string.Equals(Named(arguments, "AllowAnonymous"), "True", StringComparison.OrdinalIgnoreCase),
                candidate.ImplementationTypeName,
                request.TypeArguments[0],
                candidate.Location));
        }

        endpoints.Sort((a, b) => string.CompareOrdinal(a.Pattern + a.Method, b.Pattern + b.Method));

        return endpoints;
    }

    private static string? Named(string[] arguments, string key)
    {
        var prefix = key + "=";

        return arguments.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?.Substring(prefix.Length)
            is { Length: > 0 } value ? value : null;
    }

    private static List<ServiceRegistrationDescriptor> ResolveServices(
        ImmutableArray<ServiceCandidate> candidates,
        ZeroNames platform,
        Action<DiagnosticDescriptor, LocationInfo?, object[]> report)
    {
        var lifetimeByInterface = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{platform.Descriptors}.ISingleton"] = "Singleton",
            [$"{platform.Descriptors}.IScoped"] = "Scoped",
            [$"{platform.Descriptors}.ITransient"] = "Transient",
            [$"{platform.Descriptors}.IThread"] = "Transient"
        };

        var nonService = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{platform.Descriptors}.IServiceDescriptor",
            $"{platform.Descriptors}.ISingletonInstance",
            platform.Ignored,
            platform.Required
        };

        foreach (var key in lifetimeByInterface.Keys) nonService.Add(key);

        var result = new List<ServiceRegistrationDescriptor>();
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var lifetimes = candidate.AllInterfaces
                .Where(lifetimeByInterface.ContainsKey)
                .Select(i => lifetimeByInterface[i])
                .Distinct()
                .ToList();

            if (lifetimes.Count == 0) continue;
            if (candidate.AllInterfaces.Any(i => i == platform.Ignored)) continue;

            if (lifetimes.Count > 1)
                report(Diagnostics.MultipleLifetimes, candidate.Location,
                    [candidate.TypeName, string.Join(", ", lifetimes)]);

            if (!candidate.IsConcrete)
                report(Diagnostics.RegistrationTargetInvalid, candidate.Location, [candidate.TypeName]);

            var attribute = candidate.Attributes
                .FirstOrDefault(a => a.TypeName.StartsWith(platform.ServiceTypesAttribute, StringComparison.Ordinal));

            var key = attribute?.Arguments.FirstOrDefault(a => a.StartsWith("Key=", StringComparison.Ordinal))?.Substring(4);
            var registerSelf = attribute?.Arguments.Any(a => a.Contains("ServiceSelectorType")) == true;

            var declared = attribute?.Arguments.Where(a => a.StartsWith("global::", StringComparison.Ordinal)).ToList() ?? [];

            var serviceTypes = declared.Count > 0
                ? declared
                : Resolve(candidate, platform, nonService);

            if (serviceTypes.Count == 0 && !registerSelf)
                report(Diagnostics.ServiceTypeNotResolved, candidate.Location, [candidate.TypeName]);

            foreach (var serviceType in serviceTypes)
            {
                if (owner.TryGetValue(serviceType, out var existing) && key is null)
                    report(Diagnostics.DuplicateRegistration, candidate.Location,
                        [serviceType, existing, candidate.ImplementationTypeName]);

                owner[serviceType] = candidate.ImplementationTypeName;
            }

            result.Add(new ServiceRegistrationDescriptor(
                candidate.ImplementationTypeName,
                new EquatableArray<string>([.. serviceTypes]),
                lifetimes[0], key, registerSelf,
                candidate.ConstructorDependencies,
                candidate.Location));
        }

        return result;
    }

    private static List<string> Resolve(ServiceCandidate candidate, ZeroNames platform, HashSet<string> nonService)
    {
        // A handler is registered under the closed interface the pipeline resolves, not by
        // the naming convention. The convention would pick the open generic it declares --
        // IQueryHandler<TQuery, TResponse> -- which is not a type anything can be registered as.
        var handled = candidate.ClosedInterfaces.FirstOrDefault(i =>
            i.OpenGenericName == platform.RequestHandlerInterface && i.TypeArguments.Count == 2);

        if (handled is not null)
            return [$"global::{platform.RequestHandlerInterface}<{handled.TypeArguments[0]}, {handled.TypeArguments[1]}>"];

        var required = candidate.AllInterfaces
            .Where(i => !nonService.Contains(i) && i.StartsWith(platform.Root, StringComparison.Ordinal) is false)
            .ToList();

        var direct = candidate.DirectInterfaces.Where(i => !nonService.Contains(i)).ToList();

        if (direct.Count == 0) return [];

        var byConvention = SymbolCollector.DefaultInterface(candidate.TypeName, direct);

        return byConvention is not null ? [Global(byConvention)]
             : direct.Count == 1 ? [Global(direct[0])]
             : [];
    }

    private static bool DetectCaptiveDependencies(
        List<ServiceRegistrationDescriptor> services,
        Action<DiagnosticDescriptor, LocationInfo?, object[]> report)
    {
        var lifetime = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var service in services)
            foreach (var serviceType in service.ServiceTypeNames)
                lifetime[serviceType] = service.Lifetime;

        var found = false;

        foreach (var service in services.Where(s => s.Lifetime == "Singleton"))
            foreach (var dependency in service.ConstructorDependencies)
                if (lifetime.TryGetValue(dependency, out var dependencyLifetime) && dependencyLifetime == "Scoped")
                {
                    report(Diagnostics.CaptiveDependency, service.Location,
                        [service.ImplementationTypeName, dependency, dependencyLifetime]);
                    found = true;
                }

        return found;
    }

    private static string Global(string name) => name.StartsWith("global::", StringComparison.Ordinal) ? name : $"global::{name}";

    private static string Simple(string name)
    {
        var index = name.LastIndexOf('.');
        return index < 0 ? name : name.Substring(index + 1);
    }

    private static string Render(
        string assemblyName,
        List<string> dependencies,
        List<ServiceRegistrationDescriptor> services,
        RequestSet requests,
        bool messaging,
        List<EndpointDescriptor> endpoints,
        bool web,
        ZeroNames platform)
    {
        var ns = Sanitize(assemblyName);
        var modules = $"global::{platform.Modules}";
        var di = "global::Microsoft.Extensions.DependencyInjection";

        var b = new StringBuilder();

        b.AppendLine("// <auto-generated/>");
        b.AppendLine("#nullable enable");
        b.AppendLine();
        b.AppendLine($"namespace {ns};");
        b.AppendLine();
        b.AppendLine("/// <summary>Generated. Add module-specific registrations by implementing");
        b.AppendLine("/// <c>OnConfigureServices</c> in a partial of this class.</summary>");
        b.AppendLine("public sealed partial class Module");
        b.AppendLine($"    : {modules}.IModule,");
        b.AppendLine($"      {modules}.IModuleConfigureServicesStep");
        b.AppendLine("{");
        b.AppendLine($"    public string Name => \"{assemblyName}\";");
        b.AppendLine();
        b.AppendLine("    /// <summary>Derived from the assembly reference graph.</summary>");
        b.AppendLine("    public global::System.Collections.Generic.IReadOnlyList<global::System.Type> Dependencies { get; } =");
        b.AppendLine("    [");

        foreach (var dependency in dependencies) b.AppendLine($"        typeof({dependency}),");

        b.AppendLine("    ];");
        b.AppendLine();
        b.AppendLine("    public global::System.Threading.Tasks.ValueTask OnConfigureServicesAsync(");
        b.AppendLine($"        {modules}.IModuleServiceContext context,");
        b.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        b.AppendLine("    {");
        b.AppendLine("        RegisterServices(context.Services);");

        if (messaging)
            b.AppendLine($"        RegisterRequests(global::{platform.ModuleServiceContextExtensions}.Requests(context));");

        if (web)
            b.AppendLine($"        MapEndpoints(global::{platform.WebModuleExtensions}.Endpoints(context));");

        b.AppendLine("        OnConfigureServices(context);");
        b.AppendLine();
        b.AppendLine("        return default;");
        b.AppendLine("    }");
        b.AppendLine();
        b.AppendLine($"    partial void OnConfigureServices({modules}.IModuleServiceContext context);");
        b.AppendLine();

        b.AppendLine($"    private static void RegisterServices({di}.IServiceCollection services)");
        b.AppendLine("    {");

        if (services.Count == 0) b.AppendLine("        // No type in this assembly carries a lifetime marker.");

        foreach (var service in services)
        {
            b.AppendLine($"        // {service.Lifetime}: {service.ImplementationTypeName}");

            foreach (var serviceType in service.ServiceTypeNames)
            {
                var method = service.Key is null ? $"Add{service.Lifetime}" : $"AddKeyed{service.Lifetime}";
                var args = service.Key is null ? "services" : $"services, \"{service.Key}\"";

                b.AppendLine($"        {di}.ServiceCollectionServiceExtensions.{method}<{serviceType}, {service.ImplementationTypeName}>({args});");
            }

            if (service.RegisterSelf || service.ServiceTypeNames.Count == 0)
                b.AppendLine($"        {di}.ServiceCollectionServiceExtensions.Add{service.Lifetime}<{service.ImplementationTypeName}>(services);");
        }

        b.AppendLine("    }");
        b.AppendLine();

        if (messaging) RenderRequests(b, requests, platform);

        if (web) RenderEndpoints(b, endpoints, platform);

        b.AppendLine("}");

        return b.ToString();
    }

    /// <summary>
    /// Emits the dispatch rows for this assembly.
    /// </summary>
    /// <remarks>
    /// Both type arguments are known here, so the emitted lambda calls a closed generic and
    /// dispatching costs a dictionary read and a cast. Nothing is resolved by reflection,
    /// and the pair is checked by the compiler rather than at the first send.
    /// </remarks>
    private static void RenderRequests(StringBuilder b, RequestSet requests, ZeroNames platform)
    {
        var messaging = $"global::{platform.Messaging}";

        b.AppendLine($"    private static void RegisterRequests({messaging}.IRequestRegistryBuilder builder)");
        b.AppendLine("    {");

        if (requests.Declared.Count == 0 && requests.Handlers.Count == 0)
            b.AppendLine("        // This assembly declares no request and handles none.");

        foreach (var request in requests.Declared)
            b.AppendLine($"        builder.Declare(typeof({request}));");

        if (requests.Declared.Count > 0 && requests.Handlers.Count > 0) b.AppendLine();

        foreach (var handler in requests.Handlers)
        {
            b.AppendLine($"        // {handler.RequestTypeName} -> {handler.HandlerTypeName}");
            b.AppendLine($"        builder.Add(new {messaging}.RequestEntry(");
            b.AppendLine($"            typeof({handler.RequestTypeName}),");
            b.AppendLine($"            typeof({handler.ResponseTypeName}),");
            b.AppendLine($"            typeof({handler.HandlerTypeName}),");
            b.AppendLine("            static (services, request, cancellationToken) =>");
            b.AppendLine($"                {messaging}.RequestPipeline.RunAsync<{handler.RequestTypeName}, {handler.ResponseTypeName}>(");
            b.AppendLine($"                    ({handler.RequestTypeName})request, services, cancellationToken)));");
            b.AppendLine();
        }

        b.AppendLine("    }");
        b.AppendLine();
    }

    /// <summary>
    /// Emits one endpoint per routed request.
    /// </summary>
    /// <remarks>
    /// A real ASP.NET endpoint each, not one catch-all route: authorization, rate limiting,
    /// caching, OpenAPI and telemetry all attach per method, and a wrong verb gives 405
    /// instead of 404.
    /// </remarks>
    private static void RenderEndpoints(StringBuilder b, List<EndpointDescriptor> endpoints, ZeroNames platform)
    {
        var web = $"global::{platform.Web}";

        b.AppendLine($"    private static void MapEndpoints({web}.IEndpointRegistryBuilder builder)");
        b.AppendLine("    {");

        if (endpoints.Count == 0) b.AppendLine("        // No request in this assembly declares a route.");

        foreach (var endpoint in endpoints)
        {
            b.AppendLine($"        // {endpoint.Method} {endpoint.Pattern}");
            b.AppendLine($"        builder.Add(new {web}.ZeroEndpointDescriptor(");
            b.AppendLine($"            \"{endpoint.Method}\",");
            b.AppendLine($"            \"{endpoint.Pattern}\",");
            b.AppendLine($"            \"{endpoint.Name}\",");
            b.AppendLine($"            {Literal(endpoint.Tag)},");
            b.AppendLine($"            {Literal(endpoint.Policy)},");
            b.AppendLine($"            {(endpoint.AllowAnonymous ? "true" : "false")},");
            b.AppendLine($"            typeof({endpoint.RequestTypeName}),");
            b.AppendLine($"            typeof({endpoint.ResponseTypeName}),");
            b.AppendLine($"            static context => {web}.ZeroEndpoint.RunAsync<{endpoint.RequestTypeName}, {endpoint.ResponseTypeName}>(context)));");
            b.AppendLine();
        }

        b.AppendLine("    }");
        b.AppendLine();
    }

    private static string Literal(string? value) => value is null ? "null" : $"\"{value}\"";

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
            builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_');

        return builder.ToString();
    }
}
