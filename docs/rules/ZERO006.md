# ZERO006 — More than one lifetime declared

**Severity:** error · **Category:** Zero.Registration

A type implements more than one lifetime marker, so there is no single answer to how it
should be registered.

```csharp
public sealed class ReportBuilder : IScoped, ISingleton;   // ZERO006
```

Zero does not pick one. Picking would hide the contradiction, and the wrong choice produces
either a captive dependency or an object rebuilt far more often than intended — neither of
which announces itself at runtime.

## Fix

Keep the marker that matches how the type is actually used and remove the rest.

If two different lifetimes are genuinely wanted, that is two types. A common case: a
singleton cache in front of a scoped worker. Split them, and have the singleton open a scope
for the worker (see [ZERO009](ZERO009)).

## Note

The role interfaces in `IQOne.Zero.Fundamentals` already carry a lifetime — `IStep` is a
singleton, `IProvider` and `IBuilder` are transient. Implementing `IStep` and `IScoped`
together triggers this rule.
