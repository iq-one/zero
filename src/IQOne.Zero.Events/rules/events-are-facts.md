---
id: zero.events.events-are-facts
title: Publish facts, and know what the caller's transaction covers
package: IQOne.Zero.Events
applies-to: ["**/*.cs"]
enforced-by: [ZERO500, ZERO501]
---

An event says something already happened. A request asks for something to happen. The
difference is direction, not shape, and it decides everything else: an event is named in the
past tense, has any number of subscribers, and returns nothing anyone waits on.

## Do

```csharp
public sealed record InvoicePaid(int InvoiceId, decimal Amount) : IEvent;

public sealed class UpdateLedger(ILedger ledger) : IEventHandler<InvoicePaid>
{
    public async Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
    {
        await ledger.RecordAsync(@event.InvoiceId, @event.Amount, cancellationToken);

        return Result.Success();
    }
}
```

Publish from the handler that made the fact true:

```csharp
invoice.Pay(command.Amount);

await publisher.PublishAsync(new InvoicePaid(invoice.Id, command.Amount), cancellationToken);
```

## What the caller's transaction covers — read this before you rely on either half

Publishing is in-process, awaited, and runs in **the caller's scope**. Subscribers therefore
share the caller's `DbContext` and unit of work, which has two consequences that people
usually discover one at a time and six months apart:

- **A subscriber's database writes are inside the caller's transaction.** If the command
  fails after publishing, `TransactionBehavior` rolls back the subscriber's writes along with
  everything else. That is what you want: the ledger entry should not survive an invoice
  payment that was undone.
- **Anything not transactional is not covered.** An email sent, an HTTP call made, a message
  put on a queue — those have already left. Rolling back the transaction does not recall
  them, and nothing reports that they escaped.

So: keep database work in subscribers, and put anything that leaves the process behind an
outbox, where it is written as part of the same transaction and dispatched after it commits.
Until `IQOne.Zero.Outbox` exists, write the intent to a table in the subscriber and send it
from a background job — the point is that the decision to send is committed with the fact,
not before it.

## Order between subscribers is not defined

Two subscribers to one event are independent. If one needs the other to have run first, they
are not two subscribers — they are one, or the second belongs behind its own event that the
first publishes.

A subscriber that works today because it happens to run second is a bug with a schedule.

## A subscriber's failure does not undo the event

The fact already happened; nothing about it is conditional on who managed to keep up. By
default every subscriber runs and `PublishResult` comes back carrying each outcome, so the
caller can see which one failed rather than only that something did.

```csharp
var published = await publisher.PublishAsync(new InvoicePaid(id, amount), cancellationToken);

if (published.IsFailure)
    logger.LogWarning("Subscribers behind on {Event}: {Outcomes}", published.EventType.Name, published.Outcomes);
```

Do not treat that failure as a reason to fail the command. The payment happened.

## Don't

Do not make an event settable. Subscribers run over the same instance, so a settable
property is a private channel between two of them in an order nobody defined. This is
**ZERO501**.

Do not publish an event that leads back to the one being handled — the compiler traces it and
reports **ZERO500**. Do the second piece of work in this subscriber, or publish from the
command instead of from a subscriber.

Do not give an event a response type. Something waiting for an answer has exactly one caller
and one answer, which makes it a request. There is no `IEvent<TResponse>`.
