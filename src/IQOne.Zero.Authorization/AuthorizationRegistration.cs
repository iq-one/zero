using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Authorization;

/// <summary>Adds authorization to an application.</summary>
public static class AuthorizationRegistration
{
    /// <summary>
    /// Refuses every request the caller may not make, before the handler or anything else
    /// reads data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing else has to be called. Requirement handlers are found by the generator, the
    /// built-in role and claim handlers are registered here, and the behaviour is added to
    /// the pipeline at <see cref="BehaviorOrder.Authorization"/>.
    /// </para>
    /// <para>
    /// One thing the application still supplies is <see cref="ICurrentUser"/>, because only
    /// the host knows where an identity comes from. Until it does, a caller who is never
    /// authenticated is used, so a host that forgets refuses protected requests rather than
    /// serving them to nobody in particular.
    /// </para>
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Declares policies and adjusts the settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A setting cannot be used as it stands.</exception>
    public static IServiceCollection AddZeroAuthorization(
        this IServiceCollection services, Action<AuthorizationOptions>? configure = null)
    {
        var options = new AuthorizationOptions();
        configure?.Invoke(options);

        // Checked and sealed before anything can resolve it, so a bad setting stops startup
        // rather than surfacing on the first request that happens to exercise it.
        // Checked now, sealed later. Checking here means a nonsensical setting stops startup
        // even in a host with no modules; sealing here would be before the modules run, and a
        // module that owns a set of routes owns the policies guarding them.
        services.AddSingleton(options.Validate());

        services.AddSingleton<IQOne.Zero.Modules.IModuleFeatureContributor>(
            new AuthorizationFeatureContributor(options));

        // TryAdd, so a host that registers its own is unaffected whichever order the two calls
        // happen in: added first, this one is skipped; added after, it wins as the later
        // registration does.
        services.TryAddScoped(_ => CurrentUser.Anonymous);

        // Reports the omission once at startup. The fallback above keeps a host with no
        // notion of a user working; this keeps forgetting from looking the same as meaning it.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, CurrentUserCheck>());

        services.AddScoped<IResourceAuthorizer, ResourceAuthorizer>();
        services.AddScoped<IRequirementHandler<RolesRequirement>, RolesRequirementHandler>();
        services.AddScoped<IRequirementHandler<ClaimRequirement>, ClaimRequirementHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));

        return services;
    }

    /// <summary>
    /// Registers a requirement handler explicitly.
    /// </summary>
    /// <remarks>
    /// For tests and for hosts that assemble their registrations by hand. An application
    /// writes the handler and lets the generator find it: a class implementing
    /// <see cref="IRequirementHandler{TRequirement}"/> is registered at build time, so there
    /// is nothing to list and nothing to forget.
    /// </remarks>
    /// <typeparam name="TRequirement">The requirement decided.</typeparam>
    /// <typeparam name="THandler">The class that decides it.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRequirementHandler<TRequirement, THandler>(this IServiceCollection services)
        where TRequirement : IAuthorizationRequirement
        where THandler : class, IRequirementHandler<TRequirement>
    {
        services.AddScoped<IRequirementHandler<TRequirement>, THandler>();

        return services;
    }

    /// <summary>Registers a resource requirement handler explicitly.</summary>
    /// <remarks>As with the other overload, an application normally lets the generator do this.</remarks>
    /// <typeparam name="TRequirement">The requirement decided.</typeparam>
    /// <typeparam name="TResource">What it is decided against.</typeparam>
    /// <typeparam name="THandler">The class that decides it.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRequirementHandler<TRequirement, TResource, THandler>(
        this IServiceCollection services)
        where TRequirement : IAuthorizationRequirement
        where THandler : class, IRequirementHandler<TRequirement, TResource>
    {
        services.AddScoped<IRequirementHandler<TRequirement, TResource>, THandler>();

        return services;
    }
}
