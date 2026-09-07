# ZERO257 — Two maps are declared for one pair

**Severity:** error · **Category:** Zero.Mapping

A pair has one map.

```csharp
public sealed partial class BedMap : Map<Bed, BedModel> { /* ... */ }

public sealed partial class OtherBedMap : Map<Bed, BedModel> { /* ... */ }   // ZERO257
```

## Why one

Everything that reaches for a map reaches **by pair**, never by name: a composition writing a
nested member, a specification taking its selector. With two declared, which one won would
depend on the order they happen to be read in — and the two would drift, which is the failure
a single declaration exists to prevent. It is the same failure one level up: two hand-written
selectors for one model, disagreeing about a field.

## Fix

Keep one. If the two really are different shapes, they are different pairs: give the second one
its own destination type, which also gives the reader a name for the difference.

```csharp
public sealed partial class BedMap : Map<Bed, BedModel>;

// A bed as the ward board shows it, which is not the same shape at all.
public sealed partial class BedBoardMap : Map<Bed, BedBoardModel>;
```

If the second exists to serve one query, that query wants its own model — or its own
hand-written selector, which `[NoGenerate]` on the specification leaves alone.
