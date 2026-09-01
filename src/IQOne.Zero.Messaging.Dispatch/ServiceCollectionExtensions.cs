using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Messaging.Dispatch;

/// <summary>Adds dispatch to a Zero application.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Lets modules register their service methods into a single dispatch table, which is
    /// frozen once every module has been configured.
    /// </summary>
    /// <remarks>Call this before <c>AddModules</c>.</remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddServiceDispatch(this IServiceCollection services)
    {
        services.AddSingleton<IModuleFeatureContributor>(new DispatchFeatureContributor());
        return services;
    }

    /// <summary>
    /// Builds a dispatch table from an explicit set of entries, bypassing the module phase.
    /// </summary>
    /// <remarks>Intended for tests and for hosts that assemble their table by hand.</remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adds the entries.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddServiceDispatch(
        this IServiceCollection services, Action<IServiceRegistryBuilder> configure)
    {
        var registry = new ServiceRegistry();
        configure(registry);
        registry.Freeze();

        services.AddSingleton(registry);
        services.AddScoped<IServiceDispatcher, ServiceDispatcher>();

        return services;
    }
}
