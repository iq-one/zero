using Microsoft.AspNetCore.Http;

namespace IQOne.Zero.Web;

/// <summary>
/// How Zero's endpoints read requests and write responses.
/// </summary>
/// <remarks>
/// Serialization is deliberately absent. JSON is the default binder's and the default
/// writer's business, and both read the application's own <c>ConfigureHttpJsonOptions</c>
/// settings — the same ones every other endpoint in the application already uses. A package
/// not named for a serializer does not put one on its options surface.
/// </remarks>
public sealed class ZeroWebOptions
{
    /// <summary>Prefix applied to every generated route, for example <c>/api</c>.</summary>
    public string RoutePrefix { get; set; } = string.Empty;

    /// <summary>
    /// The largest request body the binder will read, in bytes. Zero or less removes the
    /// limit and leaves only the server's.
    /// </summary>
    /// <remarks>
    /// One mebibyte, deliberately far below Kestrel's 30 MB: a command is not an upload, and
    /// the binder holds the body in memory to overlay route and query values onto it. Raise
    /// it for an endpoint that genuinely carries a large document, and prefer a route that
    /// streams for anything that carries a file.
    /// </remarks>
    public long MaxBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Whether an endpoint that names no policy and is not marked anonymous requires an
    /// authenticated caller.
    /// </summary>
    /// <remarks>
    /// On, because the alternative is that forgetting to write <c>Policy</c> publishes the
    /// endpoint, and nothing about the code says so. The mistake this prevents is silent in
    /// exactly the way the framework's analyzers exist to catch; the fix costs an
    /// <c>AllowAnonymous</c> on the endpoints that really are open. Turn it off only in an
    /// application that has no authentication at all — with it on, an endpoint that reaches
    /// a pipeline with no authorization middleware fails loudly rather than serving.
    /// </remarks>
    public bool RequireAuthorizationByDefault { get; set; } = true;


    /// <summary>
    /// The status code each failure kind is reported with.
    /// </summary>
    /// <remarks>
    /// Kept here rather than on <see cref="ErrorKind"/> because it is a transport decision:
    /// the same failure is a 404 over HTTP and something else entirely on a queue. Change an
    /// entry when a published contract requires it. Read by the default response writer; an
    /// application that replaces the writer decides its statuses there instead.
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
