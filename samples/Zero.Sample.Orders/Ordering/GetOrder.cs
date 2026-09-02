using IQOne.Zero;
using IQOne.Zero.Authorization;
using IQOne.Zero.Messaging;
using IQOne.Zero.Persistence;
using IQOne.Zero.Web;

namespace Zero.Sample.Orders.Ordering;

/// <summary>What the API returns for an order.</summary>
/// <param name="Reference">The reference the caller chose.</param>
/// <param name="State">Where it is in its life.</param>
/// <param name="Total">What it comes to.</param>
/// <param name="ExpiresAt">When it stops waiting for payment.</param>
/// <param name="Lines">What is on it.</param>
public sealed record OrderModel(
    string Reference,
    string State,
    decimal Total,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<OrderLineModel> Lines);

/// <summary>One line of an order, as the API returns it.</summary>
/// <param name="ProductCode">Which product.</param>
/// <param name="Quantity">How many.</param>
/// <param name="UnitPrice">What one cost when the order was placed.</param>
public sealed record OrderLineModel(string ProductCode, int Quantity, decimal UnitPrice);

/// <summary>Reads one order.</summary>
/// <param name="Reference">Which order.</param>
[Get("/orders/{reference}", Tag = "Ordering", Policy = OrderPolicies.Pay)]
public sealed record GetOrder(string Reference) : IQuery<OrderModel>;

/// <summary>Serves <see cref="GetOrder"/>.</summary>
/// <param name="orders">Where the orders are.</param>
/// <param name="authorizer">Decides whether the caller may see this particular order.</param>
public sealed class GetOrderHandler(
    IReadRepository<Order> orders,
    IResourceAuthorizer authorizer) : IQueryHandler<GetOrder, OrderModel>
{
    /// <inheritdoc />
    public async Task<Result<OrderModel>> HandleAsync(GetOrder query, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(new OrderByReference(query.Reference), cancellationToken);

        if (order is null)
            return Error.NotFound("order.missing", $"No order with reference '{query.Reference}'.");

        // Resource authorization, and it has to be here rather than in a behaviour: whether
        // this caller may see THIS order depends on the order, and nothing outside the
        // handler has loaded it. The policy on the route answered the cheaper question —
        // may this caller ask at all — before any data was read.
        var allowed = await authorizer.AuthorizeAsync(new MustOwnOrder(), order, cancellationToken);

        if (allowed.IsFailure) return allowed.Cast<OrderModel>();

        return new OrderModel(
            order.Reference,
            order.State.ToString(),
            order.Total,
            order.ExpiresAt,
            [.. order.Lines.Select(line => new OrderLineModel(line.ProductCode, line.Quantity, line.UnitPrice))]);
    }
}
