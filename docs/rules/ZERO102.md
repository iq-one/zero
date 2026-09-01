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
