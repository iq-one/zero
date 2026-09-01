using System.Diagnostics;
using IQOne.Zero.Messaging;

namespace IQOne.Zero.Observability;

/// <summary>
/// Puts every request on one activity, tagged with how it turned out.
/// </summary>
/// <remarks>
/// <para>
/// One activity per request, named for the request, of kind
/// <see cref="ActivityKind.Internal"/> — the work happens in this process, and the span that
/// says a call arrived over HTTP or off a queue belongs to whichever instrumentation
/// received it. Ours is the child that says what the application then did.
/// </para>
/// <para>
/// Nothing is created when nothing is listening. <c>StartActivity</c> returns
/// <see langword="null"/> until a collector has subscribed to
/// <see cref="ZeroTelemetry.ActivitySourceName"/>, so an application that never configures
/// tracing pays for one null check per request.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request traced.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="options">Whether to trace at all.</param>
public sealed class TracingBehavior<TRequest, TResponse>(ObservabilityOptions options)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestName = typeof(TRequest).Name;
    private static readonly string RequestType = typeof(TRequest).FullName ?? RequestName;

    /// <inheritdoc />
    public int Order => ObservabilityOrder.Tracing;

    /// <inheritdoc />
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!options.EnableTracing) return await next().ConfigureAwait(false);

        using var activity = ZeroTelemetry.Source.StartActivity(RequestName, ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(TelemetryTags.RequestName, RequestName);
            activity.SetTag(TelemetryTags.RequestType, RequestType);

            if (CorrelationId.Supplied is { } correlationId)
                activity.SetTag(TelemetryTags.CorrelationId, correlationId);
        }

        try
        {
            var result = await next().ConfigureAwait(false);

            if (activity is not null) Describe(activity, result);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Left Unset rather than marked an error: the trace should show the work stopped,
            // not claim the service broke because a caller hung up.
            activity?.SetTag(TelemetryTags.Outcome, RequestOutcome.Cancelled.ToTagValue());
            throw;
        }
        catch (Exception exception)
        {
            if (activity is not null) Record(activity, exception);

            throw;
        }
    }

    /// <summary>Tags the activity with the outcome, and marks it failed only when it failed.</summary>
    /// <remarks>
    /// A rejection leaves the status <see cref="ActivityStatusCode.Unset"/>. Marking it an
    /// error would paint every trace of a working application red, which is the same mistake
    /// as treating an HTTP 404 from a healthy server as a server error.
    /// </remarks>
    private static void Describe(Activity activity, in Result<TResponse> result)
    {
        if (result.IsSuccess)
        {
            activity.SetTag(TelemetryTags.Outcome, RequestOutcome.Success.ToTagValue());
            return;
        }

        var error = result.Error;
        var outcome = error.Kind.ToOutcome();

        activity.SetTag(TelemetryTags.Outcome, outcome.ToTagValue());
        activity.SetTag(TelemetryTags.ErrorType, error.Code);
        activity.SetTag(TelemetryTags.ErrorKind, error.Kind.ToString());

        if (outcome is RequestOutcome.Faulted) activity.SetStatus(ActivityStatusCode.Error, error.Message);
    }

    /// <summary>Records an escaped exception the way the OpenTelemetry conventions describe it.</summary>
    /// <remarks>
    /// Written out rather than calling <c>Activity.AddException</c>, which arrived in .NET 9
    /// and would leave the net8.0 build with a thinner trace than the net10.0 one for no
    /// reason a consumer could discover.
    /// </remarks>
    private static void Record(Activity activity, Exception exception)
    {
        var type = exception.GetType().FullName ?? exception.GetType().Name;

        activity.SetTag(TelemetryTags.Outcome, RequestOutcome.Faulted.ToTagValue());
        activity.SetTag(TelemetryTags.ErrorType, type);
        activity.SetStatus(ActivityStatusCode.Error, exception.Message);

        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            ["exception.type"] = type,
            ["exception.message"] = exception.Message,
            ["exception.stacktrace"] = exception.ToString()
        }));
    }
}
