using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace IQOne.Zero.Observability.Analyzers;

/// <summary>
/// Reports observability a handler took on itself when the pipeline already had it.
/// </summary>
/// <remarks>
/// <para>
/// The scope is narrow on purpose. A handler may log: an invoice closed, a payment matched, a
/// batch skipped are all things only the handler knows, and a rule that reported them would be
/// wrong often enough to be turned off — taking the two rules that are never wrong with it.
/// </para>
/// <para>
/// What is reported is the pair of mistakes that produce no symptom. A private
/// <c>ActivitySource</c> or <c>Meter</c> records into a name nobody subscribed to, so the data
/// is dropped and the graph stays empty. A request written into a log line is collected
/// faithfully, which is the problem: whatever the caller sent is now in a log that travels
/// further than the database it came from.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HandlerTelemetryAnalyzer : DiagnosticAnalyzer
{
    private const string HandlerMarker = "IQOne.Zero.Messaging.IRequestHandler";
    private const string RequestInterface = "IQOne.Zero.Messaging.IRequest`1";
    private const string ActivitySource = "System.Diagnostics.ActivitySource";
    private const string Meter = "System.Diagnostics.Metrics.Meter";
    private const string Logger = "Microsoft.Extensions.Logging.ILogger";
    private const string LoggerExtensions = "Microsoft.Extensions.Logging.LoggerExtensions";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Diagnostics.OwnTelemetrySource, Diagnostics.RequestInLog];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var handler = start.Compilation.GetTypeByMetadataName(HandlerMarker);

            // Nothing to do in a compilation that has no handlers in it.
            if (handler is null) return;

            var sources = new[]
                {
                    start.Compilation.GetTypeByMetadataName(ActivitySource),
                    start.Compilation.GetTypeByMetadataName(Meter)
                }
                .Where(t => t is not null)
                .ToImmutableArray();

            if (sources.Length > 0)
                start.RegisterOperationAction(
                    c => OwnTelemetrySource(c, handler, sources!), OperationKind.ObjectCreation);

            var request = start.Compilation.GetTypeByMetadataName(RequestInterface);
            var logger = start.Compilation.GetTypeByMetadataName(Logger);
            var extensions = start.Compilation.GetTypeByMetadataName(LoggerExtensions);

            if (request is not null && logger is not null)
                start.RegisterOperationAction(
                    c => RequestInLog(c, handler, request, logger, extensions), OperationKind.Invocation);
        });
    }

    /// <summary>Reports an <c>ActivitySource</c> or <c>Meter</c> constructed inside a handler.</summary>
    /// <remarks>
    /// Wherever in the handler it is written — a static field, a constructor, the method body —
    /// because the mistake is owning the source at all, not where it was assigned.
    /// </remarks>
    private static void OwnTelemetrySource(
        OperationAnalysisContext context, INamedTypeSymbol handler, ImmutableArray<INamedTypeSymbol> sources)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var created = creation.Type;

        if (created is null || !sources.Any(s => SymbolEqualityComparer.Default.Equals(created, s))) return;

        var owner = Owner(context.ContainingSymbol);

        if (owner is null || !Implements(owner, handler)) return;

        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.OwnTelemetrySource, creation.Syntax.GetLocation(), owner.Name, created.Name));
    }

    /// <summary>Reports a request handed whole to a logging call inside a handler.</summary>
    /// <remarks>
    /// The test is the argument's type, not its name: <c>request.Reference</c> is a string the
    /// author chose to write down, while <c>request</c> is everything the caller sent, including
    /// the fields nobody thought about when the log line was added.
    /// </remarks>
    private static void RequestInLog(
        OperationAnalysisContext context,
        INamedTypeSymbol handler,
        INamedTypeSymbol request,
        INamedTypeSymbol logger,
        INamedTypeSymbol? extensions)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!IsLoggingCall(invocation.TargetMethod, logger, extensions)) return;

        var owner = Owner(context.ContainingSymbol);

        if (owner is null || !Implements(owner, handler)) return;

        foreach (var argument in invocation.Arguments)
            foreach (var value in Values(argument.Value))
            {
                if (!IsRequest(value.Type, request)) continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.RequestInLog, value.Syntax.GetLocation(), owner.Name));
            }
    }

    /// <summary>Whether the call goes to <c>ILogger</c>, directly or through its extensions.</summary>
    /// <remarks>
    /// Both routes matter: <c>LogInformation</c> is a static extension, while <c>Log</c> and
    /// <c>BeginScope</c> are declared on the interface, and a scope leaks a request just as
    /// thoroughly as a message does.
    /// </remarks>
    private static bool IsLoggingCall(IMethodSymbol method, INamedTypeSymbol logger, INamedTypeSymbol? extensions)
        => SymbolEqualityComparer.Default.Equals(method.ContainingType, logger)
           || (extensions is not null && SymbolEqualityComparer.Default.Equals(method.ContainingType, extensions));

    /// <summary>The values one argument actually carries.</summary>
    /// <remarks>
    /// A logging call's message arguments arrive as one <c>params object?[]</c>, so the array
    /// has to be opened to see what is in it, and each element unwrapped from the conversion
    /// that boxed it.
    /// </remarks>
    private static IEnumerable<IOperation> Values(IOperation argument)
    {
        var value = Unwrap(argument);

        if (value is not IArrayCreationOperation { Initializer: { } initializer })
        {
            yield return value;
            yield break;
        }

        foreach (var element in initializer.ElementValues) yield return Unwrap(element);
    }

    private static IOperation Unwrap(IOperation operation)
        => operation is IConversionOperation conversion ? Unwrap(conversion.Operand) : operation;

    private static bool IsRequest(ITypeSymbol? type, INamedTypeSymbol request)
        => type is INamedTypeSymbol named
           && named.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, request));

    /// <summary>The type the operation was written in, whether that is a member or the type itself.</summary>
    /// <remarks>
    /// A field initializer's containing symbol is the field, a method body's is the method, and
    /// a primary constructor's is the type. All three reach the handler the same way.
    /// </remarks>
    private static INamedTypeSymbol? Owner(ISymbol symbol)
        => symbol as INamedTypeSymbol ?? symbol.ContainingType;

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol @interface)
        => type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, @interface));
}
