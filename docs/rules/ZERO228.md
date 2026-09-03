# ZERO228 — The type holding a mapping is not partial

**Severity:** error · **Category:** Zero.Persistence

The body is written into a second part of the type. Without the modifier there is nowhere
to put it.

```csharp
public sealed class SaveBeds                    // ZERO228
{
    [Mapping]
    private static partial void Apply(BedModel model, Bed bed);
}
```

## Fix

```csharp
public sealed partial class SaveBeds
{
    [Mapping]
    private static partial void Apply(BedModel model, Bed bed);
}
```
