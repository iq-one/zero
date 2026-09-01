using IQOne.Zero.DependencyInjection.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.App.Steps;

/// <summary>
/// Convenience base for taking part in several lifecycle phases from one type. Override
/// only the phases you need; the rest do nothing.
/// </summary>
[ServiceTypes<IApplicationInitializeStep>]
[ServiceTypes<IApplicationPreRunStep>]
[ServiceTypes<IApplicationConfigureServicesStep>]
[ServiceTypes<IApplicationPostRunStep>]
public abstract class ApplicationSteps :
    IApplicationInitializeStep,
    IApplicationPreRunStep,
    IApplicationConfigureServicesStep,
    IApplicationPostRunStep
{
    /// <inheritdoc />
    public virtual int Order => 0;

    /// <inheritdoc />
    public virtual Task OnInitializeAsync(IApplication application, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task OnPreRunAsync(IApplication application, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task OnConfigureServicesAsync(IServiceCollection services, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public virtual Task OnPostRunAsync(IApplication application, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
