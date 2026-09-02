using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using IQOne.Zero.Generators.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

        // A routed type need not implement anything, and a lifetime attribute is written
        // precisely where the abstraction says nothing, so neither would be seen by the
        // provider above. Filtered by attribute name in syntax only; the full type is
        // verified during emission.
        var annotated = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => HasZeroAttribute(node),
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

                // Sanitized, because that is the namespace the module was emitted into. An
                // assembly named Acme.Billing-Core emits Acme.Billing_Core.Module, and looking
                // for the raw name found nothing at all.
                if (assembly.GetTypeByMetadataName($"{Sanitize(assembly.Name)}.Module") is not { } moduleType)
                    continue;

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
            services.Combine(annotated).Combine(moduleInfo).Combine(platform),
            static (spc, input) => Emit(
                spc, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    /// <summary>
    /// Attribute names worth resolving a symbol for.
    /// </summary>
    /// <remarks>
    /// Matched by simple name only, so a same-named attribute of the consumer's own costs one
    /// symbol lookup and is then discarded. The full type name decides during emission.
    /// </remarks>
    private static readonly string[] AnnotationNames =
    [
        "Get", "Post", "Put", "Patch", "Delete",
        "ServiceTypes", "DependsOn", "LifeStyle",
        "Singleton", "Scoped", "Transient", "Thread", "Pooled", "Custom", "Bound", "Undefined"
    ];

    private static bool HasZeroAttribute(SyntaxNode node)
        => node is TypeDeclarationSyntax { AttributeLists.Count: > 0 } declaration
        && declaration.AttributeLists.Any(list => list.Attributes.Any(attribute =>
        {
            var name = attribute.Name.ToString();
            var angle = name.IndexOf('<');

            // Before the namespace: a generic attribute's type argument may itself be qualified.
            if (angle >= 0) name = name.Substring(0, angle);

            var dot = name.LastIndexOf('.');

            if (dot >= 0) name = name.Substring(dot + 1);

            if (name.EndsWith("Attribute", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Attribute".Length);

            return Array.IndexOf(AnnotationNames, name) >= 0;
        }));

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<ServiceCandidate> serviceCandidates,
        ImmutableArray<ServiceCandidate> annotatedCandidates,
        ModuleInfo moduleInfo,
        ZeroNames platform)
    {
        // A project that does not reference the module system is not a module.
        if (!moduleInfo.ReferencedAssemblies.Any(a => a == platform.CoreAssembly)) return;

        void Report(DiagnosticDescriptor descriptor, LocationInfo? location, params object[] args)
            => context.ReportDiagnostic(Diagnostic.Create(descriptor, location?.ToLocation(), args));

        // A partial class declared across two files arrives once per declaration, and every
        // one of them carries the same symbol facts. Registering each produced two identical
        // AddScoped calls, two identical dispatch rows -- which throws at startup -- and a
        // ZERO010 naming the same type as both implementations.
        var candidates = serviceCandidates
            .Concat(annotatedCandidates)
            .GroupBy(c => c.ImplementationTypeName, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToImmutableArray();

        var services = ResolveServices(candidates, platform, Report);

        DetectCaptiveDependencies(services, Report);

        // Dispatch is generated only for an assembly that references messaging; an
        // application that does not use commands and queries pays nothing for them.
        var messaging = moduleInfo.ReferencedAssemblies.Any(a => a == platform.MessagingAssembly);

        var requests = messaging
            ? ResolveRequests(candidates, platform)
            : new RequestSet([], []);

        var events = moduleInfo.ReferencedAssemblies.Any(a => a == platform.EventsAssembly);

        var subscriptions = events
            ? ResolveEvents(candidates, platform)
            : new EventSet([], []);

        var web = moduleInfo.ReferencedAssemblies.Any(a => a == platform.WebAssembly);

        var endpoints = web ? ResolveEndpoints(candidates, platform, Report) : [];

        var dependencies = moduleInfo.ModuleTypes
            .Where(m => m.Interfaces.Any(i => i == platform.ModuleInterface))
            .Select(m => m.TypeName)
            .Concat(DeclaredDependencies(candidates, platform))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        // Emitted whatever was reported. Withholding the file turned one diagnostic into
        // 'CS0246: Module does not exist' pointing nowhere, and a team that downgraded a
        // rule in .editorconfig lost the module entirely. The errors fail the build by
        // themselves; they do not need the file withheld to do it.
        context.AddSource("Module.g.cs", SourceText.From(
            Render(moduleInfo.AssemblyName, dependencies, services, requests, messaging,
                   subscriptions, events, endpoints, web, platform),
            Encoding.UTF8));
    }

    /// <summary>
    /// Ordering constraints stated with <c>[DependsOn]</c>.
    /// </summary>
    /// <remarks>
    /// The reference graph cannot express every ordering — a module that seeds data another
    /// reads at startup needs no reference to it. The attribute was the documented way to say
    /// so, and nothing read it; the consumer could not work around that either, because the
    /// generated partial already declares <c>Dependencies</c>.
    /// </remarks>
    private static IEnumerable<string> DeclaredDependencies(
        ImmutableArray<ServiceCandidate> candidates, ZeroNames platform)
        => candidates
            .SelectMany(c => c.Attributes)
            .Where(a => string.Equals(a.TypeName, platform.DependsOnAttribute, StringComparison.Ordinal))
            .SelectMany(a => a.ConstructorArguments)
            .Where(a => a.IsType && a.Value is { Length: > 0 })
            .Select(a => a.Value!);

    /// <summary>Events declared in this assembly, and the subscribers to each.</summary>
    private sealed record EventSet(List<string> Declared, List<EventSubscription> Subscriptions);

    /// <summary>One event and every subscriber to it in this assembly.</summary>
    private sealed record EventSubscription(string EventTypeName, List<string> HandlerTypeNames);

    /// <summary>
    /// Groups subscribers by the event they handle.
    /// </summary>
    /// <remarks>
    /// Unlike a request, an event has any number of subscribers — that is the whole point —
    /// so this groups rather than rejecting a second one. A type may subscribe to several
    /// events, and each subscription becomes its own row.
    /// </remarks>
    private static EventSet ResolveEvents(
        ImmutableArray<ServiceCandidate> candidates, ZeroNames platform)
    {
        var declared = new List<string>();
        var byEvent = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (!candidate.IsConcrete) continue;

            var handled = candidate.ClosedInterfaces
                .Where(i => i.OpenGenericName == platform.EventHandlerInterface && i.TypeArguments.Count == 1)
                .ToList();

            foreach (var subscription in handled)
            {
                if (!byEvent.TryGetValue(subscription.TypeArguments[0], out var handlers))
                    byEvent[subscription.TypeArguments[0]] = handlers = [];

                handlers.Add(candidate.ImplementationTypeName);
            }

            if (handled.Count > 0) continue;

            // An event declares itself so a host can be told about one nobody subscribes to.
            if (candidate.AllInterfaces.ToArray().Any(i => i == platform.EventInterface))
                declared.Add(candidate.ImplementationTypeName);
        }

        declared.Sort(StringComparer.Ordinal);

        var subscriptions = byEvent
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new EventSubscription(
                pair.Key, [.. pair.Value.OrderBy(h => h, StringComparer.Ordinal)]))
            .ToList();

        return new EventSet(declared, subscriptions);
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

            // The first positional argument, never 'the argument without an =': a pattern
            // may legally contain one, and [Get("/reports/{page=1}")] was then read as a
            // named argument and reported as an empty route.
            var pattern = route.Attribute.ConstructorArguments.Count > 0
                ? route.Attribute.ConstructorArguments[0].Value ?? string.Empty
                : string.Empty;

            if (pattern.Length == 0)
            {
                report(Diagnostics.EmptyRoutePattern, candidate.Location, [candidate.TypeName]);
                continue;
            }

            var policy = Named(route.Attribute, "Policy");
            var anonymous = string.Equals(
                Named(route.Attribute, "AllowAnonymous"), "True", StringComparison.OrdinalIgnoreCase);

            // Zero refuses an unauthenticated caller by default, so a forgotten endpoint
            // fails loudly. What stays silent is one that should have named a policy and
            // instead serves every logged-in caller.
            if (policy is null && !anonymous)
                report(Diagnostics.UndeclaredEndpointPolicy, candidate.Location, [candidate.TypeName]);

            endpoints.Add(new EndpointDescriptor(
                route.Method,
                pattern,
                Named(route.Attribute, "Name") ?? EndpointName(candidate.ImplementationTypeName),
                Named(route.Attribute, "Tag"),
                policy,
                anonymous,
                candidate.ImplementationTypeName,
                request.TypeArguments[0],
                candidate.Location));
        }

        endpoints.Sort((a, b) => string.CompareOrdinal(a.Pattern + a.Method, b.Pattern + b.Method));

        return endpoints;
    }

    /// <summary>The value of one named attribute argument, or null when it was not given.</summary>
    private static string? Named(AttributeUsage attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
            if (string.Equals(argument.Name, name, StringComparison.Ordinal)
                && argument.Argument.Value is { Length: > 0 } value)
                return value;

        return null;
    }

    /// <summary>
    /// A namespace-qualified endpoint name.
    /// </summary>
    /// <remarks>
    /// The simple type name collides as soon as two modules both have a <c>GetInvoice</c>, and
    /// ASP.NET does not notice at startup: it throws 'Duplicate endpoint name' on the first
    /// <c>LinkGenerator</c> or OpenAPI use, far from the two declarations that caused it.
    /// </remarks>
    private static string EndpointName(string implementationTypeName)
    {
        var name = implementationTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? implementationTypeName.Substring("global::".Length)
            : implementationTypeName;

        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
            builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_');

        return builder.ToString();
    }

    /// <summary><c>ServiceSelectorType</c>'s flags. The generator targets netstandard2.0 and
    /// never loads the framework it generates for, so the values are restated here.</summary>
    private const int SelectorSelf = 1;
    private const int SelectorInterfaces = 2 | 4 | 8;

    /// <summary><c>LifeStyle</c>'s values the generator has to tell apart, as written in metadata.</summary>
    private const string LifeStyleSingleton = "1";
    private const string LifeStyleScoped = "7";

    private static List<ServiceRegistrationDescriptor> ResolveServices(
        ImmutableArray<ServiceCandidate> candidates,
        ZeroNames platform,
        Action<DiagnosticDescriptor, LocationInfo?, object[]> report)
    {
        var lifetimeByInterface = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{platform.Descriptors}.ISingleton"] = "Singleton",
            [$"{platform.Descriptors}.IScoped"] = "Scoped",
            [$"{platform.Descriptors}.ITransient"] = "Transient"
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

            var annotated = AnnotatedLifetime(candidate, platform);

            if (lifetimes.Count == 0 && annotated is null) continue;
            if (candidate.AllInterfaces.Any(i => i == platform.Ignored)) continue;

            // The attribute exists for the type whose lifetime no abstraction can express, so
            // it settles a contradiction between markers rather than adding to it.
            if (lifetimes.Count > 1 && annotated is null)
                report(Diagnostics.MultipleLifetimes, candidate.Location,
                    [candidate.TypeName, string.Join(", ", lifetimes)]);

            var lifetime = annotated ?? lifetimes[0];

            // An abstract type is never constructed. Its marker declares the lifetime of the
            // classes deriving from it, and those are registered in its place -- silently,
            // because the shape is the one the documentation tells you to write.
            if (candidate.IsAbstract) continue;

            var annotations = candidate.Attributes
                .Where(a => a.TypeName.StartsWith(platform.ServiceTypesAttribute, StringComparison.Ordinal))
                .ToList();

            var declared = annotations
                .SelectMany(a => a.ConstructorArguments.Where(x => x.IsType).Select(x => x.Value!)
                    .Concat(a.TypeArguments))
                .Select(Global)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var key = Key(annotations);
            var selector = Selector(annotations);

            var registerSelf = selector is { } self && (self & SelectorSelf) != 0;
            var byConvention = selector is not { } stated || (stated & SelectorInterfaces) != 0;

            var serviceTypes = declared.Count > 0
                ? declared.Select(d => (TypeName: d, Extension: false)).ToList()
                : byConvention ? Resolve(candidate, platform, nonService) : [];

            if (serviceTypes.Count == 0 && !registerSelf)
            {
                // An open generic can only be registered under an interface it forwards its
                // own type parameters to. A marker it merely inherited is not this file's
                // mistake, so reporting it would send the reader to the wrong declaration.
                var blame = candidate.Arity == 0
                    ? Diagnostics.ServiceTypeNotResolved
                    : Diagnostics.RegistrationTargetInvalid;

                if (candidate.Arity == 0 || Declares(candidate, lifetimeByInterface, platform))
                    report(blame, candidate.Location, [candidate.TypeName]);

                if (candidate.Arity > 0) continue;
            }

            // A keyed registration competes with nothing: it is only ever reached through its
            // key, which is exactly what ZERO010's message tells the reader to write.
            if (key is null)
                foreach (var serviceType in serviceTypes)
                {
                    // A closed-generic extension point is resolved as IEnumerable<T>. Several
                    // validators for one request is the designed shape, not a collision.
                    if (serviceType.Extension) continue;

                    if (owner.TryGetValue(serviceType.TypeName, out var existing))
                        report(Diagnostics.DuplicateRegistration, candidate.Location,
                            [serviceType.TypeName, existing, candidate.ImplementationTypeName]);

                    owner[serviceType.TypeName] = candidate.ImplementationTypeName;
                }

            result.Add(new ServiceRegistrationDescriptor(
                candidate.Arity > 0 ? candidate.UnboundImplementationTypeName : candidate.ImplementationTypeName,
                new EquatableArray<string>([.. serviceTypes.Select(s => s.TypeName)]),
                lifetime, key, registerSelf, candidate.Arity > 0,
                candidate.ConstructorDependencies,
                candidate.Location));
        }

        return result;
    }

    /// <summary>Whether a lifetime marker is reachable from the type's own declaration.</summary>
    private static bool Declares(
        ServiceCandidate candidate, Dictionary<string, string> lifetimeByInterface, ZeroNames platform)
        => candidate.DeclaredInterfaces.Any(lifetimeByInterface.ContainsKey)
        || AnnotatedLifetime(candidate, platform) is not null;

    /// <summary>
    /// The lifetime an annotation declares, or null when the type carries none.
    /// </summary>
    /// <remarks>
    /// <c>[Singleton]</c>, <c>[Scoped]</c>, <c>[Transient]</c> and <c>[LifeStyle]</c> are the
    /// documented escape hatch for when the abstraction cannot express the lifetime. Nothing
    /// read them, so a type carrying one and no marker interface was silently not registered
    /// at all — the failure mode the whole generated-registration design exists to avoid.
    /// </remarks>
    private static string? AnnotatedLifetime(ServiceCandidate candidate, ZeroNames platform)
    {
        foreach (var attribute in candidate.Attributes)
        {
            if (string.Equals(attribute.TypeName, platform.LifeStyleAttribute, StringComparison.Ordinal))
            {
                var value = attribute.ConstructorArguments.Count > 0
                    ? attribute.ConstructorArguments[0].Value
                    : null;

                return value switch
                {
                    null => null,
                    LifeStyleSingleton => "Singleton",
                    LifeStyleScoped => "Scoped",
                    _ => "Transient"
                };
            }

            foreach (var (name, lifetime) in platform.LifetimeAttributes)
                if (string.Equals(attribute.TypeName, name, StringComparison.Ordinal))
                    return lifetime;
        }

        return null;
    }

    /// <summary>The service key, from either the positional or the named argument.</summary>
    private static string? Key(List<AttributeUsage> annotations)
    {
        foreach (var annotation in annotations)
        {
            foreach (var named in annotation.NamedArguments)
                if (string.Equals(named.Name, "Key", StringComparison.Ordinal) && !named.Argument.IsType)
                    return named.Argument.Expression;

            // [ServiceTypes("primary", typeof(IEmailSender))] -- the form both the ZERO010
            // message and its rule page tell you to write. Everything else positional is a type.
            foreach (var positional in annotation.ConstructorArguments)
                if (!positional.IsType)
                    return positional.Expression;
        }

        return null;
    }

    /// <summary>The stated <c>ServiceSelectorType</c> flags, or null when none was stated.</summary>
    private static int? Selector(List<AttributeUsage> annotations)
    {
        int? selector = null;

        foreach (var annotation in annotations)
            foreach (var named in annotation.NamedArguments)
                if (string.Equals(named.Name, "ServiceSelectorType", StringComparison.Ordinal)
                    && int.TryParse(named.Argument.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
                    selector = (selector ?? 0) | flags;

        return selector;
    }

    private static List<(string TypeName, bool Extension)> Resolve(
        ServiceCandidate candidate, ZeroNames platform, HashSet<string> nonService)
    {
        // Framework extension points are registered under the closed generic they implement.
        // The naming convention would pick the open definition -- IQueryHandler<TQuery,
        // TResponse> -- which nothing can be registered as, and a class deriving from a base
        // class rather than implementing an interface directly would match nothing at all.
        var closed = candidate.ClosedInterfaces
            .Where(i => platform.ClosedRegistrationInterfaces.Contains(i.OpenGenericName))
            .ToList();

        var direct = candidate.DirectInterfaces.Where(i => !nonService.Contains(i.OpenGenericName)).ToList();
        var inherited = candidate.InheritedInterfaces.Where(i => !nonService.Contains(i.OpenGenericName)).ToList();

        // An open generic has no closed service type: its type arguments are type parameters,
        // which do not exist at the registration site. It goes in unbound instead --
        // AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)) -- which is what
        // IPipelineBehavior's own documentation tells the reader to write.
        if (candidate.Arity > 0)
        {
            var source = closed.Count > 0 ? closed : direct.Count > 0 ? direct : inherited;

            return [.. source
                .Where(i => i.ForwardsTypeParameters)
                .Select(i => (i.UnboundName, closed.Count > 0))
                .Distinct()];
        }

        if (closed.Count > 0)
            return [.. closed.Select(i => (i.ClosedName, true)).Distinct()];

        // The base class's interfaces, when this type declares none of its own. 'CsvExportFormat
        // : ExportFormat', where the base implements IExportFormat, is the shape ZERO008 gives
        // as its fix; resolving it to nothing and reporting ZERO007 made that fix a dead end.
        var convention = direct.Count > 0 ? direct : inherited;

        if (convention.Count == 0) return [];

        var byName = SymbolCollector.DefaultInterface(candidate.TypeName, convention);

        return byName is not null ? [(byName.ClosedName, false)]
             : convention.Count == 1 ? [(convention[0].ClosedName, false)]
             : [];
    }

    private static void DetectCaptiveDependencies(
        List<ServiceRegistrationDescriptor> services,
        Action<DiagnosticDescriptor, LocationInfo?, object[]> report)
    {
        var lifetime = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var service in services)
            foreach (var serviceType in service.ServiceTypeNames)
                lifetime[serviceType] = service.Lifetime;

        foreach (var service in services.Where(s => s.Lifetime == "Singleton"))
            foreach (var dependency in service.ConstructorDependencies)
            {
                // The closed form first. Recording only the definition meant a singleton
                // taking IValidator<Foo> was compared against IValidator<> and never matched
                // the IValidator<Foo> registration; the unbound form is what an open generic
                // registration is keyed by.
                var registered =
                    lifetime.TryGetValue(dependency.TypeName, out var found) ? found
                    : dependency.UnboundTypeName is { } unbound
                        && lifetime.TryGetValue(unbound, out var open) ? open
                    : null;

                if (registered == "Scoped")
                    report(Diagnostics.CaptiveDependency, service.Location,
                        [service.ImplementationTypeName, dependency.TypeName, registered]);
            }
    }

    private static string Global(string name)
        => name.StartsWith("global::", StringComparison.Ordinal) ? name : $"global::{name}";

    private static string Render(
        string assemblyName,
        List<string> dependencies,
        List<ServiceRegistrationDescriptor> services,
        RequestSet requests,
        bool messaging,
        EventSet subscriptions,
        bool events,
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
        b.AppendLine($"    public string Name => {Literal(assemblyName)};");
        b.AppendLine();
        b.AppendLine("    /// <summary>Derived from the assembly reference graph and any [DependsOn].</summary>");
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
            b.AppendLine($"        RegisterRequests(global::{platform.MessagingModuleContextExtensions}.Requests(context));");

        if (events)
            b.AppendLine($"        RegisterEvents(global::{platform.EventsModuleContextExtensions}.Events(context));");

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
            b.AppendLine($"        // {service.Lifetime}: {Comment(service.ImplementationTypeName)}");

            foreach (var serviceType in service.ServiceTypeNames)
            {
                var method = service.Key is null ? $"Add{service.Lifetime}" : $"AddKeyed{service.Lifetime}";

                // An open generic can only be named through typeof, so the Type overloads are
                // the only ones that can express it.
                var args = (service.IsOpenGeneric, service.Key) switch
                {
                    (true, null) => $"services, typeof({serviceType}), typeof({service.ImplementationTypeName})",
                    (true, { } key) => $"services, typeof({serviceType}), {key}, typeof({service.ImplementationTypeName})",
                    (false, null) => "services",
                    (false, { } key) => $"services, {key}"
                };

                var generics = service.IsOpenGeneric ? string.Empty : $"<{serviceType}, {service.ImplementationTypeName}>";

                b.AppendLine($"        {di}.ServiceCollectionServiceExtensions.{method}{generics}({args});");
            }

            if (service.RegisterSelf || service.ServiceTypeNames.Count == 0)
                b.AppendLine(service.IsOpenGeneric
                    ? $"        {di}.ServiceCollectionServiceExtensions.Add{service.Lifetime}(services, typeof({service.ImplementationTypeName}));"
                    : $"        {di}.ServiceCollectionServiceExtensions.Add{service.Lifetime}<{service.ImplementationTypeName}>(services);");
        }

        b.AppendLine("    }");
        b.AppendLine();

        if (messaging) RenderRequests(b, requests, platform);

        if (events) RenderEvents(b, subscriptions, platform);

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
            b.AppendLine($"        // {Comment(handler.RequestTypeName)} -> {Comment(handler.HandlerTypeName)}");
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
            b.AppendLine($"        // {endpoint.Method} {Comment(endpoint.Pattern)}");
            b.AppendLine($"        builder.Add(new {web}.ZeroEndpointDescriptor(");
            b.AppendLine($"            {Literal(endpoint.Method)},");
            b.AppendLine($"            {Literal(endpoint.Pattern)},");
            b.AppendLine($"            {Literal(endpoint.Name)},");
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

    /// <summary>
    /// A C# string literal for <paramref name="value"/>, escaped.
    /// </summary>
    /// <remarks>
    /// Interpolating the raw value emitted the developer's own text as source. A route
    /// pattern of <c>@"/products/{sku:regex(^\d{3}$)}"</c> put a <c>\d</c> escape into
    /// Module.g.cs and failed the build with CS1009, in a file nobody wrote.
    /// </remarks>
    private static string Literal(string? value)
        => value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>Keeps an emitted comment on one line, whatever the value contained.</summary>
    private static string Comment(string value)
        => value.Replace("\r", " ").Replace("\n", " ");

    /// <summary>
    /// Turns an assembly name into a namespace.
    /// </summary>
    /// <remarks>
    /// Applied per segment, because a namespace segment may not start with a digit:
    /// <c>Company.2024.Api</c> used to emit a namespace that did not compile.
    /// </remarks>
    /// <summary>Emits the delivery rows for this assembly.</summary>
    /// <remarks>
    /// The event type is known here, so the emitted lambda calls a closed generic and
    /// delivery costs a dictionary read and a cast. Subscribers are listed by type so a
    /// caller can see which ones ran without their instances being kept alive.
    /// </remarks>
    private static void RenderEvents(StringBuilder b, EventSet events, ZeroNames platform)
    {
        var ns = $"global::{platform.Events}";

        b.AppendLine($"    private static void RegisterEvents({ns}.IEventRegistryBuilder builder)");
        b.AppendLine("    {");

        if (events.Declared.Count == 0 && events.Subscriptions.Count == 0)
            b.AppendLine("        // This assembly declares no event and subscribes to none.");

        foreach (var declared in events.Declared)
            b.AppendLine($"        builder.Declare(typeof({declared}));");

        if (events.Declared.Count > 0 && events.Subscriptions.Count > 0) b.AppendLine();

        foreach (var subscription in events.Subscriptions)
        {
            var handlers = string.Join(", ", subscription.HandlerTypeNames.Select(h => $"typeof({h})"));

            b.AppendLine($"        // {subscription.EventTypeName} -> {subscription.HandlerTypeNames.Count} subscriber(s)");
            b.AppendLine($"        builder.Add(new {ns}.EventEntry(");
            b.AppendLine($"            typeof({subscription.EventTypeName}),");
            b.AppendLine($"            [{handlers}],");
            b.AppendLine("            static (services, @event, cancellationToken) =>");
            b.AppendLine($"                {ns}.EventDispatch.RunAsync<{subscription.EventTypeName}>(");
            b.AppendLine($"                    ({subscription.EventTypeName})@event, services, cancellationToken)));");
            b.AppendLine();
        }

        b.AppendLine("    }");
        b.AppendLine();
    }

    private static string Sanitize(string name)
    {
        var segments = name.Split('.');

        for (var s = 0; s < segments.Length; s++)
        {
            var builder = new StringBuilder(segments[s].Length + 1);

            foreach (var c in segments[s])
                builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

            if (builder.Length == 0 || char.IsDigit(builder[0])) builder.Insert(0, '_');

            segments[s] = builder.ToString();
        }

        return string.Join(".", segments);
    }
}
