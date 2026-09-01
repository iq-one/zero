using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IQOne.Zero.Results.Analyzers;

/// <summary>
/// Reports results that are discarded, read without being checked, or thrown.
/// </summary>
/// <remarks>
/// All three mistakes compile and run. A discarded result silently swallows a failure; an
/// unchecked <c>Value</c> turns an expected failure back into an exception; a thrown one
/// breaks the promise the signature made. None has a symptom that points at the line that
/// caused it, which is what makes them worth a compiler diagnostic rather than a convention.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultUsageAnalyzer : DiagnosticAnalyzer
{
    private const string ResultType = "IQOne.Zero.Result";
    private const string GenericResultType = "IQOne.Zero.Result`1";
    private const string TaskType = "System.Threading.Tasks.Task`1";
    private const string ValueTaskType = "System.Threading.Tasks.ValueTask`1";

    /// <summary>
    /// Calls that look at the failure, so a chain ending in one has not thrown it away.
    /// </summary>
    /// <remarks>
    /// Matched by name rather than by symbol on purpose: an application that writes its own
    /// <c>OnFailure</c> has satisfied this rule just as well as one using the built-in
    /// <c>TapError</c>, and a false positive here is what teaches people to suppress the rule.
    /// </remarks>
    private static readonly ImmutableHashSet<string> Observers = ImmutableHashSet.Create(
        StringComparer.Ordinal, "TapError", "Match", "OnFailure", "IfFailure", "GetValueOr");

    /// <summary>
    /// Exceptions that stay exceptions even in a method that returns a result.
    /// </summary>
    /// <remarks>
    /// These say "this code is wrong" or "this is not the operation's outcome", which is
    /// exactly what ZERO102 wants left alone. Derived types are covered by walking the base
    /// chain, so <c>ObjectDisposedException</c> and <c>TaskCanceledException</c> are in here
    /// through their bases.
    /// </remarks>
    private static readonly ImmutableHashSet<string> Defects = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "System.ArgumentNullException",
        "System.InvalidOperationException",
        "System.NotImplementedException",
        "System.NotSupportedException",
        "System.OperationCanceledException",
        "System.Diagnostics.UnreachableException");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Diagnostics.Discarded, Diagnostics.UncheckedValue, Diagnostics.ThrownExpectedFailure];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var result = start.Compilation.GetTypeByMetadataName(ResultType);
            var generic = start.Compilation.GetTypeByMetadataName(GenericResultType);

            // Nothing to do in a compilation that does not use the package.
            if (result is null && generic is null) return;

            var known = new KnownTypes(
                result,
                generic,
                start.Compilation.GetTypeByMetadataName(TaskType),
                start.Compilation.GetTypeByMetadataName(ValueTaskType));

            start.RegisterOperationAction(c => Discarded(c, known), OperationKind.ExpressionStatement);
            start.RegisterOperationAction(c => UncheckedValue(c, known), OperationKind.PropertyReference);
            start.RegisterOperationAction(c => Thrown(c, known), OperationKind.Throw);
        });
    }

    private static void Discarded(OperationAnalysisContext context, KnownTypes known)
    {
        var statement = (IExpressionStatementOperation)context.Operation;

        // Only a call whose result nobody takes. An assignment, a return or an argument all
        // pass the value on, and any of those may legitimately ignore it later.
        var dropped = statement.Operation;

        // `_ = Foo();` is a discard assignment rather than a bare invocation. It is the form
        // people reach for to make the compiler stop complaining, so it is the one this rule
        // most needs to see.
        if (dropped is ISimpleAssignmentOperation { Target: IDiscardOperation } discard)
            dropped = discard.Value;

        // The type dropped is the type of the whole expression, not of the call inside it:
        // `await ApplyAsync(x);` drops a Result even though the call produced a Task<Result>.
        if (!known.IsResult(dropped.Type)) return;

        if (Unwrap(dropped) is not IInvocationOperation invocation) return;

        // A chain that handled the failure has read the outcome, even if it then drops the
        // value. Reporting the fix this rule's own documentation offers is how a rule stops
        // being believed.
        if (Observes(invocation)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.Discarded, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));
    }

    private static IOperation Unwrap(IOperation operation) => operation switch
    {
        IAwaitOperation await => Unwrap(await.Operation),
        IConversionOperation conversion => Unwrap(conversion.Operand),
        _ => operation
    };

    /// <summary>Whether anything in the call chain looked at the failure.</summary>
    private static bool Observes(IOperation? operation) => operation switch
    {
        IAwaitOperation await => Observes(await.Operation),
        IConversionOperation conversion => Observes(conversion.Operand),
        IInvocationOperation invocation => Observers.Contains(invocation.TargetMethod.Name)
                                           || Observes(Receiver(invocation)),
        _ => false
    };

    /// <summary>What the call was made on. An extension method carries it as its first argument.</summary>
    private static IOperation? Receiver(IInvocationOperation invocation)
        => invocation.Instance
           ?? (invocation.TargetMethod.IsExtensionMethod && invocation.Arguments.Length > 0
               ? invocation.Arguments[0].Value
               : null);

    private static void UncheckedValue(OperationAnalysisContext context, KnownTypes known)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;

        if (reference.Property.Name != "Value") return;
        if (!known.IsGenericResult(reference.Property.ContainingType)) return;

        // Reading Value inside the type's own members is how Value is implemented.
        if (known.IsGenericResult(context.ContainingSymbol?.ContainingType)) return;

        var subject = Subject(reference.Instance);

        // An expression with no name to track — a call chained straight into .Value — is
        // reported: there is nowhere a check could have happened.
        if (subject is not null && IsChecked(context, subject, known)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.UncheckedValue,
            reference.Syntax.GetLocation(),
            subject?.Name ?? reference.Syntax.ToString()));
    }

    /// <summary>
    /// Reports a method that promised its failures are values and then threw one.
    /// </summary>
    /// <remarks>
    /// The boundary is stated in docs/rules/ZERO102.md and implemented here: a bare rethrow,
    /// a throw while handling an exception, and the exceptions that mean "this code is
    /// wrong" are all left alone. What is left is a method choosing to raise a failure it
    /// could have returned.
    /// </remarks>
    private static void Thrown(OperationAnalysisContext context, KnownTypes known)
    {
        var thrown = (IThrowOperation)context.Operation;

        // `throw;` re-raises what was caught. Nothing new is being turned into an exception.
        if (thrown.Exception is null) return;

        // A throw while handling an exception is translating a failure that already arrived
        // as one. Whether it should have been a result is a question about whoever threw it.
        if (InCatch(thrown)) return;

        if (Unwrap(thrown.Exception).Type is not INamedTypeSymbol exception) return;
        if (IsDefect(exception)) return;

        var method = Enclosing(thrown, context);

        if (method is null || !known.ReturnsResult(method.ReturnType)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.ThrownExpectedFailure,
            thrown.Syntax.GetLocation(),
            Name(method, context),
            exception.Name));
    }

    /// <summary>Whether the throw is inside a catch clause of the same body.</summary>
    private static bool InCatch(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is ICatchClauseOperation) return true;

            // A lambda has its own body; a catch outside it says nothing about this throw.
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation) return false;
        }

        return false;
    }

    private static bool IsDefect(INamedTypeSymbol exception)
    {
        for (INamedTypeSymbol? type = exception; type is not null; type = type.BaseType)
            if (Defects.Contains(type.ToDisplayString()))
                return true;

        return false;
    }

    /// <summary>
    /// Whose promise the throw breaks.
    /// </summary>
    /// <remarks>
    /// A lambda or a local function makes its own promise: a <c>Func&lt;int&gt;</c> written
    /// inside a result-returning method never said its failures would be values.
    /// </remarks>
    private static IMethodSymbol? Enclosing(IOperation operation, OperationAnalysisContext context)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation lambda: return lambda.Symbol;
                case ILocalFunctionOperation local: return local.Symbol;
            }
        }

        return context.ContainingSymbol as IMethodSymbol;
    }

    private static string Name(IMethodSymbol method, OperationAnalysisContext context)
        => method.Name.Length > 0 ? method.Name : context.ContainingSymbol?.Name ?? "this method";

    /// <summary>The local or parameter the value is read from, when there is one.</summary>
    private static ISymbol? Subject(IOperation? instance) => instance switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IFieldReferenceOperation field => field.Field,
        IPropertyReferenceOperation property => property.Property,
        IConversionOperation conversion => Subject(conversion.Operand),
        _ => null
    };

    /// <summary>
    /// Whether the enclosing body checks this result anywhere.
    /// </summary>
    /// <remarks>
    /// Deliberately not a flow analysis. Asking "is this guarded on every path" produces
    /// false positives on patterns people write on purpose — an early return, a switch, a
    /// check in a helper. Asking "was it checked at all" catches the mistake this rule is
    /// about, which is forgetting entirely, and stays quiet otherwise.
    /// </remarks>
    private static bool IsChecked(OperationAnalysisContext context, ISymbol subject, KnownTypes known)
    {
        var body = context.Operation.Syntax.FirstAncestorOrSelf<Microsoft.CodeAnalysis.SyntaxNode>(
            n => n is Microsoft.CodeAnalysis.CSharp.Syntax.BaseMethodDeclarationSyntax
                   or Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax
                   or Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax
                   or Microsoft.CodeAnalysis.CSharp.Syntax.LambdaExpressionSyntax);

        if (body is null) return false;

        var model = context.Operation.SemanticModel!;

        foreach (var node in body.DescendantNodes())
        {
            if (node is not Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax access) continue;

            var name = access.Name.Identifier.ValueText;

            if (name is not ("IsSuccess" or "IsFailure" or "TryGetValue" or "TryGetError" or "Match")) continue;

            var accessed = model.GetSymbolInfo(access.Expression).Symbol;

            if (accessed is not null && SymbolEqualityComparer.Default.Equals(accessed, subject)) return true;
        }

        // A `switch` or `is` pattern over the result also counts as having looked at it.
        return body.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IsPatternExpressionSyntax>()
            .Any(pattern => SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(pattern.Expression).Symbol, subject));
    }

    private readonly struct KnownTypes(
        INamedTypeSymbol? result,
        INamedTypeSymbol? generic,
        INamedTypeSymbol? task,
        INamedTypeSymbol? valueTask)
    {
        public bool IsResult(ITypeSymbol? type)
            => type is INamedTypeSymbol named
            && (SymbolEqualityComparer.Default.Equals(named, result)
                || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, generic));

        public bool IsGenericResult(ITypeSymbol? type)
            => type is INamedTypeSymbol named
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, generic);

        /// <summary>Whether a signature promises a result, awaited or not.</summary>
        public bool ReturnsResult(ITypeSymbol? type)
        {
            if (type is not INamedTypeSymbol named) return false;

            if (named.Arity == 1
                && (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, task)
                    || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, valueTask)))
                return IsResult(named.TypeArguments[0]);

            return IsResult(named);
        }
    }
}
