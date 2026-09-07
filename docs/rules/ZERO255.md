# ZERO255 — A composition names a pair with no map

**Severity:** error · **Category:** Zero.Mapping

Composition writes the other map's tree **in place of the member**, so that map has to be
readable from here — which means declared in this compilation.

```csharp
map.Member(m => m.BedType, e => e.BedType.To<BedTypeModel>());   // ZERO255 with no BedTypeMap
```

## Fix

Declare the map. If every member matches by name it is one line and says nothing:

```csharp
public sealed partial class BedTypeMap : Map<BedType, BedTypeModel>;
```

The message spells out the declaration to write, including both type arguments.

## Why it cannot just be called

A composition is not a call to the other map — it is that map's tree written out. A method
call in an expression tree is not something a query provider can translate, so composing by
calling would turn one `SELECT` into a client-side evaluation or a second query. Writing it
out is what keeps a nested model one statement.

That is also why the map has to be in **this** compilation: the generator needs its source,
not just its type. A pair whose map lives in a referenced assembly is not composable this way;
give the composing side its own map for the pair, or move the map to where it is used.
