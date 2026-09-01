using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.Web.Tests;

/// <summary>
/// W6: the binder holds the body to overlay values onto it, so it needs a limit of its own.
/// </summary>
/// <remarks>
/// Kestrel's 30 MB allowance is a server-wide ceiling for uploads, not a per-request memory
/// budget for a JSON command. Twenty concurrent bodies at that size is an out-of-memory, and
/// one is enough on an endpoint where the application raised the ceiling deliberately.
/// </remarks>
public class BodyLimitTests
{
    private static async Task<HttpResponseMessage> Post(HttpContent body, long limit)
    {
        using var host = Fixture.Build(options => options.MaxBodyBytes = limit);
        using var client = host.GetTestClient();

        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserHeader, "tester");

        return await client.PostAsync("/things", body);
    }

    private static string Oversized()
        => $$"""{"name":"{{new string('x', 4096)}}","quantity":5}""";

    [Fact]
    public async Task A_body_over_the_limit_is_refused_with_413()
    {
        var response = await Post(new StringContent(Oversized(), Encoding.UTF8, "application/json"), 512);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("request.too-large");
    }

    /// <summary>
    /// The limit has to hold when the caller declares no length at all.
    /// </summary>
    /// <remarks>
    /// Chunked transfer encoding is how a body arrives with nothing to check up front, and
    /// it is the case a limit read from Content-Length would miss entirely.
    /// </remarks>
    [Fact]
    public async Task A_chunked_body_over_the_limit_is_refused_while_it_is_being_read()
    {
        using var content = new StreamContent(new Unmeasured(Encoding.UTF8.GetBytes(Oversized())));

        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await Post(content, 512);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_body_within_the_limit_is_read_as_usual()
    {
        var body = new StringContent("""{"name":"widget","quantity":5}""", Encoding.UTF8, "application/json");

        (await Post(body, 512)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_limit_of_zero_leaves_only_the_server_s()
    {
        var body = new StringContent(Oversized(), Encoding.UTF8, "application/json");

        (await Post(body, 0)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A stream that will not say how long it is, so the request goes out chunked.</summary>
    private sealed class Unmeasured(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();

            base.Dispose(disposing);
        }
    }
}
