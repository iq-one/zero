using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.Web.Tests;

/// <summary>
/// Driven over real HTTP through the test server, because binding and the result-to-status
/// mapping are only true if they are true at the wire.
/// </summary>
public class EndpointTests : IDisposable
{
    private readonly IHost _host = Fixture.Build();
    private readonly HttpClient _client;

    public EndpointTests()
    {
        _client = _host.GetTestClient();

        // Endpoints require an authenticated caller unless they say otherwise, so every test
        // that is not about authorization arrives as somebody.
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task A_route_value_reaches_the_request()
    {
        var thing = await _client.GetFromJsonAsync<ThingModel>("/things/7", Fixture.Json);

        thing!.Id.Should().Be(7);
    }

    [Fact]
    public async Task A_query_value_reaches_the_request_alongside_the_route()
    {
        var thing = await _client.GetFromJsonAsync<ThingModel>("/things/7?note=hello", Fixture.Json);

        thing!.Id.Should().Be(7);
        thing.Note.Should().Be("hello");
    }

    [Fact]
    public async Task A_body_fills_the_request()
    {
        var response = await _client.PostAsJsonAsync("/things", new { name = "widget", quantity = 5 }, Fixture.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(Fixture.Json)).Should().Be(5);
    }

    [Fact]
    public async Task A_route_value_wins_over_the_same_name_in_the_body()
    {
        // The URL is what the caller asked for; the body must not quietly redirect it.
        var request = new HttpRequestMessage(HttpMethod.Get, "/things/7")
        {
            Content = new StringContent("""{"id":99}""", Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);
        var thing = await response.Content.ReadFromJsonAsync<ThingModel>(Fixture.Json);

        thing!.Id.Should().Be(7);
    }

    [Fact]
    public async Task A_not_found_error_becomes_404_without_the_handler_naming_a_status()
    {
        var response = await _client.GetAsync("/things/404");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("thing.missing");
    }

    [Fact]
    public async Task A_conflict_error_becomes_409()
        => (await _client.GetAsync("/things/409")).StatusCode.Should().Be(HttpStatusCode.Conflict);

    [Fact]
    public async Task A_validation_error_becomes_400()
    {
        var response = await _client.PostAsJsonAsync("/things", new { name = "", quantity = 1 }, Fixture.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_command_that_produces_nothing_answers_204()
        => (await _client.DeleteAsync("/things/3")).StatusCode.Should().Be(HttpStatusCode.NoContent);

    [Fact]
    public async Task A_wrong_verb_answers_405_because_each_request_is_a_real_endpoint()
        => (await _client.PutAsync("/things/3", null)).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

    [Fact]
    public async Task An_unreadable_body_is_the_caller_s_mistake_not_a_500()
    {
        var response = await _client.PostAsync(
            "/things", new StringContent("not json", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_failure_carries_the_trace_id_so_a_report_can_be_matched_to_a_log()
    {
        var response = await _client.GetAsync("/things/404");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.TryGetProperty("traceId", out var traceId).Should().BeTrue();
        traceId.GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>W8: a transport must not crash on an input it can be handed.</summary>
    [Fact]
    public async Task A_failure_with_no_reasons_is_reported_rather_than_thrown()
    {
        var response = await _client.GetAsync("/things/no-reason");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors").GetArrayLength().Should().Be(0);
    }
}
