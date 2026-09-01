# ZERO211 — A cacheable query's key ignores what it was asked

**Severity:** warning · **Category:** Zero.Caching

A query takes parameters, but its `CacheKey` is the same string every time.

```csharp
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>, ICacheable
{
    public string CacheKey => "invoice";      // ZERO211 — every id shares one answer
}
```

The first caller's answer is stored under `invoice`, and every caller after it is handed that
answer whatever they asked for. Nothing fails, nothing is logged, and the answer is
well-formed — it is simply to a question nobody asked. Reports of this reach you as "the
invoice screen sometimes shows the wrong invoice", weeks later.

## Fix

Build the key from the values the answer depends on:

```csharp
public string CacheKey => $"invoice:{Id}";
```

All of them. A parameter left out is a parameter the cache pretends does not exist:

```csharp
public sealed record GetInvoices(int CustomerId, bool IncludeDrafts) : IQuery<...>, ICacheable
{
    public string CacheKey => $"invoice:customer:{CustomerId}:drafts:{IncludeDrafts}";
}
```

Write keys as a path, most general part first, so a command can drop a branch of them with
`ICacheInvalidator.InvalidateByPrefixAsync`.

## What is not reported

A query with nothing to vary on may have a constant key — there is nothing it could have left
out:

```csharp
public sealed record GetCurrencies : IQuery<IReadOnlyList<Currency>>, ICacheable
{
    public string CacheKey => "currency:all";
}
```

A key that varies is not reported either, whether it branches or interpolates:

```csharp
public string CacheKey => Drafts ? "invoices:drafts" : "invoices";
```

The rule asks only whether the key could possibly differ between two calls. Where it can, the
author has thought about it, and what they decided is theirs.
