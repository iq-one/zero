# ZERO229 — A produced member has no source

**Severity:** error · **Category:** Zero.Persistence

A `[Mapping]` that **produces** an object holds the result to account, as a projection does.
A member nobody fills is an absent field with nothing in the code to explain it.

```csharp
public sealed class BedModel
{
    public string? Name { get; set; }
    public string? Tags { get; set; }        // Bed has no Tags
}

[Mapping]
private static partial BedModel ToModel(Bed bed);      // ZERO229
```

## Which end is checked depends on the shape

One sentence covers both: **what you construct must be complete, what you consume must be
consumed.**

| shape | held to account | reported as |
| --- | --- | --- |
| `TResult M(TSource source)` — produces | the **result** | ZERO229 |
| `void M(TSource source, TTarget target)` — writes onto | the **source** | ZERO225 |

So a mapping that produces a model may read from an entity with far more columns, and one
that writes onto an entity may leave its key, audit columns and state alone — but a field the
caller sent is never discarded silently, and a field the caller receives is never left empty
silently.

## Fix

If something else fills it — a value read after the mapping, a tree linked up in memory,
a field a sibling step supplies — say so:

```csharp
[Mapping(Ignore = [nameof(BedModel.Tags)])]
private static partial BedModel ToModel(Bed bed);
```

If it needs a decision, ignore it and make the decision at the call site:

```csharp
var model = ToModel(bed);
model.Tags = string.Join(',', bed.TagIds);
```

## Producing writes the key; writing onto does not

When constructing, the key is part of what the caller receives, so it is filled. When
writing onto an existing row it is skipped: there the key is how the row was found, and
assigning it from the caller's object is a no-op at best and a different row at worst.
