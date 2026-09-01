# The capability contract

Every Zero package above the kernel is a **capability**: one thing a line-of-business
application would otherwise build for itself. This document is the contract each one keeps.
It exists so that adding the fourteenth capability is as mechanical as adding the second,
and so that a coding agent can predict the shape of a package it has never seen.

## Why the contract matters more than any single capability

The framework's purpose is to stop work being redone. That applies to the framework's own
authors too: a catalog where every package is shaped differently is a catalog nobody can
learn once. It also applies to agents, which generalise from what they have seen — a
predictable shape means a correct guess.

## The kernel

Four packages that everything else assumes and nothing else may be built without:

| Package | Holds |
| --- | --- |
| `IQOne.Zero.Abstractions` | Lifetime markers, role interfaces, module and application contracts |
| `IQOne.Zero.Core` | Application lifecycle, module graph |
| `IQOne.Zero.Configuration` | Options bound and validated at startup |
| `IQOne.Zero.Regify` | Registration generation, analyzers |

The kernel takes no dependency on any capability. A capability that the kernel needs is not
a capability — it belongs in the kernel.

## What every capability ships

### 1. One entry point

A single extension method, named for the capability, on `IServiceCollection`:

```csharp
services.AddZeroCaching();
```

No second call is required to make it work. Where a capability needs configuration, the
entry point takes an optional delegate, and the defaults are the ones most applications
would choose:

```csharp
services.AddZeroCaching(options => options.DefaultLifetime = TimeSpan.FromMinutes(5));
```

A capability never requires the consumer to register its internals. If a type has to be
registered by hand for the capability to function, the capability is unfinished.

### 2. Abstractions the application implements, not classes it inherits

The consumer's code depends on interfaces from the capability, and the capability's own
implementations stay internal wherever possible. Inheritance is offered only where it
genuinely reduces work, and never as the only route.

### 3. Generated wiring

Whatever can be resolved at build time is. A capability that needs its implementations
discovered contributes to the generator rather than scanning at startup.

### 4. Analyzers for the boundaries that matter

A capability reports, as compiler diagnostics, the misuses that have no reliable runtime
symptom. Diagnostics get an id in the capability's reserved range, a message that states
the fix, and a page under `docs/rules/`.

Not every misuse deserves an analyzer. The test is whether the mistake is *silent*: if it
throws immediately with a clear message, the exception is already the diagnostic.

### 5. Rule files, in the package

`src/<Package>/rules/*.md`, packed to `zero/rules/<Package>/`. These teach an agent how to
use the capability correctly. Each carries frontmatter linking it to the diagnostics that
enforce it.

### 6. A capability manifest

`src/<Package>/zero/capability.json`, packed to `zero/capability.json`. This is what makes
the catalog machine-readable: what the capability is for, its entry point, the types a
consumer touches, and one canonical example.

```json
{
  "id": "caching",
  "title": "Caching",
  "summary": "Read-through caching with keys derived from the request.",
  "useWhen": "A read is expensive and slightly stale data is acceptable.",
  "package": "IQOne.Zero.Caching",
  "entryPoint": "services.AddZeroCaching()",
  "keyTypes": ["ICachePolicy<T>", "ICache"],
  "diagnostics": ["RGF210", "RGF211"],
  "example": "..."
}
```

### 7. Tests, including one that proves the entry point alone is enough

Every capability has a test that calls only its `Add` method, builds the provider with
validation on, and resolves the capability's public types. That test is what makes
"install the package and write one line" true rather than aspirational.

## Diagnostic id ranges

| Range | Owner |
| --- | --- |
| RGF001–RGF099 | Kernel: registration, modules, configuration |
| RGF100–RGF199 | Results, validation |
| RGF200–RGF299 | Persistence, caching |
| RGF300–RGF399 | Messaging, web |
| RGF400–RGF499 | Observability, resilience, background work |

Ids are never reused. A retired diagnostic keeps its number and its page.

## What a capability may depend on

The kernel, and capabilities below it in this order:

```
kernel
  └── results
        └── validation
              └── persistence ── messaging
                                    └── web
```

Anything outside that chain is a sign the boundary is wrong. A capability that needs a
sibling usually wants an abstraction that belongs one level down.

## What no capability may do

- Reference a specific database, transport, serializer or logging sink from a package that
  is not named for it. `IQOne.Zero.Persistence` knows no ORM; `IQOne.Zero.Persistence.EntityFramework` does.
- Define an application's wire contract. Envelope shape, status code mapping and property
  naming belong to whoever has to keep them stable for their callers.
- Require a base class in the consumer's domain model.
- Read configuration outside a validated options type.
- Start a thread, a timer or a connection during `Add`. Nothing may run before the
  application's own lifecycle says so.
