using IQOne.Zero.Messaging;
using IQOne.Zero.Persistence;

namespace IQOne.Zero.Persistence.Tests;

internal sealed record PlaceOrder(int Id) : ICommand<int>;

internal sealed record ReadOrder(int Id) : IQuery<int>;

/// <summary>Records what the behaviour asked of the unit of work, in order.</summary>
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public List<string> Log { get; } = [];

    public bool HasActiveTransaction { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Log.Add("save");
        return Task.FromResult(1);
    }

    public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        Log.Add("begin");
        HasActiveTransaction = true;

        return Task.FromResult<ITransaction>(new Transaction(this));
    }

    private sealed class Transaction(RecordingUnitOfWork owner) : ITransaction
    {
        private bool _completed;

        public bool IsOwner => true;

        public Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            _completed = true;
            owner.Log.Add("commit");

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            owner.Log.Add(_completed ? "dispose" : "rollback");
            owner.HasActiveTransaction = false;

            return ValueTask.CompletedTask;
        }
    }
}

public class TransactionBehaviorTests
{
    private static async Task<(RecordingUnitOfWork Work, Result<int> Result)> Run<TRequest>(
        TRequest request, Result<int> outcome)
        where TRequest : IRequest<int>
    {
        var work = new RecordingUnitOfWork();
        var behavior = new TransactionBehavior<TRequest, int>(work);

        var result = await behavior.HandleAsync(request, () => Task.FromResult(outcome), CancellationToken.None);

        return (work, result);
    }

    [Fact]
    public async Task A_successful_command_saves_and_commits()
    {
        var (work, result) = await Run(new PlaceOrder(1), Result<int>.Success(42));

        result.Value.Should().Be(42);
        work.Log.Should().Equal("begin", "save", "commit", "dispose");
    }

    [Fact]
    public async Task A_failed_command_rolls_back_and_never_saves()
    {
        var (work, result) = await Run(
            new PlaceOrder(1), Result<int>.Failure(Error.Conflict("order.taken", "Already placed.")));

        result.IsFailure.Should().BeTrue();
        work.Log.Should().Equal("begin", "rollback");
        work.Log.Should().NotContain("save",
            "a failed result must undo exactly as much as an exception would");
    }

    [Fact]
    public async Task A_query_opens_no_transaction_at_all()
    {
        var (work, result) = await Run(new ReadOrder(1), Result<int>.Success(7));

        result.Value.Should().Be(7);
        work.Log.Should().BeEmpty("reads pay for a transaction in lock contention and get nothing back");
    }

    [Fact]
    public void The_behaviour_sits_innermost_among_the_framework_s_own()
    {
        var behavior = new TransactionBehavior<PlaceOrder, int>(new RecordingUnitOfWork());

        behavior.Order.Should().Be(BehaviorOrder.Transaction);
        behavior.Order.Should().BeGreaterThan(BehaviorOrder.Caching,
            "a request that a cache or a validator would reject must never open a transaction");
    }
}
