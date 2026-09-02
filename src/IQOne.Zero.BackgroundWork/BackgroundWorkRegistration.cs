using IQOne.Zero.DependencyInjection.Extensions;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.BackgroundWork;

/// <summary>Adds recurring work to an application.</summary>
public static class BackgroundWorkRegistration
{
    /// <summary>
    /// Runs the registered jobs under the application's host: one fresh scope per run, never
    /// overlapping, stopping when the application stops.
    /// </summary>
    /// <remarks>
    /// Call this once; jobs are added with <see cref="AddRecurringJob{TJob}"/> or
    /// <see cref="AddRecurringCommand{TCommand}"/> and may be added before or after it.
    /// Calling it again refines the options rather than registering a second host.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts how jobs run.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroBackgroundWork(
        this IServiceCollection services, Action<BackgroundWorkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = Options(services);

        configure?.Invoke(options);

        // Already hosted: a second call adjusted the options above and is done. Registering
        // the host twice runs every job twice, which is the failure a scheduled job hides
        // best -- two reconciliations racing look exactly like one that is slow.
        if (services.GetRegisteredInstance<Hosted>() is not null) return services;

        services.AddSingleton(new Hosted());
        services.AddSingleton<IHostedService>(RecurringJobHost.Create);

        return services;
    }

    /// <summary>
    /// Runs a job class on a schedule.
    /// </summary>
    /// <remarks>
    /// The job is resolved fresh for every run, from that run's own scope, so it may take
    /// scoped dependencies. Holding one across runs is the captive-dependency failure the
    /// compiler reports as ZERO009 — here it would be invisible, because a job with a stale
    /// context keeps working until the context's connection is dropped.
    /// </remarks>
    /// <typeparam name="TJob">The job to run.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="name">
    /// How the job is named in logs, in status and in <c>BackgroundWorkOptions.Disabled</c>.
    /// Stated rather than taken from the type name, because a rename must not silently
    /// re-enable a job somebody switched off.
    /// </param>
    /// <param name="schedule">When it runs.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRecurringJob<TJob>(
        this IServiceCollection services, string name, JobSchedule schedule)
        where TJob : class, IRecurringJob
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.TryAddScoped<TJob>();

        return services.AddRecurringJob(
            name,
            schedule,
            static (provider, context, cancellationToken) =>
                provider.GetRequiredService<TJob>().RunAsync(context, cancellationToken));
    }

    /// <summary>
    /// Sends a command on a schedule.
    /// </summary>
    /// <remarks>
    /// Most scheduled work is this: a use case that already exists, run on a clock instead of
    /// on a request. Expressing it as a command rather than a job class means the work has
    /// one implementation, one set of validators and one place in the pipeline, whether it
    /// was triggered by a person or by the schedule.
    /// </remarks>
    /// <typeparam name="TCommand">The command to send.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="name">How the job is named in logs, in status and when switching it off.</param>
    /// <param name="schedule">When it runs.</param>
    /// <param name="create">
    /// Builds the command for one occurrence. Takes the context so the command can carry the
    /// occurrence it serves rather than reading a clock.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRecurringCommand<TCommand>(
        this IServiceCollection services,
        string name,
        JobSchedule schedule,
        Func<JobRunContext, TCommand> create)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(create);

        return services.AddRecurringJob(name, schedule, async (provider, context, cancellationToken) =>
            await provider
                .GetRequiredService<ISender>()
                .SendAsync(create(context), cancellationToken)
                .ConfigureAwait(false));
    }

    /// <summary>
    /// Sends a command that produces a value on a schedule.
    /// </summary>
    /// <remarks>
    /// The value is recorded with the run and otherwise discarded — nobody is waiting for
    /// it. This overload exists because the most natural scheduled command answers "how many
    /// did I do": a sweep, a reconciliation, a retry pass. Without it those had to be
    /// rewritten as <see cref="ICommand"/> and lose the count they already knew.
    /// </remarks>
    /// <typeparam name="TCommand">The command to send.</typeparam>
    /// <typeparam name="TResponse">What the command produces.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="name">How the job is named in logs, in status and when switching it off.</param>
    /// <param name="schedule">When it runs.</param>
    /// <param name="create">
    /// Builds the command for one occurrence. Takes the context so the command can carry the
    /// occurrence it serves rather than reading a clock.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddRecurringCommand<TCommand, TResponse>(
        this IServiceCollection services,
        string name,
        JobSchedule schedule,
        Func<JobRunContext, TCommand> create)
        where TCommand : ICommand<TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(create);

        return services.AddRecurringJob(name, schedule, async (provider, context, cancellationToken) =>
            await provider
                .GetRequiredService<ISender>()
                .SendAsync(create(context), cancellationToken)
                .ConfigureAwait(false));
    }

    /// <summary>Runs an inline body on a schedule.</summary>
    /// <remarks>
    /// For work too small to name. Anything that needs a test of its own wants
    /// <see cref="AddRecurringCommand{TCommand}"/> instead — a lambda registered here cannot
    /// be reached from a test without starting the host.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="name">How the job is named in logs, in status and when switching it off.</param>
    /// <param name="schedule">When it runs.</param>
    /// <param name="run">
    /// The work for one occurrence. The provider is that run's own scope; resolve from it
    /// rather than capturing anything.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">A job is already registered under that name.</exception>
    public static IServiceCollection AddRecurringJob(
        this IServiceCollection services,
        string name,
        JobSchedule schedule,
        Func<IServiceProvider, JobRunContext, CancellationToken, Task<Result>> run)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(run);

        Catalog(services).Add(new RecurringJobDescriptor(name, schedule, run));
        Options(services);

        return services;
    }

    /// <summary>
    /// The one catalog for this application, found in the collection before any provider exists.
    /// </summary>
    /// <remarks>
    /// Registered as an instance for the same reason the dispatch table is: a job may be
    /// added before or after the entry point, and neither order may lose the other's work.
    /// </remarks>
    private static RecurringJobCatalog Catalog(IServiceCollection services)
    {
        if (services.GetRegisteredInstance<RecurringJobCatalog>() is { } existing) return existing;

        var catalog = new RecurringJobCatalog();

        services.AddSingleton(catalog);
        services.AddSingleton<IBackgroundWorkStatus>(catalog);

        return catalog;
    }

    private static BackgroundWorkOptions Options(IServiceCollection services)
    {
        if (services.GetRegisteredInstance<BackgroundWorkOptions>() is { } existing) return existing;

        var options = new BackgroundWorkOptions();

        services.AddSingleton(options);

        // The application's clock wins when it registered one. Otherwise the real one -- and
        // a test that supplies a fake states how much time passed instead of waiting for it.
        services.TryAddSingleton(TimeProvider.System);

        return options;
    }

    /// <summary>
    /// Marks the host as registered.
    /// </summary>
    /// <remarks>
    /// A sentinel rather than a scan over the descriptors: a factory registration cannot be
    /// compared by identity, and matching on the service type alone would find any other
    /// hosted service the application has and skip ours.
    /// </remarks>
    private sealed class Hosted;
}
