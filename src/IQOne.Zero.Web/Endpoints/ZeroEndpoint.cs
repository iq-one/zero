using IQOne.Zero.Messaging;
using IQOne.Zero.Web.Binding;
using IQOne.Zero.Web.Writing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Web;

/// <summary>
/// What a generated endpoint runs: bind, send, write.
/// </summary>
/// <remarks>
/// Public because generated code lives in the consumer's assembly. It is not meant to be
/// called by hand — the route attribute on the request is how an endpoint comes into being.
/// </remarks>
public static class ZeroEndpoint
{
    /// <summary>Binds the request, sends it through the pipeline, and writes the outcome.</summary>
    /// <typeparam name="TRequest">The request the route addresses.</typeparam>
    /// <typeparam name="TResponse">What handling it produces.</typeparam>
    /// <param name="context">The current call.</param>
    /// <returns>The response to write.</returns>
    public static async Task<IResult> RunAsync<TRequest, TResponse>(HttpContext context)
        where TRequest : IRequest<TResponse>
    {
        var services = context.RequestServices;
        var writer = services.GetRequiredService<IResponseWriter>();
        var cancellationToken = context.RequestAborted;

        object request;

        try
        {
            request = await services
                .GetRequiredService<IRequestBinder>()
                .BindAsync(context, typeof(TRequest), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UnsupportedMediaTypeException exception)
        {
            // 415 rather than 400, and the distinction is not cosmetic: it is the answer that
            // keeps a cross-origin form post from being executed as a command.
            return writer.Failure(
                context,
                [Error.Validation("request.media-type", exception.Message)],
                StatusCodes.Status415UnsupportedMediaType);
        }
        catch (RequestBodyTooLargeException exception)
        {
            return writer.Failure(
                context,
                [Error.Validation("request.too-large", exception.Message)],
                StatusCodes.Status413PayloadTooLarge);
        }
        catch (RequestBindingException exception)
        {
            // A body the caller cannot fix by retrying is theirs to correct, so it is a 400
            // rather than the 500 an unhandled deserialization failure would produce.
            return writer.Failure(
                context,
                [Error.Validation("request.unreadable", exception.Message)],
                StatusCodes.Status400BadRequest);
        }

        var result = await services
            .GetRequiredService<ISender>()
            .SendAsync((IRequest<TResponse>)request, cancellationToken)
            .ConfigureAwait(false);

        // The status is the writer's to choose here: these failures are the application's,
        // and how they are reported is the contract it keeps with its callers.
        if (result.IsFailure) return writer.Failure(context, result.Errors, null);

        // A command that produces nothing has nothing to serialise.
        return typeof(TResponse) == typeof(Unit)
            ? writer.Empty(context)
            : writer.Success(context, result.Value);
    }
}
