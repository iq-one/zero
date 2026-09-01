namespace IQOne.Zero.Modules;

/// <summary>Marker for module lifecycle participation.</summary>
public interface IModuleStep;

/// <summary>Registers services and dispatch entries. Runs before the service provider is built.</summary>
public interface IModuleConfigureServicesStep : IModuleStep
{
    /// <summary>Adds this module's registrations.</summary>
    ValueTask OnConfigureServicesAsync(IModuleServiceContext context, CancellationToken cancellationToken);
}

/// <summary>Runs once resolved services are available.</summary>
public interface IModuleInitializeStep : IModuleStep
{
    /// <summary>Performs initialization that needs resolved services.</summary>
    ValueTask OnInitializeAsync(IModuleContext context, CancellationToken cancellationToken);
}

/// <summary>Runs immediately before the application starts accepting requests.</summary>
public interface IModulePreRunStep : IModuleStep
{
    /// <summary>Performs the last setup before requests arrive.</summary>
    ValueTask OnPreRunAsync(IModuleContext context, CancellationToken cancellationToken);
}

/// <summary>Runs during shutdown, in reverse dependency order.</summary>
public interface IModulePostRunStep : IModuleStep
{
    /// <summary>Releases what this module set up.</summary>
    ValueTask OnPostRunAsync(IModuleContext context, CancellationToken cancellationToken);
}
