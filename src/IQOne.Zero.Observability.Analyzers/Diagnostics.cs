using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Observability.Analyzers;

/// <summary>Diagnostics for observability a handler took on itself.</summary>
/// <remarks>
/// <para>
/// Both rules here catch a handler doing something the pipeline already does, and both
/// mistakes are silent. Telemetry recorded on a source nobody subscribed to is not collected
/// and nothing says so; a request written into a log line is collected perfectly well, by
/// people who were never meant to read it.
/// </para>
/// <para>
/// Deliberately absent: a rule about a handler taking an <c>ILogger</c>. A handler that logs
/// a domain event — an invoice closed, a payment reconciled — is writing something the
/// pipeline cannot know about, and reporting that would train everyone to suppress the
/// category. What is reported is the narrow case where the handler's own telemetry is
/// provably useless or provably unsafe.
/// </para>
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.Observability";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    public static readonly DiagnosticDescriptor OwnTelemetrySource = new(
        "ZERO400",
        "A handler creates its own telemetry source",
        "'{0}' creates a {1} of its own. Nothing subscribes to it, so what it records is never " +
        "collected; the pipeline already traces and counts every request.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An ActivitySource or a Meter is only ever read by a collector that was told its name. " +
                     "A name invented inside a handler is a name no collector was told, so the spans and the " +
                     "measurements are produced and then dropped — which looks exactly like code that is " +
                     "never reached.",
        helpLinkUri: HelpRoot + "ZERO400");

    public static readonly DiagnosticDescriptor RequestInLog = new(
        "ZERO401",
        "A handler writes the request itself to the log",
        "'{0}' passes the whole request to the logger. Log the values the line actually needs, or turn on " +
        "ObservabilityOptions.LogRequestContents to make it one decision for the whole application.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A request carries whatever the caller sent — an email address, a diagnosis, a bank " +
                     "account, a password typed into the wrong field. The pipeline keeps request contents out " +
                     "of the log unless an application opts in, and a handler that writes the request itself " +
                     "quietly cancels that decision for one request type.",
        helpLinkUri: HelpRoot + "ZERO401");
}
