# ZERO501 — An event can be changed after it is published

**Severity:** warning · **Category:** Zero.Events

An event has a settable property.

```csharp
public sealed record InvoicePaid : IEvent
{
    public int InvoiceId { get; set; }        // ZERO501
    public decimal Amount { get; set; }       // ZERO501
}
```

Subscribers run one after another over the **same instance**. A settable property is
therefore a private channel between two of them — one writes, another reads — in an order
the framework does not define.

That code works in development, where there are two subscribers and they happen to be
registered in the order the author had in mind. It fails when a third is added, or when the
registration order changes for an unrelated reason, and it fails by producing a wrong value
rather than an error.

## Fix

Make the event a value:

```csharp
public sealed record InvoicePaid(int InvoiceId, decimal Amount) : IEvent;
```

Or, for a property outside the primary constructor, `init`:

```csharp
public sealed record InvoicePaid(int InvoiceId)
{
    public decimal Amount { get; init; }
}
```

## If two subscribers really need to share something

They are not two subscribers. Either it is one subscriber doing both pieces, or the second
piece belongs behind its own event that the first publishes — which makes the dependency
explicit and the order defined, instead of relying on a mutable field and luck.

## Why a warning

A mutable event that nobody actually mutates is harmless, and there are legitimate reasons a
type has a setter — a serializer that cannot use `init`, for instance. The rule flags the
shape that makes the mistake possible, and that judgement stays with the author.
