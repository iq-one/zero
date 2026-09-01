---
id: zero.testing.no-hand-built-containers
title: Test through the harness, never a hand-built container
package: IQOne.Zero.Testing
applies-to: ["**/*Tests/**/*.cs", "**/*Test/**/*.cs", "**/*Tests.cs"]
enforced-by: []
---

A test of a Zero application builds nothing by hand. There is a harness for one handler, a
test application for the whole wiring, a fake `ISender` for the code that sends, and
assertions on `Result` that print the errors when they do not hold.

Hand-building a registry, an entry, a pipeline lambda and a service provider is thirty lines
that go stale the moment the framework changes — and they are usually built with the
validations off, so the test passes on wiring the application would refuse to start with.

## Assert on a Result

```csharp
var id = result.ShouldSucceed();                              // hands back the value
result.ShouldFailWith("invoice.missing");                     // by code
result.ShouldFailWith(ErrorKind.NotFound);                    // by kind
result.ShouldFailWithCodes("invoice.reference", "invoice.amount");
result.ShouldHaveValue(invoice => invoice.Total > 0);
```

```csharp
// Don't: the failure says "expected True, found False" and the errors are lost.
result.IsSuccess.Should().BeTrue();

// Don't: this throws InvalidOperationException from Result<T>.Value, hiding the real error.
result.Value.Should().Be(expected);
```

`ShouldSucceed()` on a failed result prints every error with its code, kind and message. It
throws `ZeroAssertionException`, which every runner reports as a failed test.

## One handler

```csharp
var result = await HandlerHarness
    .For<CreateInvoice, int>(new CreateInvoiceHandler(fakeStore))
    .WithValidator(new CreateInvoiceValidator())
    .WithBehavior(new AuditBehavior<CreateInvoice, int>(log))
    .SendAsync(new CreateInvoice("INV-001", 250m));
```

`SendAsync` runs the behaviours in `Order` and then the handler — the same pipeline the
generated dispatch table runs. `HandleAsync` calls the handler alone, for a test that is only
about the handler's own logic.

Nothing is validated unless a validator is added, so a rejected request in a harness test is
one the test set up on purpose.

## The whole application

```csharp
await using var app = await ZeroTestApplication.Create()
    .AddServices(services => services.AddScoped<IInvoiceStore, FakeInvoiceStore>())
    .AddConfiguration(("Mail:Host", "smtp.example.com"))      // binds like appsettings.json
    .AddHandler<CreateInvoice, int, CreateInvoiceHandler>()   // the container builds it
    .AddValidator<CreateInvoice, UniqueReferenceValidator>()
    .AddBehavior(typeof(AuditBehavior<,>))
    .AddModule(new MyApp.Module())                            // the generated one
    .BuildAsync();

var result = await app.SendAsync(new CreateInvoice("INV-001", 250m));
```

`AddModule` takes the `Module` class the generator wrote for an assembly, which is the closest
a test gets to the running application: every service, handler and request the assembly
declares, wired the way startup wires them. A request the module declares but nobody handles
fails `BuildAsync`, naming it — exactly as it would fail startup.

Validation is in the pipeline whether or not the test adds a validator, because it is in the
application's pipeline.

### Scopes are real here

`ValidateScopes` is on, so `app.Services.GetRequiredService<ISender>()` throws: `ISender`,
handlers, behaviours and validators are all scoped. Use `app.SendAsync(...)`, which opens a
scope per send the way a request does, `app.InScope<TService, TResult>(...)` for a single
assertion, or `app.CreateScope()` when the test needs to hold one open.

## Code that sends

```csharp
var sender = new FakeSender()
    .Returns<GetInvoice, InvoiceModel>(new InvoiceModel(1, 250m))
    .Fails<CloseInvoice>(Error.Conflict("invoice.closed", "Already closed."));

await new NightlyRun(sender).ExecuteAsync(CancellationToken.None);

sender.ShouldHaveSent<GetInvoice>(request => request.Id == 1);
sender.ShouldNotHaveSent<ArchiveInvoice>();
```

Script an outcome per request type. A request nobody scripted throws and names what to call:
a fake that returned a silent default would let the test pass while the code did the wrong
thing.

## Doubles that already exist

- `StubHandler<TRequest, TResponse>` — returns a scripted outcome, and answers
  `ShouldHaveRun()` / `ShouldNotHaveRun()`. Use it instead of a one-off handler with a static
  `Ran` flag, which leaks into the next test.
- `RecordingBehavior<TRequest, TResponse>` — writes `name:in` and `name:out` into a shared
  list, so pipeline ordering is an assertion on a list of strings.
- `ShortCircuitBehavior<TRequest, TResponse>` — fails without calling the rest of the
  pipeline, standing in for whatever would reject the request in production.

## Validators

```csharp
await validator.ShouldAcceptAsync(new CreateInvoice("INV-001", 250m));
await validator.ShouldRejectAsync(new CreateInvoice("", 250m), "invoice.reference");
```

Test the rules directly, one test per rule. Whether the pipeline applies the validator at all
is one test, through `ZeroTestApplication`, not one per rule.

## Don't

```csharp
// Don't rebuild the framework's plumbing in a test.
var registry = new RequestRegistry();
registry.Add(new RequestEntry(typeof(CreateInvoice), typeof(int), typeof(CreateInvoiceHandler),
    static (sp, r, ct) => RequestPipeline.RunAsync<CreateInvoice, int>((CreateInvoice)r, sp, ct)));
registry.Freeze();
services.AddSingleton(registry);
var provider = services.BuildServiceProvider();          // validations off: startup is stricter
var sender = provider.GetRequiredService<ISender>();     // resolved from the root, not a scope
```

```csharp
// Don't call a handler directly to avoid setting up the pipeline. The pipeline is where
// validation and authorization live, and a test that skips them proves nothing about the
// path a caller takes. Use HandlerHarness.SendAsync, or HandleAsync when the test really is
// about the handler alone.
var result = await new CreateInvoiceHandler(store).HandleAsync(command, default);
```
