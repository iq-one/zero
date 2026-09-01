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
}
