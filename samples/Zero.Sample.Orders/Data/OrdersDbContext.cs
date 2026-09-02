using IQOne.Zero.Persistence.Conventions;
using IQOne.Zero.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Zero.Sample.Orders.Catalog;
using Zero.Sample.Orders.Ordering;

namespace Zero.Sample.Orders.Data;

/// <summary>
/// This application's context.
/// </summary>
/// <remarks>
/// Deriving from <see cref="ConventionDbContext"/> is what applies the registered
/// conventions — the soft-delete filter and the audit stamps — to every entity that claims
/// them. Nothing here repeats either concern.
/// </remarks>
/// <param name="options">How the context connects and behaves.</param>
/// <param name="modelConventions">Rules applied to the model as a whole.</param>
/// <param name="filterConventions">Named filters applied to the entities they claim.</param>
public sealed class OrdersDbContext(
    DbContextOptions<OrdersDbContext> options,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
    IEnumerable<IEntityFilterConvention> filterConventions)
    : ConventionDbContext(options, modelConventions, filterConventions)
{
    /// <summary>Everything that can be ordered.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Every order.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <inheritdoc />
    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(product =>
        {
            product.HasKey(p => p.Id);
            product.Property(p => p.Code).HasMaxLength(32).IsRequired();
            product.Property(p => p.Name).HasMaxLength(200).IsRequired();
            product.HasIndex(p => p.Code).IsUnique();
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id);
            order.Property(o => o.Reference).HasMaxLength(32).IsRequired();

            // The reference is unique, and that constraint — not the validator — is what
            // actually prevents a duplicate. Two requests can pass a uniqueness check at the
            // same moment; only the database can be the last word.
            order.HasIndex(o => o.Reference).IsUnique();

            order.OwnsMany(o => o.Lines, line =>
            {
                line.WithOwner().HasForeignKey("OrderId");
                line.Property<int>("Id");
                line.HasKey("Id");
            });
        });
    }
}
