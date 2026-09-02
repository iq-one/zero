# IQOne.Zero

The set every application needs, in one reference.

```bash
dotnet add package IQOne.Zero
```

Brings `IQOne.Zero.Abstractions`, `.Core`, `.Configuration`, `.Messaging`, `.Results`,
`.Validation` and the `.Generators` that write your registrations at build time.

```csharp
builder.Services.AddZeroMessaging();
builder.Services.AddZeroValidation();
builder.Services.AddModules(new Module());
```

Everything else is opt-in, because a console worker should not drag ASP.NET in and a service
that stores nothing should not carry a data layer:

| | |
| --- | --- |
| `IQOne.Zero.Web` | HTTP endpoints from your requests |
| `IQOne.Zero.Persistence` + `.EntityFramework` | specifications, repositories, transactions |
| `IQOne.Zero.Events` | domain events |
| `IQOne.Zero.Authorization` | who may make a request |
| `IQOne.Zero.Caching` | read-through caching |
| `IQOne.Zero.Observability` | logging, tracing, metrics |
| `IQOne.Zero.BackgroundWork` | recurring work |
| `IQOne.Zero.Resilience` | retrying what is worth retrying |
| `IQOne.Zero.Testing` | testing an application built on it |

Install `IQOne.Zero.Tool` and run `zero rules init` to write what a coding agent needs to
know about all of it into your repository.

**The API will change before 1.0.** Pin the version.

[Zero](https://iqone.solutions/zero) is built and maintained by IQOne.
