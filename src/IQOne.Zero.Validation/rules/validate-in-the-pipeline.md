---
id: zero.validation.validate-in-the-pipeline
title: Validate in a validator, never in a handler
package: IQOne.Zero.Validation
applies-to: ["**/*.cs"]
---

A request's rules live in a validator. The pipeline runs it before the handler, so an
unacceptable request never reaches the handler at all.

Validating inside a handler means trusting each handler to do it — and the next one, and
the one after that. A validator cannot be skipped, and it cannot be half-applied.

## Do

```csharp
public sealed class CreateInvoiceValidator : Validator<CreateInvoice>
{
    protected override void Configure(RuleSet<CreateInvoice> rules)
    {
        rules.NotEmpty(x => x.Reference, "invoice.reference");
        rules.InRange(x => x.Amount, "invoice.amount", 0.01m, 1_000_000m);
        rules.Must(x => x.Due > x.Issued, "invoice.due", "The due date must be after the issue date.");
    }
}
```

There is nothing to register. The generator finds it; `AddZeroValidation()` runs it.

## Don't

```csharp
public async Task<Result<int>> HandleAsync(CreateInvoice command, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(command.Reference))       // belongs in a validator
        return Error.Validation("invoice.reference", "Required.");
    ...
}
```

## Error codes are written out, not derived

```csharp
rules.NotEmpty(x => x.Reference, "invoice.reference");
```

The code is part of the published contract: callers branch on it and translators key off it.
Deriving it from the property name would mean a rename silently changes what clients see.
The message may change freely; the code may not.

## Rules that need a dependency

Use `MustAsync` for a check that has to reach out — a reference that must be unique, a code
that must exist:

```csharp
rules.MustAsync((x, ct) => store.IsReferenceFreeAsync(x.Reference, ct),
                "invoice.reference.taken", "That reference is already in use.");
```

Keep these few. Each is a round trip taken before the handler runs, and a uniqueness check
here is a hint rather than a guarantee — the database constraint is what actually enforces
it, and the handler still has to handle the conflict.

## Every failure at once

All rules run, and all validators for the request run. A caller correcting a form gets the
whole list rather than discovering the second mistake on the next attempt.

## Where it sits

After authorization — there is no point explaining what is wrong with a request the caller
may not make — and before caching and transactions, which an unacceptable request should
never reach.
