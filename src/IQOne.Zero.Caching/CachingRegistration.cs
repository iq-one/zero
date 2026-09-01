using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Caching;

/// <summary>Adds read-through caching for queries to an application.</summary>
public static class CachingRegistration
{
    /// <summary>
    /// Serves any query implementing <see cref="ICacheable"/> from the cache, and keeps
    /// answers in memory unless another <see cref="ICache"/> is registered first.
    /// </summary>
    /// <remarks>
    /// One call is enough. Nothing is cached until a query says it may be, so adding this to
    /// an application changes no behaviour on its own — which is what makes it safe to add
    /// before anyone has decided what to cache.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts the lifetime, the key prefix and the on/off switch.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroCaching(
        this IServiceCollection services, Action<CachingOptions>? configure = null)
    {
        var options = services
            .AddOptions<CachingOptions>()
            .Validate(
                o => o.DefaultLifetime > TimeSpan.Zero,
                $"{nameof(CachingOptions)}.{nameof(CachingOptions.DefaultLifetime)} must be greater than zero. " +
                "To stop caching altogether, set Enabled to false; a query that should never be cached simply " +
                "does not implement ICacheable.")
            .Validate(
                o => o.KeyPrefix is not null,
                $"{nameof(CachingOptions)}.{nameof(CachingOptions.KeyPrefix)} must not be null. Use an empty " +
                "string when the store belongs to this application alone.");

        if (configure is not null) options.Configure(configure);

        options.ValidateOnStart();

        services.AddMemoryCache();

        // TryAdd, so an application that registers a distributed cache keeps it. The
        // in-memory one exists to make the package work out of the box, not to win.
        services.TryAddSingleton<ICache, InMemoryCache>();
        services.TryAddSingleton<ICacheInvalidator, CacheInvalidator>();

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>)));

        return services;
    }
}
