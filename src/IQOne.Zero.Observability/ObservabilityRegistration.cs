using System.Linq;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Observability;

/// <summary>Adds logging, tracing and metrics to an application.</summary>
public static class ObservabilityRegistration
{
    /// <summary>
    /// Observes every request that goes through the pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One call turns on all three signals. Each can be switched off, but that is a decision
    /// about what a host collects, not about what a handler writes: a handler never mentions
    /// logging, tracing or metrics either way.
    /// </para>
    /// <para>
    /// Nothing starts here — no exporter, no timer, no connection. The activity source and
    /// the meter are inert until a collector subscribes to them by name, and an application
    /// that adds this and configures no collector pays for a null check per request.
    /// </para>
    /// <para>
    /// Logging still needs a logging provider, which is the host's to add: this package
    /// depends on <c>ILogger</c> and deliberately does not register one, because a fallback
    /// registered here would quietly win over the real one an application adds later.
    /// </para>
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Switches a signal off, or opts into logging request contents.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroObservability(
        this IServiceCollection services, Action<ObservabilityOptions>? configure = null)
    {
        // Refined, not replaced. A module and a host may both add observability, and a second
        // call that handed over a fresh instance would silently discard what the first one
        // configured — while leaving a second options singleton behind it in the collection.
        var options = Configured(services) ?? Register(services);

        configure?.Invoke(options);

        // The application's own clock wins when it has one; otherwise the real one. Duration
        // is measured through TimeProvider so a test can state how long a request took
        // instead of sleeping until it is true.
        services.TryAddSingleton(TimeProvider.System);

        // TryAddEnumerable, not Add: calling this twice — a module and a host both being
        // careful — would otherwise double every log line and count every request twice.
        //
        // All three are registered whatever the switches say, and each one reads its own
        // switch as the request goes through. Deciding here instead would mean the switches
        // only worked when they were set by the first caller to arrive, which is an ordering
        // rule nobody can see in their own file.
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>)));

        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>)));

        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>), typeof(MetricsBehavior<,>)));

        return services;
    }

    /// <summary>The options instance already in the collection, when there is one.</summary>
    /// <remarks>
    /// Also finds one a consumer registered themselves before calling this, which is the
    /// answer for an application that keeps its settings somewhere of its own and wants them
    /// to be the ones the behaviours read.
    /// </remarks>
    /// <param name="services">The registrations to look in.</param>
    /// <returns>The registered options, or <see langword="null"/>.</returns>
    private static ObservabilityOptions? Configured(IServiceCollection services)
        => services
            .FirstOrDefault(d => d.ServiceType == typeof(ObservabilityOptions) && !d.IsKeyedService)
            ?.ImplementationInstance as ObservabilityOptions;

    private static ObservabilityOptions Register(IServiceCollection services)
    {
        var options = new ObservabilityOptions();

        services.AddSingleton(options);

        return options;
    }
}
