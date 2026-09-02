# IQOne.Zero.Resilience

Retries a request that failed for a reason another attempt could change — and only when
trying again is safe.

```csharp
services.AddZeroResilience();
```

**Why this exists when .NET already has Polly:** Polly retries on exceptions. A Zero
operation reports an expected failure by *returning* one, so a policy written against throws
sees a completed task, records a success, and never retries. This fills that gap and nothing
else — keep using Polly for the HTTP call inside your repository.

Queries are retried by default. A command is not, until it declares `IIdempotent`, which
claims something specific: the same command handled twice leaves the state one handling
would have.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
