using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace Zero.Sample.Orders.Catalog;

/// <summary>
/// Maps the catalogue's entities.
/// </summary>
/// <remarks>
/// The module's own mapping, contributed to whatever context the application built. The
/// context does not name <see cref="Product"/>, so this module can be added or removed
/// without touching it.
/// </remarks>
public sealed class CatalogModel : IModelConvention<ModelBuilder>, ISingleton
{
    /// <inheritdoc />
    public void Apply(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Product>(product =>
        {
            product.HasKey(p => p.Id);
            product.Property(p => p.Code).HasMaxLength(32).IsRequired();
            product.Property(p => p.Name).HasMaxLength(200).IsRequired();
            product.HasIndex(p => p.Code).IsUnique();
        });
}
