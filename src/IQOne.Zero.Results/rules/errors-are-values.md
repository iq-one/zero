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

Read the outcome in a way that cannot skip the failure:

```csharp
return result.Match(
    invoice => Results.Ok(invoice),
    errors => Problem(errors));
```

## Don't

Do not discard a result. This is **ZERO100** and fails the build:

```csharp
_ = ApplyPayment(invoice, payment);      // the failure disappears
ApplyPayment(invoice, payment);          // so does this one
```

Do not read `Value` without checking. This is **ZERO101**:

```csharp
var invoice = GetAsync(id).Result.Value;   // throws when it failed
```

Do not throw a failure you already promised to return. This is **ZERO102**:

```csharp
public Result<Invoice> Get(int id)
{
    if (id <= 0) throw new ArgumentException(...);   // return Error.Validation instead
}
```

## Choosing an error

`Error.Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `Unavailable`,
and plain `Error.Failure` for anything else. The kind classifies the failure; it is not an
HTTP status. Mapping a kind to a status code, an exit code or a retry decision belongs at
the edge of the application, where the transport is known.

Give the code a stable, greppable identifier — `area.reason` — and put the human-readable
part in the message. Callers branch on the code; the message may change without notice.
