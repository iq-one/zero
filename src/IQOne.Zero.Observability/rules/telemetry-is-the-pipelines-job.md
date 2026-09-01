---
id: zero.observability.telemetry-in-the-pipeline
title: Do not log, trace or time inside a handler
package: IQOne.Zero.Observability
applies-to: ["**/*.cs"]
enforced-by: [ZERO400, ZERO401]
---

The pipeline logs, traces and times every request. A handler that does any of it repeats
work that is already done — and does it inconsistently, because the next handler will do it
slightly differently and the one after that will forget.

## Do

```csharp
services.AddZeroObservability();
```

That is the whole setup. Subscribe from the host:

```csharp
tracing.AddSource(ZeroTelemetry.ActivitySourceName);
metrics.AddMeter(ZeroTelemetry.MeterName);
```

## Don't

Do not create an `ActivitySource` or a `Meter` in a handler. Nothing subscribes to it, so
what it records is never collected. That is **ZERO400**.

Do not pass the whole request to the logger:

```csharp
logger.LogInformation("Closing {Command}", command);   // ZERO401
```

A command carries user data. Log the values the line actually needs, or turn on
`ObservabilityOptions.LogRequestContents` so it is one decision for the whole application
rather than a decision made again in every handler — and made differently.

## The level follows the outcome

A `Validation` or `NotFound` failure is a normal answer to a normal question, and logging it
as a warning trains everyone to ignore warnings. `Failure` and `Unavailable` say something
is wrong with the system, and those are warnings. The pipeline already knows which is which.

## Logging something the domain cares about

A handler may still log a domain event — "invoice 42 written off" — because that is a fact
about the business, not a fact about the request. The rule is about request telemetry:
timing, outcomes, and the request object itself.

## Correlation

`CorrelationId` flows with the request so that a log line, a trace span and a metric can be
lined up afterwards. Read it; do not invent a second one.
