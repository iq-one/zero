using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using IQOne.Zero.Modules;
using IQOne.Zero.Messaging.Dispatch;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Modules;

/// <summary>Bridges module configuration into the application step pipeline.</summary>
public sealed class ModuleConfigureServicesStep(IReadOnlyList<IModule> modules) : IApplicationConfigureServicesStep
{
    public int Order => -500;

    public Task OnConfigureServicesAsync(IServiceCollection services, CancellationToken cancellationToken)
        => services.AddModulesAsync(modules, cancellationToken).AsTask();
}
