# Changelog

Zero follows semantic versioning from this first published package. Until 1.0 the API will
change; if you adopt it now, pin the version.

## 0.2.0

Fixes found by putting Zero to real work: a hospital information system with 2.758 service
methods was rewritten on top of it. Everything below is something that release broke, or
something that was already broken and only visible from outside.

### Two guards that were not guarding

Both of these were green while covering less than they claimed, which is the worst state a
check can be in — it costs the same and buys nothing.

- **The guidance check could not see Entity Framework.** Every example naming a `DbContext`
  or a `ModelBuilder` was unverified: the types did not resolve, that reads as CS0246, and
  CS0246 is deliberately ignored so snippets may name illustrative domain types. So a
  `ConventionDbContext` example with its constructor arguments in the wrong order shipped in
  0.1.0 and the test stayed green. The dependency namespaces are now derived from the
  framework's own public API rather than listed by hand, and `GuidanceCheckerTests` fails if
  a type the guidance leans on resolves to zero assemblies or to two.
- **The API surface lock covered ten of sixteen assemblies.** `Persistence`, its Entity
  Framework provider, `Caching`, `Observability`, `Authorization` and `Testing` had no
  approved surface at all, so a breaking change to any of them passed review unnoticed. The
  list is now derived from the repository, and the six are locked.

### Fixed

- **`ConventionDbContext`'s documented constructor order was wrong** in both
  `capability.json` and the Entity Framework rule. It is
  `(options, modelConventions, filterConventions)`. The compiler catches the mistake, but
  the example is what an agent copies.
- **A second `AddZeroEntityFramework` call silently bound every repository to the first
  context.** `TryAdd` made the second call a no-op, and the symptom was a query against the
  wrong database rather than an error. It now refuses, and says to register that module's
  repositories against its own context instead. An application with a context per module is
  a real shape; this made it quietly incorrect.
- **`IEntityFilterConvention` is registered by the generator**, like `IModelConvention` and
  `ISaveChangesConvention` already were. It was the one convention an application had to
  register by hand, and forgetting showed up as a query with no tenant filter.

### Added

- **ZERO303** — a route attribute derived from `RouteAttribute` produces no endpoint.
  `RouteAttribute` is public and abstract, so deriving from it looks supported; the
  generator matches the five attributes this package declares because it cannot evaluate a
  derived constructor. Before this the endpoint was simply never mapped and nothing said so.
- **`Specification.Page(int skip, int? take)`** — `take` was required, so a specification
  could not express an offset with no limit and callers invented one.

## 0.1.0

First release. What it does and what each package holds is in the [README](README.md); this
lists what a reader of a future version needs to know about this one.

**The API will change before 1.0.** Nothing here is settled by having shipped. Breaking
changes will be listed under their version with what to write instead.

### Known limits

- **No outbox.** `IQOne.Zero.Events` is in-process: a subscriber's database writes are inside
  the caller's transaction, but anything that leaves the process — an email, an HTTP call —
  is not, and rolling back does not recall it. Write the intent inside the transaction and
  dispatch it from a background job until `IQOne.Zero.Outbox` exists.
- **Background work does not coordinate across replicas.** Three instances each run the
  nightly job. `BackgroundWorkOptions.Disabled` on all but one is the crude answer; a lease
  taken as the job's first act is the better one.
- **Captive-dependency and duplicate-registration detection is per-assembly.** A singleton in
  one module taking a scoped dependency from another is not reported at compile time. The
  container's scope validation still catches it at startup.
- **No project template.** There is no `dotnet new zero`; start from
  `samples/Zero.Sample.Orders`, which uses every package.
