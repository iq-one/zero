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

    public static readonly DiagnosticDescriptor IgnoredMemberDoesNotExist = Error(
        "ZERO226", "An ignored member is not part of the source",
        "[Mapping(Ignore = [\"{0}\"])] on '{1}' names something '{2}' does not have. Remove it, or " +
        "correct the spelling.",
        "An ignore entry that matches nothing accounts for no member while reading as though a real " +
        "omission were accounted for. It is usually left behind by a rename.");

    public static readonly DiagnosticDescriptor WrongShape = Error(
        "ZERO227", "A mapping method has the wrong shape",
        "'{0}' carries [Mapping], so it must be 'static partial void' with exactly two parameters — " +
        "the source first, the target second. {1}",
        "The signature is where the generator reads the two types, so its shape is the declaration. " +
        "A mapping that returned something, or took one object, would be a different operation.");

    public static readonly DiagnosticDescriptor ContainerNotPartial = Error(
        "ZERO228", "The type holding a mapping is not partial",
        "'{0}' holds a [Mapping] method, so the generator writes its body into a second part of the " +
        "type. Declare '{0}' 'partial'.",
        "A generated implementation is another part of the same type. Without the modifier there is " +
        "nowhere to put it.");
}
