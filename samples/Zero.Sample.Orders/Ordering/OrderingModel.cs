using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace Zero.Sample.Orders.Ordering;

/// <summary>Maps the ordering module's entities.</summary>
public sealed class OrderingModel : IModelConvention<ModelBuilder>, ISingleton
{
    /// <inheritdoc />
    public void Apply(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id);
            order.Property(o => o.Reference).HasMaxLength(32).IsRequired();

            // The unique index, not the validator, is what actually prevents a duplicate
            // reference. Two requests can pass a uniqueness check at the same moment; only
            // the database can be the last word.
            order.HasIndex(o => o.Reference).IsUnique();

            order.OwnsMany(o => o.Lines, line =>
            {
                line.WithOwner().HasForeignKey("OrderId");
                line.Property<int>("Id");
                line.HasKey("Id");
            });
        });
}
