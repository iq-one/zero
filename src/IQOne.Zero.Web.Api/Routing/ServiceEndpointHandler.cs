using IQOne.Zero.Messaging;
using IQOne.Zero.Messaging.Dispatch;
using IQOne.Zero.Messaging.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IQOne.Zero.Web.Api.Routing;

/// <summary>Request delegate for a generated endpoint. Routing has already selected the entry.</summary>
internal static class ServiceEndpointHandler
{
    public static async Task HandleAsync(HttpContext context, ServiceEntry entry)
    {
        var cancellationToken = context.RequestAborted;

        ServiceResponse response;

        try
        {
            var body = await ReadBodyAsync(context, cancellationToken).ConfigureAwait(false);

            response = await entry
                .Invoke(context.RequestServices, body, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected; do not attempt to write a body.
            context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return;
        }
        catch (ServiceException exception)
        {
            response = new ServiceResponse
            {
                Messages = [new KeyValuePairModel(exception.GetType().Name, exception.Message)]
            };
        }

        response.RequestId = context.TraceIdentifier;

        context.Response.StatusCode = response.StatusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        // Newtonsoft is deliberate: System.Text.Json differs in null handling, date
        // formatting and property order, which would silently change the contract.
        await context.Response
            .WriteAsync(JsonConvert.SerializeObject(response), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<JObject> ReadBodyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is null or 0) return [];

        using var reader = new StreamReader(context.Request.Body);
        var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(text) ? [] : JObject.Parse(text);
    }
}
