---
id: zero.messaging.one-handler-per-request
title: One handler per request, and reach it only through ISender
package: IQOne.Zero.Messaging
applies-to: ["**/*.cs"]
---

A use case is a request plus exactly one handler. Callers send the request; they never
resolve or call a handler themselves.

Going straight to a handler skips the pipeline, and the pipeline is where validation and
authorization live. A call that bypasses them looks identical to one that does not, which
is why this is a rule rather than a preference.

## Do

```csharp
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>;

public sealed class GetInvoiceHandler(IInvoiceStore store) : IQueryHandler<GetInvoice, InvoiceModel>
{
    public async Task<Result<InvoiceModel>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
    {
        var invoice = await store.FindAsync(query.Id, cancellationToken);

        return invoice is null
            ? Error.NotFound("invoice.missing", $"No invoice with id {query.Id}.")
            : invoice.ToModel();
    }
}
```

```csharp
var result = await sender.SendAsync(new GetInvoice(id), cancellationToken);
```

## Don't

```csharp
// Skips validation, authorization, caching and logging.
var result = await handler.HandleAsync(new GetInvoice(id), cancellationToken);
```

```csharp
// Two handlers for one request. Startup refuses this.
public sealed class AlsoGetInvoiceHandler : IQueryHandler<GetInvoice, InvoiceModel>;
```

## Command or query

`ICommand` changes something; `IQuery<T>` does not. The distinction is read by the
pipeline, not by the compiler: a query may be cached and retried, a command may open a
transaction. Marking a command as a query is how a write ends up cached.

## Every request needs a handler

Startup fails when a request has no handler, naming it. The check costs nothing at
runtime — the generator recorded every request it compiled, so no assembly is scanned to
find out.

## Cross-cutting work is a behaviour

Logging, authorization, validation, caching, transactions, retries: all of these wrap the
pipeline once instead of appearing in every handler. Use `BehaviorOrder` for position, and
leave gaps so an application can slot its own between two of the framework's.

A handler that logs, checks permissions or opens a transaction is doing work that would
then have to be repeated in the next handler — and forgotten in the one after that.
