namespace IQOne.Zero.BackgroundWork;

/// <summary>
/// How one occurrence turned out.
/// </summary>
/// <remarks>
/// The same distinction <c>RequestOutcome</c> draws for a request, drawn for a run: what an
/// operator needs at three in the morning is not success against failure but whose problem it
/// is. The difference here is that nobody is waiting for a job's answer, so a failure it
/// returned and a failure it threw are both reported — a job whose <see cref="Failed"/> count
/// climbs quietly for six weeks is the failure mode this package has to make impossible.
/// </remarks>
public enum JobRunOutcome
{
    /// <summary>The run finished and reported success.</summary>
    Succeeded,

    /// <summary>
    /// The run finished and reported a failure.
    /// </summary>
    /// <remarks>
    /// The job did its job — it decided the work could not be done and said so — but the work
    /// still did not happen, and nobody received that answer. Logged at error level for
    /// exactly that reason.
    /// </remarks>
    Failed,

    /// <summary>An exception escaped the run.</summary>
    Faulted,

    /// <summary>The application began stopping before the run finished.</summary>
    /// <remarks>
    /// Not a fault. Counting a deployment as an incident is how an error-rate alert becomes
    /// something people mute.
    /// </remarks>
    Cancelled
}

/// <summary>What one occurrence did, as it is handed to the log, the meter and the status report.</summary>
/// <param name="Name">The job's registered name.</param>
/// <param name="Number">Which run this was, counted from one.</param>
/// <param name="ScheduledFor">The occurrence it served.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="Duration">How long it took.</param>
/// <param name="Outcome">How it turned out.</param>
/// <param name="Failure">The reason it failed, when it did.</param>
internal sealed record JobRun(
    string Name,
    long Number,
    DateTimeOffset ScheduledFor,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    JobRunOutcome Outcome,
    string? Failure);

/// <summary>
/// What a registered job is doing, for a health check or an operator's endpoint.
/// </summary>
/// <remarks>
/// A snapshot, not a live view: read it again to see it change.
/// </remarks>
/// <param name="Name">The job's registered name.</param>
/// <param name="Period">How often it is meant to run.</param>
/// <param name="Enabled">Whether it is allowed to run right now.</param>
/// <param name="NextDueAt">When the next occurrence falls, or <see langword="null"/> before the host starts.</param>
/// <param name="LastStartedAt">When it last began, or <see langword="null"/> if it never has.</param>
/// <param name="LastOutcome">How the last run turned out, or <see langword="null"/> if there was none.</param>
/// <param name="LastFailure">Why the last run failed, when it did.</param>
/// <param name="Runs">How many runs have finished in this process.</param>
/// <param name="Failures">How many of those failed or faulted.</param>
/// <param name="ConsecutiveFailures">
/// How many have failed in a row. The number a health check should look at: one failure is
/// weather, four hundred is a broken job that nobody has been paged about.
/// </param>
/// <param name="Skipped">
/// How many occurrences were dropped because a run was still going. Persistently above zero
/// means the period is shorter than the work.
/// </param>
public sealed record RecurringJobStatus(
    string Name,
    TimeSpan Period,
    bool Enabled,
    DateTimeOffset? NextDueAt,
    DateTimeOffset? LastStartedAt,
    JobRunOutcome? LastOutcome,
    string? LastFailure,
    long Runs,
    long Failures,
    long ConsecutiveFailures,
    long Skipped);

/// <summary>
/// What background work is registered and how it is going.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <c>AddZeroBackgroundWork</c> as a singleton, so an application can map a
/// health check or a diagnostic endpoint onto it without this package knowing what a health
/// check or an endpoint is.
/// </para>
/// <para>
/// This is the answer to "the job threw on run 400 of 10,000 — how does an operator find out".
/// The other two answers are the log, which carries the stack trace, and
/// <c>zero.job.runs{zero.job.outcome="faulted"}</c>, which is what an alert is written
/// against. This one is what a readiness probe can read without a metrics backend at all.
/// </para>
/// </remarks>
public interface IBackgroundWorkStatus
{
    /// <summary>Every registered job, in registration order.</summary>
    IReadOnlyList<RecurringJobStatus> Jobs { get; }

    /// <summary>One job by name, or <see langword="null"/> when nothing is registered under it.</summary>
    /// <param name="name">The job's registered name. Compared without regard to case.</param>
    /// <returns>Its status, or <see langword="null"/>.</returns>
    RecurringJobStatus? Find(string name);
}
