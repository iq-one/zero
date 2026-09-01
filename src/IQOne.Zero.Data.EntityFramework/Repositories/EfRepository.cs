using System.Linq.Expressions;
using IQOne.Zero.Data.Entities;
using IQOne.Zero.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Data.EntityFramework.Repositories;

/// <summary>
/// Entity Framework repository base. <see cref="Query"/> is untracked; use
/// <see cref="Tracked"/> on write paths.
/// </summary>
public abstract class EfRepository<TEntity>(DbContext context) : IRepository<TEntity>
    where TEntity : class, IEntity
{
    protected DbContext Context { get; } = context;

    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    protected IQueryable<TEntity> Query => Set.AsNoTracking();

    protected IQueryable<TEntity> Tracked => Set;

    public virtual Task<TEntity?> GetAsync(
        Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => Query.FirstOrDefaultAsync(predicate, cancellationToken);

    public virtual Task<List<TEntity>> GetListAsync(CancellationToken cancellationToken = default)
        => Query.ToListAsync(cancellationToken);

    public virtual Task<List<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
        => Query.Where(predicate).ToListAsync(cancellationToken);

    public virtual async Task CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
        => await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);

    public virtual async Task CreateRangeAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
        => await Set.AddRangeAsync(entities, cancellationToken).ConfigureAwait(false);

    public virtual Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Set.Update(entity);
        return Task.CompletedTask;
    }

    public virtual Task UpdateRangeAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Set.UpdateRange(entities);
        return Task.CompletedTask;
    }

    /// <summary>Marks the entity removed; the audit interceptor converts this to a soft delete.</summary>
    public virtual Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Set.Remove(entity);
        return Task.CompletedTask;
    }

    public virtual Task DeleteRangeAsync(
        IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Set.RemoveRange(entities);
        return Task.CompletedTask;
    }
}
