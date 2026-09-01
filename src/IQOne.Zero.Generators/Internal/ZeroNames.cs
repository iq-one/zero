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

    /// <summary>Assembly whose presence turns on request dispatch generation.</summary>
    public string MessagingAssembly => $"{Root}.Messaging";

    public string Messaging => $"{Root}.Messaging";
    public string RequestInterface => $"{Messaging}.IRequest";
    public string RequestHandlerInterface => $"{Messaging}.IRequestHandler";
    public string RequestRegistryBuilder => $"{Messaging}.IRequestRegistryBuilder";
    public string RequestEntry => $"{Messaging}.RequestEntry";
    public string RequestPipeline => $"{Messaging}.RequestPipeline";
    public string ModuleServiceContextExtensions => $"{Messaging}.ModuleServiceContextExtensions";

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
        $"{Root}.Validation.IValidator",
        $"{Messaging}.IPipelineBehavior"
    ];

    /// <summary>Assembly whose presence turns on endpoint generation.</summary>
    public string WebAssembly => $"{Root}.Web";

    public string Web => $"{Root}.Web";
    public string RouteAttribute => $"{Web}.RouteAttribute";
    public string EndpointRegistryBuilder => $"{Web}.IEndpointRegistryBuilder";
    public string EndpointDescriptor => $"{Web}.ZeroEndpointDescriptor";
    public string ZeroEndpoint => $"{Web}.ZeroEndpoint";
    public string WebModuleExtensions => $"{Web}.ModuleServiceContextExtensions";

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
