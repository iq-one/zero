using IQOne.Zero.Generators.Internal;

namespace IQOne.Zero.Generators.Registration;

/// <summary>One attribute applied to a candidate, flattened to strings so the value stays equatable.</summary>
internal sealed record AttributeUsage(
    string TypeName,
    EquatableArray<string> Arguments);

/// <summary>Raw registration facts; lifetime interfaces are matched during emission.</summary>
internal sealed record ServiceCandidate(
    string ImplementationTypeName,
    bool IsConcrete,
    EquatableArray<string> AllInterfaces,
    EquatableArray<string> DirectInterfaces,
    string TypeName,
    EquatableArray<AttributeUsage> Attributes,
    EquatableArray<string> ConstructorDependencies,
    LocationInfo? Location);

internal sealed record ServiceRegistrationDescriptor(
    string ImplementationTypeName,
    EquatableArray<string> ServiceTypeNames,
    string Lifetime,
    string? Key,
    bool RegisterSelf,
    EquatableArray<string> ConstructorDependencies,
    LocationInfo? Location);
