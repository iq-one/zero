using IQOne.Zero.Authorization;
using IQOne.Zero.BackgroundWork;
using IQOne.Zero.Configuration.Options;
using IQOne.Zero.Modules;

namespace Zero.Sample.Orders.Ordering;

/// <summary>
/// The ordering module.
/// </summary>
/// <remarks>
/// Everything here is something the generator cannot know. The handlers, validators,
/// specifications, subscribers, requirement handlers and the model convention are all
/// registered for it, because each of them says what it is by the abstraction it implements.
/// </remarks>
public sealed partial class Module
{
    /// <summary>What this module needs registered that nothing could infer.</summary>
    /// <param name="context">The registrations to add to.</param>
    partial void OnConfigureServices(IModuleServiceContext context)
    {
        // How ordering behaves here. Bound and validated at startup; a nonsensical payment
        // window stops the application rather than surfacing on the first order.
        context.Services.AddZeroOptions<OrderingOptions>();

        // The policies guarding this module's routes, declared next to the routes rather
        // than in a host that has no reason to know what "orders:place" protects.
        foreach (var permission in OrderPolicies.All)
            context.Authorization().AddPolicy(permission, new MustHavePermission(permission));

        // The sweep is a command, so it has one implementation whether a clock or an
        // operator triggered it. The occurrence it serves is carried by the command; a run
        // that read the clock instead would leave a gap the size of its own start-up delay.
        context.Services.AddRecurringCommand<ExpireUnpaidOrders, int>(
            "expire-unpaid-orders",
            JobSchedule.Every(TimeSpan.FromMinutes(1)),
            run => new ExpireUnpaidOrders(run.ScheduledFor));
    }
}
