# ZERO100 — A result is discarded

**Severity:** error · **Category:** Zero.Results

A call returns `Result` or `Result<T>` and nothing reads it.

```csharp
ApplyPayment(invoice, payment);       // ZERO100
_ = ApplyPayment(invoice, payment);   // ZERO100 — the discard does not make it intentional
```

The failure disappears. Nothing logs it, nothing retries it, and the code after this line
runs as though the operation had succeeded. That is worse than an unhandled exception,
which at least stops.

## Fix

Handle it, or pass it on:

```csharp
var result = ApplyPayment(invoice, payment);

if (result.IsFailure) return result;
```

```csharp
return ApplyPayment(invoice, payment);
```

```csharp
ApplyPayment(invoice, payment)
    .TapError(errors => logger.LogWarning("Payment refused: {Errors}", errors));
```

## If the failure genuinely does not matter

Say so in code, not by dropping the value:

```csharp
// Best-effort: a failed touch must not fail the request.
_ = TouchLastSeen(user).GetValueOr(false);
```

An expression that reads the outcome — even to ignore it — satisfies the rule, and leaves
the next reader in no doubt that the choice was deliberate.
