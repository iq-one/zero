using IQOne.Zero;
using IQOne.Zero.Caching;
using IQOne.Zero.Messaging;
using IQOne.Zero.Persistence;
using IQOne.Zero.Web;

namespace Zero.Sample.Orders.Catalog;

/// <summary>What the API returns for a product.</summary>
/// <param name="Code">The customer-visible code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Stock">How many are on the shelf.</param>
public sealed record ProductModel(string Code, string Name, int Stock);

/// <summary>
/// Everything on sale, one page at a time.
/// </summary>
/// <remarks>
/// Cacheable, and the key carries the page — a constant key on a paged query hands page
/// three to whoever asked for page one, which is ZERO211.
/// </remarks>
/// <param name="Skip">How many to skip.</param>
/// <param name="Take">How many to take.</param>
[Get("/products", Tag = "Catalog", AllowAnonymous = true)]
public sealed record GetProducts(int Skip = 0, int Take = 20) : IQuery<IReadOnlyList<ProductModel>>, ICacheable
{
    /// <inheritdoc />
    public string CacheKey => $"catalog:products:{Skip}:{Take}";

    /// <summary>
    /// A minute. The catalogue tolerates being a minute stale; a stock level shown on an
    /// order confirmation would not, which is why that reads the order and not this.
    /// </summary>
    public TimeSpan? Lifetime => TimeSpan.FromMinutes(1);
}

/// <summary>Products on sale, soonest code first.</summary>
public sealed class AvailableProducts : Specification<Product, ProductModel>
{
    /// <summary>Builds the query.</summary>
    /// <param name="skip">How many to skip.</param>
    /// <param name="take">How many to take.</param>
    public AvailableProducts(int skip, int take)
    {
        Where(product => product.IsAvailable);
        OrderBy(product => product.Code);
        Page(skip, take);
        ReadOnly();
    }

    /// <inheritdoc />
    public override System.Linq.Expressions.Expression<Func<Product, ProductModel>> Selector =>
        product => new ProductModel(product.Code, product.Name, product.Stock);
}

/// <summary>Serves <see cref="GetProducts"/>.</summary>
/// <param name="products">Where the products are.</param>
public sealed class GetProductsHandler(IReadRepository<Product> products)
    : IQueryHandler<GetProducts, IReadOnlyList<ProductModel>>
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ProductModel>>> HandleAsync(
        GetProducts query, CancellationToken cancellationToken)
    {
        // No Entity Framework, no HTTP, no cache, no log. The projection happens in the
        // database, because the specification carries a selector.
        var page = await products.ListAsync(new AvailableProducts(query.Skip, query.Take), cancellationToken);

        // Result.Success rather than a bare `return page;`: the value's type is an interface,
        // and C# will not apply a user-defined conversion to one.
        return Result.Success(page);
    }
}
