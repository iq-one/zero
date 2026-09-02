using IQOne.Zero.DependencyInjection.Extensions;
using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Events;

/// <summary>Adds in-process events and their subscribers to an application.</summary>
public static class EventsRegistration
{
    /// <summary>
    /// Registers the publisher and the delivery table, and offers that table to modules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This call alone is enough: <see cref="IPublisher"/>, <see cref="EventRegistry"/> and
    /// <see cref="EventOptions"/> are all resolvable afterwards. The table is filled while
    /// modules configure and sealed when the last one has, so call this before
    /// <c>AddModules</c>; publishing before the table is sealed says so.
    /// </para>
    /// <para>Calling it twice does nothing the second time.</para>
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts how delivery behaves.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroEvents(
        this IServiceCollection services, Action<EventOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetRegisteredInstance<EventsFeatureContributor>() is not null) return services;

        var registry = Register(services, configure);
        var options = services.GetRegisteredInstance<EventOptions>()!;

        services.AddSingleton<IModuleFeatureContributor>(new EventsFeatureContributor(options, registry));

        return services;
    }

    /// <summary>
    /// Builds the delivery table from an explicit set of subscribers, bypassing the module phase.
    /// </summary>
    /// <remarks>
    /// For tests and for hosts that assemble their table by hand. An application calls
    /// <see cref="AddZeroEvents"/> and lets the generator fill the table. The name differs
    /// from that one on purpose: as an overload it would be ambiguous at the call site, and
    /// the two do opposite things.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adds the entries.</param>
    /// <param name="options">Adjusts delivery, exactly as the ordinary entry point does.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroEventsWithHandlers(
        this IServiceCollection services,
        Action<IEventRegistryBuilder> configure,
        Action<EventOptions>? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var registry = Register(services, options);

        configure(registry);
        registry.Freeze();

        return services;
    }

    /// <summary>
    /// What both entry points register.
    /// </summary>
    /// <remarks>
    /// The registry and the publisher are registered here rather than when the module phase
    /// completes, because a capability's <c>Add</c> has to be sufficient on its own — and the
    /// module phase does not run at all in an application that has no modules.
    /// </remarks>
    /// <summary>
    /// Registers everything the capability needs, reusing what is already there.
    /// </summary>
    /// <remarks>
    /// The options are read back from the collection rather than replaced, so calling one
    /// entry point after the other keeps the settings the first one was given. Creating a
    /// fresh <see cref="EventOptions"/> here discarded them silently, which is the worst
    /// shape a configuration bug can take: the switch is set, the code reads a default, and
    /// nothing says the two disagree.
    /// </remarks>
    private static EventRegistry Register(IServiceCollection services, Action<EventOptions>? configure)
    {
        var options = services.GetRegisteredInstance<EventOptions>() ?? new EventOptions();

        configure?.Invoke(options);

        var registry = services.GetRegisteredInstance<EventRegistry>() ?? new EventRegistry();

        services.TryAddSingleton(options);
        services.TryAddSingleton(registry);
        services.TryAddScoped<IPublisher, Publisher>();

        // The application's own clock wins when it has one; otherwise the real one. Per
        // subscriber timing is measured through TimeProvider so a test can state how long a
        // subscriber took instead of sleeping until it is true.
        services.TryAddSingleton(TimeProvider.System);

        return registry;
    }
}

/// <summary>How events are delivered.</summary>
public sealed class EventOptions
{
    /// <summary>
    /// What happens to the subscribers after one that failed. Continue by default.
    /// </summary>
    /// <remarks>
    /// Subscribers are independent by definition — if they were not, they would be one
    /// subscriber — so stopping at the third of five applies the event to some of the
    /// application and not the rest, and which ones depends on an order nobody defined.
    /// Continuing gives every subscriber its chance and hands the caller every failure at
    /// once, which is the union of what stopping and logging would each have told it.
    /// </remarks>
    public HandlerFailure OnHandlerFailure { get; set; } = HandlerFailure.Continue;

    /// <summary>
    /// How many times publishing may re-enter itself before it is called a cycle. Eight by default.
    /// </summary>
    /// <remarks>
    /// A subscriber may legitimately publish — that is how one fact leads to another — but a
    /// chain deeper than a handful is almost always a loop. Without a limit the process dies
    /// of a stack overflow, which cannot be caught and leaves no log line; with one, the
    /// caller gets <see cref="PublishDepthExceededException"/> naming the event.
    /// </remarks>
    public int MaxPublishDepth { get; set; } = 8;

    /// <summary>
    /// Whether an event that nobody subscribes to stops startup. Off by default.
    /// </summary>
    /// <remarks>
    /// Off, and this is the opposite of the equivalent messaging option on purpose. A request
    /// with no handler cannot be served, so it is a defect; an event with no subscriber is
    /// simply a fact nobody has needed yet, and demanding a subscriber would put back the
    /// coupling that publishing removed. Turn it on in an application where every event is
    /// meant to have found its audience, and a silent one means a misspelled subscription.
    /// </remarks>
    public bool RequireSubscriberForEveryEvent { get; set; }
}

/// <summary>What delivery does after a subscriber fails.</summary>
public enum HandlerFailure
{
    /// <summary>Every remaining subscriber still runs, and all the failures come back together.</summary>
    Continue,

    /// <summary>Delivery stops at the first failure, and the subscribers after it do not run.</summary>
    /// <remarks>
    /// Which subscribers those are depends on an order the framework does not define, so this
    /// is for the application that has decided one failure invalidates the whole fan-out —
    /// not for expressing a dependency between two subscribers.
    /// </remarks>
    Stop
}

/// <summary>
/// Offers the delivery table to modules while they configure, then seals and checks it.
/// </summary>
/// <remarks>
/// The module system knows nothing about events: it only runs the contributors it finds.
/// Adding events to an application is a registration, not a change to the core.
/// </remarks>
internal sealed class EventsFeatureContributor(EventOptions options, EventRegistry registry)
    : IModuleFeatureContributor
{
    public void Contribute(IModuleFeatureCollection features)
        => features.Set<IEventRegistryBuilder>(registry);

    /// <remarks>
    /// Sealing only. The registry and the publisher were registered by <c>AddZeroEvents</c>,
    /// so they exist whether or not the application has modules.
    /// </remarks>
    public void Complete(IServiceCollection services)
    {
        registry.Freeze();

        if (options.RequireSubscriberForEveryEvent && registry.Unsubscribed.Count > 0)
            throw new InvalidOperationException(
                "Nobody subscribes to these events: " +
                string.Join(", ", registry.Unsubscribed.Select(t => t.FullName).Order(StringComparer.Ordinal)) +
                ". Implement IEventHandler<> for each, or set " +
                $"{nameof(EventOptions)}.{nameof(EventOptions.RequireSubscriberForEveryEvent)} to false.");
    }
}

/// <summary>Reaches the delivery table from inside a module's configure-services step.</summary>
public static class EventsModuleContextExtensions
{
    /// <summary>The delivery table a module registers its subscribers into.</summary>
    /// <param name="context">The module's configure-services context.</param>
    /// <returns>The registry builder.</returns>
    /// <exception cref="InvalidOperationException">
    /// Events were not added to the application; call <c>AddZeroEvents()</c> first.
    /// </exception>
    public static IEventRegistryBuilder Events(this IModuleServiceContext context)
        => context.Feature<IEventRegistryBuilder>();
}
