using IQOne.Zero.Regify.Internal;
using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Regify.Dispatch;

/// <summary>
/// Values carried through the incremental pipeline. Holds no <c>ISymbol</c>, since that
/// would break caching.
/// </summary>
internal sealed record HandlerCandidate(
    string HandlerTypeName,
    bool IsConcrete,
    EquatableArray<AttributeUsage> Attributes,
    EquatableArray<InterfaceUsage> Interfaces,
    EquatableArray<string> RequestBaseChain,
    LocationInfo? Location);

internal sealed record AttributeUsage(
    string TypeName,
    EquatableArray<string> Arguments);

internal sealed record InterfaceUsage(
    string OpenGenericName,
    EquatableArray<string> TypeArguments);

/// <summary>A resolved route ready for emission.</summary>
internal sealed record HandlerDescriptor(
    string Module,
    string Service,
    string Method,
    string HandlerTypeName,
    string RequestTypeName,
    string ResponseTypeName,
    LocationInfo? Location);

internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> Arguments)
{
    public Diagnostic ToDiagnostic()
        => Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.ToArray());
}
