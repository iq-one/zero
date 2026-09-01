using IQOne.Zero.Data.Query;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Data.EntityFramework.Query;

/// <summary>Entity Framework implementation of <see cref="IQueryExecutor"/>.</summary>
public sealed class EfQueryExecutor : IQueryExecutor
{
    public Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        => query.ToListAsync(cancellationToken);

    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        => query.FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        => query.CountAsync(cancellationToken);

    public Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        => query.AnyAsync(cancellationToken);
}
