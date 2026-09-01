using IQOne.Zero.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IQOne.Zero.Persistence.EntityFramework.Interceptors;

/// <summary>
/// Runs every registered save-changes convention just before the write.
/// </summary>
/// <remarks>
/// <para>
/// An interceptor rather than an override of <c>SaveChanges</c>: an override only fires for
/// the one context that has it, and the rule is meant to be true of every write in the
/// application. It also keeps the context free of rules that have nothing to do with mapping.
/// </para>
/// <para>
/// Conventions run first among Zero's interceptors, because one of them may turn a delete
/// into an update and the ownership check has to see the operation that will actually reach
/// the table.
/// </para>
/// </remarks>
/// <param name="conventions">The rules to apply, in registration order.</param>
public sealed class SaveChangesConventionInterceptor(IEnumerable<ISaveChangesConvention<DbContext>> conventions)
    : SaveChangesInterceptor
{
    private readonly IReadOnlyList<ISaveChangesConvention<DbContext>> _conventions = [.. conventions];

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Apply(eventData.Context);

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

        Apply(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        foreach (var convention in _conventions) convention.Apply(context);
    }
}
