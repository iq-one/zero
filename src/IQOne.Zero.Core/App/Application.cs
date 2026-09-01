using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using IQOne.Zero.DependencyInjection.Extensions;
using IQOne.Zero.Fundamentals.Disposable;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.App;

/// <summary>
/// Drives the application lifecycle: initialize, pre-run, configure services,
/// build the provider, post-run.
/// </summary>
/// <remarks>
/// Steps execute sequentially in <c>Order</c>. Running them concurrently would race on
/// <see cref="IServiceCollection"/>, which is not thread-safe.
/// </remarks>
public class Application : AsyncDisposable, IApplication
{
    private bool _initialized;

    /// <summary>Builds on an existing service collection.</summary>
    /// <param name="serviceCollection">Registrations to start from.</param>
    public Application(IServiceCollection serviceCollection) => ServiceCollection = serviceCollection;

    /// <summary>Builds on a fresh service collection.</summary>
    public Application() : this(new ServiceCollection()) { }

    /// <inheritdoc />
    public virtual IServiceCollection ServiceCollection { get; set; }

    /// <inheritdoc />
    /// <remarks>Null until <see cref="RunAsync"/> has built it.</remarks>
    public virtual IServiceProvider ServiceProvider { get; set; } = null!;

    /// <inheritdoc />
    public IApplicationOptions Options { get; set; } = new ApplicationOptions();

    /// <inheritdoc />
    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        ServiceCollection.AddSingleton<IApplication>(this);
        ServiceCollection.AddSingleton(ServiceCollection);

        await OnInitializingAsync(cancellationToken).ConfigureAwait(false);

        foreach (var step in Steps<IApplicationInitializeStep>())
            await step.OnInitializeAsync(this, cancellationToken).ConfigureAwait(false);

        await OnInitializedAsync(cancellationToken).ConfigureAwait(false);

        _initialized = true;
    }

    /// <inheritdoc />
    public virtual async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        foreach (var step in Steps<IApplicationPreRunStep>())
            await step.OnPreRunAsync(this, cancellationToken).ConfigureAwait(false);

        foreach (var step in Steps<IApplicationConfigureServicesStep>())
            await step.OnConfigureServicesAsync(ServiceCollection, cancellationToken).ConfigureAwait(false);

        ServiceProvider = await CreateServiceProviderAsync(cancellationToken).ConfigureAwait(false);

        foreach (var step in Steps<IApplicationPostRunStep>())
            await step.OnPostRunAsync(this, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual Task StopAsync(CancellationToken cancellationToken = default)
    {
        ServiceCollection.Clear();
        ServiceProvider = null!;

        return Task.CompletedTask;
    }

    /// <summary>Builds the container. Override to supply a different one.</summary>
    /// <param name="cancellationToken">Cancels the build.</param>
    /// <returns>The built provider.</returns>
    protected virtual Task<IServiceProvider> CreateServiceProviderAsync(CancellationToken cancellationToken)
        => Task.FromResult<IServiceProvider>(ServiceCollection.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = Options.ValidateScopes,
                ValidateOnBuild = Options.ValidateOnBuild
            }));

    /// <summary>Runs before the initialize steps.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    protected virtual Task OnInitializingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs after the initialize steps.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    protected virtual Task OnInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Reads steps from the service collection before a provider exists.</summary>
    /// <remarks>Only instances marked <c>ISingletonInstance</c> can be materialized this early.</remarks>
    private IEnumerable<TStep> Steps<TStep>() where TStep : IApplicationStep
        => ServiceCollection.GetServiceCollection<TStep>().OrderBy(s => s.Order);

    /// <inheritdoc />
    protected override void ReleaseManagedResources()
    {
        StopAsync().GetAwaiter().GetResult();
        base.ReleaseManagedResources();
    }

    /// <inheritdoc />
    protected override async ValueTask ReleaseManagedResourcesAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await base.ReleaseManagedResourcesAsync().ConfigureAwait(false);
    }
}
