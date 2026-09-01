# IQOne.Zero.Testing

Everything a test needs to exercise an application built on Zero. No test runner is
referenced, so it works with xunit, NUnit, MSTest or anything else.

```csharp
await using var app = await ZeroTestApplication.Create()
    .AddServices(services => services.AddScoped<IInvoiceStore, FakeInvoiceStore>())
    .AddConfiguration(("Mail:Host", "smtp.example.com"))
    .AddHandler<CreateInvoice, int, CreateInvoiceHandler>()
    .AddValidator(new CreateInvoiceValidator())
    .AddModule(new MyApp.Module())
    .BuildAsync();

var result = await app.SendAsync(new CreateInvoice("", 0m));

result.ShouldFailWithCodes("invoice.reference", "invoice.amount");
```

The container is built with `ValidateScopes` and `ValidateOnBuild` on, and each send runs in
its own scope — so a captive dependency, a missing registration or a request with no handler
fails in the test rather than at startup.

## One handler, with or without the pipeline

```csharp
var result = await HandlerHarness
    .For<CreateInvoice, int>(new CreateInvoiceHandler(store))
    .WithBehavior(new MyAuditBehavior<CreateInvoice, int>())
    .SendAsync(new CreateInvoice("INV-001", 250m));   // HandleAsync skips the behaviours
```

## Testing what sends, rather than what handles

```csharp
var sender = new FakeSender()
    .Returns<CreateInvoice, int>(42)
    .Succeeds<CloseInvoice>();

await new InvoiceService(sender).ImportAsync(file);

sender.ShouldHaveSent<CloseInvoice>(request => request.Id == 42);
```

## Assertions that say what happened

`ShouldSucceed`, `ShouldFail`, `ShouldFailWith(code)`, `ShouldFailWith(kind)`,
`ShouldFailWithCodes`, `ShouldHaveValue` — each prints every error, with its code, kind and
message, when it does not hold. `ShouldSucceed()` on a `Result<T>` hands back the value.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
