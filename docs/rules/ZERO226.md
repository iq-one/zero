# ZERO226 — An ignored member is not part of the source

**Severity:** error · **Category:** Zero.Persistence

`[Mapping(Ignore = [...])]` names a member the source does not have.

```csharp
[Mapping(Ignore = ["BedStait"])]                                  // ZERO226
private static partial void Apply(BedModel model, Bed bed);
```

An entry that matches nothing accounts for no member, while reading as though a real
omission were accounted for. It is usually left behind by a rename.

## Fix

Correct it, or remove it, and prefer `nameof` so the next rename is a build error rather
than a stale note:

```csharp
[Mapping(Ignore = [nameof(BedModel.BedState)])]
private static partial void Apply(BedModel model, Bed bed);
```
