using IQOne.Zero.Persistence;

namespace Zero.Sample.Orders.Catalog;

/// <summary>Every product, whatever its state. Used to decide whether the shelf is empty.</summary>
public sealed class AllProducts : Specification<Product>;

/// <summary>One product by its customer-visible code.</summary>
public sealed class ProductByCode : Specification<Product>
{
    /// <summary>Builds the query.</summary>
    /// <param name="code">The code to look for.</param>
    public ProductByCode(string code) => Where(product => product.Code == code);
}
