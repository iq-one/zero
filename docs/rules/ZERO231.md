# ZERO231 — A custom-mapped member names no usable method

**Severity:** error · **Category:** Zero.Persistence

The method `[MapMember]` names takes the shape of the mapping itself, **one member's
worth**:

| the mapping | the method | because |
| --- | --- | --- |
| `TResult M(TSource source)` — produces | `TMember H(TSource source)` | it returns the value, which goes into the initialiser |
| `void M(TSource source, TTarget target)` — writes onto | `void H(TSource source, TTarget target)` | it performs the write |

The second takes the target because the member it writes need not be the one it discharges.
An enum on the model stored as a byte on the entity is the ordinary case, and it is why the
attribute exists.

```csharp
[Mapping]
[MapMember(nameof(BedModel.BedState), nameof(WriteBedState))]
private static partial void Apply(BedModel model, Bed bed);

private static void WriteBedState(BedModel model, Bed bed) => bed.State = (byte)model.BedState;
```

The method lives in the same type as the mapping, so it may be `private`: the generated body
is another part of that type. The message says what was wrong and prints the signature it
expected.

## A static mapping can only call a static method

```csharp
[Mapping]
[MapMember(nameof(BedModel.DepartmentName), nameof(NameOf))]
private static partial void Apply(BedModel model, Bed bed);   // ZERO231

private string? NameOf(BedModel model) => departments.Name(model.DepartmentId);
```

Drop `static` from the mapping and the helper can read the constructor's dependencies. A
mapping's home is a class; making that class a service is how it gets a lifetime
(`IScoped` and its siblings), a key (`[ServiceTypes(key, ...)]`) and its dependencies — all
declared the way every other service declares them:

```csharp
[ServiceTypes("detailed", typeof(IBedMapper))]
public sealed partial class BedMapper(IDepartmentNames departments) : IBedMapper, IScoped
{
    [Mapping]
    [MapMember(nameof(BedModel.DepartmentName), nameof(NameOf))]
    public partial BedModel ToModel(Bed bed);

    private string? NameOf(Bed bed) => departments.Name(bed.DepartmentId);
}
```

Inject something already in memory — a cache, the current user's claims, configuration. A
repository queried inside a helper runs once per element, and for a list of a thousand that
is a thousand round trips with nothing in the type system to say so. Load first, map second.

## A mapping may not name itself

The writing shape has the same signature as its own helper, so naming the mapping compiles
and then calls itself forever. The shape check cannot see it; the name check can.
