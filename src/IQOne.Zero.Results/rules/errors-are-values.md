---
id: zero.results.errors-are-values
title: Return expected failures, throw only for the unexpected
package: IQOne.Zero.Results
applies-to: ["**/*.cs"]
enforced-by: [ZERO100, ZERO101, ZERO102]
---

An operation that can fail in a way the caller is expected to handle returns
`Result` or `Result<T>`. Exceptions stay for what nobody planned for: a bug, a broken
invariant, a machine in trouble.

The difference is not severity, it is *whose problem it is*. A missing customer is the
caller's problem and belongs in the signature. A corrupt index is nobody's problem to
handle here, and a stack trace is the most useful thing you can produce.

## Do

```csharp
public async Task<Result<Invoice>> GetAsync(int id, CancellationToken cancellationToken)
{
    var invoice = await store.FindAsync(id, cancellationToken);

    return invoice is null
        ? Error.NotFound("invoice.missing", $"No invoice with id {id}.")
        : invoice;                     // implicit conversion, both directions
}
```

Compose without unpacking. Each step runs only if the previous one succeeded:

```csharp
return await GetAsync(id, cancellationToken)
    .Ensure(i => i.IsOpen, Error.Conflict("invoice.closed", "This invoice is already closed."))
    .Bind(i => ApplyAsync(i, payment, cancellationToken))
    .Map(i => i.ToModel());
```

`Map`, `Bind`, `Ensure`, `Tap`, `TapError`, `MapError`, `WithError` and `GetValueOr` all exist
in the awaited form as well, so one asynchronous step does not break the chain apart. A
command that produces nothing composes the same way:

```csharp
return Authorize(user, invoice)
    .Bind(() => Close(invoice))
    .TapError(errors => logger.LogWarning("Could not close {Invoice}: {Errors}", invoice.Id, errors));
```

Read the outcome in a way that cannot skip the failure:

```csharp
return result.Match(
    invoice => Results.Ok(invoice),
    errors => Problem(errors));
```

When a failure has to keep travelling but the type around it changes, say so once rather
than restating its errors:

```csharp
public Result<InvoiceModel> Describe(Result<Invoice> invoice)
{
    if (invoice.IsFailure) return invoice.Cast<InvoiceModel>();   // same failure, new type

    return invoice.Value.ToModel();
}
```

`return result.Errors;` works too: a list of reasons converts to a failed result of any type.

## Don't

Do not discard a result. This is **ZERO100** and fails the build:

```csharp
ApplyPayment(invoice, payment);          // the failure disappears
_ = ApplyPayment(invoice, payment);      // and the discard does not make it intentional
```

Handling the failure — `TapError`, `Match`, `GetValueOr` — satisfies the rule even when
nothing is done with what comes back. Ignoring a failure on purpose is fine; ignoring one by
saying nothing is not.

Do not read `Value` without checking. This is **ZERO101**:

```csharp
var invoice = GetAsync(id).Result.Value;   // throws when it failed
```

Do not throw a failure you already promised to return. This is **ZERO102**:

```csharp
public Result<Invoice> Get(int id)
{
    if (id <= 0) throw new ArgumentException("The id must be positive.");

    return store.Find(id);
}
```

Return `Error.Validation("invoice.id", "The id must be positive.")` instead. The rule leaves
alone what should still be thrown: a rethrow, a throw while handling an exception, and the
exceptions that mean the code itself is wrong — `ArgumentNullException`,
`InvalidOperationException`, `NotImplementedException`, `NotSupportedException`,
`OperationCanceledException`, `UnreachableException`.

## A result you never assigned is a failure

`default(Result)` and `default(Result<T>)` are failures — the ones an unset field, a
`FirstOrDefault` over an empty sequence, or a `TryGetValue` that returned `false` hand you. A
struct that defaulted to success would turn a forgotten assignment into a silent pass.

Such a failure still states a reason, `Error.Uninitialised`, so it can be logged, mapped to a
status and propagated exactly like any other. A failure never carries an empty `Errors`; code
downstream may read `Errors[0]` without checking the count.

## Choosing an error

`Error.Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `Unavailable`,
and plain `Error.Failure` for anything else. The kind classifies the failure; it is not an
HTTP status. Mapping a kind to a status code, an exit code or a retry decision belongs at
the edge of the application, where the transport is known.

Give the code a stable, greppable identifier — `area.reason` — and put the human-readable
part in the message. Callers branch on the code; the message may change without notice.

Ask `Error.IsNone`, never `Kind`, to tell a reason from the absence of one: `Error.None` has
to have some kind, and the one it has is `Failure`.

`Error.With(metadata)` attaches structured data for a caller that knows what to do with it.
The dictionary is copied, and two errors with the same contents are equal, so an error stays
a value you can compare and assert on.
