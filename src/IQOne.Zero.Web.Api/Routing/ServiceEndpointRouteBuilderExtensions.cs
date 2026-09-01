using IQOne.Zero.Messaging.Dispatch;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Web.Api.Routing;

public static class ServiceEndpointRouteBuilderExtensions
{
    /// <summary>Registers every generated service method as its own endpoint.</summary>
    public static IEndpointRouteBuilder MapServiceEndpoints(
        this IEndpointRouteBuilder endpoints, Action<ServiceEndpointOptions>? configure = null)
    {
        var options = new ServiceEndpointOptions();
        configure?.Invoke(options);

        var registry = endpoints.ServiceProvider.GetRequiredService<ServiceRegistry>();

        endpoints.DataSources.Add(new ServiceEndpointDataSource(registry, options));

        return endpoints;
    }
}
