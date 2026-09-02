using IQOne.Zero;
using IQOne.Zero.Authorization;
using IQOne.Zero.Events;
using IQOne.Zero.Messaging;
using IQOne.Zero.Persistence;
using IQOne.Zero.Resilience;
using IQOne.Zero.Validation;
using IQOne.Zero.Web;
using Microsoft.Extensions.Options;
using Zero.Sample.Orders.Catalog;
using Zero.Sample.Orders.Pricing;

namespace Zero.Sample.Orders.Ordering;

/// <summary>One product and how many of it.</summary>
/// <param name="ProductCode">Which product.</param>
/// <param name="Quantity">How many.</param>
public sealed record OrderItem(string ProductCode, int Quantity);

/// <summary>
/// Places an order.
/// </summary>
/// <remarks>
/// <see cref="Reference"/> is chosen by the caller. That is what makes the command safe to
/// retry, and it is why the handler can recognise an order it has already placed.
/// </remarks>
/// <param name="Reference">The reference the caller chose.</param>
/// <param name="Items">What is being ordered.</param>
[Post("/orders", Tag = "Ordering", Policy = OrderPolicies.Place)]
public sealed record PlaceOrder(string Reference, IReadOnlyList<OrderItem> Items)
    : ICommand<string>, IIdempotent;

/// <summary>What a new order must look like before anything reads it.</summary>
/// <param name="options">How ordering behaves here.</param>
public sealed class PlaceOrderValidator(IOptions<OrderingOptions> options) : Validator<PlaceOrder>
{
    /// <inheritdoc />
    protected override void Configure(RuleSet<PlaceOrder> rules)
    {
        rules.NotEmpty(order => order.Reference, "order.reference");
        rules.Length(order => order.Reference, "order.reference", 3, 32);
        rules.NotEmpty(order => order.Items, "order.items");

        rules.Must(
            order => order.Items.Count <= options.Value.MaxLines,
            "order.items.tooMany",
            $"An order may carry at most {options.Value.MaxLines} lines.");

        rules.Must(
            order => order.Items.All(item => item.Quantity > 0),
            "order.quantity",
            "Every line needs a quantity of at least one.");

        rules.Must(
            order => order.Items.Select(item => item.ProductCode).Distinct(StringComparer.Ordinal).Count()
                  == order.Items.Count,
            "order.items.duplicate",
            "The same product appears on more than one line; combine them.");
    }
}

/// <summary>Serves <see cref="PlaceOrder"/>.</summary>
/// <param name="orders">Where the orders are.</param>
/// <param name="products">Where the products are.</param>
/// <param name="pricing">What things cost.</param>
/// <param name="publisher">Tells whoever cares that the order was placed.</param>
/// <param name="options">How ordering behaves here.</param>
/// <param name="user">Who is asking.</param>
/// <param name="time">The clock.</param>
public sealed class PlaceOrderHandler(
    IRepository<Order> orders,
    IRepository<Product> products,
    IPricingService pricing,
    IPublisher publisher,
    IOptions<OrderingOptions> options,
    ICurrentUser user,
    TimeProvider time) : ICommandHandler<PlaceOrder, string>
{
    /// <inheritdoc />
    public async Task<Result<string>> HandleAsync(PlaceOrder command, CancellationToken cancellationToken)
    {
        // The command is retryable, so the first thing to do is recognise work already done.
        // Without this, a retry would place a second order under the same reference and the
        // unique index would reject it — correct, but as a 500 rather than the original answer.
        if (await orders.FindAsync(new OrderByReference(command.Reference), cancellationToken) is { } existing)
            return existing.Reference;

        var order = new Order
        {
            Reference = command.Reference,
            CustomerId = user.Id ?? "anonymous"
        };

        order.ExpiresOn(time.GetUtcNow() + options.Value.PaymentWindow);

        foreach (var item in command.Items)
        {
            var product = await products.FindAsync(
                new ProductByCode(item.ProductCode), cancellationToken);

            if (product is null)
                return Error.NotFound("product.missing", $"No product with code '{item.ProductCode}'.");

            // The entity owns the rule that stock cannot go negative, so there is one place
            // that knows it rather than one per handler.
            var reserved = product.Reserve(item.Quantity);

            if (reserved.IsFailure) return reserved.Cast<string>();

            // Retried by the pipeline when it answers Unavailable, which it does the first
            // time it is asked about anything. Nothing here mentions retrying.
            var price = await pricing.PriceAsync(item.ProductCode, cancellationToken);

            if (price.IsFailure) return price.Cast<string>();

            order.Add(new OrderLine(product.Id, product.Code, item.Quantity, price.Value));
        }

        await orders.AddAsync(order, cancellationToken);

        // Published before the transaction commits, so a subscriber's database writes are
        // inside it. Anything that leaves the process is not — see EmailCustomer.
        var published = await publisher.PublishAsync(
            new OrderPlaced(order.Reference, order.CustomerId, order.Total),
            cancellationToken);

        // A subscriber falling behind does not undo the order. The fact happened; the caller
        // gets its id, and the outcome of each subscriber is available to whoever logs it.
        _ = published.IsFailure;

        // The reference, not the id. The caller chose it, so it already has it — and an
        // identity column does not exist until the insert happens, which is after this
        // method returns. Returning what the caller supplied avoids needing to read it back
        // at all, and it is the same property that makes this command safe to retry.
        return order.Reference;
    }
}
