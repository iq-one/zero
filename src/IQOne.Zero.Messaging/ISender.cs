using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.Results;

namespace IQOne.Zero.Messaging;

/// <summary>
/// Sends a request through the pipeline to its handler.
/// </summary>
/// <remarks>
/// Callers depend on this rather than on a handler, so a caller cannot skip the pipeline —
/// which would skip validation and authorization with it. The lookup is a dictionary read
/// against a table built at compile time, not reflection over the request type.
/// </remarks>
public interface ISender : IScoped
{
    /// <summary>Sends a request and returns what its handler produced.</summary>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="request">What is being asked for.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="MissingRequestHandlerException">No handler is registered for the request.</exception>
    Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when a request reaches the sender with no handler registered for it.
/// </summary>
/// <remarks>
/// This should never be seen at runtime: <c>AddZeroMessaging</c> checks the table at startup
/// and refuses to start when a request has no handler. Seeing it means a request type was
/// created after that check — by reflection, or in an assembly loaded late.
/// </remarks>
/// <param name="requestType">The request that could not be dispatched.</param>
public sealed class MissingRequestHandlerException(Type requestType)
    : InvalidOperationException(
        $"No handler is registered for '{requestType.FullName}'. " +
        "Implement IRequestHandler<,> for it in a module the application references.")
{
    /// <summary>The request that could not be dispatched.</summary>
    public Type RequestType { get; } = requestType;
}
