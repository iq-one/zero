using Microsoft.Extensions.Logging;

namespace IQOne.Zero.Observability;

/// <summary>
/// How a request turned out, in the only terms an operator cares about.
/// </summary>
/// <remarks>
/// <para>
/// The distinction that matters at three in the morning is not success against failure, it
/// is <em>whose problem it is</em>. A request rejected because the input was unacceptable or
/// the invoice does not exist is the application working: the answer is "no", and it was
/// delivered correctly. A request that faulted is the application failing.
/// </para>
/// <para>
/// Mixing the two is how dashboards become useless. An error rate that counts every
/// not-found never goes to zero, so nobody alerts on it, so nobody notices when it climbs.
/// </para>
/// </remarks>
public enum RequestOutcome
{
    /// <summary>The handler produced a value.</summary>
    Success,

    /// <summary>
    /// The application gave a definite negative answer, and was right to.
    /// </summary>
    /// <remarks>
    /// The input was not acceptable, the thing does not exist, the state does not allow it,
    /// or the caller may not. Nothing is wrong with the system.
    /// </remarks>
    Rejected,

    /// <summary>
    /// Something went wrong that the application did not intend.
    /// </summary>
    /// <remarks>
    /// An unclassified failure, an unavailable dependency, or an exception that escaped the
    /// handler. This is the outcome worth waking someone for.
    /// </remarks>
    Faulted,

    /// <summary>
    /// The caller went away, or the host is shutting down, before an answer was produced.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Faulted"/> because a client that hangs up is not an
    /// incident, and counting it as one inflates the error rate exactly when a service is
    /// under the load that makes clients hang up.
    /// </remarks>
    Cancelled
}

/// <summary>
/// Classifies a failure for logging, tracing and metrics.
/// </summary>
/// <remarks>
/// This judgement is made once, here, and the three behaviours share it. If logging thought
/// a not-found were a warning while metrics thought it a success, neither signal could be
/// trusted against the other.
/// </remarks>
public static class RequestOutcomeExtensions
{
    /// <summary>How a failure of this kind should be counted.</summary>
    /// <param name="kind">How the failure was classified when it was produced.</param>
    /// <returns>The outcome recorded against the request.</returns>
    public static RequestOutcome ToOutcome(this ErrorKind kind) => kind switch
    {
        ErrorKind.Failure or ErrorKind.Unavailable => RequestOutcome.Faulted,
        _ => RequestOutcome.Rejected
    };

    /// <summary>
    /// The level a failure of this kind is logged at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A rejection is logged at <see cref="LogLevel.Information"/>: it is a record of what
    /// the application answered, not a report of anything being wrong. Raising it to a
    /// warning teaches operators that warnings are noise.
    /// </para>
    /// <para>
    /// <see cref="ErrorKind.Unavailable"/> is a warning rather than an error because it says
    /// a dependency blinked and the operation is usually worth retrying;
    /// <see cref="ErrorKind.Failure"/> is an error because nobody classified it, which means
    /// nobody expected it.
    /// </para>
    /// </remarks>
    /// <param name="kind">How the failure was classified when it was produced.</param>
    /// <returns>The level to log at.</returns>
    public static LogLevel ToLogLevel(this ErrorKind kind) => kind switch
    {
        ErrorKind.Failure => LogLevel.Error,
        ErrorKind.Unavailable => LogLevel.Warning,
        _ => LogLevel.Information
    };

    /// <summary>The value written to the outcome tag on activities and measurements.</summary>
    /// <remarks>
    /// Written out rather than derived from the enum name: the tag is queried in dashboards
    /// and alert rules that nobody recompiles, so renaming a member must not silently change
    /// what those rules match.
    /// </remarks>
    /// <param name="outcome">The outcome recorded against the request.</param>
    /// <returns>The tag value.</returns>
    internal static string ToTagValue(this RequestOutcome outcome) => outcome switch
    {
        RequestOutcome.Success => "success",
        RequestOutcome.Rejected => "rejected",
        RequestOutcome.Faulted => "faulted",
        RequestOutcome.Cancelled => "cancelled",
        _ => "unknown"
    };
}
