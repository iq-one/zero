using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.Web.Tests;

/// <summary>
/// What a route or query value has to survive on its way into a request.
/// </summary>
/// <remarks>
/// Every value in a URL is text. The binder carries it as a JSON string and lets the
/// serializer decide what it means, so these tests are about the types that used to be lost
/// in that trip.
/// </remarks>
public class BindingTests : IDisposable
{
    private readonly IHost _host = Fixture.Build();
    private readonly HttpClient _client;

    public BindingTests()
    {
        _client = _host.GetTestClient();
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>W2: a query string has no way to carry a JSON boolean.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public async Task A_boolean_query_value_binds(string text, bool expected)
    {
        var found = await _client.GetFromJsonAsync<FindModel>($"/find?includePaid={text}", Fixture.Json);

        found!.IncludePaid.Should().Be(expected);
    }

    /// <summary>W2: nor an enum.</summary>
    [Fact]
    public async Task An_enum_query_value_binds_by_name()
    {
        var found = await _client.GetFromJsonAsync<FindModel>("/find?kind=Final", Fixture.Json);

        found!.Kind.Should().Be("Final");
    }

    [Fact]
    public async Task A_boolean_that_is_not_one_is_the_caller_s_mistake()
        => (await _client.GetAsync("/find?includePaid=perhaps")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

    /// <summary>W3: the conventional answer, and the one that keeps a scalar working.</summary>
    [Fact]
    public async Task A_repeated_query_key_binds_the_last_value_to_a_scalar()
    {
        var found = await _client.GetFromJsonAsync<FindModel>("/find?id=1&id=2", Fixture.Json);

        found!.Id.Should().Be(2);
    }

    /// <summary>W3: and every value to a collection, however many there are.</summary>
    [Fact]
    public async Task A_repeated_query_key_binds_every_value_to_a_collection()
    {
        var found = await _client.GetFromJsonAsync<FindModel>("/find?tags=red&tags=blue", Fixture.Json);

        found!.Tags.Should().Equal("red", "blue");
    }

    [Fact]
    public async Task A_single_query_value_still_binds_to_a_collection()
    {
        var found = await _client.GetFromJsonAsync<FindModel>("/find?tags=red", Fixture.Json);

        found!.Tags.Should().Equal("red");
    }

    /// <summary>
    /// W2 again, from the other side: the type decides, not the text.
    /// </summary>
    /// <remarks>
    /// The reason booleans and enums are handled by a converter rather than by sniffing the
    /// value and emitting a real JSON literal. Sniffing would read this as a number and fail
    /// to give a string property its value.
    /// </remarks>
    [Fact]
    public async Task A_string_query_value_that_looks_like_a_number_stays_a_string()
    {
        var found = await _client.GetFromJsonAsync<FindModel>("/find?query=123", Fixture.Json);

        found!.Query.Should().Be("123");
    }

    /// <summary>W4: the same request must not depend on the server's locale.</summary>
    [Fact]
    public async Task A_typed_route_value_is_read_under_the_invariant_culture()
    {
        var culture = CultureInfo.DefaultThreadCurrentCulture;

        try
        {
            // Under tr-TR, formatting 1.5 with the ambient culture produces "1,5", which no
            // JSON reader accepts as a number.
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("tr-TR");

            using var host = Fixture.Build();
            using var client = host.GetTestClient();

            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");

            var measure = await client.GetFromJsonAsync<MeasureModel>("/measure", Fixture.Json);

            measure!.Ratio.Should().Be(1.5d);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentCulture = culture;
        }
    }

    /// <summary>
    /// W5: the overlay must replace the body's property, not add a second one beside it.
    /// </summary>
    /// <remarks>
    /// Turning duplicates off is what an application does to harden its parsing, and it is
    /// what turns a tolerated duplicate into a 400 on every request that carries the same
    /// value in the URL and the body.
    /// </remarks>
    [Fact]
    public async Task A_body_property_differing_only_in_case_is_overlaid_not_duplicated()
    {
        using var host = Fixture.Build(register: services => services.ConfigureHttpJsonOptions(
            options => options.SerializerOptions.AllowDuplicateProperties = false));

        using var client = host.GetTestClient();

        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");

        var request = new HttpRequestMessage(HttpMethod.Get, "/things/7")
        {
            Content = new StringContent("""{"Id":99}""", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ThingModel>(Fixture.Json))!.Id.Should().Be(7);
    }

    /// <summary>
    /// Reading the body case-insensitively must not turn a caller's duplicate into a 500.
    /// </summary>
    /// <remarks>
    /// The other side of the same coin as the overlay: once names are matched without
    /// regard to case, a body that carries both spellings is a collision, and a collision
    /// the parser reports has to reach the caller as their mistake.
    /// </remarks>
    [Fact]
    public async Task A_body_that_names_the_same_property_twice_is_the_caller_s_mistake()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/things/7")
        {
            Content = new StringContent("""{"id":1,"Id":2}""", Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);

        ((int)response.StatusCode).Should().BeLessThan(500);
    }

    /// <summary>
    /// W11: what the application configures must not be able to break route binding.
    /// </summary>
    /// <remarks>
    /// Route and query values arrive as strings, so reading a number out of one is a
    /// structural requirement of the design rather than a serializer setting. An application
    /// that turns it off — to tighten its own body parsing, say — used to get a 400 from
    /// every endpoint with a route parameter.
    /// </remarks>
    [Fact]
    public async Task Binding_survives_an_application_that_configures_json_for_itself()
    {
        using var host = Fixture.Build(register: services => services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
            options.SerializerOptions.PropertyNameCaseInsensitive = false;
        }));

        using var client = host.GetTestClient();

        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");

        var response = await client.GetAsync("/things/7?note=hello");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var thing = await response.Content.ReadFromJsonAsync<JsonElement>();

        thing.GetProperty("id").GetInt32().Should().Be(7);
        thing.GetProperty("note").GetString().Should().Be("hello");
    }
}
