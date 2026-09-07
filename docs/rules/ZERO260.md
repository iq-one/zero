# ZERO260 — The shape being produced cannot be constructed

**Severity:** error · **Category:** Zero.Mapping

A map writes a construction of the destination, so there has to be **one** way to write it.

Two forms are understood, and which applies is not a preference:

| the destination has | written as |
| --- | --- |
| a parameterless constructor | `new Dest { Member = …, }` |
| exactly one constructor, no parameterless one | `new Dest(a, b)` — parameters matched by name |

A positional `record` is the second form and the commonest destination in ported code: it has
no parameterless constructor at all, so an initialiser over one does not compile.

## Several constructors and no parameterless one

```csharp
public sealed class RowModel
{
    public RowModel(int a) { }
    public RowModel(int a, string? b) { }        // ZERO260
}
```

Picking one would be the generator deciding which shape the caller meant, silently — and the
two differ in exactly the member somebody cared about. Give the type a parameterless
constructor, or take the map over with `[NoGenerate]`.

## A tuple

```csharp
public sealed partial class RowMap : Map<Row, (int Id, string? Tags)>;   // ZERO260
```

A tuple's element names live at the call site. Everywhere else — including in the map — they
are `Item1` and `Item2`, so there is nothing to match by name. Declare a record instead; it
costs one line and gives the shape a name.

## A parameter is accounted for like any other member

```csharp
public sealed record RowModel(int A, State State);

protected override void Configure(IMapBuilder<Row, RowModel> map)
    => map.Member(m => m.State, e => (State)e.State);
```

`Ignore` works too and passes `default`: there is no way not to pass a parameter, and `default`
is what an unassigned member would have held.
