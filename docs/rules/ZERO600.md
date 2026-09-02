# ZERO600 — A handler waits and tries again by hand

**Severity:** warning · **Category:** Zero.Resilience

A handler loops over its own work, sleeping between attempts.

```csharp
for (var attempt = 0; attempt < 3; attempt++)          // ZERO600
{
    var result = await rates.FetchAsync(pair, cancellationToken);

    if (result.IsSuccess) return result;

    await Task.Delay(200 * (attempt + 1), cancellationToken);
}
```

Three things are wrong with that, and all three are quiet:

- **No jitter.** Every caller that failed at the same moment retries at the same moment and
  arrives together. The retry becomes the load.
- **No brake.** A dependency that is fully down receives three times the traffic it already
  cannot serve, which lengthens its outage.
- **It is one implementation of many.** The next handler will write it slightly differently,
  and nobody will notice until the two behave differently under load.

## Fix

Delete the loop and let the pipeline do it:

```csharp
services.AddZeroResilience();
```

```csharp
return await rates.FetchAsync(pair, cancellationToken);
```

A query is retried without being asked. If this is a command, say whether trying again is
safe — and mean it:

```csharp
public sealed record BookPayment(Guid PaymentId, decimal Amount) : ICommand, IIdempotent;
```

## When a loop in a handler is right

Retrying is not the only reason to loop. Paging through a result set, walking a queue until
it is empty, polling something that reports its own readiness — none of those are retries,
and the rule looks for the shape it is about: a delay inside a loop that repeats work which
just failed.

If yours genuinely is a retry that the pipeline cannot express — a partial retry of one step
inside a longer operation, say — suppress it on the line and write down which case it is.
The explicit line survives the next reader; a silent loop does not explain itself.
