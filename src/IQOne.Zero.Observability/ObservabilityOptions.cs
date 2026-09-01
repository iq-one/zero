using IQOne.Zero.Messaging;

namespace IQOne.Zero.Observability;

/// <summary>What the pipeline observes, and how much of it.</summary>
/// <remarks>
/// One instance for the application, held as a singleton and read by the behaviours as each
/// request goes through. A second <c>AddZeroObservability</c> call refines this instance
/// rather than replacing it, so a module and a host may both configure observability and
/// neither silently undoes the other.
/// </remarks>
public sealed class ObservabilityOptions
{
    /// <summary>Whether every request is logged. On by default.</summary>
    /// <remarks>
    /// Turning this off is almost never the right lever. A log that is too loud is turned
    /// down with a logging filter — <c>"Logging:LogLevel:Acme.Invoices": "Warning"</c> — which
    /// leaves the failures and drops the rest. Turning the behaviour off loses both.
    /// </remarks>
    public bool EnableLogging { get; set; } = true;

    /// <summary>Whether every request starts an activity. On by default.</summary>
    /// <remarks>
    /// Costs nothing when nothing is listening: an activity is only created once a collector
    /// has subscribed to <see cref="ZeroTelemetry.ActivitySourceName"/>.
    /// </remarks>
    public bool EnableTracing { get; set; } = true;

    /// <summary>Whether every request is counted and timed. On by default.</summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Whether the request object itself is written to the log. Off by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A command carries whatever the caller sent: an email address, a diagnosis, a bank
    /// account, a password someone put in the wrong field. Logs travel further than the
    /// database that data came from — to a shared aggregator, into a support ticket, onto a
    /// laptop — under retention and access rules nobody checked against the ones the data
    /// arrived with. Defaulting to on would make every new command a data-protection
    /// decision taken by whoever forgot to make it.
    /// </para>
    /// <para>
    /// So it is opt-in, per application, by someone who has looked at what their requests
    /// hold. Even then it is written at <c>Debug</c>, so it takes a second deliberate act to
    /// make it appear in production.
    /// </para>
    /// </remarks>
    public bool LogRequestContents { get; set; }
}

/// <summary>
/// Where the observability behaviours sit, in the gaps <see cref="BehaviorOrder"/> leaves.
/// </summary>
/// <remarks>
/// Both sit just inside <see cref="BehaviorOrder.Logging"/> and outside everything that can
/// reject a request, so a request refused by authorization is still traced and still counted.
/// A rejection nobody measured is how a broken permission looks like a quiet afternoon.
/// </remarks>
public static class ObservabilityOrder
{
    /// <summary>
    /// Inside logging, outside metrics.
    /// </summary>
    /// <remarks>
    /// Outside metrics so that <see cref="System.Diagnostics.Activity.Current"/> is set when
    /// a measurement is recorded: that is what lets a collector attach the trace id to the
    /// measurement as an exemplar, and an exemplar is what turns a spike on a latency graph
    /// into the trace that caused it.
    /// </remarks>
    public const int Tracing = BehaviorOrder.Logging + 10;

    /// <summary>Innermost of the three, so its measurement covers everything below it.</summary>
    public const int Metrics = BehaviorOrder.Logging + 20;
}
