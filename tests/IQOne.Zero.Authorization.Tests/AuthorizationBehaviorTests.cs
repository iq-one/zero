using IQOne.Zero.Authorization;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization.Tests;

/// <summary>
/// Every case here is driven through the real pipeline, because a refusal that only happens
/// when the behaviour is called by hand is not a refusal.
/// </summary>
public class AuthorizationBehaviorTests
{
    [Fact]
    public void The_behaviour_sits_outside_validation()
    {
        var behavior = new AuthorizationBehavior<WhoAmI, string>(
            Callers.Nobody, new AuthorizationOptions(), new ServiceCollection().BuildServiceProvider());

        behavior.Order.Should().Be(BehaviorOrder.Authorization);
        behavior.Order.Should().BeLessThan(BehaviorOrder.Validation,
            "there is no point explaining what is wrong with a request the caller may not make");
        behavior.Order.Should().BeGreaterThan(BehaviorOrder.Logging, "a refusal is still worth recording");
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_a_protected_request_and_the_handler_never_runs()
    {
        var pipeline = Pipeline.For<WhoAmI>(Callers.Nobody);

        var result = await pipeline.SendAsync(new WhoAmI());

        result.Error.Kind.Should().Be(ErrorKind.Unauthorized, "we do not know who they are");
        pipeline.Reached.Should().BeFalse();
    }

    [Fact]
    public async Task An_authenticated_caller_passes_a_request_that_only_asks_for_an_identity()
    {
        var pipeline = Pipeline.For<WhoAmI>(Callers.Known());

        var result = await pipeline.SendAsync(new WhoAmI());

        result.IsSuccess.Should().BeTrue();
        pipeline.Reached.Should().BeTrue();
    }

    [Fact]
    public async Task An_authenticated_caller_who_fails_a_requirement_is_forbidden_not_unauthorized()
    {
        var pipeline = Pipeline.For<CloseInvoice>(
            Callers.Known(),
            options => options.AddPolicy("invoices.close", new AlwaysFails()),
            services => services.AddRequirementHandler<AlwaysFails, AlwaysFailsHandler>());

        var result = await pipeline.SendAsync(new CloseInvoice(7));

        result.Error.Kind.Should().Be(ErrorKind.Forbidden,
            "we know exactly who they are, and signing in again will not help");
        result.Error.Code.Should().Be("test.always-fails");
        pipeline.Reached.Should().BeFalse();
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_before_any_requirement_is_consulted()
    {
        var pipeline = Pipeline.For<CloseInvoice>(
            Callers.Nobody,
            options => options.AddPolicy("invoices.close", new AlwaysThrows()),
            services => services.AddRequirementHandler<AlwaysThrows, AlwaysThrowsHandler>());

        var result = await pipeline.SendAsync(new CloseInvoice(7));

        result.Error.Kind.Should().Be(ErrorKind.Unauthorized);
        result.Error.Code.Should().Be("authorization.unauthenticated");
    }

    [Fact]
    public async Task A_refusal_for_an_unidentified_caller_says_nothing_about_what_was_required()
    {
        var pipeline = Pipeline.For<CloseInvoice>(
            Callers.Nobody,
            options => options.AddPolicy("invoices.close", new AlwaysFails()),
            services => services.AddRequirementHandler<AlwaysFails, AlwaysFailsHandler>());

        var result = await pipeline.SendAsync(new CloseInvoice(7));

        result.Error.Message.Should().NotContain("invoices.close",
            "telling an unidentified caller which policy guards a request tells them it exists");
    }

    [Fact]
    public async Task An_anonymous_request_is_served_with_nobody_behind_it()
    {
        var pipeline = Pipeline.For<Ping>(Callers.Nobody);

        var result = await pipeline.SendAsync(new Ping());

        result.IsSuccess.Should().BeTrue();
        pipeline.Reached.Should().BeTrue();
    }

    [Fact]
    public async Task AllowAnonymous_wins_over_Authorize_on_the_same_request()
    {
        var pipeline = Pipeline.For<Contradictory>(Callers.Nobody);

        var result = await pipeline.SendAsync(new Contradictory());

        result.IsSuccess.Should().BeTrue("this is the reading every framework uses, and ZERO451 reports it");
    }

    [Fact]
    public async Task An_unhandled_request_with_no_ICurrentUser_registered_is_still_refused()
    {
        var pipeline = Pipeline.For<WhoAmI>();

        var result = await pipeline.SendAsync(new WhoAmI());

        result.Error.Kind.Should().Be(ErrorKind.Unauthorized,
            "a host that forgot to say who the caller is has not said it is everyone");
        pipeline.Reached.Should().BeFalse();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("auditor")]
    public async Task Roles_within_one_attribute_are_alternatives(string role)
    {
        var pipeline = Pipeline.For<ReadLedger>(Callers.InRole(role));

        (await pipeline.SendAsync(new ReadLedger())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_caller_without_any_of_the_roles_is_forbidden()
    {
        var pipeline = Pipeline.For<ReadLedger>(Callers.InRole("clerk"));

        var result = await pipeline.SendAsync(new ReadLedger());

        result.Error.Kind.Should().Be(ErrorKind.Forbidden);
        result.Error.Code.Should().Be("authorization.role");
        pipeline.Reached.Should().BeFalse();
    }

    [Fact]
    public async Task Roles_are_matched_case_sensitively()
    {
        var pipeline = Pipeline.For<ReadLedger>(Callers.InRole("Admin"));

        (await pipeline.SendAsync(new ReadLedger())).IsFailure.Should().BeTrue(
            "widening a role match by ignoring case widens access nobody granted");
    }

    [Fact]
    public async Task Every_Authorize_attribute_on_a_request_must_pass()
    {
        Pipeline Build(ICurrentUser user) => Pipeline.For<PurgeLedger>(
            user,
            options => options.AddPolicy("invoices.close", new AlwaysFails()),
            services => services.AddRequirementHandler<AlwaysFails, AlwaysFailsHandler>());

        var withRole = Build(Callers.InRole("admin"));

        (await withRole.SendAsync(new PurgeLedger())).IsFailure.Should().BeTrue(
            "the role passed, and the policy on the second attribute did not");
        withRole.Reached.Should().BeFalse();
    }
}
