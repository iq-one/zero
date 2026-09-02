namespace Zero.Sample.Orders.Ordering;

/// <summary>
/// The policies this application authorises against.
/// </summary>
/// <remarks>
/// Constants rather than strings at each use. A policy named by a literal in six places is
/// one typo away from an endpoint that requires a policy nobody defined — and a policy that
/// does not exist must fail closed, which means the endpoint stops working with no clue why.
/// </remarks>
public static class OrderPolicies
{
    /// <summary>May place an order.</summary>
    public const string Place = "orders:place";

    /// <summary>May pay for an order.</summary>
    public const string Pay = "orders:pay";

    /// <summary>May read any order, not only their own.</summary>
    public const string ReadAny = "orders:read-any";

    /// <summary>All of them, so the module can declare each as a policy in one loop.</summary>
    public static string[] All => [Place, Pay, ReadAny];
}

/// <summary>The caller must carry a named permission.</summary>
/// <remarks>
/// A requirement rather than a claim check written out at each policy, so "what does this
/// permission mean" has one answer. The sample's identity provider happens to put
/// permissions in claims; a different one might read them from a table, and only this class
/// would change.
/// </remarks>
/// <param name="Permission">The permission the caller must carry.</param>
public sealed record MustHavePermission(string Permission) : IQOne.Zero.Authorization.IAuthorizationRequirement;

/// <summary>Decides <see cref="MustHavePermission"/>.</summary>
public sealed class MustHavePermissionHandler
    : IQOne.Zero.Authorization.IRequirementHandler<MustHavePermission>
{
    /// <inheritdoc />
    public ValueTask<IQOne.Zero.Authorization.AuthorizationDecision> CheckAsync(
        MustHavePermission requirement,
        IQOne.Zero.Authorization.ICurrentUser user,
        CancellationToken cancellationToken)
        => new(user.Claims.Any(claim => claim.Type == "permission" && claim.Value == requirement.Permission)
            ? IQOne.Zero.Authorization.AuthorizationDecision.Allowed
            : IQOne.Zero.Authorization.AuthorizationDecision.Deny(
                "permission.missing", $"This request needs the '{requirement.Permission}' permission."));
}
