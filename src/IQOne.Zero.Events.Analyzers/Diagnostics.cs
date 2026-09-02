using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Events.Analyzers;

/// <summary>Diagnostics that keep fan-out from turning into something else.</summary>
/// <remarks>
/// All three catch mistakes with no reliable runtime symptom. A cycle kills the process with
/// a stack overflow that cannot be caught and writes no log line; a mutable event works
/// perfectly until a second subscriber appears; a query that publishes stops publishing the
/// day someone caches it. None of them fails where it was written, which is what earns them
/// a place in the compiler's output.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.Events";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    public static readonly DiagnosticDescriptor PublishCycle = new(
        "ZERO500",
        "Publishing an event that leads back to the one being handled",
        "'{0}' handles '{1}' and publishes '{2}', which leads back to '{1}' ({3}). Break the cycle: do the " +
        "second piece of work in this subscriber, or publish from the command instead of from a subscriber.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Publishing is awaited in-process, so a cycle recurses until the stack overflows — which " +
                     "cannot be caught, writes no log line, and inside a transaction leaves the database " +
                     "holding locks. MaxPublishDepth turns it into an exception; this turns it into a build error.",
        helpLinkUri: HelpRoot + "ZERO500");

    public static readonly DiagnosticDescriptor MutableEvent = new(
        "ZERO501",
        "An event can be changed after it is published",
        "'{0}.{1}' can be assigned after '{0}' is published. Subscribers run one after another over the same " +
        "instance, so this is a channel between them; make it get-only or init-only.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An event states what happened. A subscriber that changes it changes what the later " +
                     "subscribers are told, in an order the framework does not define — so the application " +
                     "behaves one way with one subscriber and another way with two.",
        helpLinkUri: HelpRoot + "ZERO501");

    public static readonly DiagnosticDescriptor PublishFromQuery = new(
        "ZERO502",
        "A query handler publishes an event",
        "'{0}' handles a query and publishes '{1}'. A query changes nothing and may be cached or retried; " +
        "move the publish to the command that made the fact true.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The pipeline is allowed to serve a query from a cache and to retry it. Either one " +
                     "silently changes how many times the event is published — twice, or never — and neither " +
                     "leaves a symptom that points back at this line.",
        helpLinkUri: HelpRoot + "ZERO502");
}
