using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Resilience.Analyzers;

/// <summary>Diagnostics that keep retrying where the pipeline can see it.</summary>
/// <remarks>
/// One rule, because there is only one mistake here with no runtime symptom. A hand-written
/// retry loop works: it compiles, it passes its test, and it quietly retries inside the
/// transaction the pipeline opened around it, on a request nobody has said is safe to
/// repeat. Nothing fails until the day it repeats something that mattered.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.Resilience";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    public static readonly DiagnosticDescriptor HandRolledRetry = new(
        "ZERO600",
        "A handler waits and tries again by hand",
        "'{0}' loops around a wait and tries again. Delete the loop and add IQOne.Zero.Resilience: it retries " +
        "on ErrorKind.Unavailable, backs off with jitter, and does it outside the transaction. If the request " +
        "is a command, mark it IIdempotent first.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A retry written inside a handler runs inside whatever the pipeline has already opened " +
                     "around it — a transaction the first failure may have poisoned, a span that now covers " +
                     "three attempts — and nothing checks that the request is safe to repeat.",
        helpLinkUri: HelpRoot + "ZERO600");
}
