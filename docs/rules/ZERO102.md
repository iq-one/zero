# ZERO102 — An expected failure is thrown instead of returned

**Severity:** warning · **Category:** Zero.Results

A method that returns `Result` throws for a failure it clearly expects.

```csharp
public Result<Invoice> Get(int id)
{
    if (id <= 0) throw new ArgumentException("id must be positive");   // ZERO102

    ...
}
```

The signature promised that failures are values. Throwing one anyway means every caller has
to handle failure twice — once by checking the result, once by catching — and in practice
they will do one of the two.

## Fix

```csharp
public Result<Invoice> Get(int id)
{
    if (id <= 0) return Error.Validation("invoice.id", "The id must be positive.");

    ...
}
```

## When throwing is right

Keep throwing for what the caller cannot handle and did not ask about: a broken invariant, a
null that should have been impossible, a failure that means the process is no longer sound.
Those are not outcomes of the operation, they are evidence of a defect, and a stack trace is
the most useful thing you can produce.

This rule is a warning rather than an error for that reason: the line between the two is a
judgement, and sometimes the judgement is that this one really should throw.

## What the rule leaves alone

The boundary is drawn so that the cases above never have to be suppressed. Nothing is
reported for:

- **A rethrow.** `throw;` re-raises what was caught; nothing new becomes an exception here.
- **A throw while handling one.** Anything thrown inside a `catch` clause is translating a
  failure that already arrived as an exception. Whether *that* should have been a result is a
  question about the code that threw it.
- **The exceptions that mean the code is wrong**, and any type derived from them:
  `ArgumentNullException`, `InvalidOperationException` (so also `ObjectDisposedException`),
  `NotImplementedException`, `NotSupportedException`, `OperationCanceledException` (so also
  `TaskCanceledException`), and `UnreachableException`.
- **Guard helpers**, such as `ArgumentNullException.ThrowIfNull(x)` and
  `cancellationToken.ThrowIfCancellationRequested()`. They are calls, not throws.
- **A lambda or local function with its own signature.** A `Func<Invoice, int>` written
  inside a result-returning method never promised anything about failures; the rule looks at
  the signature the throw actually sits in.

`InvalidOperationException` is on that list deliberately. It is .NET's conventional "this
should not have happened" exception, and no analyzer can tell a genuine invariant from a
conflict that should have been `Error.Conflict`. Reporting all of them would make the rule
noise, and a noisy rule gets suppressed wholesale — which costs more than the cases it would
have caught.

What is left is the case the rule is for: a method that returns a result choosing to raise a
failure it could have returned — `ArgumentException`, `ArgumentOutOfRangeException`,
`KeyNotFoundException`, a domain exception of your own.

The promise is the same when it is awaited: `Task<Result<T>>` and `ValueTask<Result<T>>` are
read through to the result inside them.
