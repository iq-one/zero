using IQOne.Zero;
using IQOne.Zero.Messaging;
using IQOne.Zero.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zero.Sample.Orders.Catalog;
using Zero.Sample.Orders.Configuration;

namespace Zero.Sample.Orders.Ordering;

/// <summary>
/// Releases the stock held by orders that were never paid for.
/// </summary>
/// <remarks>
/// A command, not a job class, and that is the point: the sweep is a use case, so it has one
/// implementation, one place in the pipeline and one transaction — whether the schedule
/// triggered it or an operator did. <c>AddRecurringCommand</c> is what puts it on a clock.
/// </remarks>
/// <param name="AsOf">
/// The moment to compare against. Carried by the command rather than read from a clock, so
/// the sweep covers the occurrence it was scheduled for. A run that took its window from the
/// clock would leave a gap the size of its own start-up delay, every time.
/// </param>
public sealed record ExpireUnpaidOrders(DateTimeOffset AsOf) : ICommand<int>;

/// <summary>Serves <see cref="ExpireUnpaidOrders"/>.</summary>
/// <param name="orders">Where the orders are.</param>
/// <param name="products">Where the products are.</param>
/// <param name="options">How many to sweep at once.</param>
/// <param name="logger">Records what the sweep did.</param>
public sealed class ExpireUnpaidOrdersHandler(
    IRepository<Order> orders,
    IRepository<Product> products,
    IOptions<OrderingOptions> options,
    ILogger<ExpireUnpaidOrdersHandler> logger) : ICommandHandler<ExpireUnpaidOrders, int>
{
    /// <inheritdoc />
    public async Task<Result<int>> HandleAsync(
        ExpireUnpaidOrders command, CancellationToken cancellationToken)
    {
        var overdue = await orders.ListAsync(
            new UnpaidOrdersDueBefore(command.AsOf, options.Value.ExpirySweepSize),
            cancellationToken);

        var expired = 0;

        foreach (var order in overdue)
        {
            if (!order.Expire()) continue;

            foreach (var line in order.Lines)
            {
                var product = await products.FindAsync(
                    new ProductByCode(line.ProductCode), cancellationToken);

                product?.Release(line.Quantity);
            }

            expired++;
        }

        // A domain event worth recording — "we released stock" is a fact about the business,
        // not telemetry about the request, which is why logging it here is not the thing
        // ZERO401 is about.
        if (expired > 0) logger.LogInformation("Released the stock held by {Count} unpaid orders", expired);

        return expired;
    }
}
