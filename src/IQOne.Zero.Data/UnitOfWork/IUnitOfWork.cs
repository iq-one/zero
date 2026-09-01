namespace IQOne.Zero.Data.UnitOfWork;

/// Transaction disiplini: OKUMA yolunda transaction ACILMAZ.
/// COMED bugun her istegi transaction'a sariyor ve bu, ayni veritabanina yazan
/// diger iki uygulamaya karsi gereksiz kilit cekismesi uretiyor.
public interface IUnitOfWork : IAsyncDisposable
{
    bool HasActiveTransaction { get; }

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
