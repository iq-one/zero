using System.Collections.Immutable;
using System.Text;
using IQOne.Zero.Regify.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IQOne.Zero.Regify.Dispatch;

/// <summary>
/// Generates a module's dispatch table, handler registrations and service
/// registrations.
/// </summary>
/// <remarks>
/// Every name this generator looks for or emits derives from
/// <c>RegifyPlatformNamespace</c>, so it carries no product-specific identifier.
/// Candidates are filtered at the syntax level; symbols are resolved only for those.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ServiceDispatchGenerator : IIncrementalGenerator
{
    private const string AttributeSimpleName = "ServiceMethod";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var platform = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            PlatformNames.From(
                options.GlobalOptions.TryGetValue(PlatformNames.RootProperty, out var root) ? root : null));

        var handlers = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => HasServiceMethodAttribute(node),
                transform: static (ctx, _) =>
                    ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) is INamedTypeSymbol symbol
                        ? SymbolCollector.DescribeHandler(symbol, ctx.Node)
                        : null)
            .Where(static c => c is not null)
            .Select(static (c, _) => c!)
            .Collect();

        var services = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
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
            handlers.Combine(services).Combine(moduleInfo).Combine(platform),
            static (spc, input) => Emit(
                spc, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    // Syntax-only filter. The fully qualified name is verified during emission,
    // once the platform namespace is known.
    private static bool HasServiceMethodAttribute(SyntaxNode node)
        => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 } declaration
        && declaration.AttributeLists.Any(list => list.Attributes.Any(attribute =>
        {
            var name = attribute.Name.ToString();
            var index = name.LastIndexOf('.');

            if (index >= 0) name = name.Substring(index + 1);

            return name is AttributeSimpleName or AttributeSimpleName + "Attribute";
        }));

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<HandlerCandidate> handlerCandidates,
        ImmutableArray<ServiceCandidate> serviceCandidates,
        ModuleInfo moduleInfo,
        PlatformNames platform)
    {
        // Projects that do not reference the dispatch assembly are not modules.
        if (!moduleInfo.ReferencedAssemblies.Any(a => a == platform.DispatchAssembly)) return;

        var hasError = false;

        void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params object[] args)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location?.ToLocation(), args));
            if (descriptor.DefaultSeverity == DiagnosticSeverity.Error) hasError = true;
        }

        var handlers = ResolveHandlers(handlerCandidates, platform, Report, out var routeErrors);
        hasError |= routeErrors;

        var services = ResolveServices(serviceCandidates, platform, Report);

        hasError |= DetectCaptiveDependencies(handlers, services, Report);

        if (hasError) return;

        var dependencies = moduleInfo.ModuleTypes
            .Where(m => m.Interfaces.Any(i => i == platform.ModuleInterface))
            .Select(m => m.TypeName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        context.AddSource("Module.g.cs", SourceText.From(
            Render(moduleInfo.AssemblyName, dependencies, handlers, services, platform), Encoding.UTF8));
    }

    private static List<HandlerDescriptor> ResolveHandlers(
        ImmutableArray<HandlerCandidate> candidates,
        PlatformNames platform,
        Action<DiagnosticDescriptor, LocationInfo?, object[]> report,
        out bool hasError)
    {
        hasError = false;

        var result = new List<HandlerDescriptor>();
        var seen = new Dictionary<string, HandlerDescriptor>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            var routes = candidate.Attributes
                .Where(a => a.TypeName == platform.ServiceMethodAttribute)
                .ToList();

            if (routes.Count == 0) continue;   // a same-named attribute from elsewhere

            var handlerInterface = candidate.Interfaces
                .FirstOrDefault(i => i.OpenGenericName == platform.ServiceHandlerInterface
                                  && i.TypeArguments.Count == 2);

            if (handlerInterface is null)
            {
                report(Diagnostics.HandlerInterfaceMissing, candidate.Location, [Simple(candidate.HandlerTypeName)]);
                hasError = true;
                continue;
            }

            if (!candidate.IsConcrete)
            {
                report(Diagnostics.AbstractHandler, candidate.Location, [Simple(candidate.HandlerTypeName)]);
                hasError = true;
            }

            var arguments = handlerInterface.TypeArguments.ToArray();

            if (!candidate.RequestBaseChain.Any(b => b == platform.ServiceRequest))
            {
                report(Diagnostics.RequestNotServiceRequest, candidate.Location,
                    [Simple(candidate.HandlerTypeName), Simple(arguments[0])]);
                hasError = true;
            }

            foreach (var route in routes)
            {
                var parts = route.Arguments.ToArray();

                if (parts.Length < 3) continue;

                if (parts.Any(string.IsNullOrEmpty))
                {
                    report(Diagnostics.EmptyRouteSegment, candidate.Location, [Simple(candidate.HandlerTypeName)]);
                    hasError = true;
                    continue;
                }

                var descriptor = new HandlerDescriptor(
                    parts[0], parts[1], parts[2],
                    candidate.HandlerTypeName, arguments[0], arguments[1], candidate.Location);

                var key = $"{parts[0]}/{parts[1]}/{parts[2]}";

                if (seen.TryGetValue(key, out var existing))
                {
                    report(Diagnostics.DuplicateServiceMethod, candidate.Location,
                        [parts[0], parts[1], parts[2], existing.HandlerTypeName, descriptor.HandlerTypeName]);
                    hasError = true;
                    continue;
                }

                seen[key] = descriptor;
                result.Add(descriptor);
            }
        }

        return result;
    }

    private static List<ServiceRegistrationDescriptor> ResolveServices(
        ImmutableArray<ServiceCandidate> candidates,
        PlatformNames platform,
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

    private static List<string> Resolve(ServiceCandidate candidate, PlatformNames platform, HashSet<string> nonService)
    {
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
        List<HandlerDescriptor> handlers,
        List<ServiceRegistrationDescriptor> services,
        Action<DiagnosticDescriptor, LocationInfo?, object[]> report)
    {
        var lifetime = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var service in services)
            foreach (var serviceType in service.ServiceTypeNames)
                lifetime[serviceType] = service.Lifetime;

        foreach (var handler in handlers) lifetime[handler.HandlerTypeName] = "Scoped";

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
        List<HandlerDescriptor> handlers,
        List<ServiceRegistrationDescriptor> services,
        PlatformNames platform)
    {
        var ns = Sanitize(assemblyName);
        var modules = $"global::{platform.Modules}";
        var dispatch = $"global::{platform.Dispatch}";
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
        b.AppendLine($"        RegisterServiceMethods({dispatch}.ModuleServiceContextExtensions.Registry(context));");
        b.AppendLine("        RegisterHandlers(context.Services);");
        b.AppendLine("        OnConfigureServices(context);");
        b.AppendLine();
        b.AppendLine("        return default;");
        b.AppendLine("    }");
        b.AppendLine();
        b.AppendLine($"    partial void OnConfigureServices({modules}.IModuleServiceContext context);");
        b.AppendLine();

        b.AppendLine($"    private static void RegisterServices({di}.IServiceCollection services)");
        b.AppendLine("    {");

        if (services.Count == 0) b.AppendLine("        // yasam suresi isareti tasiyan tip yok");

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

        b.AppendLine($"    private static void RegisterServiceMethods({dispatch}.IServiceRegistryBuilder builder)");
        b.AppendLine("    {");

        if (handlers.Count == 0) b.AppendLine("        // [ServiceMethod] ile isaretli handler yok");

        foreach (var h in handlers)
        {
            var route = $"{h.Module}/{h.Service}/{h.Method}";

            b.AppendLine($"        // {route}");
            b.AppendLine($"        builder.Add(new {dispatch}.ServiceEntry(");
            b.AppendLine($"            \"{h.Module}\", \"{h.Service}\", \"{h.Method}\",");
            b.AppendLine($"            typeof({h.RequestTypeName}), typeof({h.ResponseTypeName}), typeof({h.HandlerTypeName}),");
            b.AppendLine("            static async (serviceProvider, body, cancellationToken) =>");
            b.AppendLine("            {");
            b.AppendLine($"                var request = body.ToObject<{h.RequestTypeName}>()");
            b.AppendLine($"                    ?? throw new global::{platform.ValidationException}(");
            b.AppendLine($"                        \"Istek govdesi cozumlenemedi: {route}\");");
            b.AppendLine();
            b.AppendLine($"                var handler = {di}.ServiceProviderServiceExtensions");
            b.AppendLine($"                    .GetRequiredService<{h.HandlerTypeName}>(serviceProvider);");
            b.AppendLine();
            b.AppendLine("                var result = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);");
            b.AppendLine();
            b.AppendLine($"                return new global::{platform.ServiceResponse}<{h.ResponseTypeName}>(result);");
            b.AppendLine("            }));");
            b.AppendLine();
        }

        b.AppendLine("    }");
        b.AppendLine();

        b.AppendLine($"    private static void RegisterHandlers({di}.IServiceCollection services)");
        b.AppendLine("    {");

        var handlerTypes = handlers.Select(h => h.HandlerTypeName).Distinct().ToList();

        if (handlerTypes.Count == 0) b.AppendLine("        // kaydedilecek handler yok");

        foreach (var handlerType in handlerTypes)
            b.AppendLine($"        {di}.ServiceCollectionServiceExtensions.AddScoped<{handlerType}>(services);");

        b.AppendLine("    }");
        b.AppendLine("}");

        return b.ToString();
    }

    private static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
            builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_');

        return builder.ToString();
    }
}
