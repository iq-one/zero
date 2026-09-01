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

| Package | Contents |
| --- | --- |
| `IQOne.Zero` | Metapackage covering the common set |
| `IQOne.Zero.Abstractions` | Fundamentals, lifetime markers, module and application contracts |
| `IQOne.Zero.Core` | Application lifecycle, steps, module graph |
| `IQOne.Zero.Configuration` | Options bound and validated at startup |
| `IQOne.Zero.Generators` | Registration source generator and analyzers |
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

## Rules for AI agents

Zero encodes its rules twice, from one source: as analyzers the compiler enforces, and as
rule files an agent can read. The rule files travel **inside the packages**, so they are
always the same version as the code they describe.

```bash
dotnet tool install --global IQOne.Zero.Tool
zero rules init
```

This writes `AGENTS.md`, `CLAUDE.md` and editor rule files composed from every Zero package
the project references. Re-run it after a version upgrade.

## Status

Early. The API will change before 1.0; releases follow semantic versioning from the first
published package. If you adopt Zero now, pin the version.

Built and maintained by IQOne.
