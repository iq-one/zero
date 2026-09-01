---
id: zero.di.captive-dependency
title: Never take a shorter-lived dependency
package: IQOne.Zero.Abstractions
applies-to: ["**/*.cs"]
enforced-by: [ZERO009]
severity: error
---

A singleton that takes a scoped dependency captures the first instance it is handed and
holds it for the life of the process. Every later request then reads state belonging to
whichever request happened to arrive first. Zero reports this as a build error (ZERO009).

The lifetimes, longest first: `ISingleton` > `IScoped` > `ITransient`.
A service may depend on its own lifetime or a longer one, never a shorter one.

## Don't

```csharp
public sealed class ReportCache(IInvoiceStore store) : ISingleton;
//                              ^ IScoped — frozen on first resolution
```

## Do

Take a scope factory and open a scope per use:

```csharp
public sealed class ReportCache(IServiceScopeFactory scopes) : ISingleton
{
    public async Task<Report> BuildAsync(int id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
        ...
    }
}
```

Or reconsider the lifetime: something that needs per-request state is usually not a singleton.
