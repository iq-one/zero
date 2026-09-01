using IQOne.Zero.Persistence.Conventions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>
/// A real Sqlite database, held open in memory for the length of one test.
/// </summary>
/// <remarks>
/// Sqlite rather than the InMemory provider: a specification that does not translate has to
/// fail here, and InMemory would run it in LINQ-to-Objects and pass.
/// </remarks>
public sealed class ShopDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");

    public ShopDatabase()
    {
        // The database lives as long as the connection does, so it stays open across contexts.
        _connection.Open();

        using var schema = Plain();
        schema.Database.EnsureCreated();
    }

    /// <summary>The connection every context in one test shares.</summary>
    public SqliteConnection Connection => _connection;

    /// <summary>A context with no filters.</summary>
    public PlainShopContext Plain(params IInterceptor[] interceptors)
        => new(Options<PlainShopContext>(interceptors), [], []);

    /// <summary>A context with the soft-delete and tenant filters, speaking for one tenant.</summary>
    public FilteredShopContext Filtered(string tenant, params IInterceptor[] interceptors)
        => new(Options<FilteredShopContext>(interceptors), [], [new SoftDeleteFilter(), new TenantFilter()])
        {
            Tenant = tenant
        };

    /// <summary>A context whose tenant filter closed over a value instead of reading the context.</summary>
    public CapturedShopContext Captured(string tenant)
        => new(Options<CapturedShopContext>([]), [], [new CapturedTenantFilter()]) { Tenant = tenant };

    /// <summary>Puts a known set of invoices in the database, filters bypassed.</summary>
    public async Task SeedAsync()
    {
        await using var context = Plain();

        context.Invoices.AddRange(
            new Invoice
            {
                Tenant = "north", Customer = "Acme", Total = 300, Due = new DateOnly(2026, 1, 10),
                Lines = { new InvoiceLine { Description = "widget", Amount = 300 } }
            },
            new Invoice
            {
                Tenant = "north", Customer = "Bolt", Total = 100, Due = new DateOnly(2026, 1, 20),
                Lines =
                {
                    new InvoiceLine { Description = "nut", Amount = 40 },
                    new InvoiceLine { Description = "bolt", Amount = 60 }
                }
            },
            new Invoice { Tenant = "north", Customer = "Cog", Total = 200, Due = new DateOnly(2026, 1, 30), IsPaid = true },
            new Invoice { Tenant = "north", Customer = "Dial", Total = 500, Due = new DateOnly(2026, 2, 1), IsDeleted = true },
            new Invoice { Tenant = "south", Customer = "Edge", Total = 400, Due = new DateOnly(2026, 1, 5) },
            new Invoice { Tenant = "south", Customer = "Flux", Total = 600, Due = new DateOnly(2026, 1, 6), IsDeleted = true });

        await context.SaveChangesAsync();
    }

    public void Dispose() => _connection.Dispose();

    private DbContextOptions<TContext> Options<TContext>(IInterceptor[] interceptors)
        where TContext : DbContext
        => new DbContextOptionsBuilder<TContext>()
            .UseSqlite(_connection)
            .AddInterceptors(interceptors)
            .Options;
}

/// <summary>
/// A context whose tenant filter closes over a value instead of reading the context.
/// </summary>
/// <remarks>
/// Its own type, because the model is cached per context type and this one must never share
/// a model with <see cref="FilteredShopContext"/>.
/// </remarks>
public sealed class CapturedShopContext(
    DbContextOptions<CapturedShopContext> options,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
    IEnumerable<IEntityFilterConvention> filterConventions)
    : ShopContext(options, modelConventions, filterConventions);
