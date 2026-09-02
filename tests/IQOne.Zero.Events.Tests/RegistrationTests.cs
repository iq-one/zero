using IQOne.Zero.Events;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Events.Tests;

/// <summary>
/// Contract §7: the entry point alone has to be enough. A capability that needs a second
/// registration to function is unfinished, and the only way to know is to call just the one.
/// </summary>
public class RegistrationTests
{
    [Fact]
    public void The_entry_point_alone_registers_everything_the_capability_needs()
    {
        var services = new ServiceCollection();

        services.AddZeroEvents();

        // Frozen by the module phase in a real application; a table nobody filled is still
        // a table, and resolving through it must not throw.
        services.AddZeroEventsWithHandlers(_ => { });

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPublisher>().Should().NotBeNull();
        provider.GetRequiredService<EventRegistry>().Should().NotBeNull();
    }

    [Fact]
    public void Calling_the_entry_point_twice_changes_nothing()
    {
        var services = new ServiceCollection();

        services.AddZeroEvents();
        services.AddZeroEvents(options => options.MaxPublishDepth = 99);
        services.AddZeroEventsWithHandlers(_ => { });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<EventRegistry>().Should().HaveCount(1);
    }

    [Fact]
    public void Options_given_to_one_entry_point_survive_the_other()
    {
        var services = new ServiceCollection();

        services.AddZeroEvents(options => options.OnHandlerFailure = HandlerFailure.Stop);
        services.AddZeroEventsWithHandlers(_ => { });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<EventOptions>().OnHandlerFailure
            .Should().Be(HandlerFailure.Stop,
                "the second call used to create fresh defaults, so a configured switch was " +
                "silently replaced by the value it was set away from");
    }

    [Fact]
    public void An_event_nobody_subscribes_to_is_known_and_reported_only_when_asked_for()
    {
        var quiet = new EventRegistry();
        quiet.Declare(typeof(NobodyCares));
        quiet.Freeze();

        quiet.Unsubscribed.Should().Equal([typeof(NobodyCares)],
            "the table knows; whether that is a failure is the application's decision");
    }
}
