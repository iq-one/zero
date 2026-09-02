using System.Collections.Immutable;
using System.Globalization;
using IQOne.Zero.Generators.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace IQOne.Zero.Generators.Registration;

/// <summary>Extracts raw symbol facts. Interpretation happens during emission.</summary>
internal static class SymbolCollector
{
    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>Reads everything registration, dispatch and routing need from one type.</summary>
    /// <param name="type">The declared type.</param>
    /// <param name="node">Its declaration, for diagnostic locations.</param>
    /// <returns>The facts, in a form the incremental pipeline can compare.</returns>
    public static ServiceCandidate DescribeService(INamedTypeSymbol type, SyntaxNode node)
    {
        var attributes = ImmutableArray.CreateBuilder<AttributeUsage>();

        foreach (var attribute in Attributes(type))
        {
            var constructorArguments = attribute.ConstructorArguments
                .SelectMany(a => a.Kind == TypedConstantKind.Array ? a.Values : [a])
                .Select(Argument);

            var namedArguments = attribute.NamedArguments
                .Select(n => new NamedAttributeArgument(n.Key, Argument(n.Value)));

            // A generic attribute passes its type argument in a base-constructor call, which
            // is invisible to ConstructorArguments. Without this [ServiceTypes<T>] is a no-op.
            var typeArguments = attribute.AttributeClass is { IsGenericType: true } generic
                ? generic.TypeArguments.Select(a => a.ToDisplayString(Full))
                : [];

            attributes.Add(new AttributeUsage(
                attribute.AttributeClass?.OriginalDefinition.ToDisplayString() ?? string.Empty,
                new EquatableArray<AttributeArgument>([.. constructorArguments]),
                new EquatableArray<NamedAttributeArgument>([.. namedArguments]),
                new EquatableArray<string>([.. typeArguments])));
        }

        var dependencies = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault()
            ?.Parameters.Select(p => Dependency(p.Type)).ToList() ?? [];

        var closed = type.AllInterfaces
            .Where(i => i.IsGenericType)
            .Select(i => Describe(i, type));

        var direct = type.Interfaces.Select(i => Describe(i, type));

        // The naming convention used to see only the base list, so a class implementing its
        // interface through a base class -- the shape ZERO008 tells you to write -- resolved
        // to nothing and was reported as unresolvable.
        var inherited = BaseTypes(type).SelectMany(b => b.Interfaces).Select(i => Describe(i, type));

        // Reachable from this type's own base list. A marker found only outside this set was
        // inherited, and blaming this declaration for it would point at the wrong file.
        var declared = type.Interfaces
            .SelectMany(i => new[] { i }.Concat(i.AllInterfaces))
            .Select(OpenName)
            .Distinct(StringComparer.Ordinal);

        // Which of the type's own interfaces carries which marker. Flattening loses this,
        // and it is the difference between "you wrote two markers" and "your abstraction
        // already said one" -- two mistakes with different fixes.
        var carriedMarkers = type.Interfaces
            .SelectMany(own => own.AllInterfaces.Select(carried => new InheritedMarker(
                own.OriginalDefinition.ToDisplayString(),
                carried.OriginalDefinition.ToDisplayString())));

        return new ServiceCandidate(
            type.ToDisplayString(Full),
            Unbound(type),
            type.IsAbstract,
            type.Arity,
            new EquatableArray<string>([.. type.AllInterfaces.Select(OpenName)]),
            new EquatableArray<string>([.. declared]),
            new EquatableArray<InterfaceUsage>([.. direct]),
            new EquatableArray<InterfaceUsage>([.. inherited]),
            type.Name,
            new EquatableArray<AttributeUsage>(attributes.ToImmutable()),
            new EquatableArray<DependencyReference>([.. dependencies]),
            new EquatableArray<InterfaceUsage>([.. closed]),
            new EquatableArray<InheritedMarker>([.. carriedMarkers]),
            LocationInfo.From(node));
    }

    /// <summary>The base classes of <paramref name="type"/>, nearest first.</summary>
    private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
            yield return current;
    }

    /// <summary>
    /// The attributes that apply to <paramref name="type"/>, its base classes' included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Roslyn's <c>GetAttributes</c> returns only what a declaration wrote itself, so a class
    /// deriving from a base that states its service types — <c>ApplicationSteps</c>, and every
    /// base the framework ships after it — resolved to nothing and was reported as
    /// unregistrable. The base list is walked so the annotation reaches where it was aimed.
    /// </para>
    /// <para>
    /// An attribute that says <c>Inherited = false</c> is left where it was written. Route
    /// attributes say so, and inheriting one would map a second endpoint on the same pattern
    /// and throw when the endpoint table is built.
    /// </para>
    /// <para>
    /// The nearest declaration carrying an attribute supplies all of that attribute's
    /// instances; a nearer one replaces the inherited set rather than adding to it. These
    /// annotations are overrides, and a derived class naming its own service types means
    /// those and not also the four it would otherwise inherit.
    /// </para>
    /// </remarks>
    private static IEnumerable<AttributeData> Attributes(INamedTypeSymbol type)
    {
        var settled = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
        {
            var inheriting = !SymbolEqualityComparer.Default.Equals(current, type);

            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass is not { } declaration) continue;

                // Arity-stripped, so [ServiceTypes<T>] and [ServiceTypes(...)] are one family
                // and a derived class stating either replaces whichever the base used.
                var family = OpenName(declaration);

                if (inheriting && (settled.Contains(family) || !Inherited(declaration))) continue;

                yield return attribute;
            }

            foreach (var attribute in current.GetAttributes())
                if (attribute.AttributeClass is { } declaration)
                    settled.Add(OpenName(declaration));
        }
    }

    /// <summary>Whether an attribute class reaches derived types. <c>AttributeUsage</c> defaults to true.</summary>
    private static bool Inherited(INamedTypeSymbol attributeClass)
    {
        // AttributeUsage is itself inherited, so a subclass that states none takes its base's.
        for (var current = attributeClass; current is not null; current = current.BaseType)
            foreach (var usage in current.GetAttributes())
                if (usage.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute")
                {
                    foreach (var named in usage.NamedArguments)
                        if (named.Key == "Inherited" && named.Value.Value is bool inherited)
                            return inherited;

                    return true;
                }

        return true;
    }

    /// <summary>Records one implemented interface in every form emission may need.</summary>
    private static InterfaceUsage Describe(INamedTypeSymbol declaration, INamedTypeSymbol owner)
    {
        var parameters = owner.TypeParameters;

        var forwards = declaration.IsGenericType
            && parameters.Length > 0
            && declaration.TypeArguments.Length == parameters.Length
            && !declaration.TypeArguments.Where((a, i) =>
                !SymbolEqualityComparer.Default.Equals(a, parameters[i])).Any();

        return new InterfaceUsage(
            OpenName(declaration),
            new EquatableArray<string>([.. declaration.TypeArguments.Select(a => a.ToDisplayString(Full))]),
            declaration.ToDisplayString(Full),
            Unbound(declaration),
            forwards);
    }

    /// <summary>Records a constructor parameter's type in both the closed and unbound forms.</summary>
    private static DependencyReference Dependency(ITypeSymbol type) => new(
        type.ToDisplayString(Full),
        type is INamedTypeSymbol { IsGenericType: true } generic ? Unbound(generic) : null);

    /// <summary>Fully qualified name with empty type arguments, for example <c>global::Ns.IFoo&lt;&gt;</c>.</summary>
    private static string Unbound(INamedTypeSymbol type) => type.IsGenericType
        ? type.OriginalDefinition.ConstructUnboundGenericType().ToDisplayString(Full)
        : type.ToDisplayString(Full);

    /// <summary>Open generic name without arity, for example <c>Ns.IServiceHandler</c>.</summary>
    private static string OpenName(INamedTypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString();
        var index = name.IndexOf('<');

        return index < 0 ? name : name.Substring(0, index);
    }

    /// <summary>Turns one attribute argument into the value and the expression that reproduces it.</summary>
    private static AttributeArgument Argument(TypedConstant constant)
    {
        if (constant.Value is ITypeSymbol type)
        {
            var name = type.ToDisplayString(Full);

            return new AttributeArgument(name, $"typeof({name})", true);
        }

        return new AttributeArgument(
            constant.Value is null ? null : Convert.ToString(constant.Value, CultureInfo.InvariantCulture),
            Expression(constant),
            false);
    }

    /// <summary>
    /// The C# expression that reproduces a constant.
    /// </summary>
    /// <remarks>
    /// A service key used to be emitted as a string literal whatever it was written as, so
    /// <c>Key = 1</c> became <c>"1"</c> and never matched an int-keyed resolution. The key
    /// keeps the type it was written with.
    /// </remarks>
    private static string Expression(TypedConstant constant)
    {
        if (constant.IsNull || constant.Value is null) return "null";

        var literal = SymbolDisplay.FormatPrimitive(constant.Value, quoteStrings: true, useHexadecimalNumbers: false);

        if (literal is null) return "null";

        // An enum key compares by Equals, and the boxed underlying number is not equal to the
        // boxed enum value, so the cast is what makes a keyed resolution find the service.
        if (constant.Kind == TypedConstantKind.Enum && constant.Type is not null)
            return $"(({constant.Type.ToDisplayString(Full)}){literal})";

        // A bare numeric literal is an int and a bare real is a double; without the suffix a
        // long or decimal key would be emitted as a different type than it was declared with.
        return constant.Value switch
        {
            long => literal + "L",
            ulong => literal + "UL",
            uint => literal + "U",
            float => literal + "F",
            decimal => literal + "M",
            _ => literal
        };
    }

    /// <summary>Applies the <c>FooRepository</c> to <c>IFooRepository</c> naming convention.</summary>
    /// <param name="typeName">The class's simple name.</param>
    /// <param name="candidates">The interfaces it may be registered under.</param>
    /// <returns>The matching interface, or null when none matches exactly.</returns>
    public static InterfaceUsage? DefaultInterface(string typeName, IEnumerable<InterfaceUsage> candidates)
    {
        var expected = "I" + typeName;

        foreach (var candidate in candidates)
        {
            var simple = candidate.OpenGenericName;
            var dot = simple.LastIndexOf('.');

            if (dot >= 0) simple = simple.Substring(dot + 1);

            // Exact, never EndsWith: 'UserService : IService, IUserService' matched IService
            // first, registered under it, and left IUserService unregistered entirely.
            if (string.Equals(simple, expected, StringComparison.Ordinal)) return candidate;
        }

        return null;
    }
}
