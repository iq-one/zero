using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IQOne.Zero.BackgroundWork;

/// <summary>
/// Carries the job schedules into a generic host.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="IHostedService"/>, registered the way <c>ModuleHostedService</c> is — through
/// <c>TryAddEnumerable</c>, so a module and a host both adding background work does not start
/// every schedule twice. There is no second host integration here and no
/// <c>BackgroundService</c> per job: the host has one place to look, and shutdown has one
/// thing to wait for.
/// </para>
/// <para>
/// <c>StartAsync</c> fixes each job's first occurrence and then returns. Hosted services start
/// one after another, so anything that waited here would delay every service after it and,
/// in an ASP.NET application, the first request too.
/// </para>
/// <para>
/// <c>StopAsync</c> cancels the loops and waits for whatever is mid-run, bounded by the token
/// the host supplies — which is the host's own shutdown timeout, not one invented here. A job
/// still running when that expires is abandoned, which is why a job body has to honour its
/// cancellation token (ZERO551).
/// </para>
/// </remarks>
/// <param name="catalog">The registered jobs and their status.</param>
/// <param name="options">Whether anything runs at all.</param>
/// <param name="scopes">Opens the fresh scope each run resolves from.</param>
/// <param name="time">The clock every wait and measurement goes through.</param>
/// <param name="loggers">
/// The host's logger factory. Supplied by the registration rather than injected, so that an
/// application with no logging configured still starts — and so that nothing this package
/// registers can shadow the logging an application adds later.
/// </param>
internal sealed class RecurringJobHost(
    RecurringJobCatalog catalog,
    BackgroundWorkOptions options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILoggerFactory loggers) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<Task> _loops = [];

    private bool _disposed;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Switched off wholesale: nothing starts, everything stays registered and visible. A
        // test that builds the real application gets the real wiring without the real
        // schedule, which is the difference between a suite that is reproducible and one that
        // fails on whichever test happened to be running at the top of the minute.
        if (!options.Enabled)
        {
            foreach (var job in catalog.Registered)
            {
                catalog.Allowed(job.Name, false);
                JobLog.Disabled(Logger(job.Name), job.Name);
            }

            return Task.CompletedTask;
        }

        // Read once, here, so every job in this process dates its schedule from the same
        // instant and two jobs on the same period stay on the same marks.
        var now = time.GetUtcNow();

        foreach (var job in catalog.Registered)
        {
            var loop = new RecurringJobLoop(job, catalog, options, scopes, time, Logger(job.Name));

            loop.ScheduleFrom(now);

            // Started, not awaited. The loop yields before it does anything, so this returns
            // as soon as the continuation is queued; the task is kept because shutdown has to
            // be able to wait for it.
            _loops.Add(loop.RunAsync(_stopping.Token));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_loops.Count == 0) return;

        await _stopping.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(_loops).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host's shutdown timeout expired with work still in flight. Nothing useful is
            // left to do — the process is going — and throwing from StopAsync would replace a
            // clean shutdown with a stack trace that says only that shutdown took too long.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _stopping.Dispose();
    }

    /// <summary>
    /// A logger per job, under this package's namespace.
    /// </summary>
    /// <remarks>
    /// <c>IQOne.Zero.BackgroundWork.&lt;name&gt;</c>, so one job can be turned up or down from
    /// configuration the same way one request type can. A single category for the whole
    /// package would mean the only choice is all of them.
    /// </remarks>
    private ILogger Logger(string name) => loggers.CreateLogger($"{ZeroJobTelemetry.MeterName}.{name}");

    /// <summary>
    /// Builds the host, taking the application's logger factory when it has one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolved optionally rather than injected, and this is the reason: a
    /// <see cref="NullLoggerFactory"/> registered as a fallback would win over the real
    /// factory an application adds afterwards, because <c>AddLogging</c> itself registers with
    /// <c>TryAdd</c>. Asking for it here instead means the real one is used whenever it
    /// exists, nothing is registered that could shadow it, and
    /// <c>AddZeroBackgroundWork()</c> on its own still builds under
    /// <c>ValidateOnBuild</c> — which is what makes "one call is enough" true rather than
    /// nearly true.
    /// </para>
    /// </remarks>
    /// <param name="services">The built provider.</param>
    /// <returns>The host.</returns>
    internal static RecurringJobHost Create(IServiceProvider services) => new(
        services.GetRequiredService<RecurringJobCatalog>(),
        services.GetRequiredService<BackgroundWorkOptions>(),
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<TimeProvider>(),
        services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance);
}
