using IQOne.Zero.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>
/// Filters registered by name, and the one thing that makes them safe: reading their values
/// through the context rather than remembering them.
/// </summary>
public sealed class FilterConventionTests : IDisposable
{
    private readonly ShopDatabase _database = new();
    private readonly ISpecificationEvaluator _evaluator = SpecificationEvaluator.Default;

    public void Dispose() => _database.Dispose();

    [Fact]
    public void Every_filter_is_registered_under_its_own_name()
    {
        using var context = _database.Filtered("north");

        var keys = context.Model.FindEntityType(typeof(Invoice))!
            .GetDeclaredQueryFilters()
            .Select(filter => filter.Key)
            .ToList();

        keys.Should().BeEquivalentTo(
            ["soft-delete", "tenant"],
            "a specification can only opt out of a filter that has a name of its own");
    }

    [Fact]
    public async Task Both_filters_apply_when_nothing_opts_out()
    {
        await _database.SeedAsync();
        await using var context = _database.Filtered("north");

        var invoices = await _evaluator.Evaluate(context.Invoices, new EveryInvoice()).ToListAsync();

        invoices.Select(i => i.Customer).Should().Equal("Acme", "Bolt", "Cog");
    }

    [Fact]
    public async Task Opting_out_of_the_soft_delete_filter_leaves_the_tenant_filter_on()
    {
        await _database.SeedAsync();
        await using var context = _database.Filtered("north");

        var invoices = await _evaluator
            .Evaluate(context.Invoices, new InvoicesIncludingDeleted())
            .ToListAsync();

        invoices.Select(i => i.Customer).Should().Equal("Acme", "Bolt", "Cog", "Dial");
        invoices.Should().OnlyContain(i => i.Tenant == "north",
            "asking to see deleted rows says nothing about seeing another tenant's");
    }

    [Fact]
    public async Task Opting_out_of_the_tenant_filter_leaves_the_soft_delete_filter_on()
    {
        await _database.SeedAsync();
        await using var context = _database.Filtered("north");

        var invoices = await _evaluator
            .Evaluate(context.Invoices, new InvoicesAcrossTenants())
            .ToListAsync();

        invoices.Select(i => i.Customer).Should().Equal("Acme", "Bolt", "Cog", "Edge");
        invoices.Should().OnlyContain(i => !i.IsDeleted);
    }

    [Fact]
    public async Task A_filter_follows_a_value_that_changes_between_requests()
    {
        await _database.SeedAsync();

        // Two contexts of the same type, so both read the one model EF built and cached. If
        // the filter had kept the value it saw while that model was built, the second request
        // would be looking at the first request's tenant.
        await using (var first = _database.Filtered("north"))
        {
            var invoices = await _evaluator.Evaluate(first.Invoices, new EveryInvoice()).ToListAsync();
            invoices.Should().OnlyContain(i => i.Tenant == "north").And.NotBeEmpty();
        }

        await using (var second = _database.Filtered("south"))
        {
            var invoices = await _evaluator.Evaluate(second.Invoices, new EveryInvoice()).ToListAsync();
            invoices.Should().OnlyContain(i => i.Tenant == "south").And.NotBeEmpty();
        }
    }

    [Fact]
    public async Task A_filter_follows_a_value_that_changes_on_one_context()
    {
        await _database.SeedAsync();
        await using var context = _database.Filtered("north");

        (await _evaluator.Evaluate(context.Invoices, new EveryInvoice()).ToListAsync())
            .Should().OnlyContain(i => i.Tenant == "north").And.NotBeEmpty();

        context.Tenant = "south";

        // Same context, same compiled query, different answer: the value is a query parameter
        // read at execution, not a literal compiled into the SQL.
        (await _evaluator.Evaluate(context.Invoices, new EveryInvoice()).AsNoTracking().ToListAsync())
            .Should().OnlyContain(i => i.Tenant == "south").And.NotBeEmpty();
    }

    [Fact]
    public async Task A_filter_that_captures_its_value_freezes_it_which_is_why_Build_takes_the_context()
    {
        await _database.SeedAsync();

        await using (var first = _database.Captured("north"))
            (await first.Invoices.ToListAsync()).Should().OnlyContain(i => i.Tenant == "north");

        await using var second = _database.Captured("south");

        // The model was built once, while the first context said "north", and the value went
        // into it. This is the failure the abstraction's remarks describe, pinned down so a
        // change in EF that made it stop happening would be noticed.
        (await second.Invoices.AsNoTracking().ToListAsync())
            .Should().OnlyContain(i => i.Tenant == "north");
    }

    [Fact]
    public void A_model_convention_reaches_every_entity()
    {
        using var context = new ModelConventionContext(
            new DbContextOptionsBuilder<ModelConventionContext>().UseSqlite(_database.Connection).Options,
            [new EveryStringIsShort()],
            []);

        context.Model.FindEntityType(typeof(Invoice))!
            .FindProperty(nameof(Invoice.Customer))!.GetMaxLength().Should().Be(64);
    }

    [Fact]
    public void Two_conventions_claiming_one_key_on_one_entity_are_refused()
    {
        using var context = new DuplicateKeyContext(
            new DbContextOptionsBuilder<DuplicateKeyContext>().UseSqlite(_database.Connection).Options,
            [],
            [new SoftDeleteFilter(), new SoftDeleteFilter()]);

        var building = () => context.Model;

        building.Should().Throw<InvalidOperationException>()
            .WithMessage("*soft-delete*replace*");
    }
}

/// <summary>Its own type so its model is never shared with a context that has filters.</summary>
public sealed class ModelConventionContext(
    DbContextOptions<ModelConventionContext> options,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
    IEnumerable<IEntityFilterConvention> filterConventions)
    : ShopContext(options, modelConventions, filterConventions);

/// <summary>Its own type, because the model it tries to build never finishes.</summary>
public sealed class DuplicateKeyContext(
    DbContextOptions<DuplicateKeyContext> options,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
    IEnumerable<IEntityFilterConvention> filterConventions)
    : ShopContext(options, modelConventions, filterConventions);
