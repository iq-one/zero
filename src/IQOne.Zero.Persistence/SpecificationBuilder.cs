using System.Linq.Expressions;

namespace IQOne.Zero.Persistence;

/// <summary>
/// Base for a specification: state the query in the constructor and give it a name.
/// </summary>
/// <remarks>
/// A named class rather than an inline chain, so the query can be found, reused and tested.
/// "Overdue invoices for a customer" is a thing the domain says; a <c>Where</c> buried in a
/// handler is not.
/// </remarks>
/// <typeparam name="T">The entity queried.</typeparam>
public abstract class Specification<T> : ISpecification<T>
{
    private readonly List<Expression<Func<T, object?>>> _includes = [];
    private readonly List<Ordering<T>> _orderings = [];
    private readonly HashSet<string> _ignoredFilters = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Expression<Func<T, bool>>? Criteria { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<Expression<Func<T, object?>>> Includes => _includes;

    /// <inheritdoc />
    public IReadOnlyList<Ordering<T>> Orderings => _orderings;

    /// <inheritdoc />
    public int? Skip { get; private set; }

    /// <inheritdoc />
    public int? Take { get; private set; }

    /// <inheritdoc />
    public bool AsNoTracking { get; private set; }

    /// <inheritdoc />
    public IReadOnlySet<string> IgnoredFilters => _ignoredFilters;

    /// <summary>
    /// Narrows the matches. Calling it again narrows further rather than replacing.
    /// </summary>
    /// <remarks>
    /// Combining rather than replacing lets a derived specification add to what its base
    /// already required, which is the whole reason for putting a query in a class.
    /// </remarks>
    /// <param name="criteria">What must also be true.</param>
    /// <returns>This specification, for chaining.</returns>
    protected Specification<T> Where(Expression<Func<T, bool>> criteria)
    {
        Criteria = Criteria is null ? criteria : Criteria.AndAlso(criteria);

        return this;
    }

    /// <summary>Loads related data with each match.</summary>
    /// <param name="include">What to bring with it.</param>
    /// <returns>This specification, for chaining.</returns>
    protected Specification<T> Include(Expression<Func<T, object?>> include)
    {
        _includes.Add(include);

        return this;
    }

    /// <summary>Orders the matches. Called again, it becomes a secondary ordering.</summary>
    /// <param name="keySelector">What to order by.</param>
    /// <returns>This specification, for chaining.</returns>
    protected Specification<T> OrderBy(Expression<Func<T, object?>> keySelector)
    {
        _orderings.Add(new Ordering<T>(keySelector, Descending: false));

        return this;
    }

    /// <summary>Orders the matches in reverse. Called again, it becomes a secondary ordering.</summary>
    /// <param name="keySelector">What to order by.</param>
    /// <returns>This specification, for chaining.</returns>
    protected Specification<T> OrderByDescending(Expression<Func<T, object?>> keySelector)
    {
        _orderings.Add(new Ordering<T>(keySelector, Descending: true));

        return this;
    }

    /// <summary>
    /// Takes one page of matches.
    /// </summary>
    /// <remarks>
    /// Paging an unordered query returns an arbitrary page, and the arbitrariness only shows
    /// up under load, so add an ordering as well.
    /// </remarks>
    /// <param name="skip">Matches to skip.</param>
    /// <param name="take">
    /// Matches to take, or <see langword="null"/> for all of them from <paramref name="skip"/>
    /// onwards. An offset with no limit is unusual but legitimate, and a required limit
    /// forced callers to invent one.
    /// </param>
    /// <returns>This specification, for chaining.</returns>
    protected Specification<T> Page(int skip, int? take)
    {
        Skip = skip;
        Take = take;

        return this;
    }

    /// <summary>Marks the matches as read-only, so nothing tracks them for changes.</summary>
    /// <returns>This specification, for chaining.</returns>
    protected Specification<T> ReadOnly()
    {
        AsNoTracking = true;

        return this;
    }

    /// <summary>
    /// Opts out of one named filter.
    /// </summary>
    /// <remarks>
    /// Say which one. A specification that ignores every filter at once will, sooner or
    /// later, read another tenant's rows.
    /// </remarks>
    /// <param name="key">The filter's name, as its convention declares it.</param>
    /// <returns>This specification, for chaining.</returns>
    protected Specification<T> IgnoreFilter(string key)
    {
        _ignoredFilters.Add(key);

        return this;
    }
}

/// <summary>Base for a specification that reshapes each match.</summary>
/// <typeparam name="T">The entity queried.</typeparam>
/// <typeparam name="TResult">The shape returned.</typeparam>
public abstract class Specification<T, TResult> : Specification<T>, ISpecification<T, TResult>
{
    /// <inheritdoc />
    public abstract Expression<Func<T, TResult>> Selector { get; }
}

/// <summary>Combines criteria without losing the ability to translate them to a query.</summary>
public static class SpecificationExpressions
{
    /// <summary>
    /// Joins two predicates with AND, rewriting the second to use the first's parameter.
    /// </summary>
    /// <remarks>
    /// <c>Expression.AndAlso</c> alone produces a tree with two different parameters, which
    /// every query provider rejects. Rebinding is what makes the combined tree translatable.
    /// </remarks>
    /// <typeparam name="T">The entity the predicates apply to.</typeparam>
    /// <param name="left">The first predicate.</param>
    /// <param name="right">The second predicate.</param>
    /// <returns>A predicate that requires both.</returns>
    public static Expression<Func<T, bool>> AndAlso<T>(
        this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rebound = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body)!;

        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, rebound), parameter);
    }

    /// <summary>Joins two predicates with OR, rewriting the second to use the first's parameter.</summary>
    /// <typeparam name="T">The entity the predicates apply to.</typeparam>
    /// <param name="left">The first predicate.</param>
    /// <param name="right">The second predicate.</param>
    /// <returns>A predicate that requires either.</returns>
    public static Expression<Func<T, bool>> OrElse<T>(
        this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var rebound = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body)!;

        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, rebound), parameter);
    }

    private sealed class ParameterRebinder(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node)
            => node == from ? to : base.VisitParameter(node);
    }
}
