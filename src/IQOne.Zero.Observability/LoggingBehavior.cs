using IQOne.Zero.Messaging;
using Microsoft.Extensions.Logging;

namespace IQOne.Zero.Observability;

/// <summary>
/// Writes one line for every request, at the level its outcome deserves.
/// </summary>
/// <remarks>
/// <para>
/// Placed at <see cref="BehaviorOrder.Logging"/>, the outermost position, so that nothing
/// finishes unobserved: a request rejected by authorization, a request stopped by validation
/// and an exception thrown by any other behaviour all pass back through here.
/// </para>
/// <para>
/// The level is chosen by the failure, not by the fact of failing. A rejected request is the
/// application answering correctly and is logged at <c>Information</c>; a fault is logged at
/// <c>Warning</c> or <c>Error</c>. The reason to care is that operators learn what a warning
/// means from how often it is wrong, and a warning that is usually a mistyped postcode
/// teaches them to ignore the one that is a dead database.
/// </para>
/// <para>
/// The category is the request type, so an application can turn one request up or a whole
/// namespace down — <c>"Logging:LogLevel:Acme.Invoices.Queries": "Warning"</c> — without an
/// option, a filter of ours, or a redeploy.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request logged.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="logger">Writes the lines, under the request type's category.</param>
/// <param name="options">Whether to log at all, and whether contents may be written.</param>
/// <param name="time">Measures how long the pipeline took.</param>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<TRequest> logger, ObservabilityOptions options, TimeProvider time)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;

    /// <inheritdoc />
    public int Order => BehaviorOrder.Logging;

    /// <inheritdoc />
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!options.EnableLogging) return await next().ConfigureAwait(false);

        // Only an id that came from outside. The trace id already reaches every line through
        // Activity.Current, and a scope repeating it would be a duplicated column on all of
        // them; an id a caller issued reaches the log through nothing else at all.
        using var correlated = CorrelationId.Supplied is { } supplied
            ? logger.BeginScope(new[] { new KeyValuePair<string, object>(TelemetryTags.CorrelationId, supplied) })
            : null;

        RequestLog.Started(logger, RequestName);

        if (options.LogRequestContents) RequestLog.Contents(logger, RequestName, request!);

        var started = time.GetTimestamp();

        try
        {
            var result = await next().ConfigureAwait(false);

            if (result.IsSuccess)
            {
                RequestLog.Succeeded(logger, RequestName, Elapsed(started));
            }
            else
            {
                var error = result.Error;

                RequestLog.Failed(
                    logger,
                    error.Kind.ToLogLevel(),
                    RequestName,
                    Elapsed(started),
                    result.Errors.Count,
                    error.Kind,
                    error.Code,
                    error.Message);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RequestLog.Cancelled(logger, RequestName, Elapsed(started));
            throw;
        }
        catch (Exception exception)
        {
            RequestLog.Threw(logger, exception, RequestName, Elapsed(started));

            // Rethrown, not turned into a failed result: deciding what an unplanned exception
            // means to a caller is the edge's job, and swallowing it here would hand every
            // transport a success-shaped hole where the error mapping should be.
            throw;
        }
    }

    private double Elapsed(long started) => time.GetElapsedTime(started).TotalMilliseconds;
}
