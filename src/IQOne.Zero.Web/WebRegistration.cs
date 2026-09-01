using IQOne.Zero.Modules;
using IQOne.Zero.Web.Binding;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Web;

/// <summary>Adds HTTP endpoints for commands and queries.</summary>
public static class WebRegistration
{
    /// <summary>
    /// Lets modules contribute their endpoints, and registers what reads requests and writes
    /// responses.
    /// </summary>
    /// <remarks>Call this before <c>AddModules</c>, then <c>MapZeroEndpoints</c> on the app.</remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts routing, serialization and status codes.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroWeb(
        this IServiceCollection services, Action<ZeroWebOptions>? configure = null)
    {
        if (configure is not null) services.Configure(configure);

        services.AddOptions<ZeroWebOptions>();
        services.TryAddSingleton<IRequestBinder, JsonRequestBinder>();
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
    public static IEndpointRouteBuilder MapZeroEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<EndpointRegistry>();
        var options = endpoints.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<ZeroWebOptions>>().Value;

        foreach (var endpoint in registry.Endpoints)
        {
            var pattern = string.IsNullOrEmpty(options.RoutePrefix)
                ? endpoint.Pattern
                : $"{options.RoutePrefix.TrimEnd('/')}/{endpoint.Pattern.TrimStart('/')}";

            var builder = endpoints
                .MapMethods(pattern, [endpoint.Method], endpoint.Handler)
                .WithName(endpoint.Name);

            if (endpoint.Tag is not null) builder.WithTags(endpoint.Tag);

            if (endpoint.AllowAnonymous) builder.AllowAnonymous();
            else if (endpoint.Policy is not null) builder.RequireAuthorization(endpoint.Policy);
        }

        return endpoints;
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
public static class ModuleServiceContextExtensions
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
