using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IQOne.Zero.Regify.Analyzers;

/// Handler'lar veri saglayicisindan BAGIMSIZ kalir.
///
/// EF'e ozgu her sey (EF.Functions, ToListAsync, Include, DbContext...) repository
/// katmaninda yasar; boylece saglayici degisimi handler'lara degil, repository'lere
/// ve saglayici paketine dokunur.
///
/// Analyzer URUNDEN BAGIMSIZ: aradigi handler arayuzu RegifyPlatformNamespace
/// ozelliginden turer, kodda urune ozgu bir metin yoktur.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerPersistenceIndependenceAnalyzer : DiagnosticAnalyzer
{
    private const string RootProperty = "build_property.RegifyPlatformNamespace";
    private const string DefaultRoot = "Platform";
    private const string HandlerSuffix = ".Messaging.Handlers.IServiceHandler";
    private const string BannedNamespace = "Microsoft.EntityFrameworkCore";

    public static readonly DiagnosticDescriptor Rule = new(
        id: "RGF011",
        title: "Handler veri saglayicisina bagimli olmamali",
        messageFormat: "'{0}' handler'i '{1}' kullaniyor; saglayiciya ozgu kod repository'de olmali",
        category: "Regify.Architecture",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Saglayiciya ozgu ifadeler repository katmaninda yasar; handler yalnizca " +
                     "saglayicidan bagimsiz LINQ ve IQueryExecutor kullanir.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var root = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                .TryGetValue(RootProperty, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value.Trim()
                    : DefaultRoot;

            var handlerInterface = root + HandlerSuffix;

            start.RegisterOperationAction(
                operation => Analyze(operation, handlerInterface),
                OperationKind.Invocation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.ObjectCreation);
        });
    }

    private static void Analyze(OperationAnalysisContext context, string handlerInterface)
    {
        var used = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod.ContainingType,
            IPropertyReferenceOperation property => property.Property.ContainingType,
            IFieldReferenceOperation field => field.Field.ContainingType,
            IObjectCreationOperation creation => creation.Type as INamedTypeSymbol,
            _ => null
        };

        if (used is null) return;

        var usedNamespace = used.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (!usedNamespace.StartsWith(BannedNamespace, StringComparison.Ordinal)) return;

        var enclosing = context.ContainingSymbol?.ContainingType;

        if (enclosing is null || !IsHandler(enclosing, handlerInterface)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, context.Operation.Syntax.GetLocation(), enclosing.Name, used.ToDisplayString()));
    }

    private static bool IsHandler(INamedTypeSymbol type, string handlerInterface)
        => type.AllInterfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString().StartsWith(handlerInterface, StringComparison.Ordinal));
}
