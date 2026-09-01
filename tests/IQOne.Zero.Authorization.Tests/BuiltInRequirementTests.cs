using System.Security.Claims;
using IQOne.Zero.Authorization;

namespace IQOne.Zero.Authorization.Tests;

/// <summary>
/// The two requirements the package ships, so the fiftieth application does not write them
/// again. Both are usable inside a policy of your own, which is the point of them being
/// requirements rather than special cases in the behaviour.
/// </summary>
public class BuiltInRequirementTests
{
    [Fact]
    public async Task A_role_requirement_inside_a_policy_behaves_as_the_attribute_does()
    {
        var pipeline = Pipeline.For<CloseInvoice>(
            Callers.InRole("finance"),
            options => options.AddPolicy("invoices.close", new RolesRequirement("finance", "admin")));

        (await pipeline.SendAsync(new CloseInvoice(1))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_role_requirement_reads_whichever_claim_the_application_named()
    {
        var pipeline = Pipeline.For<CloseInvoice>(
            Callers.Known("u-1", new Claim("roles", "finance")),
            options =>
            {
                options.RoleClaimType = "roles";
                options.AddPolicy("invoices.close", new RolesRequirement("finance"));
            });

        (await pipeline.SendAsync(new CloseInvoice(1))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_role_requirement_with_no_roles_would_refuse_everyone_so_it_is_refused_here()
    {
        var build = () => new RolesRequirement();

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task A_claim_requirement_with_values_accepts_any_one_of_them()
    {
        var pipeline = Pipeline.For<CloseInvoice>(
            Callers.Known("u-1", new Claim("tenant", "north")),
            options => options.AddPolicy("invoices.close", new ClaimRequirement("tenant", "north", "south")));

        (await pipeline.SendAsync(new CloseInvoice(1))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_claim_requirement_refuses_a_caller_carrying_a_different_value()
    {
        var pipeline = Pipeline.For<CloseInvoice>(
            Callers.Known("u-1", new Claim("tenant", "east")),
            options => options.AddPolicy("invoices.close", new ClaimRequirement("tenant", "north")));

        var result = await pipeline.SendAsync(new CloseInvoice(1));

        result.Error.Kind.Should().Be(ErrorKind.Forbidden);
        result.Error.Code.Should().Be("authorization.claim");
        pipeline.Reached.Should().BeFalse();
    }

    [Fact]
    public async Task A_claim_requirement_with_no_values_only_asks_that_the_claim_is_there()
    {
        var present = Pipeline.For<CloseInvoice>(
            Callers.Known("u-1", new Claim("tenant", "anything")),
            options => options.AddPolicy("invoices.close", new ClaimRequirement("tenant")));

        (await present.SendAsync(new CloseInvoice(1))).IsSuccess.Should().BeTrue();

        var absent = Pipeline.For<CloseInvoice>(
            Callers.Known(),
            options => options.AddPolicy("invoices.close", new ClaimRequirement("tenant")));

        (await absent.SendAsync(new CloseInvoice(1))).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void A_default_decision_is_a_refusal()
    {
        default(AuthorizationDecision).IsAllowed.Should().BeFalse(
            "a value that defaults to 'allowed' turns a forgotten assignment into access nobody granted");

        default(AuthorizationDecision).Code.Should().Be("authorization.denied");
        AuthorizationDecision.Deny().IsAllowed.Should().BeFalse();
        AuthorizationDecision.Allowed.IsAllowed.Should().BeTrue();
    }
}
