using System.Collections.Immutable;
using System.Text;
using IQOne.Zero.Generators.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IQOne.Zero.Generators.Mapping;

/// <summary>
/// Writes the selector of a <c>Map&lt;TSource, TDestination&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tree is written AS SOURCE, which is the point of doing this at build time rather than at
/// startup. Two things follow. The accounting is a build error instead of an empty field found
/// in production: every member of the destination is filled by name, filled by a declaration,
/// or declared empty, and there is no fourth case. And the result is a file somebody can open —
/// what a member gets is a line you can go to, not the shape of a tree assembled by reflection.
/// </para>
/// <para>
/// Matching is by NAME and by name only, with narrow type rules: the same type, an implicit
/// widening, or an enum and its underlying number. Everything else asks a question, and the
/// author answers it in <c>Configure</c> — which this generator reads rather than runs.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class MapGenerator : IIncrementalGenerator
{
    private const string MapName = "IQOne.Zero.Mapping.Map`2";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // No marker attribute: the base type already names the pair, and asking for an
        // attribute as well would be a second place to say the same thing.
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => Describe(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(candidates, Emit);
    }

    private static Candidate? Describe(GeneratorSyntaxContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type) return null;

        var map = Base(type);

        if (map is null) return null;

        var location = LocationInfo.From(declaration.Identifier.Parent);

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return Candidate.Failed(type.Name, MapDiagnostics.NotPartial, location, [type.Name]);

        var source = map.TypeArguments[0];
        var destination = map.TypeArguments[1];

        var parameter = "source";
        var declared = new List<Declared>();
        var ignored = new List<string>();
        string? duplicate = null;

        if (Configure(declaration) is { } configure)
        {
            var unreadable = Declarations.Read(
                configure, context.SemanticModel, out parameter, out declared, out ignored, out duplicate);

            if (unreadable is not null)
                return Candidate.Failed(
                    type.Name, MapDiagnostics.ConfigureCannotBeRead, location, [type.Name, unreadable]);
        }

        var members = Assignable(destination);
        var names = new HashSet<string>(members.Select(m => m.Name), StringComparer.Ordinal);

        foreach (var name in ignored.Concat(declared.Select(d => d.Member)))
            if (!names.Contains(name))
                return Candidate.Failed(
                    type.Name, MapDiagnostics.NotASettableMember, location,
                    [name, type.Name, destination.ToDisplayString(), Missing(destination, name)]);

        if (duplicate is not null)
            return Candidate.Failed(
                type.Name, MapDiagnostics.MemberIsAccountedForTwice, location,
                [duplicate, type.Name, "it is named more than once by map.Member or map.Ignore"]);

        var byName = declared.ToDictionary(d => d.Member, d => d.Expression, StringComparer.Ordinal);
        var empty = new HashSet<string>(ignored, StringComparer.Ordinal);

        var assignments = ImmutableArray.CreateBuilder<string>();

        foreach (var member in members)
        {
            if (empty.Contains(member.Name)) continue;

            if (byName.TryGetValue(member.Name, out var expression))
            {
                assignments.Add($"{member.Name} = {expression}");

                continue;
            }

            var reason = Read(source, member, parameter, out var read);

            if (reason is not null)
                return Candidate.Failed(
                    type.Name, MapDiagnostics.MemberIsUnaccountedFor, location,
                    [destination.Name, member.Name, type.Name, reason]);

            assignments.Add($"{member.Name} = {read}");
        }

        return new Candidate(
            Namespace(type),
            type.Name,
            source.ToDisplayString(Full),
            destination.ToDisplayString(Full),
            parameter,
            Usings(declaration),
            new EquatableArray<string>(assignments.ToImmutable()),
            null,
            null,
            location);
    }

    /// <summary>
    /// The using directives in scope where the map was written.
    /// </summary>
    /// <remarks>
    /// A declaration's expression is copied VERBATIM, so it resolves names the way the file it
    /// was written in resolves them. Without these, a cast to a type the author imported —
    /// <c>(EnumBedState)</c> over a <c>using</c> — is an unresolved name in the generated file,
    /// and the error names a file nobody wrote. Aliases and statics come along for the same
    /// reason.
    /// </remarks>
    private static EquatableArray<string> Usings(ClassDeclarationSyntax declaration)
    {
        var found = ImmutableArray.CreateBuilder<string>();

        for (SyntaxNode? node = declaration; node is not null; node = node.Parent)
        {
            var directives = node switch
            {
                CompilationUnitSyntax unit => unit.Usings,
                BaseNamespaceDeclarationSyntax ns => ns.Usings,
                _ => default
            };

            foreach (var directive in directives) found.Add(directive.ToString().Trim());
        }

        return new EquatableArray<string>(found.ToImmutable());
    }

    /// <summary>The closed <c>Map&lt;TSource, TDestination&gt;</c> in the base chain.</summary>
    /// <remarks>
    /// Walked rather than matched on the immediate base, so an application can put its own layer
    /// in between and that layer is still a map of the same two shapes.
    /// </remarks>
    private static INamedTypeSymbol? Base(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var definition = current.OriginalDefinition;

            // MetadataName, because the arity is part of the name.
            var name = definition.ContainingNamespace.IsGlobalNamespace
                ? definition.MetadataName
                : $"{definition.ContainingNamespace.ToDisplayString()}.{definition.MetadataName}";

            if (name == MapName && current.TypeArguments.Length == 2) return current;
        }

        return null;
    }

    private static MethodDeclarationSyntax? Configure(ClassDeclarationSyntax declaration)
        => declaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Configure" && m.ParameterList.Parameters.Count == 1);

    /// <summary>Public instance properties of the destination an initialiser can set.</summary>
    /// <remarks>
    /// Most derived first, and by name: a model that hides a base member with <c>new</c> — a
    /// typed <c>Id</c> over an <c>object</c> one, which is a real shape in ported code — has to
    /// be seen as the typed one, or the assignment the generator writes does not compile.
    /// </remarks>
    private static List<IPropertySymbol> Assignable(ITypeSymbol destination)
    {
        var found = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = destination as INamedTypeSymbol; current is not null; current = current.BaseType)
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer) continue;
                if (property.DeclaredAccessibility != Accessibility.Public) continue;
                if (property.SetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;

                if (seen.Add(property.Name)) found.Add(property);
            }

        return found;
    }

    /// <summary>
    /// The expression that reads this member from the source, or the reason there is none.
    /// </summary>
    private static string? Read(
        ITypeSymbol source, IPropertySymbol member, string parameter, out string expression)
    {
        expression = string.Empty;

        var origin = Readable(source, member.Name);

        if (origin is null) return $"'{source.Name}' has no readable member of that name";

        var from = origin.Type;
        var to = member.Type;

        if (SymbolEqualityComparer.IncludeNullability.Equals(from, to)
            || SymbolEqualityComparer.Default.Equals(from, to)
            || Widens(from, to))
        {
            expression = $"{parameter}.{origin.Name}";

            return null;
        }

        // A nullable value into a non-nullable member is the one mismatch that looks harmless
        // and is not: the fallback is a choice — zero, false, the default enum member — and it
        // belongs where a reader can see it.
        if (IsNullableValue(from) && !IsNullableValue(to) && to.IsValueType)
            return $"'{origin.Name}' is nullable and '{member.Name}' is not; say what an absent " +
                   "value becomes";

        if (CastsBetweenEnumAndNumber(from, to))
        {
            expression = $"({to.ToDisplayString(Full)}){parameter}.{origin.Name}";

            return null;
        }

        return $"'{origin.Name}' is {from.ToDisplayString()} and '{member.Name}' is {to.ToDisplayString()}";
    }

    private static IPropertySymbol? Readable(ITypeSymbol type, string name)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            foreach (var property in current.GetMembers(name).OfType<IPropertySymbol>())
            {
                if (property.IsStatic) continue;

                if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public }) return property;
            }

        return null;
    }

    /// <summary>Why a named member is not one an initialiser can set.</summary>
    private static string Missing(ITypeSymbol destination, string name)
    {
        var property = (destination as INamedTypeSymbol)?
            .GetMembers(name)
            .OfType<IPropertySymbol>()
            .FirstOrDefault();

        if (property is null) return "It has no member of that name; check the spelling.";

        return property.SetMethod is null
            ? "It is read-only, so an initialiser cannot fill it."
            : "Its setter is not public.";
    }

    private static bool IsNullableValue(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static bool Widens(ITypeSymbol from, ITypeSymbol to)
    {
        if (IsNullableValue(to)
            && to is INamedTypeSymbol { TypeArguments.Length: 1 } nullable
            && (SymbolEqualityComparer.Default.Equals(from, nullable.TypeArguments[0])
                || Widens(from, nullable.TypeArguments[0])))
            return true;

        return (from.SpecialType, to.SpecialType) switch
        {
            (SpecialType.System_Byte, SpecialType.System_Int16
                or SpecialType.System_Int32 or SpecialType.System_Int64
                or SpecialType.System_Decimal or SpecialType.System_Double) => true,
            (SpecialType.System_Int16, SpecialType.System_Int32
                or SpecialType.System_Int64 or SpecialType.System_Decimal
                or SpecialType.System_Double) => true,
            (SpecialType.System_Int32, SpecialType.System_Int64
                or SpecialType.System_Decimal or SpecialType.System_Double) => true,
            (SpecialType.System_Single, SpecialType.System_Double) => true,
            _ => false
        };
    }

    private static bool CastsBetweenEnumAndNumber(ITypeSymbol from, ITypeSymbol to)
    {
        var left = Underlying(from);
        var right = Underlying(to);

        if (left is null || right is null) return false;

        return (from.TypeKind == TypeKind.Enum || to.TypeKind == TypeKind.Enum)
            && (left.SpecialType == right.SpecialType || Widens(left, right));
    }

    private static ITypeSymbol? Underlying(ITypeSymbol type)
        => type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying }
            ? underlying
            : type.IsValueType && type.SpecialType != SpecialType.None ? type : null;

    private static string? Namespace(INamedTypeSymbol type)
        => type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();

    private static void Emit(SourceProductionContext context, Candidate candidate)
    {
        if (candidate.Descriptor is { } descriptor)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor, candidate.Location?.ToLocation(), candidate.Arguments!.Value.ToArray()));

            return;
        }

        var b = new StringBuilder();

        b.AppendLine("// <auto-generated/>");
        b.AppendLine("#nullable enable");
        b.AppendLine();

        if (candidate.Namespace is { } ns)
        {
            b.AppendLine($"namespace {ns};");
            b.AppendLine();
        }

        if (candidate.Usings.Count > 0)
        {
            foreach (var directive in candidate.Usings) b.AppendLine(directive);

            b.AppendLine();
        }

        b.AppendLine($"partial class {candidate.TypeName}");
        b.AppendLine("{");

        // Static, so the tree is built once for the type rather than once per instance: a map
        // that is resolved per request would otherwise rebuild it every time.
        b.AppendLine($"    private static readonly global::System.Linq.Expressions.Expression<" +
                     $"global::System.Func<{candidate.SourceType}, {candidate.DestinationType}>> Tree =");
        b.AppendLine($"        {candidate.Parameter} => new {candidate.DestinationType}");
        b.AppendLine("        {");

        foreach (var assignment in candidate.Assignments)
            b.AppendLine($"            {assignment},");

        b.AppendLine("        };");
        b.AppendLine();

        // Lazy, because compiling a tree costs far more than building one and a map used only
        // on queries never needs it.
        b.AppendLine($"    private static readonly global::System.Lazy<" +
                     $"global::System.Func<{candidate.SourceType}, {candidate.DestinationType}>> Compiled =");
        b.AppendLine("        new(Tree.Compile, global::System.Threading.LazyThreadSafetyMode." +
                     "ExecutionAndPublication);");
        b.AppendLine();
        b.AppendLine("    /// <inheritdoc />");
        b.AppendLine($"    public override global::System.Linq.Expressions.Expression<" +
                     $"global::System.Func<{candidate.SourceType}, {candidate.DestinationType}>> " +
                     "Selector => Tree;");
        b.AppendLine();
        b.AppendLine("    /// <inheritdoc />");
        b.AppendLine($"    public override global::System.Func<{candidate.SourceType}, " +
                     $"{candidate.DestinationType}> Project => Compiled.Value;");
        b.AppendLine("}");

        context.AddSource($"{candidate.TypeName}.Map.g.cs", SourceText.From(b.ToString(), Encoding.UTF8));
    }

    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    private sealed record Candidate(
        string? Namespace,
        string TypeName,
        string SourceType,
        string DestinationType,
        string Parameter,
        EquatableArray<string> Usings,
        EquatableArray<string> Assignments,
        DiagnosticDescriptor? Descriptor,
        EquatableArray<string>? Arguments,
        LocationInfo? Location)
    {
        public static Candidate Failed(
            string typeName,
            DiagnosticDescriptor descriptor,
            LocationInfo? location,
            string[] arguments)
            => new(null, typeName, string.Empty, string.Empty, "source", EquatableArray<string>.Empty,
                EquatableArray<string>.Empty, descriptor,
                new EquatableArray<string>(ImmutableArray.Create(arguments)), location);
    }
}
