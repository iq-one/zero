using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

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
/// <para>
/// A body is read only when it is declared as JSON. Anything else is 415, which is the same
/// answer ASP.NET's own body binding gives and the reason a cross-origin form cannot reach
/// a JSON endpoint.
/// </para>
/// </remarks>
public sealed class JsonRequestBinder : IRequestBinder
{
    /// <remarks>
    /// The overlay writes route and query values by name, and it must find the property the
    /// body already wrote under a different case rather than adding a second one beside it.
    /// A duplicate is only harmless while the serializer tolerates duplicates; an
    /// application that hardens by refusing them would get a 400 on every request that
    /// carries the same value in both places.
    /// </remarks>
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly JsonSerializerOptions _read;
    private readonly long _maxBodyBytes;

    /// <summary>Creates the binder.</summary>
    /// <param name="options">How Zero's endpoints read requests.</param>
    /// <param name="json">
    /// The application's JSON settings, configured with <c>ConfigureHttpJsonOptions</c> —
    /// the same ones the rest of its endpoints use.
    /// </param>
    public JsonRequestBinder(IOptions<ZeroWebOptions> options, IOptions<JsonOptions> json)
    {
        _read = ReadOptions(json.Value.SerializerOptions);
        _maxBodyBytes = options.Value.MaxBodyBytes;
    }

    /// <inheritdoc />
    public async ValueTask<object> BindAsync(
        HttpContext context, Type requestType, CancellationToken cancellationToken)
    {
        var request = context.Request;

        // Nothing to overlay and a body that is known to be there: hand the stream straight
        // to the serializer. That is the whole request in one pass, instead of a buffered
        // copy plus a DOM plus the object — three copies of a body that may be megabytes.
        // A length is required because without one an empty body cannot be told from a
        // malformed one without reading it, and that distinction is worth a buffer.
        if (request.Query.Count == 0 && request.RouteValues.Count == 0 && request.ContentLength > 0)
        {
            if (!IsJson(request.ContentType))
                throw new UnsupportedMediaTypeException(requestType, request.ContentType);

            return await ReadAsync(Capped(request, requestType), requestType, cancellationToken)
                .ConfigureAwait(false);
        }

        var node = await ReadBodyAsync(request, requestType, cancellationToken).ConfigureAwait(false);

        foreach (var (key, values) in request.Query)
            node[key] = FromQuery(requestType, key, values);

        foreach (var (key, value) in request.RouteValues)
            if (value is not null)
                node[key] = JsonValue.Create(Text(value));

        try
        {
            return node.Deserialize(requestType, _read)
                   ?? throw new RequestBindingException(requestType, "the body produced no value");
        }
        catch (JsonException exception)
        {
            throw new RequestBindingException(requestType, exception.Message);
        }
    }

    private async ValueTask<object> ReadAsync(
        Stream body, Type requestType, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync(body, requestType, _read, cancellationToken)
                       .ConfigureAwait(false)
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
    private async ValueTask<JsonObject> ReadBodyAsync(
        HttpRequest request, Type requestType, CancellationToken cancellationToken)
    {
        if (request.ContentLength is 0) return [];

        var declared = request.ContentType;
        var json = IsJson(declared);

        // Refused before it is buffered: bytes this binder will never parse are bytes it has
        // no business holding.
        if (!json && !string.IsNullOrEmpty(declared))
            throw new UnsupportedMediaTypeException(requestType, declared);

        using var buffer = new MemoryStream();

        await Capped(request, requestType).CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length == 0) return [];

        // A body arrived under no declared media type at all. Only now is it known to be a
        // body rather than a GET that mentioned nothing.
        if (!json) throw new UnsupportedMediaTypeException(requestType, declared);

        buffer.Position = 0;

        try
        {
            var parsed = await JsonNode
                             .ParseAsync(buffer, NodeOptions, cancellationToken: cancellationToken)
                             .ConfigureAwait(false) as JsonObject
                         ?? throw new RequestBindingException(requestType, "the body must be a JSON object");

            // The object's index is built on first use, so a body that spells the same
            // property two ways only collides when it is touched. Doing that here keeps the
            // collision inside the catch, where it is reported as the caller's mistake
            // rather than escaping as one of ours.
            _ = parsed.Count;

            return parsed;
        }
        catch (JsonException exception)
        {
            throw new RequestBindingException(requestType, exception.Message);
        }
        catch (ArgumentException)
        {
            throw new RequestBindingException(
                requestType, "the body names the same property more than once, ignoring case");
        }
    }

    /// <summary>Wraps the body in the binder's own size limit.</summary>
    private Stream Capped(HttpRequest request, Type requestType)
    {
        if (_maxBodyBytes <= 0) return request.Body;

        // A declared length over the limit is refused without reading a byte.
        if (request.ContentLength > _maxBodyBytes)
            throw new RequestBodyTooLargeException(requestType, _maxBodyBytes);

        return new CappedStream(request.Body, _maxBodyBytes, requestType);
    }

    /// <summary>
    /// Whether the caller declared a media type this binder reads.
    /// </summary>
    /// <remarks>
    /// <c>application/json</c> and any <c>+json</c> suffix, with or without parameters such
    /// as a charset. Everything else — including a body with no media type at all — is
    /// refused, because that refusal is what stops a cross-origin form post from being
    /// executed as a command.
    /// </remarks>
    private static bool IsJson(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var media)) return false;

        return (media.Type.Equals("application", StringComparison.OrdinalIgnoreCase)
                && media.SubType.Equals("json", StringComparison.OrdinalIgnoreCase))
               || media.Suffix.Equals("json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One query key, as the request wants to read it.
    /// </summary>
    /// <remarks>
    /// A repeated key is an array only where the request holds a collection. Everywhere else
    /// the last value wins, which is the convention every server and every query-string
    /// library already follows, and which keeps <c>?id=1&amp;id=2</c> from turning a scalar
    /// parameter into a 400.
    /// </remarks>
    private static JsonNode? FromQuery(Type requestType, string key, StringValues values)
    {
        if (RequestShape.BindsMany(requestType, key))
            return new JsonArray([.. values.Select(v => (JsonNode?)JsonValue.Create(v))]);

        return values.Count == 0 ? null : JsonValue.Create(values[^1]);
    }

    /// <summary>
    /// A route value as text, under the invariant culture.
    /// </summary>
    /// <remarks>
    /// Route values are usually strings, but they need not be: middleware and custom
    /// matchers put real values there. Formatting one under the ambient culture makes
    /// binding depend on the server's locale — under tr-TR a double becomes "1,5", which is
    /// not a number to any JSON reader — so the same request answers 200 on one deployment
    /// and 400 on another.
    /// </remarks>
    private static string? Text(object value)
        => value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// The application's JSON settings, plus what overlaying route and query values needs.
    /// </summary>
    /// <remarks>
    /// A route or query value can only be carried as a JSON string, so reading a number out
    /// of one — and matching <c>?includePaid=</c> to <c>IncludePaid</c> — are not style
    /// choices an application gets to turn off: without them a route parameter binds to
    /// zero and the caller is told nothing. They are forced on a copy, so that what the
    /// application configured still decides everything about the response, and the
    /// converters are appended so that one it registered itself still wins.
    /// </remarks>
    private static JsonSerializerOptions ReadOptions(JsonSerializerOptions application)
    {
        var read = new JsonSerializerOptions(application)
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = application.NumberHandling | JsonNumberHandling.AllowReadingFromString
        };

        read.Converters.Add(new TextBooleanConverter());
        read.Converters.Add(new JsonStringEnumConverter());

        return read;
    }
}
