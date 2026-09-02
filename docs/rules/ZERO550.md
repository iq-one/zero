# ZERO550 — A job reads the clock instead of the occurrence it is serving

**Severity:** warning · **Category:** Zero.BackgroundWork

A recurring job reads the current time inside `RunAsync`.

```csharp
public async Task<Result> RunAsync(JobRunContext context, CancellationToken cancellationToken)
{
    var since = time.GetUtcNow().AddMinutes(-15);            // ZERO550
    await ReconcileAsync(since, cancellationToken);

    return Result.Success();
}
```

`context.ScheduledFor` is the occurrence being served. The clock is the moment this run
happened to start — later, by however long the queue, the previous run, or start-up took.

A job that reconciles "everything since last time" and takes its window from the clock
therefore leaves a gap the size of that difference. **Every run.** And every run looks like
it worked: no error, no missing job, just a slowly widening set of records nobody reconciled.

## Fix

Take the window from the occurrence:

```csharp
public async Task<Result> RunAsync(JobRunContext context, CancellationToken cancellationToken)
{
    await ReconcileAsync(context.ScheduledFor.AddMinutes(-15), cancellationToken);

    return Result.Success();
}
```

Or, better, let the command carry it, so the same use case works from a request too:

```csharp
services.AddRecurringCommand(
    "reconcile-payments",
    JobSchedule.Every(TimeSpan.FromMinutes(15)),
    context => new ReconcilePayments(Since: context.ScheduledFor));
```

## When reading the clock is right

Measuring how long something took, stamping "processed at", deciding whether a lease is
still valid — all of those are about *now* and belong on the clock. The rule fires on any
clock read inside a job because it cannot tell which one you meant; if this is one of those
cases, suppress it on the line with a comment saying which.

The rule never fires outside `IRecurringJob.RunAsync`. Reading a clock is perfectly ordinary
everywhere else; what makes it suspect here is that the method was handed the occurrence and
chose a different answer.
