using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Zero.Sample.Invoices;

namespace Zero.Sample.Tests;

/// <summary>
/// Runs the sample application exactly as `dotnet run` would, and drives it over HTTP.
/// </summary>
/// <remarks>
/// <para>
/// This is the only test in the repository that proves the framework works when it is used
/// the way its own documentation says to use it. Everything else builds its registries by
/// hand, which is how the blocking defect survived: `AddModules` registered a step that
/// nothing in an ASP.NET host ever executed, so no capability was ever wired up, and the
/// framework's own tests could not tell because they never called it.
/// </para>
/// <para>
/// If this file passes, `Program.cs` — four Add calls and a Map — is enough.
/// </para>
/// </remarks>
public class SampleApplicationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => factory.CreateClient();

    private static object NewInvoice(string reference) =>
        new { reference, amount = 250.00m, due = "2026-06-30" };

    [Fact]
    public async Task The_application_starts_and_serves_a_generated_endpoint()
    {
        var response = await Client.GetAsync("/api/invoices");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the route came from an attribute on the request, and nothing registered it by hand");
    }

    [Fact]
    public async Task A_command_runs_and_a_query_sees_what_it_did()
    {
        var client = Client;

        var created = await client.PostAsJsonAsync("/api/invoices", NewInvoice("INV-100"), Json);

        created.StatusCode.Should().Be(HttpStatusCode.OK);

        var id = await created.Content.ReadFromJsonAsync<int>(Json);
        var invoice = await client.GetFromJsonAsync<InvoiceModel>($"/api/invoices/{id}", Json);

        invoice!.Reference.Should().Be("INV-100");
        invoice.IsPaid.Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_invoice_is_a_404_although_no_handler_mentions_http()
    {
        var response = await Client.GetAsync("/api/invoices/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("invoice.missing");
    }

    [Fact]
    public async Task Validation_runs_before_the_handler_and_reports_every_reason_at_once()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/invoices", new { reference = "", amount = 0m, due = "2026-06-30" }, Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var codes = problem.GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("code").GetString())
            .ToArray();

        codes.Should().Contain("invoice.reference").And.Contain("invoice.amount",
            "a caller correcting a form should not discover the second mistake on the next attempt");
    }

    [Fact]
    public async Task A_validator_that_asks_the_store_runs_too()
    {
        var client = Client;

        await client.PostAsJsonAsync("/api/invoices", NewInvoice("INV-UNIQUE"), Json);

        var again = await client.PostAsJsonAsync("/api/invoices", NewInvoice("INV-UNIQUE"), Json);

        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await again.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors")[0].GetProperty("code").GetString()
            .Should().Be("invoice.reference.taken");
    }

    [Fact]
    public async Task A_command_with_nothing_to_return_answers_204_and_a_conflict_answers_409()
    {
        var client = Client;

        var created = await client.PostAsJsonAsync("/api/invoices", NewInvoice("INV-PAY"), Json);
        var id = await created.Content.ReadFromJsonAsync<int>(Json);

        var paid = await client.PostAsync($"/api/invoices/{id}/pay", null);
        paid.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await client.PostAsync($"/api/invoices/{id}/pay", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_wrong_verb_answers_405_because_each_request_is_a_real_endpoint()
    {
        var response = await Client.DeleteAsync("/api/invoices/1");

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}
