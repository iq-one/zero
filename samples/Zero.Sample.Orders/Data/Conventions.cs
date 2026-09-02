using System.Linq.Expressions;
using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace Zero.Sample.Orders.Data;

/// <summary>
/// Hides deleted rows from every query that does not ask for them.
/// </summary>
/// <remarks>
/// Registered under its own name, so a query can opt out of exactly this and keep whatever
/// else is filtering. That is the whole reason filters are named: a report that needs to see
/// deleted orders must not also stop filtering by tenant.
/// </remarks>
public sealed class SoftDeleteConvention : IEntityFilterConvention, ISingleton
{
    /// <summary>The name a specification uses to see deleted rows.</summary>
    public const string Name = "soft-delete";

    /// <inheritdoc />
    public string Key => Name;

    /// <inheritdoc />
    public bool AppliesTo(Type entityType) => typeof(IAuditedEntity).IsAssignableFrom(entityType);

    /// <inheritdoc />
    public LambdaExpression? Build(Type entityType, object context)
    {
        var row = Expression.Parameter(entityType, "row");

        return Expression.Lambda(
            Expression.Not(Expression.Property(row, nameof(IAuditedEntity.IsDeleted))),
            row);
    }
}

/// <summary>
/// Stamps rows as they are written, and turns a delete into a soft delete.
/// </summary>
/// <remarks>
/// Here rather than in a handler because it has to be true of every write. A handler that
/// stamped its own rows would be one of many, and the one added next month would forget.
/// </remarks>
/// <param name="time">The clock. Never <c>DateTimeOffset.UtcNow</c>, so a test can state the time.</param>
public sealed class AuditConvention(TimeProvider time) : ISaveChangesConvention<DbContext>, ISingleton
{
    /// <inheritdoc />
    public void Apply(DbContext context)
    {
        var now = time.GetUtcNow();

        foreach (var entry in context.ChangeTracker.Entries<IAuditedEntity>())
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    break;

                // A row nothing points at any more is still a row somebody may need to
                // explain. Deleting it destroys the answer to "what happened to that order".
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.ModifiedAt = now;
                    break;
            }
    }
}
