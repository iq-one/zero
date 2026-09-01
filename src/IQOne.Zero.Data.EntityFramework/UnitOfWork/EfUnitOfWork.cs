using System.Data;
using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.Data.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IQOne.Zero.Data.EntityFramework.UnitOfWork;

/// <summary>
/// Entity Framework unit of work. Transactions are opened explicitly, kept short,
/// and never opened on read paths.
/// </summary>
public sealed class EfUnitOfWork(DbContext context) : IUnitOfWork, IScoped
{
    private IDbContextTransaction? _transaction;

    public bool HasActiveTransaction => _transaction is not null;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Bu kapsamda zaten acik bir transaction var.");

        _transaction = await context.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (_transaction is null) return;

        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await DisposeTransactionAsync().ConfigureAwait(false);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null) return;

        await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        await DisposeTransactionAsync().ConfigureAwait(false);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => context.SaveChangesAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        // An uncommitted transaction must roll back rather than close silently.
        if (_transaction is not null)
            await _transaction.RollbackAsync().ConfigureAwait(false);

        await DisposeTransactionAsync().ConfigureAwait(false);
    }

    private async ValueTask DisposeTransactionAsync()
    {
        if (_transaction is null) return;

        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }
}
