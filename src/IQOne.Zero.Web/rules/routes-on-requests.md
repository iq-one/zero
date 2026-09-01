---
id: zero.web.routes-on-requests
title: Declare the route on the request, and let the endpoint be generated
package: IQOne.Zero.Web
applies-to: ["**/*.cs"]
enforced-by: [ZERO300, ZERO301]
---

An HTTP endpoint is a request with a route attribute. There is no controller, no mapping
file and no `app.MapGet` to keep in step with it.

## Do

```csharp
[Get("/invoices/{id:int}", Tag = "Invoices")]
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>;

[Post("/invoices/{id:int}/close", Policy = "invoices:write")]
public sealed record CloseInvoice(int Id, string Reason) : ICommand;
```

The generator emits one real ASP.NET endpoint per request, so each gets its own
authorization policy, rate limit, cache entry, OpenAPI operation and telemetry name — and a
wrong verb answers 405 rather than 404.

## How the request is filled

One rule, the same for every verb: **the body is read first, then query and route values
are applied over it by name.** Narrowest wins, so a route value is never contradicted by
something in the body.

That means a positional record binds the same way a mutable class does, and an endpoint
reads identically whether its values arrive in the URL, the query string or the body.

## Don't

Do not write a controller or a minimal-API mapping for something Zero already routes:

```csharp
app.MapGet("/invoices/{id}", async (int id, ISender sender) => ...);   // already generated
```

Do not put a route on a type that is not a request. That is **ZERO300**, and the attribute
would otherwise do nothing at all.

Do not map results to status codes by hand:

```csharp
return result.IsFailure ? Results.NotFound() : Results.Ok(result.Value);
```

The mapping is `ZeroWebOptions.StatusCodeByKind`, applied everywhere at once. Doing it per
endpoint is how one of them ends up answering 200 with an error body.

## Handlers do not know about HTTP

A handler returns `Result<T>`. `Error.NotFound` becomes 404, `Error.Conflict` 409,
`Error.Validation` 400. Nothing in the handler names a status code, which is what lets the
same handler serve a queue, a job or a test without changing.

Change the mapping in one place when a published contract requires it:

```csharp
services.AddZeroWeb(options => options.StatusCodeByKind[ErrorKind.Conflict] = 422);
```
