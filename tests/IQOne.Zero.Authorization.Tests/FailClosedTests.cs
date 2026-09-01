using IQOne.Zero.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization.Tests;

/// <summary>
/// The cases where authorization does not get a clean answer.
/// </summary>
/// <remarks>
/// Every one of them has an obvious way to fail open — treat the missing thing as "nothing
/// to check" and carry on — and every one of them must not. These are the tests worth
/// keeping if any are.
/// </remarks>
public class FailClosedTests
{
    [Fact]
    public async Task A_policy_that_was_never_declared_refuses_everyone()
    {
        var pipeline = Pipeline.For<UsesMissingPolicy>(Callers.Known());

        var result = await pipeline.SendAsync(new UsesMissingPolicy());

        result.Error.Kind.Should().Be(ErrorKind.Forbidden);
        result.Error.Code.Should().Be("authorization.policy.unknown");
        result.Error.Message.Should().Contain("never.declared", "the message has to be enough to find the bug");
        pipeline.Reached.Should().BeFalse("an unwritten rule is not a rule everyone passes");
    }

    [Fact]
    public async Task A_requirement_with_no_registered_handler_refuses_everyone()
    {
        var pipeline = Pipeline.For<UsesUnhandledRequirement>(
            Callers.Known(),
            options => options.AddPolicy("unhandled", new NobodyHandlesThis()));

        var result = await pipeline.SendAsync(new UsesUnhandledRequirement());

        result.Error.Kind.Should().Be(ErrorKind.Forbidden);
        result.Error.Code.Should().Be("authorization.requirement.unhandled");
        pipeline.Reached.Should().BeFalse();
    }

    [Fact]
    public async Task A_requirement_that_throws_refuses_and_keeps_the_exception_for_the_log()
    {
        var pipeline = Pipeline.For<UsesFaultyRequirement>(
            Callers.Known(),
            options => options.AddPolicy("faulty", new AlwaysThrows()),
            services => services.AddRequirementHandler<AlwaysThrows, AlwaysThrowsHandler>());

        var result = await pipeline.SendAsync(new UsesFaultyRequirement());

        result.Error.Kind.Should().Be(ErrorKind.Forbidden);
        result.Error.Code.Should().Be("authorization.requirement.faulted");
        pipeline.Reached.Should().BeFalse("a check that blew up did not say yes");

        result.Error.Metadata.Should().NotBeNull();
        result.Error.Metadata!["exception"].Should().BeOfType<InvalidOperationException>(
            "the detail belongs in a log, not in the response");
        result.Error.Message.Should().NotContain("permission store",
            "what broke inside the check is not the caller's business");
    }

    [Fact]
    public async Task Cancellation_is_not_turned_into_a_refusal()
    {
        var pipeline = Pipeline.For<UsesCancellingRequirement>(
            Callers.Known(),
            options => options.AddPolicy("cancelling", new Cancels()),
            services => services.AddRequirementHandler<Cancels, CancelsHandler>());

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var send = async () => await pipeline.SendAsync(new UsesCancellingRequirement(), cancelled.Token);

        await send.Should().ThrowAsync<OperationCanceledException>(
            "a caller who went away was not refused, and reporting a 403 would say something untrue");
    }

    [Fact]
    public async Task The_first_requirement_that_refuses_stops_the_rest()
    {
        var pipeline = Pipeline.For<CloseInvoice>(
            Callers.Known(),
            options => options.AddPolicy("invoices.close", new AlwaysFails(), new AlwaysThrows()),
            services => services
                .AddRequirementHandler<AlwaysFails, AlwaysFailsHandler>()
                .AddRequirementHandler<AlwaysThrows, AlwaysThrowsHandler>());

        var result = await pipeline.SendAsync(new CloseInvoice(1));

        result.Error.Code.Should().Be("test.always-fails",
            "the second requirement would have thrown, and it was never reached");
    }
}

/// <summary>
/// What happens to a request that says nothing.
/// </summary>
/// <remarks>
/// The package refuses it. The alternative — treating "no attribute" as "no restriction" —
/// makes forgetting the attribute indistinguishable from deciding the request is public, and
/// only one of those two is ever intended.
/// </remarks>
public class UndeclaredRequestTests
{
    [Fact]
    public async Task By_default_a_request_that_declares_nothing_is_refused()
    {
        var pipeline = Pipeline.For<Undeclared>(Callers.Known());

        var result = await pipeline.SendAsync(new Undeclared());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("authorization.undeclared");
        pipeline.Reached.Should().BeFalse();
    }

    [Fact]
    public void Deny_is_the_value_a_default_options_instance_carries()
    {
        new AuthorizationOptions().Unannotated.Should().Be(MissingAuthorization.Deny);
        default(MissingAuthorization).Should().Be(MissingAuthorization.Deny,
            "an enum whose zero value is the permissive one turns a field nobody set into an open door");
    }

    [Fact]
    public async Task An_undeclared_request_refused_for_an_unknown_caller_is_unauthorized()
    {
        var pipeline = Pipeline.For<Undeclared>(Callers.Nobody);

        var result = await pipeline.SendAsync(new Undeclared());

        result.Error.Kind.Should().Be(ErrorKind.Unauthorized,
            "which refusal it is describes the caller, not the rule");
    }

    [Fact]
    public async Task An_undeclared_request_refused_for_a_known_caller_is_forbidden()
    {
        var pipeline = Pipeline.For<Undeclared>(Callers.Known());

        var result = await pipeline.SendAsync(new Undeclared());

        result.Error.Kind.Should().Be(ErrorKind.Forbidden);
    }

    [Fact]
    public async Task An_application_can_opt_into_requiring_only_an_identity()
    {
        var known = Pipeline.For<Undeclared>(
            Callers.Known(), options => options.Unannotated = MissingAuthorization.RequireAuthentication);

        (await known.SendAsync(new Undeclared())).IsSuccess.Should().BeTrue();
        known.Reached.Should().BeTrue();

        var nobody = Pipeline.For<Undeclared>(
            Callers.Nobody, options => options.Unannotated = MissingAuthorization.RequireAuthentication);

        (await nobody.SendAsync(new Undeclared())).Error.Kind.Should().Be(ErrorKind.Unauthorized);
        nobody.Reached.Should().BeFalse();
    }

    [Fact]
    public async Task An_application_can_opt_out_entirely()
    {
        var pipeline = Pipeline.For<Undeclared>(
            Callers.Nobody, options => options.Unannotated = MissingAuthorization.Allow);

        (await pipeline.SendAsync(new Undeclared())).IsSuccess.Should().BeTrue();
        pipeline.Reached.Should().BeTrue();
    }
}
