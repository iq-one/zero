# ZERO500 — Publishing an event that leads back to the one being handled

**Severity:** error · **Category:** Zero.Events

A subscriber publishes an event whose subscribers publish, directly or through a chain, the
event this one handles.

```csharp
public sealed class OnInvoicePaid(IPublisher publisher) : IEventHandler<InvoicePaid>
{
    public async Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
    {
        await publisher.PublishAsync(new LedgerUpdated(@event.InvoiceId), cancellationToken);
        //                           ^ a subscriber to LedgerUpdated publishes InvoicePaid
        return Result.Success();
    }
}
```

At run time this recurses until the stack runs out. A `StackOverflowException` cannot be
caught, does not run a `finally`, and terminates the process without a log line — so the
first evidence is a container that restarted and no record of why.

`EventOptions.MaxPublishDepth` turns that into a catchable exception, but the cycle is
visible at compile time and a build error is better than an exception in production.

## Fix

**Do the second piece of work here.** If updating the ledger is part of what happens when an
invoice is paid, this subscriber can do it. An event between two steps that always run
together buys nothing and costs a cycle.

**Or publish from the command, not from a subscriber.** The handler that made the facts true
knows about both of them:

```csharp
invoice.Pay(command.Amount);
ledger.Record(invoice.Id, command.Amount);

await publisher.PublishAsync(new InvoicePaid(invoice.Id, command.Amount), cancellationToken);
await publisher.PublishAsync(new LedgerUpdated(invoice.Id), cancellationToken);
```

Two facts published side by side cannot form a cycle. A chain of subscribers each triggering
the next can, and the chain is also much harder to read six months later.

## What the rule can and cannot see

It follows publish calls through handler types it can resolve in this compilation and the
assemblies it references. A cycle that closes through a handler compiled later, or reached by
reflection, is not visible — `MaxPublishDepth` is the backstop for that.
