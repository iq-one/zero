using System.Security.Claims;
using IQOne.Zero.Authorization;
using IQOne.Zero.BackgroundWork;
using IQOne.Zero.Caching;
using IQOne.Zero.Configuration.Options;
using IQOne.Zero.Events;
using IQOne.Zero.Messaging;
using IQOne.Zero.Modules;
using IQOne.Zero.Observability;
using IQOne.Zero.Persistence;
using IQOne.Zero.Persistence.EntityFramework;
using IQOne.Zero.Resilience;
using IQOne.Zero.Validation;
using IQOne.Zero.Web;
using Microsoft.EntityFrameworkCore;
using Zero.Sample.Orders.Configuration;
using Zero.Sample.Orders.Data;
using Zero.Sample.Orders.Ordering;
using Zero.Sample.Orders.Pricing;

// A complete application on Zero. Everything below is wiring: no handler, validator,
// endpoint, subscriber or convention is registered by hand, because the generator wrote
// those. Open generated/ to read what it produced.

var builder = WebApplication.CreateBuilder(args);

// ---- the framework -------------------------------------------------------------------
// One Add per capability. Each is sufficient on its own; none needs a second call.

builder.Services.AddZeroOptions<OrderingOptions>();

builder.Services.AddZeroMessaging();
builder.Services.AddZeroEvents();
builder.Services.AddZeroValidation();
builder.Services.AddZeroObservability();
builder.Services.AddZeroCaching();
builder.Services.AddZeroResilience();
builder.Services.AddZeroTransactions();
builder.Services.AddZeroAuthorization(options =>
{
    // Zero's own policies, read by the pipeline in every host. Requirement-based, so the
    // meaning of a permission lives in one class rather than being written out per policy.
    foreach (var policy in SamplePolicies.All)
        options.AddPolicy(policy, new MustHavePermission(policy));
});
builder.Services.AddZeroBackgroundWork();
builder.Services.AddZeroWeb(options => options.RoutePrefix = "/api");

// The engine is the application's choice. The framework's data package names no ORM and
// this one names no database; Sqlite appears here and nowhere else.
builder.Services.AddZeroEntityFramework<OrdersDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Orders")));

// ---- this application ----------------------------------------------------------------

builder.Services.AddScoped<IPricingService, FlakyPricingService>();

// Who the caller is. The framework cannot supply this: which claim carries the identity is
// the application's decision, and IQOne.Zero.Authorization deliberately knows nothing about
// HTTP. Leave it out and every request is anonymous — the framework says so at startup.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser>(services => new ClaimsPrincipalCurrentUser(
    services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User ?? new ClaimsPrincipal()));

builder.Services.AddRequirementHandler<MustHavePermission, MustHavePermissionHandler>();
builder.Services.AddRequirementHandler<MustOwnOrder, Order, MustOwnOrderHandler>();

// The sweep is a command, so it has one implementation whether a clock or an operator
// triggered it. The occurrence it serves is carried by the command, never read from a clock.
builder.Services.AddRecurringCommand<ExpireUnpaidOrders, int>(
    "expire-unpaid-orders",
    JobSchedule.Every(TimeSpan.FromMinutes(1)),
    context => new ExpireUnpaidOrders(context.ScheduledFor));

// Authentication is the host's business, not the framework's. This one reads headers so the
// sample's authorization can be exercised; see HeaderAuthenticationHandler.
builder.Services
    .AddAuthentication(HeaderAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
               HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, null);

var authorization = builder.Services.AddAuthorizationBuilder();

foreach (var policy in SamplePolicies.All)
    authorization.AddPolicy(policy, rule => rule.RequireClaim("permission", policy));

builder.Services.AddModules(new Zero.Sample.Orders.Module());

var app = builder.Build();

await app.SeedAsync();

app.UseAuthentication();
app.UseAuthorization();

app.MapZeroEndpoints();

await app.RunAsync();

/// <summary>Names the sample's host so a test can reach it.</summary>
public partial class Program;
