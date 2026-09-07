using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IQOne.Zero.Generators.Mapping;

/// <summary>One <c>map.Member</c>: a member, and the source text that fills it.</summary>
/// <param name="Member">The member being filled.</param>
/// <param name="Expression">Its source, already rewritten onto the generated parameter.</param>
internal readonly record struct Declared(string Member, string Expression);

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
/// The rewriting is the interesting part. A declaration is written against its own parameter
/// name — <c>e</c>, <c>x</c>, <c>bed</c>, whatever the author chose — and the generated tree
/// has one parameter for all of them. Every identifier that BINDS to the declaration's
/// parameter is renamed; binding rather than name, so a nested lambda that shadows the name
/// keeps its own meaning.
/// </para>
/// </remarks>
internal static class Declarations
{
    private const string BuilderName = "IQOne.Zero.Mapping.IMapBuilder`2";

    /// <summary>Candidate names for the generated parameter, in order of preference.</summary>
    /// <remarks>
    /// A declaration body may itself mention something called <c>source</c> — a field, a local
    /// captured from outside. Renaming onto that name would silently change what the expression
    /// reads, so a name nothing else in the body uses is chosen instead.
    /// </remarks>
    private static readonly string[] Names = ["source", "src", "__source"];

    /// <summary>What the body declares, or the reason it cannot be read.</summary>
    /// <param name="configure">The Configure method.</param>
    /// <param name="model">The semantic model over it.</param>
    /// <param name="parameter">The name chosen for the generated parameter.</param>
    /// <param name="members">Members filled by a declaration.</param>
    /// <param name="ignored">Members declared deliberately empty.</param>
    /// <param name="duplicate">The first member declared twice, when there is one.</param>
    /// <returns>Null when the body was read.</returns>
    public static string? Read(
        MethodDeclarationSyntax configure,
        SemanticModel model,
        out string parameter,
        out List<Declared> members,
        out List<string> ignored,
        out string? duplicate)
    {
        parameter = Names[0];
        members = [];
        ignored = [];
        duplicate = null;

        var calls = new List<InvocationExpressionSyntax>();

        var reason = Chain(configure, model, calls);

        if (reason is not null) return reason;

        // Named before rewriting: the choice has to hold for every declaration at once.
        parameter = Parameter(calls);

        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Source order, so the generated initialiser reads in the order it was declared.
        foreach (var call in calls)
        {
            var name = Name(call);

            if (name == "Ignore")
            {
                foreach (var argument in call.ArgumentList.Arguments)
                {
                    var target = Target(argument.Expression, model, out var why);

                    if (target is null) return why;

                    if (!seen.Add(target)) duplicate ??= target;

                    ignored.Add(target);
                }

                continue;
            }

            if (call.ArgumentList.Arguments.Count != 2)
                return $"map.Member takes a member and its source, but was given " +
                       $"{call.ArgumentList.Arguments.Count} argument(s)";

            var member = Target(call.ArgumentList.Arguments[0].Expression, model, out var reasonForMember);

            if (member is null) return reasonForMember;

            var expression = Rewritten(
                call.ArgumentList.Arguments[1].Expression, model, parameter, out var reasonForSource);

            if (expression is null) return reasonForSource;

            if (!seen.Add(member)) duplicate ??= member;

            members.Add(new Declared(member, expression));
        }

        return null;
    }

    /// <summary>
    /// Collects the Member and Ignore calls, or says what else the body contains.
    /// </summary>
    /// <remarks>
    /// Both shapes a body can take are accepted — an arrow over a single chain, and a block of
    /// statements — because both are how somebody would naturally write this. Everything else
    /// is refused by name, so the message says what was found rather than that something was.
    /// </remarks>
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

    /// <summary>
    /// Walks one chain from its end back to the builder, collecting calls in source order.
    /// </summary>
    private static string? Link(
        ExpressionSyntax expression, SemanticModel model, List<InvocationExpressionSyntax> calls)
    {
        var found = new List<InvocationExpressionSyntax>();

        var current = expression;

        while (true)
        {
            if (current is not InvocationExpressionSyntax invocation)
                return current is IdentifierNameSyntax && IsBuilder(current, model)
                    ? Done(found, calls)
                    : $"'{current}' is not a map.Member or map.Ignore call";

            if (invocation.Expression is not MemberAccessExpressionSyntax access)
                return $"'{invocation}' is not a map.Member or map.Ignore call";

            var name = access.Name.Identifier.ValueText;

            if (name is not ("Member" or "Ignore"))
                return $"'{name}' is not one of map.Member or map.Ignore";

            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method
                || Closed(method.ContainingType) != BuilderName)
                return $"'{name}' is not a call on the map builder";

            found.Add(invocation);

            current = access.Expression;
        }
    }

    private static string? Done(List<InvocationExpressionSyntax> found, List<InvocationExpressionSyntax> calls)
    {
        // Walked from the end, so reversed puts them back in the order they were written.
        found.Reverse();
        calls.AddRange(found);

        return null;
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
    private static string? Target(ExpressionSyntax expression, SemanticModel model, out string? reason)
    {
        reason = null;

        if (expression is not LambdaExpressionSyntax lambda)
        {
            reason = $"'{expression}' is not a lambda naming a member";

            return null;
        }

        // The compiler inserts a conversion to object on Ignore's parameters, so the body of a
        // value-typed member arrives wrapped in a cast that says nothing about which member.
        var body = lambda.Body;

        while (body is CastExpressionSyntax cast) body = cast.Expression;
        while (body is ParenthesizedExpressionSyntax parenthesised) body = parenthesised.Expression;

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

        return property.Name;
    }

    /// <summary>
    /// A declaration's source expression, with its own parameter renamed to the generated one.
    /// </summary>
    private static string? Rewritten(
        ExpressionSyntax expression, SemanticModel model, string parameter, out string? reason)
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

        var symbol = Symbol(lambda, model);

        if (symbol is null)
        {
            reason = $"'{expression}' does not resolve to a lambda with one parameter";

            return null;
        }

        var renamed = new List<SyntaxToken>();

        foreach (var identifier in body.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            // BINDING, not name: a nested lambda may shadow the parameter's name, and renaming
            // by text there would change what the inner expression reads.
            if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, symbol))
                continue;

            renamed.Add(identifier.Identifier);
        }

        var rewritten = body.ReplaceTokens(
            renamed, (original, _) => SyntaxFactory.Identifier(parameter).WithTriviaFrom(original));

        return rewritten.ToFullString().Trim();
    }

    private static IParameterSymbol? Symbol(LambdaExpressionSyntax lambda, SemanticModel model)
        => model.GetSymbolInfo(lambda).Symbol is IMethodSymbol { Parameters.Length: 1 } method
            ? method.Parameters[0]
            : null;

    /// <summary>A parameter name none of the declarations already uses for something else.</summary>
    private static string Parameter(List<InvocationExpressionSyntax> calls)
    {
        var used = new HashSet<string>(
            calls
                .SelectMany(c => c.ArgumentList.Arguments)
                .SelectMany(a => a.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                .Select(i => i.Identifier.ValueText),
            StringComparer.Ordinal);

        foreach (var name in Names)
            if (!used.Contains(name)) return name;

        // Every candidate is spoken for, which takes a body written to collide on purpose.
        return "__source" + calls.Count;
    }

    private static string Describe(StatementSyntax statement)
    {
        var text = statement.ToString().Trim();

        // netstandard2.0: no Index/Range.
        return text.Length <= 60 ? text : text.Substring(0, 57) + "...";
    }
}
