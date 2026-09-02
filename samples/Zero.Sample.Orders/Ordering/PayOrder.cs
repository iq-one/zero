using IQOne.Zero;
using IQOne.Zero.Authorization;
using IQOne.Zero.Messaging;
using IQOne.Zero.Persistence;
using IQOne.Zero.Resilience;
using IQOne.Zero.Web;

namespace Zero.Sample.Orders.Ordering;

/// <summary>
/// Records payment for an order.
/// </summary>
/// <remarks>
/// Idempotent, and here the claim is easy to check: paying an order that is already paid
/// leaves it paid. Retrying cannot charge anyone twice, because this command records a
/// payment rather than taking one.
/// </remarks>
/// <param name="Reference">Which order.</param>
[Post("/orders/{reference}/pay", Tag = "Ordering", Policy = OrderPolicies.Pay)]
public sealed record PayOrder(string Reference) : ICommand, IIdempotent;

/// <summary>Serves <see cref="PayOrder"/>.</summary>
/// <param name="orders">Where the orders are.</param>
public sealed class PayOrderHandler(IRepository<Order> orders) : ICommandHandler<PayOrder>
{
    /// <inheritdoc />
    public async Task<Result<Unit>> HandleAsync(PayOrder command, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(new OrderByReference(command.Reference), cancellationToken);

        if (order is null)
            return Error.NotFound("order.missing", $"No order with reference '{command.Reference}'.");

        var paid = order.Pay();

        // No SaveChanges here. TransactionBehavior commits when the command succeeds and
        // rolls back when it fails — a handler that saved halfway through would have
        // committed part of its work by the time it returned a failure.
        return paid.IsFailure ? paid.Cast<Unit>() : Unit.Success;
    }
}
