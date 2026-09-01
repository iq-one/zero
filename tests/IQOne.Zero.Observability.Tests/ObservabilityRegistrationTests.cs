using IQOne.Zero.Messaging;
using IQOne.Zero.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IQOne.Zero.Observability.Tests;

/// <summary>
/// One Add call has to be enough, and two of them have to be the same as one.
/// </summary>
/// <remarks>
/// A capability that needs a second call is a capability whose documentation is load-bearing,
/// and documentation is the part nobody reads. A capability that cannot survive being added
/// twice is worse: a module and a host both being careful produces two of every log line, and
/// nobody suspects the framework of it.
/// </remarks>
public class ObservabilityRegistrationTests
{
    /// <summary>
    /// Everything the package needs, plus the logging provider the host owns.
    /// </summary>
    /// <remarks>
    /// <c>ILogger&lt;&gt;</c> is deliberately not registered by <c>AddZeroObservability</c>:
    /// a fallback registered there would quietly win over the real one an application adds
    /// later. So a test stands in for the host, exactly as the documentation says.
    /// </remarks>
    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();

        services.AddSingleton(new LogSink());
        services.AddSingleton(typeof(ILogger<>), typeof(RecordingLogger<>));

        return services;
    }

    [Fact]
    public void The_entry_point_alone_puts_all_three_behaviours_in_the_pipeline()
    {
        var services = Services();

        services.AddZeroObservability();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        provider.GetRequiredService<ObservabilityOptions>().Should().NotBeNull();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IPipelineBehavior<Ping, string>>()
            .Select(b => b.GetType())
            .Should().BeEquivalentTo(
            [
                typeof(LoggingBehavior<Ping, string>),
                typeof(TracingBehavior<Ping, string>),
                typeof(MetricsBehavior<Ping, string>)
            ]);
    }

    [Fact]
    public void The_defaults_are_the_ones_most_applications_would_choose()
    {
        var services = Services();

        services.AddZeroObservability();

        var options = services.BuildServiceProvider().GetRequiredService<ObservabilityOptions>();

        options.EnableLogging.Should().BeTrue();
        options.EnableTracing.Should().BeTrue();
        options.EnableMetrics.Should().BeTrue();

        options.LogRequestContents.Should().BeFalse(
            "a command carries user data, and turning that into a log line must be a decision somebody took");
    }

    [Fact]
    public void Adding_it_twice_does_not_log_every_request_twice()
    {
        var services = Services();

        services.AddZeroObservability();
        services.AddZeroObservability();

        using var scope = services.BuildServiceProvider().CreateScope();

        scope.ServiceProvider.GetServices<IPipelineBehavior<Ping, string>>().Should().HaveCount(3);
    }

    [Fact]
    public void Adding_it_twice_leaves_one_set_of_options_behind()
    {
        var services = Services();

        services.AddZeroObservability();
        services.AddZeroObservability();

        services.Count(d => d.ServiceType == typeof(ObservabilityOptions)).Should().Be(1);
    }

    [Fact]
    public void A_second_call_refines_what_the_first_one_configured_rather_than_replacing_it()
    {
        var services = Services();

        // A module asks for request contents; the host later adds observability without
        // saying anything about them. The module's decision must survive.
        services.AddZeroObservability(options => options.LogRequestContents = true);
        services.AddZeroObservability(options => options.EnableMetrics = false);

        var options = services.BuildServiceProvider().GetRequiredService<ObservabilityOptions>();

        options.LogRequestContents.Should().BeTrue();
        options.EnableMetrics.Should().BeFalse();
    }

    [Fact]
    public async Task A_switch_turned_off_by_a_later_call_still_takes_effect()
    {
        var application = TestApplication.With(options => options.EnableLogging = false);

        application.Handles<Ping>();

        using var running = application.Build();

        await running.SendAsync(new Ping("hello"));

        // The behaviour is registered whatever the switch says and reads it as the request
        // goes through, so the switch cannot depend on which caller happened to arrive first.
        application.Log.Lines.Should().BeEmpty();
    }

    [Fact]
    public void An_application_that_brings_its_own_clock_keeps_it()
    {
        var mine = new SteppingTime();

        var services = Services();

        services.AddSingleton<TimeProvider>(mine);
        services.AddZeroObservability();

        services.BuildServiceProvider().GetRequiredService<TimeProvider>().Should().BeSameAs(mine);
    }

    [Fact]
    public void An_application_that_brings_its_own_options_keeps_them()
    {
        var mine = new ObservabilityOptions { LogRequestContents = true };

        var services = Services();

        services.AddSingleton(mine);
        services.AddZeroObservability();

        services.BuildServiceProvider().GetRequiredService<ObservabilityOptions>().Should().BeSameAs(mine);
    }

    [Fact]
    public void The_behaviours_are_open_generics_rather_than_one_registration_per_request()
    {
        var services = Services();

        services.AddZeroObservability();

        // Whatever can be resolved at build time is. Three open generic descriptors wrap every
        // request there will ever be, so no assembly is scanned and no request type is missed.
        services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Should().HaveCount(3)
            .And.OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void The_three_behaviours_wrap_everything_that_can_reject_a_request()
    {
        // A request refused by authorization is still logged, still traced and still counted.
        // A rejection nobody measured is how a broken permission looks like a quiet afternoon.
        BehaviorOrder.Logging.Should().BeLessThan(BehaviorOrder.Authorization);
        ObservabilityOrder.Tracing.Should().BeLessThan(BehaviorOrder.Authorization);
        ObservabilityOrder.Metrics.Should().BeLessThan(BehaviorOrder.Authorization);
    }
}
