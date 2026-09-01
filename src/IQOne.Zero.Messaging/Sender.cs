using IQOne.Zero.Results;

namespace IQOne.Zero.Messaging;

/// <summary>Looks a request up in the frozen table and runs its pipeline.</summary>
/// <param name="registry">The frozen dispatch table.</param>
/// <param name="services">The scope handlers and behaviours resolve from.</param>
internal sealed class Sender(RequestRegistry registry, IServiceProvider services) : ISender
{
    /// <inheritdoc />
    public Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The concrete type, not TResponse: a request is dispatched by what it is, and the
        // caller may well be holding it through its interface.
        if (!registry.TryGet(request.GetType(), out var entry))
            throw new MissingRequestHandlerException(request.GetType());

        return (Task<Result<TResponse>>)entry.Invoke(services, request, cancellationToken);
    }
}
