using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IQOne.Zero.Web.Writing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace IQOne.Zero.Web.Tests;

/// <summary>
/// W7: the envelope belongs to whoever has to keep it stable for their callers.
/// </summary>
/// <remarks>
/// A team with a published API cannot adopt a package that decides the shape of its
/// responses. There is a seam for input; this is the one for output, and these tests are
/// what say it is really a seam rather than a hook that the framework works around.
/// </remarks>
public class ResponseWriterTests
{
    /// <summary>An application's own contract: no problem details, no traceId, its own names.</summary>
    private sealed class HouseStyle : IResponseWriter
    {
        public IResult Success<TResponse>(HttpContext context, TResponse value)
            => HttpResults.Json(new { ok = true, data = value });

        public IResult Empty(HttpContext context) => HttpResults.StatusCode(StatusCodes.Status200OK);

        public IResult Failure(HttpContext context, IReadOnlyList<Error> errors, int? status)
            => HttpResults.Json(
                new { ok = false, reasons = errors.Select(e => e.Code).ToArray() },
                statusCode: status ?? StatusCodes.Status422UnprocessableEntity);
    }

    private static IHost Host() => Fixture.Build(
        register: services => services.AddSingleton<IResponseWriter, HouseStyle>());

    private static HttpClient Client(IHost host)
    {
        var client = host.GetTestClient();

        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");

        return client;
    }

    [Fact]
    public async Task A_registered_writer_shapes_a_successful_response()
    {
        using var host = Host();
        using var client = Client(host);

        var body = await client.GetFromJsonAsync<JsonElement>("/things/7", Fixture.Json);

        body.GetProperty("ok").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("id").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task A_registered_writer_shapes_a_failure_and_chooses_its_status()
    {
        using var host = Host();
        using var client = Client(host);

        var response = await client.GetAsync("/things/404");

        // The default maps NotFound to 404; this application says every failure is a 422.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Fixture.Json);

        body.GetProperty("reasons")[0].GetString().Should().Be("thing.missing");
        body.TryGetProperty("traceId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_registered_writer_decides_what_nothing_looks_like()
    {
        using var host = Host();
        using var client = Client(host);

        (await client.DeleteAsync("/things/3")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The transport still says which status HTTP itself dictates.
    /// </summary>
    /// <remarks>
    /// A 415 is not the application's decision to make: the caller sent a media type the
    /// binder does not read, and that answer is fixed. What the body looks like is still the
    /// writer's.
    /// </remarks>
    [Fact]
    public async Task A_media_type_refusal_keeps_its_status_through_a_custom_writer()
    {
        using var host = Host();
        using var client = Client(host);

        var response = await client.PostAsync(
            "/things",
            new StringContent("""{"name":"pwned","quantity":1}""", System.Text.Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Fixture.Json);

        body.GetProperty("reasons")[0].GetString().Should().Be("request.media-type");
    }
}
