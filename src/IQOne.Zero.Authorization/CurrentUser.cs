using System.Security.Claims;
using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Authorization;

/// <summary>
/// Who is making the current request.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is a <see langword="string"/> on purpose. It is a <see cref="Guid"/> in one
/// application and an <see langword="int"/> in the next, and a framework that picks one is
/// wrong for half of the applications that install it. Parse it where you know what it is.
/// </para>
/// <para>
/// Nothing here mentions HTTP. A worker, a console host and a test each supply their own
/// implementation, which is what lets the same authorization rule run in all of them.
/// </para>
/// <para>
/// When no implementation is registered, <c>AddZeroAuthorization()</c> supplies one that is
/// never authenticated. That is the safe direction: the application refuses protected
/// requests instead of letting them through with nobody behind them.
/// </para>
/// </remarks>
public interface ICurrentUser : IScoped
{
    /// <summary>Whether anyone is behind this request at all.</summary>
    /// <remarks>
    /// False means the caller could not be identified, which is <see cref="ErrorKind.Unauthorized"/>.
    /// True and still refused is <see cref="ErrorKind.Forbidden"/>.
    /// </remarks>
    bool IsAuthenticated { get; }

    /// <summary>What the identity provider calls this caller, or null when there is nobody.</summary>
    string? Id { get; }

    /// <summary>Everything the identity provider said about the caller. Empty when there is nobody.</summary>
    IReadOnlyCollection<Claim> Claims { get; }
}

/// <summary>
/// A caller described directly, for a host that has no <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// Worth having beyond tests: a job runner that acts as a service account, or a queue
/// consumer that reads the caller out of a message header, has an identity but no principal.
/// </remarks>
public sealed class CurrentUser : ICurrentUser
{
    /// <summary>Nobody. Every protected request is refused for this caller.</summary>
    public static readonly ICurrentUser Anonymous = new CurrentUser(false, null, []);

    private CurrentUser(bool isAuthenticated, string? id, IReadOnlyCollection<Claim> claims)
    {
        IsAuthenticated = isAuthenticated;
        Id = id;
        Claims = claims;
    }

    /// <summary>An authenticated caller with the given identifier and claims.</summary>
    /// <param name="id">What the identity provider calls this caller.</param>
    /// <param name="claims">Everything else known about them.</param>
    public CurrentUser(string id, params Claim[] claims) : this(true, id, claims) { }

    /// <inheritdoc />
    public bool IsAuthenticated { get; }

    /// <inheritdoc />
    public string? Id { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<Claim> Claims { get; }
}

/// <summary>Reads a caller's claims without every rule writing the same loop.</summary>
public static class CurrentUserExtensions
{
    /// <summary>The value of the first claim of this type, or null when there is none.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="claimType">The claim to look for.</param>
    /// <returns>The claim's value, or null.</returns>
    public static string? FindFirst(this ICurrentUser user, string claimType)
    {
        ArgumentNullException.ThrowIfNull(user);

        foreach (var claim in user.Claims)
            if (Matches(claim.Type, claimType))
                return claim.Value;

        return null;
    }

    /// <summary>Every value the caller carries for this claim type.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="claimType">The claim to look for.</param>
    /// <returns>The values, in the order the identity provider gave them.</returns>
    public static IEnumerable<string> FindAll(this ICurrentUser user, string claimType)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Claims.Where(c => Matches(c.Type, claimType)).Select(c => c.Value);
    }

    /// <summary>Whether the caller carries this exact claim.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="claimType">The claim to look for.</param>
    /// <param name="value">The value it must have.</param>
    /// <returns><see langword="true"/> when the caller has it.</returns>
    public static bool HasClaim(this ICurrentUser user, string claimType, string value)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Claims.Any(c => Matches(c.Type, claimType) && string.Equals(c.Value, value, StringComparison.Ordinal));
    }

    /// <summary>Whether the caller holds this role.</summary>
    /// <remarks>
    /// A role is a claim, and which claim carries it depends on the token: OpenID Connect
    /// providers often use <c>roles</c> where ASP.NET Identity uses the long WS-Federation
    /// URI. <c>AuthorizationOptions.RoleClaimType</c> says which one this application reads,
    /// and that is what the behaviour passes here.
    /// </remarks>
    /// <param name="user">The caller.</param>
    /// <param name="role">The role to look for.</param>
    /// <param name="roleClaimType">The claim that carries roles.</param>
    /// <returns><see langword="true"/> when the caller holds the role.</returns>
    public static bool IsInRole(this ICurrentUser user, string role, string roleClaimType = ClaimTypes.Role)
        => user.HasClaim(roleClaimType, role);

    // Claim types are compared without case, as ClaimsIdentity does, because they arrive as
    // URIs from several issuers. Values are compared exactly: a role named "Admin" is not
    // the role named "admin", and deciding otherwise would widen access by accident.
    private static bool Matches(string claimType, string wanted)
        => string.Equals(claimType, wanted, StringComparison.OrdinalIgnoreCase);
}
