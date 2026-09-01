# IQOne.Zero.Persistence

Provider-neutral data access: specifications, segregated repositories, an explicit
transaction boundary, and conventions applied uniformly.

```csharp
public sealed class OverdueInvoices : Specification<Invoice>
{
    public OverdueInvoices(DateOnly asOf)
    {
        Where(i => !i.IsPaid && i.Due < asOf);
        OrderBy(i => i.Due);
        ReadOnly();
    }
}
```

```csharp
var overdue = await repository.ListAsync(new OverdueInvoices(today), cancellationToken);
```

This package references no ORM. Add a provider package for the implementation, and
`AddZeroTransactions()` to wrap commands — never queries — in a transaction.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
