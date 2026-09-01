using IQOne.Zero.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization.Tests;

/// <summary>
/// "May this caller act on <em>this</em> invoice", asked from where the invoice exists.
/// </summary>
public class ResourceAuthorizationTests
{
    private static IResourceAuthorizer Authorizer(
        ICurrentUser user, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();

        services.AddScoped(_ => user);
        services.AddZeroAuthorization();
        register?.Invoke(services);

        return services.BuildServiceProvider().CreateScope().ServiceProvider
            .GetRequiredService<IResourceAuthorizer>();
    }

    [Fact]
    public async Task The_owner_may_act_on_their_own_invoice()
    {
        var authorizer = Authorizer(
            Callers.Known("u-1"),
            services => services.AddRequirementHandler<MustBeOwner, Invoice, MustBeOwnerHandler>());

        var result = await authorizer.AuthorizeAsync(
            new MustBeOwner(), new Invoice(7, "u-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Someone_else_is_forbidden_from_acting_on_it()
    {
        var authorizer = Authorizer(
            Callers.Known("u-2"),
            services => services.AddRequirementHandler<MustBeOwner, Invoice, MustBeOwnerHandler>());

        var result = await authorizer.AuthorizeAsync(
            new MustBeOwner(), new Invoice(7, "u-1"), CancellationToken.None);

        result.Error.Kind.Should().Be(ErrorKind.Forbidden);
        result.Error.Code.Should().Be("invoice.not-owner");
    }

    [Fact]
    public async Task An_unidentified_caller_is_unauthorized_before_the_resource_is_looked_at()
    {
        var authorizer = Authorizer(
            Callers.Nobody,
            services => services.AddRequirementHandler<MustBeOwner, Invoice, MustBeOwnerHandler>());

        var result = await authorizer.AuthorizeAsync(
            new MustBeOwner(), new Invoice(7, "u-1"), CancellationToken.None);

        result.Error.Kind.Should().Be(ErrorKind.Unauthorized);
    }

    [Fact]
    public async Task A_resource_requirement_with_no_handler_refuses()
    {
        var authorizer = Authorizer(Callers.Known());

        var result = await authorizer.AuthorizeAsync(
            new MustBeOwner(), new Invoice(7, "u-1"), CancellationToken.None);

        result.Error.Kind.Should().Be(ErrorKind.Forbidden);
        result.Error.Code.Should().Be("authorization.requirement.unhandled");
    }

    [Fact]
    public async Task A_resource_handler_that_throws_refuses()
    {
        var authorizer = Authorizer(
            Callers.Known(),
            services => services.AddRequirementHandler<AlwaysThrows, Invoice, OwnerThrowsHandler>());

        var result = await authorizer.AuthorizeAsync(
            new AlwaysThrows(), new Invoice(7, "u-1"), CancellationToken.None);

        result.Error.Code.Should().Be("authorization.requirement.faulted");
    }

    [Fact]
    public async Task The_rule_can_be_tested_without_a_container_a_pipeline_or_a_database()
    {
        var decision = await new MustBeOwnerHandler().CheckAsync(
            new MustBeOwner(), new Invoice(7, "u-1"), Callers.Known("u-1"), CancellationToken.None);

        decision.IsAllowed.Should().BeTrue("that is the whole reason the rule is a class");
    }
}
