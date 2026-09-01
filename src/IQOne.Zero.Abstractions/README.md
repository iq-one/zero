# IQOne.Zero.Abstractions

Zero's fundamental abstractions. Every other Zero package references this one.

- **Lifetime markers** — `ISingleton`, `IScoped`, `ITransient`, `IThread`. A service's
  lifetime is carried by the abstraction it implements, so registration needs no attribute
  and no call.
- **Role interfaces** — `IStep`, `IProvider`, `IFactory`, `IBuilder`, `IAdapter`, each
  already carrying the lifetime its role implies.
- **Module contracts** — `IModule` and the lifecycle steps. Ordering is derived from
  declared dependencies, never from a number.
- **Application contracts** — `IApplication` and its startup steps.

Rules for AI agents ship inside this package under `zero/rules/`. Run `zero rules init`
to materialize them into your repository.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
