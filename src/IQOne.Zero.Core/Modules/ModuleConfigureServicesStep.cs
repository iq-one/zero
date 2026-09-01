using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Modules;

/// <summary>Bridges module configuration into the application step pipeline.</summary>
/// <param name="modules">The modules, already in dependency order.</param>
public sealed class ModuleConfigureServicesStep(IReadOnlyList<IModule> modules) : IApplicationConfigureServicesStep
{
    /// <summary>Runs early so later steps see the modules' registrations.</summary>
    public int Order => -500;

    /// <inheritdoc />
    public Task OnConfigureServicesAsync(IServiceCollection services, CancellationToken cancellationToken)
        => services.AddModulesAsync(modules, cancellationToken).AsTask();
}
