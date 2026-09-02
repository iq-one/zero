using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace IQOne.Zero.BackgroundWork;

/// <summary>
/// The names a collector subscribes to for background work, and the instruments behind them.
/// </summary>
/// <remarks>
/// <para>
/// A second source and a second meter, next to <c>IQOne.Zero.Observability</c>'s, and only
/// because a job run genuinely is not a request. Observability's behaviours wrap
/// <c>ISender</c>; nothing in a job's own body ever passes through them, so a job that keeps
/// its work in a class body would otherwise be the one part of the application producing no
/// telemetry at all. Taking a package reference on Observability to avoid the second name
/// would make a capability depend on a sibling, which the contract forbids and which would
/// force logging, tracing and metrics on an application that wanted a timer.
/// </para>
/// <para>
/// Where a job's work <em>is</em> a command — which is the shape this package pushes you
/// towards — you get both, and they are not duplicates. <c>zero.request.*</c> measures the
/// command; <c>zero.job.*</c> measures the occurrence, including the ones that produced no
/// command because the previous run was still going. The job's activity is the request
/// activity's parent, so one trace shows the schedule and the work in the same tree.
/// </para>
/// <para>
/// The source and the meter live here, on a non-generic type, for the same reason
/// <c>ZeroTelemetry</c> does: a static field on a per-job type would create one source per
/// job, none of which the consumer subscribed to as a unit.
/// </para>
/// </remarks>
public static class ZeroJobTelemetry
{
    /// <summary>The activity source every job run is started from.</summary>
    /// <remarks>Pass this to <c>AddSource</c> when configuring OpenTelemetry tracing.</remarks>
    public const string ActivitySourceName = "IQOne.Zero.BackgroundWork";

    /// <summary>The meter every job measurement is recorded on.</summary>
    /// <remarks>Pass this to <c>AddMeter</c> when configuring OpenTelemetry metrics.</remarks>
    public const string MeterName = "IQOne.Zero.BackgroundWork";

    /// <summary>How many job runs finished, by job name and outcome.</summary>
    /// <remarks>
    /// The series an alert is written against:
    /// <c>zero.job.runs{zero.job.outcome="faulted"}</c> going above zero, or
    /// <c>zero.job.runs{zero.job.outcome="succeeded"}</c> going to zero for longer than the
    /// job's period, are the two ways a broken schedule shows itself.
    /// </remarks>
    public const string JobRunCountName = "zero.job.runs";

    /// <summary>How long job runs took, by job name and outcome.</summary>
    /// <remarks>
    /// Compare it against the period. A distribution that reaches the period is a job whose
    /// occurrences are being dropped, and that is visible here before anyone reads a log.
    /// </remarks>
    public const string JobDurationName = "zero.job.duration";

    /// <summary>The tag carrying the job's registered name.</summary>
    public const string JobNameTag = "zero.job.name";

    /// <summary>The tag carrying the run's outcome: succeeded, failed, faulted or cancelled.</summary>
    public const string JobOutcomeTag = "zero.job.outcome";

    /// <summary>The tag carrying the error code, when a run failed.</summary>
    /// <remarks>
    /// <c>error.type</c> is the OpenTelemetry name, and a Zero error code is exactly the
    /// low-cardinality, written-out value that convention asks for.
    /// </remarks>
    public const string ErrorTypeTag = "error.type";

    private static readonly string? Version = typeof(ZeroJobTelemetry).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static readonly Meter Meter = new(MeterName, Version);

    /// <summary>Started once per run, and only when something is listening.</summary>
    internal static readonly ActivitySource Source = new(ActivitySourceName, Version);

    /// <summary>One increment per finished run.</summary>
    internal static readonly Counter<long> Runs = Meter.CreateCounter<long>(
        JobRunCountName,
        unit: "{run}",
        description: "Recurring job runs that finished.");

    /// <summary>
    /// How long a run took, in seconds.
    /// </summary>
    /// <remarks>
    /// Seconds because that is what the OpenTelemetry conventions specify for a duration.
    /// Log lines say milliseconds, because a human reads those.
    /// </remarks>
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        JobDurationName,
        unit: "s",
        description: "Duration of a recurring job run.");

    /// <summary>
    /// The value written to the outcome tag.
    /// </summary>
    /// <remarks>
    /// Written out rather than derived from the enum name: dashboards and alert rules match on
    /// these strings and nobody recompiles those, so renaming a member must not change what
    /// they match.
    /// </remarks>
    /// <param name="outcome">How the run turned out.</param>
    /// <returns>The tag value.</returns>
    internal static string ToTagValue(this JobRunOutcome outcome) => outcome switch
    {
        JobRunOutcome.Succeeded => "succeeded",
        JobRunOutcome.Failed => "failed",
        JobRunOutcome.Faulted => "faulted",
        JobRunOutcome.Cancelled => "cancelled",
        _ => "unknown"
    };
}
