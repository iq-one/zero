# ZERO300 — A route is declared on something that is not a request

**Severity:** error · **Category:** Zero.Web

A type carries `[Get]`, `[Post]` or another route attribute but does not implement
`ICommand`, `ICommand<T>` or `IQuery<T>`.

```csharp
[Get("/invoices/{id:int}")]
public sealed record GetInvoice(int Id);      // ZERO300 — not a request
```

An endpoint is served by sending its request through the pipeline. A type that is not a
request has nothing to send, so the attribute would compile and then do nothing — no route,
no error, no way to tell from the outside except that the URL answers 404.

## Fix

```csharp
[Get("/invoices/{id:int}")]
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>;
```

If the type is genuinely not a request — a DTO, a response model — remove the attribute.
