using System.Security.Claims;
using IQOne.Zero.Authorization;
using IQOne.Zero.BackgroundWork;
using IQOne.Zero.Caching;
using IQOne.Zero.Events;
using IQOne.Zero.Messaging;
using IQOne.Zero.Modules;
using IQOne.Zero.Observability;
using IQOne.Zero.Persistence;
using IQOne.Zero.Persistence.EntityFramework;
using IQOne.Zero.Resilience;
using IQOne.Zero.Validation;
using IQOne.Zero.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Zero.Sample.Orders.Data;
using Zero.Sample.Orders.Host;

// The host. Three jobs and nothing else: turn the framework on, say how this deployment
// connects and who its callers are, and name the modules.
//
// No handler, validator, endpoint, subscriber, policy, job, convention or service is
// registered here. Each module does its own — look for Module.cs in any of them.

var builder = WebApplication.CreateBuilder(args);

// ---- the framework ------------------------------------------------------------------
// One call per capability. Each is sufficient on its own.

builder.Services.AddZeroMessaging();
builder.Services.AddZeroEvents();
builder.Services.AddZeroValidation();
builder.Services.AddZeroObservability();
builder.Services.AddZeroCaching();
builder.Services.AddZeroResilience();
builder.Services.AddZeroTransactions();
builder.Services.AddZeroAuthorization();
builder.Services.AddZeroBackgroundWork();
builder.Services.AddZeroWeb(options => options.RoutePrefix = "/api");

// ---- this deployment ----------------------------------------------------------------
// The engine and the connection string: the only place either appears.

builder.Services.AddZeroEntityFramework<OrdersDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Orders")));

// Who the caller is. The framework cannot decide which claim carries the identity, and
// IQOne.Zero.Authorization deliberately knows nothing about HTTP. Leave this out and every
// caller is anonymous — Zero says so once, at startup.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser>(services => new ClaimsPrincipalCurrentUser(
    services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User ?? new ClaimsPrincipal()));

// Authentication is the host's business. This one reads headers so the sample's
// authorization can be exercised; see HeaderAuthenticationHandler.
builder.Services
    .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(
        HeaderAuthenticationHandler.SchemeName, null);

builder.Services.AddAuthorization();

// ---- the modules --------------------------------------------------------------------
// Named, not ordered. Ordering depends on Catalog and Pricing, so it is configured after
// them — derived from the assembly reference graph, which cannot drift from what the
// projects actually reference.

// Data has no Module.cs of its own, and is listed anyway: its generated module is what
// registers the soft-delete filter and the audit stamps. A module with nothing written by
// hand still carries everything the generator found in its assembly.
builder.Services.AddModules(
    new Zero.Sample.Orders.Data.Module(),
    new Zero.Sample.Orders.Catalog.Module(),
    new Zero.Sample.Orders.Ordering.Module(),
    new Zero.Sample.Orders.Pricing.Module());

var app = builder.Build();

// The schema. A sample with a throwaway SQLite file; a real application uses migrations,
// because a schema created from the model has no history to migrate from.
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreatedAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapZeroEndpoints();

await app.RunAsync();

/// <summary>Names the host so a test can reach it.</summary>
public partial class Program;
