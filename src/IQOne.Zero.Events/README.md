# IQOne.Zero.Events

Publish a fact to any number of subscribers, in process and awaited.

```csharp
public sealed record InvoicePaid(int InvoiceId, decimal Amount) : IEvent;

public sealed class UpdateLedger(ILedger ledger) : IEventHandler<InvoicePaid>
{
    public async Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
        => await ledger.RecordAsync(@event.InvoiceId, @event.Amount, cancellationToken);
}
```

```csharp
services.AddZeroEvents();
```

Dispatch is generated at build time. Every subscriber's outcome comes back separately —
`PublishResult` says *which* one failed, which is the only thing a caller can act on, since
the fact has already happened and cannot be retried.

The compiler traces publish cycles (**ZERO500**) and reports an event that can be changed
after publishing (**ZERO501**).

Part of [Zero](https://iqone.solutions/zero) by IQOne.
