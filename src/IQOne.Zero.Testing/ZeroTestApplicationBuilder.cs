using IQOne.Zero.Messaging;
using IQOne.Zero.Modules;
using IQOne.Zero.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Testing;

/// <summary>
/// Describes the application a test wants, then builds it.
/// </summary>
/// <remarks>
/// <para>
/// Everything a Zero application needs is already on: messaging with its dispatch table,
/// validation in the pipeline, and a container with <c>ValidateScopes</c> and
/// <c>ValidateOnBuild</c>. What is left for the test to say is which handlers, validators,
/// behaviours and modules take part.
/// </para>
/// <para>
/// Validation is registered whether or not the test adds a validator. A test application that
/// quietly left it out would let through a request the running application rejects, which is
/// the one difference a test must never have. For the same reason, do not call
/// <c>AddZeroMessaging</c> or <c>AddZeroValidation</c> from <see cref="AddServices"/>: they are
/// already here, and a second registration would run every validator twice.
/// </para>
/// </remarks>
public sealed class ZeroTestApplicationBuilder
{
    private readonly List<Action<IServiceCollection>> _services = [];
    private readonly List<Action<IRequestRegistryBuilder>> _requests = [];
    private readonly List<IModule> _modules = [];
    private readonly List<KeyValuePair<string, string?>> _settings = [];

    internal ZeroTestApplicationBuilder() { }

    /// <summary>Adds a handler instance and its row in the dispatch table.</summary>
    /// <remarks>
    /// The row is what the generator would have emitted for this handler, so the request
    /// travels the same path here as in the application: sender, table, pipeline, handler.
    /// </remarks>
    /// <typeparam name="TRequest">The request handled.</typeparam>
    /// <typeparam name="TResponse">What handling it produces.</typeparam>
    /// <param name="handler">The handler, already built with whatever fakes the test wants.</param>
    /// <returns>This builder, for chaining.</returns>
    public ZeroTestApplicationBuilder AddHandler<TRequest, TResponse>(
        IRequestHandler<TRequest, TResponse> handler)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(handler);

        _services.Add(services => services.AddSingleton(handler));

        return Dispatch<TRequest, TResponse>(handler.GetType());
    }

    /// <summary>Adds a handler the container constructs, and its row in the dispatch table.</summary>
    /// <remarks>
    /// Use this to put the handler's real constructor under test: with the container
    /// validating on build, a dependency nobody registered fails the build rather than the
    /// first send.
    /// </remarks>
    /// <typeparam name="TRequest">The request handled.</typeparam>
    /// <typeparam name="TResponse">What handling it produces.</typeparam>
    /// <typeparam name="THandler">The handler type to construct.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public ZeroTestApplicationBuilder AddHandler<TRequest, TResponse, THandler>()
        where TRequest : IRequest<TResponse>
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        _services.Add(services => services.AddScoped<IRequestHandler<TRequest, TResponse>, THandler>());

        return Dispatch<TRequest, TResponse>(typeof(THandler));
    }

    /// <summary>Adds a validator instance, run by the pipeline before the handler.</summary>
    /// <typeparam name="T">What the validator checks, normally the request type.</typeparam>
    /// <param name="validator">The validator.</param>
    /// <returns>This builder, for chaining.</returns>
    public ZeroTestApplicationBuilder AddValidator<T>(IValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        _services.Add(services => services.AddSingleton(validator));

        return this;
    }

    /// <summary>Adds a validator the container constructs, for one that takes dependencies.</summary>
    /// <typeparam name="T">What the validator checks, normally the request type.</typeparam>
    /// <typeparam name="TValidator">The validator type to construct.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public ZeroTestApplicationBuilder AddValidator<T, TValidator>()
        where TValidator : class, IValidator<T>
    {
        _services.Add(services => services.AddScoped<IValidator<T>, TValidator>());

        return this;
    }

    /// <summary>Puts a behaviour in the pipeline for one request type.</summary>
    /// <typeparam name="TRequest">The request wrapped.</typeparam>
    /// <typeparam name="TResponse">What handling produces.</typeparam>
    /// <param name="behavior">The behaviour.</param>
    /// <returns>This builder, for chaining.</returns>
    public ZeroTestApplicationBuilder AddBehavior<TRequest, TResponse>(
        IPipelineBehavior<TRequest, TResponse> behavior)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(behavior);

        _services.Add(services => services.AddSingleton(behavior));

        return this;
    }

    /// <summary>Puts a behaviour in the pipeline for every request.</summary>
    /// <remarks>
    /// The open generic form, as an application registers its own cross-cutting behaviours:
    /// <c>AddBehavior(typeof(LoggingBehavior&lt;,&gt;))</c>.
    /// </remarks>
    /// <param name="behaviorType">An open generic type implementing <see cref="IPipelineBehavior{TRequest,TResponse}"/>.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException">The type is not an open generic behaviour.</exception>
    public ZeroTestApplicationBuilder AddBehavior(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        // Checked here rather than left to the container, whose complaint arrives on the
        // first send and names a generic arity rather than the mistake.
        if (!behaviorType.IsGenericTypeDefinition || !Implements(behaviorType, typeof(IPipelineBehavior<,>)))
            throw new ArgumentException(
                $"'{behaviorType.Name}' is not an open generic pipeline behaviour. Pass something like " +
                "typeof(LoggingBehavior<,>), or use the overload that takes an instance.",
                nameof(behaviorType));

        _services.Add(services => services.AddScoped(typeof(IPipelineBehavior<,>), behaviorType));

        return this;
    }

    /// <summary>
    /// Adds a module, normally the <c>Module</c> class the generator wrote for an assembly.
    /// </summary>
    /// <remarks>
    /// This is the closest a test gets to the real application: the module registers every
    /// service, handler and request the assembly declares, and the same startup check runs —
    /// a request the module declares but nobody handles fails the build, naming it.
    /// </remarks>
    /// <param name="module">The module to configure.</param>
    /// <returns>This builder, for chaining.</returns>
    public ZeroTestApplicationBuilder AddModule(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        _modules.Add(module);

        return this;
    }

    /// <summary>Supplies settings, as an <c>appsettings.json</c> would.</summary>
    /// <remarks>
    /// Keys are the usual colon-separated paths — <c>("Mail:Host", "smtp.example.com")</c> —
    /// so an options type binds here exactly as it does in the application. An
    /// <see cref="IConfiguration"/> is registered whether or not this is called, because a
    /// module that binds options needs one to exist; leaving out a setting then fails the way
    /// a missing setting fails at startup, rather than with a puzzle about a missing service.
    /// </remarks>
    /// <param name="settings">The keys and values the test wants configured.</param>
    /// <returns>This builder, for chaining.</returns>
    public ZeroTestApplicationBuilder AddConfiguration(params (string Key, string? Value)[] settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        foreach (var (key, value) in settings) _settings.Add(new KeyValuePair<string, string?>(key, value));

        return this;
    }

    /// <summary>Registers whatever the other methods do not cover.</summary>
    /// <remarks>
    /// The escape hatch: fakes for the application's own abstractions, options, a logger, or
    /// another Zero capability's <c>Add</c> call. It runs before the modules are configured,
    /// so a capability added here is available to them.
    /// </remarks>
    /// <param name="configure">Adds to the application's service collection.</param>
    /// <returns>This builder, for chaining.</returns>
    public ZeroTestApplicationBuilder AddServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _services.Add(configure);

        return this;
    }

    /// <summary>
    /// Configures the modules, builds the container and hands back the application.
    /// </summary>
    /// <remarks>
    /// Asynchronous because configuring a module is: a module may reach a dependency while it
    /// registers. There is no synchronous overload, so that a test written without modules
    /// does not have to be rewritten when one is added.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the module phase.</param>
    /// <returns>The built application. Dispose it when the test ends.</returns>
    /// <exception cref="InvalidOperationException">
    /// A request has no handler, or two handlers claim one request. The message names it.
    /// </exception>
    /// <exception cref="AggregateException">
    /// A registration cannot be satisfied, or a singleton holds a scoped service. This is what
    /// <c>ValidateOnBuild</c> reports, and the inner messages name the service.
    /// </exception>
    public async ValueTask<ZeroTestApplication> BuildAsync(CancellationToken cancellationToken = default)
    {
        var services = new ServiceCollection();

        // Before the modules: messaging contributes the dispatch table they register into,
        // and seals it once they are done.
        services.AddZeroMessaging();
        services.AddZeroValidation();

        // Registered before the test's own callbacks, so a test that brings its own
        // configuration replaces this one rather than fighting it.
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(_settings).Build());

        foreach (var configure in _services) configure(services);

        // The explicit registrations travel as a module of their own, so both routes into the
        // dispatch table are the same route and a duplicate handler is caught either way.
        var modules = new List<IModule>(_modules) { new ExplicitRegistrationsModule(_requests) };

        await services.AddModulesAsync(modules, cancellationToken).ConfigureAwait(false);

        return new ZeroTestApplication(services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
    }

    private ZeroTestApplicationBuilder Dispatch<TRequest, TResponse>(Type handlerType)
        where TRequest : IRequest<TResponse>
    {
        _requests.Add(builder => builder.Add(new RequestEntry(
            typeof(TRequest),
            typeof(TResponse),
            handlerType,
            static (services, request, cancellationToken) =>
                RequestPipeline.RunAsync<TRequest, TResponse>((TRequest)request, services, cancellationToken))));

        return this;
    }

    private static bool Implements(Type type, Type openGenericInterface)
        => type.GetInterfaces().Any(candidate =>
            candidate.IsGenericType && candidate.GetGenericTypeDefinition() == openGenericInterface);
}

/// <summary>
/// Carries the handlers a test registered by hand into the dispatch table.
/// </summary>
/// <remarks>
/// A module rather than a direct call to the registry, because the registry only exists
/// during the module phase — and going through the same door as the generated modules means
/// two handlers for one request are refused no matter which side they came from.
/// </remarks>
/// <param name="registrations">What to add to the table.</param>
internal sealed class ExplicitRegistrationsModule(IReadOnlyList<Action<IRequestRegistryBuilder>> registrations)
    : IModule, IModuleConfigureServicesStep
{
    public string Name => "IQOne.Zero.Testing";

    public ValueTask OnConfigureServicesAsync(
        IModuleServiceContext context, CancellationToken cancellationToken)
    {
        var requests = context.Requests();

        foreach (var register in registrations) register(requests);

        return default;
    }
}
