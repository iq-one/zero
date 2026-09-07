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

        // Collected, because composition is resolved across maps. The cost is that one map
        // changing re-emits all of them; the alternative is that a map cannot reach another,
        // which is the feature.
        context.RegisterSourceOutput(drafts.Collect(), Emit);
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

        var members = Assignable(destination);
        var names = new HashSet<string>(members.Select(m => m.Name), StringComparer.Ordinal);

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

        foreach (var member in members)
        {
            if (empty.Contains(member.Name)) continue;

            if (byName.TryGetValue(member.Name, out var declaredBinding))
            {
                bindings.Add(declaredBinding);

                continue;
            }

            var reason = Read(source, member, out var read);

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
    private static string? Read(ITypeSymbol source, IPropertySymbol member, out string expression)
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
            expression = $"{Declarations.Placeholder}.{origin.Name}";

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
            expression = $"({to.ToDisplayString(Full)}){Declarations.Placeholder}.{origin.Name}";

            return null;
        }

        // A shape that needs another map says so, rather than being guessed at.
        if (from is INamedTypeSymbol { TypeKind: TypeKind.Class } && to is INamedTypeSymbol { TypeKind: TypeKind.Class })
            return $"'{origin.Name}' is {from.ToDisplayString()} and '{member.Name}' is " +
                   $"{to.ToDisplayString()}; compose the map for the pair with " +
                   $"e.{origin.Name}.To<{to.Name}>()";

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

    private static void Emit(SourceProductionContext context, ImmutableArray<Map> maps)
    {
        var byPair = new Dictionary<string, Map>(StringComparer.Ordinal);

        foreach (var map in maps)
            if (map.Descriptor is null && !byPair.ContainsKey(map.Pair))
                byPair[map.Pair] = map;

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
                var assignments = new List<string>();

                var failure = Resolve(map, byPair, [], 3, assignments);

                if (failure is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        failure.Value.Descriptor, map.Location?.ToLocation(), failure.Value.Arguments));

                    continue;
                }

                context.AddSource(
                    $"{map.TypeName}.Map.g.cs", SourceText.From(Render(map, assignments), Encoding.UTF8));
            }
            catch (Exception exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MapDiagnostics.GeneratorFailed, map.Location?.ToLocation(),
                    map.TypeName, Guard.Describe(exception)));
            }
        }
    }

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
        List<string> assignments)
    {
        path.Add(map.Pair);

        try
        {
            foreach (var binding in map.Bindings)
            {
                if (!binding.Composed)
                {
                    assignments.Add($"{binding.Member} = {binding.Expression}");

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

                var inner = new List<string>();

                var failure = Resolve(child, byPair, path, indent + 2, inner);

                if (failure is not null) return failure;

                assignments.Add(Written(binding, child, inner, indent));
            }

            return null;
        }
        finally
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>One composition, written out in place of the member.</summary>
    private static string Written(Binding binding, Map child, List<string> inner, int indent)
    {
        var pad = new string(' ', indent * 4);
        var body = new StringBuilder();

        body.AppendLine($"new {child.DestinationType}");
        body.AppendLine($"{pad}{{");

        foreach (var assignment in inner)
            body.AppendLine($"{pad}    {Substituted(assignment, binding)},");

        body.Append($"{pad}}}");

        var construction = body.ToString();

        if (binding.Element.Length > 0)
            return $"{binding.Member} = {binding.Receiver}" +
                   $".Select({binding.Element} => {construction})" +
                   $".{binding.Materialiser}()";

        // A missing row gives NOTHING rather than an object whose members are all zero, which
        // is what a nested initialiser over an absent navigation produces. It is also what
        // keeps the generated file compiling: without it the child reads through a nullable
        // navigation and the compiler says so (CS8602), naming a file nobody wrote. Removing
        // this line fails the build twice over, which is the right number.
        return binding.Guard
            ? $"{binding.Member} = {binding.Receiver} == null ? null : {construction}"
            : $"{binding.Member} = {construction}";
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

    private static string Render(Map map, List<string> assignments)
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
        b.AppendLine($"        {Parameter} => new {map.DestinationType}");
        b.AppendLine("        {");

        foreach (var assignment in assignments)
            b.AppendLine($"            {assignment.Replace(Declarations.Placeholder, Parameter)},");

        b.AppendLine("        };");
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

    private readonly record struct Failure(DiagnosticDescriptor Descriptor, string[] Arguments);

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
