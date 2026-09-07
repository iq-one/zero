# ZERO246 — A map's configuration cannot be read

**Severity:** error · **Category:** Zero.Mapping

`Configure` is **read, not run**. The generator takes what it declares and writes the tree from
it as source, which means it reads the syntax rather than executing it.

```csharp
protected override void Configure(IMapBuilder<Bed, BedModel> map)
{
    if (DateTime.Now.Year > 2020)                                  // ZERO246
        map.Member(m => m.BedState, e => (EnumBedState)e.State);
}
```

A body that decides at run time what to map has no single answer to read. Guessing at one would
produce a map nobody declared, and skipping it silently would be exactly the failure this design
exists to remove — a declaration that looks honoured and is not.

## What can be read

A plain sequence of `map.Member` and `map.Ignore` calls, in a block or behind an arrow, chained
or separate:

```csharp
protected override void Configure(IMapBuilder<Bed, BedModel> map) => map
    .Member(m => m.BedState, e => (EnumBedState)e.State)
    .Ignore(m => m.BuildingUnit, m => m.Department);
```

```csharp
protected override void Configure(IMapBuilder<Bed, BedModel> map)
{
    map.Member(m => m.BedState, e => (EnumBedState)e.State);
    map.Ignore(m => m.BuildingUnit);
}
```

Each `Member`'s source has to be one expression — a lambda with a statement body has nothing to
copy into an initialiser.

## If the shape really does vary

Then it is two maps, not one that decides. Declare both and let the call site choose which it
wants; the choice is then something a reader can see.
