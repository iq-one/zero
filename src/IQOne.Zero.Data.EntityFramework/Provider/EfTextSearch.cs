using System.Linq.Expressions;
using IQOne.Zero.Data.Provider;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Data.EntityFramework.Provider;

/// <summary>Entity Framework implementation, expressed as SQL <c>LIKE</c>.</summary>
public sealed class EfTextSearch<T> : ITextSearch<T>
{
    public IQueryable<T> Apply(
        IQueryable<T> source, string? term, params Expression<Func<T, string?>>[] fields)
    {
        if (string.IsNullOrWhiteSpace(term) || fields.Length == 0) return source;

        var pattern = $"%{term.Trim()}%";
        var parameter = fields[0].Parameters[0];

        Expression? body = null;

        foreach (var field in fields)
        {
            var member = new ParameterReplacer(parameter).Visit(field.Body)!;

            var like = Expression.Call(
                typeof(DbFunctionsExtensions).GetMethod(
                    nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!,
                Expression.Constant(EF.Functions),
                member,
                Expression.Constant(pattern));

            body = body is null ? like : Expression.OrElse(body, like);
        }

        return source.Where(Expression.Lambda<Func<T, bool>>(body!, parameter));
    }

    private sealed class ParameterReplacer(ParameterExpression parameter) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => parameter;
    }
}
