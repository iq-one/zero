# IQOne.Zero.Caching

Read-through caching as a pipeline behaviour, for queries that ask for it.

```csharp
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>, ICacheable
{
    public string CacheKey => $"invoice:{Id}";
}
```

```csharp
services.AddZeroCaching();
```

The handler is unchanged and never learns it is being cached. Nothing is cached until a query
says it may be — no key is derived by serialising a request, because a key built from a shape
nobody declared collides silently and misses silently.

Only successes are stored. A failure is about the moment rather than the question, and keeping
one would answer with it long after the cause had gone.

A command drops what it made wrong, by the same key the query wrote:

```csharp
await invalidator.InvalidateByPrefixAsync($"invoice:{command.Id}", cancellationToken);
```

Answers are kept in this process by default. Register your own `ICache` before
`AddZeroCaching()` to share them between instances; no query changes.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
