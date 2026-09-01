using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IQOne.Zero.Results.Analyzers;

/// <summary>
/// Reports results that are discarded or read without being checked.
/// </summary>
/// <remarks>
/// Both mistakes compile and run. A discarded result silently swallows a failure; an
/// unchecked <c>Value</c> turns an expected failure back into an exception. Neither has a
/// symptom that points at the line that caused it, which is what makes them worth a
/// compiler error rather than a convention.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultUsageAnalyzer : DiagnosticAnalyzer
{
    private const string ResultType = "IQOne.Zero.Results.Result";
    private const string GenericResultType = "IQOne.Zero.Results.Result`1";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Diagnostics.Discarded, Diagnostics.UncheckedValue];

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

            var known = new KnownTypes(result, generic);

            start.RegisterOperationAction(c => Discarded(c, known), OperationKind.ExpressionStatement);
            start.RegisterOperationAction(c => UncheckedValue(c, known), OperationKind.PropertyReference);
        });
    }

    private static void Discarded(OperationAnalysisContext context, KnownTypes known)
    {
        var statement = (IExpressionStatementOperation)context.Operation;

        // Only a call whose result nobody takes. An assignment, a return or an argument all
        // pass the value on, and any of those may legitimately ignore it later.
        var operation = Unwrap(statement.Operation);

        if (operation is not IInvocationOperation invocation) return;
        if (!known.IsResult(invocation.Type)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.Discarded, invocation.Syntax.GetLocation(), invocation.TargetMethod.Name));
    }

    private static IOperation Unwrap(IOperation operation) => operation switch
    {
        IAwaitOperation await => Unwrap(await.Operation),
        IConversionOperation conversion => Unwrap(conversion.Operand),
        _ => operation
    };

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

            if (name is not ("IsSuccess" or "IsFailure" or "TryGetValue" or "Match")) continue;

            var accessed = model.GetSymbolInfo(access.Expression).Symbol;

            if (accessed is not null && SymbolEqualityComparer.Default.Equals(accessed, subject)) return true;
        }

        // A `switch` or `is` pattern over the result also counts as having looked at it.
        return body.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.IsPatternExpressionSyntax>()
            .Any(pattern => SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(pattern.Expression).Symbol, subject));
    }

    private readonly struct KnownTypes(INamedTypeSymbol? result, INamedTypeSymbol? generic)
    {
        public bool IsResult(ITypeSymbol? type)
            => type is INamedTypeSymbol named
            && (SymbolEqualityComparer.Default.Equals(named, result)
                || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, generic));

        public bool IsGenericResult(ITypeSymbol? type)
            => type is INamedTypeSymbol named
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, generic);
    }
}
