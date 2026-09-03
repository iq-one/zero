# Changelog

Zero follows semantic versioning from this first published package. Until 1.0 the API will
change; if you adopt it now, pin the version.

## 0.4.1

### Fixed

- **ZERO450 reported the very pattern 0.4.0 recommended.** The rule asks whether a request
  says who may make it, and it reads the answer from the attribute's arguments — `Policy`,
  `Roles`, `AllowAnonymous` — because that is what a reader sees at the request. An attribute
  that DERIVES the policy in its constructor, which 0.4.0 made possible and the changelog
  offered as the example, writes nothing the analyzer can read. So the recommended code did
  not build.

  An attribute now says once, on itself, that it decides:

  ```csharp
  [DeclaresAuthorization]
  public sealed class ServiceRouteAttribute : PostAttribute
  {
      public ServiceRouteAttribute(string pattern) : base(pattern)
          => Policy = pattern.TrimStart('/');
  }
  ```

  On the attribute type rather than inferred from its constructor: inference would work in
  the assembly that declares the attribute and fail for one referenced as metadata, so the
  rule would depend on where the attribute lives. The marker suppresses nothing else — the
  attribute still supplies the policy at runtime, and one that carries the marker while
  deciding nothing leaves its requests requiring only an authenticated caller.

### Added

- **A test project for the authorization analyzer.** It had none, which is why the above
  shipped: the rule's whole value is in what it refuses, and nothing ran it. Seven tests now
  pin both directions, including that a route with no policy still says nothing.

## 0.4.0

### Added

- **A route attribute of your own is now recognised.** The five method attributes — `Get`,
  `Post`, `Put`, `Patch`, `Delete` — are no longer sealed, and recognition walks the base
  chain, so an application whose routes share a shape says it once:

  ```csharp
  public sealed class ServiceRouteAttribute : PostAttribute
  {
      public ServiceRouteAttribute(string pattern) : base(pattern)
          => Policy = pattern.TrimStart('/');
  }

  [ServiceRoute("/shared/lookups/countries")]
  public sealed record GetCountries : IQuery<CountryModel[]>;
  ```

  One string, written once, with the policy derived from it. Before this the same endpoint
  needed the path twice — as the pattern and as the policy — or a constant per endpoint plus
  a list to register from.

  What the generator can and cannot see is worth stating, because it is what shapes the
  rule. The **pattern** is read at compile time from the first positional argument, so a
  derived attribute has to forward it rather than compute it. Anything the attribute sets on
  **itself** — `Policy`, `Roles`, `Tag`, `AllowAnonymous` — is read from the live instance at
  runtime, so a constructor may compute it freely.

### Changed

- **ZERO303 now reports what it always meant.** It was "a derived route attribute produces
  no endpoint", which was true only because recognition matched the exact type name. It is
  now "a route attribute names no method", which is the case that genuinely cannot work: a
  method passed to `RouteAttribute`'s own constructor is invisible to the generator, because
  the generator sees attribute arguments and not the constructor body that forwards them.
  The message says which of the five to derive from instead.

## 0.3.1

### Fixed

- **An attribute with an array-valued named argument crashed the registration generator.**
  `TypedConstant.Value` throws on an array rather than returning null, and that one
  unhandled kind took down generation for the whole assembly — after which the compiler
  reported the partial method the generated file was going to implement. So the error named
  a file the author never wrote, about a member they never removed, and said nothing about
  the attribute that caused it.

  Constructor arrays never reached the failing path: the caller flattens them, which is what
  `[ServiceTypes(typeof(A), typeof(B))]` needs. A NAMED array argument is not flattened, and
  one anywhere in the assembly was enough — 0.3.0's own
  `[Projection(Ignore = [nameof(Model.Price)])]` found it on first use.

## 0.3.0

### Added

- **`[Projection]` writes a specification's `Selector`.** A specification that reshapes rows
  declares `Expression<Func<TSource, TResult>> Selector`, and when the mapping is member for
  member by name, writing it out is work with a sharp edge: a member the result has and the
  entity does not is a silently absent field in the response, and the symptom is a column
  missing from a screen with nothing in the code to explain it. The attribute takes no type
  arguments — the class already names them in its base — and the generated selector is an
  expression tree, so the provider translates it and only the result's columns are read.
  That is the difference from mapping after materialisation, where the whole row and every
  navigation loaded alongside it are fetched and most of it discarded.

  It is **all or nothing**. A member that cannot be mapped is ZERO220 naming the member, and
  nothing is generated: three quarters generated and one quarter absent is the failure the
  generator exists to prevent. Refused on purpose, because each is a decision rather than a
  conversion: no source of that name; a nullable source into a non-nullable member (the
  fallback belongs where a reader can see it); a narrowing conversion (a cast compiles and
  silently wraps); a nested model or a collection (whether to load the navigation, under
  which condition, and which members, is the endpoint's call). Allowed without asking:
  identical types, an implicit widening, a value type into its nullable form, and an enum
  with the number it is stored as.

  A member that legitimately has no source is declared — `[Projection(Ignore = [...])]` —
  and the list is checked: an entry naming something the result does not have is ZERO221,
  because a stale entry silences nothing while reading as though a real hole were accounted
  for. ZERO222 through ZERO224 cover the attribute on a non-specification, a class that is
  not `partial`, and a selector written by hand alongside the attribute.

## 0.2.1

Two more defects found the same way 0.2.0's were: by porting a hospital information system
onto Zero. Both produced a compiler error inside GENERATED code — the worst place for one,
because nothing in the author's own file is wrong.

### Fixed

- **A response type that can be null lost its annotation, and the generated registration
  did not compile.** A handler whose response is legitimately nullable — a lookup that
  answers `null` when the thing it looks up does not exist, which is not the same as an
  empty list on the wire — declares
  `IQueryHandler<TQuery, IReadOnlyList<Model>?>`. The registration generator rendered every
  type name with `SymbolDisplayFormat.FullyQualifiedFormat`, and that format drops the `?`.
  The emitted registration therefore named a *different* closed interface than the class
  implements, and `Module.g.cs` failed with CS8631 pointing at a generic constraint. The
  annotation is now kept in generic argument lists and dropped in `typeof`, where
  `typeof(T?)` would be CS8639 instead. Both renderings are carried explicitly: a nullable
  *value* type prints as `int?` either way, so one cannot be derived from the other by
  removing `?`.
- **`EfUnitOfWork` was sealed, so the documented escape hatch was half a hatch.** When
  `AddZeroEntityFramework` refuses a second context it tells you to name your repositories
  per context — `OrderRepository(OrderContext, ...) : EfRepository<Order>(...)` — which
  works because `EfRepository` is derivable. The unit of work is registered by the same
  call and needs the same treatment, but it could not be derived. It is now a `class`, and
  the refusal message says so. The message also says what it did not before: a
  context-per-module application cannot use `AddZeroTransactions`, because one open-generic
  pipeline behaviour cannot pick a context; it opens the boundary in the handler.
- **`Specification.Page` would not accept a null offset.** `take` was widened to `int?` in
  0.2.0 and `skip` was left behind. A specification that wants a limit and no offset had to
  pass zero, and zero is not nothing: `Skip = 0` still emits an offset, SQL Server requires
  an `ORDER BY` for one, so the provider invents `ORDER BY (SELECT 1)` and a query that
  should have been `SELECT TOP(n)` becomes `OFFSET 0 ROWS FETCH NEXT n`. `Page(int? skip,
  int? take)` — existing callers passing an `int` still compile.

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
