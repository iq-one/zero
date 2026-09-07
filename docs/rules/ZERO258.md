# ZERO258 — A map is not partial

**Severity:** error · **Category:** Zero.Mapping

The generator writes the selector into a **second part of the map's type**, so the type has to
be declared `partial`.

```csharp
public sealed class BedMap : Map<Bed, BedModel>;            // ZERO258
```

```csharp
public sealed partial class BedMap : Map<Bed, BedModel>;    // fine
```

There is no marker attribute to add: the base type already names the pair, and asking for an
attribute as well would be a second place to say the same thing. `partial` is the only
ceremony, and it is the one the language requires.
