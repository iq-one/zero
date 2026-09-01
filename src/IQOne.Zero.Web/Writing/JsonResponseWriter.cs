using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
// Aliased because Zero's own Results namespace would otherwise shadow this type. The
// framework's Result and Error live in IQOne.Zero for exactly that reason; this file is
// the one place that still needs both.
using HttpResults = Microsoft.AspNetCore.Http.Results;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace IQOne.Zero.Web.Writing;

/// <summary>
/// Writes JSON on success and an RFC 7807 problem on failure.
/// </summary>
/// <remarks>
/// <para>
/// This is what Zero answers with until an application says otherwise. Everything about the
/// shape — the <c>errors</c> array, its property names, the <c>traceId</c>, the titles, the
/// status chosen for each error kind — is this class's opinion and not the framework's.
/// Nothing else in Zero reads it, so replacing the whole writer changes the wire contract
/// and breaks nothing else.
/// </para>
/// <para>
/// It serializes with the application's own <c>ConfigureHttpJsonOptions</c> settings, so a
/// naming policy or converter set once applies to Zero's endpoints and to everything else
/// the application maps.
/// </para>
/// </remarks>
/// <param name="options">Status codes and how much of a failure reaches the caller.</param>
/// <param name="json">The application's JSON settings.</param>
public sealed class JsonResponseWriter(IOptions<ZeroWebOptions> options, IOptions<JsonOptions> json)
    : IResponseWriter
{
    private readonly ZeroWebOptions _options = options.Value;
    private readonly JsonSerializerOptions _json = json.Value.SerializerOptions;

    /// <inheritdoc />
    public IResult Success<TResponse>(HttpContext context, TResponse value)
        => HttpResults.Json(value, _json);

    /// <inheritdoc />
    public IResult Empty(HttpContext context) => HttpResults.StatusCode(_options.EmptySuccessStatusCode);

    /// <inheritdoc />
    public IResult Failure(HttpContext context, IReadOnlyList<Error> errors, int? status)
    {
        var code = status ?? StatusFor(errors);

        return HttpResults.Problem(
            statusCode: code,
            title: TitleFor(code),
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = context.TraceIdentifier,
                ["errors"] = errors.Select(e => new
                {
                    code = e.Code,
                    message = _options.IncludeErrorMessages ? e.Message : null,
                    kind = e.Kind.ToString()
                }).ToArray()
            });
    }

    /// <summary>
    /// The status for a set of failures: the most specific one wins.
    /// </summary>
    /// <remarks>
    /// Several errors of different kinds usually means validation collected them, so
    /// validation is reported. A single error reports its own kind. An empty list should not
    /// arrive — a failed <see cref="Result"/> always carries a reason — but this is a
    /// transport, and a transport that indexes into whatever it is handed answers a stack
    /// trace where it owed a status.
    /// </remarks>
    private int StatusFor(IReadOnlyList<Error> errors)
    {
        var kinds = errors.Select(e => e.Kind).Distinct().ToArray();

        var kind = kinds.Length switch
        {
            0 => ErrorKind.Failure,
            1 => kinds[0],
            _ => kinds.Contains(ErrorKind.Validation) ? ErrorKind.Validation : kinds[0]
        };

        return _options.StatusCodeByKind.TryGetValue(kind, out var status)
            ? status
            : StatusCodes.Status500InternalServerError;
    }

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "The request was not acceptable.",
        StatusCodes.Status401Unauthorized => "The caller could not be identified.",
        StatusCodes.Status403Forbidden => "The caller is not permitted to do this.",
        StatusCodes.Status404NotFound => "What was asked for does not exist.",
        StatusCodes.Status409Conflict => "The current state does not allow this.",
        StatusCodes.Status413PayloadTooLarge => "The request body is too large.",
        StatusCodes.Status415UnsupportedMediaType => "The request body is not in a format this endpoint reads.",
        StatusCodes.Status503ServiceUnavailable => "A dependency is unavailable.",
        _ => "The request failed."
    };
}
