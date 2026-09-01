using IQOne.Zero.DependencyInjection.Accessors;

namespace IQOne.Zero.App;

/// <summary>
/// A Zero application: the object that owns the service collection, builds the provider
/// and drives the lifecycle steps.
/// </summary>
public interface IApplication :
    IServiceCollectionAccessor,
    IServiceProviderAccessor,
    IAsyncDisposable,
    IDisposable
{
    /// <summary>Container settings applied when the service provider is built.</summary>
    IApplicationOptions Options { get; set; }

    /// <summary>
    /// Runs the configure-services phase, builds the service provider, then runs the
    /// initialize phase against it.
    /// </summary>
    /// <remarks>
    /// Startup is explicit rather than implicit in a constructor: a constructor cannot be
    /// awaited, so failures raised during it are either swallowed or surface on an
    /// unrelated thread. Callers await this and observe the exception at its origin.
    /// </remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initializes the application when it has not started yet, then runs the pre-run phase
    /// so it is ready to accept work.
    /// </summary>
    /// <remarks>
    /// This returns once the application is running; it does not block for the lifetime of
    /// the process. Whoever owns the process decides how long that is, and calls
    /// <see cref="StopAsync"/>.
    /// </remarks>
    Task RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the shutdown steps in reverse order, then disposes the service provider.
    /// </summary>
    /// <remarks>
    /// Disposing the provider is part of stopping: every singleton it built is owned by it,
    /// and with <see cref="IApplicationOptions.ValidateOnBuild"/> on, every one of them was
    /// built. Calling this more than once is safe and does nothing after the first.
    /// </remarks>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>An application whose options are a known concrete type.</summary>
/// <typeparam name="TOptions">The options type this application exposes.</typeparam>
public interface IApplication<TOptions> : IApplication
    where TOptions : IApplicationOptions
{
    /// <summary>Strongly typed view of <see cref="IApplication.Options"/>.</summary>
    new TOptions Options { get; set; }
}
