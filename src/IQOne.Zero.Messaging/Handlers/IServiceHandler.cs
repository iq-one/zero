namespace IQOne.Zero.Messaging.Handlers;

/// <summary>Marker used by the dispatch generator to discover handlers.</summary>
public interface IServiceHandler;

/// <summary>
/// Handles one service method and returns the payload only.
/// </summary>
/// <remarks>
/// A handler never builds a response envelope. It returns what it computed, and the
/// transport wraps it — so the same handler serves an application whose wire format the
/// framework has never heard of. Signal an expected failure by throwing
/// <see cref="Exceptions.ServiceException"/> or a derived type.
/// </remarks>
/// <typeparam name="TRequest">The request this handler accepts.</typeparam>
/// <typeparam name="TResponseModel">The payload it returns.</typeparam>
public interface IServiceHandler<in TRequest, TResponseModel> : IServiceHandler
    where TRequest : ServiceRequest
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">The deserialized request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The payload, without an envelope.</returns>
    Task<TResponseModel> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
