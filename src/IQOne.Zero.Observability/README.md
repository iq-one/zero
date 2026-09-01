# IQOne.Zero.Observability

Logging, tracing and metrics for every request, without a handler mentioning any of them.

```csharp
services.AddZeroObservability();
```

```csharp
tracing.AddSource(ZeroTelemetry.ActivitySourceName);
metrics.AddMeter(ZeroTelemetry.MeterName);
```

The level follows the outcome: a validation failure is a normal answer, not a warning about
the system. A request's contents stay out of the log unless the application turns them on —
a command carries user data, and that should be one decision, not one per handler.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
