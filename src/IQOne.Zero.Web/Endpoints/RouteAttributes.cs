namespace IQOne.Zero.Web;

/// <summary>
/// Declares the HTTP route a request is reachable at.
/// </summary>
/// <remarks>
/// <para>
/// The route lives on the request rather than in a separate mapping file, because the two
/// drift apart the moment they are in different places: a renamed request keeps its old
/// route, a deleted one leaves a mapping behind. Here they cannot disagree.
/// </para>
/// <para>
/// The route is stated, never derived from the type name. A published URL must not move
/// because someone renamed a class.
/// </para>
/// </remarks>
/// <param name="method">The HTTP method.</param>
/// <param name="pattern">The route pattern, with ASP.NET's constraint syntax.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public abstract class RouteAttribute(string method, string pattern)
    : Attribute, IQOne.Zero.Authorization.IAuthorizationDeclaration
{
    /// <summary>The HTTP method.</summary>
    public string Method { get; } = method;

    /// <summary>The route pattern, for example <c>/invoices/{id:int}</c>.</summary>
    public string Pattern { get; } = pattern;

    /// <summary>
    /// Name used in link generation and OpenAPI. Defaults to the request's type name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Groups the endpoint in OpenAPI.</summary>
    public string? Tag { get; set; }

    /// <summary>
    /// Authorization policy applied to the endpoint.
    /// </summary>
    /// <remarks>
    /// Null does not leave the endpoint open: it falls back to
    /// <see cref="ZeroWebOptions.RequireAuthorizationByDefault"/>, which requires an
    /// authenticated caller unless the application turned it off. Say
    /// <see cref="AllowAnonymous"/> to publish an endpoint, so that the decision is in the
    /// code rather than in what nobody wrote.
    /// </remarks>
    public string? Policy { get; set; }

    /// <summary>Whether the endpoint may be reached without authentication.</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>Roles, any one of which will do. Comma-separated.</summary>
    public string? Roles { get; set; }

    /// <inheritdoc />
    bool IQOne.Zero.Authorization.IAuthorizationDeclaration.AllowsAnonymous => AllowAnonymous;
}

/// <summary>Reachable with GET. Safe to cache and to retry.</summary>
/// <param name="pattern">The route pattern.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class GetAttribute(string pattern) : RouteAttribute("GET", pattern);

/// <summary>Reachable with POST.</summary>
/// <param name="pattern">The route pattern.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PostAttribute(string pattern) : RouteAttribute("POST", pattern);

/// <summary>Reachable with PUT.</summary>
/// <param name="pattern">The route pattern.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PutAttribute(string pattern) : RouteAttribute("PUT", pattern);

/// <summary>Reachable with PATCH.</summary>
/// <param name="pattern">The route pattern.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class PatchAttribute(string pattern) : RouteAttribute("PATCH", pattern);

/// <summary>Reachable with DELETE.</summary>
/// <param name="pattern">The route pattern.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class DeleteAttribute(string pattern) : RouteAttribute("DELETE", pattern);
