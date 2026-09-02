using System.Collections.Concurrent;
using IQOne.Zero.DependencyInjection.Descriptors;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Resilience;

/// <summary>
/// Decides whether retrying a request type is still doing any good.
/// </summary>
/// <remarks>
/// <para>
/// This is a brake on the retrier, not a circuit breaker on the dependency, and the
/// difference is the reason the package does not ship one of those. A breaker's unit is the
/// resource — a database, a named <c>HttpClient</c> — and that is where the platform already
/// puts one. A pipeline behaviour's unit is the request type, which is the wrong unit for a
/// breaker in both directions: three request types sharing one database do not protect it by
/// breaking one of them, and breaking one of them refuses callers whose dependency is fine.
/// A breaker keyed by the wrong thing is worse than none, because it looks like protection.
/// </para>
/// <para>
/// So this brake can only ever remove retries, never requests. When it engages, a request is
/// handled exactly once and the caller gets whatever the handler said — the same outcome
/// they would have had if the package were not installed. That is the strongest safety
/// property available here: switching the brake on cannot make availability worse than
/// switching the package off.
/// </para>
/// <para>
/// The default keys on the request type, counts requests that failed every attempt they were
/// given, and pauses for <see cref="ResilienceOptions.RetryPause"/>. Register your own
/// before <c>AddZeroResilience()</c> to key on something else — the dependency the request
/// reaches, a tenant, a shard — and nothing else in the package changes.
/// </para>
/// </remarks>
public interface IRetryBrake : ISingleton
{
    /// <summary>Whether a failed request of this type may be handled again.</summary>
    /// <param name="requestType">The concrete request type being handled.</param>
    /// <returns><see langword="true"/> when another attempt is allowed.</returns>
    bool AllowsRetry(Type requestType);

    /// <summary>Records that a request of this type used every attempt it was allowed and still failed.</summary>
    /// <param name="requestType">The concrete request type being handled.</param>
    void Exhausted(Type requestType);

    /// <summary>Records that a request of this type succeeded, on whichever attempt.</summary>
    /// <param name="requestType">The concrete request type being handled.</param>
    void Succeeded(Type requestType);
}

/// <summary>
/// Pauses retrying a request type after it has failed outright several times running.
/// </summary>
/// <remarks>
/// The state lives here rather than in a static field on the behaviour's closed generic
/// type, which would give the same per-request-type bucketing for nothing. A static would
/// leak between tests in the same process and could never be reset without a hook that
/// exists only for tests; a singleton belongs to its container, so two applications — or two
/// tests — cannot see each other's outages.
/// </remarks>
/// <param name="options">The threshold and how long the pause lasts.</param>
/// <param name="time">Decides when a pause has run out.</param>
internal sealed class ConsecutiveFailureBrake(IOptions<ResilienceOptions> options, TimeProvider time) : IRetryBrake
{
    private readonly ConcurrentDictionary<Type, Streak> _streaks = new();

    /// <inheritdoc />
    public bool AllowsRetry(Type requestType)
        => !_streaks.TryGetValue(requestType, out var streak) || streak.AllowsRetry(time.GetUtcNow());

    /// <inheritdoc />
    public void Exhausted(Type requestType)
    {
        var settings = options.Value;

        if (settings.PauseRetriesAfterConsecutiveFailures <= 0) return;

        _streaks
            .GetOrAdd(requestType, static _ => new Streak())
            .Exhausted(
                time.GetUtcNow(), settings.PauseRetriesAfterConsecutiveFailures, settings.RetryPause);
    }

    /// <inheritdoc />
    public void Succeeded(Type requestType)
    {
        // Only when something is being counted. A healthy application would otherwise fill
        // the dictionary with an entry per request type to record that nothing is wrong.
        if (_streaks.TryGetValue(requestType, out var streak)) streak.Succeeded();
    }

    /// <summary>What one request type has been doing lately.</summary>
    private sealed class Streak
    {
        private readonly object _gate = new();

        private int _failures;
        private DateTimeOffset _pausedUntil;

        public bool AllowsRetry(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_pausedUntil == default) return true;
                if (now < _pausedUntil) return false;

                // The pause has run out, so the streak starts again from nothing. Keeping the
                // old count would re-engage the brake on the first failure after every pause,
                // which is a dependency that never gets a second chance to prove it is back.
                _failures = 0;
                _pausedUntil = default;

                return true;
            }
        }

        public void Exhausted(DateTimeOffset now, int threshold, TimeSpan pause)
        {
            lock (_gate)
            {
                // Already paused: this request was allowed one attempt and it failed, which
                // says nothing new. Counting it would extend the pause every time somebody
                // asked, and the pause would then end only when the traffic did.
                if (_pausedUntil != default) return;

                if (++_failures < threshold) return;

                _pausedUntil = now + pause;
            }
        }

        public void Succeeded()
        {
            lock (_gate)
            {
                _failures = 0;
                _pausedUntil = default;
            }
        }
    }
}
