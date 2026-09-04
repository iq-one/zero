# ZERO230 — A custom-mapped member is not part of the accounted type

**Severity:** error · **Category:** Zero.Persistence

`[MapMember]` names the member the mapping is **held to account for** — the same list
`[Mapping(Ignore = [...])]` draws from, so the two read as alternatives on one set. Which
type that is follows the shape:

| shape | held to account | so `[MapMember]` names a member of |
| --- | --- | --- |
| `TResult M(TSource source)` — produces | the **result** | the result |
| `void M(TSource source, TTarget target)` — writes onto | the **source** | the source |

## The usual mistake: naming the member you WRITE

```csharp
[Mapping]
[MapMember(nameof(Bed.State), nameof(WriteBedState))]      // ZERO230
private static partial void Apply(BedModel model, Bed bed);

private static void WriteBedState(BedModel model, Bed bed) => bed.State = model.BedState;
```

`Apply` writes onto `bed`, so its account is `BedModel`. `Bed.State` is what the helper
*writes*, not what it discharges — and `BedModel.BedState`, the member that actually needed
an answer, is still unaccounted for. Left unreported this would read as though the enum had
been handled while ZERO225 fired on a different line, or worse, `BedModel` gained a matching
member later and the helper silently became redundant.

## Fix

Name the member being accounted for. The helper still writes wherever it likes — that is
usually the whole reason it exists:

```csharp
[Mapping]
[MapMember(nameof(BedModel.BedState), nameof(WriteBedState))]
private static partial void Apply(BedModel model, Bed bed);
```

If instead the name is simply stale after a rename, correct it — and use `nameof`, which
would have failed to compile rather than reaching this rule.
