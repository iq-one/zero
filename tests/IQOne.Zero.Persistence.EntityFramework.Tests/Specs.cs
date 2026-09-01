using System.Linq.Expressions;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>The three fields a list endpoint actually shows.</summary>
public sealed record InvoiceSummary(int Id, string Customer, int Total);

/// <summary>Two criteria, to prove the second narrows rather than replaces the first.</summary>
public sealed class LargeUnpaidInvoices : Specification<Invoice>
{
    public LargeUnpaidInvoices(int atLeast)
    {
        Where(i => !i.IsPaid);
        Where(i => i.Total >= atLeast);
        OrderBy(i => i.Customer);
    }
}

/// <summary>An ordering and a tie-breaker, to prove the second becomes a ThenBy.</summary>
public sealed class InvoicesByTenantThenLargestFirst : Specification<Invoice>
{
    public InvoicesByTenantThenLargestFirst()
    {
        OrderBy(i => i.Tenant);
        OrderByDescending(i => i.Total);
    }
}

public sealed class InvoicesWithTheirLines : Specification<Invoice>
{
    public InvoicesWithTheirLines()
    {
        Include(i => i.Lines);
        OrderBy(i => i.Customer);
    }
}

public sealed class OnePageOfInvoices : Specification<Invoice>
{
    public OnePageOfInvoices(int skip, int take)
    {
        OrderBy(i => i.Customer);
        Page(skip, take);
    }
}

public sealed class UnpaidInvoicesOnOnePage : Specification<Invoice>
{
    public UnpaidInvoicesOnOnePage(int skip, int take)
    {
        Where(i => !i.IsPaid);
        OrderBy(i => i.Customer);
        Page(skip, take);
    }
}

public sealed class InvoicesNobodyTracks : Specification<Invoice>
{
    public InvoicesNobodyTracks() => ReadOnly();
}

public sealed class EveryInvoice : Specification<Invoice>
{
    public EveryInvoice() => OrderBy(i => i.Customer);
}

public sealed class InvoicesIncludingDeleted : Specification<Invoice>
{
    public InvoicesIncludingDeleted()
    {
        IgnoreFilter("soft-delete");
        OrderBy(i => i.Customer);
    }
}

public sealed class InvoicesAcrossTenants : Specification<Invoice>
{
    public InvoicesAcrossTenants()
    {
        IgnoreFilter("tenant");
        OrderBy(i => i.Customer);
    }
}

public sealed class InvoiceSummaries : Specification<Invoice, InvoiceSummary>
{
    public InvoiceSummaries() => OrderBy(i => i.Customer);

    public override Expression<Func<Invoice, InvoiceSummary>> Selector
        => invoice => new InvoiceSummary(invoice.Id, invoice.Customer, invoice.Total);
}

public sealed class OnePageOfSummaries : Specification<Invoice, InvoiceSummary>
{
    public OnePageOfSummaries(int skip, int take)
    {
        OrderBy(i => i.Customer);
        Page(skip, take);
    }

    public override Expression<Func<Invoice, InvoiceSummary>> Selector
        => invoice => new InvoiceSummary(invoice.Id, invoice.Customer, invoice.Total);
}
