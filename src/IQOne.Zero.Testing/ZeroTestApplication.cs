using IQOne.Zero.Fundamentals.Disposable;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Testing;

/// <summary>
/// A running Zero application, assembled for a test.
/// </summary>
/// <remarks>
/// <para>
/// The container was built the way the application builds its own: scopes validated,
/// registrations validated at build. A captive dependency, a missing registration or a
/// request with no handler fails here, in a test, with the same message startup would give.
/// </para>
/// <para>
/// Build it with <see cref="Create"/>.
/// </para>
/// </remarks>
public sealed class ZeroTestApplication : AsyncDisposable
{
    private readonly ServiceProvider _provider;

    internal ZeroTestApplication(ServiceProvider provider) => _provider = provider;

    /// <summary>Starts describing an application to build.</summary>
    /// <returns>The builder.</returns>
    public static ZeroTestApplicationBuilder Create() => new();

    /// <summary>
    /// The root provider.
    /// </summary>
    /// <remarks>
    /// Root, not a scope: resolving a scoped service from here throws, exactly as it would in
    /// production. Use <see cref="CreateScope"/> for anything scoped, which includes handlers,
    /// behaviours, validators and <see cref="ISender"/>.
    /// </remarks>
    public IServiceProvider Services => _provider;

    /// <summary>Opens a scope, the test's equivalent of one request.</summary>
    /// <returns>The scope. Dispose it when the test is done with it.</returns>
    public IServiceScope CreateScope() => _provider.CreateScope();

    /// <summary>Sends a request through the real pipeline, in its own scope.</summary>
    /// <remarks>
    /// A scope per send, because that is what a request gets in production: state left in a
    /// scoped service by one send cannot leak into the next, and a service that only works
    /// when reused across requests fails here rather than under load.
    /// </remarks>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="request">What is being asked for.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="MissingRequestHandlerException">No handler is registered for the request.</exception>
    public async Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        using var scope = _provider.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ISender>()
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Resolves a service inside a scope that lives only for the call.</summary>
    /// <remarks>
    /// For asserting on something the application registered — a repository, an options
    /// value — without the test opening and closing a scope by hand. Do not keep the returned
    /// instance: its scope is gone by the time the call returns.
    /// </remarks>
    /// <typeparam name="TService">The service to resolve.</typeparam>
    /// <typeparam name="TResult">What the assertion produces.</typeparam>
    /// <param name="use">Reads what the test needs from the service.</param>
    /// <returns>Whatever <paramref name="use"/> produced.</returns>
    /// <exception cref="InvalidOperationException">The service is not registered.</exception>
    public TResult InScope<TService, TResult>(Func<TService, TResult> use)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(use);

        using var scope = _provider.CreateScope();

        return use(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <inheritdoc />
    protected override void ReleaseManagedResources()
    {
        _provider.Dispose();
        base.ReleaseManagedResources();
    }

    /// <inheritdoc />
    protected override async ValueTask ReleaseManagedResourcesAsync()
    {
        await _provider.DisposeAsync().ConfigureAwait(false);
        await base.ReleaseManagedResourcesAsync().ConfigureAwait(false);
    }
}
