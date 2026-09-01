using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Persistence;

/// <summary>
/// The boundary a set of changes succeeds or fails within.
/// </summary>
/// <remarks>
/// <para>
/// A transaction is opened deliberately, never around every request. Wrapping reads costs
/// nothing visible on a database one application owns, and produces lock contention against
/// every other writer on a database that is shared — which is where the cost is hardest to
/// attribute to whoever caused it.
/// </para>
/// <para>
/// In practice a handler rarely touches this: <c>TransactionBehavior</c> opens and commits
/// around commands. Reach for it directly only when the boundary is not the request.
/// </para>
/// </remarks>
public interface IUnitOfWork : IScoped
{
    /// <summary>Whether a transaction is currently open.</summary>
    bool HasActiveTransaction { get; }

    /// <summary>Persists everything recorded so far.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of rows affected.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a transaction, or joins the one already open.
    /// </summary>
    /// <remarks>
    /// Joining rather than nesting: a handler that calls another handler should not find
    /// itself in a second transaction it did not ask for and cannot commit independently.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A scope that commits when completed and rolls back otherwise.</returns>
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// An open transaction.
/// </summary>
/// <remarks>
/// Disposing without completing rolls back. That way an exception on any path undoes the
/// work, without every path having to remember to say so.
/// </remarks>
public interface ITransaction : IAsyncDisposable
{
    /// <summary>Whether this scope owns the transaction, rather than having joined one.</summary>
    bool IsOwner { get; }

    /// <summary>Commits the work. Does nothing when this scope joined an outer transaction.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
