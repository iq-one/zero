# ZERO243 — A member is accounted for twice

**Severity:** error · **Category:** Zero.Mapping

A member gets one answer.

```csharp
protected override void Configure(IMapBuilder<Bed, BedModel> map) => map
    .Member(m => m.BedState, e => (EnumBedState)e.State)
    .Ignore(m => m.BedState);                          // ZERO243
```

`Ignore` leaves the member empty; `Member` fills it. Honouring one leaves the other a lie
sitting in the source, and which one won would depend on the order they happen to be read in.
Two `Member` calls for the same member, or two `Ignore` entries, say it twice the same way.

## Fix

Keep the one that is true. `Member` when the member has a value; `Ignore` when it is
deliberately empty on this path.
