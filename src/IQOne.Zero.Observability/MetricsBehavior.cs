using System.Diagnostics;
using IQOne.Zero.Messaging;

namespace IQOne.Zero.Observability;

/// <summary>
/// Counts and times every request.
/// </summary>
/// <remarks>
/// <para>
/// Two instruments, both tagged with the request's name and its outcome: a counter, which an
/// availability alert is written against, and a duration histogram, which a latency
/// objective is. The tags are the same on both so that "how many" and "how slow" can be
/// sliced the same way and compared without a translation table.
/// </para>
/// <para>
/// Innermost of the three observability behaviours, so the activity started by
/// <see cref="TracingBehavior{TRequest,TResponse}"/> is current when a measurement is
/// recorded and a collector can hang the trace id on it as an exemplar.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request measured.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="options">Whether to measure at all.</param>
/// <param name="time">Measures how long the pipeline took.</param>
public sealed class MetricsBehavior<TRequest, TResponse>(ObservabilityOptions options, TimeProvider time)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;

    /// <inheritdoc />
    public int Order => ObservabilityOrder.Metrics;

    /// <inheritdoc />
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!options.EnableMetrics) return await next().ConfigureAwait(false);

        var started = time.GetTimestamp();

        try
        {
            var result = await next().ConfigureAwait(false);

            if (result.IsSuccess) Record(started, RequestOutcome.Success, null);
            else Record(started, result.Error.Kind.ToOutcome(), result.Error.Code);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Record(started, RequestOutcome.Cancelled, null);
            throw;
        }
        catch (Exception exception)
        {
            Record(started, RequestOutcome.Faulted, exception.GetType().FullName);
            throw;
        }
    }

    /// <summary>Writes both instruments from one set of tags.</summary>
    /// <remarks>
    /// <see cref="TagList"/> rather than a dictionary or an array: it keeps up to eight tags
    /// on the stack, and this runs on every request of every kind, including the ones an
    /// application sends thousands of times a second.
    /// </remarks>
    private void Record(long started, RequestOutcome outcome, string? errorType)
    {
        var tags = new TagList
        {
            { TelemetryTags.RequestName, RequestName },
            { TelemetryTags.Outcome, outcome.ToTagValue() }
        };

        if (errorType is not null) tags.Add(TelemetryTags.ErrorType, errorType);

        ZeroTelemetry.RequestCount.Add(1, tags);
        ZeroTelemetry.RequestDuration.Record(time.GetElapsedTime(started).TotalSeconds, tags);
    }
}
