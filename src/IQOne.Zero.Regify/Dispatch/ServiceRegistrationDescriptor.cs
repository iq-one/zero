using IQOne.Zero.Regify.Internal;

namespace IQOne.Zero.Regify.Dispatch;

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
