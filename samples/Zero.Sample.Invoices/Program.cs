using IQOne.Zero.Messaging;
using IQOne.Zero.Modules;
using IQOne.Zero.Validation;
using IQOne.Zero.Web;

// A complete Zero application. Everything below this line is wiring; there is no
// registration for a handler, a validator or an endpoint, because the generator wrote them.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddZeroWeb(options =>
{
    options.RoutePrefix = "/api";

    // Zero requires authorization on every endpoint unless the request says otherwise,
    // because forgetting to protect a request and deciding it is public produce identical
    // source. This sample has no authentication at all, so it opts out deliberately —
    // which is the point of the switch. A real application leaves it on and adds
    // AddAuthorization() and UseAuthorization().
    options.RequireAuthorizationByDefault = false;
});
builder.Services.AddZeroMessaging();
builder.Services.AddZeroValidation();

// The module is generated from this assembly. Its name, its dependencies and its
// registrations all come from the code; open generated/ to read what it produced.
builder.Services.AddModules(new Zero.Sample.Invoices.Module());

var app = builder.Build();

app.MapZeroEndpoints();

await app.RunAsync();

/// <summary>Names the sample's host so a test can reach it.</summary>
public partial class Program;
