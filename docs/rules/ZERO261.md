# ZERO261 — The map produces nothing

**Severity:** error · **Category:** Zero.Mapping

The destination has no constructor parameter and no settable member, so a map to it carries
none of the source.

```csharp
public sealed partial class RowMap : Map<Row, int>;      // ZERO261
```

Left unreported this compiles to `new int()` — zero, for every row. The emptiest possible kind
of wrong answer, and silent.

## What was usually wanted

A **scalar** result is a projection to a value, not a map to a shape. Write the selector:

```csharp
public sealed class ServiceIds : Specification<Service, int>
{
    public override Expression<Func<Service, int>> Selector => e => e.Id;
}
```

An **interface** as the destination is the same mistake one level up — there is nothing to
construct. Name the type the caller receives.
