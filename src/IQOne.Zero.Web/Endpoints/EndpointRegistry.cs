using System.Collections.Frozen;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace IQOne.Zero.Web;

/// <summary>
/// One endpoint, as the generator described it.
/// </summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Pattern">The route pattern, before any prefix.</param>
/// <param name="Name">Name used in link generation and OpenAPI.</param>
/// <param name="Tag">OpenAPI grouping, or null.</param>
/// <param name="Policy">
/// Authorization policy, or null to fall back to
/// <see cref="ZeroWebOptions.RequireAuthorizationByDefault"/> — which requires an
/// authenticated caller unless the application turned it off.
/// </param>
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
    /// <exception cref="InvalidOperationException">Another request already matches those calls.</exception>
    void Add(ZeroEndpointDescriptor endpoint);
}

/// <summary>The endpoints of an application: filled while modules configure, then frozen.</summary>
public sealed class EndpointRegistry : IEndpointRegistryBuilder
{
    private readonly Dictionary<(string Method, string Shape), ZeroEndpointDescriptor> _endpoints =
        new(RouteComparer.Instance);

    private FrozenSet<ZeroEndpointDescriptor>? _frozen;

    /// <inheritdoc />
    public void Add(ZeroEndpointDescriptor endpoint)
    {
        if (_frozen is not null)
            throw new InvalidOperationException(
                $"The endpoint table is frozen; '{endpoint.Method} {endpoint.Pattern}' cannot be added.");

        var key = (endpoint.Method, Shape(endpoint.Pattern));

        if (_endpoints.TryGetValue(key, out var claimed))
            throw new InvalidOperationException(
                $"'{endpoint.Method} {endpoint.Pattern}' and '{claimed.Method} {claimed.Pattern}' match the " +
                $"same calls, and are claimed by '{endpoint.RequestType.Name}' and " +
                $"'{claimed.RequestType.Name}'. A route belongs to one request.");

        _endpoints.Add(key, endpoint);
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

    /// <summary>
    /// What a pattern actually matches, with the parameter names taken out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two patterns conflict when they match the same calls, and ASP.NET decides that from
    /// the structure, not the text: <c>/invoices/{id}</c> and <c>/invoices/{invoiceId}</c>
    /// are the same route under two names. Keying on the text let both be registered, and
    /// every call to <c>/invoices/5</c> then threw <c>AmbiguousMatchException</c> — a 500 in
    /// production for a mistake that was visible at startup.
    /// </para>
    /// <para>
    /// Constraints stay in the key, because they are what makes two similar patterns
    /// genuinely different: <c>{id:int}</c> and <c>{slug:alpha}</c> never match the same
    /// call, and an unconstrained <c>{id}</c> loses to a constrained one by precedence
    /// rather than ambiguity.
    /// </para>
    /// </remarks>
    private static string Shape(string pattern)
    {
        if (pattern.IndexOf('{') < 0) return pattern;

        var shape = new StringBuilder(pattern.Length);

        for (var i = 0; i < pattern.Length; i++)
        {
            // A doubled brace is a literal brace, not the start of a parameter.
            if (i + 1 < pattern.Length && pattern[i] is '{' or '}' && pattern[i + 1] == pattern[i])
            {
                shape.Append(pattern[i]).Append(pattern[i]);
                i++;
                continue;
            }

            if (pattern[i] != '{')
            {
                shape.Append(pattern[i]);
                continue;
            }

            var end = Close(pattern, i);

            if (end < 0)
            {
                // Malformed: leave it as written and let the route parser report it.
                shape.Append(pattern, i, pattern.Length - i);
                break;
            }

            shape.Append(Parameter(pattern.AsSpan(i + 1, end - i - 1)));
            i = end;
        }

        return shape.ToString();
    }

    private static int Close(string pattern, int start)
    {
        for (var i = start + 1; i < pattern.Length; i++)
        {
            if (pattern[i] != '}') continue;
            if (i + 1 < pattern.Length && pattern[i + 1] == '}') { i++; continue; }

            return i;
        }

        return -1;
    }

    /// <summary>Everything about a parameter except what it is called.</summary>
    private static string Parameter(ReadOnlySpan<char> inner)
    {
        var stars = 0;

        while (stars < inner.Length && inner[stars] == '*') stars++;

        var named = inner[stars..];
        var tail = named.IndexOfAny(':', '=', '?');

        return $"{{{new string('*', stars)}{(tail < 0 ? string.Empty : named[tail..].ToString())}}}";
    }

    /// <summary>A route is the same route whatever case it was written in.</summary>
    private sealed class RouteComparer : IEqualityComparer<(string Method, string Shape)>
    {
        public static readonly RouteComparer Instance = new();

        public bool Equals((string Method, string Shape) x, (string Method, string Shape) y)
            => string.Equals(x.Method, y.Method, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Shape, y.Shape, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Method, string Shape) obj)
            => HashCode.Combine(obj.Method.ToUpperInvariant(), obj.Shape.ToLowerInvariant());
    }
}
