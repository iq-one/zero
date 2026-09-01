using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IQOne.Zero.Messaging;
using IQOne.Zero.Web.Writing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Web.Tests;

internal enum ThingKind
{
    Draft,
    Final
}

internal sealed record GetThing(int Id, string? Note) : IQuery<ThingModel>;

internal sealed record CreateThing(string Name, int Quantity) : ICommand<int>;

internal sealed record DeleteThing(int Id) : ICommand;

internal sealed record FindThings(int Id, bool IncludePaid, ThingKind Kind, string[]? Tags, string? Query)
    : IQuery<FindModel>;

internal sealed record MeasureThing(double Ratio) : IQuery<MeasureModel>;

internal sealed record ThingModel(int Id, string Name, string? Note);

internal sealed record FindModel(int Id, bool IncludePaid, string Kind, string[] Tags, string? Query);

internal sealed record MeasureModel(double Ratio);

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

internal sealed class FindThingsHandler : IQueryHandler<FindThings, FindModel>
{
    public Task<Result<FindModel>> HandleAsync(FindThings query, CancellationToken cancellationToken)
        => Task.FromResult<Result<FindModel>>(new FindModel(
            query.Id, query.IncludePaid, query.Kind.ToString(), query.Tags ?? [], query.Query));
}

internal sealed class MeasureThingHandler : IQueryHandler<MeasureThing, MeasureModel>
{
    public Task<Result<MeasureModel>> HandleAsync(MeasureThing query, CancellationToken cancellationToken)
        => Task.FromResult<Result<MeasureModel>>(new MeasureModel(query.Ratio));
}

/// <summary>
/// Authenticates whoever names themselves in a header, and nobody else.
/// </summary>
/// <remarks>
/// The endpoints under test require an authenticated caller by default, so the suite needs a
/// scheme to be authenticated by. Leaving the header off is how a test asks what an
/// anonymous caller sees.
/// </remarks>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Name = "Test";

    public const string UserHeader = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user.ToString())], Name);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
    }
}

/// <summary>
/// Builds a real host over the endpoint table, filled the way generated code fills it, so
/// binding and the result-to-status mapping are exercised through actual HTTP.
/// </summary>
internal static class Fixture
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IHost Build(
        Action<ZeroWebOptions>? configure = null, Action<IServiceCollection>? register = null)
    {
        var builder = Host.CreateDefaultBuilder();

        builder.ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services =>
            {
                register?.Invoke(services);

                services.AddRouting();
                services.AddZeroWeb(configure ?? (_ => { }));

                services
                    .AddAuthentication(TestAuthenticationHandler.Name)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.Name, _ => { });

                services.AddAuthorization(options => options.AddPolicy(
                    "things:admin", policy => policy.RequireClaim("scope", "admin")));

                services.AddScoped<IRequestHandler<GetThing, ThingModel>, GetThingHandler>();
                services.AddScoped<IRequestHandler<CreateThing, int>, CreateThingHandler>();
                services.AddScoped<IRequestHandler<DeleteThing, Unit>, DeleteThingHandler>();
                services.AddScoped<IRequestHandler<FindThings, FindModel>, FindThingsHandler>();
                services.AddScoped<IRequestHandler<MeasureThing, MeasureModel>, MeasureThingHandler>();

                services.AddZeroMessagingWithRequests(requests =>
                {
                    requests.Add(new RequestEntry(typeof(GetThing), typeof(ThingModel), typeof(GetThingHandler),
                        static (sp, r, ct) => RequestPipeline.RunAsync<GetThing, ThingModel>((GetThing)r, sp, ct)));

                    requests.Add(new RequestEntry(typeof(CreateThing), typeof(int), typeof(CreateThingHandler),
                        static (sp, r, ct) => RequestPipeline.RunAsync<CreateThing, int>((CreateThing)r, sp, ct)));

                    requests.Add(new RequestEntry(typeof(DeleteThing), typeof(Unit), typeof(DeleteThingHandler),
                        static (sp, r, ct) => RequestPipeline.RunAsync<DeleteThing, Unit>((DeleteThing)r, sp, ct)));

                    requests.Add(new RequestEntry(typeof(FindThings), typeof(FindModel), typeof(FindThingsHandler),
                        static (sp, r, ct) => RequestPipeline.RunAsync<FindThings, FindModel>((FindThings)r, sp, ct)));

                    requests.Add(new RequestEntry(typeof(MeasureThing), typeof(MeasureModel), typeof(MeasureThingHandler),
                        static (sp, r, ct) =>
                            RequestPipeline.RunAsync<MeasureThing, MeasureModel>((MeasureThing)r, sp, ct)));
                });

                services.AddSingleton(Endpoints().Freeze());
            })
            .Configure(app => app
                .UseRouting()
                // Route values are usually strings, but they need not be. This is where a
                // real application's middleware or matcher puts a typed one, and where the
                // culture the server happens to run under starts to matter.
                .Use(async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments("/measure"))
                        context.Request.RouteValues["ratio"] = 1.5d;

                    await next();
                })
                .UseAuthentication()
                .UseAuthorization()
                .UseEndpoints(e => e.MapZeroEndpoints())));

        var host = builder.Build();
        host.Start();

        return host;
    }

    private static EndpointRegistry Endpoints()
    {
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

        endpoints.Add(new ZeroEndpointDescriptor(
            "GET", "/find", nameof(FindThings), null, null, false,
            typeof(FindThings), typeof(FindModel),
            static context => ZeroEndpoint.RunAsync<FindThings, FindModel>(context)));

        endpoints.Add(new ZeroEndpointDescriptor(
            "GET", "/measure", nameof(MeasureThing), null, null, false,
            typeof(MeasureThing), typeof(MeasureModel),
            static context => ZeroEndpoint.RunAsync<MeasureThing, MeasureModel>(context)));

        endpoints.Add(new ZeroEndpointDescriptor(
            "GET", "/open/things/{id:int}", "GetThingOpenly", null, null, true,
            typeof(GetThing), typeof(ThingModel),
            static context => ZeroEndpoint.RunAsync<GetThing, ThingModel>(context)));

        // Not a Zero endpoint: a handler that hands the writer a failure with no reasons in
        // it. Nothing in the pipeline can produce that any more, and the writer still must
        // not answer a stack trace if something does.
        endpoints.Add(new ZeroEndpointDescriptor(
            "GET", "/things/no-reason", "NoReason", null, null, true,
            typeof(GetThing), typeof(ThingModel),
            static context => Task.FromResult(context.RequestServices
                .GetRequiredService<IResponseWriter>()
                .Failure(context, [], null))));

        return endpoints;
    }
}
