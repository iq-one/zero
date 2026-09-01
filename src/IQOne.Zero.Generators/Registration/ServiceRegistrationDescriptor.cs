using IQOne.Zero.Generators.Internal;

namespace IQOne.Zero.Generators.Registration;

/// <summary>One attribute applied to a candidate, flattened to strings so the value stays equatable.</summary>
internal sealed record AttributeUsage(
    string TypeName,
    EquatableArray<string> Arguments);

/// <summary>
/// One interface a candidate implements, with its type arguments kept.
/// </summary>
/// <remarks>
/// The open definition alone is not enough for messaging: dispatching needs to know that a
/// handler implements <c>IRequestHandler&lt;CreateInvoice, int&gt;</c>, not merely that it
/// implements <c>IRequestHandler&lt;,&gt;</c>.
/// </remarks>
internal sealed record InterfaceUsage(
    string OpenGenericName,
    EquatableArray<string> TypeArguments);

/// <summary>Raw registration facts; lifetime interfaces are matched during emission.</summary>
internal sealed record ServiceCandidate(
    string ImplementationTypeName,
    bool IsConcrete,
    EquatableArray<string> AllInterfaces,
    EquatableArray<string> DirectInterfaces,
    string TypeName,
    EquatableArray<AttributeUsage> Attributes,
    EquatableArray<string> ConstructorDependencies,
    EquatableArray<InterfaceUsage> ClosedInterfaces,
    LocationInfo? Location);

/// <summary>A request and the handler that serves it, ready for emission.</summary>
internal sealed record RequestDescriptor(
    string RequestTypeName,
    string ResponseTypeName,
    string HandlerTypeName,
    LocationInfo? Location);

/// <summary>An HTTP endpoint and the request behind it, ready for emission.</summary>
internal sealed record EndpointDescriptor(
    string Method,
    string Pattern,
    string Name,
    string? Tag,
    string? Policy,
    bool AllowAnonymous,
    string RequestTypeName,
    string ResponseTypeName,
    LocationInfo? Location);

internal sealed record ServiceRegistrationDescriptor(
    string ImplementationTypeName,
    EquatableArray<string> ServiceTypeNames,
    string Lifetime,
    string? Key,
    bool RegisterSelf,
    EquatableArray<string> ConstructorDependencies,
    LocationInfo? Location);
