using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Results.Analyzers;

/// <summary>Diagnostics that keep results from being ignored.</summary>
/// <remarks>
/// A result type only pays for itself if the outcome cannot be overlooked. Every rule here
/// exists because the mistake it catches is silent: the code compiles, runs, and quietly
/// treats a failure as a success.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.Results";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    public static readonly DiagnosticDescriptor Discarded = new(
        "ZERO100",
        "A result is discarded",
        "'{0}' returns a result that is never read. Check it with 'if (result.IsFailure)', " +
        "return it to the caller, or match on it.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Discarding a result throws the failure away silently. Nothing logs it, nothing " +
                     "retries it, and the caller continues as if the operation had succeeded.",
        helpLinkUri: HelpRoot + "ZERO100");

    public static readonly DiagnosticDescriptor UncheckedValue = new(
        "ZERO101",
        "A result's value is read without checking the outcome",
        "'{0}' is read without checking whether the operation succeeded. Use 'Match', " +
        "'TryGetValue', or check 'IsSuccess' first.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Reading Value on a failed result throws at runtime, which turns an expected " +
                     "failure back into the exception the result type existed to avoid.",
        helpLinkUri: HelpRoot + "ZERO101");

    public static readonly DiagnosticDescriptor ThrownExpectedFailure = new(
        "ZERO102",
        "An expected failure is thrown instead of returned",
        "'{0}' returns a result but throws '{1}' for a failure it expects. Return an Error instead.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A method that returns a result has promised its failures are values. Throwing " +
                     "one of them means callers must handle failure twice, and will handle it once.",
        helpLinkUri: HelpRoot + "ZERO102");
}
