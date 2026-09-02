using IQOne.Zero;
using IQOne.Zero.Persistence;
using Zero.Sample.Orders.Data;

namespace Zero.Sample.Orders.Ordering;

/// <summary>Where an order is in its life.</summary>
public enum OrderState
{
    /// <summary>Placed, stock reserved, not yet paid.</summary>
    AwaitingPayment = 1,

    /// <summary>Paid.</summary>
    Paid = 2,

    /// <summary>Abandoned before payment; the stock went back.</summary>
    Expired = 3
}

/// <summary>One product on an order.</summary>
/// <param name="ProductId">Which product.</param>
/// <param name="ProductCode">Its code at the time of ordering, so the line still reads later.</param>
/// <param name="Quantity">How many.</param>
/// <param name="UnitPrice">What one cost at the time of ordering.</param>
public sealed record OrderLine(int ProductId, string ProductCode, int Quantity, decimal UnitPrice)
{
    /// <summary>What this line comes to.</summary>
    public decimal Total => Quantity * UnitPrice;
}

/// <summary>
/// An order, which is the boundary a transaction is drawn around.
/// </summary>
/// <remarks>
/// The lines are owned by the order and loaded and saved with it. That is what makes
/// "an order's total" a question with one answer — without the boundary, a total is
/// whatever rows the last query happened to touch.
/// </remarks>
public sealed class Order : IAggregateRoot, IEntity<int>, IAuditedEntity
{
    private readonly List<OrderLine> _lines = [];

    /// <inheritdoc />
    public int Id { get; private set; }

    /// <summary>
    /// The reference the caller chose.
    /// </summary>
    /// <remarks>
    /// Chosen by the caller, not generated here, and that is what lets the place-order
    /// command be retried safely: the second attempt carries the same reference, so the
    /// handler can recognise work it has already done.
    /// </remarks>
    public required string Reference { get; init; }

    /// <summary>Who placed it.</summary>
    public required string CustomerId { get; init; }

    /// <summary>Where it is in its life.</summary>
    public OrderState State { get; private set; } = OrderState.AwaitingPayment;

    /// <summary>What is on it.</summary>
    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>What it comes to.</summary>
    public decimal Total => _lines.Sum(line => line.Total);

    /// <summary>When it stops waiting for payment.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>Adds a line.</summary>
    /// <param name="line">What to add.</param>
    public void Add(OrderLine line) => _lines.Add(line);

    /// <summary>States when it stops waiting for payment.</summary>
    /// <param name="at">The moment it expires.</param>
    public void ExpiresOn(DateTimeOffset at) => ExpiresAt = at;

    /// <summary>
    /// Records payment.
    /// </summary>
    /// <remarks>
    /// Paying an order that is already paid succeeds and changes nothing, which is what makes
    /// the command idempotent rather than merely retried. The claim is not "the second call
    /// is harmless"; it is "the state afterwards is the one a single call would have left".
    /// </remarks>
    /// <returns>Success, or why it could not be paid.</returns>
    public Result Pay()
    {
        if (State == OrderState.Paid) return Result.Success();

        if (State == OrderState.Expired)
            return Error.Conflict("order.expired", "That order expired and its stock was released.");

        State = OrderState.Paid;

        return Result.Success();
    }

    /// <summary>Marks it abandoned. Its stock is released by whoever calls this.</summary>
    /// <returns>Whether the order changed.</returns>
    public bool Expire()
    {
        if (State != OrderState.AwaitingPayment) return false;

        State = OrderState.Expired;

        return true;
    }
}
