using System.Linq.Expressions;

namespace IQOne.Zero.Data.Conventions;

/// <summary>
/// Contributes a named query filter to every entity it applies to.
/// </summary>
/// <remarks>
/// Filters are registered individually so that a caller can disable one without the
/// others. The platform supplies tenant scoping; applications add their own conventions
/// for concerns such as soft deletion.
/// </remarks>
public interface IEntityFilterConvention
{
    /// <summary>Name used to disable this filter for a single query.</summary>
    string Key { get; }

    /// <summary>
    /// Returns the predicate for <paramref name="entityType"/>, or null when the
    /// convention does not apply to it.
    /// </summary>
    /// <param name="owner">
    /// The context instance. Values read through it are parameterized by the provider
    /// rather than baked into the cached query plan.
    /// </param>
    LambdaExpression? Build(Type entityType, object owner);
}
