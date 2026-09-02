using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IQOne.Zero.BackgroundWork;

/// <summary>
/// Keeps one job on its schedule for as long as the application runs.
/// </summary>
/// <remarks>
/// <para>
/// One loop per job, and each loop serves its occurrences one at a time. That is the overlap
/// answer, and it is not configurable: a job whose run takes longer than its period does not
/// start a second copy of itself. Two runs of the same reconciliation touching the same rows
/// is a deadlock or a double posting, and the applications that genuinely want concurrency
/// want it <em>inside</em> one run, where they can bound it — not from a scheduler that
/// silently multiplies the load exactly when the system is already slow.
/// </para>
/// <para>
/// Occurrences that fall during a run are dropped rather than queued, counted on the status
/// report, and logged at warning. See <see cref="JobSchedule.Next"/> for why.
/// </para>
/// <para>
/// Nothing in here stops the schedule. A run that returns a failure and a run that throws are
/// both recorded and both followed by the next occurrence. A schedule that stopped itself on
/// the four hundredth run would stop in the small hours and be discovered by whoever noticed
/// the reports were stale, which is the worst of both: the work is not happening and nobody
/// has been told.
/// </para>
/// </remarks>
/// <param name="job">What to run and how often.</param>
/// <param name="catalog">Where runs are counted and the status report is kept.</param>
/// <param name="options">Read on every occurrence, so switching a job off does not need a restart.</param>
/// <param name="scopes">Opens the fresh scope each run resolves from.</param>
/// <param name="time">The clock. Every wait and every measurement goes through it.</param>
/// <param name="logger">Writes under this job's own category.</param>
internal sealed class RecurringJobLoop(
    RecurringJobDescriptor job,
    RecurringJobCatalog catalog,
    BackgroundWorkOptions options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger logger)
{
    /// <summary>
    /// The longest single wait, however far away the next occurrence is.
    /// </summary>
    /// <remarks>
    /// Waking up to look at the clock costs nothing and buys two things: a schedule longer than
    /// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> can express does not
    /// throw, and a machine whose clock was corrected — by NTP, by a resumed VM — reaches the
    /// right occurrence within the hour instead of at the time the old clock implied.
    /// </remarks>
    private static readonly TimeSpan LongestWait = TimeSpan.FromHours(1);

    /// <summary>When the next occurrence falls. Fixed before the loop starts.</summary>
    /// <remarks>
    /// Computed by the host, synchronously, while <c>StartAsync</c> is still on the stack.
    /// Working it out inside the loop would date the schedule from whenever the thread pool
    /// got round to it, which is unpredictable under a cold start and untestable under a
    /// clock the test moves by hand.
    /// </remarks>
    private DateTimeOffset _due;

    /// <summary>Prepares the first occurrence and reports it.</summary>
    /// <param name="now">The time the host started.</param>
    internal void ScheduleFrom(DateTimeOffset now)
    {
        _due = now + job.Schedule.InitialDelay;
        catalog.Due(job.Name, _due);
    }

    /// <summary>Serves this job's occurrences until the application stops.</summary>
    /// <param name="stopping">Cancelled when the host is shutting down.</param>
    internal async Task RunAsync(CancellationToken stopping)
    {
        // Hands control back to StartAsync before doing anything. IHostedService starts its
        // services one after another, so a loop that ran even briefly on this thread would
        // delay every service after it — and the application's first request with them.
        await Task.Yield();

        var allowed = Announce();
        long number = 0;

        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await WaitUntilAsync(_due, stopping).ConfigureAwait(false);

                if (stopping.IsCancellationRequested) break;

                allowed = Reconsider(allowed);

                if (allowed)
                {
                    number = catalog.NextNumber(job.Name);

                    await RunOnceAsync(
                        new JobRunContext(job.Name, number, _due, time.GetUtcNow()), stopping).ConfigureAwait(false);
                }

                var (next, skipped) = job.Schedule.Next(_due, time.GetUtcNow());

                _due = next;
                catalog.Due(job.Name, _due);

                if (skipped == 0) continue;

                catalog.Dropped(job.Name, skipped);
                JobLog.Skipped(logger, job.Name, number, skipped, _due);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Shutdown. Every other cancellation is a fault and is reported as one.
        }
        catch (Exception exception)
        {
            // Unreachable by design — every run is wrapped — so if it happens the scheduler
            // itself is broken and this job is gone until the process restarts. Loud.
            JobLog.ScheduleStopped(logger, exception, job.Name);
        }
        finally
        {
            catalog.Due(job.Name, null);
        }
    }

    /// <summary>Says what this job is going to do, once, at startup.</summary>
    /// <returns>Whether it is currently allowed to run.</returns>
    private bool Announce()
    {
        var allowed = options.Runs(job.Name);

        catalog.Allowed(job.Name, allowed);

        if (allowed) JobLog.Scheduled(logger, job.Name, job.Schedule.ToString(), _due);
        else JobLog.Disabled(logger, job.Name);

        return allowed;
    }

    /// <summary>
    /// Re-reads whether the job may run, and says so when the answer has changed.
    /// </summary>
    /// <remarks>
    /// Per occurrence rather than at startup, so a job switched off through reloaded
    /// configuration stops on the next tick rather than on the next deployment. Only the
    /// change is logged: an application that leaves a job off would otherwise get the same
    /// line every period forever, which is how a log stops being read.
    /// </remarks>
    /// <param name="was">What the answer was last time.</param>
    /// <returns>What it is now.</returns>
    private bool Reconsider(bool was)
    {
        var allowed = options.Runs(job.Name);

        if (allowed == was) return allowed;

        catalog.Allowed(job.Name, allowed);

        if (allowed) JobLog.Scheduled(logger, job.Name, job.Schedule.ToString(), _due);
        else JobLog.Disabled(logger, job.Name);

        return allowed;
    }

    /// <summary>Waits for an occurrence, in stretches short enough to notice a corrected clock.</summary>
    private async Task WaitUntilAsync(DateTimeOffset due, CancellationToken cancellationToken)
    {
        while (true)
        {
            var remaining = due - time.GetUtcNow();

            if (remaining <= TimeSpan.Zero) return;

            await Task.Delay(remaining < LongestWait ? remaining : LongestWait, time, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serves one occurrence, in a scope of its own, and survives whatever it does.
    /// </summary>
    /// <remarks>
    /// The scope is the reason this package exists. It is opened here and disposed the moment
    /// the run ends, so the job's dependencies — a database context, a unit of work, anything
    /// else marked <c>IScoped</c> — live exactly as long as the run and no longer. A hosted
    /// service resolving its dependencies once would hold the first ones forever: the captive
    /// dependency Zero reports as ZERO009 when it can see the constructor, and cannot see at
    /// all once somebody has written the loop by hand.
    /// </remarks>
    private async Task RunOnceAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        JobLog.Started(logger, job.Name, context.Number, context.ScheduledFor);

        // Parent of whatever the run sends. A command dispatched inside it gets its span from
        // Observability's TracingBehavior, nested under this one, so a single trace shows both
        // the occurrence and the work it caused.
        using var activity = ZeroJobTelemetry.Source.StartActivity(job.Name, ActivityKind.Internal);

        activity?.SetTag(ZeroJobTelemetry.JobNameTag, job.Name);

        var started = time.GetTimestamp();

        var outcome = JobRunOutcome.Succeeded;
        Error error = default;
        var errorCount = 0;
        Exception? thrown = null;

        try
        {
            await using var scope = scopes.CreateAsyncScope();

            var result = await job.Run(scope.ServiceProvider, context, cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                outcome = JobRunOutcome.Failed;
                error = result.Error;
                errorCount = result.Errors.Count;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = JobRunOutcome.Cancelled;
        }
        catch (Exception exception)
        {
            // Not rethrown. The pipeline rethrows because an edge has to decide what an
            // unplanned exception means to the caller; here there is no caller and no edge,
            // and letting it out would end the schedule for the life of the process.
            outcome = JobRunOutcome.Faulted;
            thrown = exception;
        }

        var elapsed = time.GetElapsedTime(started);

        var run = new JobRun(
            job.Name,
            context.Number,
            context.ScheduledFor,
            context.StartedAt,
            elapsed,
            outcome,
            thrown?.Message ?? (outcome == JobRunOutcome.Failed ? error.ToString() : null));

        var consecutive = catalog.Record(run);

        Report(run, error, errorCount, thrown, consecutive);
        Measure(activity, run, outcome == JobRunOutcome.Failed ? error.Code : thrown?.GetType().Name);

        catalog.Completed?.Invoke(run);
    }

    /// <summary>Writes the one line that says how the run turned out.</summary>
    private void Report(JobRun run, Error error, int errorCount, Exception? thrown, long consecutive)
    {
        var elapsed = run.Duration.TotalMilliseconds;

        switch (run.Outcome)
        {
            case JobRunOutcome.Succeeded:
                JobLog.Succeeded(logger, job.Name, run.Number, elapsed);

                break;

            case JobRunOutcome.Failed:
                JobLog.Failed(
                    logger, job.Name, run.Number, elapsed, errorCount,
                    error.Kind, error.Code, error.Message, consecutive);

                break;

            case JobRunOutcome.Faulted:
                JobLog.Threw(logger, thrown!, job.Name, run.Number, elapsed, consecutive);

                break;

            case JobRunOutcome.Cancelled:
                JobLog.Cancelled(logger, job.Name, run.Number, elapsed);

                break;
        }
    }

    /// <summary>Tags the activity and records the two instruments a dashboard reads.</summary>
    private static void Measure(Activity? activity, JobRun run, string? errorType)
    {
        var tags = new TagList
        {
            { ZeroJobTelemetry.JobNameTag, run.Name },
            { ZeroJobTelemetry.JobOutcomeTag, run.Outcome.ToTagValue() }
        };

        if (errorType is { Length: > 0 }) tags.Add(ZeroJobTelemetry.ErrorTypeTag, errorType);

        ZeroJobTelemetry.Runs.Add(1, tags);
        ZeroJobTelemetry.Duration.Record(run.Duration.TotalSeconds, tags);

        if (activity is null) return;

        activity.SetTag(ZeroJobTelemetry.JobOutcomeTag, run.Outcome.ToTagValue());

        if (errorType is { Length: > 0 }) activity.SetTag(ZeroJobTelemetry.ErrorTypeTag, errorType);

        // Cancelled is not an error: a run abandoned by a deployment must not colour the trace
        // the same way a broken job does.
        if (run.Outcome is JobRunOutcome.Failed or JobRunOutcome.Faulted)
            activity.SetStatus(ActivityStatusCode.Error, run.Failure);
    }
}
