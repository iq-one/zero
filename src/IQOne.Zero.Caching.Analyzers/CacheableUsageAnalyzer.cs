using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IQOne.Zero.Caching.Analyzers;

/// <summary>
/// Reports requests that ask to be cached but cannot be cached correctly.
/// </summary>
/// <remarks>
/// Both mistakes compile, run, and return something. A cached command silently stops doing
/// its work; a constant key silently serves one caller's answer to another. Neither leaves a
/// symptom that points back at the line responsible, which is what earns them a place in the
/// compiler's output rather than a paragraph in a document.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CacheableUsageAnalyzer : DiagnosticAnalyzer
{
    private const string CacheableType = "IQOne.Zero.Caching.ICacheable";
    private const string QueryType = "IQOne.Zero.Messaging.IQuery`1";
    private const string CacheKey = "CacheKey";
    private const string Lifetime = "Lifetime";
    private const string EqualityContract = "EqualityContract";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Diagnostics.NotAQuery, Diagnostics.ConstantKey];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var cacheable = start.Compilation.GetTypeByMetadataName(CacheableType);
            var query = start.Compilation.GetTypeByMetadataName(QueryType);

            // Nothing to do in a compilation that does not use the package.
            if (cacheable is null || query is null) return;

            start.RegisterSymbolAction(c => NotAQuery(c, cacheable, query), SymbolKind.NamedType);
            start.RegisterOperationBlockAction(c => ConstantKey(c, cacheable));
        });
    }

    private static void NotAQuery(SymbolAnalysisContext context, INamedTypeSymbol cacheable, INamedTypeSymbol query)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Only what a request actually is. An interface that gathers ICacheable with
        // something else is a shape a consumer is allowed to declare.
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return;
        if (!Implements(type, cacheable)) return;

        if (type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, query))) return;

        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NotAQuery, type.Locations[0], type.Name));
    }

    private static void ConstantKey(OperationBlockAnalysisContext context, INamedTypeSymbol cacheable)
    {
        var property = Property(context.OwningSymbol);

        if (property is null || property.Name != CacheKey) return;
        if (!Implements(property.ContainingType, cacheable)) return;

        // A query with nothing to vary on has nothing to leave out of its key.
        if (!TakesParameters(property.ContainingType)) return;
        if (!IsConstant(context.OperationBlocks)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.ConstantKey, property.Locations[0], property.ContainingType.Name));
    }

    /// <summary>The property behind an operation block: its getter's, or its initializer's.</summary>
    private static IPropertySymbol? Property(ISymbol owner) => owner switch
    {
        IMethodSymbol { MethodKind: MethodKind.PropertyGet } getter => getter.AssociatedSymbol as IPropertySymbol,
        IPropertySymbol property => property,
        _ => null
    };

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol @interface)
        => type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, @interface));

    /// <summary>Whether the request carries anything its answer could depend on.</summary>
    private static bool TakesParameters(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is not IPropertySymbol { IsStatic: false, IsIndexer: false } property) continue;
            if (property.DeclaredAccessibility != Accessibility.Public) continue;
            if (property.Name is CacheKey or Lifetime or EqualityContract) continue;

            return true;
        }

        // A class that keeps its constructor arguments in private fields still varies by them.
        return type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared && c.Parameters.Length > 0);
    }

    /// <summary>Whether the key is the same string every time.</summary>
    /// <remarks>
    /// One way out, holding a constant. Two returns mean the key already varies with
    /// something, and a conditional expression is not constant at all — both are the author
    /// having thought about it, which is all this rule is asking for.
    /// </remarks>
    private static bool IsConstant(ImmutableArray<IOperation> blocks)
    {
        IOperation? only = null;

        foreach (var block in blocks)
        {
            if (block is IPropertyInitializerOperation initializer) return initializer.Value.ConstantValue.HasValue;

            foreach (var operation in Walk(block))
            {
                if (operation is not IReturnOperation { ReturnedValue: { } returned }) continue;
                if (only is not null) return false;

                only = returned;
            }
        }

        return only?.ConstantValue.HasValue == true;
    }

    private static IEnumerable<IOperation> Walk(IOperation operation)
    {
        yield return operation;

        foreach (var child in operation.ChildOperations)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }
}
