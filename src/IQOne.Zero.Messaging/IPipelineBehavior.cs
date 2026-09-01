using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero;

namespace IQOne.Zero.Messaging;

/// <summary>Calls the rest of the pipeline, ending at the handler.</summary>
/// <typeparam name="TResponse">What handling produces.</typeparam>
/// <returns>The outcome from further down the pipeline.</returns>
public delegate Task<Result<TResponse>> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Wraps every request of a given shape.
/// </summary>
/// <remarks>
/// <para>
/// This is where the concerns that apply to all requests live: validation, authorization,
/// logging, caching, transactions, retries. Writing them here rather than in handlers is the
/// difference between one implementation and one per handler.
/// </para>
/// <para>
/// A behaviour may return a failure without calling <c>next</c> — that is how validation
/// stops a request. It may also inspect the outcome on the way back out.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request shape wrapped. Use an open generic to wrap everything.</typeparam>
/// <typeparam name="TResponse">What handling produces.</typeparam>
public interface IPipelineBehavior<in TRequest, TResponse> : IScoped
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Ascending; lower runs further out. Behaviours with equal order run in registration
    /// order, which is not something to rely on.
    /// </summary>
    /// <remarks>
    /// Order is stated rather than inferred because it is load-bearing: authorization must
    /// run before anything that reads data, and a transaction must open inside logging, not
    /// outside it.
    /// </remarks>
    int Order => 0;

    /// <summary>Wraps the rest of the pipeline.</summary>
    /// <param name="request">What was asked for.</param>
    /// <param name="next">Calls the rest of the pipeline. May be skipped to short-circuit.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome.</returns>
    Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>Well-known positions, so behaviours from different packages compose predictably.</summary>
/// <remarks>
/// Leaving gaps between them is deliberate: an application can slot its own behaviour
/// between two framework ones without renumbering anything.
/// </remarks>
public static class BehaviorOrder
{
    /// <summary>Outermost. Observes everything, including failures raised by everything else.</summary>
    public const int Logging = -1000;

    /// <summary>Rejects a caller who may not make this request, before any work is done.</summary>
    public const int Authorization = -800;

    /// <summary>Rejects a request that is not acceptable, before any work is done.</summary>
    public const int Validation = -600;

    /// <summary>Returns a stored answer without reaching the handler.</summary>
    public const int Caching = -400;

    /// <summary>Opens a transaction around the handler, inside everything that may reject.</summary>
    public const int Transaction = -200;
}
