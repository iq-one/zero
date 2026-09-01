using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>What a repository promises on top of the evaluator.</summary>
public sealed class RepositoryTests : IDisposable
{
    private readonly ShopDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static EfRepository<Invoice, int> Repository(DbContext context)
        => new(context, SpecificationEvaluator.Default);

    [Fact]
    public async Task Counting_ignores_the_paging_the_specification_asks_for()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var repository = Repository(context);
        var specification = new UnpaidInvoicesOnOnePage(skip: 1, take: 2);

        var page = await repository.ListAsync(specification);
        var total = await repository.CountAsync(specification);

        page.Should().HaveCount(2, "the page is what the specification asks for");
        total.Should().Be(5, "counting a page would report the page size back to a caller that set it");
    }

    [Fact]
    public async Task Counting_still_honours_the_criteria()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        (await Repository(context).CountAsync(new LargeUnpaidInvoices(atLeast: 400))).Should().Be(3);
    }

    [Fact]
    public async Task Finding_returns_the_first_match_in_the_specification_order()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var invoice = await Repository(context).FindAsync(new LargeUnpaidInvoices(atLeast: 300));

        invoice!.Customer.Should().Be("Acme");
    }

    [Fact]
    public async Task Finding_a_projection_returns_the_shape()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var summary = await Repository(context).FindAsync(new InvoiceSummaries());

        summary!.Customer.Should().Be("Acme");
        summary.Total.Should().Be(300);
    }

    [Fact]
    public async Task Listing_a_projection_returns_the_shapes()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var summaries = await Repository(context).ListAsync(new OnePageOfSummaries(skip: 4, take: 2));

        summaries.Select(s => s.Customer).Should().Equal("Edge", "Flux");
    }

    [Fact]
    public async Task Asking_whether_anything_matches_costs_one_round_trip()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var repository = Repository(context);

        (await repository.AnyAsync(new LargeUnpaidInvoices(atLeast: 300))).Should().BeTrue();
        (await repository.AnyAsync(new LargeUnpaidInvoices(atLeast: 10_000))).Should().BeFalse();
    }

    [Fact]
    public async Task A_key_lookup_finds_the_aggregate()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var id = await context.Invoices.Where(i => i.Customer == "Cog").Select(i => i.Id).SingleAsync();

        (await Repository(context).GetAsync(id))!.Customer.Should().Be("Cog");
        (await Repository(context).GetAsync(-1)).Should().BeNull();
    }

    [Fact]
    public async Task A_key_lookup_still_has_the_filters_on()
    {
        await _database.SeedAsync();

        int deleted;

        await using (var plain = _database.Plain())
            deleted = await plain.Invoices.Where(i => i.Customer == "Dial").Select(i => i.Id).SingleAsync();

        await using var context = _database.Filtered("north");

        (await Repository(context).GetAsync(deleted)).Should().BeNull(
            "a key lookup that reaches the database is a query like any other");
    }

    [Fact]
    public async Task Writing_records_the_intent_and_the_unit_of_work_persists_it()
    {
        await using var context = _database.Plain();

        var repository = Repository(context);
        var unitOfWork = new EfUnitOfWork(context);

        await repository.AddAsync(new Invoice { Tenant = "north", Customer = "Gear", Total = 700 });
        await repository.AddRangeAsync(
        [
            new Invoice { Tenant = "north", Customer = "Hinge", Total = 800 },
            new Invoice { Tenant = "north", Customer = "Idler", Total = 900 }
        ]);

        await using (var before = _database.Plain())
            (await before.Invoices.CountAsync()).Should().Be(0, "nothing reaches the database until it is saved");

        await unitOfWork.SaveChangesAsync();

        await using var after = _database.Plain();
        (await after.Invoices.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Removing_takes_the_row_out()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        var repository = Repository(context);
        var invoices = await repository.ListAsync(new EveryInvoice());

        repository.Remove(invoices.Single(i => i.Customer == "Cog"));

        await new EfUnitOfWork(context).SaveChangesAsync();

        await using var after = _database.Plain();
        (await after.Invoices.AnyAsync(i => i.Customer == "Cog")).Should().BeFalse();
        (await after.Invoices.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task Updating_an_aggregate_that_arrived_from_elsewhere_is_written()
    {
        await _database.SeedAsync();

        int id;

        await using (var read = _database.Plain())
            id = await read.Invoices.Where(i => i.Customer == "Cog").Select(i => i.Id).SingleAsync();

        await using (var write = _database.Plain())
        {
            var repository = Repository(write);

            repository.Update(new Invoice { Id = id, Tenant = "north", Customer = "Cog", Total = 250, IsPaid = true });

            await new EfUnitOfWork(write).SaveChangesAsync();
        }

        await using var after = _database.Plain();
        (await after.Invoices.SingleAsync(i => i.Id == id)).Total.Should().Be(250);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_read()
    {
        await _database.SeedAsync();
        await using var context = _database.Plain();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var reading = () => Repository(context).ListAsync(new EveryInvoice(), cancelled.Token);

        await reading.Should().ThrowAsync<OperationCanceledException>();
    }
}
