using IQOne.Zero.Modules;
using IQOne.Zero.Web.Binding;
using IQOne.Zero.Web.Writing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Web;

/// <summary>Adds HTTP endpoints for commands and queries.</summary>
public static class WebRegistration
{
    /// <summary>
    /// Lets modules contribute their endpoints, and registers what reads requests and writes
    /// responses.
    /// </summary>
    /// <remarks>
    /// Call this before <c>AddModules</c>, then <c>MapZeroEndpoints</c> on the app. The
    /// binder and the writer are registered only if nothing has claimed them, so an
    /// application that publishes its own wire contract registers an
    /// <see cref="IResponseWriter"/> first and keeps it.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts routing, authorization, limits and status codes.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroWeb(
        this IServiceCollection services, Action<ZeroWebOptions>? configure = null)
    {
        if (configure is not null) services.Configure(configure);

        services.AddOptions<ZeroWebOptions>();
        services.TryAddSingleton<IRequestBinder, JsonRequestBinder>();
        services.TryAddSingleton<IResponseWriter, JsonResponseWriter>();
        services.AddSingleton<IModuleFeatureContributor>(new WebFeatureContributor());

        return services;
    }

    /// <summary>
    /// Maps every endpoint the modules contributed.
    /// </summary>
    /// <remarks>
    /// One real ASP.NET endpoint per request, not a catch-all: each gets its own
    /// authorization policy, rate limit, cache entry, OpenAPI operation and telemetry name,
    /// and a wrong method gives 405 rather than 404.
    /// </remarks>
    /// <param name="endpoints">The application's route builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// An endpoint needs authorization and the application registered none.
    /// </exception>
    public static IEndpointRouteBuilder MapZeroEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<EndpointRegistry>();
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<ZeroWebOptions>>().Value;

        EnsureAuthorizationIsAvailable(endpoints, registry, options);

        foreach (var endpoint in registry.Endpoints)
        {
            var pattern = string.IsNullOrEmpty(options.RoutePrefix)
                ? endpoint.Pattern
                : $"{options.RoutePrefix.TrimEnd('/')}/{endpoint.Pattern.TrimStart('/')}";

            var builder = endpoints
                .MapMethods(pattern, [endpoint.Method], endpoint.Handler)
                .WithName(endpoint.Name);

            if (endpoint.Tag is not null) builder.WithTags(endpoint.Tag);

            Authorize(builder, endpoint, options);
        }

        return endpoints;
    }

    /// <summary>
    /// Refuses to map endpoints that need authorization into an application that has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, attaching the metadata succeeds and ASP.NET refuses at request time with
    /// its own message — every route answering 500, and nothing to say that Zero closed them
    /// or which switch opens them again. Wiring mistakes belong at startup, next to the
    /// wiring.
    /// </para>
    /// <para>
    /// The probe is <see cref="IAuthorizationHandlerProvider"/> because that is the same
    /// service <c>WebApplication</c> looks for when deciding whether to insert the
    /// authorization middleware for itself: on that host, registering the services really
    /// does give you the middleware. On a host that composes its own pipeline the services
    /// are only half the answer, so the message names both halves.
    /// </para>
    /// </remarks>
    private static void EnsureAuthorizationIsAvailable(
        IEndpointRouteBuilder endpoints, EndpointRegistry registry, ZeroWebOptions options)
    {
        var byDefault = options.RequireAuthorizationByDefault
            ? registry.Endpoints.FirstOrDefault(e => !e.AllowAnonymous && e.Policy is null)
            : null;

        var byPolicy = registry.Endpoints.FirstOrDefault(e => !e.AllowAnonymous && e.Policy is not null);

        if (byDefault is null && byPolicy is null) return;
        if (endpoints.ServiceProvider.GetService<IAuthorizationHandlerProvider>() is not null) return;

        // The endpoint that says nothing is the one worth reporting: it is closed by a
        // decision the application never wrote down, so it is the one whose author is
        // surprised.
        throw new InvalidOperationException(byDefault is not null
            ? $"'{byDefault.Method} {byDefault.Pattern}' names neither a policy nor AllowAnonymous, so Zero " +
              "requires an authenticated caller for it — and this application has registered no " +
              "authorization services, which would make that endpoint and every other silent one fail on " +
              "its first request. Call services.AddAuthorization() and app.UseAuthorization(); or, for an " +
              "application with no authentication at all, opt out deliberately with " +
              "services.AddZeroWeb(options => options.RequireAuthorizationByDefault = false)."
            : $"'{byPolicy!.Method} {byPolicy.Pattern}' requires the authorization policy " +
              $"'{byPolicy.Policy}', and this application has registered no authorization services. " +
              "Call services.AddAuthorization() and app.UseAuthorization().");
    }

    /// <summary>
    /// Attaches the endpoint's authorization, or the application's default when it names none.
    /// </summary>
    /// <remarks>
    /// An endpoint that says nothing gets the default rather than nothing at all. Silence is
    /// the one case where the answer must not be "open": in a codebase where most endpoints
    /// carry a policy, the one where someone forgot looks exactly like the rest.
    /// </remarks>
    private static void Authorize(
        IEndpointConventionBuilder builder, ZeroEndpointDescriptor endpoint, ZeroWebOptions options)
    {
        if (endpoint.AllowAnonymous)
        {
            builder.AllowAnonymous();
            return;
        }

        if (endpoint.Policy is not null)
        {
            builder.RequireAuthorization(endpoint.Policy);
            return;
        }

        if (!options.RequireAuthorizationByDefault) return;

        if (options.DefaultPolicy is null) builder.RequireAuthorization();
        else builder.RequireAuthorization(options.DefaultPolicy);
    }
}

/// <summary>Offers the endpoint table to modules, then seals it.</summary>
internal sealed class WebFeatureContributor : IModuleFeatureContributor
{
    private readonly EndpointRegistry _registry = new();

    public void Contribute(IModuleFeatureCollection features)
        => features.Set<IEndpointRegistryBuilder>(_registry);

    public void Complete(IServiceCollection services)
    {
        _registry.Freeze();
        services.AddSingleton(_registry);
    }
}

/// <summary>Reaches the endpoint table from inside a module's configure-services step.</summary>
public static class WebModuleContextExtensions
{
    /// <summary>The endpoint table a module contributes its routes to.</summary>
    /// <param name="context">The module's configure-services context.</param>
    /// <returns>The registry builder.</returns>
    /// <exception cref="InvalidOperationException">
    /// The web layer was not added; call <c>AddZeroWeb()</c> first.
    /// </exception>
    public static IEndpointRegistryBuilder Endpoints(this IModuleServiceContext context)
        => context.Feature<IEndpointRegistryBuilder>();
}
