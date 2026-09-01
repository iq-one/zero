using IQOne.Zero.Web.Binding;
using Microsoft.AspNetCore.Http;

namespace IQOne.Zero.Web.Writing;

/// <summary>
/// Turns the outcome of a request into the HTTP response.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <see cref="IRequestBinder"/>, and it exists for the same reason that seam
/// does: the envelope, the status codes and the property names are the application's
/// contract with its callers, not the framework's. A team with a published API has to keep
/// answering tomorrow what it answered yesterday, and a framework that hardcodes a shape
/// leaves them forking it.
/// </para>
/// <para>
/// Register an implementation to replace the default, which writes JSON on success and an
/// RFC 7807 problem on failure. Registering before <c>AddZeroWeb</c> is enough — it only
/// fills in what nothing else has claimed.
/// </para>
/// </remarks>
public interface IResponseWriter
{
    /// <summary>The response for a request that produced a value.</summary>
    /// <typeparam name="TResponse">What handling the request produced.</typeparam>
    /// <param name="context">The current call.</param>
    /// <param name="value">What the handler produced.</param>
    /// <returns>The response to write.</returns>
    IResult Success<TResponse>(HttpContext context, TResponse value);

    /// <summary>The response for a request that produced nothing.</summary>
    /// <param name="context">The current call.</param>
    /// <returns>The response to write.</returns>
    IResult Empty(HttpContext context);

    /// <summary>The response for a request that failed.</summary>
    /// <param name="context">The current call.</param>
    /// <param name="errors">Every reason it failed. May be empty; a transport must not crash on that.</param>
    /// <param name="status">
    /// The status HTTP itself dictates — 415 for a body in the wrong media type, 413 for one
    /// too large — or <see langword="null"/> when the errors are what decide it.
    /// </param>
    /// <returns>The response to write.</returns>
    IResult Failure(HttpContext context, IReadOnlyList<Error> errors, int? status);
}
