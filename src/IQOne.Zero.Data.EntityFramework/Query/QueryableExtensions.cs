using System.Linq.Expressions;
using IQOne.Zero.Messaging;

namespace IQOne.Zero.Data.EntityFramework.Query;

/// COMED'deki Framework.Linq desenlerinin karsiligi.
/// Tasinan servis kodunun neredeyse birebir ayni okunmasini sagliyor.
public static class QueryableExtensions
{
    /// Kosul saglanmiyorsa filtre hic uygulanmaz.
    public static IQueryable<T> Where<T>(
        this IQueryable<T> query, Expression<Func<T, bool>> predicate, bool condition)
        => condition ? query.Where(predicate) : query;

    /// SQL LIKE deseni: bos ise null doner ve cagiran filtreyi atlar.
    public static string ToSqlSearchString(this string? search)
        => string.IsNullOrWhiteSpace(search) ? "%" : $"%{search.Trim()}%";

    /// FilterRequest'teki sayfalama ve siralamayi uygular.
    /// OrderBy verilmemisse varsayilan anahtarlara gore siralanir.
    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, FilterRequest request)
    {
        if (request.Skip > 0) query = query.Skip(request.Skip);

        if (request.Take is > 0) query = query.Take(request.Take.Value);

        return query;
    }

    public static IOrderedQueryable<T> OrderByDefault<T, TKey>(
        this IQueryable<T> query, Expression<Func<T, TKey>> keySelector)
        => query.OrderBy(keySelector);
}
