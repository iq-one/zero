---
id: zero.resilience.retry-what-is-worth-retrying
title: Let the pipeline retry, and only say a command is idempotent when it is
package: IQOne.Zero.Resilience
applies-to: ["**/*.cs"]
enforced-by: [ZERO600]
---

```csharp
services.AddZeroResilience();
```

That is the whole setup. Queries are now retried when they fail for a reason another attempt
could change; commands are not, until they say they are safe.

## Why this exists when .NET already has Polly

Polly is good and Zero does not replace it. It retries on **exceptions**, and a Zero
operation reports an expected failure by *returning* one:

```csharp
return Error.Unavailable("rates.down", "The rate service did not answer.");
```

Nothing is thrown. A retry policy written against throws — every general-purpose one,
including Polly's out of the box — sees a completed task, records a success, and never
retries. That gap is the only thing this package fills.

So: use Polly for the HTTP call inside your repository, where exceptions are what happen.
Use this for the request pipeline, where failures are values. They are not alternatives.

## A query is retried; a command has to earn it

A query changes nothing — `IQuery<T>` says so — and trying again costs a round trip.

A command is different, and the cost of being wrong is not a wasted round trip: it is a
customer charged twice, an email sent twice, a stock movement booked twice. So the safe case
is the default, and the dangerous one is written down:

```csharp
public sealed record BookPayment(Guid PaymentId, decimal Amount) : ICommand, IIdempotent;
```

`IIdempotent` makes a specific claim. Not "this command is important", not "this usually
works". It is: **if the same command is handled twice, the state afterwards is the one a
single handling would have left.** In practice that needs the command to carry the identity
of what it creates — a reference the *caller* chose, a version it expects — so the handler
can recognise work it has already done.

A command whose handler generates that identity itself is **not** idempotent, however
carefully it is written. Two attempts produce two identities and two rows.

## Only failures another attempt could change

`ErrorKind.Unavailable` by default. `Validation` and `Forbidden` are never retried, because
the same input fails identically — retrying is pure latency. `Conflict` is sometimes worth
retrying and sometimes a loop; add it deliberately:

```csharp
services.AddZeroResilience(options => options.RetryOn.Add(ErrorKind.Conflict));
```

## Don't write the loop

```csharp
for (var attempt = 0; attempt < 3; attempt++)          // ZERO600
{
    var result = await rates.FetchAsync(pair, cancellationToken);

    if (result.IsSuccess) return result;

    await Task.Delay(200 * (attempt + 1), cancellationToken);
}
```

Three things are wrong and all of them are quiet: the delay has no jitter, so every caller
retries in step and arrives together; there is no brake, so a dependency that is fully down
gets three times the traffic it cannot serve; and the next handler will write it slightly
differently.

## Where it sits, and why that matters

`ResilienceOrder.Retry` is **outside** the transaction and **inside** the cache.

Outside the transaction because each attempt needs a fresh one — retrying inside reuses a
transaction that may already be doomed, and the second attempt fails for a reason that has
nothing to do with the first.

Inside the cache because a stored answer should short-circuit before anything is retried at
all.

## The brake

After `PauseRetriesAfterConsecutiveFailures` exhausted requests of one type, retries for
that type pause for `RetryPause`. A dependency that is down does not benefit from three
times the traffic, and the requests still fail — they just fail immediately, which is the
faster and cheaper way to fail.

## Testing

Register a controllable `TimeProvider` and state how long the waits were. A resilience test
that actually waits is a slow test that will eventually be a flaky one.
