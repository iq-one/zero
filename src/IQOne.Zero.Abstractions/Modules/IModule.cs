namespace IQOne.Zero.Modules;

/// <summary>
/// A deployable unit of application functionality.
/// </summary>
/// <remarks>
/// Execution order is derived from <see cref="Dependencies"/> by topological sort; modules
/// never declare a numeric order. Numeric ordering has to be renumbered whenever a module is
/// inserted, and it silently encodes constraints nobody can verify.
/// </remarks>
public interface IModule
{
    /// <summary>Stable identifier used in ordering diagnostics and cycle reports.</summary>
    string Name { get; }

    /// <summary>Modules that must be configured before this one.</summary>
    IReadOnlyList<Type> Dependencies => [];
}

/// <summary>
/// Declares an ordering constraint that the assembly reference graph does not express.
/// </summary>
/// <remarks>
/// Dependencies are normally derived from project references, so they cannot drift from
/// what the code actually uses. Apply this only for an ordering requirement that exists
/// without a reference — for example a module that must seed data another module reads.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnAttribute(Type moduleType) : Attribute
{
    /// <summary>The module that must be configured first.</summary>
    public Type ModuleType { get; } = moduleType;
}

/// <summary>
/// Thrown when module dependencies form a cycle. <see cref="Cycle"/> names the participants.
/// </summary>
public sealed class ModuleDependencyCycleException(IReadOnlyList<string> cycle)
    : InvalidOperationException($"Module dependencies form a cycle: {string.Join(" -> ", cycle)}")
{
    /// <summary>The modules taking part in the cycle, in traversal order.</summary>
    public IReadOnlyList<string> Cycle { get; } = cycle;
}
