# ZERO210 — A cacheable request is not a query

**Severity:** error · **Category:** Zero.Caching

A type implements `ICacheable` but does not implement `IQuery<T>`.

```csharp
public sealed record CloseInvoice(int Id) : ICommand, ICacheable   // ZERO210 — a command
{
    public string CacheKey => $"invoice:{Id}";
}
```

A command changes something. An answer served from a cache is a change that never happened —
the first call closes the invoice, and every call after it is told the invoice was closed
while nothing runs at all.

`CachingBehavior` throws when one reaches it, but only on the path that sends this particular
request, which in practice means on the day someone exercises it.

## Fix

If it reads, make it a query:

```csharp
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>, ICacheable
{
    public string CacheKey => $"invoice:{Id}";
}
```

If it changes something, remove `ICacheable`. What the command probably wanted is the other
side of caching — dropping the answers it has just made wrong:

```csharp
await invalidator.InvalidateByPrefixAsync($"invoice:{command.Id}", cancellationToken);
```

## What is reported

Classes, records and structs only. An interface that gathers `ICacheable` together with
something else is a shape a consumer is allowed to declare:

```csharp
public interface ICacheableQuery<TResponse> : IQuery<TResponse>, ICacheable;
```

Types implementing that are queries, and are not reported.
