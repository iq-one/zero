using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Tests;

public class ModuleLifecycleTests
{
    [Fact]
    public async Task Phases_run_in_order_and_modules_run_in_dependency_order()
    {
        var log = new List<string>();
        var services = new ServiceCollection();

        await services.AddModulesAsync([new SecondModule(log), new FirstModule(log)]);

        var provider = services.BuildServiceProvider();

        await provider.InitializeModulesAsync();
        await provider.PreRunModulesAsync();

        log.Should().Equal(
            "FirstModule:configure", "SecondModule:configure",
            "FirstModule:initialize", "SecondModule:initialize",
            "FirstModule:prerun", "SecondModule:prerun");
    }

    [Fact]
    public async Task Shutdown_runs_in_reverse_dependency_order()
    {
        var log = new List<string>();
        var services = new ServiceCollection();

        await services.AddModulesAsync([new SecondModule(log), new FirstModule(log)]);

        var provider = services.BuildServiceProvider();
        log.Clear();

        await provider.PostRunModulesAsync();

        log.Should().Equal("SecondModule:postrun", "FirstModule:postrun");
    }

    [Fact]
    public async Task Contributor_offers_a_capability_to_modules_then_seals_it()
    {
        // The core names no higher-layer concept. A layer attaches itself through this
        // mechanism, and the core only runs whichever contributors it finds.
        var services = new ServiceCollection();
        var contributor = new RecordingContributor();

        services.AddSingleton<IModuleFeatureContributor>(contributor);

        var module = new FeatureReadingModule();
        await services.AddModulesAsync([module]);

        contributor.Contributed.Should().BeTrue();
        module.SawFeature.Should().Be(contributor.Feature, "a module must be able to reach the capability while it configures");
        contributor.CompletedAfterModules.Should().BeTrue("sealing must happen after every module has been configured");
    }

    private sealed record Capability(string Name);

    private sealed class RecordingContributor : IModuleFeatureContributor
    {
        public Capability Feature { get; } = new("test");

        public bool Contributed { get; private set; }

        public bool CompletedAfterModules { get; private set; }

        public static bool ModuleRan { get; set; }

        public void Contribute(IModuleFeatureCollection features)
        {
            Contributed = true;
            features.Set(Feature);
        }

        public void Complete(IServiceCollection services) => CompletedAfterModules = ModuleRan;
    }

    private sealed class FeatureReadingModule : FakeModule, IModuleConfigureServicesStep
    {
        public Capability? SawFeature { get; private set; }

        public ValueTask OnConfigureServicesAsync(IModuleServiceContext context, CancellationToken cancellationToken)
        {
            SawFeature = context.Feature<Capability>();
            RecordingContributor.ModuleRan = true;

            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Cancellation_token_reaches_the_modules()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var services = new ServiceCollection();
        var module = new CancellationAwareModule();

        await services.AddModulesAsync([module], cts.Token);

        module.ObservedToken.IsCancellationRequested.Should().BeTrue();
    }

    private sealed class CancellationAwareModule : FakeModule, IModuleConfigureServicesStep
    {
        public CancellationToken ObservedToken { get; private set; }

        public ValueTask OnConfigureServicesAsync(
            IModuleServiceContext context, CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void The_synchronous_entry_point_configures_the_modules_during_the_call()
    {
        // Nothing else runs them. An ASP.NET application never builds Zero's Application, so
        // a phase deferred to it is a phase that never happens.
        var log = new List<string>();
        var services = new ServiceCollection();

        services.AddModules(new SecondModule(log), new FirstModule(log));

        log.Should().Equal("FirstModule:configure", "SecondModule:configure");
    }

    [Fact]
    public void The_synchronous_entry_point_seals_the_contributed_capabilities()
    {
        var services = new ServiceCollection();
        var contributor = new SealingContributor();

        services.AddSingleton<IModuleFeatureContributor>(contributor);
        services.AddModules(new FirstModule([]));

        contributor.Completed.Should().BeTrue();

        services.BuildServiceProvider().GetService<SealingContributor.Sealed>().Should().NotBeNull(
            "what a contributor registers on completion is what makes the capability usable");
    }

    private sealed class SealingContributor : IModuleFeatureContributor
    {
        internal sealed record Sealed;

        public bool Completed { get; private set; }

        public void Contribute(IModuleFeatureCollection features) => features.Set(new Sealed());

        public void Complete(IServiceCollection services)
        {
            Completed = true;

            services.AddSingleton(new Sealed());
        }
    }

    [Fact]
    public void A_module_that_really_awaits_is_told_to_use_the_asynchronous_entry_point()
    {
        var services = new ServiceCollection();

        var add = () => services.AddModules(new PendingModule());

        add.Should().Throw<InvalidOperationException>().WithMessage("*AddModulesAsync*");
    }

    /// <summary>Never completes, so the synchronous entry point cannot be in any doubt.</summary>
    private sealed class PendingModule : FakeModule, IModuleConfigureServicesStep
    {
        private readonly TaskCompletionSource _never = new();

        public ValueTask OnConfigureServicesAsync(
            IModuleServiceContext context, CancellationToken cancellationToken)
            => new(_never.Task);
    }

    [Fact]
    public async Task A_module_that_really_awaits_works_through_the_asynchronous_entry_point()
    {
        var services = new ServiceCollection();
        var module = new AwaitingModule();

        await services.AddModulesAsync([module]);

        module.Ran.Should().BeTrue();
    }

    private sealed class AwaitingModule : FakeModule, IModuleConfigureServicesStep
    {
        public bool Ran { get; private set; }

        public async ValueTask OnConfigureServicesAsync(
            IModuleServiceContext context, CancellationToken cancellationToken)
        {
            await Task.Yield();

            Ran = true;
        }
    }
}
