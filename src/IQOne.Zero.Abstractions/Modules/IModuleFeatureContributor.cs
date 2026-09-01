using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Modules;

/// <summary>Receives the capabilities offered to modules during configure-services.</summary>
public interface IModuleFeatureCollection
{
    /// <summary>Offers <paramref name="feature"/> to modules under the type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type modules will ask for.</typeparam>
    /// <param name="feature">The capability.</param>
    void Set<T>(T feature) where T : notnull;
}

/// <summary>
/// Contributes a capability to the module configure-services phase on behalf of a layer
/// the module system does not reference.
/// </summary>
/// <remarks>
/// This is how a higher layer reaches modules without the core reaching back. Dispatch, for
/// example, is contributed by <c>IQOne.Zero.Messaging.Dispatch</c>: the core creates no
/// registry and names no dispatch type. Register contributors as instances so they can be
/// read before the service provider exists.
/// </remarks>
public interface IModuleFeatureContributor
{
    /// <summary>Offers capabilities to modules. Runs before the first module is configured.</summary>
    /// <param name="features">The collection to add capabilities to.</param>
    void Contribute(IModuleFeatureCollection features);

    /// <summary>
    /// Seals the contributed capability and registers whatever consumes it. Runs after every
    /// module has been configured.
    /// </summary>
    /// <param name="services">The registrations gathered so far.</param>
    void Complete(IServiceCollection services) { }
}
