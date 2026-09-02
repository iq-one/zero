---
id: zero.backgroundwork.work-on-a-clock
title: Schedule a command; never write your own loop
package: IQOne.Zero.BackgroundWork
applies-to: ["**/*.cs"]
enforced-by: [ZERO550, ZERO551]
---

Work that happens on a clock is registered, not looped. The host runs it in a fresh scope
per run, never overlapping, and stops it when the application stops.

## Do

Most scheduled work is a use case that already exists, run on a clock instead of on a
request. Register the command; there is no class to write:

```csharp
services.AddZeroBackgroundWork();

services.AddRecurringCommand(
    "reconcile-payments",
    JobSchedule.Every(TimeSpan.FromMinutes(15)),
    context => new ReconcilePayments(Since: context.ScheduledFor));
```

That way the work has one implementation, one set of validators and one place in the
pipeline, whether a person triggered it or the schedule did.

When it needs its own class:

```csharp
public sealed class SweepExpiredHolds(IHoldStore holds) : IRecurringJob
{
    public async Task<Result> RunAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        await holds.ReleaseBefore(context.ScheduledFor, cancellationToken);

        return Result.Success();
    }
}
```

```csharp
services.AddRecurringJob<SweepExpiredHolds>("sweep-holds", JobSchedule.Every(TimeSpan.FromHours(1)));
```

## Don't

Do not write the loop yourself:

```csharp
public sealed class Sweeper : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)      // don't
        {
            await DoWorkAsync();
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
```

Three things go wrong in that shape and all three are quiet: the scope is captured once so
every run shares the first `DbContext`; a run that takes longer than the delay silently
drifts; and an exception ends the loop for the life of the process with nothing scheduled to
notice.

Do not loop inside `RunAsync` either. It runs **one** occurrence and returns; the schedule
brings it back. A body that loops until cancelled runs once and never again, and nothing
reports it.

## Reason about the occurrence, not the clock

`context.ScheduledFor` is the occurrence being served. `TimeProvider.GetUtcNow()` is the
moment this run happened to start, which is later — by the queue, by the previous run, by
however long start-up took.

A job that reconciles "everything since last time" and takes its window from the clock leaves
a gap the size of that difference, **every time**, and the gap is invisible because each run
looks like it worked. This is **ZERO550**.

## Pass the token to everything awaited

The token is cancelled when the application is stopping. A run that ignores it is killed
part-way through by the host's shutdown timeout instead of finishing or declining to start.
This is **ZERO551**.

## Overlap, failure, and more than one replica

**Overlap:** a run never starts while the previous one is still going. The occurrence it
would have served is dropped and counted — read `IBackgroundWorkStatus` to see how often,
because a job that regularly drops occurrences is one whose interval is wrong.

**Failure:** a failed or throwing run is recorded and the schedule continues. Job 400 of
10,000 failing must not stop the other 9,600.

**Replicas:** three instances of your service will each run the nightly job.
**This package does not solve that** and does not pretend to. Your options, in order of how
much they cost: leave the job enabled on one instance only (`BackgroundWorkOptions.Disabled`
on the others), take a lease in your own database as the job's first act, or move the schedule
out to something built for it. Choose deliberately — the default is that it runs everywhere.

## Testing

Register a fake `TimeProvider` and state how much time passed. A test that waits for a
schedule is a test that is slow and, eventually, flaky.
