using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Generators.Mapping;

/// <summary>Diagnostics reported while generating a member-for-member mapping.</summary>
internal static class Diagnostics
{
    private const string Category = "Zero.Persistence";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    private static DiagnosticDescriptor Error(
        string id, string title, string message, string description)
        => new(id, title, message, Category, DiagnosticSeverity.Error, true, description, HelpRoot + id);

    public static readonly DiagnosticDescriptor MemberIsNotWritten = Error(
        "ZERO225", "A mapped member is not written anywhere",
        "'{0}.{1}' cannot be written to '{2}': {3}. Either add it to [Mapping(Ignore = [...])] " +
        "if it is deliberately not written, or write the method by hand.",
        "The SOURCE is what a mapping has to account for: a member the caller sent and nothing " +
        "consumed is a field that was silently discarded, and the request looks like it worked. The " +
        "generator maps by name and refuses to guess.");

    public static readonly DiagnosticDescriptor MemberHasNoSource = Error(
        "ZERO229", "A produced member has no source",
        "'{0}.{1}' cannot be read from '{2}': {3}. Either add it to [Mapping(Ignore = [...])] " +
        "if something else fills it, or write the method by hand.",
        "A mapping that PRODUCES an object holds the result to account, as a projection does: the " +
        "object being constructed is what a caller receives, and a member nobody fills is an absent " +
        "field with nothing in the code to explain it. The shape decides which end is checked — what " +
        "you construct must be complete, what you consume must be consumed.");

    public static readonly DiagnosticDescriptor IgnoredMemberDoesNotExist = Error(
        "ZERO226", "An ignored member is not part of the source",
        "[Mapping(Ignore = [\"{0}\"])] on '{1}' names something '{2}' does not have. Remove it, or " +
        "correct the spelling.",
        "An ignore entry that matches nothing accounts for no member while reading as though a real " +
        "omission were accounted for. It is usually left behind by a rename. Which type it must name " +
        "follows the shape: the source when the method writes onto a target, the result when it " +
        "produces one.");

    public static readonly DiagnosticDescriptor WrongShape = Error(
        "ZERO227", "A mapping method has the wrong shape",
        "'{0}' carries [Mapping], so it must be one of two shapes: " +
        "'static partial void M(TSource source, TTarget target)' to write onto something that " +
        "already exists, or 'static partial TResult M(TSource source)' to produce a new object. {1}",
        "The signature is where the generator reads the types, so its shape is the declaration — and " +
        "the shape also decides which end is held to account: what you construct must be complete, " +
        "what you consume must be consumed.");

    public static readonly DiagnosticDescriptor ContainerNotPartial = Error(
        "ZERO228", "The type holding a mapping is not partial",
        "'{0}' holds a [Mapping] method, so the generator writes its body into a second part of the " +
        "type. Declare '{0}' 'partial'.",
        "A generated implementation is another part of the same type. Without the modifier there is " +
        "nowhere to put it.");
}
