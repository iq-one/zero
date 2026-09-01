using IQOne.Zero.Messaging;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Caching;

/// <summary>
/// Serves a query from the cache when its answer is still there, and stores the answer when
/// it is not.
/// </summary>
/// <remarks>
/// <para>
/// Placed at <see cref="BehaviorOrder.Caching"/>: inside validation and authorization,
/// because a request that will be rejected must be rejected whether or not an answer happens
/// to be stored; outside the transaction, because a cache hit should not open one.
/// </para>
/// <para>
/// Only a request implementing <see cref="ICacheable"/> is touched at all. Everything else
/// passes straight through, which is what makes caching something a query opts into rather
/// than something an application discovers it has.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request wrapped.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="cache">Where answers are kept.</param>
/// <param name="options">The lifetime, prefix and on/off switch.</param>
public sealed class CachingBehavior<TRequest, TResponse>(ICache cache, IOptions<CachingOptions> options)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public int Order => BehaviorOrder.Caching;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The request implements <see cref="ICacheable"/> but is not a query, or declares a key
    /// or lifetime that cannot be used.
    /// </exception>
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable) return await next().ConfigureAwait(false);

        // Checked before the switch below, so turning caching off in a test cannot hide a
        // mistake that would still be a mistake in production.
        Assert(cacheable);

        var settings = options.Value;

        if (!settings.Enabled) return await next().ConfigureAwait(false);

        var key = settings.KeyPrefix + cacheable.CacheKey;

        var cached = await cache.GetAsync<TResponse>(key, cancellationToken).ConfigureAwait(false);

        if (cached.TryGetValue(out var stored)) return Result<TResponse>.Success(stored);

        var result = await next().ConfigureAwait(false);

        // Successes only. A failure is usually about the moment rather than the question — a
        // dependency that timed out, a row locked by someone else — and storing it would keep
        // answering with it long after the cause has gone.
        if (result.TryGetValue(out var produced))
        {
            await cache
                .SetAsync(key, produced, cacheable.Lifetime ?? settings.DefaultLifetime, cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Refuses a request that cannot be cached correctly.</summary>
    /// <remarks>
    /// Each of these is a bug in the code that declared the request, not a condition the
    /// caller can do anything about, so none of them is an <see cref="Error"/>. ZERO210 and
    /// ZERO211 catch the same mistakes at compile time; this is what is left for a request
    /// the compiler never saw.
    /// </remarks>
    /// <param name="cacheable">What the request said about itself.</param>
    private static void Assert(ICacheable cacheable)
    {
        if (cacheable is not IQuery<TResponse>)
            throw new InvalidOperationException(
                $"'{typeof(TRequest).FullName}' implements ICacheable but is not an IQuery<{typeof(TResponse).Name}>. " +
                "Only a query may be cached: a command changes something, and serving it from a cache would " +
                "skip the change. Make it a query, or remove ICacheable. See ZERO210.");

        if (string.IsNullOrWhiteSpace(cacheable.CacheKey))
            throw new InvalidOperationException(
                $"'{typeof(TRequest).FullName}' declares an empty CacheKey, so every query with an empty key " +
                "would share one answer. Give it a key that carries everything the answer depends on.");

        if (cacheable.Lifetime is { } lifetime && lifetime <= TimeSpan.Zero)
            throw new InvalidOperationException(
                $"'{typeof(TRequest).FullName}' declares a lifetime of {lifetime}, which stores an answer that " +
                "has already expired. Leave Lifetime null for the configured default, or remove ICacheable.");
    }
}
