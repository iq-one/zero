using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IQOne.Zero.Resilience.Analyzers;

/// <summary>
/// Reports a request handler that loops around a wait and tries again.
/// </summary>
/// <remarks>
/// <para>
/// The mistake is not that the loop is wrong — it usually works. It is that it runs in the
/// wrong place. A handler sits inside the transaction the pipeline opened, so its second
/// attempt reuses a scope the first one may already have poisoned; it sits inside the
/// activity, so three attempts are recorded as one slow request; and it decides on its own
/// that the request is safe to repeat, which for a command is a decision that belongs in the
/// request's own declaration rather than in the body of the code doing the repeating.
/// </para>
/// <para>
/// Reported rather than merely documented because there is nothing to notice at run time.
/// The tests pass, the request succeeds, and the only evidence is the day two of something
/// happens instead of one.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandRolledRetryAnalyzer : DiagnosticAnalyzer
{
    private const string HandlerType = "IQOne.Zero.Messaging.IRequestHandler";
    private const string TaskType = "System.Threading.Tasks.Task";
    private const string ThreadType = "System.Threading.Thread";
    private const string Delay = "Delay";
    private const string Sleep = "Sleep";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Diagnostics.HandRolledRetry];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var handler = start.Compilation.GetTypeByMetadataName(HandlerType);

            // Nothing to do in a compilation that has no handlers in it.
            if (handler is null) return;

            var task = start.Compilation.GetTypeByMetadataName(TaskType);
            var thread = start.Compilation.GetTypeByMetadataName(ThreadType);

            start.RegisterOperationBlockAction(c => Inspect(c, handler, task, thread));
        });
    }

    private static void Inspect(
        OperationBlockAnalysisContext context,
        INamedTypeSymbol handler,
        INamedTypeSymbol? task,
        INamedTypeSymbol? thread)
    {
        var type = context.OwningSymbol.ContainingType;

        if (type is null) return;
        if (!type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, handler))) return;

        foreach (var block in context.OperationBlocks)
        {
            foreach (var loop in OutermostLoops(block))
            {
                if (!WaitsAndTriesAgain(loop, task, thread)) continue;

                // The keyword rather than the whole statement: a squiggle under twenty lines
                // of loop body says less about what is wrong than one under `while`.
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.HandRolledRetry, loop.Syntax.GetFirstToken().GetLocation(), type.Name));
            }
        }
    }

    /// <summary>The loops in a block that are not inside another loop.</summary>
    /// <remarks>
    /// A retry loop containing a <c>foreach</c> is one mistake, not two, and reporting the
    /// inner loop as well would put the second squiggle on the line least responsible for it.
    /// </remarks>
    private static IEnumerable<ILoopOperation> OutermostLoops(IOperation operation)
    {
        if (operation is ILoopOperation loop)
        {
            yield return loop;

            yield break;
        }

        foreach (var child in operation.ChildOperations)
            foreach (var nested in OutermostLoops(child))
                yield return nested;
    }

    /// <summary>Whether the loop both pauses and has a way of stopping early.</summary>
    /// <remarks>
    /// Both, because either on its own is ordinary code. A loop that pauses without an early
    /// exit is a paced batch — questionable in a handler, but not a retry. A loop with an
    /// early exit and no pause is a search. The pair is what makes it "try, and if that did
    /// not work, wait and try again", which is this package's job.
    /// </remarks>
    private static bool WaitsAndTriesAgain(ILoopOperation loop, INamedTypeSymbol? task, INamedTypeSymbol? thread)
    {
        var waits = false;
        var stopsEarly = false;

        foreach (var operation in Inside(loop))
        {
            waits = waits || IsWait(operation, task, thread);
            stopsEarly = stopsEarly || StopsEarly(operation);

            if (waits && stopsEarly) return true;
        }

        return false;
    }

    private static bool IsWait(IOperation operation, INamedTypeSymbol? task, INamedTypeSymbol? thread)
    {
        if (operation is not IInvocationOperation invocation) return false;

        var owner = invocation.TargetMethod.ContainingType;

        return invocation.TargetMethod.Name switch
        {
            Delay => SymbolEqualityComparer.Default.Equals(owner, task),
            Sleep => SymbolEqualityComparer.Default.Equals(owner, thread),
            _ => false
        };
    }

    /// <summary>Whether this is a way out of the loop other than running out of iterations.</summary>
    private static bool StopsEarly(IOperation operation)
        => operation is ICatchClauseOperation or IBranchOperation { BranchKind: BranchKind.Break } or IReturnOperation;

    /// <summary>
    /// Everything the loop's own code does.
    /// </summary>
    /// <remarks>
    /// A <c>switch</c> is stepped over because the <c>break</c> that ends each of its
    /// sections is a branch like any other in the tree, and counting it would report every
    /// loop that pauses and contains a switch. So is a lambda or a local function: its
    /// <c>return</c> leaves the lambda, not the loop.
    /// </remarks>
    private static IEnumerable<IOperation> Inside(IOperation operation)
    {
        foreach (var child in operation.ChildOperations)
        {
            if (child is ISwitchOperation or IAnonymousFunctionOperation or ILocalFunctionOperation) continue;

            yield return child;

            foreach (var descendant in Inside(child)) yield return descendant;
        }
    }
}
