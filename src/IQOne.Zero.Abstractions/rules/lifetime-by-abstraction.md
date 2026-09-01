---
id: zero.di.lifetime-by-abstraction
title: Let the abstraction carry the lifetime
package: IQOne.Zero.Abstractions
applies-to: ["**/*.cs"]
enforced-by: [RGF001, RGF009]
---

A service's lifetime is declared by the interface it implements, not at a registration
call site. Registration is then generated at compile time and the container performs no
assembly scanning at startup.

## Do

Implement the marker that matches the role:

```csharp
public interface IPatientRepository : IScoped { }

public sealed class PatientRepository(RadiologyDbContext context) : IPatientRepository;
```

`IScoped`, `ISingleton`, `ITransient` and `IThread` live in
`IQOne.Zero.DependencyInjection.Descriptors`. The role interfaces in
`IQOne.Zero.Fundamentals` already carry one: `IStep` is a singleton, `IProvider` is
transient, `IBuilder` is transient.

## Don't

Do not write a registration call for a type you own:

```csharp
services.AddScoped<IPatientRepository, PatientRepository>();   // generated already
```

Do not restate a lifetime the abstraction has given:

```csharp
[Scoped]                                    // redundant
public sealed class PatientRepository : IPatientRepository;
```

## When the abstraction cannot express it

Apply `[LifeStyle]` (or `[Singleton]`, `[Scoped]`, `[Transient]`) directly. This is the
exception, not the pattern — reach for it only when the type's lifetime genuinely differs
from what its role implies.

## Registering a third-party type

Types you do not own have no Zero marker, so register them explicitly in the module's
`OnConfigureServices`. That is the correct place for `services.AddX(...)` calls.
