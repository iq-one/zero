using System.Text.Json;
using IQOne.Zero.Messaging;
using IQOne.Zero.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.Web.Tests;

internal sealed record GetThing(int Id, string? Note) : IQuery<ThingModel>;

internal sealed record CreateThing(string Name, int Quantity) : ICommand<int>;

internal sealed record DeleteThing(int Id) : ICommand;

internal sealed record ThingModel(int Id, string Name, string? Note);

internal sealed class GetThingHandler : IQueryHandler<GetThing, ThingModel>
{
    public Task<Result<ThingModel>> HandleAsync(GetThing query, CancellationToken cancellationToken)
        => Task.FromResult<Result<ThingModel>>(query.Id switch
        {
            404 => Error.NotFound("thing.missing", $"No thing with id {query.Id}."),
            409 => Error.Conflict("thing.locked", "That thing is locked."),
            _ => new ThingModel(query.Id, "a thing", query.Note)
        });
}

internal sealed class CreateThingHandler : ICommandHandler<CreateThing, int>
{
    public Task<Result<int>> HandleAsync(CreateThing command, CancellationToken cancellationToken)
        => Task.FromResult<Result<int>>(string.IsNullOrWhiteSpace(command.Name)
            ? Error.Validation("thing.name", "A name is required.")
            : command.Quantity);
}

internal sealed class DeleteThingHandler : ICommandHandler<DeleteThing>
{
    public Task<Result<Unit>> HandleAsync(DeleteThing command, CancellationToken cancellationToken)
        => Task.FromResult(Unit.Success);
}

/// <summary>
/// Builds a real host over the endpoint table, filled the way generated code fills it, so
/// binding and the result-to-status mapping are exercised through actual HTTP.
/// </summary>
internal static class Fixture
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IHost Build(Action<ZeroWebOptions>? configure = null)
    {
        var builder = Host.CreateDefaultBuilder();

        builder.ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddZeroWeb(configure ?? (_ => { }));

                services.AddScoped<IRequestHandler<GetThing, ThingModel>, GetThingHandler>();
                services.AddScoped<IRequestHandler<CreateThing, int>, CreateThingHandler>();
                services.AddScoped<IRequestHandler<DeleteThing, Unit>, DeleteThingHandler>();

                services.AddZeroMessaging(requests =>
                {
                    requests.Add(new RequestEntry(typeof(GetThing), typeof(ThingModel), typeof(GetThingHandler),
                        static (sp, r, ct) => RequestPipeline.RunAsync<GetThing, ThingModel>((GetThing)r, sp, ct)));

                    requests.Add(new RequestEntry(typeof(CreateThing), typeof(int), typeof(CreateThingHandler),
                        static (sp, r, ct) => RequestPipeline.RunAsync<CreateThing, int>((CreateThing)r, sp, ct)));

                    requests.Add(new RequestEntry(typeof(DeleteThing), typeof(Unit), typeof(DeleteThingHandler),
                        static (sp, r, ct) => RequestPipeline.RunAsync<DeleteThing, Unit>((DeleteThing)r, sp, ct)));
                });

                var endpoints = new EndpointRegistry();

                endpoints.Add(new ZeroEndpointDescriptor(
                    "GET", "/things/{id:int}", nameof(GetThing), null, null, false,
                    typeof(GetThing), typeof(ThingModel),
                    static context => ZeroEndpoint.RunAsync<GetThing, ThingModel>(context)));

                endpoints.Add(new ZeroEndpointDescriptor(
                    "POST", "/things", nameof(CreateThing), null, null, false,
                    typeof(CreateThing), typeof(int),
                    static context => ZeroEndpoint.RunAsync<CreateThing, int>(context)));

                endpoints.Add(new ZeroEndpointDescriptor(
                    "DELETE", "/things/{id:int}", nameof(DeleteThing), null, null, false,
                    typeof(DeleteThing), typeof(Unit),
                    static context => ZeroEndpoint.RunAsync<DeleteThing, Unit>(context)));

                services.AddSingleton(endpoints.Freeze());
            })
            .Configure(app => app.UseRouting().UseEndpoints(e => e.MapZeroEndpoints())));

        var host = builder.Build();
        host.Start();

        return host;
    }
}
