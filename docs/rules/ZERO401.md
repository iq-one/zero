# ZERO401 — A handler writes the request itself to the log

**Severity:** warning · **Category:** Zero.Observability

The whole request object is passed to a logger.

```csharp
logger.LogInformation("Closing {Command}", command);   // ZERO401
```

A command carries whatever the caller sent: a name, an address, a reference, sometimes a
secret. Logging the object logs all of it, to wherever the logs go, for as long as they are
kept — and nobody revisits that decision when a field is added to the record later.

## Fix

Log the values the line actually needs:

```csharp
logger.LogInformation("Closing invoice {InvoiceId}", command.Id);
```

Or make it one decision for the whole application rather than one per handler:

```csharp
services.AddZeroObservability(options => options.LogRequestContents = true);
```

That switch exists because in some applications — an internal tool, a system with no
personal data — logging the request is exactly what you want, and doing it in the pipeline
means it is consistent, and it is written down in one place where a reviewer can find it.

## Why a warning and not an error

Whether a particular request is safe to log is a judgement about the data, and the compiler
cannot make it. The rule exists to make the judgement conscious.
