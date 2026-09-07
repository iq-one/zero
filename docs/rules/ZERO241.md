# ZERO241 — A declared member cannot fill its target

**Severity:** error · **Category:** Zero.Mapping

The expression in `map.Member` is **copied into the tree the generator writes**, so it has to
fit the member it fills.

```csharp
// model: string Code — expression produces int
map.Member(m => m.Code, e => e.BuildingUnitId);     // ZERO241
```

Reported at the declaration rather than left to the compiler, because a mismatch inside a
generated file names a file nobody wrote. Here the error is on the line you typed.

## Fix

Make the expression produce the member's type:

```csharp
map.Member(m => m.Code, e => e.BuildingUnitId.ToString());
```

If the conversion is one the map should not be making — a lookup, a format that belongs to a
presentation layer — the member may be the wrong thing to fill here at all.
