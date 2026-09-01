using IQOne.Zero.Messaging;

namespace IQOne.Zero.Validation;

/// <summary>
/// Runs every validator registered for a request before the handler sees it.
/// </summary>
/// <remarks>
/// <para>
/// Placed at <see cref="BehaviorOrder.Validation"/>: after authorization, because there is
/// no point telling a caller what is wrong with a request they are not allowed to make; and
/// before caching and transactions, because an unacceptable request should reach neither.
/// </para>
/// <para>
/// Several validators may exist for one request — one from the module, one from a shared
/// package — and all of them run. Their failures are reported together.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request checked.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="validators">Every validator registered for this request.</param>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public int Order => BehaviorOrder.Validation;

    /// <inheritdoc />
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        List<Error>? errors = null;

        foreach (var validator in validators)
        {
            var found = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

            if (found.Count > 0) (errors ??= []).AddRange(found);
        }

        // Everything wrong at once: a caller correcting a form should not have to send it
        // again to discover the second mistake.
        return errors is null
            ? await next().ConfigureAwait(false)
            : Result<TResponse>.Failure(errors);
    }
}
