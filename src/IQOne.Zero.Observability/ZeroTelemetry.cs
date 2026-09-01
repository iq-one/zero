using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace IQOne.Zero.Observability;

/// <summary>
/// The names a collector subscribes to, and the instruments behind them.
/// </summary>
/// <remarks>
/// <para>
/// The names are constants because subscribing is a compile-time decision on the consumer's
/// side: <c>tracing.AddSource(ZeroTelemetry.ActivitySourceName)</c> is checked by the
/// compiler, a string typed into a configuration file is checked by nobody and fails
/// silently — with no trace, which looks exactly like no traffic.
/// </para>
/// <para>
/// The source and the meter live on a non-generic type on purpose. A static field inside
/// <c>TracingBehavior&lt;TRequest, TResponse&gt;</c> would exist once per closed generic
/// type, so an application with two hundred requests would create two hundred activity
/// sources and two hundred meters, none of which the consumer subscribed to as a unit.
/// </para>
/// </remarks>
public static class ZeroTelemetry
{
    /// <summary>The activity source every request activity is started from.</summary>
    /// <remarks>Pass this to <c>AddSource</c> when configuring OpenTelemetry tracing.</remarks>
    public const string ActivitySourceName = "IQOne.Zero.Observability";

    /// <summary>The meter every request measurement is recorded on.</summary>
    /// <remarks>Pass this to <c>AddMeter</c> when configuring OpenTelemetry metrics.</remarks>
    public const string MeterName = "IQOne.Zero.Observability";

    /// <summary>How many requests were handled, by name and outcome.</summary>
    public const string RequestCountName = "zero.request.count";

    /// <summary>How long requests took, by name and outcome.</summary>
    public const string RequestDurationName = "zero.request.duration";

    private static readonly string? Version = typeof(ZeroTelemetry).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    private static readonly Meter Meter = new(MeterName, Version);

    /// <summary>Started once per request, and only when something is listening.</summary>
    internal static readonly ActivitySource Source = new(ActivitySourceName, Version);

    /// <summary>One increment per request.</summary>
    /// <remarks>
    /// Redundant with the duration histogram's own count, and kept anyway: it survives
    /// histogram sampling, it is cheaper to query, and it is the series an availability
    /// alert is written against.
    /// </remarks>
    internal static readonly Counter<long> RequestCount = Meter.CreateCounter<long>(
        RequestCountName,
        unit: "{request}",
        description: "Requests handled by the Zero pipeline.");

    /// <summary>
    /// How long the pipeline took, in seconds.
    /// </summary>
    /// <remarks>
    /// Seconds rather than milliseconds because that is what the OpenTelemetry conventions
    /// specify for a duration, and a backend that assumes otherwise will draw the graph with
    /// the wrong axis. Log lines report milliseconds, because a human reads those.
    /// </remarks>
    internal static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        RequestDurationName,
        unit: "s",
        description: "Duration of a request through the Zero pipeline.");
}

/// <summary>
/// The tag names carried by activities and measurements.
/// </summary>
/// <remarks>
/// <see cref="ErrorType"/> is the OpenTelemetry name; the rest are namespaced under
/// <c>zero.</c> because no convention covers them and an unprefixed <c>request.name</c>
/// would collide with whatever the next instrumentation library decides to call its own.
/// </remarks>
internal static class TelemetryTags
{
    /// <summary>The request's short type name, which is also the activity name.</summary>
    internal const string RequestName = "zero.request.name";

    /// <summary>The request's full type name, for telling two same-named requests apart.</summary>
    internal const string RequestType = "zero.request.type";

    /// <summary>Success, rejected, faulted or cancelled.</summary>
    internal const string Outcome = "zero.request.outcome";

    /// <summary>
    /// The error code, or the exception type when one escaped.
    /// </summary>
    /// <remarks>
    /// OpenTelemetry asks for a predictable, low-cardinality value here. A Zero error code
    /// is exactly that — it is a written-out constant that the contract forbids changing —
    /// and it is far more actionable than the seven-value kind, which is also recorded.
    /// </remarks>
    internal const string ErrorType = "error.type";

    /// <summary>The <see cref="IQOne.Zero.ErrorKind"/> the failure was classified as.</summary>
    internal const string ErrorKind = "zero.error.kind";

    /// <summary>An id supplied from outside, when one was.</summary>
    internal const string CorrelationId = "zero.correlation.id";
}
