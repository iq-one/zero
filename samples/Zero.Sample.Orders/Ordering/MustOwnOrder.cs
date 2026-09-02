using IQOne.Zero.Authorization;

namespace Zero.Sample.Orders.Ordering;

/// <summary>
/// The caller must be the customer the order belongs to, or be allowed to read any.
/// </summary>
/// <remarks>
/// A requirement rather than an <c>if</c> in the handler, so the rule is a class with a name
/// and a test. "May this caller see this order" is a sentence the business says; a comparison
/// buried in a query handler is not, and the next handler will phrase it differently.
/// </remarks>
public sealed record MustOwnOrder : IAuthorizationRequirement;

/// <summary>Decides <see cref="MustOwnOrder"/> for one order.</summary>
public sealed class MustOwnOrderHandler : IRequirementHandler<MustOwnOrder, Order>
{
    /// <inheritdoc />
    public ValueTask<AuthorizationDecision> CheckAsync(
        MustOwnOrder requirement, Order resource, ICurrentUser user, CancellationToken cancellationToken)
    {
        if (resource.CustomerId == user.Id) return new(AuthorizationDecision.Allowed);

        // A claim, not a second database read: whether someone may read any order is about
        // the caller, and the caller arrived with everything needed to answer it.
        var readsAny = user.Claims.Any(claim =>
            claim.Type == "permission" && claim.Value == OrderPolicies.ReadAny);

        return new(readsAny
            ? AuthorizationDecision.Allowed
            : AuthorizationDecision.Deny("order.notYours", "That order belongs to another customer."));
    }
}
