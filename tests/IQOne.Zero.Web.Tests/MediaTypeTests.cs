using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.Web.Tests;

/// <summary>
/// W1: what a body has to declare before the binder will read it.
/// </summary>
/// <remarks>
/// This is the CSRF boundary. A cross-origin HTML form can post text/plain,
/// multipart/form-data and application/x-www-form-urlencoded with the victim's cookies and
/// without a preflight, and a form can be shaped so that its text/plain body is valid JSON.
/// It cannot post application/json without CORS approval. Refusing everything else with 415
/// is what keeps another site from driving a state-changing endpoint.
/// </remarks>
public class MediaTypeTests : IDisposable
{
    private readonly IHost _host = Fixture.Build();
    private readonly HttpClient _client;

    public MediaTypeTests()
    {
        _client = _host.GetTestClient();

        // The victim of a CSRF is an authenticated caller; that is the whole point of it.
        _client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");
    }

    public void Dispose()
    {
        _client.Dispose();
        _host.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The attack, exactly as a form would send it.
    /// </summary>
    /// <remarks>
    /// <c>&lt;form enctype="text/plain"&gt;</c> writes <c>name=value</c> pairs, so an input
    /// named <c>{"name":"pwned","quantity":1,"x":"</c> with the value <c>"}</c> puts valid
    /// JSON on the wire. Nothing about the body gives it away; only the media type does.
    /// </remarks>
    [Fact]
    public async Task A_text_plain_body_that_happens_to_be_json_is_refused_with_415()
    {
        var forged = new StringContent(
            """{"name":"pwned","quantity":1,"x":"="}""", Encoding.UTF8, "text/plain");

        var response = await _client.PostAsync("/things", forged);

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task A_form_post_is_refused_with_415()
    {
        using var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("name", "pwned"),
            new KeyValuePair<string, string>("quantity", "1")
        ]);

        (await _client.PostAsync("/things", form)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task A_body_with_no_media_type_at_all_is_refused_with_415()
    {
        var body = new ByteArrayContent(Encoding.UTF8.GetBytes("""{"name":"widget","quantity":5}"""));
        body.Headers.ContentType = null;

        (await _client.PostAsync("/things", body)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task A_refusal_says_what_was_wrong_without_running_the_command()
    {
        var response = await _client.PostAsync(
            "/things", new StringContent("""{"name":"pwned","quantity":1}""", Encoding.UTF8, "text/plain"));

        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        problem.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("request.media-type");
    }

    [Fact]
    public async Task A_charset_does_not_change_the_media_type()
    {
        var body = new StringContent("""{"name":"widget","quantity":5}""", Encoding.UTF8, "application/json");

        (await _client.PostAsync("/things", body)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_json_suffix_is_still_json()
    {
        var body = new StringContent("""{"name":"widget","quantity":5}""");
        body.Headers.ContentType = new MediaTypeHeaderValue("application/merge-patch+json");

        (await _client.PostAsync("/things", body)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_call_with_no_body_needs_no_media_type()
        => (await _client.DeleteAsync("/things/3")).StatusCode.Should().Be(HttpStatusCode.NoContent);
}
