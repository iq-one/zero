using Microsoft.Extensions.Logging;

namespace IQOne.Zero.Observability;

/// <summary>
/// Every line the pipeline writes, declared once.
/// </summary>
/// <remarks>
/// <para>
/// These are <c>[LoggerMessage]</c> methods, so the compiler generates the plumbing: the
/// message template is parsed at build time instead of on every call, the level check comes
/// first, and the arguments never reach a <c>params object[]</c>. A line that is switched off
/// costs one <c>IsEnabled</c> call and nothing else — no formatting, no boxing, no allocation.
/// An interpolated string would have paid for the message before finding out nobody wanted it.
/// </para>
/// <para>
/// The class is not generic on purpose. Putting the methods on the behaviour would give every
/// closed <c>LoggingBehavior&lt;TRequest, TResponse&gt;</c> its own copy of the generated
/// state machinery, which in an application with two hundred requests is two hundred copies
/// of code that differs in nothing.
/// </para>
/// </remarks>
internal static partial class RequestLog
{
    [LoggerMessage(
        EventId = 4001,
        EventName = "ZeroRequestStarted",
        Level = LogLevel.Debug,
        Message = "{Request} started")]
    internal static partial void Started(ILogger logger, string request);

    /// <summary>
    /// The request object itself, written only when the application has opted in.
    /// </summary>
    /// <remarks>
    /// Separate from every other line so that turning contents on cannot accidentally turn
    /// anything else up, and so a sink can drop this one event by name.
    /// </remarks>
    [LoggerMessage(
        EventId = 4002,
        EventName = "ZeroRequestContents",
        Level = LogLevel.Debug,
        Message = "{Request} contents: {Contents}")]
    internal static partial void Contents(ILogger logger, string request, object contents);

    [LoggerMessage(
        EventId = 4003,
        EventName = "ZeroRequestSucceeded",
        Level = LogLevel.Information,
        Message = "{Request} succeeded in {ElapsedMilliseconds}ms")]
    internal static partial void Succeeded(ILogger logger, string request, double elapsedMilliseconds);

    /// <summary>
    /// A request the application answered "no" to, or failed to serve.
    /// </summary>
    /// <remarks>
    /// The level is a parameter because it is decided by the failure, not by the call site:
    /// see <see cref="RequestOutcomeExtensions.ToLogLevel"/>. The first error is written out
    /// in full and the rest are counted, because a form with nine unacceptable fields should
    /// not be nine lines, and the first code is enough to find the validator.
    /// </remarks>
    [LoggerMessage(
        EventId = 4004,
        EventName = "ZeroRequestFailed",
        Message = "{Request} failed in {ElapsedMilliseconds}ms with {ErrorCount} error(s): "
                + "{ErrorKind} {ErrorCode} — {ErrorMessage}")]
    internal static partial void Failed(
        ILogger logger,
        LogLevel level,
        string request,
        double elapsedMilliseconds,
        int errorCount,
        ErrorKind errorKind,
        string errorCode,
        string errorMessage);

    /// <summary>
    /// The caller went away, or the host is stopping.
    /// </summary>
    /// <remarks>
    /// Information, not error. A cancelled request is the system doing as it was told, and
    /// logging it as a fault turns a deployment into a page.
    /// </remarks>
    [LoggerMessage(
        EventId = 4005,
        EventName = "ZeroRequestCancelled",
        Level = LogLevel.Information,
        Message = "{Request} was cancelled after {ElapsedMilliseconds}ms")]
    internal static partial void Cancelled(ILogger logger, string request, double elapsedMilliseconds);

    /// <summary>
    /// An exception escaped the pipeline.
    /// </summary>
    /// <remarks>
    /// Always an error: a failure the application expected would have been an
    /// <see cref="Error"/> returned in a <see cref="Result{TValue}"/>. Reaching here means
    /// nobody planned for it, which is what the stack trace is for.
    /// </remarks>
    [LoggerMessage(
        EventId = 4006,
        EventName = "ZeroRequestThrew",
        Level = LogLevel.Error,
        Message = "{Request} threw after {ElapsedMilliseconds}ms")]
    internal static partial void Threw(
        ILogger logger, Exception exception, string request, double elapsedMilliseconds);
}
