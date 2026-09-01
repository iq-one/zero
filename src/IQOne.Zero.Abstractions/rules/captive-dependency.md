---
id: zero.di.captive-dependency
title: Never take a shorter-lived dependency
package: IQOne.Zero.Abstractions
applies-to: ["**/*.cs"]
enforced-by: [RGF009]
severity: error
---

A singleton that takes a scoped dependency captures the first instance it is given and
holds it forever. Every later request then reads state belonging to the request that
happened to arrive first. Zero reports this as a build error (RGF009).

The lifetimes, longest first: `ISingleton` > `IThread` > `IScoped` > `ITransient`.
A service may depend on its own lifetime or longer, never shorter.

## Don't

```csharp
public sealed class ReportCache(IPatientRepository repository) : ISingleton;
//                              ^ IScoped — frozen on first resolution
```

## Do

Take a factory for the shorter-lived dependency and open a scope per use:

```csharp
public sealed class ReportCache(IServiceScopeFactory scopes) : ISingleton
{
    public async Task<Report> BuildAsync(int id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPatientRepository>();
        ...
    }
}
```

Or reconsider the lifetime: a cache that needs per-request data is usually not a singleton.
