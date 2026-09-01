using System.Text.Json;
using IQOne.Zero;
using Microsoft.AspNetCore.Http;

namespace IQOne.Zero.Web;

/// <summary>How Zero's endpoints read requests and write responses.</summary>
public sealed class ZeroWebOptions
{
    /// <summary>Prefix applied to every generated route, for example <c>/api</c>.</summary>
    public string RoutePrefix { get; set; } = string.Empty;

    /// <summary>Serializer settings, shared by the binder and the response writer.</summary>
    public JsonSerializerOptions SerializerOptions { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The status code each failure kind is reported with.
    /// </summary>
    /// <remarks>
    /// Kept here rather than on <see cref="ErrorKind"/> because it is a transport decision:
    /// the same failure is a 404 over HTTP and something else entirely on a queue. Change an
    /// entry when a published contract requires it.
    /// </remarks>
    public IDictionary<ErrorKind, int> StatusCodeByKind { get; } = new Dictionary<ErrorKind, int>
    {
        [ErrorKind.Failure] = StatusCodes.Status500InternalServerError,
        [ErrorKind.Validation] = StatusCodes.Status400BadRequest,
        [ErrorKind.NotFound] = StatusCodes.Status404NotFound,
        [ErrorKind.Conflict] = StatusCodes.Status409Conflict,
        [ErrorKind.Unauthorized] = StatusCodes.Status401Unauthorized,
        [ErrorKind.Forbidden] = StatusCodes.Status403Forbidden,
        [ErrorKind.Unavailable] = StatusCodes.Status503ServiceUnavailable
    };

    /// <summary>The status code a command with nothing to return is reported with.</summary>
    public int EmptySuccessStatusCode { get; set; } = StatusCodes.Status204NoContent;

    /// <summary>
    /// Whether a failed result's <see cref="Error.Message"/> reaches the caller.
    /// </summary>
    /// <remarks>
    /// On, because these messages are written for the caller by definition: an expected
    /// failure is one the caller is meant to act on. Turn it off for a public surface where
    /// even that much is more than you want to say.
    /// </remarks>
    public bool IncludeErrorMessages { get; set; } = true;
}
