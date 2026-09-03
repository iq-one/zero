# ZERO223 — A projected specification is not partial

**Severity:** error · **Category:** Zero.Persistence

The selector is written into a second part of the class. Without the modifier there is
nowhere to put it.

```csharp
[Projection]
public sealed class InvoiceQuery : Specification<Invoice, InvoiceModel>;   // ZERO223
```

## Fix

```csharp
[Projection]
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
```
