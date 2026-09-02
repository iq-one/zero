<img src="assets/logo.png" alt="Zero" width="96" />

# Zero

An application framework for .NET 10 that resolves registration, dispatch, routing and
mapping while the compiler is still running.

Registration tables, dispatch rows and endpoint maps are generated source you can open and
read. Startup does no assembly scanning, and a wiring mistake is a compiler error rather
than a stack trace someone reads on a Sunday.

```bash
dotnet add package IQOne.Zero
```

Everything a line-of-business application would otherwise build for itself — commands and
queries, validation, data access, events, caching, authorization, telemetry, scheduled work
— is here, one `Add` call each. Install what you need; the metapackage carries only what
every application has.

## Packages

**The set every application needs.** `IQOne.Zero` brings all of these:

| Package | Holds |
| --- | --- |
| `IQOne.Zero.Abstractions` | Lifetime markers, role interfaces, module contracts |
| `IQOne.Zero.Core` | Application lifecycle, module graph |
| `IQOne.Zero.Configuration` | Options bound and validated at startup |
| `IQOne.Zero.Generators` | Source generators and analyzers |
| `IQOne.Zero.Results` | An outcome type for operations that are expected to fail |
| `IQOne.Zero.Messaging` | Commands, queries and the pipeline around them |
| `IQOne.Zero.Validation` | Request validation as a pipeline behaviour |

**Opt-in, because a console worker should not drag in ASP.NET and a service that stores
nothing should not carry a data layer:**

| Package | Use it for |
| --- | --- |
| `IQOne.Zero.Web` | HTTP endpoints from your requests |
| `IQOne.Zero.Persistence` | Specifications, repositories, an explicit transaction boundary |
| `IQOne.Zero.Persistence.EntityFramework` | The Entity Framework provider. Names no database |
| `IQOne.Zero.Events` | Domain events, in process and awaited |
| `IQOne.Zero.Authorization` | Who may make a request. Transport-independent |
| `IQOne.Zero.Caching` | Read-through caching for queries |
| `IQOne.Zero.Observability` | Logging, tracing and metrics for every request |
| `IQOne.Zero.BackgroundWork` | Recurring work, one scope per run |
| `IQOne.Zero.Resilience` | Retrying what is worth retrying |
| `IQOne.Zero.Testing` | Testing an application built on Zero |
| `IQOne.Zero.Tool` | The `zero` command |

Generators and analyzers configure themselves. Nothing is added to your project file.

## What it does

**Lifetime is carried by the abstraction.** A type implementing `IScoped` is registered as
scoped — no attribute, no registration call. `IStep` is a singleton, `IProvider` is
transient; the role has already said it.

**Registrations are generated.** Duplicate registrations, ambiguous service types and
captive dependencies are reported as build errors, not discovered in production.

**Modules have no startup order.** They declare what they depend on and the host sorts them
topologically, deriving dependencies from the assembly reference graph so they cannot drift
from what the projects actually reference. A cycle is reported by name.

**Failures are values.** An operation that can fail returns `Result<T>`, so the failure is in
the signature. Discarding one, or reading its value unchecked, is a build error.

**A use case is a request and one handler.** Dispatch is a generated table, so sending costs
a dictionary read rather than reflection — and a request nobody handles stops startup.

**An endpoint is a route attribute on a request.** One real ASP.NET endpoint each, generated
at build time. The handler never mentions HTTP: a `NotFound` error becomes a 404, a
`Conflict` becomes a 409.

**Cross-cutting work is a pipeline behaviour.** Validation, authorization, caching,
transactions, retries and telemetry wrap every request once, in a stated order, instead of
appearing in each handler and being forgotten in the next.

**Configuration is validated before traffic arrives.** A missing or malformed setting stops
the application with a message naming it.

## Rules for AI agents

Zero encodes its rules twice, from one source: as analyzers the compiler enforces, and as
rule files an agent can read. Both travel **inside the packages**, so they are always the
same version as the code they describe.

```bash
dotnet tool install --global IQOne.Zero.Tool
zero rules init
```

This writes, from every Zero package the project references:

| | |
| --- | --- |
| `AGENTS.md` | what the project has, what Zero also offers, and an index of the rules |
| `.zero/rules/*.md` | the full text of each rule |
| `.cursor/rules/*.mdc` | the same rules, scoped to the files they apply to |
| `CLAUDE.md` | an import of `AGENTS.md` |

`AGENTS.md` holds the catalogue and an index rather than every rule in full: it is loaded at
the start of every agent session, and an agent cannot look up a capability it does not know
exists, but it can read a rule when it reaches that area. Commit all of it.

Re-run after upgrading. `zero rules check` exits non-zero when the committed files no longer
match the restored packages, which is the CI gate: an upgrade nobody re-ran leaves an agent
reading last release's rules, and the file looks current because somebody committed it on
purpose.

## The sample

[`samples/Zero.Sample.Orders`](samples/Zero.Sample.Orders) is a small ordering API over
SQLite that uses every package, split into five modules. Its host is eleven `Add` calls and
a `MapZeroEndpoints`; nothing registers a handler, validator, endpoint, subscriber, policy,
job or convention.

```bash
dotnet run --project samples/Zero.Sample.Orders/Host
```

## Status

Early. The API will change before 1.0; releases follow semantic versioning from the first
published package. **If you adopt Zero now, pin the version.**

[CHANGELOG.md](CHANGELOG.md) lists what this version cannot do yet — there is no outbox, no
distributed coordination for background work, and no project template.

Zero is built alongside the system it was designed for: the rewrite of a hospital
information system with hundreds of modules and roughly two thousand service methods,
running against a database two other applications write to at the same time. The constraints
in it are lived ones rather than anticipated ones.

Built and maintained by [IQOne](https://iqone.solutions).
