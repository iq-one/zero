using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;

namespace IQOne.Zero.Web;

/// <summary>
/// One endpoint, as the generator described it.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Pattern">The route pattern, before any prefix.</param>
/// <param name="Name">Name used in link generation and OpenAPI.</param>
/// <param name="Tag">OpenAPI grouping, or null.</param>
/// <param name="Policy">Authorization policy, or null to use the application's default.</param>
/// <param name="AllowAnonymous">Whether the endpoint may be reached unauthenticated.</param>
/// <param name="RequestType">The request the route addresses.</param>
/// <param name="ResponseType">What handling it produces.</param>
/// <param name="Handler">Binds, sends and writes.</param>
public sealed record ZeroEndpointDescriptor(
    string Method,
    string Pattern,
    string Name,
    string? Tag,
    string? Policy,
    bool AllowAnonymous,
    Type RequestType,
    Type ResponseType,
    Func<HttpContext, Task<IResult>> Handler);

/// <summary>Collects endpoints while modules are being configured.</summary>
public interface IEndpointRegistryBuilder
{
    /// <summary>Adds one endpoint.</summary>
    /// <param name="endpoint">The endpoint to add.</param>
    /// <exception cref="InvalidOperationException">The method and pattern are already taken.</exception>
    void Add(ZeroEndpointDescriptor endpoint);
}

/// <summary>The endpoints of an application: filled while modules configure, then frozen.</summary>
public sealed class EndpointRegistry : IEndpointRegistryBuilder
{
    private readonly Dictionary<(string Method, string Pattern), ZeroEndpointDescriptor> _endpoints =
        new(RouteComparer.Instance);

    private FrozenSet<ZeroEndpointDescriptor>? _frozen;

    /// <inheritdoc />
    public void Add(ZeroEndpointDescriptor endpoint)
    {
        if (_frozen is not null)
            throw new InvalidOperationException(
                $"The endpoint table is frozen; '{endpoint.Method} {endpoint.Pattern}' cannot be added.");

        var key = (endpoint.Method, endpoint.Pattern);

        if (!_endpoints.TryAdd(key, endpoint))
            throw new InvalidOperationException(
                $"'{endpoint.Method} {endpoint.Pattern}' is claimed by both " +
                $"'{_endpoints[key].RequestType.Name}' and '{endpoint.RequestType.Name}'. " +
                "A route belongs to one request.");
    }

    /// <summary>Seals the table. Called once, after every module has been configured.</summary>
    /// <returns>This instance.</returns>
    public EndpointRegistry Freeze()
    {
        _frozen = _endpoints.Values.ToFrozenSet();
        return this;
    }

    /// <summary>Every endpoint, in a stable order.</summary>
    public IReadOnlyCollection<ZeroEndpointDescriptor> Endpoints =>
        [.. _endpoints.Values
            .OrderBy(e => e.Pattern, StringComparer.Ordinal)
            .ThenBy(e => e.Method, StringComparer.Ordinal)];

    /// <summary>A route is the same route whatever case it was written in.</summary>
    private sealed class RouteComparer : IEqualityComparer<(string Method, string Pattern)>
    {
        public static readonly RouteComparer Instance = new();

        public bool Equals((string Method, string Pattern) x, (string Method, string Pattern) y)
            => string.Equals(x.Method, y.Method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Pattern, y.Pattern, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Method, string Pattern) obj)
            => HashCode.Combine(obj.Method.ToUpperInvariant(), obj.Pattern.ToLowerInvariant());
    }
}
