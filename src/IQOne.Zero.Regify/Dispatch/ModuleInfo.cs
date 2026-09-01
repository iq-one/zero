using IQOne.Zero.Regify.Internal;

namespace IQOne.Zero.Regify.Dispatch;

/// <summary>Compilation facts used to resolve module identity and dependencies.</summary>
internal sealed record ModuleInfo(
    string AssemblyName,
    EquatableArray<string> ReferencedAssemblies,
    EquatableArray<ModuleReference> ModuleTypes);

/// <summary>A module type discovered in a referenced assembly.</summary>
internal sealed record ModuleReference(
    string TypeName,
    EquatableArray<string> Interfaces);
