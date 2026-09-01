using IQOne.Zero.App.Steps;
using IQOne.Zero.DependencyInjection.Extensions;
using IQOne.Zero.Fundamentals.Disposable;
using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.App;

/// <summary>
/// Drives the application lifecycle: configure services, build the provider, initialize,
/// pre-run — and, on the way down, post-run.
/// </summary>
/// <remarks>
/// Steps execute sequentially in <c>Order</c>. Running them concurrently would race on
/// <see cref="IServiceCollection"/>, which is not thread-safe.
/// </remarks>
public class Application : AsyncDisposable, IApplication
{
    private bool _initialized;
    private bool _stopped;

    private IReadOnlyList<IApplicationInitializeStep> _initializeSteps = [];
    private IReadOnlyList<IApplicationPreRunStep> _preRunSteps = [];
    private IReadOnlyList<IApplicationPostRunStep> _postRunSteps = [];

    /// <summary>Builds on an existing service collection.</summary>
    /// <param name="serviceCollection">Registrations to start from.</param>
    public Application(IServiceCollection serviceCollection) => ServiceCollection = serviceCollection;

    /// <summary>Builds on a fresh service collection.</summary>
    public Application() : this(new ServiceCollection()) { }

    /// <inheritdoc />
    public virtual IServiceCollection ServiceCollection { get; set; }

    /// <inheritdoc />
    /// <remarks>Null until <see cref="InitializeAsync"/> has built it.</remarks>
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

        foreach (var step in Steps<IApplicationConfigureServicesStep>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            await step.OnConfigureServicesAsync(ServiceCollection, cancellationToken).ConfigureAwait(false);
        }

        // Discovered before the provider is built, and only then, because reading a step out
        // of the collection also pins it there: after the build, the container has already
        // taken its copy of the registrations and would construct a second object.
        _initializeSteps = Steps<IApplicationInitializeStep>();
        _preRunSteps = Steps<IApplicationPreRunStep>();
        _postRunSteps = [.. Steps<IApplicationPostRunStep>().Reverse()];

        ServiceProvider = await CreateServiceProviderAsync(cancellationToken).ConfigureAwait(false);

        foreach (var step in _initializeSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await step.OnInitializeAsync(this, cancellationToken).ConfigureAwait(false);
        }

        await ModuleLifecycleOf(ServiceProvider).InitializeAsync(ServiceProvider, cancellationToken).ConfigureAwait(false);

        await OnInitializedAsync(cancellationToken).ConfigureAwait(false);

        _initialized = true;
        _stopped = false;
    }

    /// <inheritdoc />
    public virtual async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        foreach (var step in _preRunSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await step.OnPreRunAsync(this, cancellationToken).ConfigureAwait(false);
        }

        await ModuleLifecycleOf(ServiceProvider).PreRunAsync(ServiceProvider, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The guard that keeps each module phase to a single run.
    /// </summary>
    /// <remarks>
    /// A generic host runs the same phases through a hosted service, so an application
    /// hosted inside one would otherwise initialise every module twice. Absent when the
    /// application registered no modules, in which case there is nothing to run.
    /// </remarks>
    private static Modules.ModuleLifecycle ModuleLifecycleOf(IServiceProvider services)
        => services.GetService(typeof(Modules.ModuleLifecycle)) as Modules.ModuleLifecycle
           ?? new Modules.ModuleLifecycle();

    /// <inheritdoc />
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped) return;

        _stopped = true;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ServiceProvider is not null)
            {
                foreach (var step in _postRunSteps)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await step.OnPostRunAsync(this, cancellationToken).ConfigureAwait(false);
                }

                await ModuleLifecycleOf(ServiceProvider).PostRunAsync(ServiceProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // In a finally: a shutdown step that throws, or a token that cancels mid-way,
            // must not leave the container and every singleton it built behind.
            await ReleaseProviderAsync().ConfigureAwait(false);
        }
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

    /// <summary>Runs before the configure-services steps.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    protected virtual Task OnInitializingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Runs after the initialize steps, with the provider already built.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    protected virtual Task OnInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Reads steps from the service collection before a provider exists.</summary>
    /// <remarks>Only instances marked <c>ISingletonInstance</c> can be materialized this early.</remarks>
    private IReadOnlyList<TStep> Steps<TStep>() where TStep : IApplicationStep
        => [.. ServiceCollection.GetRegisteredInstances<TStep>().OrderBy(s => s.Order)];

    private async ValueTask ReleaseProviderAsync()
    {
        switch (ServiceProvider)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;

            case IDisposable disposable:
                disposable.Dispose();
                break;
        }

        Reset();
    }

    private void Reset()
    {
        ServiceProvider = null!;
        ServiceCollection.Clear();

        _initializeSteps = [];
        _preRunSteps = [];
        _postRunSteps = [];
        _initialized = false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The shutdown steps are asynchronous and this path cannot await them, so it releases
    /// the container without running them — blocking here is the deadlock the framework bans.
    /// Call <see cref="StopAsync"/> or <c>DisposeAsync</c> to run them.
    /// </remarks>
    protected override void ReleaseManagedResources()
    {
        if (!_stopped)
        {
            _stopped = true;

            (ServiceProvider as IDisposable)?.Dispose();

            Reset();
        }

        base.ReleaseManagedResources();
    }

    /// <inheritdoc />
    protected override async ValueTask ReleaseManagedResourcesAsync()
    {
        await StopAsync().ConfigureAwait(false);
        await base.ReleaseManagedResourcesAsync().ConfigureAwait(false);
    }
}
