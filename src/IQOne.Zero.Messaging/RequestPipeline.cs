using IQOne.Zero.Results;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Messaging;

/// <summary>
/// Runs a request through its behaviours and then its handler.
/// </summary>
/// <remarks>
/// Called from generated code, which supplies both type arguments, so nothing here is
/// resolved by reflection. It is public because generated code lives in the consumer's
/// assembly; it is not meant to be called by hand.
/// </remarks>
public static class RequestPipeline
{
    /// <summary>Builds the pipeline for one request and runs it.</summary>
    /// <typeparam name="TRequest">The request's concrete type.</typeparam>
    /// <typeparam name="TResponse">What handling it produces.</typeparam>
    /// <param name="request">What is being asked for.</param>
    /// <param name="services">The scope the handler and behaviours resolve from.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome.</returns>
    public static Task<Result<TResponse>> RunAsync<TRequest, TResponse>(
        TRequest request, IServiceProvider services, CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        var handler = services.GetRequiredService<IRequestHandler<TRequest, TResponse>>();

        var behaviors = services
            .GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .OrderBy(b => b.Order)
            .ToArray();

        RequestHandlerDelegate<TResponse> next = () => handler.HandleAsync(request, cancellationToken);

        // Wrapped from the inside out, so the lowest Order ends up outermost.
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var downstream = next;

            next = () => behavior.HandleAsync(request, downstream, cancellationToken);
        }

        return next();
    }
}
