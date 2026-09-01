using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>
/// What a specification promises, checked against a database that has to translate it.
/// </summary>
/// <remarks>
/// Every test here runs the query. A feature that produces untranslatable SQL fails rather
/// than falling back to evaluating in memory, which is the reason these run on Sqlite.
/// </remarks>
public sealed class SpecificationEvaluatorTests : IDisposable
{
    private readonly ShopDatabase _database = new();
    private readonly ISpecificationEvaluator _evaluator = SpecificationEvaluator.Default;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task A_second_criterion_narrows_rather_than_replaces()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var invoices = await _evaluator
            .Evaluate(context.Invoices, new LargeUnpaidInvoices(atLeast: 300))
            .ToListAsync();

        invoices.Select(i => i.Customer).Should().Equal("Acme", "Dial", "Edge", "Flux");
    }

    [Fact]
    public async Task An_include_brings_the_related_rows()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var invoices = await _evaluator
            .Evaluate(context.Invoices, new InvoicesWithTheirLines())
            .ToListAsync();

        invoices.Single(i => i.Customer == "Bolt").Lines
            .Select(l => l.Description).Should().BeEquivalentTo("nut", "bolt");

        invoices.Single(i => i.Customer == "Cog").Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task The_first_ordering_sorts_and_the_next_one_breaks_the_tie()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var query = _evaluator.Evaluate(context.Invoices, new InvoicesByTenantThenLargestFirst());

        // Proof that the tie-breaker is a ThenBy and not a second OrderBy that threw the
        // first one away: the tenants stay grouped.
        query.ToQueryString().Should().Contain("ORDER BY").And.Contain("DESC");

        (await query.ToListAsync()).Select(i => i.Customer)
            .Should().Equal("Dial", "Acme", "Cog", "Bolt", "Flux", "Edge");
    }

    [Fact]
    public async Task Paging_takes_one_page_and_the_database_does_the_skipping()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var query = _evaluator.Evaluate(context.Invoices, new OnePageOfInvoices(skip: 1, take: 2));

        query.ToQueryString().Should().Contain("LIMIT").And.Contain("OFFSET");

        (await query.ToListAsync()).Select(i => i.Customer).Should().Equal("Bolt", "Cog");
    }

    [Fact]
    public async Task A_read_only_specification_leaves_nothing_in_the_change_tracker()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        await _evaluator.Evaluate(context.Invoices, new InvoicesNobodyTracks()).ToListAsync();

        context.ChangeTracker.Entries<Invoice>().Should().BeEmpty();

        await _evaluator.Evaluate(context.Invoices, new EveryInvoice()).ToListAsync();

        context.ChangeTracker.Entries<Invoice>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_projection_is_computed_by_the_database()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        IQueryable<InvoiceSummary> query = _evaluator.Evaluate(context.Invoices, new InvoiceSummaries());
        var sql = query.ToQueryString();

        sql.Should().Contain("\"Customer\"").And.Contain("\"Total\"");

        // The columns the shape does not ask for never leave the server. If the projection
        // ran after materialisation these would be in the SELECT list too.
        sql.Should().NotContain("\"IsPaid\"").And.NotContain("\"Due\"").And.NotContain("\"Tenant\"");

        (await query.ToListAsync()).Select(s => s.Customer)
            .Should().Equal("Acme", "Bolt", "Cog", "Dial", "Edge", "Flux");
    }

    [Fact]
    public async Task A_projection_is_paged_in_the_database_rather_than_after_it()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        IQueryable<InvoiceSummary> query = _evaluator.Evaluate(context.Invoices, new OnePageOfSummaries(skip: 2, take: 2));

        query.ToQueryString().Should().Contain("LIMIT").And.Contain("OFFSET");

        (await query.ToListAsync()).Select(s => s.Customer).Should().Equal("Cog", "Dial");
    }

    [Fact]
    public async Task Counting_drops_the_paging_the_ordering_and_the_includes()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var sql = _evaluator
            .EvaluateForCount(context.Invoices, new UnpaidInvoicesOnOnePage(skip: 1, take: 2))
            .ToQueryString();

        sql.Should().NotContain("LIMIT").And.NotContain("OFFSET").And.NotContain("ORDER BY");
    }

    [Fact]
    public async Task An_ordering_key_reaches_the_database_as_its_own_type()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        // A specification declares its key as Func<T, object?>, so the compiler boxes an int.
        // Left boxed, this either fails to translate or sorts by something that is not a number.
        var invoices = await _evaluator
            .Evaluate(context.Invoices, new InvoicesByTenantThenLargestFirst())
            .ToListAsync();

        invoices.Where(i => i.Tenant == "north").Select(i => i.Total)
            .Should().BeInDescendingOrder();
    }
}
