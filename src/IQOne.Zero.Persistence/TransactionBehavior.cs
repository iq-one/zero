using IQOne.Zero.Messaging;

namespace IQOne.Zero.Persistence;

/// <summary>
/// Wraps a command in a transaction and saves when it succeeds.
/// </summary>
/// <remarks>
/// <para>
/// Commands only. A query opens no transaction, because reads on a shared database pay for
/// one in lock contention and get nothing back for it.
/// </para>
/// <para>
/// A failed result rolls back exactly as an exception does. That is the point of returning
/// failures rather than throwing them: the two paths behave the same, so a handler can pick
/// whichever reads better without changing what happens to the data.
/// </para>
/// <para>
/// Placed innermost among the framework's behaviours — inside logging, authorization,
/// validation and caching — so a request that is going to be rejected never opens one.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request wrapped.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="unitOfWork">The boundary the changes succeed or fail within.</param>
public sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public int Order => BehaviorOrder.Transaction;

    /// <inheritdoc />
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ICommand and not ICommand<TResponse>) return await next().ConfigureAwait(false);

        await using var transaction = await unitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = await next().ConfigureAwait(false);

        if (result.IsFailure) return result;

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CompleteAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }
}
