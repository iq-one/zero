using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Modules;

internal sealed class ModuleServiceContext(IServiceCollection services)
    : IModuleServiceContext, IModuleFeatureCollection
{
    private readonly Dictionary<Type, object> _features = [];

    public IServiceCollection Services { get; } = services;

    public void Set<T>(T feature) where T : notnull => _features[typeof(T)] = feature;

    public T Feature<T>() where T : notnull
        => TryGetFeature<T>(out var feature)
            ? feature
            : throw new InvalidOperationException(
                $"No '{typeof(T).Name}' capability is available to modules. " +
                "The layer that contributes it has probably not been added to the application.");

    public bool TryGetFeature<T>(out T feature) where T : notnull
    {
        if (_features.TryGetValue(typeof(T), out var found) && found is T typed)
        {
            feature = typed;
            return true;
        }

        feature = default!;
        return false;
    }
}

internal sealed class ModuleContext(IServiceProvider services) : IModuleContext
{
    public IServiceProvider Services { get; } = services;
}
