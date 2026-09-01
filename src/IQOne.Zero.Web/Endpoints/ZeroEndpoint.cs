using IQOne.Zero.Messaging;
using IQOne.Zero;
using IQOne.Zero.Web.Binding;
// Aliased because Zero's own Results namespace would otherwise shadow this type. The
// framework's Result and Error live in IQOne.Zero for exactly that reason; this file is
// the one place that still needs both.
using HttpResults = Microsoft.AspNetCore.Http.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
        var options = services.GetRequiredService<IOptions<ZeroWebOptions>>().Value;
        var cancellationToken = context.RequestAborted;

        object request;

        try
        {
            request = await services
                .GetRequiredService<IRequestBinder>()
                .BindAsync(context, typeof(TRequest), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestBindingException exception)
        {
            // A body the caller cannot fix by retrying is theirs to correct, so it is a 400
            // rather than the 500 an unhandled deserialization failure would produce.
            return Problem(
                options,
                StatusCodes.Status400BadRequest,
                [Error.Validation("request.unreadable", exception.Message)],
                context);
        }

        var result = await services
            .GetRequiredService<ISender>()
            .SendAsync((IRequest<TResponse>)request, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure) return Problem(options, StatusFor(options, result.Errors), result.Errors, context);

        // A command that produces nothing has nothing to serialise; 204 says so precisely.
        return typeof(TResponse) == typeof(Unit)
            ? HttpResults.StatusCode(options.EmptySuccessStatusCode)
            : HttpResults.Json(result.Value, options.SerializerOptions);
    }

    /// <summary>
    /// The status for a set of failures: the most specific one wins.
    /// </summary>
    /// <remarks>
    /// Several errors of different kinds usually means validation collected them, so
    /// validation is reported. A single error reports its own kind.
    /// </remarks>
    private static int StatusFor(ZeroWebOptions options, ErrorList errors)
    {
        var kinds = errors.Select(e => e.Kind).Distinct().ToArray();

        var kind = kinds.Length == 1
            ? kinds[0]
            : kinds.Contains(ErrorKind.Validation) ? ErrorKind.Validation : kinds[0];

        return options.StatusCodeByKind.TryGetValue(kind, out var status)
            ? status
            : StatusCodes.Status500InternalServerError;
    }

    private static IResult Problem(ZeroWebOptions options, int status, IEnumerable<Error> errors, HttpContext context)
        => HttpResults.Problem(
            statusCode: status,
            title: TitleFor(status),
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = context.TraceIdentifier,
                ["errors"] = errors.Select(e => new
                {
                    code = e.Code,
                    message = options.IncludeErrorMessages ? e.Message : null,
                    kind = e.Kind.ToString()
                })
            });

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "The request was not acceptable.",
        StatusCodes.Status401Unauthorized => "The caller could not be identified.",
        StatusCodes.Status403Forbidden => "The caller is not permitted to do this.",
        StatusCodes.Status404NotFound => "What was asked for does not exist.",
        StatusCodes.Status409Conflict => "The current state does not allow this.",
        StatusCodes.Status503ServiceUnavailable => "A dependency is unavailable.",
        _ => "The request failed."
    };
}
