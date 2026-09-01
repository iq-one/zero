using System.Linq.Expressions;

namespace IQOne.Zero.Data.Query;

/// <summary>Provider-neutral LINQ helpers used by handlers.</summary>
public static class QueryableExtensions
{
    /// <summary>Applies the predicate only when <paramref name="condition"/> holds.</summary>
    public static IQueryable<T> Where<T>(
        this IQueryable<T> query, Expression<Func<T, bool>> predicate, bool condition)
        => condition ? query.Where(predicate) : query;

    public static IQueryable<T> Page<T>(this IQueryable<T> query, int skip, int? take)
    {
        if (skip > 0) query = query.Skip(skip);
        if (take is > 0) query = query.Take(take.Value);

        return query;
    }
}
