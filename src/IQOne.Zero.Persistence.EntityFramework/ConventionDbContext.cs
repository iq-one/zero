using IQOne.Zero.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework;

/// <summary>
/// A context that applies the application's registered conventions to every entity it maps.
/// </summary>
/// <remarks>
/// <para>
/// Derive from this instead of <see cref="DbContext"/> and put the application's own mapping
/// in <see cref="ConfigureModel"/>. <see cref="OnModelCreating"/> is sealed so the ordering
/// holds: a rule that has to cover every entity cannot run before the last entity is mapped.
/// </para>
/// <para>
/// The model is built once and cached for the lifetime of the process. That is why
/// <see cref="IEntityFilterConvention.Build"/> is handed the context: a filter that reads
/// through it is re-read on every query, while one that closes over a value read here keeps
/// that value forever — and the second request quietly sees the first request's tenant.
/// </para>
/// </remarks>
public abstract class ConventionDbContext : DbContext
{
    private readonly IReadOnlyList<IModelConvention<ModelBuilder>> _modelConventions;
    private readonly IReadOnlyList<IEntityFilterConvention> _filterConventions;

    /// <summary>Creates a context that applies the conventions it is given.</summary>
    /// <param name="options">How the context connects and behaves.</param>
    /// <param name="modelConventions">Rules applied to the model as a whole.</param>
    /// <param name="filterConventions">Named filters applied to the entities they claim.</param>
    protected ConventionDbContext(
        DbContextOptions options,
        IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
        IEnumerable<IEntityFilterConvention> filterConventions)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(modelConventions);
        ArgumentNullException.ThrowIfNull(filterConventions);

        _modelConventions = [.. modelConventions];
        _filterConventions = [.. filterConventions];
    }

    /// <summary>
    /// Maps the application's own entities.
    /// </summary>
    /// <remarks>
    /// Called before the conventions, so anything configured here — including entity types
    /// discovered by <c>ApplyConfigurationsFromAssembly</c> — is in the model by the time a
    /// filter looks for entities to apply to.
    /// </remarks>
    /// <param name="modelBuilder">The model being built.</param>
    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }

    /// <inheritdoc />
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        ConfigureModel(modelBuilder);

        foreach (var convention in _modelConventions) convention.Apply(modelBuilder);

        ApplyFilters(modelBuilder);
    }

    private void ApplyFilters(ModelBuilder modelBuilder)
    {
        if (_filterConventions.Count == 0) return;

        var claimed = new Dictionary<string, string>(StringComparer.Ordinal);

        // Materialised first: applying a filter can touch the model, and enumerating it while
        // it changes is a failure that only shows up on the mapping that happens to be last.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            // A filter belongs to the root of an inheritance hierarchy — EF refuses it on a
            // derived type, and the root's filter already covers the derived rows. An owned
            // type is filtered through its owner for the same reason.
            if (entityType.BaseType is not null || entityType.IsOwned()) continue;

            claimed.Clear();

            foreach (var convention in _filterConventions)
            {
                if (!convention.AppliesTo(entityType.ClrType)) continue;

                // 'this', never a value lifted out of it. See the remarks on the type.
                var filter = convention.Build(entityType.ClrType, this);

                if (filter is null) continue;

                if (claimed.TryGetValue(convention.Key, out var previous))
                {
                    throw new InvalidOperationException(
                        $"'{previous}' and '{convention.GetType().Name}' both register the filter " +
                        $"'{convention.Key}' on '{entityType.DisplayName()}'. The second would replace " +
                        "the first and nothing would report it. Give one of them a different key.");
                }

                claimed[convention.Key] = convention.GetType().Name;

                entityType.SetQueryFilter(convention.Key, filter);
            }
        }
    }
}
