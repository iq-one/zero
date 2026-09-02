using IQOne.Zero.Events;

namespace IQOne.Zero.Events.Tests;

internal sealed record InvoicePaid(int InvoiceId, decimal Amount) : IEvent;

internal sealed record NobodyCares : IEvent;

/// <summary>Notes that it ran, so ordering and completeness can be asserted.</summary>
internal sealed class Recording(List<string> log, string name) : IEventHandler<InvoicePaid>
{
    public Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
    {
        log.Add(name);
        return Task.FromResult(Result.Success());
    }
}

/// <summary>Reports a failure the way a subscriber that could not keep up would.</summary>
internal sealed class Failing(List<string> log) : IEventHandler<InvoicePaid>
{
    public static readonly Error Behind = Error.Unavailable("ledger.down", "The ledger is unreachable.");

    public Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
    {
        log.Add("failing");
        return Task.FromResult(Result.Failure(Behind));
    }
}

/// <summary>Throws, which is a bug rather than an expected failure.</summary>
internal sealed class Throwing(List<string> log) : IEventHandler<InvoicePaid>
{
    public Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
    {
        log.Add("throwing");
        throw new InvalidOperationException("a defect in a subscriber");
    }
}
