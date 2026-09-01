# ZERO301 — Route pattern is empty

**Severity:** error · **Category:** Zero.Web

A route attribute was given no pattern.

```csharp
[Get("")]
public sealed record ListInvoices : IQuery<InvoiceModel[]>;   // ZERO301
```

An empty pattern maps the endpoint to the application root, which is almost never what was
meant and quietly takes over a URL something else may want.

## Fix

Give it a pattern:

```csharp
[Get("/invoices")]
public sealed record ListInvoices : IQuery<InvoiceModel[]>;
```

To serve the root deliberately, say so:

```csharp
[Get("/")]
public sealed record Home : IQuery<HomeModel>;
```
