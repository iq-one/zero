using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Caching.Analyzers;

/// <summary>Diagnostics that keep a cache from answering the wrong question.</summary>
/// <remarks>
/// A cache is the one place where being wrong looks exactly like being fast. Both rules here
/// exist because the mistake they catch produces a plausible answer: the wrong caller's
/// answer, or an answer to a request that should never have been repeated at all.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.Caching";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    public static readonly DiagnosticDescriptor NotAQuery = new(
        "ZERO210",
        "A cacheable request is not a query",
        "'{0}' implements ICacheable but is not an IQuery<T>. Only a query may be cached; make it a query, " +
        "or remove ICacheable.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "A command changes something, so serving it from a cache would skip the change. The " +
                     "pipeline throws when it meets one, which is late; this reports it where it was written.",
        helpLinkUri: HelpRoot + "ZERO210");

    public static readonly DiagnosticDescriptor ConstantKey = new(
        "ZERO211",
        "A cacheable query's key ignores what it was asked",
        "'{0}' takes parameters but its CacheKey is a constant, so every call shares one answer. Build the " +
        "key from the values the answer depends on.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A key that leaves out a parameter hands one caller's answer to the next. Nothing fails " +
                     "and nothing is logged — the answer is simply for a question nobody asked.",
        helpLinkUri: HelpRoot + "ZERO211");
}
