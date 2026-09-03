using System.Collections.Immutable;
using System.Text;
using IQOne.Zero.Generators.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IQOne.Zero.Generators.Projection;

/// <summary>
/// Writes the <c>Selector</c> of a specification marked <c>[Projection]</c>.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is by NAME and by name only, and every member of the result has to be
/// accounted for. A member with no source is a build error, not an empty field. That
/// asymmetry is the point: the entity is allowed to be wider than the model — most are —
/// but the model is a published response, and a member nobody fills is a column missing
/// from a screen with nothing to explain it.
/// </para>
/// <para>
/// Type rules are deliberately narrow. Same type, an implicit conversion, or an enum and
/// its underlying number: those are the cases where guessing is not guessing. Everything
/// else — a nullable source into a non-nullable member, a nested model, a collection —
/// asks a question the author has to answer, and the diagnostic asks it.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ProjectionGenerator : IIncrementalGenerator
{
    private const string AttributeName = "IQOne.Zero.Persistence.ProjectionAttribute";
    private const string SpecificationName = "IQOne.Zero.Persistence.Specification`2";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => Describe(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(candidates, Emit);
    }

    private static Candidate? Describe(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type) return null;

        var declaration = (ClassDeclarationSyntax)context.TargetNode;
        var location = LocationInfo.From(declaration.Identifier.Parent);

        var ignore = Ignored(context.Attributes[0]);

        var specification = Base(type);

        if (specification is null)
            return Candidate.Failed(type.Name, Diagnostics.NotASpecification, location, [type.Name]);

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return Candidate.Failed(type.Name, Diagnostics.NotPartial, location, [type.Name]);

        if (type.GetMembers("Selector").Any(m => !m.IsImplicitlyDeclared))
            return Candidate.Failed(type.Name, Diagnostics.SelectorAlreadyWritten, location, [type.Name]);

        var source = specification.TypeArguments[0];
        var result = specification.TypeArguments[1];

        var members = Assignable(result);
        var names = new HashSet<string>(members.Select(m => m.Name), StringComparer.Ordinal);

        foreach (var name in ignore)
            if (!names.Contains(name))
                return Candidate.Failed(
                    type.Name, Diagnostics.IgnoredMemberDoesNotExist, location,
                    new[] { name, type.Name, result.ToDisplayString() });

        var assignments = ImmutableArray.CreateBuilder<string>();

        foreach (var member in members)
        {
            if (ignore.Contains(member.Name)) continue;

            var reason = Map(source, member, out var expression);

            if (reason is not null)
                return Candidate.Failed(
                    type.Name, Diagnostics.MemberHasNoSource, location,
                    new[] { result.Name, member.Name, source.ToDisplayString(), reason });

            assignments.Add($"{member.Name} = {expression}");
        }

        return new Candidate(
            Namespace(type),
            type.Name,
            source.ToDisplayString(Full),
            result.ToDisplayString(Full),
            new EquatableArray<string>(assignments.ToImmutable()),
            null,
            null,
            location);
    }

    /// <summary>The closed <c>Specification&lt;TSource, TResult&gt;</c> in the base chain.</summary>
    /// <remarks>
    /// Walked rather than matched on the immediate base: an application often puts its own
    /// layer in between — a base that applies paging and soft-delete rules to every query —
    /// and that base is still a projection of the same two types.
    /// </remarks>
    private static INamedTypeSymbol? Base(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var definition = current.OriginalDefinition;

            // MetadataName, cunku arity adin parcasi: `2. Bir goruntu bicimi generic
            // kismi ya atar ya da tip parametrelerinin ADLARINI yazar; ikisi de
            // Specification<T, TResult> ile Specification<T>'yi ayirt etmeye yetmiyor.
            var name = definition.ContainingNamespace.IsGlobalNamespace
                ? definition.MetadataName
                : $"{definition.ContainingNamespace.ToDisplayString()}.{definition.MetadataName}";

            if (name == SpecificationName && current.TypeArguments.Length == 2) return current;
        }

        return null;
    }

    /// <summary>Public instance properties of the result that an initialiser can set.</summary>
    /// <remarks>
    /// Most derived first, and by name: a model that hides a base member with <c>new</c> —
    /// a typed <c>Id</c> over an <c>object</c> one, which is a real shape in ported code —
    /// must be seen as the typed one. Taking the base member would emit an assignment the
    /// compiler rejects.
    /// </remarks>
    private static List<IPropertySymbol> Assignable(ITypeSymbol result)
    {
        var found = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = result as INamedTypeSymbol; current is not null; current = current.BaseType)
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer) continue;
                if (property.SetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
                if (property.DeclaredAccessibility != Accessibility.Public) continue;

                if (seen.Add(property.Name)) found.Add(property);
            }

        return found;
    }

    /// <summary>
    /// The expression that reads this member from the entity, or the reason there is none.
    /// </summary>
    private static string? Map(ITypeSymbol source, IPropertySymbol target, out string expression)
    {
        expression = string.Empty;

        var origin = Readable(source, target.Name);

        if (origin is null) return $"'{source.Name}' has no readable member of that name";

        var from = origin.Type;
        var to = target.Type;

        if (SymbolEqualityComparer.IncludeNullability.Equals(from, to)
            || SymbolEqualityComparer.Default.Equals(from, to))
        {
            expression = $"e.{origin.Name}";

            return null;
        }

        // A nullable value into a non-nullable member is the one mismatch that looks
        // harmless and is not: the fallback is a choice — zero, false, the default enum
        // member — and it belongs in the projection where a reader can see it.
        if (IsNullableValue(from) && !IsNullableValue(to) && to.IsValueType)
            return $"'{origin.Name}' is nullable and '{target.Name}' is not; say what an absent value becomes";

        if (Widens(from, to))
        {
            expression = $"e.{origin.Name}";

            return null;
        }

        if (CastsBetweenEnumAndNumber(from, to))
        {
            expression = $"({to.ToDisplayString(Full)})e.{origin.Name}";

            return null;
        }

        return $"'{origin.Name}' is {from.ToDisplayString()} and '{target.Name}' is {to.ToDisplayString()}";
    }

    private static IPropertySymbol? Readable(ITypeSymbol source, string name)
    {
        for (var current = source as INamedTypeSymbol; current is not null; current = current.BaseType)
            foreach (var property in current.GetMembers(name).OfType<IPropertySymbol>())
                if (!property.IsStatic
                    && property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
                    return property;

        return null;
    }

    private static bool IsNullableValue(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary>Whether the language converts one to the other without being asked.</summary>
    /// <remarks>
    /// Only the widening numeric conversions and value-to-nullable. Narrowing is excluded
    /// even where a cast would compile: <c>int</c> into <c>short</c> silently wraps.
    /// </remarks>
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

    /// <summary>Whether an explicit cast between the two is lossless and translatable.</summary>
    /// <remarks>
    /// An enum is stored as its underlying number, so a cast either way is the same bits and
    /// the provider translates it. Two different enums are allowed only when their underlying
    /// types match — otherwise the cast narrows.
    /// </remarks>
    private static bool CastsBetweenEnumAndNumber(ITypeSymbol from, ITypeSymbol to)
    {
        var left = Underlying(from);
        var right = Underlying(to);

        if (left is null || right is null) return false;

        return from.TypeKind == TypeKind.Enum || to.TypeKind == TypeKind.Enum
            ? left.SpecialType == right.SpecialType || Widens(left, right)
            : false;
    }

    private static ITypeSymbol? Underlying(ITypeSymbol type)
        => type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying }
            ? underlying
            : type.IsValueType && type.SpecialType != SpecialType.None ? type : null;

    private static ImmutableHashSet<string> Ignored(AttributeData attribute)
    {
        foreach (var argument in attribute.NamedArguments)
            if (argument.Key == "Ignore" && argument.Value.Kind == TypedConstantKind.Array)
                return
                [
                    .. argument.Value.Values
                        .Select(v => v.Value as string)
                        .Where(v => v is not null)
                        .Select(v => v!)
                ];

        return [];
    }

    private static string? Namespace(INamedTypeSymbol type)
        => type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();

    private static void Emit(SourceProductionContext context, Candidate candidate)
    {
        if (candidate.Descriptor is { } descriptor)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                descriptor,
                candidate.Location?.ToLocation(),
                candidate.Arguments!.Value.ToArray()));

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

        b.AppendLine($"partial class {candidate.TypeName}");
        b.AppendLine("{");
        b.AppendLine("    /// <summary>Generated from the two types this specification names.</summary>");
        b.AppendLine($"    public override global::System.Linq.Expressions.Expression<" +
                     $"global::System.Func<{candidate.SourceType}, {candidate.ResultType}>> Selector =>");
        b.AppendLine($"        e => new {candidate.ResultType}");
        b.AppendLine("        {");

        foreach (var assignment in candidate.Assignments)
            b.AppendLine($"            {assignment},");

        b.AppendLine("        };");
        b.AppendLine("}");

        context.AddSource($"{candidate.TypeName}.Projection.g.cs", SourceText.From(b.ToString(), Encoding.UTF8));
    }

    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    private sealed record Candidate(
        string? Namespace,
        string TypeName,
        string SourceType,
        string ResultType,
        EquatableArray<string> Assignments,
        DiagnosticDescriptor? Descriptor,
        EquatableArray<string>? Arguments,
        LocationInfo? Location)
    {
        public static Candidate Failed(
            string typeName, DiagnosticDescriptor descriptor, LocationInfo? location, string[] arguments)
            => new(null, typeName, string.Empty, string.Empty, EquatableArray<string>.Empty,
                descriptor, new EquatableArray<string>(ImmutableArray.Create(arguments)), location);
    }
}
