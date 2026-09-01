using IQOne.Zero.Data.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IQOne.Zero.Data.EntityFramework.Interceptors;

/// <summary>
/// Runs the registered save-changes conventions before persisting.
/// </summary>
public sealed class ConventionSaveChangesInterceptor(
    IEnumerable<ISaveChangesConvention<DbContext>> conventions) : SaveChangesInterceptor
{
    private readonly IReadOnlyList<ISaveChangesConvention<DbContext>> _conventions = [.. conventions];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        foreach (var convention in _conventions) convention.Apply(context);
    }
}
