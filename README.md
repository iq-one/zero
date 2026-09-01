# Zero

An application framework for .NET 10 that resolves dependency injection, module ordering
and configuration while the compiler is still running.

Registration tables and module declarations are generated source you can open and read.
Startup does no assembly scanning, and a wiring mistake is a compiler error rather than a
stack trace someone reads on a Sunday.

```bash
dotnet add package IQOne.Zero
```

Zero is deliberately small. It covers what every application needs and stops there: it has
no opinion about your transport, your data access or your wire format, and adds no
dependency that would give it one.

## Packages

| Package | Use it for |
| --- | --- |
| `IQOne.Zero` | Metapackage: the kernel, results and messaging |
| `IQOne.Zero.Abstractions` | Lifetime markers, role interfaces, module and application contracts |
| `IQOne.Zero.Core` | Application lifecycle, module graph |
| `IQOne.Zero.Configuration` | Options bound and validated at startup |
| `IQOne.Zero.Generators` | Source generator and analyzers |
| `IQOne.Zero.Results` | An outcome type for operations that are expected to fail |
| `IQOne.Zero.Messaging` | Commands, queries and the pipeline around them |
| `IQOne.Zero.Web` | HTTP endpoints, added deliberately so nothing else drags in ASP.NET |
| `IQOne.Zero.Tool` | The `zero` command line tool |

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

**Configuration is validated before traffic arrives.** A missing or malformed setting stops
the application with a message naming it.

**Failures are values.** An operation that can fail returns `Result<T>`, so the failure is in
the signature. Discarding one, or reading its value unchecked, is a build error.

**A use case is a request and one handler.** Dispatch is a generated table, so sending costs
a dictionary read rather than reflection — and a request nobody handles stops startup.

**An endpoint is a route attribute on a request.** One real ASP.NET endpoint each, generated
at build time. The handler never mentions HTTP: a `NotFound` error becomes 404.

## Rules for AI agents

Zero encodes its rules twice, from one source: as analyzers the compiler enforces, and as
rule files an agent can read. The rule files travel **inside the packages**, so they are
always the same version as the code they describe.

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

`AGENTS.md` is loaded at the start of every agent session, so it holds the catalog and an
index rather than every rule in full — an agent cannot look up a capability it does not know
exists, but it can read a rule when it reaches that area. Commit all of it.

Re-run after upgrading. `zero rules check` exits non-zero when the committed files no longer
match the restored packages, which is the CI gate: an upgrade nobody re-ran leaves an agent
reading last release's rules, and the file looks current because somebody committed it on
purpose.

## Status

Early. The API will change before 1.0; releases follow semantic versioning from the first
published package. If you adopt Zero now, pin the version.

Built and maintained by IQOne.
