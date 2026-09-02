using IQOne.Zero.Messaging;

namespace IQOne.Zero.Resilience;

/// <summary>How the pipeline retries, for the whole application.</summary>
/// <remarks>
/// <para>
/// Everything about a single request — whether it may be retried at all — belongs to the
/// request, through <see cref="IIdempotent"/> and through being a query or a command. What
/// is left here is the handful of numbers that are the same for every request in a
/// deployment: how many attempts, how long between them, and which failures are worth
/// another try.
/// </para>
/// <para>
/// These are retries around a <em>use case</em>. Retrying one HTTP call is a different job
/// and the platform already does it better: put a Polly pipeline on the
/// <c>HttpClient</c> with <c>Microsoft.Extensions.Http.Resilience</c>. Turning three
/// attempts here into a substitute for that re-runs authorization, validation and a
/// transaction to work around one flaky socket.
/// </para>
/// </remarks>
public sealed class ResilienceOptions
{
    /// <summary>
    /// Kinds that another attempt cannot change, whatever the configuration says.
    /// </summary>
    /// <remarks>
    /// The same input validated by the same rules fails identically; the same caller is
    /// still not permitted; the row that was not there is still not there. Retrying any of
    /// them only delays the caller's answer and multiplies the load that produced it.
    /// </remarks>
    internal static readonly ErrorKind[] NeverWorthRetrying =
    [
        ErrorKind.Validation,
        ErrorKind.Unauthorized,
        ErrorKind.Forbidden,
        ErrorKind.NotFound
    ];

    /// <summary>
    /// Whether anything is retried at all. On by default.
    /// </summary>
    /// <remarks>
    /// One switch, so a test can turn retrying off without unpicking its registrations. A
    /// test whose subject is what a handler does with a failure should see that failure
    /// once, not three times.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many times a request may be handled in total, the first try included. Three by
    /// default.
    /// </summary>
    /// <remarks>
    /// Attempts rather than retries, because "three retries" is read as three by half the
    /// team and four by the other half. One means never retry, which is what
    /// <see cref="Enabled"/> already says more clearly.
    /// </remarks>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>How long to wait before the second attempt. 200ms by default.</summary>
    /// <remarks>
    /// Short, because the failure this exists for is a connection reset or a lock timeout,
    /// and the caller is usually a person waiting for a page. A dependency that needs
    /// seconds to come back needs a queue, not a longer pause on the request thread.
    /// </remarks>
    public TimeSpan FirstDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>What each wait is multiplied by for the next one. Two by default.</summary>
    /// <remarks>
    /// Backing off matters more than the exact factor: a dependency that is failing because
    /// it is overloaded is made worse by a client that retries at a fixed rate, and every
    /// client doing so at once is the outage that will not end.
    /// </remarks>
    public double BackoffFactor { get; set; } = 2.0;

    /// <summary>The longest any single wait may grow to. Five seconds by default.</summary>
    /// <remarks>
    /// Doubling has no natural ceiling, and without one the eighth attempt waits half a
    /// minute on a thread someone is waiting behind.
    /// </remarks>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How much of each wait is decided at random, from 0 to 1. Half by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero waits exactly the computed time; one waits anywhere between nothing and it; the
    /// default waits at least half of it. Randomness is the point of the knob, not a detail
    /// of it: clients that fail together back off together, and an un-jittered backoff turns
    /// one spike into a slower, sharper spike at every multiple of the delay.
    /// </para>
    /// <para>
    /// Set it to zero when a test needs to state the exact waits it expects.
    /// </para>
    /// </remarks>
    public double Jitter { get; set; } = 0.5;

    /// <summary>
    /// The failure kinds worth another attempt. <see cref="ErrorKind.Unavailable"/> only, by
    /// default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole reason the package exists. A Zero operation reports failure by
    /// returning a value, so a retry policy written against exceptions never fires on one:
    /// the handler returns <see cref="ErrorKind.Unavailable"/> and, as far as anything
    /// watching for a throw is concerned, the call succeeded.
    /// </para>
    /// <para>
    /// <see cref="ErrorKind.Conflict"/> is the one worth considering next, and only when the
    /// conflict is optimistic concurrency — two writers racing, where the loser reloads and
    /// wins the second time. A conflict that means "the invoice is already closed" will mean
    /// the same thing on every attempt, so add it per deployment rather than by default.
    /// </para>
    /// <para>
    /// <see cref="ErrorKind.Validation"/>, <see cref="ErrorKind.Unauthorized"/>,
    /// <see cref="ErrorKind.Forbidden"/> and <see cref="ErrorKind.NotFound"/> are refused at
    /// startup rather than quietly ignored, because a setting that is silently disregarded
    /// is a setting somebody is relying on.
    /// </para>
    /// </remarks>
    public ISet<ErrorKind> RetryOn { get; } = new HashSet<ErrorKind> { ErrorKind.Unavailable };

    /// <summary>
    /// How many requests of one type may fail every attempt, one after another, before
    /// retrying that type pauses. Five by default; zero never pauses.
    /// </summary>
    /// <remarks>
    /// A retrier with no brake is a load amplifier: the moment a dependency starts failing,
    /// the traffic against it triples, which is the last thing it needs. This is the brake.
    /// It is deliberately not a circuit breaker — see <see cref="IRetryBrake"/> for why the
    /// difference matters.
    /// </remarks>
    public int PauseRetriesAfterConsecutiveFailures { get; set; } = 5;

    /// <summary>How long retrying stays paused once the brake engages. Thirty seconds by default.</summary>
    /// <remarks>
    /// Long enough that a restarting dependency is not hammered while it comes up, short
    /// enough that recovery does not wait on a human. Any request of that type that succeeds
    /// releases the brake immediately, so the duration is a ceiling rather than a sentence.
    /// </remarks>
    public TimeSpan RetryPause { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Where the retry behaviour sits, in the gap <see cref="BehaviorOrder"/> leaves between
/// caching and the transaction.
/// </summary>
/// <remarks>
/// <para>
/// The position that matters is <em>outside</em> <see cref="BehaviorOrder.Transaction"/>, so
/// each attempt gets a transaction of its own. Retrying inside one is retrying inside a
/// scope the first failure may already have poisoned — a deadlock victim's transaction is
/// aborted by the server, and every command issued after that throws rather than retries.
/// Even where the scope survives, a second attempt inside it accumulates the first
/// attempt's half-written changes and commits them together.
/// </para>
/// <para>
/// It sits inside caching so that a stored answer is not retried and a hard-won one is
/// stored, and inside validation and authorization so that a request which will be refused
/// is refused once.
/// </para>
/// </remarks>
public static class ResilienceOrder
{
    /// <summary>Outside the transaction, inside the cache.</summary>
    public const int Retry = BehaviorOrder.Transaction - 100;
}
