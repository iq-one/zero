using System.Security.Claims;

namespace IQOne.Zero.Authorization;

/// <summary>
/// Describes the caller from a <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// <para>
/// This lives here, rather than in a web package, because <see cref="ClaimsPrincipal"/> is
/// in the base library: a worker reading a JWT, a gRPC service and a test all have one
/// without referencing ASP.NET. Anything that needs <c>HttpContext</c> to be built belongs
/// in whatever package owns <c>HttpContext</c>.
/// </para>
/// <para>
/// The principal is read once, when this is constructed. Register it per scope so each
/// request gets the principal that arrived with it:
/// </para>
/// <code>
/// services.AddScoped&lt;ICurrentUser&gt;(sp =&gt; new ClaimsPrincipalCurrentUser(
///     sp.GetRequiredService&lt;IHttpContextAccessor&gt;().HttpContext?.User ?? new ClaimsPrincipal()));
/// </code>
/// </remarks>
public sealed class ClaimsPrincipalCurrentUser : ICurrentUser
{
    /// <summary>
    /// The claims tried, in order, when no identifier claim is named.
    /// </summary>
    /// <remarks>
    /// Two, because there is no single answer: OpenID Connect issues <c>sub</c>, while
    /// ASP.NET Identity and WS-Federation issue <see cref="ClaimTypes.NameIdentifier"/>.
    /// Naming the claim explicitly is better than relying on this order.
    /// </remarks>
    public static readonly IReadOnlyList<string> DefaultIdentifierClaimTypes = [ClaimTypes.NameIdentifier, "sub"];

    /// <summary>Reads the caller out of a principal.</summary>
    /// <param name="principal">The principal for this request. An empty one means nobody.</param>
    /// <param name="identifierClaimTypes">
    /// Which claims may carry the identifier, most preferred first. Defaults to
    /// <see cref="DefaultIdentifierClaimTypes"/>.
    /// </param>
    public ClaimsPrincipalCurrentUser(ClaimsPrincipal principal, params string[] identifierClaimTypes)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // Any authenticated identity counts. Reading Identity alone would miss a caller
        // whose primary identity is an unauthenticated one, which is how several
        // authentication schemes on one request compose.
        IsAuthenticated = principal.Identities.Any(identity => identity.IsAuthenticated);

        Claims = principal.Claims.ToArray();

        var wanted = identifierClaimTypes.Length > 0 ? identifierClaimTypes : DefaultIdentifierClaimTypes;

        // An identifier on an unauthenticated principal is not an identity. Reporting one
        // would let a rule that only checks Id treat an anonymous caller as a known one.
        Id = IsAuthenticated ? wanted.Select(this.FindFirst).FirstOrDefault(value => value is not null) : null;
    }

    /// <inheritdoc />
    public bool IsAuthenticated { get; }

    /// <inheritdoc />
    public string? Id { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<Claim> Claims { get; }
}
