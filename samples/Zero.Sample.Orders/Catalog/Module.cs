using IQOne.Zero.Modules;
using IQOne.Zero.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Zero.Sample.Orders.Catalog;

/// <summary>
/// The catalogue module.
/// </summary>
/// <remarks>
/// <para>
/// The class is generated: its name, its dependencies, and the registration of every
/// handler, specification and convention in this assembly. Only what the generator cannot
/// know is written here.
/// </para>
/// <para>
/// The interface is added by this partial. The generated half implements
/// <c>IModuleConfigureServicesStep</c>; a module that also wants a later phase says so where
/// it uses it, and the framework runs it in dependency order with the rest.
/// </para>
/// </remarks>
public sealed partial class Module : IModuleInitializeStep
{
    /// <summary>
    /// Puts something on the shelf.
    /// </summary>
    /// <remarks>
    /// The initialize phase, because seeding needs services resolved and during
    /// configure-services there is no provider yet. And the catalogue's business rather than
    /// the host's: the host does not know what a product is.
    /// </remarks>
    /// <param name="context">The built provider.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    public async ValueTask OnInitializeAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        await using var scope = context.Services.CreateAsyncScope();

        var products = scope.ServiceProvider.GetRequiredService<IRepository<Product>>();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        if (await products.AnyAsync(new AllProducts(), cancellationToken)) return;

        foreach (var (code, name, stock) in Opening)
        {
            var product = new Product { Code = code, Name = name };

            product.SetStock(stock);

            await products.AddAsync(product, cancellationToken);
        }

        await work.SaveChangesAsync(cancellationToken);
    }

    /// <summary>What the shelf starts with.</summary>
    private static (string Code, string Name, int Stock)[] Opening =>
    [
        ("DESK-01", "Standing desk", 1_000),
        ("CHAIR-01", "Task chair", 1_000),

        // Deliberately scarce, so ordering more than the shelf holds is something you can
        // try rather than something you have to take on trust.
        ("LAMP-01", "Desk lamp", 5)
    ];
}
