# ZERO400 — A handler creates its own telemetry source

**Severity:** warning · **Category:** Zero.Observability

A handler constructs an `ActivitySource` or a `Meter` of its own.

```csharp
public sealed class CloseInvoiceHandler : ICommandHandler<CloseInvoice>
{
    private static readonly ActivitySource Source = new("Invoices");   // ZERO400
}
```

Nothing subscribes to it. A host subscribes to the names it was told about —
`ZeroTelemetry.ActivitySourceName` and `ZeroTelemetry.MeterName` — so a source invented in a
handler records into nowhere. The work is done and the data is lost, which is worse than not
collecting it: it looks instrumented.

The pipeline already opens an activity and records a duration for every request.

## Fix

Delete it. `AddZeroObservability()` traces and times the request, tags the activity with the
outcome and the error code, and does it the same way for every handler.

If you need a span *inside* the handler for a step worth seeing separately, start a child
activity from the ambient one rather than a new source:

```csharp
using var step = Activity.Current?.Source.StartActivity("invoice.recalculate");
```

That way it is subscribed to along with everything else.

## When a separate source is right

A long-running background component that is not a request — a poller, a consumer loop — is
outside the pipeline and may own its source. This rule only fires inside a request handler.
