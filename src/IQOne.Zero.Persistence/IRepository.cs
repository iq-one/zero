using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Persistence;

/// <summary>
/// Reads aggregates. Cannot write, and the compiler enforces that.
/// </summary>
/// <remarks>
/// Reading and writing are separate interfaces so that a constructor says which one a type
/// does. A single all-verb repository makes every dependency look like a write dependency,
/// and a reviewer then has to read the body to find out.
/// </remarks>
/// <typeparam name="T">The aggregate read.</typeparam>
public interface IReadRepository<T> : IScoped
    where T : class, IAggregateRoot
{
    /// <summary>The first match, or null.</summary>
    /// <param name="specification">What to look for.</param>
    /// <param name="cancellationToken">Cancels the round trip.</param>
    /// <returns>The match, or <see langword="null"/>.</returns>
    Task<T?> FindAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    /// <summary>The first match, reshaped, or the shape's default.</summary>
    /// <typeparam name="TResult">The shape returned.</typeparam>
    /// <param name="specification">What to look for, and how to reshape it.</param>
    /// <param name="cancellationToken">Cancels the round trip.</param>
    /// <returns>The reshaped match, or <see langword="default"/>.</returns>
    Task<TResult?> FindAsync<TResult>(
        ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);

    /// <summary>Every match.</summary>
    /// <param name="specification">What to look for.</param>
    /// <param name="cancellationToken">Cancels the round trip.</param>
    /// <returns>The matches.</returns>
    Task<IReadOnlyList<T>> ListAsync(
        ISpecification<T> specification, CancellationToken cancellationToken = default);

    /// <summary>Every match, reshaped.</summary>
    /// <typeparam name="TResult">The shape returned.</typeparam>
    /// <param name="specification">What to look for, and how to reshape it.</param>
    /// <param name="cancellationToken">Cancels the round trip.</param>
    /// <returns>The reshaped matches.</returns>
    Task<IReadOnlyList<TResult>> ListAsync<TResult>(
        ISpecification<T, TResult> specification, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many match, ignoring any paging the specification asks for.
    /// </summary>
    /// <remarks>
    /// Paging is ignored because the only reason to count a paged query is to report the
    /// total, and counting one page would answer a question nobody asked.
    /// </remarks>
    /// <param name="specification">What to look for.</param>
    /// <param name="cancellationToken">Cancels the round trip.</param>
    /// <returns>The number of matches.</returns>
    Task<int> CountAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);

    /// <summary>Whether anything matches.</summary>
    /// <param name="specification">What to look for.</param>
    /// <param name="cancellationToken">Cancels the round trip.</param>
    /// <returns><see langword="true"/> when at least one entity matches.</returns>
    Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken cancellationToken = default);
}

/// <summary>Reads aggregates by key as well as by specification.</summary>
/// <typeparam name="T">The aggregate read.</typeparam>
/// <typeparam name="TKey">The key's type.</typeparam>
public interface IReadRepository<T, in TKey> : IReadRepository<T>
    where T : class, IAggregateRoot, IEntity<TKey>
{
    /// <summary>The aggregate with this key, or null.</summary>
    /// <param name="key">The identity to look for.</param>
    /// <param name="cancellationToken">Cancels the round trip.</param>
    /// <returns>The aggregate, or <see langword="null"/>.</returns>
    Task<T?> GetAsync(TKey key, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads and writes aggregates.
/// </summary>
/// <remarks>
/// Writing here only records the intent. Nothing reaches the database until the unit of work
/// saves, which is what lets one handler change several aggregates and have them succeed or
/// fail together.
/// </remarks>
/// <typeparam name="T">The aggregate stored.</typeparam>
public interface IRepository<T> : IReadRepository<T>
    where T : class, IAggregateRoot
{
    /// <summary>Records that the aggregate is new.</summary>
    /// <param name="entity">The aggregate to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Records that several aggregates are new.</summary>
    /// <param name="entities">The aggregates to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the aggregate has changed.
    /// </summary>
    /// <remarks>
    /// Unnecessary for an aggregate the repository loaded and is tracking; call it for one
    /// that arrived from somewhere else.
    /// </remarks>
    /// <param name="entity">The aggregate that changed.</param>
    void Update(T entity);

    /// <summary>Records that the aggregate should be removed.</summary>
    /// <param name="entity">The aggregate to remove.</param>
    void Remove(T entity);

    /// <summary>Records that several aggregates should be removed.</summary>
    /// <param name="entities">The aggregates to remove.</param>
    void RemoveRange(IEnumerable<T> entities);
}

/// <summary>Reads and writes aggregates, and reads them by key.</summary>
/// <typeparam name="T">The aggregate stored.</typeparam>
/// <typeparam name="TKey">The key's type.</typeparam>
public interface IRepository<T, in TKey> : IRepository<T>, IReadRepository<T, TKey>
    where T : class, IAggregateRoot, IEntity<TKey>;
