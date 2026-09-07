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
/// Maps are read one at a time and then resolved TOGETHER, because a composition writes another
/// map's tree in place of a member and cannot know that map until every one has been read. That
/// is also where a circle is found — and a circle is a build error naming the loop, which is
/// the thing no runtime mapper can do.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class MapGenerator : IIncrementalGenerator
{
    private const string MapName = "IQOne.Zero.Mapping.Map`2";
    private const string SpecificationName = "IQOne.Zero.Persistence.Specification`2";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // No marker attribute: the base type already names the pair, and asking for an
        // attribute as well would be a second place to say the same thing.
        var drafts = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => Guarded(ctx))
            .Where(static d => d is not null)
            .Select(static (d, _) => d!);

        // A specification names a pair in its base type, and if a map is declared for that pair
        // there is nothing for the specification to say: its selector IS that map.
        var projections = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => Projected(ctx))
            .Where(static p => p is not null)
            .Select(static (p, _) => p!);

        // Collected, because composition is resolved across maps and a specification has to
        // find one. The cost is that one map changing re-emits all of them; the alternative is
        // that a map cannot reach another, which is the feature.
        context.RegisterSourceOutput(drafts.Collect().Combine(projections.Collect()), Emit);
    }

    /// <summary>
    /// A specification whose selector a map can supply, or null when it needs nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Purely additive: a specification that writes its own selector, declines generation, or
    /// has no map for its pair is left exactly as it was. Nothing is reported when no map is
    /// found either — the language already requires the member, and CS0534 says so precisely.
    /// </para>
    /// <para>
    /// A specification and its map may be in different files or different assemblies; only the
    /// map's TYPE is needed here, not its source, which is what separates this from
    /// composition.
    /// </para>
    /// </remarks>
    private static Projection? Projected(GeneratorSyntaxContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type) return null;

        if (Closed(type, SpecificationName) is not { } specification) return null;

        if (OptOut.Declared(type)) return null;

        // Hand-written wins, silently: the selector is right there saying what happened.
        if (Declares(type, "Selector")) return null;

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword)) return null;

        var source = specification.TypeArguments[0];
        var result = specification.TypeArguments[1];

        return new Projection(
            Namespace(type),
            type.Name,
            source.ToDisplayString(Full),
            result.ToDisplayString(Full),
            Declarations.Key(source, result),
            Field(type),
            Usings(declaration),
            LocationInfo.From(declaration.Identifier.Parent));
    }

    /// <summary>A field name the specification does not already use.</summary>
    /// <remarks>
    /// Generated into the author's type, so a name it already has would be a compiler error in
    /// a file nobody wrote.
    /// </remarks>
    private static string Field(INamedTypeSymbol type)
    {
        foreach (var name in new[] { "Projection", "MapForThisPair", "__map" })
            if (type.GetMembers(name).Length == 0) return name;

        return "__map" + type.Name.Length;
    }

    /// <summary>
    /// <see cref="Draft"/>, with a throw turned into a diagnostic.
    /// </summary>
    /// <remarks>
    /// A generator that throws takes the whole build with it, and generated code cannot be
    /// edited — so a bug here would stop somebody's build with nothing for them to try but a
    /// framework release. Caught per map, one map reports and the rest are written as usual;
    /// the map that failed can be taken over by declaring its Selector.
    /// </remarks>
    private static Map? Guarded(GeneratorSyntaxContext context)
    {
        try
        {
            return Draft(context);
        }
        catch (Exception exception)
        {
            var declaration = (ClassDeclarationSyntax)context.Node;

            return Map.Failed(
                declaration.Identifier.ValueText, MapDiagnostics.GeneratorFailed,
                LocationInfo.From(declaration.Identifier.Parent),
                [declaration.Identifier.ValueText, Guard.Describe(exception)]);
        }
    }

    private static Map? Draft(GeneratorSyntaxContext context)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;

        if (context.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type) return null;

        var closed = Base(type);

        if (closed is null) return null;

        var location = LocationInfo.From(declaration.Identifier.Parent);

        // Declined outright: [NoGenerate] on the map, an enclosing type, or the assembly.
        if (OptOut.Declared(type)) return null;

        // WRITTEN BY HAND wins, and this is the escape hatch the whole design needs: generated
        // code cannot be edited, so a generator that is wrong about one map would otherwise stop
        // its user's build with nothing to do but wait for a release. Declaring the property
        // takes the map over, and nothing is generated for it — no diagnostic either, because
        // the hand-written selector is right there in the same type saying what happened.
        if (Declares(type, "Selector")) return null;

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return Map.Failed(type.Name, MapDiagnostics.NotPartial, location, [type.Name]);

        var source = closed.TypeArguments[0];
        var destination = closed.TypeArguments[1];

        var declared = new List<Binding>();
        var ignored = new List<string>();
        string? duplicate = null;

        if (Configure(declaration) is { } configure)
        {
            var unreadable = Declarations.Read(
                configure, context.SemanticModel, out declared, out ignored, out duplicate);

            if (unreadable is not null)
                return Map.Failed(
                    type.Name, MapDiagnostics.ConfigureCannotBeRead, location, [type.Name, unreadable]);
        }

        var impossible = Construction(destination, out var parameters);

        if (impossible is not null)
            return Map.Failed(
                type.Name, MapDiagnostics.CannotBeConstructed, location,
                [destination.ToDisplayString(), impossible]);

        // Written positionally, the parameters are the account and the members are whatever the
        // shape carries on top of them — a record's extra settable property, for instance.
        var positional = new HashSet<string>(parameters.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        var members = Assignable(destination)
            .Where(m => !positional.Contains(m.Name))
            .ToList();

        if (parameters.Length == 0 && members.Count == 0)
            return Map.Failed(
                type.Name, MapDiagnostics.NothingIsProduced, location,
                [type.Name, source.ToDisplayString(), destination.ToDisplayString()]);

        var names = new HashSet<string>(
            members.Select(m => m.Name).Concat(parameters.Select(p => p.Name)), StringComparer.Ordinal);

        foreach (var name in ignored.Concat(declared.Select(d => d.Member)))
            if (!names.Contains(name))
                return Map.Failed(
                    type.Name, MapDiagnostics.NotASettableMember, location,
                    [name, type.Name, destination.ToDisplayString(), Missing(destination, name)]);

        if (duplicate is not null)
            return Map.Failed(
                type.Name, MapDiagnostics.MemberIsAccountedForTwice, location,
                [duplicate, type.Name, "it is named more than once by map.Member or map.Ignore"]);

        var byName = new Dictionary<string, Binding>(StringComparer.Ordinal);

        foreach (var binding in declared) byName[binding.Member] = binding;

        var empty = new HashSet<string>(ignored, StringComparer.Ordinal);

        var bindings = ImmutableArray.CreateBuilder<Binding>();

        for (var ordinal = 0; ordinal < parameters.Length; ordinal++)
        {
            var parameter = parameters[ordinal];

            // Ignored, a parameter gets the default: there is no way not to pass one, and
            // 'default' is the same thing an unassigned member would have held.
            if (empty.Contains(parameter.Name))
            {
                bindings.Add(Binding
                    .Plain(parameter.Name, $"default({parameter.Type.ToDisplayString(Full)})")
                    .At(ordinal));

                continue;
            }

            if (byName.TryGetValue(parameter.Name, out var declaredArgument))
            {
                bindings.Add(declaredArgument.At(ordinal));

                continue;
            }

            var argument = Read(source, parameter.Name, parameter.Type, out var read);

            if (argument is not null)
                return Map.Failed(
                    type.Name, MapDiagnostics.MemberIsUnaccountedFor, location,
                    [destination.Name, parameter.Name, type.Name, argument]);

            bindings.Add(Binding.Plain(parameter.Name, read).At(ordinal));
        }

        foreach (var member in members)
        {
            if (empty.Contains(member.Name)) continue;

            if (byName.TryGetValue(member.Name, out var declaredBinding))
            {
                bindings.Add(declaredBinding);

                continue;
            }

            var reason = Read(source, member.Name, member.Type, out var read);

            if (reason is not null)
                return Map.Failed(
                    type.Name, MapDiagnostics.MemberIsUnaccountedFor, location,
                    [destination.Name, member.Name, type.Name, reason]);

            bindings.Add(Binding.Plain(member.Name, read));
        }

        return new Map(
            Namespace(type),
            type.Name,
            source.ToDisplayString(Full),
            destination.ToDisplayString(Full),
            source.Name,
            destination.Name,
            Declarations.Key(source, destination),
            Usings(declaration),
            new EquatableArray<Binding>(bindings.ToImmutable()),
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
    /// and the error names a file nobody wrote.
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
    private static INamedTypeSymbol? Base(INamedTypeSymbol type) => Closed(type, MapName);

    /// <summary>The closed two-argument base of this metadata name, walking the chain.</summary>
    /// <remarks>
    /// Walked rather than matched on the immediate base, so an application can put its own
    /// layer in between — a base applying paging and soft-delete rules to every query — and
    /// that layer still names the same two shapes.
    /// </remarks>
    private static INamedTypeSymbol? Closed(INamedTypeSymbol type, string wanted)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            var definition = current.OriginalDefinition;

            // MetadataName, because the arity is part of the name.
            var name = definition.ContainingNamespace.IsGlobalNamespace
                ? definition.MetadataName
                : $"{definition.ContainingNamespace.ToDisplayString()}.{definition.MetadataName}";

            if (name == wanted && current.TypeArguments.Length == 2) return current;
        }

        return null;
    }

    /// <summary>Whether the map declares this member itself.</summary>
    /// <remarks>
    /// Only the author's own parts are visible while generating, so anything found here was
    /// written by hand.
    /// </remarks>
    private static bool Declares(INamedTypeSymbol type, string name)
        => type.GetMembers(name).Any(m => !m.IsImplicitlyDeclared && !m.IsAbstract);

    private static MethodDeclarationSyntax? Configure(ClassDeclarationSyntax declaration)
        => declaration.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == "Configure" && m.ParameterList.Parameters.Count == 1);

    /// <summary>
    /// How the destination gets written, or the reason it cannot be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two shapes, and which one applies is not a preference: a type with a parameterless
    /// constructor is written as an initialiser, and one without has to be written positionally
    /// — a positional record has no parameterless constructor at all, and the ported code here
    /// is full of them.
    /// </para>
    /// <para>
    /// Several constructors is refused rather than resolved. Picking one would be the generator
    /// deciding which shape the caller meant, silently, and the two would differ in exactly the
    /// members somebody cared about.
    /// </para>
    /// </remarks>
    private static string? Construction(
        ITypeSymbol destination, out ImmutableArray<IParameterSymbol> parameters)
    {
        parameters = ImmutableArray<IParameterSymbol>.Empty;

        if (destination is not INamedTypeSymbol named)
            return $"'{destination.ToDisplayString()}' is not a named type";

        if (named.IsTupleType)
            return "a tuple's elements are named at the call site and are Item1 and Item2 " +
                   "everywhere else, so there is nothing to match by name";

        if (named.TypeKind is TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate)
            return $"it is {named.TypeKind.ToString().ToLowerInvariant()}, and a map writes a " +
                   "construction";

        var usable = named.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            // The copy constructor a record gets is not a way to build one from something else.
            .Where(c => c.Parameters.Length != 1
                     || !SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, named))
            .ToList();

        if (usable.Any(c => c.Parameters.Length == 0)) return null;

        if (usable.Count == 0) return "it has no public constructor";

        if (usable.Count > 1)
            return $"it has {usable.Count} constructors and no parameterless one, so which to " +
                   "write is a choice; give it a parameterless constructor, or write the map by hand";

        parameters = usable[0].Parameters;

        return null;
    }

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
        ITypeSymbol source, string wanted, ITypeSymbol type, out string expression)
    {
        expression = string.Empty;

        var origin = Readable(source, wanted);

        if (origin is null) return $"'{source.Name}' has no readable member of that name";

        var from = origin.Type;
        var to = type;

        if (SymbolEqualityComparer.IncludeNullability.Equals(from, to)
            || SymbolEqualityComparer.Default.Equals(from, to)
            || Widens(from, to))
        {
            expression = $"{Declarations.Placeholder}.{origin.Name}";

            return null;
        }

        // A nullable value into a non-nullable member is the one mismatch that looks harmless
        // and is not: the fallback is a choice — zero, false, the default enum member — and it
        // belongs where a reader can see it.
        if (IsNullableValue(from) && !IsNullableValue(to) && to.IsValueType)
            return $"'{origin.Name}' is nullable and '{wanted}' is not; say what an absent " +
                   "value becomes";

        if (CastsBetweenEnumAndNumber(from, to))
        {
            expression = $"({to.ToDisplayString(Full)}){Declarations.Placeholder}.{origin.Name}";

            return null;
        }

        // A shape that needs another map says so, rather than being guessed at.
        if (from is INamedTypeSymbol { TypeKind: TypeKind.Class } && to is INamedTypeSymbol { TypeKind: TypeKind.Class })
            return $"'{origin.Name}' is {from.ToDisplayString()} and '{wanted}' is " +
                   $"{to.ToDisplayString()}; compose the map for the pair with " +
                   $"e.{origin.Name}.To<{to.Name}>()";

        return $"'{origin.Name}' is {from.ToDisplayString()} and '{wanted}' is {to.ToDisplayString()}";
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

    private static void Emit(
        SourceProductionContext context, (ImmutableArray<Map> Maps, ImmutableArray<Projection> Projections) input)
    {
        var (maps, projections) = input;

        var byPair = new Dictionary<string, Map>(StringComparer.Ordinal);

        foreach (var map in maps)
        {
            if (map.Descriptor is not null) continue;

            // A pair has one map: everything reaching for it reaches by pair, and with two the
            // winner would be whichever was read first.
            if (byPair.TryGetValue(map.Pair, out var first))
            {
                var parts = map.Pair.Split('|');

                context.ReportDiagnostic(Diagnostic.Create(
                    MapDiagnostics.TwoMapsForOnePair, map.Location?.ToLocation(),
                    first.TypeName, map.TypeName, Short(parts[0]), Short(parts[1])));

                continue;
            }

            byPair[map.Pair] = map;
        }

        foreach (var map in maps)
        {
            if (map.Descriptor is { } descriptor)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor, map.Location?.ToLocation(), map.Arguments!.Value.ToArray()));

                continue;
            }

            try
            {
                var slots = new List<Slot>();

                var failure = Resolve(map, byPair, [], 3, slots);

                if (failure is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        failure.Value.Descriptor, map.Location?.ToLocation(), failure.Value.Arguments));

                    continue;
                }

                context.AddSource(
                    $"{map.TypeName}.Map.g.cs", SourceText.From(Render(map, slots), Encoding.UTF8));
            }
            catch (Exception exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MapDiagnostics.GeneratorFailed, map.Location?.ToLocation(),
                    map.TypeName, Guard.Describe(exception)));
            }
        }

        foreach (var projection in projections)
        {
            // No map for the pair: nothing is reported, because the language already requires
            // the member and CS0534 names it precisely.
            if (!byPair.TryGetValue(projection.Pair, out var map)) continue;

            Guard.Run(
                context, MapDiagnostics.GeneratorFailed, projection.TypeName,
                projection.Location?.ToLocation(),
                () => context.AddSource(
                    $"{projection.TypeName}.Selector.g.cs",
                    SourceText.From(Render(projection, map), Encoding.UTF8)));
        }
    }

    /// <summary>A specification's selector, taken from the map for its pair.</summary>
    private static string Render(Projection projection, Map map)
    {
        var b = new StringBuilder();

        b.AppendLine("// <auto-generated/>");
        b.AppendLine("#nullable enable");
        b.AppendLine();

        if (projection.Namespace is { } ns)
        {
            b.AppendLine($"namespace {ns};");
            b.AppendLine();
        }

        b.AppendLine($"partial class {projection.TypeName}");
        b.AppendLine("{");
        b.AppendLine($"    /// <summary>The declared map for {Short(map.SourceType)} to " +
                     $"{Short(map.DestinationType)}.</summary>");
        b.AppendLine("    /// <remarks>");
        b.AppendLine("    /// One per specification type, and the map holds its tree statically, so this");
        b.AppendLine("    /// costs nothing per query.");
        b.AppendLine("    /// </remarks>");
        b.AppendLine($"    private static readonly {Qualified(map)} {projection.FieldName} = new();");
        b.AppendLine();
        b.AppendLine("    /// <inheritdoc />");
        b.AppendLine("    public override global::System.Linq.Expressions.Expression<" +
                     $"global::System.Func<{projection.SourceType}, {projection.ResultType}>> Selector");
        b.AppendLine($"        => {projection.FieldName}.Selector;");
        b.AppendLine("}");

        return b.ToString();
    }

    private static string Qualified(Map map)
        => map.Namespace is { } ns ? $"global::{ns}.{map.TypeName}" : $"global::{map.TypeName}";

    /// <summary>
    /// The initialiser entries for a map, with every composition written out.
    /// </summary>
    /// <remarks>
    /// Recursive, and the path is what makes a circle a diagnostic rather than a hang: a pair
    /// already on the way in cannot be written out, because the source would have to contain
    /// itself.
    /// </remarks>
    private static Failure? Resolve(
        Map map,
        Dictionary<string, Map> byPair,
        List<string> path,
        int indent,
        List<Slot> slots)
    {
        path.Add(map.Pair);

        try
        {
            foreach (var binding in map.Bindings)
            {
                if (!binding.Composed)
                {
                    slots.Add(new Slot(binding.Ordinal, binding.Member, binding.Expression));

                    continue;
                }

                if (!byPair.TryGetValue(binding.Pair, out var child))
                {
                    var parts = binding.Pair.Split('|');

                    return new Failure(
                        MapDiagnostics.NoMapForThePair,
                        [
                            map.TypeName, Short(parts[0]), Short(parts[1]),
                            Short(parts[0]) + Short(parts[1]) + "Map", parts[0], parts[1]
                        ]);
                }

                if (path.Contains(binding.Pair))
                    return new Failure(
                        MapDiagnostics.CompositionCycles,
                        [map.TypeName, Circle(path, binding.Pair)]);

                var inner = new List<Slot>();

                var failure = Resolve(child, byPair, path, indent + 2, inner);

                if (failure is not null) return failure;

                slots.Add(new Slot(binding.Ordinal, binding.Member, Written(binding, child, inner, indent)));
            }

            return null;
        }
        finally
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>One composition, as the expression that fills the member.</summary>
    private static string Written(Binding binding, Map child, List<Slot> inner, int indent)
    {
        var substituted = inner
            .Select(slot => slot with { Text = Substituted(slot.Text, binding) })
            .ToList();

        var construction = Construct(child.DestinationType, substituted, indent);

        if (binding.Element.Length > 0)
            return $"{binding.Receiver}.Select({binding.Element} => {construction})" +
                   $".{binding.Materialiser}()";

        // A missing row gives NOTHING rather than an object whose members are all zero, which
        // is what a nested initialiser over an absent navigation produces. It is also what
        // keeps the generated file compiling: without it the child reads through a nullable
        // navigation and the compiler says so (CS8602), naming a file nobody wrote. Removing
        // this line fails the build twice over, which is the right number.
        return binding.Guard ? $"{binding.Receiver} == null ? null : {construction}" : construction;
    }

    /// <summary>
    /// A construction of one shape from its slots.
    /// </summary>
    /// <remarks>
    /// Both shapes at once, because a record with a primary constructor and an extra settable
    /// member needs both: the arguments in the order the constructor declares, then whatever
    /// is left as an initialiser.
    /// </remarks>
    private static string Construct(string type, List<Slot> slots, int indent)
    {
        var pad = new string(' ', indent * 4);

        var arguments = slots.Where(s => s.Ordinal >= 0).OrderBy(s => s.Ordinal).ToList();
        var members = slots.Where(s => s.Ordinal < 0).ToList();

        var b = new StringBuilder();

        b.Append($"new {type}");

        if (arguments.Count > 0)
        {
            b.AppendLine("(");

            for (var i = 0; i < arguments.Count; i++)
                b.AppendLine($"{pad}    {arguments[i].Text}{(i < arguments.Count - 1 ? "," : string.Empty)}");

            b.Append($"{pad})");
        }

        if (members.Count == 0)
        {
            // A positional construction with nothing left over: `new T(a, b)` and no braces,
            // which is also the only form a type with no settable member can take.
            if (arguments.Count > 0) return b.ToString();

            return $"new {type}()";
        }

        if (arguments.Count > 0) b.AppendLine();
        else b.AppendLine();

        b.AppendLine($"{pad}{{");

        foreach (var member in members) b.AppendLine($"{pad}    {member.Name} = {member.Text},");

        b.Append($"{pad}}}");

        return b.ToString();
    }

    /// <summary>
    /// A child's entry, reading from wherever the parent found it.
    /// </summary>
    /// <remarks>
    /// The child was written against the placeholder, so this is one substitution: the receiver
    /// for an object, the element parameter for a sequence. The receiver itself still carries the
    /// placeholder — it reads from the parent's own source — and one pass leaves that alone.
    /// </remarks>
    private static string Substituted(string assignment, Binding binding)
        => assignment.Replace(
            Declarations.Placeholder,
            binding.Element.Length > 0 ? binding.Element : binding.Receiver);

    private static string Circle(List<string> path, string closing)
    {
        var at = path.IndexOf(closing);
        var loop = path.Skip(at).Append(closing);

        return string.Join(" → ", loop.Select(p => string.Join(" → ", p.Split('|').Select(Short))));
    }

    private static string Short(string qualified)
    {
        var at = qualified.LastIndexOf('.');

        // netstandard2.0: no Index/Range.
        return at < 0 ? qualified : qualified.Substring(at + 1);
    }

    private static string Render(Map map, List<Slot> slots)
    {
        var b = new StringBuilder();

        b.AppendLine("// <auto-generated/>");
        b.AppendLine("#nullable enable");
        b.AppendLine();

        if (map.Namespace is { } ns)
        {
            b.AppendLine($"namespace {ns};");
            b.AppendLine();
        }

        // System.Linq unconditionally, because a composed sequence calls Select; deduplicated,
        // because a repeated using in the same scope is a warning and warnings are errors here.
        var directives = map.Usings.ToArray().ToList();

        if (!directives.Contains("using System.Linq;")) directives.Add("using System.Linq;");

        foreach (var directive in directives) b.AppendLine(directive);

        b.AppendLine();
        b.AppendLine($"partial class {map.TypeName}");
        b.AppendLine("{");

        // Static, so the tree is built once for the type rather than once per instance: a map
        // resolved per request would otherwise rebuild it every time.
        b.AppendLine("    private static readonly global::System.Linq.Expressions.Expression<" +
                     $"global::System.Func<{map.SourceType}, {map.DestinationType}>> Tree =");
        b.AppendLine($"        {Parameter} =>");
        b.AppendLine($"            {Construct(map.DestinationType, slots, 3).Replace(Declarations.Placeholder, Parameter)};");
        b.AppendLine();

        // Lazy, because compiling a tree costs far more than building one and a map used only
        // on queries never needs it.
        b.AppendLine("    private static readonly global::System.Lazy<" +
                     $"global::System.Func<{map.SourceType}, {map.DestinationType}>> Compiled =");
        b.AppendLine("        new(Tree.Compile, global::System.Threading.LazyThreadSafetyMode." +
                     "ExecutionAndPublication);");
        b.AppendLine();
        b.AppendLine("    /// <inheritdoc />");
        b.AppendLine("    public override global::System.Linq.Expressions.Expression<" +
                     $"global::System.Func<{map.SourceType}, {map.DestinationType}>> Selector => Tree;");
        b.AppendLine();
        b.AppendLine("    /// <inheritdoc />");
        b.AppendLine($"    public override global::System.Func<{map.SourceType}, " +
                     $"{map.DestinationType}> Project => Compiled.Value;");
        b.AppendLine("}");

        return b.ToString();
    }

    /// <summary>The name the generated tree reads from.</summary>
    private const string Parameter = "source";

    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>One thing filled in a construction.</summary>
    /// <param name="Ordinal">A constructor argument's position, or -1 for a member.</param>
    /// <param name="Name">The member's name; unused for an argument.</param>
    /// <param name="Text">The expression, still carrying the placeholder.</param>
    private readonly record struct Slot(int Ordinal, string Name, string Text);

    private readonly record struct Failure(DiagnosticDescriptor Descriptor, string[] Arguments);

    /// <summary>A specification waiting for the map that fills its selector.</summary>
    private sealed record Projection(
        string? Namespace,
        string TypeName,
        string SourceType,
        string ResultType,
        string Pair,
        string FieldName,
        EquatableArray<string> Usings,
        LocationInfo? Location);

    private sealed record Map(
        string? Namespace,
        string TypeName,
        string SourceType,
        string DestinationType,
        string SourceName,
        string DestinationName,
        string Pair,
        EquatableArray<string> Usings,
        EquatableArray<Binding> Bindings,
        DiagnosticDescriptor? Descriptor,
        EquatableArray<string>? Arguments,
        LocationInfo? Location)
    {
        public static Map Failed(
            string typeName,
            DiagnosticDescriptor descriptor,
            LocationInfo? location,
            string[] arguments)
            => new(null, typeName, string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, EquatableArray<string>.Empty, EquatableArray<Binding>.Empty,
                descriptor, new EquatableArray<string>(ImmutableArray.Create(arguments)), location);
    }
}
