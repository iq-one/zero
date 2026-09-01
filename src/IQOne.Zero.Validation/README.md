# IQOne.Zero.Validation

Request validation as a pipeline behaviour.

```csharp
public sealed class CreateInvoiceValidator : Validator<CreateInvoice>
{
    protected override void Configure(RuleSet<CreateInvoice> rules)
    {
        rules.NotEmpty(x => x.Reference, "invoice.reference");
        rules.InRange(x => x.Amount, "invoice.amount", 0.01m, 1_000_000m);
    }
}
```

```csharp
services.AddZeroValidation();
```

Validators are found at build time — there is nothing to register — and run before the
handler, so an unacceptable request never reaches it. Every failure is reported at once.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
