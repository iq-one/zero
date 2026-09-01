using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.Fundamentals;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.App.Steps;

/// <summary>
/// A unit of application startup work. For work scoped to one module see
/// <see cref="IQOne.Zero.Modules.IModuleStep"/>.
/// </summary>
/// <remarks>
/// <para>
/// The phases run in this order: configure-services, then the provider is built, then
/// initialize, then pre-run. Shutdown runs post-run, in reverse.
/// </para>
/// <para>
/// Steps run sequentially, not concurrently: <see cref="IServiceCollection"/> is not
/// thread-safe, so running configure-services steps in parallel corrupts registrations
/// in ways that surface much later as a missing or duplicated service.
/// </para>
/// </remarks>
public interface IApplicationStep : IStep
{
    /// <summary>Ascending order; lower values run first. Steps with equal order are unordered.</summary>
    /// <remarks>Shutdown reverses this, so a step tears down before whatever it ran after.</remarks>
    int Order => 0;
}

/// <summary>Contributes registrations before the service provider is built.</summary>
public interface IApplicationConfigureServicesStep : IApplicationStep, ISingletonInstance
{
    /// <summary>Adds this step's registrations to <paramref name="services"/>.</summary>
    Task OnConfigureServicesAsync(IServiceCollection services, CancellationToken cancellationToken);
}

/// <summary>Runs once the service provider exists and services can be resolved.</summary>
public interface IApplicationInitializeStep : IApplicationStep, ISingletonInstance
{
    /// <summary>Performs initialization against the built <paramref name="application"/>.</summary>
    Task OnInitializeAsync(IApplication application, CancellationToken cancellationToken);
}

/// <summary>Runs immediately before the application begins accepting work.</summary>
public interface IApplicationPreRunStep : IApplicationStep, ISingletonInstance
{
    /// <summary>Performs the last setup before the run phase.</summary>
    Task OnPreRunAsync(IApplication application, CancellationToken cancellationToken);
}

/// <summary>Runs during shutdown, after the run phase has completed.</summary>
public interface IApplicationPostRunStep : IApplicationStep, ISingletonInstance
{
    /// <summary>Performs shutdown work for this step.</summary>
    Task OnPostRunAsync(IApplication application, CancellationToken cancellationToken);
}
