using IQOne.Zero;
using IQOne.Zero.Persistence;
using Zero.Sample.Orders.Data;

namespace Zero.Sample.Orders.Catalog;

/// <summary>Something that can be ordered.</summary>
public sealed class Product : IAggregateRoot, IEntity<int>, IAuditedEntity
{
    /// <inheritdoc />
    public int Id { get; private set; }

    /// <summary>The customer-visible code.</summary>
    public required string Code { get; init; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>How many are on the shelf.</summary>
    public int Stock { get; private set; }

    /// <summary>Whether it may be ordered at all.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>
    /// Takes stock off the shelf.
    /// </summary>
    /// <remarks>
    /// On the entity rather than in a handler, so there is one place that knows stock cannot
    /// go negative. A handler that did <c>product.Stock -= n</c> would be one of several, and
    /// the second one would forget the check.
    /// </remarks>
    /// <param name="quantity">How many to take.</param>
    /// <returns>Success, or why it could not be taken.</returns>
    public Result Reserve(int quantity)
    {
        if (quantity <= 0)
            return Error.Validation("order.quantity", "The quantity must be at least one.");

        if (quantity > Stock)
            return Error.Conflict("product.stock", $"Only {Stock} of '{Code}' are left.");

        Stock -= quantity;

        return Result.Success();
    }

    /// <summary>Puts stock back, after an order was abandoned.</summary>
    /// <param name="quantity">How many to return.</param>
    public void Release(int quantity) => Stock += quantity;

    /// <summary>Sets the opening stock. Used when seeding.</summary>
    /// <param name="quantity">How many are on the shelf.</param>
    public void SetStock(int quantity) => Stock = quantity;
}
