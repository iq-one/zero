using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Generators.Projection;

/// <summary>Diagnostics reported while generating a specification's selector.</summary>
/// <remarks>
/// Every message names the MEMBER and offers the two ways out — ignore it, or write the
/// selector by hand. A generator that says only "could not map" leaves the reader
/// comparing two type declarations by eye.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.Persistence";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    private static DiagnosticDescriptor Error(
        string id, string title, string message, string description)
        => new(id, title, message, Category, DiagnosticSeverity.Error, true, description, HelpRoot + id);

    public static readonly DiagnosticDescriptor MemberHasNoSource = Error(
        "ZERO220", "A projected member has no source",
        "'{0}.{1}' cannot be projected from '{2}': {3}. Either add it to [Projection(Ignore = [...])] " +
        "if it is filled elsewhere, or write the Selector by hand.",
        "The generator maps member for member by name and refuses to guess. Filling what it can and " +
        "leaving the rest empty would produce a response with a silently absent field, which is the " +
        "mistake this generator exists to catch.");

    public static readonly DiagnosticDescriptor IgnoredMemberDoesNotExist = Error(
        "ZERO221", "An ignored member is not part of the result",
        "[Projection(Ignore = [\"{0}\"])] on '{1}' names something '{2}' does not have. Remove it, or " +
        "correct the spelling.",
        "An ignore entry that matches nothing is a note about a member that has been renamed or removed. " +
        "Left in place it silences nothing and reads as though a real hole were accounted for.");

    public static readonly DiagnosticDescriptor NotASpecification = Error(
        "ZERO222", "[Projection] is on something that is not a specification",
        "'{0}' carries [Projection] but does not derive from Specification<TSource, TResult>. The " +
        "generator reads the two types from that base; without it there is nothing to project.",
        "The attribute takes no type arguments on purpose — the class already names them. On a type that " +
        "names neither, there is no projection to generate.");

    public static readonly DiagnosticDescriptor NotPartial = Error(
        "ZERO223", "A projected specification is not partial",
        "'{0}' carries [Projection], so the generator writes its Selector into a second part of the " +
        "class. Declare it 'partial'.",
        "Generated members are added as another part of the same class. Without the modifier there is " +
        "nowhere to put them.");

    public static readonly DiagnosticDescriptor SelectorAlreadyWritten = Error(
        "ZERO224", "A projected specification already declares its Selector",
        "'{0}' carries [Projection] and also declares Selector itself. Keep the hand-written one and " +
        "remove the attribute, or remove the member and let the generator write it.",
        "Both cannot stand: the generated member would be a duplicate. Which one was meant is the " +
        "author's call, so neither is discarded silently.");
}
