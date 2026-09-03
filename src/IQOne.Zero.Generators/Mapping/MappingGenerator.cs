using System.Collections.Immutable;
using System.Text;
using IQOne.Zero.Generators.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IQOne.Zero.Generators.Mapping;

/// <summary>
/// Writes the body of a partial method marked <c>[Mapping]</c>.
/// </summary>
/// <remarks>
/// <para>
/// The SOURCE is held to account, which is the difference from a projection. A projection
/// produces the shape it is asked for, so that shape must be complete; a mapping writes
/// onto something that already exists, and there the danger is the other way round — a
/// member the caller sent that nothing consumed, discarded without a word.
/// </para>
/// <para>
/// The target is allowed to have more: its key, its audit columns, whatever a convention
/// fills. Only what arrived has to be answered for.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class MappingGenerator : IIncrementalGenerator
{
    private const string AttributeName = "IQOne.Zero.Persistence.MappingAttribute";
    private const string EntityName = "IQOne.Zero.Persistence.IEntity`1";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => Describe(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        context.RegisterSourceOutput(candidates, Emit);
    }

    private static Candidate? Describe(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IMethodSymbol method) return null;

        var declaration = (MethodDeclarationSyntax)context.TargetNode;
        var location = LocationInfo.From(declaration.Identifier.Parent);
        var container = method.ContainingType;

        var wrong = Shape(method, declaration);

        if (wrong is not null)
            return Candidate.Failed(
                container, method.Name, Diagnostics.WrongShape, location,
                new[] { method.Name, wrong });

        if (!container.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(t => t.Modifiers.Any(SyntaxKind.PartialKeyword)))
            return Candidate.Failed(
                container, method.Name, Diagnostics.ContainerNotPartial, location,
                new[] { container.Name });

        var source = method.Parameters[0];
        var target = method.Parameters[1];

        var ignore = Ignored(context.Attributes[0]);

        var members = Readable(source.Type);
        var names = new HashSet<string>(members.Select(m => m.Name), StringComparer.Ordinal);

        foreach (var name in ignore)
            if (!names.Contains(name))
                return Candidate.Failed(
                    container, method.Name, Diagnostics.IgnoredMemberDoesNotExist, location,
                    new[] { name, method.Name, source.Type.ToDisplayString() });

        var key = Key(target.Type);
        var assignments = ImmutableArray.CreateBuilder<string>();

        foreach (var member in members)
        {
            if (ignore.Contains(member.Name)) continue;

            // The key is how the row was found. Assigning it from the caller's object is a
            // no-op at best and a different row at worst, so it is skipped without asking —
            // recognised through IEntity<TKey>, not by its name.
            if (member.Name == key) continue;

            var reason = Map(target.Type, member, out var assignment);

            if (reason is not null)
                return Candidate.Failed(
                    container, method.Name, Diagnostics.MemberIsNotWritten, location,
                    new[] { source.Type.Name, member.Name, target.Type.ToDisplayString(), reason });

            assignments.Add(assignment);
        }

        return new Candidate(
            Namespace(container),
            container.Name,
            Keyword(container),
            Access(method),
            method.Name,
            source.Type.ToDisplayString(Full),
            target.Type.ToDisplayString(Full),
            source.Name,
            target.Name,
            new EquatableArray<string>(assignments.ToImmutable()),
            null,
            null,
            location);
    }

    /// <summary>What is wrong with the signature, or null when nothing is.</summary>
    private static string? Shape(IMethodSymbol method, MethodDeclarationSyntax declaration)
    {
        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword)) return "It is not partial.";
        if (!method.IsStatic) return "It is not static.";
        if (!method.ReturnsVoid) return $"It returns {method.ReturnType.ToDisplayString()}.";
        if (method.Parameters.Length != 2) return $"It takes {method.Parameters.Length} parameters.";

        foreach (var parameter in method.Parameters)
            if (parameter.RefKind != RefKind.None)
                return $"'{parameter.Name}' is passed by reference; both objects are passed by value.";

        return null;
    }

    /// <summary>The name of the target's key, or null when it declares none.</summary>
    private static string? Key(ITypeSymbol target)
    {
        foreach (var contract in (target as INamedTypeSymbol)?.AllInterfaces ?? [])
        {
            var definition = contract.OriginalDefinition;

            var name = definition.ContainingNamespace.IsGlobalNamespace
                ? definition.MetadataName
                : $"{definition.ContainingNamespace.ToDisplayString()}.{definition.MetadataName}";

            if (name == EntityName) return "Id";
        }

        return null;
    }

    private static List<IPropertySymbol> Readable(ITypeSymbol type)
    {
        var found = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer) continue;
                if (property.DeclaredAccessibility != Accessibility.Public) continue;
                if (property.GetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;

                if (seen.Add(property.Name)) found.Add(property);
            }

        return found;
    }

    /// <summary>The assignment that writes this member, or the reason there is none.</summary>
    private static string? Map(ITypeSymbol target, IPropertySymbol member, out string assignment)
    {
        assignment = string.Empty;

        var destination = Writable(target, member.Name);

        if (destination is null) return $"'{target.Name}' has no settable member of that name";

        var from = member.Type;
        var to = destination.Type;

        if (SymbolEqualityComparer.Default.Equals(from, to))
        {
            assignment = $"{{1}}.{destination.Name} = {{0}}.{member.Name}";

            return null;
        }

        if (IsNullableValue(from) && !IsNullableValue(to) && to.IsValueType)
            return $"'{member.Name}' is nullable and '{destination.Name}' is not; say what an absent value writes";

        if (Widens(from, to))
        {
            assignment = $"{{1}}.{destination.Name} = {{0}}.{member.Name}";

            return null;
        }

        if (CastsBetweenEnumAndNumber(from, to))
        {
            assignment = $"{{1}}.{destination.Name} = ({to.ToDisplayString(Full)}){{0}}.{member.Name}";

            return null;
        }

        return $"'{member.Name}' is {from.ToDisplayString()} and '{destination.Name}' is {to.ToDisplayString()}";
    }

    private static IPropertySymbol? Writable(ITypeSymbol target, string name)
    {
        for (var current = target as INamedTypeSymbol; current is not null; current = current.BaseType)
            foreach (var property in current.GetMembers(name).OfType<IPropertySymbol>())
                if (!property.IsStatic
                    && property.SetMethod is { DeclaredAccessibility: Accessibility.Public })
                    return property;

        return null;
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

    private static string Keyword(INamedTypeSymbol type)
        => type.IsRecord ? "record" : type.TypeKind == TypeKind.Struct ? "struct" : "class";

    /// <summary>The declaration's accessibility, which the implementation has to repeat.</summary>
    /// <remarks>
    /// Both parts of a partial member must agree (CS8799), so the generated half cannot
    /// simply leave it off — and leaving it off is what a reader of the generator would
    /// expect to work, since the declaration is right there.
    /// </remarks>
    private static string Access(IMethodSymbol method) => method.DeclaredAccessibility switch
    {
        Accessibility.Public => "public ",
        Accessibility.Internal => "internal ",
        Accessibility.Protected => "protected ",
        Accessibility.ProtectedOrInternal => "protected internal ",
        Accessibility.ProtectedAndInternal => "private protected ",
        Accessibility.Private => "private ",
        _ => string.Empty
    };

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

        b.AppendLine($"partial {candidate.Keyword} {candidate.TypeName}");
        b.AppendLine("{");
        b.AppendLine("    /// <summary>Generated from the two types this method names.</summary>");
        b.AppendLine($"    {candidate.Access}static partial void {candidate.MethodName}(" +
                     $"{candidate.SourceType} {candidate.SourceName}, " +
                     $"{candidate.TargetType} {candidate.TargetName})");
        b.AppendLine("    {");

        foreach (var assignment in candidate.Assignments)
            b.AppendLine($"        {string.Format(assignment, candidate.SourceName, candidate.TargetName)};");

        b.AppendLine("    }");
        b.AppendLine("}");

        context.AddSource(
            $"{candidate.TypeName}.{candidate.MethodName}.Mapping.g.cs",
            SourceText.From(b.ToString(), Encoding.UTF8));
    }

    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    private sealed record Candidate(
        string? Namespace,
        string TypeName,
        string Keyword,
        string Access,
        string MethodName,
        string SourceType,
        string TargetType,
        string SourceName,
        string TargetName,
        EquatableArray<string> Assignments,
        DiagnosticDescriptor? Descriptor,
        EquatableArray<string>? Arguments,
        LocationInfo? Location)
    {
        public static Candidate Failed(
            INamedTypeSymbol container,
            string methodName,
            DiagnosticDescriptor descriptor,
            LocationInfo? location,
            string[] arguments)
            => new(null, container.Name, "class", string.Empty, methodName, string.Empty, string.Empty,
                "source", "target", EquatableArray<string>.Empty,
                descriptor, new EquatableArray<string>(ImmutableArray.Create(arguments)), location);
    }
}
