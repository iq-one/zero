using Microsoft.CodeAnalysis;

namespace IQOne.Zero.BackgroundWork.Analyzers;

/// <summary>Diagnostics for work that runs on a clock rather than on a request.</summary>
/// <remarks>
/// Both rules here catch mistakes whose symptom appears somewhere other than the job: a gap
/// in reconciled data, or a shutdown that takes thirty seconds. Neither points back at the
/// line that caused it, which is what makes them worth a compiler warning.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.BackgroundWork";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    public static readonly DiagnosticDescriptor ReadsTheClock = new(
        "ZERO550",
        "A job reads the clock instead of the occurrence it is serving",
        "'{0}' reads the current time. Use 'context.ScheduledFor' so the run covers the " +
        "occurrence it was scheduled for, not the moment it happened to start.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A run that takes its window from the clock leaves a gap the size of its own " +
                     "start-up delay, every time. One that takes it from the occurrence it serves does not.",
        helpLinkUri: HelpRoot + "ZERO550");

    public static readonly DiagnosticDescriptor IgnoresCancellation = new(
        "ZERO551",
        "A job ignores the cancellation token",
        "'{0}' never uses its cancellation token. Pass it to everything awaited, or the run " +
        "holds up shutdown until the host kills it mid-work.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The token is cancelled when the application is stopping. A run that ignores it is " +
                     "terminated part-way through instead of finishing or declining to start.",
        helpLinkUri: HelpRoot + "ZERO551");
}
