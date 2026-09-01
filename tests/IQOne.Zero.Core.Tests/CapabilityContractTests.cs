using IQOne.Zero.Messaging;
using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Tests;

/// <summary>
/// The capability contract, §1 and §7: one entry point, no second call required, and a test
/// that calls only that entry point, builds the provider with validation on, and resolves
/// the capability's public types.
/// </summary>
public class CapabilityContractTests
{
    private static ServiceProvider Build(IServiceCollection services)
        => services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Fact]
    public void AddModules_alone_is_enough()
    {
        var services = new ServiceCollection();

        services.AddModules(new CoreModule());

        using var provider = Build(services);

        provider.GetRequiredService<IReadOnlyList<IModule>>().Should().ContainSingle();
        provider.DescribeModuleGraph().Should().Contain(nameof(CoreModule));
    }

    [Fact]
    public void AddZeroMessaging_alone_is_enough()
    {
        var services = new ServiceCollection();

        // Before, the sender and the registry were registered by the feature contributor's
        // Complete, which only runs if the application also has modules.
        services.AddZeroMessaging();

        using var provider = Build(services);

        provider.GetRequiredService<RequestRegistry>().Should().NotBeNull();
        provider.GetRequiredService<MessagingOptions>().Should().NotBeNull();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISender>().Should().NotBeNull();
    }

    [Fact]
    public void The_options_a_capability_was_configured_with_are_readable()
    {
        var services = new ServiceCollection();

        services.AddZeroMessaging(options => options.RequireHandlerForEveryRequest = false);

        Build(services).GetRequiredService<MessagingOptions>()
            .RequireHandlerForEveryRequest.Should().BeFalse();
    }

    [Fact]
    public void Adding_a_capability_twice_registers_it_once()
    {
        var services = new ServiceCollection();

        services.AddZeroMessaging();
        services.AddZeroMessaging();

        Build(services).GetServices<RequestRegistry>().Should().ContainSingle();
    }
}
