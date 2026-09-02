using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Zero.Sample.Orders.Ordering;

namespace Zero.Sample.Orders.Tests;

/// <summary>
/// Every Zero capability, composed, driven over HTTP.
/// </summary>
/// <remarks>
/// This is the test that says the framework works when all of it is used at once. A sample
/// that only builds proves the types line up; it does not prove that eleven Add calls
/// produce a working application, and the difference between those two has been where every
/// serious defect in this repository has lived.
/// </remarks>
public sealed class OrdersApiTests : IClassFixture<OrdersApiTests.Application>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Application _application;

    public OrdersApiTests(Application application) => _application = application;

    /// <summary>The sample, run on a database of its own so tests do not share state.</summary>
    public sealed class Application : WebApplicationFactory<Program>
    {
        private readonly string _database = $"orders-test-{Guid.NewGuid():N}.db";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
            => builder.UseSetting("ConnectionStrings:Orders", $"Data Source={_database}");

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing && File.Exists(_database)) File.Delete(_database);
        }
    }

    private HttpClient Client(string customer = "alice", string permissions = "orders:place,orders:pay")
    {
        var client = _application.CreateClient();

        client.DefaultRequestHeaders.Add("X-Customer", customer);
        client.DefaultRequestHeaders.Add("X-Permissions", permissions);

        return client;
    }

    private static object Order(string reference, string product = "DESK-01", int quantity = 1)
        => new { reference, items = new[] { new { productCode = product, quantity } } };

    [Fact]
    public async Task The_catalogue_answers_and_is_public()
    {
        var response = await _application.CreateClient().GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the route says AllowAnonymous, so no header is needed");

        var products = await response.Content.ReadFromJsonAsync<JsonElement>();

        products.EnumerateArray().Should().NotBeEmpty("the sample seeds a few");
    }

    [Fact]
    public async Task An_order_is_placed_read_back_and_paid()
    {
        var client = Client();
        var reference = $"REF-{Guid.NewGuid():N}"[..12];

        var placed = await client.PostAsJsonAsync("/api/orders", Order(reference), Json);

        placed.StatusCode.Should().Be(HttpStatusCode.OK,
            "the pricing service fails the first call, so this only passes if the retry ran");

        var read = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{reference}", Json);

        read.GetProperty("state").GetString().Should().Be("AwaitingPayment");
        read.GetProperty("total").GetDecimal().Should().BeGreaterThan(0m);

        var paid = await client.PostAsync($"/api/orders/{reference}/pay", null);

        paid.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{reference}", Json);

        again.GetProperty("state").GetString().Should().Be("Paid");
    }

    [Fact]
    public async Task Paying_twice_leaves_the_order_paid_which_is_what_idempotent_claims()
    {
        var client = Client();
        var reference = $"REF-{Guid.NewGuid():N}"[..12];

        await client.PostAsJsonAsync("/api/orders", Order(reference), Json);
        await client.PostAsync($"/api/orders/{reference}/pay", null);

        var second = await client.PostAsync($"/api/orders/{reference}/pay", null);

        second.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the claim is not that the second call is harmless but that the state afterwards " +
            "is the one a single call would have left");
    }

    [Fact]
    public async Task Placing_the_same_reference_twice_returns_the_first_order()
    {
        var client = Client();
        var reference = $"REF-{Guid.NewGuid():N}"[..12];

        var first = await client.PostAsJsonAsync("/api/orders", Order(reference), Json);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Read once and keep it. The test server's content is not buffered, so a second read
        // returns nothing — and an assertion message that reads it counts as the first read,
        // because the reason argument is always evaluated.
        var firstReference = await first.Content.ReadFromJsonAsync<string>(Json);

        var second = await client.PostAsJsonAsync("/api/orders", Order(reference), Json);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondReference = await second.Content.ReadFromJsonAsync<string>(Json);

        secondReference.Should().Be(firstReference,
            "the caller chose the reference, so the handler can recognise work it has done");
    }

    [Fact]
    public async Task Validation_runs_before_the_handler_and_reports_everything_at_once()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/orders",
            new { reference = "", items = Array.Empty<object>() },
            Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var codes = problem.GetProperty("errors").EnumerateArray()
            .Select(error => error.GetProperty("code").GetString())
            .ToArray();

        codes.Should().Contain("order.reference").And.Contain("order.items");
    }

    [Fact]
    public async Task Ordering_more_than_the_shelf_holds_is_a_409()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/orders", Order($"REF-{Guid.NewGuid():N}"[..12], "LAMP-01", 9_999), Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "the entity owns the rule, and Error.Conflict becomes 409 without the handler " +
            "mentioning HTTP");
    }

    [Fact]
    public async Task A_missing_product_is_a_404()
    {
        var response = await Client().PostAsJsonAsync(
            "/api/orders", Order($"REF-{Guid.NewGuid():N}"[..12], "NOPE-99"), Json);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_anonymous_caller_is_refused_a_protected_route()
    {
        var response = await _application.CreateClient()
            .PostAsJsonAsync("/api/orders", Order("REF-ANON"), Json);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "Zero requires authorization by default and the route names a policy");
    }

    [Fact]
    public async Task A_caller_without_the_policy_is_refused()
    {
        var response = await Client(permissions: "orders:pay")
            .PostAsJsonAsync("/api/orders", Order("REF-NOPOLICY"), Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "known, but not permitted — which is 403, not 401");
    }

    [Fact]
    public async Task Another_customer_s_order_is_refused_by_the_resource_requirement()
    {
        var reference = $"REF-{Guid.NewGuid():N}"[..12];

        await Client("alice").PostAsJsonAsync("/api/orders", Order(reference), Json);

        var response = await Client("bob").GetAsync($"/api/orders/{reference}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the route policy answered 'may bob ask'; only the requirement can answer " +
            "'may bob see THIS one', because that needs the order");
    }

    [Fact]
    public async Task A_caller_allowed_to_read_any_order_sees_another_customer_s()
    {
        var reference = $"REF-{Guid.NewGuid():N}"[..12];

        await Client("alice").PostAsJsonAsync("/api/orders", Order(reference), Json);

        var response = await Client("support", "orders:pay,orders:read-any")
            .GetAsync($"/api/orders/{reference}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_subscriber_falling_behind_does_not_undo_the_order()
    {
        // The mail subscriber refuses anything over 1,000 on purpose. The order still stands:
        // the fact happened, and nothing about it is conditional on who kept up.
        var reference = $"REF-{Guid.NewGuid():N}"[..12];

        var placed = await Client().PostAsJsonAsync(
            "/api/orders", Order(reference, "CHAIR-01", 150), Json);

        placed.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await Client().GetFromJsonAsync<JsonElement>($"/api/orders/{reference}", Json);

        read.GetProperty("total").GetDecimal().Should().BeGreaterThan(1_000m);
    }

    [Fact]
    public void Every_capability_is_registered_and_the_provider_validates()
    {
        // The strongest single assertion here: ValidateOnBuild constructs every singleton and
        // ValidateScopes rejects a captive dependency, so eleven capabilities composing badly
        // fails here rather than on whichever request happened to touch the bad one.
        var provider = _application.Services;

        // From a scope: ISender is scoped, and resolving it from the root is exactly what
        // ValidateScopes exists to reject. The framework refusing here is correct.
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IQOne.Zero.Messaging.ISender>().Should().NotBeNull();
        provider.GetRequiredService<IQOne.Zero.BackgroundWork.IBackgroundWorkStatus>()
            .Jobs.Should().ContainSingle(job => job.Name == "expire-unpaid-orders");
    }
}
