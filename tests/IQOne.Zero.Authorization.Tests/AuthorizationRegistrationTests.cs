using IQOne.Zero.Authorization;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization.Tests;

/// <summary>
/// "Install the package and write one line" has to be true rather than aspirational.
/// </summary>
public class AuthorizationRegistrationTests
{
    private static ServiceProvider Provider(Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();

        before?.Invoke(services);
        services.AddZeroAuthorization();

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void The_entry_point_alone_is_enough_to_resolve_what_a_consumer_touches()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentUser>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IResourceAuthorizer>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<AuthorizationOptions>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IRequirementHandler<RolesRequirement>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IRequirementHandler<ClaimRequirement>>().Should().NotBeNull();

        scope.ServiceProvider
            .GetServices<IPipelineBehavior<WhoAmI, string>>()
            .Should().ContainSingle().Which.Order.Should().Be(BehaviorOrder.Authorization);
    }

    [Fact]
    public void Without_a_host_identity_the_caller_is_nobody()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ICurrentUser>().IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void The_host_identity_wins_whichever_order_the_two_registrations_happen_in()
    {
        using var registeredFirst = Provider(services => services.AddScoped(_ => Callers.Known("early")));

        registeredFirst.CreateScope().ServiceProvider.GetRequiredService<ICurrentUser>()
            .Id.Should().Be("early");

        var services = new ServiceCollection();
        services.AddZeroAuthorization();
        services.AddScoped(_ => Callers.Known("late"));

        using var registeredAfter = services.BuildServiceProvider();

        registeredAfter.CreateScope().ServiceProvider.GetRequiredService<ICurrentUser>()
            .Id.Should().Be("late");
    }

    [Fact]
    public void Policies_declared_at_the_entry_point_are_what_the_behaviour_reads()
    {
        var services = new ServiceCollection();

        services.AddZeroAuthorization(options => options.AddPolicy("invoices.close", new AlwaysFails()));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<AuthorizationOptions>()
            .Policies["invoices.close"].Requirements.Should().ContainSingle();
    }
}
