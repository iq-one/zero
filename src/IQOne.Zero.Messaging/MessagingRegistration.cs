using IQOne.Zero.DependencyInjection.Extensions;
using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Messaging;

/// <summary>Adds commands, queries and their pipeline to an application.</summary>
public static class MessagingRegistration
{
    /// <summary>
    /// Registers the sender and the dispatch table, offers that table to modules, and refuses
    /// to start when a request has no handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This call alone is enough: <see cref="ISender"/>, <see cref="RequestRegistry"/> and
    /// <see cref="MessagingOptions"/> are all resolvable afterwards. The table is filled
    /// while modules configure and sealed when the last one has, so call this before
    /// <c>AddModules</c>; sending before the table is sealed says so.
    /// </para>
    /// <para>Calling it twice does nothing the second time.</para>
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts how messaging behaves.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroMessaging(
        this IServiceCollection services, Action<MessagingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetRegisteredInstance<RequestRegistry>() is not null) return services;

        var options = new MessagingOptions();

        configure?.Invoke(options);

        var registry = Register(services, options);

        services.AddSingleton<IModuleFeatureContributor>(new MessagingFeatureContributor(options, registry));

        return services;
    }

    /// <summary>
    /// Builds the dispatch table from an explicit set of handlers, bypassing the module phase.
    /// </summary>
    /// <remarks>
    /// For tests and for hosts that assemble their table by hand. An application calls
    /// <see cref="AddZeroMessaging"/> and lets the generator fill the table. The name differs
    /// from that one on purpose: as an overload it was ambiguous at the call site — a lambda
    /// with an untyped parameter matched both, and the two do opposite things.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adds the entries.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroMessagingWithRequests(
        this IServiceCollection services, Action<IRequestRegistryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var registry = Register(services, new MessagingOptions());

        configure(registry);
        registry.Freeze();

        return services;
    }

    /// <summary>
    /// What both entry points register.
    /// </summary>
    /// <remarks>
    /// The registry and the sender are registered here rather than when the module phase
    /// completes, because a capability's <c>Add</c> has to be sufficient on its own — and the
    /// module phase does not run at all in an application that has no modules.
    /// </remarks>
    private static RequestRegistry Register(IServiceCollection services, MessagingOptions options)
    {
        var registry = new RequestRegistry();

        services.AddSingleton(options);
        services.AddSingleton(registry);
        services.AddScoped<ISender, Sender>();

        return registry;
    }
}

/// <summary>How messaging behaves.</summary>
public sealed class MessagingOptions
{
    /// <summary>
    /// Whether a request with no handler stops startup. On by default.
    /// </summary>
    /// <remarks>
    /// The alternative is finding out when a caller sends that request, which in practice
    /// means in production, on the path nobody exercised. Turn it off only while a module is
    /// deliberately half-built.
    /// </remarks>
    public bool RequireHandlerForEveryRequest { get; set; } = true;
}

/// <summary>
/// Offers the dispatch table to modules while they configure, then seals and checks it.
/// </summary>
/// <remarks>
/// The module system knows nothing about messaging: it only runs the contributors it finds.
/// Adding messaging to an application is a registration, not a change to the core.
/// </remarks>
internal sealed class MessagingFeatureContributor(MessagingOptions options, RequestRegistry registry)
    : IModuleFeatureContributor
{
    public void Contribute(IModuleFeatureCollection features)
        => features.Set<IRequestRegistryBuilder>(registry);

    /// <remarks>
    /// Sealing only. The registry and the sender were registered by <c>AddZeroMessaging</c>,
    /// so they exist whether or not the application has modules.
    /// </remarks>
    public void Complete(IServiceCollection services)
    {
        registry.Freeze();

        if (options.RequireHandlerForEveryRequest && registry.Unhandled.Count > 0)
            throw new InvalidOperationException(
                "These requests have no handler: " +
                string.Join(", ", registry.Unhandled.Select(t => t.FullName).Order(StringComparer.Ordinal)) +
                ". Implement IRequestHandler<,> for each, or set " +
                $"{nameof(MessagingOptions)}.{nameof(MessagingOptions.RequireHandlerForEveryRequest)} to false.");
    }
}

/// <summary>Reaches the dispatch table from inside a module's configure-services step.</summary>
public static class MessagingModuleContextExtensions
{
    /// <summary>The dispatch table a module registers its handlers into.</summary>
    /// <param name="context">The module's configure-services context.</param>
    /// <returns>The registry builder.</returns>
    /// <exception cref="InvalidOperationException">
    /// Messaging was not added to the application; call <c>AddZeroMessaging()</c> first.
    /// </exception>
    public static IRequestRegistryBuilder Requests(this IModuleServiceContext context)
        => context.Feature<IRequestRegistryBuilder>();
}
