using IQOne.Zero.Messaging;
using IQOne.Zero.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Testing;

/// <summary>Starts a harness around one handler.</summary>
/// <remarks>
/// A static entry point exists only so the type arguments can be written once, at the call
/// site, instead of on a constructor.
/// </remarks>
public static class HandlerHarness
{
    /// <summary>Runs an already-constructed handler.</summary>
    /// <remarks>
    /// The usual choice: the test builds the handler with the fakes it wants, so nothing has
    /// to be registered for the dependencies at all.
    /// </remarks>
    /// <typeparam name="TRequest">The request handled.</typeparam>
    /// <typeparam name="TResponse">What handling it produces.</typeparam>
    /// <param name="handler">The handler under test.</param>
    /// <returns>The harness.</returns>
    public static HandlerHarness<TRequest, TResponse> For<TRequest, TResponse>(
        IRequestHandler<TRequest, TResponse> handler)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(handler);

        return new HandlerHarness<TRequest, TResponse>(
            services => services.AddSingleton(handler));
    }

    /// <summary>Runs a handler built from the harness's own container.</summary>
    /// <remarks>
    /// For a handler that must be constructed per scope, or one whose dependencies were
    /// registered with <see cref="HandlerHarness{TRequest,TResponse}.WithService{TService}"/>.
    /// </remarks>
    /// <typeparam name="TRequest">The request handled.</typeparam>
    /// <typeparam name="TResponse">What handling it produces.</typeparam>
    /// <param name="handler">Builds the handler from the scope it will run in.</param>
    /// <returns>The harness.</returns>
    public static HandlerHarness<TRequest, TResponse> For<TRequest, TResponse>(
        Func<IServiceProvider, IRequestHandler<TRequest, TResponse>> handler)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(handler);

        return new HandlerHarness<TRequest, TResponse>(
            services => services.AddScoped(handler));
    }
}

/// <summary>
/// Runs one handler, with or without the pipeline around it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SendAsync"/> goes through <see cref="RequestPipeline"/> — the same code the
/// generated dispatch table calls — so behaviours wrap the handler in exactly the order they
/// would in production. <see cref="HandleAsync"/> calls the handler alone, for a test that is
/// about the handler's own logic and would only be slowed down by the pipeline.
/// </para>
/// <para>
/// The container is built on the first run, with the validations an application turns on, so
/// a missing registration surfaces here rather than at startup. Each run gets its own scope,
/// which is what one request gets in production.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
public sealed class HandlerHarness<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly List<Action<IServiceCollection>> _registrations = [];

    private ServiceProvider? _provider;
    private bool _validationAdded;

    internal HandlerHarness(Action<IServiceCollection> registerHandler) => _registrations.Add(registerHandler);

    /// <summary>Puts a behaviour in the pipeline for this test.</summary>
    /// <remarks>
    /// Position comes from the behaviour's own <see cref="IPipelineBehavior{TRequest,TResponse}.Order"/>,
    /// not from the order these calls are made in — the same rule the real pipeline follows.
    /// </remarks>
    /// <param name="behavior">The behaviour to add.</param>
    /// <returns>This harness, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The harness has already run.</exception>
    public HandlerHarness<TRequest, TResponse> WithBehavior(IPipelineBehavior<TRequest, TResponse> behavior)
    {
        ArgumentNullException.ThrowIfNull(behavior);

        return Configure(services => services.AddSingleton(behavior));
    }

    /// <summary>Runs this validator before the handler, as the application would.</summary>
    /// <remarks>
    /// Adds Zero's validation behaviour the first time it is called. Validation is not added
    /// otherwise: a harness that validated without being asked would report a failure the
    /// test never set up.
    /// </remarks>
    /// <param name="validator">The validator to apply.</param>
    /// <returns>This harness, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The harness has already run.</exception>
    public HandlerHarness<TRequest, TResponse> WithValidator(IValidator<TRequest> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        if (!_validationAdded)
        {
            Configure(services => services.AddZeroValidation());
            _validationAdded = true;
        }

        return Configure(services => services.AddSingleton(validator));
    }

    /// <summary>Registers a dependency a behaviour or a handler factory needs.</summary>
    /// <typeparam name="TService">The service type to register it under.</typeparam>
    /// <param name="instance">The instance, usually a fake.</param>
    /// <returns>This harness, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The harness has already run.</exception>
    public HandlerHarness<TRequest, TResponse> WithService<TService>(TService instance)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);

        return Configure(services => services.AddSingleton(instance));
    }

    /// <summary>Adds registrations the other methods do not cover.</summary>
    /// <param name="configure">Adds to the harness's service collection.</param>
    /// <returns>This harness, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The harness has already run.</exception>
    public HandlerHarness<TRequest, TResponse> WithServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return Configure(configure);
    }

    /// <summary>Runs the request through the behaviours and then the handler.</summary>
    /// <param name="request">What is being asked for.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome.</returns>
    public async Task<Result<TResponse>> SendAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = Build().CreateScope();

        return await RequestPipeline
            .RunAsync<TRequest, TResponse>(request, scope.ServiceProvider, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Runs the handler alone, skipping every behaviour.</summary>
    /// <remarks>
    /// Use this when the test is about what the handler does, not about what wraps it. If the
    /// question is whether something can be reached without validation or authorization, the
    /// answer belongs in a test of the pipeline, not here.
    /// </remarks>
    /// <param name="request">What is being asked for.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome.</returns>
    public async Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        using var scope = Build().CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<IRequestHandler<TRequest, TResponse>>()
            .HandleAsync(request, cancellationToken)
            .ConfigureAwait(false);
    }

    private HandlerHarness<TRequest, TResponse> Configure(Action<IServiceCollection> registration)
    {
        // Refused rather than ignored: a behaviour added after the first run would silently
        // not be in the pipeline, and the test would pass for the wrong reason.
        if (_provider is not null)
            throw new InvalidOperationException(
                "The harness has already run, so its container is built. Configure the harness " +
                "fully before the first SendAsync or HandleAsync call, or start a new one.");

        _registrations.Add(registration);

        return this;
    }

    private ServiceProvider Build()
    {
        if (_provider is not null) return _provider;

        var services = new ServiceCollection();

        foreach (var register in _registrations) register(services);

        // The validations an application turns on. A harness that quietly allowed what startup
        // refuses would report success for wiring that cannot run.
        return _provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
