using IQOne.Zero.Persistence;
using Microsoft.EntityFrameworkCore;
using Zero.Sample.Orders.Catalog;

namespace Zero.Sample.Orders.Data;

/// <summary>Puts something on the shelf, so the sample answers on first run.</summary>
public static class Seed
{
    /// <summary>
    /// Creates the database and adds a few products.
    /// </summary>
    /// <remarks>
    /// <c>EnsureCreated</c> because this is a sample with a throwaway Sqlite file. A real
    /// application uses migrations — a schema created from the model has no history, so the
    /// second version of it has nothing to migrate from.
    /// </remarks>
    /// <param name="app">The built application.</param>
    public static async Task SeedAsync(this IHost app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();

        await context.Database.EnsureCreatedAsync();

        if (await context.Products.AnyAsync()) return;

        foreach (var (code, name, stock) in new[]
                 {
                     ("DESK-01", "Standing desk", 1_000),
                     ("CHAIR-01", "Task chair", 1_000),
                     // Deliberately scarce, so ordering more than the shelf holds is
                     // something you can try rather than something you have to believe.
                     ("LAMP-01", "Desk lamp", 5)
                 })
        {
            var product = new Product { Code = code, Name = name };

            product.SetStock(stock);
            context.Products.Add(product);
        }

        // Directly on the context, not through the unit of work: seeding happens before the
        // application accepts anything, so there is no request whose transaction this belongs
        // to. Everything after this point goes through a repository.
        await context.SaveChangesAsync();
    }
}
