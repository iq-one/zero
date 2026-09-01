using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IQOne.Zero.Web.Tests;

/// <summary>
/// W10: an endpoint that says nothing about authorization is not an open endpoint.
/// </summary>
/// <remarks>
/// In a codebase where most endpoints carry a policy, the one where somebody forgot looks
/// exactly like the rest of them. Defaulting to closed turns that omission into a 401 the
/// first time anyone calls it, instead of a public endpoint nobody notices.
/// </remarks>
public class AuthorizationTests
{
    private static HttpClient Client(IHost host, bool authenticated)
    {
        var client = host.GetTestClient();

        if (authenticated) client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");

        return client;
    }

    [Fact]
    public async Task An_endpoint_that_names_no_policy_still_requires_a_caller()
    {
        using var host = Fixture.Build();
        using var client = Client(host, authenticated: false);

        (await client.GetAsync("/things/7")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_endpoint_that_names_no_policy_serves_an_authenticated_caller()
    {
        using var host = Fixture.Build();
        using var client = Client(host, authenticated: true);

        (await client.GetAsync("/things/7")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_endpoint_marked_anonymous_stays_open()
    {
        using var host = Fixture.Build();
        using var client = Client(host, authenticated: false);

        (await client.GetAsync("/open/things/7")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The escape hatch for an application that has no authentication at all.</summary>
    [Fact]
    public async Task Turning_the_default_off_leaves_a_silent_endpoint_open()
    {
        using var host = Fixture.Build(options => options.RequireAuthorizationByDefault = false);
        using var client = Client(host, authenticated: false);

        (await client.GetAsync("/things/7")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_default_policy_applies_where_no_policy_was_named()
    {
        using var host = Fixture.Build(options => options.DefaultPolicy = "things:admin");
        using var client = Client(host, authenticated: true);

        // Authenticated, but the policy wants a claim this caller does not carry.
        (await client.GetAsync("/things/7")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_default_policy_does_not_reach_an_endpoint_marked_anonymous()
    {
        using var host = Fixture.Build(options => options.DefaultPolicy = "things:admin");
        using var client = Client(host, authenticated: false);

        (await client.GetAsync("/open/things/7")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// An application that closed its endpoints without meaning to is told at startup.
    /// </summary>
    /// <remarks>
    /// Attaching the metadata always succeeds; ASP.NET then refuses every request with its
    /// own message, which names a middleware but not the default that pulled it in. Mapping
    /// is where the wiring is, so mapping is where it is reported.
    /// </remarks>
    [Fact]
    public void Mapping_into_an_application_with_no_authorization_says_what_to_do()
    {
        using var app = Bare(configure: null);

        var map = () => app.MapZeroEndpoints();

        map.Should().Throw<InvalidOperationException>()
            .WithMessage("*GET /guarded*")
            .WithMessage("*AddAuthorization()*")
            .WithMessage("*UseAuthorization()*")
            .WithMessage("*RequireAuthorizationByDefault = false*");
    }

    [Fact]
    public void Mapping_says_nothing_when_the_application_opted_out()
    {
        using var app = Bare(options => options.RequireAuthorizationByDefault = false);

        var map = () => app.MapZeroEndpoints();

        map.Should().NotThrow();
    }

    [Fact]
    public void Mapping_says_nothing_when_authorization_is_registered()
    {
        using var app = Bare(configure: null, services => services.AddAuthorization());

        var map = () => app.MapZeroEndpoints();

        map.Should().NotThrow();
    }

    /// <summary>
    /// The whole point of the check: registering the services is enough on this host.
    /// </summary>
    /// <remarks>
    /// <c>WebApplication</c> inserts the authorization middleware itself once the services
    /// are registered, so an application that does what the map-time message asks reaches
    /// authorization rather than the missing-middleware exception. A 401 here is the
    /// evidence — a 500 would mean the middleware never ran.
    /// </remarks>
    [Fact]
    public async Task An_application_that_only_registers_authorization_still_reaches_it()
    {
        await using var app = Bare(configure: null, services =>
        {
            services
                .AddAuthentication(TestAuthenticationHandler.Name)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.Name, _ => { });

            services.AddAuthorization();
        });

        app.MapZeroEndpoints();

        await app.StartAsync();

        using var client = app.GetTestClient();

        (await client.GetAsync("/guarded")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>An application with one endpoint and only what Zero asked it to add.</summary>
    private static WebApplication Bare(
        Action<ZeroWebOptions>? configure, Action<IServiceCollection>? register = null)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddZeroWeb(configure ?? (_ => { }));

        register?.Invoke(builder.Services);

        var registry = new EndpointRegistry();

        registry.Add(new ZeroEndpointDescriptor(
            "GET", "/guarded", "Guarded", null, null, false, typeof(GetThing), typeof(ThingModel),
            static context => ZeroEndpoint.RunAsync<GetThing, ThingModel>(context)));

        builder.Services.AddSingleton(registry.Freeze());

        return builder.Build();
    }
}
