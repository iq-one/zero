# IQOne.Zero.BackgroundWork

Recurring work that happens outside a request.

```csharp
services.AddZeroBackgroundWork();

services.AddRecurringCommand(
    "reconcile-payments",
    JobSchedule.Every(TimeSpan.FromMinutes(15)),
    context => new ReconcilePayments(Since: context.ScheduledFor));
```

One fresh scope per run, never overlapping, stopping when the application stops. Time comes
from `TimeProvider`, so a schedule is testable without waiting.

The compiler reports a job that reads the clock instead of the occurrence it is serving
(**ZERO550**) and one that ignores its cancellation token (**ZERO551**).

Running on several replicas is **not** solved here — see the rule file for what to do about
it instead.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
