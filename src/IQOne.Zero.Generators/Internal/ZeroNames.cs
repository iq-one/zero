namespace IQOne.Zero.Generators.Internal;

/// <summary>
/// Every framework type name the generator looks for or emits.
/// </summary>
/// <remarks>
/// These were once derived from an MSBuild property so the generator could be pointed at a
/// differently named platform. That indirection is gone: the framework has a name of its own,
/// and a knob nobody should turn is a knob that will eventually be turned by accident.
/// </remarks>
internal sealed record ZeroNames
{
    /// <summary>The single instance; the names are fixed.</summary>
    public static readonly ZeroNames Default = new();

    private ZeroNames() { }

    /// <summary>Framework namespace root. Types under it are never treated as service types.</summary>
    public string Root => "IQOne.Zero";

    /// <summary>Assembly whose presence marks a project as a module.</summary>
    public string CoreAssembly => "IQOne.Zero.Core";

    public string Modules => $"{Root}.Modules";
    public string Descriptors => $"{Root}.DependencyInjection.Descriptors";
    public string Services => $"{Root}.DependencyInjection.Services";
    public string Annotations => $"{Root}.DependencyInjection.Annotations";

    public string ModuleInterface => $"{Modules}.IModule";
    public string Ignored => $"{Services}.IIgnoredService";
    public string Required => $"{Services}.IRequiredService";
    public string ServiceTypesAttribute => $"{Annotations}.ServiceTypesAttribute";

    /// <summary>States an ordering the assembly reference graph does not express.</summary>
    public string DependsOnAttribute => $"{Modules}.DependsOnAttribute";

    /// <summary>Base of the lifetime annotations; the derived ones each fix a value.</summary>
    public string LifeStyleAttribute => $"{Annotations}.LifeStyleAttribute";

    /// <summary>Says a lifetime has not been chosen, which is not a lifetime.</summary>

    /// <summary>
    /// Lifetime annotations, mapped to the container lifetime each declares.
    /// </summary>
    /// <remarks>
    /// These are the documented escape hatch for a type whose lifetime the abstraction cannot
    /// express. The values follow <c>LifeStyleAttribute.ToServiceLifetime</c>: the container
    /// has three lifetimes and Zero's vocabulary is wider, so a value without an equivalent
    /// registers as transient.
    ///
    /// <c>Undefined</c> is the exception and is absent here on purpose. It says a lifetime
    /// has not been chosen yet, and putting a service in the container on the strength of an
    /// attribute that declined to name one is the surprise this whole design exists to avoid.
    /// </remarks>
    public (string Attribute, string Lifetime)[] LifetimeAttributes =>
    [
        ($"{Annotations}.SingletonAttribute", "Singleton"),
        ($"{Annotations}.ScopedAttribute", "Scoped"),
        ($"{Annotations}.TransientAttribute", "Transient")
    ];

    /// <summary>Assembly whose presence turns on event delivery generation.</summary>
    public string EventsAssembly => $"{Root}.Events";

    public string Events => $"{Root}.Events";
    public string EventHandlerInterface => $"{Events}.IEventHandler";
    public string EventInterface => $"{Events}.IEvent";
    public string EventRegistryBuilder => $"{Events}.IEventRegistryBuilder";
    public string EventEntry => $"{Events}.EventEntry";
    public string EventDispatch => $"{Events}.EventDispatch";
    public string EventsModuleContextExtensions => $"{Events}.EventsModuleContextExtensions";

    /// <summary>Assembly whose presence turns on request dispatch generation.</summary>
    public string MessagingAssembly => $"{Root}.Messaging";

    public string Messaging => $"{Root}.Messaging";
    public string RequestInterface => $"{Messaging}.IRequest";
    public string RequestHandlerInterface => $"{Messaging}.IRequestHandler";
    public string RequestRegistryBuilder => $"{Messaging}.IRequestRegistryBuilder";
    public string RequestEntry => $"{Messaging}.RequestEntry";
    public string RequestPipeline => $"{Messaging}.RequestPipeline";
    public string MessagingModuleContextExtensions => $"{Messaging}.MessagingModuleContextExtensions";

    /// <summary>
    /// Framework interfaces whose implementations are registered under the CLOSED generic
    /// they implement, rather than by the naming convention.
    /// </summary>
    /// <remarks>
    /// These are resolved by type: the pipeline asks for <c>IRequestHandler&lt;X, Y&gt;</c>,
    /// the validation behaviour for <c>IEnumerable&lt;IValidator&lt;X&gt;&gt;</c>. The
    /// convention would pick the open definition, which nothing can be registered as, and a
    /// class deriving from a base rather than implementing directly would get nothing at all.
    ///
    /// A capability that resolves its extension point by closed generic adds its interface
    /// here; nothing else in the generator changes.
    /// </remarks>
    public string[] ClosedRegistrationInterfaces =>
    [
        RequestHandlerInterface,
        EventHandlerInterface,
        $"{Root}.Validation.IValidator",
        $"{Root}.Persistence.Conventions.IModelConvention",
        $"{Root}.Persistence.Conventions.ISaveChangesConvention",

        // The third convention belongs with the other two. It was missing, so a filter
        // convention was the one kind of convention an application had to register by hand
        // — and the omission showed up as a query with no tenant filter, not as an error.
        $"{Root}.Persistence.Conventions.IEntityFilterConvention",
        $"{Messaging}.IPipelineBehavior",

        // One entry covers all three arities: authorization deliberately gave the marker and
        // both generic forms the same name, and the open name is recorded without arity.
        $"{Root}.Authorization.IRequirementHandler"
    ];

    /// <summary>Assembly whose presence turns on endpoint generation.</summary>
    public string WebAssembly => $"{Root}.Web";

    public string Web => $"{Root}.Web";
    public string RouteAttribute => $"{Web}.RouteAttribute";
    public string EndpointRegistryBuilder => $"{Web}.IEndpointRegistryBuilder";
    public string EndpointDescriptor => $"{Web}.ZeroEndpointDescriptor";
    public string ZeroEndpoint => $"{Web}.ZeroEndpoint";
    public string WebModuleExtensions => $"{Web}.WebModuleContextExtensions";

    /// <summary>Route attributes, mapped to the HTTP method each declares.</summary>
    public static readonly (string Attribute, string Method)[] RouteAttributes =
    [
        ("IQOne.Zero.Web.GetAttribute", "GET"),
        ("IQOne.Zero.Web.PostAttribute", "POST"),
        ("IQOne.Zero.Web.PutAttribute", "PUT"),
        ("IQOne.Zero.Web.PatchAttribute", "PATCH"),
        ("IQOne.Zero.Web.DeleteAttribute", "DELETE")
    ];
}
