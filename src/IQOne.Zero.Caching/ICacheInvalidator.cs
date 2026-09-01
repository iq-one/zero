using IQOne.Zero.DependencyInjection.Descriptors;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Caching;

/// <summary>
/// Drops the cached answers a command has just made wrong.
/// </summary>
/// <remarks>
/// <para>
/// The other half of <see cref="ICacheable"/>. A query states the key its answer is stored
/// under; the command that changes the same data drops that key, or the branch of keys below
/// it. Nothing happens automatically: the cache does not know which command touches which
/// query, and a guess would be wrong quietly rather than loudly.
/// </para>
/// <para>
/// Both methods take the key exactly as the query wrote it. <see cref="CachingOptions.KeyPrefix"/>
/// is applied here, so the two sides agree on the stored key without either of them knowing
/// what the prefix is.
/// </para>
/// </remarks>
public interface ICacheInvalidator : ISingleton
{
    /// <summary>Drops one answer, by the key the query declared.</summary>
    /// <param name="cacheKey">The query's <see cref="ICacheable.CacheKey"/>.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes once the entry is gone.</returns>
    ValueTask InvalidateAsync(string cacheKey, CancellationToken cancellationToken);

    /// <summary>
    /// Drops every answer whose declared key starts with <paramref name="keyPrefix"/>.
    /// </summary>
    /// <remarks>
    /// This is what keys written as a path are for. A command that changes one invoice drops
    /// <c>invoice:42</c>; one that reprices all of them drops <c>invoice:</c> and takes every
    /// view of every invoice with it.
    /// </remarks>
    /// <param name="keyPrefix">The start of the keys to drop, as the queries declared them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A task that completes once the entries are gone.</returns>
    ValueTask InvalidateByPrefixAsync(string keyPrefix, CancellationToken cancellationToken);
}

/// <summary>Applies the configured prefix and hands the key to the store.</summary>
/// <param name="cache">Where the entries are.</param>
/// <param name="options">Supplies the prefix the behaviour wrote the keys with.</param>
internal sealed class CacheInvalidator(ICache cache, IOptions<CachingOptions> options) : ICacheInvalidator
{
    /// <inheritdoc />
    public ValueTask InvalidateAsync(string cacheKey, CancellationToken cancellationToken)
        => cache.RemoveAsync(options.Value.KeyPrefix + cacheKey, cancellationToken);

    /// <inheritdoc />
    public ValueTask InvalidateByPrefixAsync(string keyPrefix, CancellationToken cancellationToken)
        => cache.RemoveByPrefixAsync(options.Value.KeyPrefix + keyPrefix, cancellationToken);
}
