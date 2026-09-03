using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IQOne.Zero.Persistence.EntityFramework;

/// <summary>
/// The transaction boundary over a <see cref="DbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scoped alongside the context, so every repository in a request records into the same
/// change tracker and one save persists all of it.
/// </para>
/// <para>
/// Derivable, like <see cref="EfRepository{T}"/> and for the same reason: an application
/// with a context per module needs one boundary per context, and the open-generic
/// registration can only serve one. Name it the way the repositories are named:
/// <code>
/// public interface IOrderWork : IUnitOfWork;
///
/// public sealed class OrderWork(OrderContext context) : EfUnitOfWork(context), IOrderWork;
/// </code>
/// Note that <c>AddZeroTransactions</c> does not fit that shape — a pipeline behaviour is
/// open over every request, so it cannot pick the right context. Such an application opens
/// its boundary in the handler, against the module's own unit of work.
/// </para>
/// </remarks>
/// <param name="context">The context whose changes this boundary covers.</param>
public class EfUnitOfWork(DbContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public bool HasActiveTransaction => context.Database.CurrentTransaction is not null;

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // Joining, not nesting. A relational transaction has no second BEGIN to give, and a
        // handler calling another handler must not end up holding a scope it cannot commit
        // independently of the one that already exists.
        if (context.Database.CurrentTransaction is { } joined) return new EfTransaction(joined, isOwner: false);

        var owned = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        return new EfTransaction(owned, isOwner: true);
    }

    /// <summary>
    /// One scope over a database transaction — either the one that opened it, or one that
    /// joined it.
    /// </summary>
    /// <param name="transaction">The transaction underneath.</param>
    /// <param name="isOwner">Whether this scope opened it.</param>
    private sealed class EfTransaction(IDbContextTransaction transaction, bool isOwner) : ITransaction
    {
        private bool _completed;

        public bool IsOwner => isOwner;

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            _completed = true;

            // A joined scope records that it finished and stops there. Committing here would
            // commit the outer scope's unfinished work along with its own.
            return isOwner ? transaction.CommitAsync(cancellationToken) : Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            // A joined scope leaves the transaction alone: the owner is still using it, and
            // whatever made this scope skip CompleteAsync is on its way out to the owner,
            // which will skip its own and roll back.
            if (!isOwner) return;

            // No token. Dispose is the path an exception takes, and cancelling the rollback
            // would leave the transaction open with nobody left to close it.
            if (!_completed) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);

            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }
}
