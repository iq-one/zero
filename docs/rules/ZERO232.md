# ZERO232 — A member is accounted for twice

**Severity:** error · **Category:** Zero.Persistence

A member gets one answer.

```csharp
[Mapping(Ignore = [nameof(BedModel.BedState)])]
[MapMember(nameof(BedModel.BedState), nameof(WriteBedState))]     // ZERO232
private static partial void Apply(BedModel model, Bed bed);
```

`Ignore` removes the member from the account; `[MapMember]` keeps it there and says where it
goes. Honouring one leaves the other a lie sitting in the source, and which one won would
depend on the order they happen to be read in.

Two `[MapMember]` attributes on the same member are the same problem said a different way.

## Fix

Keep the one that is true:

- **Ignore** if the member must not be carried — the target has no column for it, another
  step owns it, the caller may send it but must not change it.
- **`[MapMember]`** if it must be carried *differently* — an enum stored as a byte, a value
  that has to be looked up, a name split across two columns.

Prefer the second whenever the member does have an answer. `Ignore` is the weaker statement:
it says "not here" and nothing checks that the value is anywhere at all.
