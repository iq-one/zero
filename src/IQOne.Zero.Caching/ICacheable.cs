namespace IQOne.Zero.Caching;

/// <summary>
/// A query that may be served from the cache, and says under what key and for how long.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is cached unless it says so here. The alternative — deriving a key by serialising
/// the request — builds the key out of a shape nobody declared: adding a field silently
/// changes every key, and two requests that happen to serialise the same silently share an
/// answer. Neither has a symptom anyone would notice, which is why the key is written by
/// hand instead.
/// </para>
/// <para>
/// Only a query may implement this. A command changes something, so serving it from a cache
/// would skip the change: <see cref="CachingBehavior{TRequest,TResponse}"/> throws, and
/// ZERO210 reports it at compile time.
/// </para>
/// </remarks>
public interface ICacheable
{
    /// <summary>
    /// The key the answer is stored under. Must carry everything the answer depends on.
    /// </summary>
    /// <remarks>
    /// Write it as a path — <c>invoice:42</c>, <c>invoice:42:lines</c> — so a command can drop
    /// a whole branch with <see cref="ICacheInvalidator.InvalidateByPrefixAsync"/>. A constant
    /// key on a query that takes parameters is reported as ZERO211: it hands one caller's
    /// answer to another.
    /// </remarks>
    string CacheKey { get; }

    /// <summary>
    /// How long the answer stays usable, or <see langword="null"/> for
    /// <see cref="CachingOptions.DefaultLifetime"/>.
    /// </summary>
    /// <remarks>
    /// It lives on the query because only the query knows how stale its answer may be: a list
    /// of currencies tolerates an hour, an account balance does not tolerate a minute.
    /// </remarks>
    TimeSpan? Lifetime => null;
}
