using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Authorization.Analyzers;

/// <summary>Diagnostics that keep an authorization decision from being made by omission.</summary>
/// <remarks>
/// Each of these catches a mistake with no runtime symptom that points at its cause. A
/// request nobody annotated is refused at run time with no line number; a request carrying
/// both attributes is served to anyone, and looks protected in the source. The compiler is
/// the only place either one is cheap to find.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.Authorization";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    public static readonly DiagnosticDescriptor Undeclared = new(
        "ZERO450",
        "A request declares no authorization",
        "'{0}' does not say who may make it. Name a policy — on [Authorize], or on the route " +
        "attribute if it has one — or say AllowAnonymous.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A request whose permissions nobody wrote down is refused at run time, with " +
                     "nothing in the source to explain why. Deciding here costs one attribute; " +
                     "discovering it in production costs an incident.",
        helpLinkUri: HelpRoot + "ZERO450");

    public static readonly DiagnosticDescriptor Contradictory = new(
        "ZERO451",
        "A request is both authorized and anonymous",
        "'{0}' carries both [Authorize] and [AllowAnonymous]. [AllowAnonymous] wins, so the " +
        "[Authorize] does nothing; remove whichever one is wrong.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The request is reachable by anyone while reading as though it were protected. " +
                     "Nothing at run time reports the contradiction, because both attributes are " +
                     "individually valid.",
        helpLinkUri: HelpRoot + "ZERO451");

    public static readonly DiagnosticDescriptor NotARequest = new(
        "ZERO452",
        "An authorization attribute is on something that is not a request",
        "'{0}' carries an authorization attribute but is not a command or a query. Nothing " +
        "reads it; remove the attribute, or make the type a request.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Authorization is applied by the pipeline behaviour, which only ever sees " +
                     "requests. On anything else the attribute compiles and then protects nothing.",
        helpLinkUri: HelpRoot + "ZERO452");
}
