---
id: zero.caching.cache-what-the-query-declares
title: Cache only what a query asks to have cached
package: IQOne.Zero.Caching
applies-to: ["**/*.cs"]
enforced-by: [ZERO210, ZERO211]
---

A query says it may be cached by implementing `ICacheable`, and states the key its answer is
stored under. Nothing else is cached, and no key is guessed.

Caching is a pipeline behaviour, so a handler never knows it is being cached and never has to
remember to be. `AddZeroCaching()` is the whole setup.

## Do

```csharp
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>, ICacheable
{
    public string CacheKey => $"invoice:{Id}";
}
```

```csharp
services.AddZeroCaching();
```

The handler is unchanged. The first call runs it; later calls are answered from the store
until the entry expires or a command drops it.

## Don't

Do not cache inside a handler:

```csharp
public async Task<Result<InvoiceModel>> HandleAsync(GetInvoice query, CancellationToken ct)
{
    if (_cache.TryGetValue(key, out var hit)) return hit;    // belongs in the pipeline
    ...
}
```

Do not write your own `IMemoryCache` wrapper, key builder or invalidation helper. That is
this package.

## The key is written out, not derived

The key is a string you write, and it must carry everything the answer depends on.

Zero will not build a key by serialising the request, and neither should you. A key derived
from a shape nobody declared collides the moment two requests serialise the same, and changes
wholesale the moment someone adds a field — and both of those are silent. A cache is the one
place where being wrong looks exactly like being fast.

A constant key on a query that takes parameters is ZERO211:

```csharp
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>, ICacheable
{
    public string CacheKey => "invoice";       // ZERO211 — every id shares one answer
}
```

Write keys as a path, most general part first — `invoice:42`, `invoice:42:lines` — so a
command can drop a whole branch of them at once.

## Only a query

`ICacheable` on a command is ZERO210. A command changes something, so an answer served from a
cache is a change that never happened. The behaviour throws if one reaches it at run time;
the analyzer reports it before that.

## How long

`Lifetime` is optional and belongs to the query, because only the query knows how stale its
answer may be:

```csharp
public sealed record GetCurrencies : IQuery<IReadOnlyList<Currency>>, ICacheable
{
    public string CacheKey => "currency:all";
    public TimeSpan? Lifetime => TimeSpan.FromHours(1);
}
```

Leave it null to take `CachingOptions.DefaultLifetime`, which is five minutes.

## Failures are never stored

Only a successful `Result` is written. A failure is usually about the moment rather than the
question — a dependency that timed out, a row someone else has locked — and storing one would
keep answering with it long after the cause had gone.

## Invalidating: what a command does afterwards

Nothing invalidates automatically. The cache does not know which command touches which query,
and a guess would be wrong quietly. A command that changes data drops the keys it made wrong:

```csharp
public sealed class CloseInvoiceHandler(IInvoiceStore store, ICacheInvalidator cache)
    : ICommandHandler<CloseInvoice>
{
    public async Task<Result<Unit>> HandleAsync(CloseInvoice command, CancellationToken ct)
    {
        var closed = await store.CloseAsync(command.Id, ct);

        if (closed.IsFailure) return closed;

        await cache.InvalidateByPrefixAsync($"invoice:{command.Id}", ct);

        return Unit.Success;
    }
}
```

Use `InvalidateAsync` for one key and `InvalidateByPrefixAsync` for a branch. Pass the key
exactly as the query wrote it; the configured prefix is applied for you, so the two sides
agree without either of them knowing what it is.

Invalidate after the change has succeeded, not before. Dropping a key first leaves a window in
which a concurrent read stores the old answer again.

## Turning it off

`CachingOptions.Enabled` switches the whole thing off in one place. Use it in tests: a test
that passes alone and fails in a suite because the one before it left an answer behind is a
test nobody trusts.

```csharp
services.AddZeroCaching(options => options.Enabled = false);
```

A cacheable command is still refused while caching is off. Switching it off must not hide a
mistake that would still be a mistake in production.

## Somewhere other than memory

`AddZeroCaching()` registers an in-process store, which is what a single instance needs.
Register your own `ICache` before that call to share a cache between instances — nothing else
changes, and no query is edited.
