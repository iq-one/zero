# ZERO259 — The generator failed on this map

**Severity:** error · **Category:** Zero.Mapping

A bug in the generator, reported against the map it happened on.

## Why this rule exists at all

Generated code **cannot be edited**. It is produced during compilation and there is no file the
compiler reads back — `EmitCompilerGeneratedFiles` writes a copy for you to read, and nothing
reads it. So when a generator is wrong, the person whose build stopped has no line to change.

Worse, a generator that throws normally takes the *whole build* with it (CS8785): one map the
framework mishandles, and nothing in the project compiles. That would leave its user waiting
for a framework release with nothing to try.

This rule is the alternative. The failure is caught per map: this one reports, every other map
is written as usual, and the one that failed can be taken over.

## Carry on

Declare the selector yourself, and the generator stands down for that map — no diagnostic, no
generated half:

```csharp
public sealed partial class BedMap : Map<Bed, BedModel>
{
    public override Expression<Func<Bed, BedModel>> Selector { get; } =
        e => new BedModel
        {
            Id = e.Id,
            Name = e.Name,
            BedState = (EnumBedState)e.State
        };
}
```

`Project` still works — the base compiles it from whatever you wrote. Keep `Configure` if you
like; it is read only when the generator is writing the selector, so it does nothing here.

## Then report it

The message carries the exception type and text. That plus the map is usually enough to
reproduce. The escape hatch above is a way to keep working, not a fix: a map written by hand is
a map nothing checks for completeness, which is what the generator was for.

## The one failure this cannot catch

A generator that fails while *loading* — a missing dependency, a version mismatch between the
analyzer and the compiler — never reaches a map, so there is nothing to report per map. There
the answer is the ordinary one: pin the previous framework version until it is fixed.
