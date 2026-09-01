using IQOne.Zero.Data.Conventions;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Data.EntityFramework.Context;

/// <summary>
/// Base context that applies the registered model and filter conventions to every
/// entity in the model.
/// </summary>
/// <remarks>
/// The context knows no application-specific column or filter; those arrive as
/// conventions supplied by the application.
/// </remarks>
public abstract class ConventionDbContext(
    DbContextOptions options,
    IEnumerable<IEntityFilterConvention> filterConventions,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions) : DbContext(options)
{
    private readonly IReadOnlyList<IEntityFilterConvention> _filters = [.. filterConventions];
    private readonly IReadOnlyList<IModelConvention<ModelBuilder>> _model = [.. modelConventions];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var convention in _model) convention.Apply(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var convention in _filters)
                if (convention.Build(entityType.ClrType, this) is { } filter)
                    modelBuilder.Entity(entityType.ClrType).HasQueryFilter(convention.Key, filter);
    }
}
