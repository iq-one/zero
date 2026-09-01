using System.Linq.Expressions;

namespace IQOne.Zero.Persistence;

/// <summary>
/// A query written down as a value: what to match, what to bring with it, how to order it,
/// and how much of it to take.
/// </summary>
/// <remarks>
/// <para>
/// Written down rather than executed, so the same definition can be named, reused, unit
/// tested against a list in memory, and combined — none of which is possible when the query
/// only exists as a chain of calls inside one method.
/// </para>
/// <para>
/// It also keeps the provider out of the caller: a specification says <em>what</em>, and the
/// data layer decides how to run it.
/// </para>
/// </remarks>
/// <typeparam name="T">The entity queried.</typeparam>
public interface ISpecification<T>
{
    /// <summary>What must be true of an entity for it to match. Null matches everything.</summary>
    Expression<Func<T, bool>>? Criteria { get; }

    /// <summary>Related data to load with each match.</summary>
    IReadOnlyList<Expression<Func<T, object?>>> Includes { get; }

    /// <summary>How to order the matches. Applied in the order given.</summary>
    IReadOnlyList<Ordering<T>> Orderings { get; }

    /// <summary>Matches to skip. Null takes from the start.</summary>
    int? Skip { get; }

    /// <summary>How many matches to take. Null takes all of them.</summary>
    int? Take { get; }

    /// <summary>
    /// Whether the matches are read-only.
    /// </summary>
    /// <remarks>
    /// True for a query that only reads: change tracking costs memory and time for entities
    /// nobody is going to modify.
    /// </remarks>
    bool AsNoTracking { get; }

    /// <summary>
    /// Named filters this specification opts out of.
    /// </summary>
    /// <remarks>
    /// Named one at a time on purpose. Asking to see deleted rows must not also switch off
    /// tenant isolation, which is exactly what a single "ignore all filters" switch does.
    /// </remarks>
    IReadOnlySet<string> IgnoredFilters { get; }
}

/// <summary>One ordering step.</summary>
/// <typeparam name="T">The entity ordered.</typeparam>
/// <param name="KeySelector">What to order by.</param>
/// <param name="Descending">Whether to reverse it.</param>
public readonly record struct Ordering<T>(Expression<Func<T, object?>> KeySelector, bool Descending);

/// <summary>
/// A specification that also reshapes each match.
/// </summary>
/// <remarks>
/// Projecting in the query rather than after it is what keeps a list endpoint from loading
/// entire rows to return three fields of each.
/// </remarks>
/// <typeparam name="T">The entity queried.</typeparam>
/// <typeparam name="TResult">The shape returned.</typeparam>
public interface ISpecification<T, TResult> : ISpecification<T>
{
    /// <summary>How to reshape a match.</summary>
    Expression<Func<T, TResult>> Selector { get; }
}
