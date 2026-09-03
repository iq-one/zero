# ZERO220 — A projected member has no source

**Severity:** error · **Category:** Zero.Persistence

`[Projection]` maps member for member by name. A member of the result that cannot be mapped
stops the generation.

```csharp
public sealed class InvoiceModel
{
    public int Id { get; set; }
    public string? Tags { get; set; }      // Invoice has no Tags
}

[Projection]
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;   // ZERO220
```

Nothing is generated — not even the members that would have worked. A projection that is
three quarters written and one quarter silently absent is the failure this rule exists to
prevent: the symptom is a field missing from a response, and there is nothing in the code to
explain it.

The same applies to a mapping that would need a decision rather than a conversion:

```csharp
public int? CustomerId { get; set; }   // on the entity
public int CustomerId { get; set; }    // on the model — what does absent become?

public int Id { get; set; }            // on the entity
public short Id { get; set; }          // on the model — a cast here silently wraps
```

## Fix

If something else fills the member — a value read after the query, a tree linked up in
memory, a field a sibling endpoint supplies — say so:

```csharp
[Projection(Ignore = [nameof(InvoiceModel.Tags)])]
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
```

If the mapping needs a decision, write the selector and make the decision visible:

```csharp
public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>
{
    public override Expression<Func<Invoice, InvoiceModel>> Selector =>
        e => new InvoiceModel
        {
            Id = e.Id,
            CustomerId = e.CustomerId ?? 0,   // absent means unassigned
        };
}
```

Drop the attribute when you do — a hand-written selector alongside it is ZERO224.
