using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Tests;

public class ModuleLifecycleTests
{
    [Fact]
    public async Task Fazlar_sirayla_ve_bagimlilik_sirasinda_calisir()
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
    public async Task Kapanis_fazi_ters_sirada_calisir()
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
    public async Task Katkici_yetenegi_modullere_sunar_ve_sonra_muhurler()
    {
        // Cerceve, dispatch gibi ust katman kavramlarini adlandirmaz. Bir katman
        // kendini bu mekanizmayla takar; cekirdek yalnizca buldugu katkicilari calistirir.
        var services = new ServiceCollection();
        var contributor = new RecordingContributor();

        services.AddSingleton<IModuleFeatureContributor>(contributor);

        var module = new FeatureReadingModule();
        await services.AddModulesAsync([module]);

        contributor.Contributed.Should().BeTrue();
        module.SawFeature.Should().Be(contributor.Feature, "modul yetenege configure sirasinda ulasabilmeli");
        contributor.CompletedAfterModules.Should().BeTrue("muhurleme her modul yapilandirildiktan sonra olmali");
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
    public async Task Iptal_token_i_modullere_gecirilir()
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
}
