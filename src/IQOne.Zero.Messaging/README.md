# IQOne.Zero.Messaging

Commands, queries and their handlers, with a pipeline for everything that cuts across them.

```csharp
public sealed record CloseInvoice(int Id) : ICommand;

public sealed class CloseInvoiceHandler(IInvoiceStore store) : ICommandHandler<CloseInvoice>
{
    public async Task<Result<Unit>> HandleAsync(CloseInvoice command, CancellationToken cancellationToken)
        => await store.FindAsync(command.Id, cancellationToken) is { } invoice
            ? invoice.Close()
            : Error.NotFound("invoice.missing", $"No invoice {command.Id}.");
}
```

```csharp
services.AddZeroMessaging();
```

Dispatch is a table generated at build time, so sending costs a dictionary read rather than
reflection over the request type — and a request with no handler stops startup, naming it,
instead of failing on the first call.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
