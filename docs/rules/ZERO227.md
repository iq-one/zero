# ZERO227 — A mapping method has the wrong shape

**Severity:** error · **Category:** Zero.Persistence

The signature is where the generator reads the two types, so its shape is the declaration:
`static partial void`, exactly two parameters, source first, target second.

```csharp
[Mapping]
private static partial int Apply(BedModel model, Bed bed);    // ZERO227: returns int

[Mapping]
private static partial void Apply(BedModel model);            // ZERO227: one parameter

[Mapping]
private partial void Apply(BedModel model, Bed bed);          // ZERO227: not static
```

A mapping that returned something, or took one object, would be a different operation —
a projection produces a value, and this one writes onto what it is given.

## Fix

```csharp
[Mapping]
private static partial void Apply(BedModel model, Bed bed);
```

Any accessibility works; the generated half repeats whichever you wrote.
