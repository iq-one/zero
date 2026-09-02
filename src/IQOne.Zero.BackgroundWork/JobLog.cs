using Microsoft.Extensions.Logging;

namespace IQOne.Zero.BackgroundWork;

/// <summary>
/// Every line the scheduler writes, declared once.
/// </summary>
/// <remarks>
/// <para>
/// <c>[LoggerMessage]</c> methods, for the same reasons Observability's <c>RequestLog</c> uses
/// them: the template is parsed at build time, the level check comes first, and a line nobody
/// wants costs one <c>IsEnabled</c> call.
/// </para>
/// <para>
/// The category is <c>IQOne.Zero.BackgroundWork.&lt;job name&gt;</c>, so one noisy job can be
/// turned down — <c>"Logging:LogLevel:IQOne.Zero.BackgroundWork.invoices.reconcile":
/// "Warning"</c> — without touching the others and without an option of ours.
/// </para>
/// </remarks>
internal static partial class JobLog
{
    [LoggerMessage(
        EventId = 5501,
        EventName = "ZeroJobScheduled",
        Level = LogLevel.Debug,
        Message = "{Job} is scheduled {Schedule}; first occurrence at {DueAt:O}")]
    internal static partial void Scheduled(ILogger logger, string job, string schedule, DateTimeOffset dueAt);

    /// <summary>
    /// A job that is registered but switched off.
    /// </summary>
    /// <remarks>
    /// Information rather than Debug, and written once at startup: "the job is off" is the
    /// first thing somebody wants to know when a job has not run, and finding it out from a
    /// line that is off by default helps nobody.
    /// </remarks>
    [LoggerMessage(
        EventId = 5502,
        EventName = "ZeroJobDisabled",
        Level = LogLevel.Information,
        Message = "{Job} is registered but switched off, so it will not run")]
    internal static partial void Disabled(ILogger logger, string job);

    [LoggerMessage(
        EventId = 5503,
        EventName = "ZeroJobRunStarted",
        Level = LogLevel.Debug,
        Message = "{Job} run {Number} started for the occurrence at {ScheduledFor:O}")]
    internal static partial void Started(ILogger logger, string job, long number, DateTimeOffset scheduledFor);

    [LoggerMessage(
        EventId = 5504,
        EventName = "ZeroJobRunSucceeded",
        Level = LogLevel.Information,
        Message = "{Job} run {Number} succeeded in {ElapsedMilliseconds}ms")]
    internal static partial void Succeeded(ILogger logger, string job, long number, double elapsedMilliseconds);

    /// <summary>
    /// A run that reported a failure.
    /// </summary>
    /// <remarks>
    /// Error, whatever the error's kind, and that is the one place this package deliberately
    /// disagrees with the request pipeline. A rejected request is logged at Information
    /// because a caller received the answer and can act on it. Nobody receives a job's answer.
    /// A "not found" that a schedule reported to itself and then discarded is indistinguishable
    /// from the work silently not happening, so it is written where somebody will see it.
    /// </remarks>
    [LoggerMessage(
        EventId = 5505,
        EventName = "ZeroJobRunFailed",
        Level = LogLevel.Error,
        Message = "{Job} run {Number} failed in {ElapsedMilliseconds}ms with {ErrorCount} error(s): "
                + "{ErrorKind} {ErrorCode} — {ErrorMessage}. The schedule continues; "
                + "this is failure {ConsecutiveFailures} in a row.")]
    internal static partial void Failed(
        ILogger logger,
        string job,
        long number,
        double elapsedMilliseconds,
        int errorCount,
        ErrorKind errorKind,
        string errorCode,
        string errorMessage,
        long consecutiveFailures);

    /// <summary>
    /// An exception escaped a run.
    /// </summary>
    /// <remarks>
    /// The schedule survives it. A job that stopped itself on the first bad row would be a job
    /// that stops in the small hours and is discovered by whoever notices the reports are
    /// stale — so the run is abandoned, the stack is written, and the next occurrence is kept.
    /// </remarks>
    [LoggerMessage(
        EventId = 5506,
        EventName = "ZeroJobRunThrew",
        Level = LogLevel.Error,
        Message = "{Job} run {Number} threw after {ElapsedMilliseconds}ms. The schedule continues; "
                + "this is failure {ConsecutiveFailures} in a row.")]
    internal static partial void Threw(
        ILogger logger,
        Exception exception,
        string job,
        long number,
        double elapsedMilliseconds,
        long consecutiveFailures);

    /// <summary>
    /// The application is stopping and a run was still going.
    /// </summary>
    /// <remarks>
    /// Information, not error, for the same reason a cancelled request is: this is the system
    /// doing as it was told, and paging on a deployment teaches people to ignore the pager.
    /// </remarks>
    [LoggerMessage(
        EventId = 5507,
        EventName = "ZeroJobRunCancelled",
        Level = LogLevel.Information,
        Message = "{Job} run {Number} was abandoned after {ElapsedMilliseconds}ms because the "
                + "application is stopping")]
    internal static partial void Cancelled(ILogger logger, string job, long number, double elapsedMilliseconds);

    /// <summary>
    /// Occurrences that fell while a run was still going.
    /// </summary>
    /// <remarks>
    /// Warning, because it means the period is shorter than the work and the schedule is no
    /// longer the schedule anybody wrote down. It is not an error: dropping them is the
    /// intended behaviour, and it is safer than the alternative.
    /// </remarks>
    [LoggerMessage(
        EventId = 5508,
        EventName = "ZeroJobOccurrencesSkipped",
        Level = LogLevel.Warning,
        Message = "{Job} run {Number} overran its period; {Skipped} occurrence(s) were dropped rather "
                + "than queued. Next at {DueAt:O}")]
    internal static partial void Skipped(
        ILogger logger, string job, long number, long skipped, DateTimeOffset dueAt);

    /// <summary>
    /// A loop stopped for a reason that is not shutdown.
    /// </summary>
    /// <remarks>
    /// Critical, and it should never happen: every run is already wrapped. Reaching here means
    /// the scheduler itself is broken and that job will not run again until the process
    /// restarts, which is worth the loudest level there is.
    /// </remarks>
    [LoggerMessage(
        EventId = 5509,
        EventName = "ZeroJobScheduleStopped",
        Level = LogLevel.Critical,
        Message = "{Job} stopped being scheduled because the scheduler itself faulted. It will not "
                + "run again until the application restarts.")]
    internal static partial void ScheduleStopped(ILogger logger, Exception exception, string job);
}
