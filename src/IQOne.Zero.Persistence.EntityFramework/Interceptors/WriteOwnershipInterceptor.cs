using IQOne.Zero.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IQOne.Zero.Persistence.EntityFramework.Interceptors;

/// <summary>
/// Refuses a write to a table this deployment has not claimed.
/// </summary>
/// <remarks>
/// <para>
/// The failure this prevents is silent. Writing through a replica, a synonym or another
/// application's table succeeds locally and disappears at the next synchronisation, so
/// nothing logs it and the row simply is not there later. Throwing at the write turns that
/// into a stack trace pointing at the handler that did it.
/// </para>
/// <para>
/// Registering no <see cref="IWriteOwnership"/> permits everything: an application that owns
/// its whole database should not have to say so. Registering several permits a table any one
/// of them claims, so modules can each declare their own without knowing about the rest.
/// </para>
/// </remarks>
/// <param name="ownerships">The ownership declarations to check against.</param>
public sealed class WriteOwnershipInterceptor(IEnumerable<IWriteOwnership> ownerships) : SaveChangesInterceptor
{
    private readonly IReadOnlyList<IWriteOwnership> _ownerships = [.. ownerships];

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Verify(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        cancellationToken.ThrowIfCancellationRequested();

        Verify(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Verify(DbContext? context)
    {
        if (context is null || _ownerships.Count == 0) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (Operation(entry.State) is not { } operation) continue;

            // Null for an entity mapped to a view, to a query, or to nothing at all. There is
            // no table to own, so there is nothing to refuse.
            if (entry.Metadata.GetTableName() is not { } table) continue;

            var schema = entry.Metadata.GetSchema();

            if (Permits(schema, table)) continue;

            throw new WriteOwnershipViolationException(
                schema is null ? table : $"{schema}.{table}", operation);
        }
    }

    private bool Permits(string? schema, string table)
    {
        foreach (var ownership in _ownerships)
            if (ownership.CanWrite(schema, table))
                return true;

        return false;
    }

    private static string? Operation(EntityState state) => state switch
    {
        EntityState.Added => "Insert",
        EntityState.Modified => "Update",
        EntityState.Deleted => "Delete",
        _ => null
    };
}
