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
    /// <exception cref="RequestBindingException">The request could not be built.</exception>
    ValueTask<object> BindAsync(HttpContext context, Type requestType, CancellationToken cancellationToken);
}

/// <summary>Thrown when the incoming call cannot be turned into the request it addresses.</summary>
/// <param name="requestType">The request that could not be built.</param>
/// <param name="reason">What was wrong with the call.</param>
public sealed class RequestBindingException(Type requestType, string reason)
    : Exception($"The call could not be read as {requestType.Name}: {reason}")
{
    /// <summary>The request that could not be built.</summary>
    public Type RequestType { get; } = requestType;
}
