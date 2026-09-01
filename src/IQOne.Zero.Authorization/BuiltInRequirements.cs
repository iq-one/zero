namespace IQOne.Zero.Authorization;

/// <summary>
/// The caller must hold at least one of these roles.
/// </summary>
/// <remarks>
/// What <c>[Authorize(Roles = "...")]</c> becomes, and usable inside a policy of your own so
/// that "an admin, and also the invoice's owner" is one policy rather than two attributes.
/// </remarks>
public sealed class RolesRequirement : IAuthorizationRequirement
{
    /// <summary>Requires any one of the given roles.</summary>
    /// <param name="roles">The roles that would satisfy it. At least one.</param>
    /// <exception cref="ArgumentException">No role was given.</exception>
    public RolesRequirement(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        if (roles.Length == 0)
            throw new ArgumentException("A role requirement with no roles refuses everyone.", nameof(roles));

        Roles = roles.ToArray();
    }

    /// <summary>The roles, any one of which is enough.</summary>
    public IReadOnlyList<string> Roles { get; }
}

/// <summary>
/// The caller must carry a claim, optionally with one of a set of values.
/// </summary>
/// <remarks>
/// For the checks that are genuinely a claim lookup — a tenant, a scope, a licence flag —
/// so that the fiftieth application does not write the same handler again.
/// </remarks>
public sealed class ClaimRequirement : IAuthorizationRequirement
{
    /// <summary>Requires the claim, and one of the given values when any are given.</summary>
    /// <param name="claimType">The claim the caller must carry.</param>
    /// <param name="allowedValues">
    /// The values that satisfy it. Leave empty to require only that the claim is present.
    /// </param>
    /// <exception cref="ArgumentException">The claim type is blank.</exception>
    public ClaimRequirement(string claimType, params string[] allowedValues)
    {
        ArgumentNullException.ThrowIfNull(allowedValues);

        if (string.IsNullOrWhiteSpace(claimType))
            throw new ArgumentException("A claim requirement needs a claim type.", nameof(claimType));

        ClaimType = claimType;
        AllowedValues = allowedValues.ToArray();
    }

    /// <summary>The claim the caller must carry.</summary>
    public string ClaimType { get; }

    /// <summary>The values that satisfy it. Empty means the claim only has to be there.</summary>
    public IReadOnlyList<string> AllowedValues { get; }
}

/// <summary>Decides <see cref="RolesRequirement"/> against the caller's role claims.</summary>
/// <param name="options">Says which claim carries roles in this application.</param>
internal sealed class RolesRequirementHandler(AuthorizationOptions options)
    : IRequirementHandler<RolesRequirement>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        RolesRequirement requirement, ICurrentUser user, CancellationToken cancellationToken)
        => new(requirement.Roles.Any(role => user.IsInRole(role, options.RoleClaimType))
            ? AuthorizationDecision.Allowed
            : AuthorizationDecision.Deny(
                "authorization.role",
                $"This requires one of the following roles: {string.Join(", ", requirement.Roles)}."));
}

/// <summary>Decides <see cref="ClaimRequirement"/> against the caller's claims.</summary>
internal sealed class ClaimRequirementHandler : IRequirementHandler<ClaimRequirement>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        ClaimRequirement requirement, ICurrentUser user, CancellationToken cancellationToken)
    {
        var satisfied = requirement.AllowedValues.Count == 0
            ? user.FindFirst(requirement.ClaimType) is not null
            : requirement.AllowedValues.Any(value => user.HasClaim(requirement.ClaimType, value));

        return new ValueTask<AuthorizationDecision>(satisfied
            ? AuthorizationDecision.Allowed
            : AuthorizationDecision.Deny(
                "authorization.claim",
                $"This requires the '{requirement.ClaimType}' claim."));
    }
}
