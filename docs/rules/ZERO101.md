# ZERO101 — A result's value is read without checking the outcome

**Severity:** error · **Category:** Zero.Results

`Value` is read from a `Result<T>` that was never checked.

```csharp
var invoice = GetInvoice(id).Value;   // ZERO101 — throws when it failed
```

`Value` throws on a failed result. Reading it unchecked turns an expected failure back into
the exception the result type existed to avoid, at a line that no longer says which
operation failed.

## Fix

Match, so both branches must exist:

```csharp
return GetInvoice(id).Match(
    invoice => Ok(invoice),
    errors => Problem(errors));
```

Or check first:

```csharp
var result = GetInvoice(id);

if (result.IsFailure) return result.Errors;

Use(result.Value);
```

Or take the value only when there is one:

```csharp
if (GetInvoice(id).TryGetValue(out var invoice)) Use(invoice);
```

## What counts as a check

The rule looks for `IsSuccess`, `IsFailure`, `TryGetValue`, `Match` or a pattern match on
the same result anywhere in the enclosing body — not on every path to the read.

That is deliberate. Demanding a guard on every path reports code people write on purpose:
an early return, a switch, a check done in a helper. Asking whether the result was looked
at *at all* catches forgetting, which is the mistake this rule is about, and stays quiet
otherwise.

A result read straight out of a call, with no local to check, is always reported — there is
nowhere a check could have happened.
