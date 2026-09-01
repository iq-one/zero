using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace IQOne.Zero.Caching;

/// <summary>
/// Keeps answers in this process, over <see cref="IMemoryCache"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered by default so the package works with one Add call and no server to run. It is
/// a per-process cache: two instances of the application do not share it, and neither sees
/// the other's invalidation. When that starts to matter, register a distributed
/// <see cref="ICache"/> instead and nothing else changes.
/// </para>
/// <para>
/// <see cref="IMemoryCache"/> cannot be asked which keys it holds, so the keys written here
/// are tracked beside it. That index is the whole reason
/// <see cref="RemoveByPrefixAsync"/> can be answered at all, and it is kept honest by an
/// eviction callback rather than by scanning.
/// </para>
/// </remarks>
internal sealed class InMemoryCache : ICache
{
    private readonly IMemoryCache _entries;
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);
    private readonly PostEvictionDelegate _prune;

    /// <summary>Wraps a memory cache.</summary>
    /// <param name="entries">Where the values are kept. Shared with the application.</param>
    public InMemoryCache(IMemoryCache entries)
    {
        _entries = entries;

        // Built once rather than per entry, and it prunes only when the key is genuinely
        // gone: replacing an entry evicts the old value while the new one is live, and
        // dropping the key there would hide a live entry from every prefix sweep after it.
        _prune = (key, _, _, _) =>
        {
            if (key is string text && !_entries.TryGetValue(text, out _)) _keys.TryRemove(text, out _);
        };
    }

    /// <inheritdoc />
    public ValueTask<Cached<TValue>> GetAsync<TValue>(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // What is stored is the wrapper, not the bare value: an answer that is legitimately
        // null then comes back as a hit rather than a miss that re-runs the handler forever,
        // and an entry written under some other type is a miss rather than a cast that throws.
        return new ValueTask<Cached<TValue>>(
            _entries.TryGetValue(key, out var stored) && stored is Cached<TValue> hit
                ? hit
                : Cached<TValue>.Miss);
    }

    /// <inheritdoc />
    public ValueTask SetAsync<TValue>(
        string key, TValue value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime };
        entry.RegisterPostEvictionCallback(_prune);

        // Indexed first. An entry the index does not know about survives every invalidation
        // until it expires; an index entry with nothing behind it costs one wasted lookup.
        _keys[key] = 0;
        _entries.Set(key, Cached<TValue>.Hit(value), entry);

        return default;
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _entries.Remove(key);
        _keys.TryRemove(key, out _);

        return default;
    }

    /// <inheritdoc />
    public ValueTask RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var key in _keys.Keys)
        {
            if (!key.StartsWith(keyPrefix, StringComparison.Ordinal)) continue;

            _entries.Remove(key);
            _keys.TryRemove(key, out _);
        }

        return default;
    }
}
