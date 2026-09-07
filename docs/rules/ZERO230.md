# ZERO230 — A projection or mapping generator failed

**Severity:** error · **Category:** Zero.Persistence

A bug in the generator behind `[Projection]` or `[Mapping]`, reported against the declaration
it happened on.

## Why this rule exists at all

A generator that throws fails the compilation with CS8785 and produces nothing, and generated
code cannot be edited — it is written during compilation and there is no file the compiler reads
back. Left uncaught, one declaration the framework mishandles would stop the whole build with
nothing for its author to change.

Caught per declaration, the rest are written as usual and only this one needs a hand.

## Carry on

Remove the attribute and write what it was writing:

```csharp
// [Projection] removed
public sealed class BedQuery : LegacyQuery<Bed, BedModel>
{
    public override Expression<Func<Bed, BedModel>> Selector =>
        e => new BedModel { Id = e.Id, Name = e.Name, BedState = (EnumBedState)e.State };
}
```

```csharp
// [Mapping] removed
private static void Apply(BedModel model, Bed bed)
{
    bed.Name = model.Name;
    bed.BuildingUnitId = model.BuildingUnitId;
}
```

The attribute is the trigger, so removing it is a complete opt-out — and the hand-written
member is then the only account of what happens, which nothing checks for completeness. That
is what the generator was for, so put the attribute back once the bug is fixed.

## Then report it

The message carries the exception type and text along with the declaration's name.
