# ZERO009 — Captive dependency

**Severity:** error · **Category:** Zero.Registration

A singleton takes a dependency with a shorter lifetime. The container resolves that
dependency once, when the singleton is first constructed, and the singleton then holds that
one instance for the life of the process.

```csharp
public sealed class ReportCache(IInvoiceStore store) : ISingleton;
//                              ^ IScoped
```

Every request after the first reads state belonging to whichever request arrived first. In
a web application that usually means a database context outliving its request — connections
that are never returned, a change tracker that grows without bound, and occasionally one
user's data appearing in another user's response.

None of these symptoms points at the constructor that caused them, which is why this is an
error rather than a warning.

## Fix

Take `IServiceScopeFactory` and open a scope where the work happens:

```csharp
public sealed class ReportCache(IServiceScopeFactory scopes) : ISingleton
{
    public async Task<Report> BuildAsync(int id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();

        return await store.GetAsync(id, cancellationToken);
    }
}
```

Or change the lifetime. Something that needs per-request state is rarely a singleton — and
if the singleton exists only to cache, cache the *data* rather than the service that reads it.

## Lifetimes, longest first

`ISingleton` > `IThread` > `IScoped` > `ITransient`

A service may depend on its own lifetime or a longer one, never a shorter one.
