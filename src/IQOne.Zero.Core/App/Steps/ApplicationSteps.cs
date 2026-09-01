using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using IQOne.Zero.DependencyInjection.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.App.Steps;

/// Dort fazi birden ezebilmek icin kolaylik tabani.
/// Yalnizca ihtiyac duyulan metodlar override edilir.
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
    public virtual int Order => 0;

    public virtual Task OnInitializeAsync(IApplication application, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public virtual Task OnPreRunAsync(IApplication application, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public virtual Task OnConfigureServicesAsync(IServiceCollection services, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public virtual Task OnPostRunAsync(IApplication application, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
