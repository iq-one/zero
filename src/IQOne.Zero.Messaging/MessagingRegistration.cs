using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Messaging;

/// <summary>Adds commands, queries and their pipeline to an application.</summary>
public static class MessagingRegistration
{
    /// <summary>
    /// Lets modules register their handlers into one dispatch table, freezes it once every
    /// module has been configured, and refuses to start when a request has no handler.
    /// </summary>
    /// <remarks>Call this before <c>AddModules</c>.</remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts how messaging behaves.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroMessaging(
        this IServiceCollection services, Action<MessagingOptions>? configure = null)
    {
        var options = new MessagingOptions();
        configure?.Invoke(options);

        services.AddSingleton<IModuleFeatureContributor>(new MessagingFeatureContributor(options));

        return services;
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
internal sealed class MessagingFeatureContributor(MessagingOptions options) : IModuleFeatureContributor
{
    private readonly RequestRegistry _registry = new();

    public void Contribute(IModuleFeatureCollection features)
        => features.Set<IRequestRegistryBuilder>(_registry);

    public void Complete(IServiceCollection services)
    {
        _registry.Freeze();

        if (options.RequireHandlerForEveryRequest && _registry.Unhandled.Count > 0)
            throw new InvalidOperationException(
                "These requests have no handler: " +
                string.Join(", ", _registry.Unhandled.Select(t => t.FullName).Order(StringComparer.Ordinal)) +
                ". Implement IRequestHandler<,> for each, or set " +
                $"{nameof(MessagingOptions)}.{nameof(MessagingOptions.RequireHandlerForEveryRequest)} to false.");

        services.AddSingleton(_registry);
        services.AddScoped<ISender, Sender>();
    }
}

/// <summary>Reaches the dispatch table from inside a module's configure-services step.</summary>
public static class ModuleServiceContextExtensions
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
