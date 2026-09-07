using System.Collections.Immutable;
using IQOne.Zero.Generators.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IQOne.Zero.Generators.Mapping;

/// <summary>
/// One member's answer, before composition is resolved.
/// </summary>
/// <remarks>
/// Either it is already an expression — a name match or a plain declaration — or it defers to
/// another map, which cannot be resolved until every map in the compilation has been read.
/// </remarks>
/// <param name="Member">The member being filled.</param>
/// <param name="Expression">Its source, carrying the placeholder; empty when composed.</param>
/// <param name="Receiver">Composed: what the other map reads, carrying the placeholder.</param>
/// <param name="Pair">Composed: the other map's pair key.</param>
/// <param name="Element">Composed: the lambda parameter for a sequence's element, else empty.</param>
/// <param name="Materialiser">Composed sequence: <c>ToList</c> or <c>ToArray</c>.</param>
/// <param name="Guard">Composed: wrap in a null check because the receiver can be absent.</param>
/// <param name="Type">Composed: the member's type, for the null branch.</param>
internal readonly record struct Binding(
    string Member,
    string Expression,
    string Receiver,
    string Pair,
    string Element,
    string Materialiser,
    bool Guard,
    string Type)
{
    public bool Composed => Pair.Length > 0;

    public static Binding Plain(string member, string expression)
        => new(member, expression, string.Empty, string.Empty, string.Empty, string.Empty, false, string.Empty);
}

/// <summary>
/// Reads a map's <c>Configure</c> body.
/// </summary>
/// <remarks>
/// <para>
/// Configure is READ, not run, so this is where the design earns its keep — and where it has
/// to be strict. What can be read is a plain sequence of <c>Member</c> and <c>Ignore</c> calls;
/// anything that would decide at run time what to map has no single answer to read, and is
/// reported rather than guessed at.
/// </para>
/// <para>
/// Every expression is rewritten onto <see cref="Placeholder"/> rather than onto a chosen
/// parameter name, because at this point the name is not knowable: a map's own tree uses one
/// name, and the same expression spliced into a parent has to read from wherever the parent
/// found it. The substitution happens once, at the end, when both are known.
/// </para>
/// <para>
/// The rewriting itself follows BINDING, not name. A declaration is written against its own
/// parameter — <c>e</c>, <c>x</c>, <c>bed</c> — and a nested lambda inside it may shadow that
/// name; renaming by text there would change what the inner expression reads.
/// </para>
/// </remarks>
internal static class Declarations
{
    private const string BuilderName = "IQOne.Zero.Mapping.IMapBuilder`2";
    private const string ComposeName = "IQOne.Zero.Mapping.Compose";

    /// <summary>Stands in for whatever the expression will read from.</summary>
    /// <remarks>
    /// Chosen so it cannot occur in real code; a body that uses it anyway is reported rather
    /// than silently rewritten, because the substitution would change what it reads.
    /// </remarks>
    public const string Placeholder = "__zero_source";

    /// <summary>What the body declares, or the reason it cannot be read.</summary>
    /// <param name="configure">The Configure method.</param>
    /// <param name="model">The semantic model over it.</param>
    /// <param name="bindings">One entry per member the body answers for.</param>
    /// <param name="ignored">Members declared deliberately empty.</param>
    /// <param name="duplicate">The first member declared twice, when there is one.</param>
    /// <returns>Null when the body was read.</returns>
    public static string? Read(
        MethodDeclarationSyntax configure,
        SemanticModel model,
        out List<Binding> bindings,
        out List<string> ignored,
        out string? duplicate)
    {
        bindings = [];
        ignored = [];
        duplicate = null;

        var calls = new List<InvocationExpressionSyntax>();

        var reason = Chain(configure, model, calls);

        if (reason is not null) return reason;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Source order, so the generated initialiser reads in the order it was declared.
        foreach (var call in calls)
        {
            if (Name(call) == "Ignore")
            {
                foreach (var argument in call.ArgumentList.Arguments)
                {
                    var member = Named(argument.Expression, model, out var why);

                    if (member is null) return why;

                    if (!seen.Add(member.Name)) duplicate ??= member.Name;

                    ignored.Add(member.Name);
                }

                continue;
            }

            if (call.ArgumentList.Arguments.Count != 2)
                return "map.Member takes a member and its source, but was given " +
                       $"{call.ArgumentList.Arguments.Count} argument(s)";

            var target = Named(call.ArgumentList.Arguments[0].Expression, model, out var reasonForMember);

            if (target is null) return reasonForMember;

            var binding = Bound(
                target, call.ArgumentList.Arguments[1].Expression, model, out var reasonForSource);

            if (binding is null) return reasonForSource;

            if (!seen.Add(target.Name)) duplicate ??= target.Name;

            bindings.Add(binding.Value);
        }

        return null;
    }

    /// <summary>The pair key two types make.</summary>
    /// <remarks>
    /// Fully qualified on both sides, so two models of the same name in different namespaces —
    /// which this application has several of — are different pairs.
    /// </remarks>
    public static string Key(ITypeSymbol source, ITypeSymbol destination)
        => $"{source.ToDisplayString(Full)}|{destination.ToDisplayString(Full)}";

    /// <summary>
    /// Collects the Member and Ignore calls, or says what else the body contains.
    /// </summary>
    private static string? Chain(
        MethodDeclarationSyntax configure, SemanticModel model, List<InvocationExpressionSyntax> calls)
    {
        if (configure.ExpressionBody is { } arrow) return Link(arrow.Expression, model, calls);

        if (configure.Body is not { } block) return "Configure has no body";

        foreach (var statement in block.Statements)
        {
            if (statement is not ExpressionStatementSyntax expression)
                return $"'{Describe(statement)}' is not a map.Member or map.Ignore call";

            var reason = Link(expression.Expression, model, calls);

            if (reason is not null) return reason;
        }

        return null;
    }

    /// <summary>Walks one chain from its end back to the builder, in source order.</summary>
    private static string? Link(
        ExpressionSyntax expression, SemanticModel model, List<InvocationExpressionSyntax> calls)
    {
        var found = new List<InvocationExpressionSyntax>();

        var current = expression;

        while (true)
        {
            if (current is not InvocationExpressionSyntax invocation)
            {
                if (current is not IdentifierNameSyntax || !IsBuilder(current, model))
                    return $"'{current}' is not a map.Member or map.Ignore call";

                // Walked from the end, so reversing puts them back in written order.
                found.Reverse();
                calls.AddRange(found);

                return null;
            }

            if (invocation.Expression is not MemberAccessExpressionSyntax access)
                return $"'{invocation}' is not a map.Member or map.Ignore call";

            var name = access.Name.Identifier.ValueText;

            if (name is not ("Member" or "Ignore"))
                return $"'{name}' is not one of map.Member or map.Ignore";

            if (!OnBuilder(invocation, model))
                return $"'{name}' is not a call on the map builder";

            found.Add(invocation);

            current = access.Expression;
        }
    }

    /// <summary>Whether this invocation is one of the builder's own methods.</summary>
    /// <remarks>
    /// Candidates as well as the resolved symbol, because a declaration whose ARGUMENTS do not
    /// compile — a lambda over an extension method the file never imported — resolves to
    /// nothing, and reporting "this is not a builder call" there would bury the compiler's own
    /// error under a misleading one. The call is recognised; the compiler says what is wrong
    /// with it.
    /// </remarks>
    private static bool OnBuilder(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var info = model.GetSymbolInfo(invocation);

        var symbols = info.Symbol is not null ? [info.Symbol] : info.CandidateSymbols;

        foreach (var symbol in symbols)
            if (symbol is IMethodSymbol method && Closed(method.ContainingType) == BuilderName)
                return true;

        return false;
    }

    private static bool IsBuilder(ExpressionSyntax expression, SemanticModel model)
        => model.GetTypeInfo(expression).Type is INamedTypeSymbol type && Closed(type) == BuilderName;

    private static string Closed(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;

        return definition.ContainingNamespace.IsGlobalNamespace
            ? definition.MetadataName
            : $"{definition.ContainingNamespace.ToDisplayString()}.{definition.MetadataName}";
    }

    private static string Name(InvocationExpressionSyntax call)
        => ((MemberAccessExpressionSyntax)call.Expression).Name.Identifier.ValueText;

    /// <summary>The member a <c>m =&gt; m.X</c> lambda names.</summary>
    private static IPropertySymbol? Named(ExpressionSyntax expression, SemanticModel model, out string? reason)
    {
        reason = null;

        if (expression is not LambdaExpressionSyntax lambda)
        {
            reason = $"'{expression}' is not a lambda naming a member";

            return null;
        }

        // Ignore's parameter is Func<TDestination, object?>, so a value-typed member arrives
        // wrapped in the conversion the compiler inserted, which says nothing about which
        // member was named.
        var body = lambda.Body;

        while (true)
        {
            if (body is CastExpressionSyntax cast) { body = cast.Expression; continue; }
            if (body is ParenthesizedExpressionSyntax nested) { body = nested.Expression; continue; }

            break;
        }

        if (body is not MemberAccessExpressionSyntax access)
        {
            reason = $"'{body}' is not a member of the shape being produced";

            return null;
        }

        if (model.GetSymbolInfo(access).Symbol is not IPropertySymbol property)
        {
            reason = $"'{access}' does not resolve to a property";

            return null;
        }

        return property;
    }

    /// <summary>What fills the member: an expression, or another map.</summary>
    private static Binding? Bound(
        IPropertySymbol member, ExpressionSyntax expression, SemanticModel model, out string? reason)
    {
        reason = null;

        if (expression is not LambdaExpressionSyntax lambda)
        {
            reason = $"'{expression}' is not a lambda";

            return null;
        }

        if (lambda.Body is not ExpressionSyntax body)
        {
            reason = $"'{expression}' has a statement body; a map's source has to be one expression";

            return null;
        }

        if (model.GetSymbolInfo(lambda).Symbol is not IMethodSymbol { Parameters.Length: 1 } method)
        {
            reason = $"'{expression}' does not resolve to a lambda with one parameter";

            return null;
        }

        var parameter = method.Parameters[0];

        if (Uses(body, Placeholder))
        {
            reason = $"'{expression}' uses the name '{Placeholder}', which the generator " +
                     "substitutes; rename it";

            return null;
        }

        return Composition(member, body, parameter, model, out reason)
            ?? (reason is not null
                ? null
                : Binding.Plain(member.Name, Rewritten(body, parameter, model)));
    }

    /// <summary>
    /// The composition a <c>To&lt;T&gt;()</c> call declares, or null when the body is not one.
    /// </summary>
    /// <remarks>
    /// Only as the OUTERMOST call: a composition is what the whole member is, and one buried
    /// inside a larger expression would have to be spliced into a position the generator cannot
    /// see the type of. Buried ones are reported rather than ignored.
    /// </remarks>
    private static Binding? Composition(
        IPropertySymbol member,
        ExpressionSyntax body,
        IParameterSymbol parameter,
        SemanticModel model,
        out string? reason)
    {
        reason = null;

        var call = Marker(body, model);

        if (call is null)
        {
            if (body.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => Marker(i, model) is not null))
                reason = $"'{body}' has a To<...>() inside a larger expression; a composition has " +
                         "to be the whole member";

            return null;
        }

        var access = (MemberAccessExpressionSyntax)call.Expression;
        var receiverType = model.GetTypeInfo(access.Expression).Type;
        var wanted = ((IMethodSymbol)model.GetSymbolInfo(call).Symbol!).TypeArguments[0];

        if (receiverType is null)
        {
            reason = $"'{access.Expression}' has no type the generator could read";

            return null;
        }

        var receiver = Rewritten(access.Expression, parameter, model);

        var element = Element(wanted);

        if (element is null)
        {
            if (Element(receiverType) is not null)
            {
                reason = $"'{access.Expression}' is a sequence but To<{wanted.Name}> asks for one " +
                         "object; name a collection type";

                return null;
            }

            if (receiverType.IsValueType && receiverType is INamedTypeSymbol
                { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            {
                reason = $"'{access.Expression}' is a nullable value; composition maps objects " +
                         "and sequences";

                return null;
            }

            // The receiver may be absent, and then the member is nothing rather than an object
            // whose members are all zero — which is what a nested initialiser gives you.
            var guard = !receiverType.IsValueType;

            if (guard && member.NullableAnnotation == NullableAnnotation.NotAnnotated)
            {
                reason = $"'{access.Expression}' can be absent and '{member.Name}' is not " +
                         "nullable; say what an absent value becomes";

                return null;
            }

            return new Binding(
                member.Name, string.Empty, receiver, Key(receiverType, wanted), string.Empty,
                string.Empty, guard, member.Type.ToDisplayString(Full));
        }

        var source = Element(receiverType);

        if (source is null)
        {
            reason = $"To<{wanted.Name}> asks for a collection but '{access.Expression}' is not " +
                     "a sequence";

            return null;
        }

        var materialiser = Materialiser(wanted);

        if (materialiser is null)
        {
            reason = $"'{wanted.ToDisplayString()}' is not a collection the generator can fill; " +
                     "use a list, an array, or one of the read-only collection interfaces";

            return null;
        }

        // No guard on a sequence, and that is a decision with a reason: in a query the receiver
        // is a subquery and never null, and an entity materialised by the provider has its
        // collections initialised. Guarding would put a check in every SELECT for a case that
        // does not arise.
        return new Binding(
            member.Name, string.Empty, receiver, Key(source, element), Element(receiver),
            materialiser, false, member.Type.ToDisplayString(Full));
    }

    private static InvocationExpressionSyntax? Marker(ExpressionSyntax expression, SemanticModel model)
        => expression is InvocationExpressionSyntax
           {
               Expression: MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "To" } }
           } call
           && model.GetSymbolInfo(call).Symbol is IMethodSymbol { TypeArguments.Length: 1 } method
           && method.ContainingType.ToDisplayString() == ComposeName
            ? call
            : null;

    /// <summary>The element type of a sequence, or null when it is not one.</summary>
    /// <remarks>
    /// A string is a sequence of characters and is never meant as one here, so it is excluded
    /// before anything else.
    /// </remarks>
    private static ITypeSymbol? Element(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String) return null;

        if (type is IArrayTypeSymbol array) return array.ElementType;

        if (type is INamedTypeSymbol { IsGenericType: true } named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            return named.TypeArguments[0];

        foreach (var contract in (type as INamedTypeSymbol)?.AllInterfaces ?? [])
            if (contract.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                return contract.TypeArguments[0];

        return null;
    }

    /// <summary>How a sequence becomes the collection the member holds.</summary>
    private static string? Materialiser(ITypeSymbol wanted)
    {
        if (wanted is IArrayTypeSymbol) return "ToArray";

        var name = (wanted as INamedTypeSymbol)?.OriginalDefinition.ToDisplayString();

        return name switch
        {
            "System.Collections.Generic.List<T>" or
            "System.Collections.Generic.IList<T>" or
            "System.Collections.Generic.ICollection<T>" or
            "System.Collections.Generic.IEnumerable<T>" or
            "System.Collections.Generic.IReadOnlyList<T>" or
            "System.Collections.Generic.IReadOnlyCollection<T>" => "ToList",
            _ => null
        };
    }

    /// <summary>A lambda parameter for a sequence's element, unique to this receiver.</summary>
    /// <remarks>
    /// Derived from the receiver text so that two compositions in one map, and a composition
    /// inside another, never share a name.
    /// </remarks>
    private static string Element(string receiver)
    {
        unchecked
        {
            var hash = 17;

            foreach (var c in receiver) hash = hash * 31 + c;

            return $"{Placeholder}_{(uint)hash % 100000}";
        }
    }

    /// <summary>The expression with its own parameter rewritten onto the placeholder.</summary>
    private static string Rewritten(ExpressionSyntax body, IParameterSymbol parameter, SemanticModel model)
    {
        var renamed = new List<SyntaxToken>();

        foreach (var identifier in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, parameter))
                renamed.Add(identifier.Identifier);

        return body
            .ReplaceTokens(renamed, (original, _) => SyntaxFactory.Identifier(Placeholder).WithTriviaFrom(original))
            .ToFullString()
            .Trim();
    }

    private static bool Uses(ExpressionSyntax body, string name)
        => body.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(i => i.Identifier.ValueText == name);

    private static string Describe(StatementSyntax statement)
    {
        var text = statement.ToString().Trim();

        // netstandard2.0: no Index/Range.
        return text.Length <= 60 ? text : text.Substring(0, 57) + "...";
    }

    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;
}
