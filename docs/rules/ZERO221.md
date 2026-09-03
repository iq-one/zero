# ZERO221 — An ignored member is not part of the result

**Severity:** error · **Category:** Zero.Persistence

`[Projection(Ignore = [...])]` names a member the result does not have.

```csharp
[Projection(Ignore = ["Prise"])]                                    // ZERO221
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
```

An entry that matches nothing silences nothing, while reading as though a real hole were
accounted for. It is usually left behind by a rename.

## Fix

Correct the spelling, or remove the entry:

```csharp
[Projection(Ignore = [nameof(InvoiceModel.Price)])]
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
```

`nameof` rather than a string, so the next rename is a build error rather than a stale note.
