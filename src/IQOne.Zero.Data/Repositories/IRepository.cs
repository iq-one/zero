using System.Linq.Expressions;
using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.Data.Entities;

namespace IQOne.Zero.Data.Repositories;

/// <summary>Repositories are always request-scoped; the lifetime is carried by the interface.</summary>
public interface IRepository : IScoped;

public interface ICreateOnlyRepository<in T> : IRepository where T : IEntity
{
    Task CreateAsync(T entity, CancellationToken cancellationToken = default);
    Task CreateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
}

public interface IReadOnlyRepository<T> : IRepository where T : IEntity
{
    Task<T?> GetAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
    Task<List<T>> GetListAsync(CancellationToken cancellationToken = default);
    Task<List<T>> GetListAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default);
}

public interface IReadOnlyRepository<T, in TKey> : IReadOnlyRepository<T>
    where T : IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    Task<T?> GetAsync(TKey key, CancellationToken cancellationToken = default);
    Task<List<T>> GetListAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default);
}

public interface IQueryableRepository<T> : IReadOnlyRepository<T>, IQueryable<T> where T : IEntity;

public interface IUpdateOnlyRepository<in T> : IRepository where T : IEntity
{
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
}

public interface IDeleteOnlyRepository<in T> : IRepository where T : IEntity
{
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
    Task DeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
}

public interface IDeleteOnlyRepository<in T, in TKey> : IDeleteOnlyRepository<T>
    where T : IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    Task DeleteAsync(TKey key, CancellationToken cancellationToken = default);
    Task DeleteRangeAsync(IEnumerable<TKey> keys, CancellationToken cancellationToken = default);
}

public interface IRepository<T> :
    ICreateOnlyRepository<T>, IReadOnlyRepository<T>, IUpdateOnlyRepository<T>, IDeleteOnlyRepository<T>
    where T : IEntity;

public interface IRepository<T, in TKey> :
    IRepository<T>, IReadOnlyRepository<T, TKey>, IDeleteOnlyRepository<T, TKey>
    where T : IEntity<TKey>
    where TKey : IEquatable<TKey>;

/// <summary>Variants that persist changes as part of the call.</summary>
public interface IImmediateCreateOnlyRepository<in T> : ICreateOnlyRepository<T> where T : IEntity
{
    new Task CreateAsync(T entity, CancellationToken cancellationToken = default);
    new Task CreateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
}

public interface IImmediateUpdateOnlyRepository<in T> : IUpdateOnlyRepository<T> where T : IEntity
{
    new Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
    new Task UpdateRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
}

public interface IImmediateDeleteOnlyRepository<in T> : IDeleteOnlyRepository<T> where T : IEntity
{
    new Task DeleteAsync(T entity, CancellationToken cancellationToken = default);
    new Task DeleteRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);
}

public interface IImmediateRepository<in T> :
    IImmediateCreateOnlyRepository<T>, IImmediateUpdateOnlyRepository<T>, IImmediateDeleteOnlyRepository<T>
    where T : IEntity;

public interface ISupportSaveChanges
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
