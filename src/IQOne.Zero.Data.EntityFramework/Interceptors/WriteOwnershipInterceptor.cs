using IQOne.Zero.Data.Ownership;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IQOne.Zero.Data.EntityFramework.Interceptors;

/// <summary>
/// Rejects writes to tables this deployment does not own, turning a silent data loss
/// into an explicit failure.
/// </summary>
public sealed class WriteOwnershipInterceptor(IWriteOwnership ownership) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Verify(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Verify(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Verify(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var table = entry.Metadata.GetTableName();

            if (table is null) continue;

            if (!ownership.CanWrite(entry.Metadata.GetSchema(), table))
                throw new WriteOwnershipViolationException(table, entry.State.ToString());
        }
    }
}
