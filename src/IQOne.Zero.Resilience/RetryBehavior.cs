using IQOne.Zero.Messaging;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Resilience;

/// <summary>
/// Hands a request back to the pipeline when it failed for a reason another attempt could
/// change, and only when trying again is safe.
/// </summary>
/// <remarks>
/// <para>
/// Retrying on a <see cref="Result{TValue}"/> rather than on an exception is the whole point
/// of this behaviour. A Zero operation reports an expected failure by returning a value, so
/// a retry policy written against throws — which is every general-purpose one, Polly's
/// included by default — never fires on it: the handler returns
/// <see cref="ErrorKind.Unavailable"/>, nothing is thrown, and the policy records a success.
/// This one reads the reason and decides.
/// </para>
/// <para>
/// It does not catch. An exception out of a handler is the failure nobody planned for, and
/// repeating it is how a bug becomes three bugs' worth of load. Retrying what the handler
/// deliberately reported and passing through what it did not is the same distinction
/// <see cref="Result{TValue}"/> itself makes.
/// </para>
/// <para>
/// Placed at <see cref="ResilienceOrder.Retry"/>: outside the transaction, so every attempt
/// opens its own; inside caching, validation and authorization, so a stored answer is not
/// retried and a refusal is issued once.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request wrapped.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="options">How many attempts, how long between them, and for which failures.</param>
/// <param name="brake">Stops retrying a request type that is failing outright.</param>
/// <param name="time">Waits between attempts, so a test can state the waits instead of taking them.</param>
public sealed class RetryBehavior<TRequest, TResponse>(
    IOptions<ResilienceOptions> options, IRetryBrake brake, TimeProvider time)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public int Order => ResilienceOrder.Retry;

    /// <inheritdoc />
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.Enabled || settings.MaxAttempts <= 1 || !MayBeRetried(request))
            return await next().ConfigureAwait(false);

        for (var attempt = 1; ; attempt++)
        {
            var result = await next().ConfigureAwait(false);

            if (result.IsSuccess)
            {
                brake.Succeeded(typeof(TRequest));

                return result;
            }

            // Not our failure to touch: the handler said something another attempt cannot
            // change, and the caller gets it now rather than three waits from now.
            if (!WorthRetrying(result.Errors, settings.RetryOn)) return result;

            if (attempt >= settings.MaxAttempts || !brake.AllowsRetry(typeof(TRequest)))
            {
                brake.Exhausted(typeof(TRequest));

                return result;
            }

            await Task
                .Delay(Backoff.Delay(settings, attempt), time, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Whether trying this request again is safe.</summary>
    /// <remarks>
    /// A query is safe by definition — that is what makes it a query rather than a command.
    /// Anything else has to say so, because the failure mode of guessing wrong is not a
    /// wasted call: it is the work done twice. A request that is neither a query nor a
    /// command is treated as a command, since an unstated intention is not a promise.
    /// </remarks>
    /// <param name="request">What was asked for.</param>
    /// <returns><see langword="true"/> when the request may be handled more than once.</returns>
    private static bool MayBeRetried(TRequest request) => request is IQuery<TResponse> or IIdempotent;

    /// <summary>Whether every reason the request failed is one another attempt could change.</summary>
    /// <remarks>
    /// Every reason, not the first. A failure that names something retrying cannot fix is a
    /// failure retrying cannot fix, however many of its other reasons are transient.
    /// </remarks>
    /// <param name="errors">Why the request failed.</param>
    /// <param name="kinds">The kinds this deployment retries.</param>
    /// <returns><see langword="true"/> when another attempt is worth making.</returns>
    private static bool WorthRetrying(ErrorList errors, ISet<ErrorKind> kinds)
    {
        // Indexed rather than enumerated: this runs on every failed request, and ErrorList's
        // enumerator is reached through an interface, which boxes it.
        for (var i = 0; i < errors.Count; i++)
            if (!kinds.Contains(errors[i].Kind))
                return false;

        return errors.Count > 0;
    }
}
