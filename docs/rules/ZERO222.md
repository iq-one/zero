# ZERO222 — [Projection] is on something that is not a specification

**Severity:** error · **Category:** Zero.Persistence

The attribute takes no type arguments: it reads the source and the result from
`Specification<TSource, TResult>` in the base chain. On a type that names neither, there is
nothing to project.

```csharp
[Projection]
public sealed partial class InvoiceReport;    // ZERO222
```

## Fix

Derive from the specification whose selector you want written:

```csharp
[Projection]
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
```

A base of your own in between is fine — an application layer that applies paging or a
soft-delete rule to every query is still a projection of the same two types, and the base
chain is walked:

```csharp
public abstract class AppQuery<T, TResult> : Specification<T, TResult> where T : class;

[Projection]
public sealed partial class InvoiceQuery : AppQuery<Invoice, InvoiceModel>;
```

A specification that does not reshape rows — `Specification<T>` with one type argument —
has no selector, so it needs no attribute.
