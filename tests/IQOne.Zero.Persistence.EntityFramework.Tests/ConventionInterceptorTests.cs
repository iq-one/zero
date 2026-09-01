using IQOne.Zero.Persistence.Conventions;
using IQOne.Zero.Persistence.EntityFramework.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>The two things that happen between a save and the write reaching the table.</summary>
public sealed class ConventionInterceptorTests : IDisposable
{
    private readonly ShopDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static WriteOwnershipInterceptor Ownership(params IWriteOwnership[] declarations) => new(declarations);

    private static SaveChangesConventionInterceptor Conventions(params ISaveChangesConvention<DbContext>[] conventions)
        => new(conventions);

    [Fact]
    public async Task A_write_to_a_table_this_deployment_does_not_own_is_refused()
    {
        await using var context = _database.Plain(Ownership(new InvoicesOnly()));

        context.Ledger.Add(new LedgerEntry { Note = "posted" });

        var saving = () => context.SaveChangesAsync();

        var thrown = await saving.Should().ThrowAsync<WriteOwnershipViolationException>();

        thrown.Which.Table.Should().Be("ledger");
        thrown.Which.Operation.Should().Be("Insert");

        await using var after = _database.Plain();
        (await after.Ledger.CountAsync()).Should().Be(0, "the refusal happens before the write");
    }

    [Fact]
    public async Task An_update_and_a_delete_are_refused_by_name()
    {
        await using (var seed = _database.Plain())
        {
            seed.Ledger.Add(new LedgerEntry { Note = "posted" });
            await seed.SaveChangesAsync();
        }

        await using var context = _database.Plain(Ownership(new InvoicesOnly()));

        var entry = await context.Ledger.SingleAsync();
        var saving = () => context.SaveChangesAsync();

        entry.Note = "changed";

        (await saving.Should().ThrowAsync<WriteOwnershipViolationException>())
            .Which.Operation.Should().Be("Update");

        context.Entry(entry).State = EntityState.Deleted;

        (await saving.Should().ThrowAsync<WriteOwnershipViolationException>())
            .Which.Operation.Should().Be("Delete");
    }

    [Fact]
    public async Task A_write_to_an_owned_table_goes_through()
    {
        await using var context = _database.Plain(Ownership(new InvoicesOnly()));

        context.Invoices.Add(new Invoice { Tenant = "north", Customer = "Gear", Total = 100 });

        await context.SaveChangesAsync();

        await using var after = _database.Plain();
        (await after.Invoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Declaring_no_ownership_permits_every_write()
    {
        await using var context = _database.Plain(Ownership());

        context.Ledger.Add(new LedgerEntry { Note = "posted" });

        await context.SaveChangesAsync();

        await using var after = _database.Plain();
        (await after.Ledger.CountAsync()).Should().Be(1,
            "an application that owns its whole database should not have to say so");
    }

    [Fact]
    public async Task A_save_changes_convention_runs_before_the_write()
    {
        await _database.SeedAsync();

        await using (var context = _database.Plain(Conventions(new SoftDeleteWrites())))
        {
            context.Invoices.Remove(await context.Invoices.SingleAsync(i => i.Customer == "Cog"));

            await context.SaveChangesAsync();
        }

        await using var after = _database.Plain();
        var cog = await after.Invoices.SingleAsync(i => i.Customer == "Cog");

        cog.IsDeleted.Should().BeTrue("the delete was turned into an update on the way to the table");
    }

    [Fact]
    public async Task The_conventions_run_before_the_ownership_check_sees_the_operation()
    {
        await _database.SeedAsync();

        // Ownership that permits updates to invoices but nothing else. A soft delete is an
        // update by the time it reaches the table; if the check ran first it would see a
        // delete instead and the outcome would depend on the order, not on the rule.
        await using var context = _database.Plain(
            Conventions(new SoftDeleteWrites()),
            Ownership(new InvoicesOnly()));

        context.Invoices.Remove(await context.Invoices.SingleAsync(i => i.Customer == "Cog"));

        await context.SaveChangesAsync();

        await using var after = _database.Plain();
        (await after.Invoices.SingleAsync(i => i.Customer == "Cog")).IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task A_convention_can_stamp_a_new_row()
    {
        await using var context = _database.Plain(Conventions(new StampTenantOnWrite()));

        context.Tenant = "south";
        context.Invoices.Add(new Invoice { Customer = "Gear", Total = 100 });

        await context.SaveChangesAsync();

        await using var after = _database.Plain();
        (await after.Invoices.SingleAsync()).Tenant.Should().Be("south");
    }
}
