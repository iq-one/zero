# ZERO551 — A job ignores the cancellation token

**Severity:** warning · **Category:** Zero.BackgroundWork

A recurring job's `RunAsync` never uses the `CancellationToken` it was given.

```csharp
public async Task<Result> RunAsync(JobRunContext context, CancellationToken cancellationToken)
{
    await holds.ReleaseBeforeAsync(context.ScheduledFor);    // ZERO551 — token not passed

    return Result.Success();
}
```

The token is cancelled when the application is stopping. A run that ignores it does not stop:
it keeps working until the host's shutdown timeout expires and then the process is killed
**part-way through the work** — after some rows were written and before the rest were.

That is worse than being stopped cleanly, and it only happens during a deployment, which is
exactly when nobody is reading logs.

## Fix

Pass it to everything awaited:

```csharp
await holds.ReleaseBeforeAsync(context.ScheduledFor, cancellationToken);
```

For a long loop over records, check it between batches so the run can decline to continue:

```csharp
foreach (var batch in batches)
{
    cancellationToken.ThrowIfCancellationRequested();

    await ProcessAsync(batch, cancellationToken);
}
```

Stopping between batches leaves the work in a state the next run can pick up. Stopping in
the middle of one does not.

## When there is nothing to pass it to

A job whose body is synchronous and finishes instantly has nothing to cancel. The rule still
fires, because from the outside those look identical to the job that forgot — so say which
one this is:

```csharp
#pragma warning disable ZERO551 // Synchronous and instant; there is nothing to cancel.
```

An explicit line is the point. It survives the next person adding an `await` below it.
