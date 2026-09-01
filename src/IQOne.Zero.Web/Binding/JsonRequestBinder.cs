using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Web.Binding;

/// <summary>
/// Binds a request from the JSON body, then applies route and query values over it.
/// </summary>
/// <remarks>
/// <para>
/// Overlaying rather than merging at construction time is what makes records work: the
/// values are assembled as JSON and deserialized once, so a positional record with an
/// immutable constructor binds exactly like a mutable class.
/// </para>
/// <para>
/// Precedence is body, then query, then route — narrowest wins. A route value is part of
/// the URL the caller asked for, so it should not be contradicted by something in the body.
/// </para>
/// </remarks>
/// <param name="options">Serializer settings, shared with the response writer.</param>
public sealed class JsonRequestBinder(IOptions<ZeroWebOptions> options) : IRequestBinder
{
    private readonly JsonSerializerOptions _json = options.Value.SerializerOptions;

    /// <inheritdoc />
    public async ValueTask<object> BindAsync(
        HttpContext context, Type requestType, CancellationToken cancellationToken)
    {
        var node = await ReadBodyAsync(context, requestType, cancellationToken).ConfigureAwait(false);

        foreach (var (key, value) in context.Request.Query)
            node[key] = Scalar(value.Count == 1 ? value[0] : null, value);

        foreach (var (key, value) in context.Request.RouteValues)
            if (value is not null)
                node[key] = JsonValue.Create(value.ToString());

        try
        {
            return node.Deserialize(requestType, _json)
                   ?? throw new RequestBindingException(requestType, "the body produced no value");
        }
        catch (JsonException exception)
        {
            throw new RequestBindingException(requestType, exception.Message);
        }
    }

    /// <summary>
    /// Reads the body, treating "no body" and "malformed body" as different things.
    /// </summary>
    /// <remarks>
    /// The stream is copied first rather than parsed in place, because Content-Length is
    /// absent under chunked transfer encoding — which many clients use — and trusting it
    /// would silently skip the body instead of reading it. Copying also lets an empty body
    /// be told apart from broken JSON: one is a GET with no payload, the other is the
    /// caller's mistake and deserves a 400.
    /// </remarks>
    private static async ValueTask<JsonObject> ReadBodyAsync(
        HttpContext context, Type requestType, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is 0) return [];

        using var buffer = new MemoryStream();

        await context.Request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length == 0) return [];

        buffer.Position = 0;

        try
        {
            return await JsonNode.ParseAsync(buffer, cancellationToken: cancellationToken) as JsonObject
                   ?? throw new RequestBindingException(requestType, "the body must be a JSON object");
        }
        catch (JsonException exception)
        {
            throw new RequestBindingException(requestType, exception.Message);
        }
    }

    /// <summary>
    /// A repeated query parameter becomes an array so that a collection property binds; a
    /// single one stays a string so that a scalar property does.
    /// </summary>
    private static JsonNode? Scalar(string? single, IEnumerable<string?> all)
        => single is not null ? JsonValue.Create(single) : new JsonArray([.. all.Select(v => (JsonNode?)JsonValue.Create(v))]);
}
