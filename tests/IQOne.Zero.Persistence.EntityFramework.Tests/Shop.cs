using System.Linq.Expressions;
using IQOne.Zero.Persistence;
using IQOne.Zero.Persistence.Conventions;
using IQOne.Zero.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>An entity that is hidden rather than removed.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}

/// <summary>An entity that belongs to one tenant.</summary>
public interface ITenantOwned
{
    string Tenant { get; set; }
}

public sealed class Invoice : IAggregateRoot, IEntity<int>, ISoftDeletable, ITenantOwned
{
    public int Id { get; set; }

    public string Tenant { get; set; } = "";

    public string Customer { get; set; } = "";

    /// <summary>In minor units. Sqlite refuses to order by decimal, and cents order fine.</summary>
    public int Total { get; set; }

    public bool IsPaid { get; set; }

    public bool IsDeleted { get; set; }

    public DateOnly Due { get; set; }

    public List<InvoiceLine> Lines { get; } = [];
}

public sealed class InvoiceLine
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public string Description { get; set; } = "";

    public int Amount { get; set; }
}

/// <summary>A table another application owns; this deployment reads it and must not write it.</summary>
public sealed class LedgerEntry : IAggregateRoot, IEntity<int>
{
    public int Id { get; set; }

    public string Note { get; set; } = "";
}

/// <summary>The mapping every test context shares.</summary>
public abstract class ShopContext(
    DbContextOptions options,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
    IEnumerable<IEntityFilterConvention> filterConventions)
    : ConventionDbContext(options, modelConventions, filterConventions)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> Lines => Set<InvoiceLine>();

    public DbSet<LedgerEntry> Ledger => Set<LedgerEntry>();

    /// <summary>
    /// The tenant this instance speaks for.
    /// </summary>
    /// <remarks>
    /// Settable on purpose: the tests change it between requests to prove the filter reads it
    /// each time rather than remembering what it said when the model was built.
    /// </remarks>
    public string Tenant { get; set; } = "";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.ToTable("invoices");
            invoice.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
        });

        modelBuilder.Entity<InvoiceLine>(line => line.ToTable("invoice_lines"));
        modelBuilder.Entity<LedgerEntry>(entry => entry.ToTable("ledger"));
    }
}

/// <summary>A context with both filters. Always the same set, because the model is cached per type.</summary>
public sealed class FilteredShopContext(
    DbContextOptions<FilteredShopContext> options,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
    IEnumerable<IEntityFilterConvention> filterConventions)
    : ShopContext(options, modelConventions, filterConventions);

/// <summary>A context with no filters, for the tests that are not about filtering.</summary>
public sealed class PlainShopContext(
    DbContextOptions<PlainShopContext> options,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
    IEnumerable<IEntityFilterConvention> filterConventions)
    : ShopContext(options, modelConventions, filterConventions);

public sealed class SoftDeleteFilter : IEntityFilterConvention
{
    public string Key => "soft-delete";

    public bool AppliesTo(Type entityType) => typeof(ISoftDeletable).IsAssignableFrom(entityType);

    public LambdaExpression Build(Type entityType, object context)
    {
        var entity = Expression.Parameter(entityType, "e");

        return Expression.Lambda(
            Expression.Not(Expression.Property(entity, nameof(ISoftDeletable.IsDeleted))), entity);
    }
}

public sealed class TenantFilter : IEntityFilterConvention
{
    public string Key => "tenant";

    public bool AppliesTo(Type entityType) => typeof(ITenantOwned).IsAssignableFrom(entityType);

    public LambdaExpression Build(Type entityType, object context)
    {
        // Typed as the context, not as object. EF swaps a subexpression whose type the running
        // context is assignable to for that context, and it explicitly skips anything typed
        // object — so 'Expression.Constant(context)' would bake this instance in for good.
        var shop = Expression.Constant(context, typeof(ShopContext));
        var entity = Expression.Parameter(entityType, "e");

        return Expression.Lambda(
            Expression.Equal(
                Expression.Property(entity, nameof(ITenantOwned.Tenant)),
                Expression.Property(shop, nameof(ShopContext.Tenant))),
            entity);
    }
}

/// <summary>The mistake the abstraction warns about, kept here so a test can show it failing.</summary>
public sealed class CapturedTenantFilter : IEntityFilterConvention
{
    public string Key => "captured-tenant";

    public bool AppliesTo(Type entityType) => typeof(ITenantOwned).IsAssignableFrom(entityType);

    public LambdaExpression Build(Type entityType, object context)
    {
        // Read here, at model-building time, and therefore frozen into the cached model.
        var tenant = ((ShopContext)context).Tenant;

        var entity = Expression.Parameter(entityType, "e");

        return Expression.Lambda(
            Expression.Equal(
                Expression.Property(entity, nameof(ITenantOwned.Tenant)),
                Expression.Constant(tenant, typeof(string))),
            entity);
    }
}

/// <summary>Turns a delete into a soft delete before the write reaches the table.</summary>
public sealed class SoftDeleteWrites : ISaveChangesConvention<DbContext>
{
    public void Apply(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State is not EntityState.Deleted) continue;

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
        }
    }
}

/// <summary>Stamps new rows with the tenant the saving context speaks for.</summary>
public sealed class StampTenantOnWrite : ISaveChangesConvention<DbContext>
{
    public void Apply(DbContext context)
    {
        if (context is not ShopContext shop) return;

        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
            if (entry.State is EntityState.Added)
                entry.Entity.Tenant = shop.Tenant;
    }
}

/// <summary>This deployment owns the invoice tables and nothing else.</summary>
public sealed class InvoicesOnly : IWriteOwnership
{
    public bool CanWrite(string? schema, string table) => table is "invoices" or "invoice_lines";
}

/// <summary>Applies a rule to the model as a whole, to prove model conventions run at all.</summary>
public sealed class EveryStringIsShort : IModelConvention<ModelBuilder>
{
    public void Apply(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entity => entity.GetProperties())
                     .Where(property => property.ClrType == typeof(string)))
        {
            property.SetMaxLength(64);
        }
    }
}
