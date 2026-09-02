using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IQOne.Zero.BackgroundWork.Analyzers;

/// <summary>
/// Reports a recurring job that reasons about the wrong moment, or that cannot be stopped.
/// </summary>
/// <remarks>
/// Only inside <c>IRecurringJob.RunAsync</c>. Reading a clock is perfectly ordinary
/// elsewhere; what makes it wrong here is that the method was handed the occurrence it is
/// serving and chose a different answer.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RecurringJobAnalyzer : DiagnosticAnalyzer
{
    private const string JobInterface = "IQOne.Zero.BackgroundWork.IRecurringJob";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Diagnostics.ReadsTheClock, Diagnostics.IgnoresCancellation];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var job = start.Compilation.GetTypeByMetadataName(JobInterface);

            // Nothing to do in a compilation that does not use the package.
            if (job is null) return;

            start.RegisterSymbolStartAction(symbol => Method(symbol, job), SymbolKind.Method);
        });
    }

    private static void Method(SymbolStartAnalysisContext context, INamedTypeSymbol job)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (method.Name != "RunAsync") return;
        if (!Implements(method, job)) return;

        var token = method.Parameters.FirstOrDefault(
            p => p.Type.ToDisplayString() == "System.Threading.CancellationToken");

        var usesToken = false;
        var reportedClock = false;

        context.RegisterSyntaxNodeAction(
            node =>
            {
                if (token is not null && !usesToken && Names(node.Node, token.Name)) usesToken = true;

                if (reportedClock) return;

                if (Clock(node.Node, node.SemanticModel) is not { } location) return;

                reportedClock = true;

                node.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ReadsTheClock, location, method.ContainingType.Name));
            },
            SyntaxKind.IdentifierName,
            SyntaxKind.SimpleMemberAccessExpression);

        context.RegisterSymbolEndAction(end =>
        {
            // A method with no body -- an interface declaration, or a partial -- has nothing
            // to inspect, and a job that genuinely finishes instantly has nothing to pass a
            // token to. Both would be noise.
            if (token is null || usesToken) return;
            if (method.DeclaringSyntaxReferences.Length == 0) return;

            if (method.DeclaringSyntaxReferences[0].GetSyntax() is not MethodDeclarationSyntax declaration
                || (declaration.Body is null && declaration.ExpressionBody is null))
                return;

            end.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.IgnoresCancellation,
                token.Locations.FirstOrDefault() ?? Location.None,
                method.ContainingType.Name));
        });
    }

    /// <summary>Whether this method is the interface's <c>RunAsync</c>.</summary>
    private static bool Implements(IMethodSymbol method, INamedTypeSymbol job)
    {
        if (method.ContainingType.AllInterfaces.Any(i =>
                SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, job)) is false)
            return false;

        return method.ExplicitInterfaceImplementations.Length > 0
            || method.DeclaredAccessibility == Accessibility.Public;
    }

    private static bool Names(SyntaxNode node, string name)
        => node is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == name;

    /// <summary>The location of a clock read, or null when this node is not one.</summary>
    private static Location? Clock(SyntaxNode node, SemanticModel model)
    {
        if (node is not MemberAccessExpressionSyntax access) return null;

        var name = access.Name.Identifier.ValueText;

        if (name is not ("Now" or "UtcNow" or "Today" or "GetUtcNow" or "GetLocalNow")) return null;

        var symbol = model.GetSymbolInfo(access).Symbol;

        var owner = symbol?.ContainingType?.ToDisplayString();

        return owner is "System.DateTime" or "System.DateTimeOffset" or "System.TimeProvider"
            ? access.GetLocation()
            : null;
    }
}
