using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Generators.Mapping;

/// <summary>Diagnostics reported while writing a map.</summary>
internal static class MapDiagnostics
{
    private const string Category = "Zero.Mapping";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    private static DiagnosticDescriptor Error(
        string id, string title, string message, string description)
        => new(id, title, message, Category, DiagnosticSeverity.Error, true, description, HelpRoot + id);

    public static readonly DiagnosticDescriptor MemberIsUnaccountedFor = Error(
        "ZERO250", "A mapped member is unaccounted for",
        "'{0}.{1}' is unaccounted for in '{2}': {3}. Fill it with map.Member(...), or say it is " +
        "deliberately left empty with map.Ignore(...).",
        "The shape being produced is what a caller receives, so every member of it has to have " +
        "an answer. A member nobody fills is an absent field on a screen with nothing in the code " +
        "to explain it — and the failure arrives in production rather than at the build. Matching " +
        "is by name and by name only; anything else is a decision, and this rule asks for it.");

    public static readonly DiagnosticDescriptor MemberCannotBeFilled = Error(
        "ZERO251", "A declared member cannot fill its target",
        "map.Member for '{0}.{1}' cannot be used: {2}.",
        "The expression is copied into the tree the generator writes, so it has to fit the member " +
        "it fills. A mismatch here would be a compile error inside a generated file, which names " +
        "the wrong place; reported at the declaration it names the line you wrote.");

    public static readonly DiagnosticDescriptor NotASettableMember = Error(
        "ZERO252", "A declaration does not name a settable member",
        "'{0}' in '{1}' does not name a settable member of '{2}'. {3}",
        "Both map.Member and map.Ignore name a member of the shape being produced, and it must be " +
        "one an initialiser can set. A computed or read-only member cannot be filled, and naming " +
        "one usually means the wrong end was named — the member WRITTEN, not the member the " +
        "expression reads.");

    public static readonly DiagnosticDescriptor MemberIsAccountedForTwice = Error(
        "ZERO253", "A member is accounted for twice",
        "'{0}' is accounted for twice in '{1}' — {2}. Keep the one that is true.",
        "A member gets one answer. Ignore leaves it empty; Member fills it; two of either say it " +
        "twice. Honouring one would leave the other a lie sitting in the source, and which one won " +
        "would depend on the order they happen to be read in.");

    public static readonly DiagnosticDescriptor NotPartial = Error(
        "ZERO258", "A map is not partial",
        "'{0}' is a map, so the generator writes its selector into a second part of the type. " +
        "Declare '{0}' 'partial'.",
        "A generated selector is another part of the same type. Without the modifier there is " +
        "nowhere to put it.");

    public static readonly DiagnosticDescriptor ConfigureCannotBeRead = Error(
        "ZERO256", "A map's configuration cannot be read",
        "'{0}' declares something the generator cannot read: {1}. Configure is read from source, " +
        "not run, so it has to be a plain sequence of map.Member and map.Ignore calls.",
        "The generator takes what Configure declares and writes the tree from it, which means it " +
        "reads the source rather than executing it. A body that decides at run time what to map — " +
        "a loop, a condition, a value from somewhere else — has no single answer to read, and " +
        "guessing at one would produce a map nobody declared. Reported rather than skipped, " +
        "because a declaration silently ignored is the failure this whole design exists to remove.");

    public static readonly DiagnosticDescriptor CompositionCycles = Error(
        "ZERO254", "A composition goes round in a circle",
        "'{0}' composes in a circle: {1}. Break it with map.Ignore on one of the members.",
        "A map written out as source cannot contain itself, so a circle has no output at all — " +
        "and the shapes that make one are ordinary: a department that lists its beds, a bed that " +
        "names its department. A runtime mapper meets the same circle as a stack overflow, or " +
        "stops at a depth limit nobody chose and silently truncates the result. Here it is a " +
        "build error that names the loop, and breaking it is a decision somebody writes down.");

    public static readonly DiagnosticDescriptor NoMapForThePair = Error(
        "ZERO255", "A composition names a pair with no map",
        "'{0}' composes '{1}' into '{2}' but no map is declared for that pair. Declare one: " +
        "'public sealed partial class {3} : Map<{4}, {5}>'.",
        "Composition writes the other map's tree in place of the member, so that map has to be " +
        "readable from here — which means declared in this compilation. A pair with nothing " +
        "declared for it has no tree to write.");
}
