using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework;

/// <summary>
/// Turns a specification into a query the database can run.
/// </summary>
/// <remarks>
/// Separate from the repository so that the translation can be tested on its own, and so
/// that an application with an unusual requirement can replace it without also replacing
/// every repository method.
/// </remarks>
public interface ISpecificationEvaluator
{
    /// <summary>Applies everything the specification asks for.</summary>
    /// <typeparam name="T">The entity queried.</typeparam>
    /// <param name="source">The query to build on, usually a <see cref="DbSet{TEntity}"/>.</param>
    /// <param name="specification">What to look for.</param>
    /// <returns>The query, still unexecuted.</returns>
    IQueryable<T> Evaluate<T>(IQueryable<T> source, ISpecification<T> specification)
        where T : class;

    /// <summary>
    /// Applies everything the specification asks for and reshapes each match.
    /// </summary>
    /// <remarks>
    /// The projection is part of the query, so the database returns the columns the shape
    /// needs and nothing else.
    /// </remarks>
    /// <typeparam name="T">The entity queried.</typeparam>
    /// <typeparam name="TResult">The shape returned.</typeparam>
    /// <param name="source">The query to build on, usually a <see cref="DbSet{TEntity}"/>.</param>
    /// <param name="specification">What to look for, and how to reshape it.</param>
    /// <returns>The query, still unexecuted.</returns>
    IQueryable<TResult> Evaluate<T, TResult>(IQueryable<T> source, ISpecification<T, TResult> specification)
        where T : class;

    /// <summary>
    /// Applies only what changes how many rows match.
    /// </summary>
    /// <remarks>
    /// Paging is left off — a count exists to report the total, so counting one page would
    /// answer a question nobody asked. Ordering and includes are left off too: neither
    /// changes the answer, and both cost the database work.
    /// </remarks>
    /// <typeparam name="T">The entity queried.</typeparam>
    /// <param name="source">The query to build on, usually a <see cref="DbSet{TEntity}"/>.</param>
    /// <param name="specification">What to look for.</param>
    /// <returns>The query, still unexecuted.</returns>
    IQueryable<T> EvaluateForCount<T>(IQueryable<T> source, ISpecification<T> specification)
        where T : class;
}

/// <summary>
/// The evaluator Zero registers: specification in, <see cref="IQueryable{T}"/> out.
/// </summary>
/// <remarks>
/// Nothing here executes a query. Every method returns a query the caller runs, which is
/// what lets a repository decide between first, list and count without three evaluators.
/// </remarks>
public sealed class SpecificationEvaluator : ISpecificationEvaluator
{
    private static readonly MethodInfo IncludeMethod = typeof(EntityFrameworkQueryableExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method =>
            method.Name == nameof(EntityFrameworkQueryableExtensions.Include)
            && method.GetGenericArguments().Length == 2);

    private static readonly Dictionary<string, MethodInfo> OrderMethods = typeof(Queryable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(method =>
            method.Name is nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending)
                or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending)
            && method.GetParameters().Length == 2)
        .ToDictionary(method => method.Name, StringComparer.Ordinal);

    /// <summary>The shared instance. The evaluator holds no state, so one is enough.</summary>
    public static ISpecificationEvaluator Default { get; } = new SpecificationEvaluator();

    /// <inheritdoc />
    public IQueryable<T> Evaluate<T>(IQueryable<T> source, ISpecification<T> specification)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(specification);

        var query = Narrow(source, specification);

        query = ApplyIncludes(query, specification);
        query = ApplyOrdering(query, specification);

        if (specification.AsNoTracking) query = query.AsNoTracking();

        return ApplyPaging(query, specification.Skip, specification.Take);
    }

    /// <inheritdoc />
    public IQueryable<TResult> Evaluate<T, TResult>(
        IQueryable<T> source, ISpecification<T, TResult> specification)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(specification);

        var query = Narrow(source, specification);

        // Includes are deliberately skipped. A projection already states every column the
        // caller wants; loading a navigation as well would be work whose result is discarded
        // on the way out of the query.
        query = ApplyOrdering(query, specification);

        if (specification.AsNoTracking) query = query.AsNoTracking();

        // Projected before paging, so the database pages over the narrow shape rather than
        // wrapping whole rows in a subquery and throwing the columns away afterwards.
        return ApplyPaging(query.Select(specification.Selector), specification.Skip, specification.Take);
    }

    /// <inheritdoc />
    public IQueryable<T> EvaluateForCount<T>(IQueryable<T> source, ISpecification<T> specification)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(specification);

        return Narrow(source, specification);
    }

    private static IQueryable<T> Narrow<T>(IQueryable<T> source, ISpecification<T> specification)
        where T : class
    {
        var query = source;

        // One name at a time, never a blanket switch: a query asking to see deleted rows
        // keeps tenant isolation, because it never said anything about tenants.
        if (specification.IgnoredFilters.Count > 0)
            query = query.IgnoreQueryFilters(specification.IgnoredFilters);

        return specification.Criteria is { } criteria ? query.Where(criteria) : query;
    }

    private static IQueryable<T> ApplyIncludes<T>(IQueryable<T> query, ISpecification<T> specification)
        where T : class
    {
        foreach (var include in specification.Includes)
        {
            var selector = Unbox(include);

            query = query.Provider.CreateQuery<T>(
                Expression.Call(
                    IncludeMethod.MakeGenericMethod(typeof(T), selector.ReturnType),
                    query.Expression,
                    Expression.Quote(selector)));
        }

        return query;
    }

    private static IQueryable<T> ApplyOrdering<T>(IQueryable<T> query, ISpecification<T> specification)
    {
        var first = true;

        foreach (var ordering in specification.Orderings)
        {
            var selector = Unbox(ordering.KeySelector);

            var name = (first, ordering.Descending) switch
            {
                (true, false) => nameof(Queryable.OrderBy),
                (true, true) => nameof(Queryable.OrderByDescending),
                (false, false) => nameof(Queryable.ThenBy),
                (false, true) => nameof(Queryable.ThenByDescending)
            };

            query = query.Provider.CreateQuery<T>(
                Expression.Call(
                    OrderMethods[name].MakeGenericMethod(typeof(T), selector.ReturnType),
                    query.Expression,
                    Expression.Quote(selector)));

            first = false;
        }

        return query;
    }

    private static IQueryable<TElement> ApplyPaging<TElement>(IQueryable<TElement> query, int? skip, int? take)
    {
        if (skip is { } toSkip) query = query.Skip(toSkip);
        if (take is { } toTake) query = query.Take(toTake);

        return query;
    }

    /// <summary>
    /// Recovers the property's own type from a selector declared as returning
    /// <see cref="object"/>.
    /// </summary>
    /// <remarks>
    /// A specification writes <c>i => i.Due</c> against <c>Expression&lt;Func&lt;T, object?&gt;&gt;</c>,
    /// so the compiler boxes the value into a <c>Convert</c> node. Handing that to the
    /// provider would order by a boxed object — a comparison the database cannot make and
    /// the in-memory fallback makes wrongly. Unwrapping restores the call the consumer would
    /// have written by hand.
    /// </remarks>
    private static LambdaExpression Unbox<T>(Expression<Func<T, object?>> selector)
    {
        var body = selector.Body is UnaryExpression
        {
            NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked
        } conversion && conversion.Type == typeof(object)
            ? conversion.Operand
            : selector.Body;

        return Expression.Lambda(body, selector.Parameters);
    }
}
