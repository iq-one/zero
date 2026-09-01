using System.Net;
using System.Net.Http.Json;
using IQOne.Zero.Messaging;
using IQOne.Zero.Modules;
using IQOne.Zero.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IQOne.Zero.Tests;

internal sealed record GetThing(int Id) : IQuery<ThingModel>;

internal sealed record ThingModel(int Id, string Name);

internal sealed class GetThingHandler : IRequestHandler<GetThing, ThingModel>
{
    public Task<Result<ThingModel>> HandleAsync(GetThing query, CancellationToken cancellationToken)
        => Task.FromResult<Result<ThingModel>>(new ThingModel(query.Id, "a thing"));
}

/// <summary>
/// Hand-written where the generator would have written it, so the test exercises the same
/// three registrations a real module makes: the service, the dispatch row, the route.
/// </summary>
internal sealed class ThingsModule : IModule, IModuleConfigureServicesStep
{
    public string Name => "Things";

    public ValueTask OnConfigureServicesAsync(IModuleServiceContext context, CancellationToken cancellationToken)
    {
        context.Services.AddScoped<IRequestHandler<GetThing, ThingModel>, GetThingHandler>();

        context.Requests().Add(new RequestEntry(
            typeof(GetThing), typeof(ThingModel), typeof(GetThingHandler),
            static (services, request, token) =>
                RequestPipeline.RunAsync<GetThing, ThingModel>((GetThing)request, services, token)));

        // Anonymous so the test needs no authorization middleware; the web layer is secure by
        // default and would otherwise require one.
        context.Endpoints().Add(new ZeroEndpointDescriptor(
            "GET", "/things/{id:int}", nameof(GetThing), null, null, true,
            typeof(GetThing), typeof(ThingModel),
            static context => ZeroEndpoint.RunAsync<GetThing, ThingModel>(context)));

        return default;
    }
}

/// <summary>
/// The documented startup sequence, run as written, under a real ASP.NET host.
/// </summary>
/// <remarks>
/// This is the shape the web capability's manifest gives as its canonical example, and the
/// one an agent copies verbatim. It used to register the module phase as an application step
/// that only Zero's own <c>Application.RunAsync</c> executes — which an ASP.NET application
/// never constructs — so no module was ever configured, no feature contributor ever
/// completed, and <c>MapZeroEndpoints</c> failed on a missing <c>EndpointRegistry</c>.
/// </remarks>
public class AspNetHostTests
{
    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        return builder;
    }

    [Fact]
    public async Task The_documented_startup_sequence_serves_a_request()
    {
        var builder = Builder();

        builder.Services.AddZeroWeb();
        builder.Services.AddZeroMessaging();
        builder.Services.AddModules(new ThingsModule());

        await using var app = builder.Build();

        app.MapZeroEndpoints();

        await app.StartAsync();

        using var client = app.GetTestClient();

        var thing = await client.GetFromJsonAsync<ThingModel>("/things/7");

        thing!.Id.Should().Be(7);
    }

    [Fact]
    public async Task The_route_prefix_and_the_sender_are_both_wired_without_a_second_call()
    {
        var builder = Builder();

        builder.Services.AddZeroWeb(options => options.RoutePrefix = "/api");
        builder.Services.AddZeroMessaging();
        builder.Services.AddModules(new ThingsModule());

        await using var app = builder.Build();

        app.MapZeroEndpoints();

        await app.StartAsync();

        using var client = app.GetTestClient();

        (await client.GetAsync("/api/things/7")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The same table the endpoint reads is reachable from code that never saw HTTP.
        using var scope = app.Services.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISender>()
            .SendAsync(new GetThing(3), CancellationToken.None);

        result.Value.Id.Should().Be(3);
    }

    [Fact]
    public void A_module_that_declares_a_request_nobody_handles_stops_startup_at_the_add_call()
    {
        var builder = Builder();

        builder.Services.AddZeroWeb();
        builder.Services.AddZeroMessaging();

        // The check now runs where the mistake is, not on the first request.
        var add = () => builder.Services.AddModules(new UnhandledRequestModule());

        add.Should().Throw<InvalidOperationException>()
            .WithMessage("*have no handler*" + nameof(GetThing) + "*");
    }

    private sealed class UnhandledRequestModule : IModule, IModuleConfigureServicesStep
    {
        public string Name => "Unhandled";

        public ValueTask OnConfigureServicesAsync(
            IModuleServiceContext context, CancellationToken cancellationToken)
        {
            context.Requests().Declare(typeof(GetThing));

            return default;
        }
    }
}
