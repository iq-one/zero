using System.Diagnostics.CodeAnalysis;
using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Caching;

/// <summary>
/// Somewhere to keep an answer under a key for a while.
/// </summary>
/// <remarks>
/// <para>
/// The store, not the policy. What may be cached, under what key and for how long is decided
/// by the query through <see cref="ICacheable"/> and applied by
/// <see cref="CachingBehavior{TRequest,TResponse}"/>; everything here does is put a value
/// somewhere and take it out again.
/// </para>
/// <para>
/// Keys arrive fully formed and are stored exactly as given. A store that rewrote them would
/// make <see cref="RemoveByPrefixAsync"/> unanswerable: the caller could no longer tell which
/// prefix names the entries it wants gone.
/// </para>
/// <para>
/// An in-process implementation is registered by <c>AddZeroCaching()</c>. To share a cache
/// between instances, register your own before that call — nothing else in the package
/// changes.
/// </para>
/// </remarks>
public interface ICache : ISingleton
{
    /// <summary>Reads what is stored under a key.</summary>
    /// <typeparam name="TValue">The stored value's type.</typeparam>
    /// <param name="key">The key to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value, or a miss when the key is absent or holds another type.</returns>
    ValueTask<Cached<TValue>> GetAsync<TValue>(string key, CancellationToken cancellationToken);

    /// <summary>Stores a value under a key, replacing whatever was there.</summary>
    /// <typeparam name="TValue">The stored value's type.</typeparam>
    /// <param name="key">The key to store it under.</param>
    /// <param name="value">What to store. May be null.</param>
    /// <param name="lifetime">How long it stays readable. Must be greater than zero.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the value is stored.</returns>
    ValueTask SetAsync<TValue>(string key, TValue value, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>Drops one key. Absent keys are not an error.</summary>
    /// <param name="key">The key to drop.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the key is gone.</returns>
    ValueTask RemoveAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Drops every key that starts with <paramref name="keyPrefix"/>.
    /// </summary>
    /// <remarks>
    /// This is what a command uses after it changes data, so it is part of the abstraction
    /// rather than a convenience on one implementation. A store that cannot enumerate its own
    /// keys has to keep an index to answer it, and that is a decision worth forcing rather
    /// than discovering later.
    /// </remarks>
    /// <param name="keyPrefix">The start of the keys to drop. Empty drops everything.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the keys are gone.</returns>
    ValueTask RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken);
}

/// <summary>
/// What a cache had under a key: a value, or nothing.
/// </summary>
/// <remarks>
/// A miss and a stored <see langword="null"/> are different answers, and a bare
/// <c>TValue?</c> cannot tell them apart. Collapsing the two would make a query that
/// legitimately answers null re-run its handler every time, forever, with nothing to show
/// that caching had stopped working.
/// </remarks>
/// <typeparam name="TValue">The stored value's type.</typeparam>
public readonly struct Cached<TValue>
{
    private readonly TValue? _value;

    private Cached(TValue? value)
    {
        Found = true;
        _value = value;
    }

    /// <summary>Whether the key was in the cache.</summary>
    public bool Found { get; }

    /// <summary>The key was not in the cache.</summary>
    public static Cached<TValue> Miss => default;

    /// <summary>The key was in the cache, holding this value.</summary>
    /// <param name="value">What was stored.</param>
    /// <returns>The hit.</returns>
    public static Cached<TValue> Hit(TValue value) => new(value);

    /// <summary>Reads the value only when the key was in the cache.</summary>
    /// <param name="value">What was stored, when there was a hit.</param>
    /// <returns><see langword="true"/> when the key was in the cache.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return Found;
    }
}
