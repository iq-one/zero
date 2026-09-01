using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Modules;

/// <summary>
/// Context for the configure-services phase, before an <see cref="IServiceProvider"/> exists.
/// </summary>
/// <remarks>
/// Capabilities contributed by higher layers are retrieved through <see cref="Feature{T}"/>
/// rather than exposed as properties, so this abstraction does not have to reference the
/// layers that provide them. Without the indirection, adding a web capability here would
/// make every module depend on the web layer.
/// </remarks>
public interface IModuleServiceContext
{
    /// <summary>Registrations gathered so far.</summary>
    IServiceCollection Services { get; }

    /// <summary>Retrieves a capability contributed by the host.</summary>
    /// <typeparam name="T">The capability's type.</typeparam>
    /// <returns>The capability.</returns>
    /// <exception cref="InvalidOperationException">The feature is not registered.</exception>
    T Feature<T>() where T : notnull;

    /// <summary>Retrieves a capability if the host contributed one.</summary>
    /// <typeparam name="T">The capability's type.</typeparam>
    /// <param name="feature">The capability, when present.</param>
    /// <returns><see langword="true"/> when the capability is available.</returns>
    bool TryGetFeature<T>(out T feature) where T : notnull;
}

/// <summary>Context for phases that run after the service provider is built.</summary>
public interface IModuleContext
{
    /// <summary>The built provider.</summary>
    IServiceProvider Services { get; }
}
