using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace IQOne.Zero.Web.Binding;

/// <summary>
/// Fills a request object from the HTTP request.
/// </summary>
/// <remarks>
/// One rule, the same for every verb: the body is read first, then route and query values
/// are applied over it by name. Mixing ASP.NET's own binding sources instead — parameters
/// from the route, a separate type from the body — makes each endpoint read differently
/// depending on where its values happen to come from.
/// </remarks>
public interface IRequestBinder
{
    /// <summary>Builds the request for this call.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <param name="requestType">The concrete request type to produce.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bound request.</returns>
    /// <exception cref="UnsupportedMediaTypeException">The body is not in a media type the binder reads.</exception>
    /// <exception cref="RequestBodyTooLargeException">The body is larger than the binder accepts.</exception>
    /// <exception cref="RequestBindingException">The request could not be built.</exception>
    ValueTask<object> BindAsync(HttpContext context, Type requestType, CancellationToken cancellationToken);
}

/// <summary>
/// Thrown when the incoming call cannot be turned into the request it addresses.
/// </summary>
/// <remarks>
/// The base of everything binding can refuse, so a caller that only wants "the call was the
/// caller's mistake" catches one type. The derived kinds exist because HTTP answers them
/// with different statuses, and a transport that reported all three as 400 would tell a
/// caller to fix the body when the real problem was its media type or its size.
/// </remarks>
/// <param name="requestType">The request that could not be built.</param>
/// <param name="reason">What was wrong with the call.</param>
public class RequestBindingException(Type requestType, string reason)
    : Exception($"The call could not be read as {requestType.Name}: {reason}")
{
    /// <summary>The request that could not be built.</summary>
    public Type RequestType { get; } = requestType;
}

/// <summary>
/// Thrown when a call carries a body in a media type the binder does not read.
/// </summary>
/// <remarks>
/// Answered with 415 rather than 400, and that difference is a security boundary. A
/// cross-origin HTML form can post <c>text/plain</c>, <c>multipart/form-data</c> and
/// <c>application/x-www-form-urlencoded</c> with the victim's cookies and without a
/// preflight, and a form can be shaped so that its <c>text/plain</c> body parses as valid
/// JSON. It cannot send <c>application/json</c> without CORS approval. Refusing every other
/// media type is what keeps a state-changing endpoint from being driven by another site.
/// </remarks>
/// <param name="requestType">The request that could not be built.</param>
/// <param name="contentType">The media type the caller declared, if any.</param>
public sealed class UnsupportedMediaTypeException(Type requestType, string? contentType)
    : RequestBindingException(requestType, Describe(contentType))
{
    /// <summary>The media type the caller declared, or <see langword="null"/> when it declared none.</summary>
    public string? ContentType { get; } = contentType;

    private static string Describe(string? contentType)
        => string.IsNullOrEmpty(contentType)
            ? "the body must be JSON, and the call declared no media type."
            : $"the body must be JSON, and the call declared '{contentType}'.";
}

/// <summary>
/// Thrown when a call carries a body larger than the binder accepts.
/// </summary>
/// <remarks>
/// Answered with 413. The limit is the binder's own, deliberately well below the server's:
/// the binder holds the body in memory to overlay route and query values onto it, so the
/// server's allowance — 30 MB by default in Kestrel — is a per-request memory cost this
/// layer must not inherit by accident.
/// </remarks>
/// <param name="requestType">The request that could not be built.</param>
/// <param name="limit">The largest body the binder accepts, in bytes.</param>
public sealed class RequestBodyTooLargeException(Type requestType, long limit)
    : RequestBindingException(requestType, Describe(limit))
{
    /// <summary>The largest body the binder accepts, in bytes.</summary>
    public long Limit { get; } = limit;

    private static string Describe(long limit) => string.Format(
        CultureInfo.InvariantCulture,
        "the body is larger than the {0} bytes this application accepts.",
        limit);
}
