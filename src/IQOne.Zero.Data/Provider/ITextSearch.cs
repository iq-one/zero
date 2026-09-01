using System.Linq.Expressions;

namespace IQOne.Zero.Data.Provider;

/// <summary>
/// Free-text matching over the given fields. The repository states which fields to
/// search; how the match is expressed belongs to the provider.
/// </summary>
public interface ITextSearch<T>
{
    IQueryable<T> Apply(IQueryable<T> source, string? term, params Expression<Func<T, string?>>[] fields);
}
