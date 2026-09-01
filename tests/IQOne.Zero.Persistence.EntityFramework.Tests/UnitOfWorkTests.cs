using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>The transaction boundary: who owns it, who joins it, and what disposal means.</summary>
public sealed class UnitOfWorkTests : IDisposable
{
    private readonly ShopDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static Invoice NewInvoice(string customer)
        => new() { Tenant = "north", Customer = customer, Total = 100, Due = new DateOnly(2026, 3, 1) };

    [Fact]
    public async Task Completing_commits()
    {
        await using var context = _database.Plain();
        var unitOfWork = new EfUnitOfWork(context);

        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            context.Invoices.Add(NewInvoice("Gear"));
            await unitOfWork.SaveChangesAsync();
            await transaction.CompleteAsync();
        }

        await using var after = _database.Plain();
        (await after.Invoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Disposing_without_completing_rolls_back()
    {
        await using var context = _database.Plain();
        var unitOfWork = new EfUnitOfWork(context);

        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            transaction.IsOwner.Should().BeTrue();

            context.Invoices.Add(NewInvoice("Gear"));
            await unitOfWork.SaveChangesAsync();

            // No CompleteAsync. This is the path an exception takes, and nothing on it has to
            // remember to say "undo".
        }

        unitOfWork.HasActiveTransaction.Should().BeFalse();

        await using var after = _database.Plain();
        (await after.Invoices.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_second_scope_joins_the_open_transaction_rather_than_nesting()
    {
        await using var context = _database.Plain();
        var unitOfWork = new EfUnitOfWork(context);

        await using var outer = await unitOfWork.BeginTransactionAsync();
        await using var inner = await unitOfWork.BeginTransactionAsync();

        outer.IsOwner.Should().BeTrue();
        inner.IsOwner.Should().BeFalse();

        await outer.CompleteAsync();
    }

    [Fact]
    public async Task A_joined_scope_does_not_commit_early()
    {
        await using var context = _database.Plain();
        var unitOfWork = new EfUnitOfWork(context);

        await using (var outer = await unitOfWork.BeginTransactionAsync())
        {
            context.Invoices.Add(NewInvoice("Gear"));
            await unitOfWork.SaveChangesAsync();

            await using (var inner = await unitOfWork.BeginTransactionAsync())
            {
                inner.IsOwner.Should().BeFalse();
                await inner.CompleteAsync();
            }

            // The inner scope said it was finished and the transaction is still open, still
            // the outer scope's to commit or undo.
            unitOfWork.HasActiveTransaction.Should().BeTrue();

            // The outer scope never completes.
        }

        await using var after = _database.Plain();
        (await after.Invoices.CountAsync()).Should().Be(0,
            "the joined scope's CompleteAsync must not have committed the outer scope's work");
    }

    [Fact]
    public async Task A_joined_scope_leaves_the_transaction_for_its_owner()
    {
        await using var context = _database.Plain();
        var unitOfWork = new EfUnitOfWork(context);

        await using var outer = await unitOfWork.BeginTransactionAsync();

        await using (var inner = await unitOfWork.BeginTransactionAsync())
        {
            // Disposed without completing. A joined scope rolling back here would tear the
            // transaction out from under the owner, which has not finished.
        }

        unitOfWork.HasActiveTransaction.Should().BeTrue();

        context.Invoices.Add(NewInvoice("Gear"));
        await unitOfWork.SaveChangesAsync();
        await outer.CompleteAsync();

        await using var after = _database.Plain();
        (await after.Invoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void Nothing_is_open_before_a_transaction_is_asked_for()
    {
        using var context = _database.Plain();

        new EfUnitOfWork(context).HasActiveTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_save()
    {
        await using var context = _database.Plain();
        var unitOfWork = new EfUnitOfWork(context);

        context.Invoices.Add(NewInvoice("Gear"));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var saving = () => unitOfWork.SaveChangesAsync(cancelled.Token);

        await saving.Should().ThrowAsync<OperationCanceledException>();
    }
}
