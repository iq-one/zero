# ZERO252 — A declaration does not name a settable member

**Severity:** error · **Category:** Zero.Mapping

Both `map.Member` and `map.Ignore` name a member of the shape being **produced**, and it has to
be one an initialiser can set.

```csharp
public sealed class BedModel
{
    public string? Name { get; set; }
    public string FullName => Name ?? "";     // computed
}

map.Ignore(m => m.FullName);                 // ZERO252
```

A computed or read-only member is not filled by anybody, so it is not part of the account and
naming it says nothing.

## The usual mistake: naming the other end

```csharp
map.Member(m => m.State, e => e.State);      // ZERO252 if the model has no State
```

The first lambda names the **destination**; the second reads the source. Naming the source's
member in the first position is the common slip, and it reads as though something were handled
while the member that actually needed an answer is still unaccounted for (ZERO240).

## Fix

Name a settable member of the destination, or — if the member really is computed — remove the
declaration; it was never needed.
