# ZERO225 — A mapped member is not written anywhere

**Severity:** error · **Category:** Zero.Persistence

`[Mapping]` holds the **source** to account. A member the caller sent that nothing consumes
is a field discarded without a word, on a request that looks like it worked.

```csharp
public sealed class BedModel
{
    public string? Name { get; set; }
    public byte BedState { get; set; }      // Bed has no BedState column
}

public sealed partial class SaveBeds
{
    [Mapping]
    private static partial void Apply(BedModel model, Bed bed);   // ZERO225
}
```

This is the direction that matters. A projection must produce the shape it was asked for, so
the *result* is checked; a mapping writes onto something that already exists, and the target
is allowed to have more — its key, its audit columns, whatever a convention fills. Only what
arrived has to be answered for.

Refused for the same reasons a projection refuses:

| the member | why it is a question |
| --- | --- |
| no settable member of that name | the field may belong to another step, or the name may have drifted |
| the target's member is read-only | writing it is impossible; discarding it silently is the bug |
| nullable source, non-nullable target | what an absent value writes is a decision |
| narrowing conversion | a cast compiles and silently wraps |

## Fix

If the field is deliberately not written — the caller may send it, another step owns it, or
it must not be changed here — say so:

```csharp
[Mapping(Ignore = [nameof(BedModel.BedState)])]
private static partial void Apply(BedModel model, Bed bed);
```

If it needs a decision, ignore it and make the decision at the call site, where a reader
sees both halves:

```csharp
Apply(model, bed);
bed.Name = model.Name?.Trim();
```

## The key is never written

A member matching the target's `IEntity<TKey>.Id` is skipped without being asked about. A key
is how the row was found; assigning it from the caller's object is a no-op at best and a
different row at worst. It is recognised through the interface, not by its name.
