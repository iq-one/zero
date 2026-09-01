using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Data.Query;

/// Sorguyu MATERYALIZE eden tek nokta.
///
/// Handler'lar ToListAsync gibi saglayiciya ozgu uzanti metodlarini dogrudan
/// cagirmaz; boylece EF'ten baska bir saglayiciya gecmek handler'lara dokunmaz.
/// Bu, IQueryable'i olduran bir soyutlama degil — LINQ kompozisyonu aynen kaliyor,
/// yalnizca "calistir" adimi enjekte ediliyor.
public interface IQueryExecutor : IScoped
{
    Task<List<T>> ToListAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);

    Task<bool> AnyAsync<T>(IQueryable<T> query, CancellationToken cancellationToken);
}
