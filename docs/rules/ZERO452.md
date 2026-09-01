# ZERO452 — An authorization attribute is on something that is not a request

**Severity:** error · **Category:** Zero.Authorization

A type carries `[Authorize]` or `[AllowAnonymous]` but does not implement `ICommand`,
`ICommand<T>` or `IQuery<T>`.

```csharp
[Authorize("invoices.close")]
public sealed record InvoiceModel(int Id, decimal Amount);      // ZERO452 — not a request
```

Authorization is applied by a pipeline behaviour, and the pipeline only ever sees requests.
On anything else the attribute compiles, is never read, and protects nothing — while looking
in the source exactly like protection.

## Fix

```csharp
[Authorize("invoices.close")]
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>;

public sealed record InvoiceModel(int Id, decimal Amount);
```

Put the attribute on the request. If the type is a response model, a DTO or a service,
remove it: what may be done with a value is decided when the request that produces it is
authorized.

For a rule about one particular value — "may this caller see *this* invoice" — use
`IResourceAuthorizer` from the handler. See the package's rule file for why that question
cannot be answered from the pipeline.
