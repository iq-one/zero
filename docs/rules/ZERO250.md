# ZERO250 — A mapped member is unaccounted for

**Severity:** error · **Category:** Zero.Mapping

The shape a map produces is what a caller receives, so **every member of it has to have an
answer**. Matching is by name and by name only; anything else is a decision, and this rule
asks for it.

```csharp
public sealed class BedModel
{
    public short Id { get; set; }
    public EnumBedState BedState { get; set; }   // Bed has no BedState
}

public sealed partial class BedMap : Map<Bed, BedModel>;   // ZERO250
```

## Why this is an error and not a silence

A runtime mapper drops the member and carries on: the field is null, the screen is blank, and
nothing in the code says why. The failure arrives in production, far from the line that caused
it. Here it arrives at the build, naming the member.

## Fix — one of two

Fill it, and the expression is copied into the tree the generator writes:

```csharp
protected override void Configure(IMapBuilder<Bed, BedModel> map)
    => map.Member(m => m.BedState, e => (EnumBedState)e.State);
```

Or say it is deliberately left empty, which keeps it accounted for:

```csharp
protected override void Configure(IMapBuilder<Bed, BedModel> map)
    => map.Ignore(m => m.BuildingUnit, m => m.Department);
```

`Ignore` is not a way to quiet the rule — it is a statement that the next reader will believe.
A navigation nobody loaded on this path is a legitimate one; a member you forgot is not.

## The nullable case

The reason is often *"'X' is nullable and 'Y' is not; say what an absent value becomes"*:

```csharp
// entity: short? DepartmentId — model: short DepartmentId
map.Member(m => m.DepartmentId, e => e.DepartmentId ?? 0);
```

The fallback — zero, false, the default enum member — is a choice, and it belongs where a
reader can see it rather than inside a mapper's conversion rules.

## The source may be wider

Only the destination is held to account. An entity usually carries far more than the model
publishes — audit columns, a row version, a state byte — and none of that needs mentioning.
