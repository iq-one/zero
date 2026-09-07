# ZERO254 — A composition goes round in a circle

**Severity:** error · **Category:** Zero.Mapping

A map is written out **as source**, so a circle has no output at all: the file would have to
contain itself.

```csharp
public sealed partial class BedMap : Map<Bed, BedModel>
{
    protected override void Configure(IMapBuilder<Bed, BedModel> map)
        => map.Member(m => m.Department, e => e.Department.To<DepartmentModel>());
}

public sealed partial class DepartmentMap : Map<Department, DepartmentModel>
{
    protected override void Configure(IMapBuilder<Department, DepartmentModel> map)
        => map.Member(m => m.Beds, e => e.Beds.To<List<BedModel>>());     // ZERO254
}
```

The message names the loop: `Bed → BedModel → Department → DepartmentModel → Bed`.

## Why this is worth a rule of its own

The shapes that make a circle are entirely ordinary — a bed that names its department, a
department that lists its beds — and nobody writing either one is thinking about the other.
What happens next depends on the mapper:

- **A runtime mapper** meets it as a stack overflow, or stops at a depth limit and silently
  returns a truncated object. `MaxDepth` in the mapper this design replaced is an `int?`
  compared with `>=`, so when it is unset the guard never fires: the default is no limit.
- **Here** it is a build error that names the loop, and breaking it is a decision written down.

## Fix

Break the loop at the end that does not need to travel. Usually one direction is the one
callers actually read:

```csharp
public sealed partial class DepartmentMap : Map<Department, DepartmentModel>
{
    // A bed carries its department; a department does not carry its beds on this path.
    protected override void Configure(IMapBuilder<Department, DepartmentModel> map)
        => map.Ignore(m => m.Beds);
}
```

If both directions really are needed, they are two maps rather than one that recurses: declare
a second destination shape that stops, and compose that one from the side that would loop.
