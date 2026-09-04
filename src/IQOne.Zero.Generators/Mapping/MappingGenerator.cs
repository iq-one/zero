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
    private const string MapMemberName = "IQOne.Zero.Persistence.MapMemberAttribute";

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

        var produces = !method.ReturnsVoid;

        var source = method.Parameters[0];
        var targetType = produces ? method.ReturnType : method.Parameters[1].Type;
        var targetName = produces ? "result" : method.Parameters[1].Name;

        var ignore = Ignored(context.Attributes[0]);
        var (custom, duplicate) = Custom(method);

        if (duplicate is not null)
            return Candidate.Failed(
                container, method.Name, Diagnostics.MemberIsAccountedForTwice, location,
                new[] { duplicate, method.Name, "two [MapMember] attributes name it" });

        // Constructing: the RESULT is held to account, so its settable members are the list.
        // Writing onto an existing object: the SOURCE is, so its readable members are.
        var members = produces ? Assignable(targetType) : Readable(source.Type);
        var accounted = produces ? targetType : source.Type;

        var names = new HashSet<string>(members.Select(m => m.Name), StringComparer.Ordinal);

        foreach (var name in ignore)
            if (!names.Contains(name))
                return Candidate.Failed(
                    container, method.Name, Diagnostics.IgnoredMemberDoesNotExist, location,
                    new[] { name, method.Name, accounted.ToDisplayString() });

        foreach (var name in custom.Keys)
        {
            if (!names.Contains(name))
                return Candidate.Failed(
                    container, method.Name, Diagnostics.CustomMemberDoesNotExist, location,
                    new[] { name, method.Name, accounted.ToDisplayString() });

            if (ignore.Contains(name))
                return Candidate.Failed(
                    container, method.Name, Diagnostics.MemberIsAccountedForTwice, location,
                    new[] { name, method.Name, Contradiction });
        }

        // The key is skipped only when writing onto an existing row: there it is how the row
        // was found, and assigning it from the caller's object is a no-op at best and a
        // different row at worst. When CONSTRUCTING, the key is part of what is produced and
        // leaving it out would be an incomplete object.
        var key = produces ? null : Key(targetType);

        var assignments = ImmutableArray.CreateBuilder<string>();

        foreach (var member in members)
        {
            if (ignore.Contains(member.Name)) continue;

            if (custom.TryGetValue(member.Name, out var handler))
            {
                var unusable = Handler(
                    container, method, handler, produces, method.IsStatic, source.Type, targetType,
                    member, out var call);

                if (unusable is not null)
                    return Candidate.Failed(
                        container, method.Name, Diagnostics.CustomMethodIsUnusable, location,
                        new[] { member.Name, handler, method.Name, unusable, Expected(handler, produces, method.IsStatic, source.Type, targetType, member) });

                assignments.Add(call);

                continue;
            }

            // The key is skipped only when nothing was said about it: naming it in [MapMember]
            // is somebody saying it on purpose, and refusing that would be the generator
            // overruling a decision it exists to make room for.
            if (member.Name == key) continue;

            var reason = produces
                ? Read(source.Type, member, out var assignment)
                : Write(targetType, member, out assignment);

            if (reason is not null)
                return Candidate.Failed(
                    container, method.Name,
                    produces ? Diagnostics.MemberHasNoSource : Diagnostics.MemberIsNotWritten,
                    location,
                    new[] { accounted.Name, member.Name, (produces ? source.Type : targetType).ToDisplayString(), reason });

            assignments.Add(assignment);
        }

        return new Candidate(
            Namespace(container),
            container.Name,
            Keyword(container),
            Access(method),
            method.Name,
            source.Type.ToDisplayString(Annotated),
            targetType.ToDisplayString(Annotated),
            targetType.ToDisplayString(Full),
            source.Name,
            targetName,
            produces,
            method.IsStatic,
            new EquatableArray<string>(assignments.ToImmutable()),
            null,
            null,
            location);
    }

    /// <summary>What is wrong with the signature, or null when nothing is.</summary>
    /// <remarks>
    /// Two shapes, and the shape decides the rule:
    /// <list type="bullet">
    ///   <item>
    ///     <c>void M(TSource source, TTarget target)</c> — writes onto something that
    ///     already exists. The SOURCE must be accounted for.
    ///   </item>
    ///   <item>
    ///     <c>TResult M(TSource source)</c> — produces a new object. The RESULT must be
    ///     accounted for.
    ///   </item>
    /// </list>
    /// One sentence covers both: what you construct must be complete, what you consume must
    /// be consumed. Anything else — two parameters and a return, no parameters, a ref — is a
    /// different operation and is reported rather than guessed at.
    /// </remarks>
    private static string? Shape(IMethodSymbol method, MethodDeclarationSyntax declaration)
    {
        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword)) return "It is not partial.";

        foreach (var parameter in method.Parameters)
            if (parameter.RefKind != RefKind.None)
                return $"'{parameter.Name}' is passed by reference; objects are passed by value.";

        if (method.ReturnsVoid)
            return method.Parameters.Length == 2
                ? null
                : $"It returns nothing, so it writes onto a target — and takes " +
                  $"{method.Parameters.Length} parameters instead of two.";

        return method.Parameters.Length == 1
            ? null
            : $"It returns {method.ReturnType.ToDisplayString()}, so it produces a new object — and " +
              $"takes {method.Parameters.Length} parameters instead of one.";
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

    /// <summary>Public instance properties an initialiser can set.</summary>
    private static List<IPropertySymbol> Assignable(ITypeSymbol type)
    {
        var found = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
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
    /// The initialiser entry that fills this member of the produced object, or the reason
    /// there is none.
    /// </summary>
    private static string? Read(ITypeSymbol source, IPropertySymbol member, out string assignment)
    {
        assignment = string.Empty;

        var origin = Property(source, member.Name, wanted: false);

        if (origin is null) return $"'{source.Name}' has no readable member of that name";

        var reason = Convert(origin.Type, member.Type, out var cast);

        if (reason is not null) return string.Format(reason, origin.Name, member.Name);

        assignment = $"{member.Name} = {cast}{{0}}.{origin.Name}";

        return null;
    }

    /// <summary>The assignment that writes this member, or the reason there is none.</summary>
    private static string? Write(ITypeSymbol target, IPropertySymbol member, out string assignment)
    {
        assignment = string.Empty;

        var destination = Property(target, member.Name, wanted: true);

        if (destination is null) return $"'{target.Name}' has no settable member of that name";

        var reason = Convert(member.Type, destination.Type, out var cast);

        if (reason is not null) return string.Format(reason, member.Name, destination.Name);

        assignment = $"{{1}}.{destination.Name} = {cast}{{0}}.{member.Name}";

        return null;
    }

    /// <summary>
    /// The cast one type needs to become the other, or the reason it cannot.
    /// </summary>
    /// <remarks>
    /// Shared by both directions, because the question is the same either way: is this a
    /// conversion the language makes, or a decision somebody has to write down? The reason
    /// is a format string — <c>{0}</c> the member read from, <c>{1}</c> the member written
    /// to — so each direction names them in its own order.
    /// </remarks>
    private static string? Convert(ITypeSymbol from, ITypeSymbol to, out string cast)
    {
        cast = string.Empty;

        if (SymbolEqualityComparer.Default.Equals(from, to) || Widens(from, to)) return null;

        if (IsNullableValue(from) && !IsNullableValue(to) && to.IsValueType)
            return "'{0}' is nullable and '{1}' is not; say what an absent value becomes";

        if (CastsBetweenEnumAndNumber(from, to))
        {
            cast = $"({to.ToDisplayString(Full)})";

            return null;
        }

        return $"'{{0}}' is {from.ToDisplayString()} and '{{1}}' is {to.ToDisplayString()}";
    }

    private static IPropertySymbol? Property(ITypeSymbol type, string name, bool wanted)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            foreach (var property in current.GetMembers(name).OfType<IPropertySymbol>())
            {
                if (property.IsStatic) continue;

                var accessor = wanted ? property.SetMethod : property.GetMethod;

                if (accessor is { DeclaredAccessibility: Accessibility.Public }) return property;
            }

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

    private const string Contradiction =
        "[Mapping(Ignore = [...])] removes it while [MapMember] keeps it and says where it goes";

    /// <summary>
    /// The members handed to a method of the caller's own, and the first named twice.
    /// </summary>
    /// <remarks>
    /// Read off the declaration rather than <c>context.Attributes</c>, which holds only the
    /// attribute that triggered the generator.
    /// </remarks>
    private static (Dictionary<string, string> Custom, string? Duplicate) Custom(IMethodSymbol method)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var attribute in method.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != MapMemberName) continue;
            if (attribute.ConstructorArguments.Length != 2) continue;

            if (attribute.ConstructorArguments[0].Value is not string member) continue;
            if (attribute.ConstructorArguments[1].Value is not string handler) continue;

            // netstandard2.0: no TryAdd.
            if (found.ContainsKey(member)) return (found, member);

            found[member] = handler;
        }

        return (found, null);
    }

    /// <summary>
    /// The call that hands this member to the caller's method, or the reason it cannot.
    /// </summary>
    /// <remarks>
    /// The method has the shape of the mapping itself, one member's worth: producing, it
    /// returns the value and the call goes where the generated read would have; writing onto,
    /// it performs the write, because the member it writes need not be the one it discharges —
    /// that is usually why it exists.
    /// </remarks>
    private static string? Handler(
        INamedTypeSymbol container,
        IMethodSymbol mapping,
        string name,
        bool produces,
        bool isStatic,
        ITypeSymbol source,
        ITypeSymbol target,
        IPropertySymbol member,
        out string call)
    {
        call = string.Empty;

        var candidates = new List<IMethodSymbol>();

        for (var current = container; current is not null; current = current.BaseType)
            candidates.AddRange(current.GetMembers(name).OfType<IMethodSymbol>());

        if (candidates.Count == 0) return $"'{container.Name}' has no method called '{name}'";

        if (candidates.Any(c => SymbolEqualityComparer.Default.Equals(c, mapping)))
            return "it names the mapping itself, which would call itself forever";

        string? reason = null;

        foreach (var candidate in candidates)
        {
            reason = Mismatch(candidate, produces, isStatic, source, target, member, out var cast);

            if (reason is not null) continue;

            call = produces ? $"{member.Name} = {cast}{name}({{0}})" : $"{name}({{0}}, {{1}})";

            return null;
        }

        return candidates.Count == 1
            ? reason
            : $"none of the {candidates.Count} overloads has that shape";
    }

    /// <summary>Why this method cannot handle the member, or null when it can.</summary>
    private static string? Mismatch(
        IMethodSymbol candidate,
        bool produces,
        bool isStatic,
        ITypeSymbol source,
        ITypeSymbol target,
        IPropertySymbol member,
        out string cast)
    {
        cast = string.Empty;

        // A static mapping can only call a static helper; an instance mapping can call either,
        // and an instance helper is the whole point of injecting anything.
        if (isStatic && !candidate.IsStatic)
            return "it is not static, and neither can be — the mapping is";

        foreach (var parameter in candidate.Parameters)
            if (parameter.RefKind != RefKind.None)
                return $"'{parameter.Name}' is passed by reference; objects are passed by value";

        if (produces)
        {
            if (candidate.ReturnsVoid)
                return $"it returns nothing, so there is no value to put in '{member.Name}'";

            if (candidate.Parameters.Length != 1)
                return $"it takes {Count(candidate.Parameters.Length)} instead of one";

            if (!SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, source))
                return $"it takes {candidate.Parameters[0].Type.ToDisplayString()} " +
                       $"and the source is {source.ToDisplayString()}";

            return Convert(candidate.ReturnType, member.Type, out cast) is null
                ? null
                : $"it returns {candidate.ReturnType.ToDisplayString()} and '{member.Name}' " +
                  $"is {member.Type.ToDisplayString()}";
        }

        if (!candidate.ReturnsVoid)
            return $"it returns {candidate.ReturnType.ToDisplayString()}, but a mapping that writes " +
                   "onto a target has nowhere to put a returned value";

        if (candidate.Parameters.Length != 2)
            return $"it takes {Count(candidate.Parameters.Length)} instead of two";

        if (!SymbolEqualityComparer.Default.Equals(candidate.Parameters[0].Type, source))
            return $"its first parameter is {candidate.Parameters[0].Type.ToDisplayString()} " +
                   $"and the source is {source.ToDisplayString()}";

        return SymbolEqualityComparer.Default.Equals(candidate.Parameters[1].Type, target)
            ? null
            : $"its second parameter is {candidate.Parameters[1].Type.ToDisplayString()} " +
              $"and the target is {target.ToDisplayString()}";
    }

    private static string Count(int parameters)
        => parameters == 1 ? "1 parameter" : $"{parameters} parameters";

    /// <summary>The signature the message tells the reader to write.</summary>
    private static string Expected(
        string name, bool produces, bool isStatic, ITypeSymbol source, ITypeSymbol target,
        IPropertySymbol member)
    {
        var modifier = isStatic ? "static " : string.Empty;

        return produces
            ? $"{modifier}{member.Type.ToDisplayString()} {name}({source.ToDisplayString()} source)"
            : $"{modifier}void {name}({source.ToDisplayString()} source, {target.ToDisplayString()} target)";
    }

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
        if (candidate.Produces)
        {
            b.AppendLine($"    {candidate.Access}{candidate.Modifier}partial {candidate.TargetType} " +
                         $"{candidate.MethodName}({candidate.SourceType} {candidate.SourceName})");
            b.AppendLine("    {");
            // Imza ISARETLI tipi tasiyor (iki yari tam eslemek zorunda), `new` ise
            // SILINMIS olani: `new BedModel?` gecerli C# degil (CS8628).
            b.AppendLine($"        return new {candidate.ConstructedType}");
            b.AppendLine("        {");

            foreach (var assignment in candidate.Assignments)
                b.AppendLine($"            {string.Format(assignment, candidate.SourceName)},");

            b.AppendLine("        };");
            b.AppendLine("    }");
        }
        else
        {
            b.AppendLine($"    {candidate.Access}{candidate.Modifier}partial void {candidate.MethodName}(" +
                         $"{candidate.SourceType} {candidate.SourceName}, " +
                         $"{candidate.TargetType} {candidate.TargetName})");
            b.AppendLine("    {");

            foreach (var assignment in candidate.Assignments)
                b.AppendLine($"        {string.Format(assignment, candidate.SourceName, candidate.TargetName)};");

            b.AppendLine("    }");
        }
        b.AppendLine("}");

        context.AddSource(
            $"{candidate.TypeName}.{candidate.MethodName}.Mapping.g.cs",
            SourceText.From(b.ToString(), Encoding.UTF8));
    }

    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>
    /// <see cref="Full"/>, keeping the <c>?</c> on an annotated reference type.
    /// </summary>
    /// <remarks>
    /// The two halves of a partial member must match EXACTLY, nullability included
    /// (CS8611 on a parameter, CS8819 on a return). Rendering without the annotation would
    /// make a method declared with <c>BedModel?</c> unimplementable — and the error would
    /// name the generated file.
    /// </remarks>
    private static readonly SymbolDisplayFormat Annotated = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private sealed record Candidate(
        string? Namespace,
        string TypeName,
        string Keyword,
        string Access,
        string MethodName,
        string SourceType,
        string TargetType,
        string ConstructedType,
        string SourceName,
        string TargetName,
        bool Produces,
        bool IsStatic,
        EquatableArray<string> Assignments,
        DiagnosticDescriptor? Descriptor,
        EquatableArray<string>? Arguments,
        LocationInfo? Location)
    {
        /// <summary>The <c>static</c> the implementing half has to repeat, or nothing.</summary>
        public string Modifier => IsStatic ? "static " : string.Empty;

        public static Candidate Failed(
            INamedTypeSymbol container,
            string methodName,
            DiagnosticDescriptor descriptor,
            LocationInfo? location,
            string[] arguments)
            => new(null, container.Name, "class", string.Empty, methodName, string.Empty, string.Empty,
                string.Empty, "source", "target", false, true, EquatableArray<string>.Empty,
                descriptor, new EquatableArray<string>(ImmutableArray.Create(arguments)), location);
    }
}
