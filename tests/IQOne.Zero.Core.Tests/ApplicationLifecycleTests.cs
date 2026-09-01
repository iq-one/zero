using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using IQOne.Zero.Extensions;
using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Tests;

/// <summary>
/// The lifecycle's contract is written on the step interfaces: configure-services first,
/// then the provider, then initialize "once the service provider exists", then pre-run
/// "immediately before accepting work", and post-run "during shutdown". These run the real
/// <see cref="Application"/> and hold it to that.
/// </summary>
public class ApplicationLifecycleTests
{
    private sealed class RecordingStep(List<string> log, int order = 0) : ApplicationSteps
    {
        public override int Order => order;

        public bool ProviderWasReadyOnInitialize { get; private set; }

        public override Task OnConfigureServicesAsync(IServiceCollection services, CancellationToken cancellationToken)
        {
            log.Add("configure");

            services.AddSingleton(new Marker());

            return Task.CompletedTask;
        }

        public override Task OnInitializeAsync(IApplication application, CancellationToken cancellationToken)
        {
            log.Add("initialize");

            // The contract says services can be resolved here. It used to be a null reference.
            ProviderWasReadyOnInitialize = application.GetService<Marker>() is not null;

            return Task.CompletedTask;
        }

        public override Task OnPreRunAsync(IApplication application, CancellationToken cancellationToken)
        {
            log.Add("prerun");

            return Task.CompletedTask;
        }

        public override Task OnPostRunAsync(IApplication application, CancellationToken cancellationToken)
        {
            log.Add("postrun");

            return Task.CompletedTask;
        }
    }

    private sealed class Marker;

    private sealed class TrackedSingleton : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task The_phases_run_in_the_order_their_contract_states()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        var step = new RecordingStep(log);

        services.AddSingleton<IApplicationConfigureServicesStep>(step);
        services.AddSingleton<IApplicationInitializeStep>(step);
        services.AddSingleton<IApplicationPreRunStep>(step);
        services.AddSingleton<IApplicationPostRunStep>(step);

        var application = new Application(services);

        await application.RunAsync();

        log.Should().Equal("configure", "initialize", "prerun");

        step.ProviderWasReadyOnInitialize.Should()
            .BeTrue("an initialize step is documented to run once the provider exists");

        await application.StopAsync();

        log.Should().Equal("configure", "initialize", "prerun", "postrun");
    }

    [Fact]
    public async Task Shutdown_runs_the_post_run_steps_in_reverse_order()
    {
        var log = new List<string>();
        var services = new ServiceCollection();

        services.AddSingleton<IApplicationPostRunStep>(new OrderedPostRun(log, "first", -10));
        services.AddSingleton<IApplicationPostRunStep>(new OrderedPostRun(log, "second", 10));

        var application = new Application(services);

        await application.RunAsync();
        await application.StopAsync();

        log.Should().Equal("second", "first");
    }

    private sealed class OrderedPostRun(List<string> log, string name, int order) : IApplicationPostRunStep
    {
        public int Order => order;

        public Task OnPostRunAsync(IApplication application, CancellationToken cancellationToken)
        {
            log.Add(name);

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Stopping_disposes_the_provider_and_everything_it_built()
    {
        var services = new ServiceCollection();

        services.AddSingleton<TrackedSingleton>();

        var application = new Application(services);

        await application.RunAsync();

        var tracked = application.GetRequiredService<TrackedSingleton>();

        await application.StopAsync();

        tracked.Disposed.Should().BeTrue("the container owns every singleton it built");
    }

    [Fact]
    public async Task Disposing_asynchronously_stops_once_even_after_an_explicit_stop()
    {
        var log = new List<string>();
        var services = new ServiceCollection();

        services.AddSingleton<IApplicationPostRunStep>(new OrderedPostRun(log, "postrun", 0));

        var application = new Application(services);

        await application.RunAsync();
        await application.StopAsync();
        await application.DisposeAsync();

        log.Should().Equal("postrun");
    }

    [Fact]
    public async Task Disposing_synchronously_after_disposing_asynchronously_releases_nothing_twice()
    {
        var services = new ServiceCollection();
        var application = new CountingApplication(services);

        await application.RunAsync();

        await application.DisposeAsync();

        // The defensive second call a using-block or a finalizer-conscious caller makes.
        application.Dispose();

        application.Released.Should().Be(1);
    }

    private sealed class CountingApplication(IServiceCollection services) : Application(services)
    {
        public int Released { get; private set; }

        protected override void ReleaseUnmanagedResources() => Released++;
    }

    [Fact]
    public async Task A_cancelled_shutdown_still_disposes_the_provider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<TrackedSingleton>();

        var application = new Application(services);

        await application.RunAsync();

        var tracked = application.GetRequiredService<TrackedSingleton>();

        using var cancelled = new CancellationTokenSource();

        await cancelled.CancelAsync();

        var stop = () => application.StopAsync(cancelled.Token);

        await stop.Should().ThrowAsync<OperationCanceledException>();

        tracked.Disposed.Should().BeTrue("a cancelled shutdown must not leak the container");
    }

    [Fact]
    public async Task The_module_phases_run_under_the_application()
    {
        var log = new List<string>();
        var services = new ServiceCollection();

        services.AddModules(new SecondModule(log), new FirstModule(log));

        var application = new Application(services);

        await application.RunAsync();
        await application.StopAsync();

        log.Should().Equal(
            "FirstModule:configure", "SecondModule:configure",
            "FirstModule:initialize", "SecondModule:initialize",
            "FirstModule:prerun", "SecondModule:prerun",
            "SecondModule:postrun", "FirstModule:postrun");
    }

    [Fact]
    public async Task An_application_with_no_modules_starts_and_stops()
    {
        var application = new Application();

        var lifecycle = async () =>
        {
            await application.RunAsync();
            await application.StopAsync();
        };

        await lifecycle.Should().NotThrowAsync();
    }
}
