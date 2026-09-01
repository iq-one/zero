using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework;

/// <summary>
/// Reads and writes aggregates through a <see cref="DbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as an open generic, so an application gets a repository for every aggregate
/// without listing any of them. Derive from it only when a specific aggregate needs a
/// method a specification cannot express.
/// </para>
/// <para>
/// Nothing here saves. Writing records the intent on the context; the unit of work decides
/// when it reaches the database.
/// </para>
/// </remarks>
/// <typeparam name="T">The aggregate stored.</typeparam>
public class EfRepository<T> : IRepository<T>
    where T : class, IAggregateRoot
{
    /// <summary>Creates a repository over a context.</summary>
    /// <param name="context">The context the aggregates live in.</param>
    /// <param name="evaluator">Turns a specification into a query.</param>
    public EfRepository(DbContext context, ISpecificationEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(evaluator);

        Context = context;
        Evaluator = evaluator;
    }

    /// <summary>The context the aggregates live in.</summary>
    protected DbContext Context { get; }

    /// <summary>Turns a specification into a query.</summary>
    protected ISpecificationEvaluator Evaluator { get; }

    /// <summary>The aggregate's set, before any specification is applied.</summary>
    protected DbSet<T> Set => Context.Set<T>();

    /// <inheritdoc />
    public Task<T?> FindAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
        => Query(specification).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<TResult?> FindAsync<TResult>(
        ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
        => Query(specification).FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T> specification, CancellationToken cancellationToken = default)
        => await Query(specification).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        ISpecification<T, TResult> specification, CancellationToken cancellationToken = default)
        => await Query(specification).ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        // Counting the page the specification asks for would report the page size back to a
        // caller that already knows it. The total is the only answer worth a round trip.
        return Evaluator.EvaluateForCount(Set.AsQueryable(), specification).CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unlike <see cref="CountAsync"/> this honours paging, because "is there anything on
    /// page three" is a question a caller can reasonably mean. A specification with no
    /// paging — which is nearly all of them — behaves identically either way.
    /// </remarks>
    public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default)
        => Query(specification).AnyAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        => Set.AddRangeAsync(entities, cancellationToken);

    /// <inheritdoc />
    public void Update(T entity) => Set.Update(entity);

    /// <inheritdoc />
    public void Remove(T entity) => Set.Remove(entity);

    /// <inheritdoc />
    public void RemoveRange(IEnumerable<T> entities) => Set.RemoveRange(entities);

    /// <summary>Builds the query a specification describes.</summary>
    /// <param name="specification">What to look for.</param>
    /// <returns>The query, still unexecuted.</returns>
    protected IQueryable<T> Query(ISpecification<T> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return Evaluator.Evaluate(Set.AsQueryable(), specification);
    }

    /// <summary>Builds the reshaping query a specification describes.</summary>
    /// <typeparam name="TResult">The shape returned.</typeparam>
    /// <param name="specification">What to look for, and how to reshape it.</param>
    /// <returns>The query, still unexecuted.</returns>
    protected IQueryable<TResult> Query<TResult>(ISpecification<T, TResult> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return Evaluator.Evaluate(Set.AsQueryable(), specification);
    }
}

/// <summary>Reads and writes aggregates, and looks them up by key.</summary>
/// <typeparam name="T">The aggregate stored.</typeparam>
/// <typeparam name="TKey">The key's type.</typeparam>
/// <param name="context">The context the aggregates live in.</param>
/// <param name="evaluator">Turns a specification into a query.</param>
public class EfRepository<T, TKey>(DbContext context, ISpecificationEvaluator evaluator)
    : EfRepository<T>(context, evaluator), IRepository<T, TKey>
    where T : class, IAggregateRoot, IEntity<TKey>
{
    /// <inheritdoc />
    /// <remarks>
    /// Goes through the change tracker first, so asking twice in one unit of work costs one
    /// round trip. A key lookup that does reach the database still has every filter applied.
    /// </remarks>
    public async Task<T?> GetAsync(TKey key, CancellationToken cancellationToken = default)
        => await Set.FindAsync([key], cancellationToken).ConfigureAwait(false);
}
