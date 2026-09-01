---
id: zero.di.lifetime-by-abstraction
title: Let the abstraction carry the lifetime
package: IQOne.Zero.Abstractions
applies-to: ["**/*.cs"]
enforced-by: [RGF006, RGF007, RGF008]
---

A service's lifetime is declared by the interface it implements, not at a registration call
site. Registration is then generated at compile time and the container performs no assembly
scanning at startup.

## Do

Implement the marker that matches the role:

```csharp
public interface IInvoiceStore : IScoped;

public sealed class InvoiceStore(IClock clock) : IInvoiceStore;
```

`IScoped`, `ISingleton`, `ITransient` and `IThread` live in
`IQOne.Zero.DependencyInjection.Descriptors`. The role interfaces in `IQOne.Zero.Fundamentals`
already carry one: `IStep` is a singleton, `IProvider` and `IBuilder` are transient.

## Don't

Do not write a registration call for a type you own — it is generated already:

```csharp
services.AddScoped<IInvoiceStore, InvoiceStore>();
```

Do not restate a lifetime the abstraction has given:

```csharp
[Scoped]                                     // redundant
public sealed class InvoiceStore : IInvoiceStore;
```

Do not implement two lifetime markers on one type. That is RGF006, and there is no sensible
way for the generator to pick.

## When the abstraction cannot express it

Apply `[Singleton]`, `[Scoped]` or `[Transient]` directly. This is the exception — reach for
it only when a type's lifetime genuinely differs from what its role implies.

## Naming

Registration defaults to the interface whose name matches the class: `InvoiceStore` resolves
through `IInvoiceStore`. When that interface does not exist the service type cannot be
inferred (RGF007) — either add it, or state the types with
`[ServiceTypes(typeof(ISomething))]`.

## Registering a third-party type

Types you do not own carry no Zero marker. Register them explicitly in the module's
`OnConfigureServices` — that is the correct place for `services.AddX(...)`.
