using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.Modules;

/// <summary>
/// Runs each module lifecycle phase once, whoever asks.
/// </summary>
/// <remarks>
/// Two things can drive the phases: Zero's own <see cref="App.Application"/>, and the
/// hosted service that carries them under <c>Microsoft.Extensions.Hosting</c> — which is
/// what an ASP.NET application uses. Both call through here so that an application hosting
/// Zero inside a generic host does not initialise every module twice.
/// </remarks>
internal sealed class ModuleLifecycle
{
    private int _initialized;
    private int _preRun;
    private int _postRun;

    public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
        => Claim(ref _initialized) ? services.InitializeModulesAsync(cancellationToken) : default;

    public ValueTask PreRunAsync(IServiceProvider services, CancellationToken cancellationToken)
        => Claim(ref _preRun) ? services.PreRunModulesAsync(cancellationToken) : default;

    public ValueTask PostRunAsync(IServiceProvider services, CancellationToken cancellationToken)
        => Claim(ref _postRun) ? services.PostRunModulesAsync(cancellationToken) : default;

    /// <summary>Wins the right to run a phase exactly once, from any thread.</summary>
    private static bool Claim(ref int flag) => Interlocked.Exchange(ref flag, 1) == 0;
}

/// <summary>
/// Carries the module lifecycle into a generic host.
/// </summary>
/// <remarks>
/// Without this, <see cref="IModuleInitializeStep"/>, <see cref="IModulePreRunStep"/> and
/// <see cref="IModulePostRunStep"/> would run only under Zero's own application — which is
/// to say, not in an ASP.NET application at all. A module that seeds data or opens a
/// consumer on startup would compile, register, and never run.
/// </remarks>
/// <param name="services">The built provider the phases resolve from.</param>
/// <param name="lifecycle">Ensures a phase runs once even when the application drives it too.</param>
internal sealed class ModuleHostedService(IServiceProvider services, ModuleLifecycle lifecycle) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await lifecycle.InitializeAsync(services, cancellationToken).ConfigureAwait(false);
        await lifecycle.PreRunAsync(services, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => lifecycle.PostRunAsync(services, cancellationToken).AsTask();
}
