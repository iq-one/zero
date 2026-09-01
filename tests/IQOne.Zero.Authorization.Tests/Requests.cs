using System.Security.Claims;
using IQOne.Zero.Authorization;
using IQOne.Zero.Messaging;

namespace IQOne.Zero.Authorization.Tests;

[AllowAnonymous]
internal sealed record Ping : IQuery<string>;

[Authorize]
internal sealed record WhoAmI : IQuery<string>;

[Authorize("invoices.close")]
internal sealed record CloseInvoice(int Id) : ICommand<string>;

[Authorize(Roles = "admin, auditor")]
internal sealed record ReadLedger : IQuery<string>;

[Authorize(Roles = "admin")]
[Authorize("invoices.close")]
internal sealed record PurgeLedger : ICommand<string>;

[Authorize("never.declared")]
internal sealed record UsesMissingPolicy : IQuery<string>;

[Authorize("faulty")]
internal sealed record UsesFaultyRequirement : IQuery<string>;

[Authorize("unhandled")]
internal sealed record UsesUnhandledRequirement : IQuery<string>;

[Authorize("cancelling")]
internal sealed record UsesCancellingRequirement : IQuery<string>;

[AllowAnonymous]
[Authorize("invoices.close")]
internal sealed record Contradictory : IQuery<string>;

/// <summary>Carries no authorization attribute at all. What happens to it is the point.</summary>
internal sealed record Undeclared : IQuery<string>;

/// <summary>An invoice, as far as a resource rule needs to know.</summary>
internal sealed record Invoice(int Id, string OwnerId);

internal sealed class MustBeOwner : IAuthorizationRequirement;

internal sealed class AlwaysFails : IAuthorizationRequirement;

internal sealed class AlwaysThrows : IAuthorizationRequirement;

internal sealed class Cancels : IAuthorizationRequirement;

internal sealed class NobodyHandlesThis : IAuthorizationRequirement;

internal sealed class AlwaysFailsHandler : IRequirementHandler<AlwaysFails>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        AlwaysFails requirement, ICurrentUser user, CancellationToken cancellationToken)
        => new(AuthorizationDecision.Deny("test.always-fails", "This rule never passes."));
}

internal sealed class AlwaysThrowsHandler : IRequirementHandler<AlwaysThrows>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        AlwaysThrows requirement, ICurrentUser user, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The permission store is unreachable.");
}

internal sealed class CancelsHandler : IRequirementHandler<Cancels>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        Cancels requirement, ICurrentUser user, CancellationToken cancellationToken)
        => throw new OperationCanceledException(cancellationToken);
}

internal sealed class MustBeOwnerHandler : IRequirementHandler<MustBeOwner, Invoice>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        MustBeOwner requirement, Invoice resource, ICurrentUser user, CancellationToken cancellationToken)
        => new(string.Equals(resource.OwnerId, user.Id, StringComparison.Ordinal)
            ? AuthorizationDecision.Allowed
            : AuthorizationDecision.Deny("invoice.not-owner", "Only the invoice's owner may do this."));
}

internal sealed class OwnerThrowsHandler : IRequirementHandler<AlwaysThrows, Invoice>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        AlwaysThrows requirement, Invoice resource, ICurrentUser user, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The permission store is unreachable.");
}

/// <summary>Callers the tests reuse.</summary>
internal static class Callers
{
    public static ICurrentUser Nobody => CurrentUser.Anonymous;

    public static ICurrentUser Known(string id = "u-1", params Claim[] claims) => new CurrentUser(id, claims);

    public static ICurrentUser InRole(string role, string id = "u-1")
        => new CurrentUser(id, new Claim(ClaimTypes.Role, role));
}
