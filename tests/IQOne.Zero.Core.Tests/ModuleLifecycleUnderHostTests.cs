using IQOne.Zero.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.Tests;

/// <summary>Records which lifecycle phases actually ran, and in what order.</summary>
internal sealed class PhaseRecordingModule(List<string> log)
    : IModule, IModuleConfigureServicesStep, IModuleInitializeStep, IModulePreRunStep, IModulePostRunStep
{
    public string Name => "Phases";

    public ValueTask OnConfigureServicesAsync(IModuleServiceContext context, CancellationToken cancellationToken)
    {
        log.Add("configure");
        return default;
    }

    public ValueTask OnInitializeAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        log.Add("initialize");
        return default;
    }

    public ValueTask OnPreRunAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        log.Add("prerun");
        return default;
    }

    public ValueTask OnPostRunAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        log.Add("postrun");
        return default;
    }
}

/// <summary>
/// The module lifecycle under a generic host — which is what an ASP.NET application is.
/// </summary>
/// <remarks>
/// Three of the four phases used to run only under Zero's own <c>Application</c>, so a
/// module that seeded data or opened a consumer on startup compiled, registered, and never
/// ran in the host almost every application actually uses. Nothing failed; the work simply
/// did not happen.
/// </remarks>
public class ModuleLifecycleUnderHostTests
{
    private static IHost Build(List<string> log)
    {
        var builder = Host.CreateDefaultBuilder();

        builder.ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services => services.AddModules(new PhaseRecordingModule(log)))
            .Configure(app => { }));

        return builder.Build();
    }

    [Fact]
    public async Task Every_phase_runs_and_shutdown_runs_last()
    {
        var log = new List<string>();
        var host = Build(log);

        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);
        host.Dispose();

        log.Should().Equal("configure", "initialize", "prerun", "postrun");
    }

    [Fact]
    public void Configure_has_already_run_by_the_time_the_host_is_built()
    {
        var log = new List<string>();

        using var host = Build(log);

        log.Should().Equal(
            ["configure"],
            "registrations must be in place before the provider is built, so this phase " +
            "cannot wait for the host to start");
    }

    [Fact]
    public async Task Starting_twice_does_not_run_a_phase_twice()
    {
        var log = new List<string>();
        var host = Build(log);

        await host.StartAsync(CancellationToken.None);
        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);
        host.Dispose();

        log.Should().Equal("configure", "initialize", "prerun", "postrun");
    }
}
