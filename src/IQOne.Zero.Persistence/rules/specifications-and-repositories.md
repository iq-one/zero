---
id: zero.persistence.specifications
title: Write queries as specifications, and reach data through a repository
package: IQOne.Zero.Persistence
applies-to: ["**/*.cs"]
---

A query is a named class, not a chain of calls inside a handler. The handler asks a
repository for what the specification describes.

## Do

```csharp
public sealed class OverdueInvoices : Specification<Invoice>
{
    public OverdueInvoices(int customerId, DateOnly asOf)
    {
        Where(i => i.CustomerId == customerId);
        Where(i => !i.IsPaid && i.Due < asOf);
        Include(i => i.Lines);
        OrderBy(i => i.Due);
        ReadOnly();
    }
}
```

```csharp
var invoices = await repository.ListAsync(new OverdueInvoices(id, today), cancellationToken);
```

Naming it means "overdue" is defined once. Two handlers cannot disagree about what overdue
means, and the definition can be unit tested against a list in memory with no database.

## Don't

Do not put provider-specific calls in a handler:

```csharp
var invoices = await context.Invoices
    .Include(i => i.Lines)
    .Where(i => !i.IsPaid)
    .ToListAsync(cancellationToken);      // the handler now depends on EF
```

Do not **commit** in a handler. `TransactionBehavior` commits when a command succeeds and
rolls back when it fails, so a handler that completes the transaction itself has decided the
outcome before the pipeline knows what it is.

Calling `SaveChangesAsync` is a different thing and is allowed: inside the transaction it
flushes, and the transaction still decides whether any of it survives. You need it when the
database assigns something you have to return — an identity column, a computed value:

```csharp
await orders.AddAsync(order, cancellationToken);

// The id does not exist until the insert happens, and the pipeline saves after this method
// returns. Flushing here fills it in; the transaction still governs whether it stands.
await unitOfWork.SaveChangesAsync(cancellationToken);

return order.Id;
```

Better still, avoid needing it: if the caller chose the identity — a reference, a
client-generated id — then the caller already has it and nothing has to be read back. That is
also what makes the command safe to retry.

Do not write your own repository or specification interface. This is the one Zero has.

## Read and write are separate

Take `IReadRepository<T>` when the handler only reads. The constructor then says so, and the
compiler enforces it — a reviewer does not have to read the body to find out whether a query
handler writes.

## Repositories are for aggregate roots

`IRepository<T>` requires `IAggregateRoot`. Everything reachable from a root is loaded and
saved with it. Without that boundary, a "consistency boundary" quietly becomes whatever the
last query happened to touch.

## Why a repository at all

Entity Framework's `DbContext` is already a unit of work and `DbSet` is already a
repository, and wrapping them is a real cost. Zero wraps them anyway, for two reasons that
apply to this framework specifically:

- A handler that never sees the provider can be tested without a database, and the analyzer
  can enforce that it never sees one.
- Specifications only pay off if there is somewhere to hand them.

If neither reason applies to your project, use the context directly — nothing stops you.
But do not do both: one project with two data-access styles is one an agent will guess wrong
about.

## Opting out of a filter

Name the one filter you mean:

```csharp
IgnoreFilter("soft-delete");
```

Never a switch that ignores all of them. A query that ignores every filter will, sooner or
later, read another tenant's rows.
