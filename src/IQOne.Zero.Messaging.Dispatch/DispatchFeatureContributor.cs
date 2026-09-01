using IQOne.Zero.Messaging.Dispatch;
using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Messaging.Dispatch;

/// <summary>
/// Offers the dispatch registry to modules while they configure, then seals it.
/// </summary>
/// <remarks>
/// The module system does not reference dispatch: it only runs the contributors it finds.
/// Adding dispatch to an application is therefore a registration, not a change to the core.
/// </remarks>
public sealed class DispatchFeatureContributor : IModuleFeatureContributor
{
    private readonly ServiceRegistry _registry = new();

    /// <inheritdoc />
    public void Contribute(IModuleFeatureCollection features)
        => features.Set<IServiceRegistryBuilder>(_registry);

    /// <inheritdoc />
    public void Complete(IServiceCollection services)
    {
        _registry.Freeze();

        services.AddSingleton(_registry);
        services.AddScoped<IServiceDispatcher, ServiceDispatcher>();
    }
}
