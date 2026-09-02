using IQOne.Zero;
using IQOne.Zero.Events;
using Microsoft.Extensions.Logging;

namespace Zero.Sample.Orders.Ordering;

/// <summary>An order was placed. Past tense, because it already happened.</summary>
/// <param name="Reference">
/// The reference the caller chose. Not the database id: at the moment this is published the
/// insert has not happened, so the id is still zero — and a subscriber that stored it would
/// store a zero for every order.
/// </param>
/// <param name="CustomerId">Who placed it.</param>
/// <param name="Total">What it came to.</param>
public sealed record OrderPlaced(string Reference, string CustomerId, decimal Total) : IEvent;

/// <summary>
/// Writes the order to the ledger.
/// </summary>
/// <remarks>
/// A database write, so it is inside the caller's transaction and is rolled back with it if
/// the command fails after publishing. That is the half of publishing that is covered.
/// </remarks>
/// <param name="logger">Where the sample shows what happened, having no real ledger.</param>
public sealed class RecordInLedger(ILogger<RecordInLedger> logger) : IEventHandler<OrderPlaced>
{
    /// <inheritdoc />
    public Task<Result> HandleAsync(OrderPlaced @event, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Ledger: order {Reference} for {Total}", @event.Reference, @event.Total);

        return Task.FromResult(Result.Success());
    }
}

/// <summary>
/// Tells the customer.
/// </summary>
/// <remarks>
/// An email is the half of publishing that is <em>not</em> covered: once it has left, rolling
/// the transaction back does not recall it, and nothing reports that it escaped. In a real
/// application the intent to send would be written inside the transaction and dispatched
/// after it commits — which is what an outbox is.
///
/// It also fails here on purpose, to show that one subscriber falling behind does not deprive
/// the others of the fact, and that the caller can see which one it was.
/// </remarks>
/// <param name="logger">Where the sample shows what happened, having no real mailer.</param>
public sealed class EmailCustomer(ILogger<EmailCustomer> logger) : IEventHandler<OrderPlaced>
{
    /// <inheritdoc />
    public Task<Result> HandleAsync(OrderPlaced @event, CancellationToken cancellationToken)
    {
        if (@event.Total > 1_000m)
            return Task.FromResult(Result.Failure(
                Error.Unavailable("mail.refused", "The mail gateway refused a large-order notice.")));

        logger.LogInformation("Mail: told {CustomerId} about {Reference}", @event.CustomerId, @event.Reference);

        return Task.FromResult(Result.Success());
    }
}
