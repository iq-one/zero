# ZERO224 — A projected specification already declares its Selector

**Severity:** error · **Category:** Zero.Persistence

Both cannot stand: the generated member would be a duplicate of the one you wrote.

```csharp
[Projection]
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>   // ZERO224
{
    public override Expression<Func<Invoice, InvoiceModel>> Selector =>
        e => new InvoiceModel { Id = e.Id };
}
```

Which one was meant is your call, so neither is discarded silently.

## Fix

Keep the hand-written selector and drop the attribute:

```csharp
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>
{
    public override Expression<Func<Invoice, InvoiceModel>> Selector =>
        e => new InvoiceModel { Id = e.Id };
}
```

Or drop the member and let the generator write it:

```csharp
[Projection]
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
```

A projection usually becomes hand-written because one member needs a decision. When that
happens the whole selector moves — the generator is all or nothing on purpose.
