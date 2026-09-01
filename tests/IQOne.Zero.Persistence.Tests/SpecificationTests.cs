using IQOne.Zero.Persistence;

namespace IQOne.Zero.Persistence.Tests;

internal sealed class Invoice : IAggregateRoot, IEntity<int>
{
    public int Id { get; init; }
    public int CustomerId { get; init; }
    public bool IsPaid { get; init; }
    public DateOnly Due { get; init; }
    public List<string> Lines { get; init; } = [];
}

internal sealed class OverdueInvoices : Specification<Invoice>
{
    public OverdueInvoices(int customerId, DateOnly asOf)
    {
        Where(i => i.CustomerId == customerId);
        Where(i => !i.IsPaid && i.Due < asOf);
        Include(i => i.Lines);
        OrderBy(i => i.Due);
        OrderByDescending(i => i.Id);
        Page(0, 20);
        ReadOnly();
        IgnoreFilter("soft-delete");
    }
}

/// <summary>
/// A specification is only worth the class if it can be checked without a database. These
/// run the criteria against a plain list, which is exactly how a consumer should test theirs.
/// </summary>
public class SpecificationTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly Invoice[] All =
    [
        new() { Id = 1, CustomerId = 7, IsPaid = false, Due = new DateOnly(2026, 1, 1) },
        new() { Id = 2, CustomerId = 7, IsPaid = true, Due = new DateOnly(2026, 1, 1) },
        new() { Id = 3, CustomerId = 7, IsPaid = false, Due = new DateOnly(2026, 2, 1) },
        new() { Id = 4, CustomerId = 9, IsPaid = false, Due = new DateOnly(2026, 1, 1) }
    ];

    [Fact]
    public void Repeated_Where_calls_narrow_rather_than_replace()
    {
        var specification = new OverdueInvoices(7, Today);

        var matched = All.AsQueryable().Where(specification.Criteria!).Select(i => i.Id);

        matched.Should().Equal([1], "only invoice 1 is customer 7 s, unpaid and past due");
    }

    [Fact]
    public void Orderings_are_kept_in_the_order_they_were_stated()
    {
        var specification = new OverdueInvoices(7, Today);

        specification.Orderings.Should().HaveCount(2);
        specification.Orderings[0].Descending.Should().BeFalse();
        specification.Orderings[1].Descending.Should().BeTrue();
    }

    [Fact]
    public void Paging_tracking_and_includes_are_recorded()
    {
        var specification = new OverdueInvoices(7, Today);

        specification.Skip.Should().Be(0);
        specification.Take.Should().Be(20);
        specification.AsNoTracking.Should().BeTrue();
        specification.Includes.Should().ContainSingle();
    }

    [Fact]
    public void Only_the_named_filter_is_opted_out_of()
    {
        var specification = new OverdueInvoices(7, Today);

        specification.IgnoredFilters.Should().Equal("soft-delete");
        specification.IgnoredFilters.Should().NotContain("tenant",
            "opting out of one filter must never take the others with it");
    }

    [Fact]
    public void A_specification_with_no_criteria_matches_everything()
    {
        var specification = new AllInvoices();

        specification.Criteria.Should().BeNull();
    }

    private sealed class AllInvoices : Specification<Invoice>;
}
