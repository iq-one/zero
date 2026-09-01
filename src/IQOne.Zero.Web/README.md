# IQOne.Zero.Web

Turns commands and queries into HTTP endpoints.

```csharp
[Get("/invoices/{id:int}")]
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>;
```

```csharp
services.AddZeroWeb(options => options.RoutePrefix = "/api");
app.MapZeroEndpoints();
```

One real ASP.NET endpoint per request, generated at build time — so authorization, rate
limiting, caching, OpenAPI and telemetry attach per method, and a wrong verb answers 405.

The handler never mentions HTTP: a `NotFound` error becomes 404, a `Conflict` 409.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
