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
[Get("/invoices/{id:int}", Tag = "Invoices", Policy = "invoices:read")]
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

Every value in a URL is text, so the rules for reading one are worth knowing:

| In the URL | In the request | Result |
| --- | --- | --- |
| `?includePaid=true` | `bool IncludePaid` | `true`; `True`, `1` and `0` are read too |
| `?kind=Draft` | `InvoiceKind Kind` | `InvoiceKind.Draft`, by name or by number |
| `?id=1&id=2` | `int Id` | `2` — a repeated key gives the last value |
| `?tags=a&tags=b` | `string[] Tags` | both, because the member holds many |
| `?tags=a` | `string[] Tags` | one element, for the same reason |
| `?q=123` | `string Q` | `"123"` — the member's type decides, never the text |

A route value is formatted under the invariant culture, so a request does not answer
differently on a server running under `tr-TR`.

## The body has to say it is JSON

A body is read only when its `Content-Type` is `application/json` or a `+json` suffix. Any
other media type — and a body with none at all — answers **415**.

This is not a formality. `text/plain`, `multipart/form-data` and
`application/x-www-form-urlencoded` are the three types a cross-origin HTML form can post
with the caller's cookies and without a preflight, and a form can be shaped so that its
`text/plain` body is valid JSON. Refusing them is what keeps another site from driving a
state-changing endpoint.

A body larger than `ZeroWebOptions.MaxBodyBytes` answers **413**. The default is one
mebibyte, well below the server's own ceiling, because the binder holds the body in memory
to overlay values onto it:

```csharp
services.AddZeroWeb(options => options.MaxBodyBytes = 4 * 1024 * 1024);
```

## Endpoints are closed unless they say otherwise

A request with neither `Policy` nor `AllowAnonymous` requires an authenticated caller. Say
so when an endpoint really is public:

```csharp
[Get("/health", AllowAnonymous = true)]
public sealed record GetHealth : IQuery<HealthModel>;
```

That default is part of the minimal working application: authorization has to be registered,
and it has to be in the pipeline, next to whatever authenticates the caller.

```csharp
services.AddZeroWeb();
services.AddZeroMessaging();
services.AddAuthorization();
```

```csharp illustrative
app.UseAuthentication();     // whatever scheme the application authenticates with
app.UseAuthorization();
app.MapZeroEndpoints();
```

`MapZeroEndpoints` refuses to map at all when an endpoint needs authorization and the
application has registered none, rather than letting every route answer 500 on its first
request. It can only see the services, not the middleware — on `WebApplication` the two come
together, because that host adds `UseAuthorization` for itself once the services are there;
on a host that composes its own pipeline, both halves are yours.

An application with a policy every endpoint should start from names it, and one with no
authentication at all opts out of the default deliberately:

```csharp
services.AddZeroWeb(options => options.DefaultPolicy = "authenticated-employee");
```

```csharp
services.AddZeroWeb(options => options.RequireAuthorizationByDefault = false);
```

## Don't

Do not write a controller or a minimal-API mapping for something Zero already routes:

```csharp illustrative
app.MapGet("/invoices/{id}", async (int id, ISender sender) => ...);   // already generated
```

Do not put a route on a type that is not a request. That is **ZERO300**, and the attribute
would otherwise do nothing at all.

Do not give two requests routes that match the same calls. `/invoices/{id}` and
`/invoices/{invoiceId}` are one route under two names; the endpoint table refuses the
second at startup rather than letting every call to `/invoices/5` fail as ambiguous.

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

## The envelope is yours

Zero answers with JSON on success and an RFC 7807 problem on failure, serialized with the
application's own `ConfigureHttpJsonOptions` settings. That shape is a default, not a
contract: an API that has published something else implements `IResponseWriter` and keeps
answering what its callers already parse.

```csharp
using Microsoft.AspNetCore.Http;

public sealed class HouseStyle : IResponseWriter
{
    public IResult Success<TResponse>(HttpContext context, TResponse value)
        => Results.Json(new { ok = true, data = value });

    public IResult Empty(HttpContext context) => Results.StatusCode(StatusCodes.Status200OK);

    public IResult Failure(HttpContext context, IReadOnlyList<Error> errors, int? status)
        => Results.Json(
            new { ok = false, reasons = errors.Select(e => e.Code).ToArray() },
            statusCode: status ?? StatusCodes.Status422UnprocessableEntity);
}
```

```csharp
// Registered first: AddZeroWeb only fills in what nothing else has claimed.
services.AddSingleton<IResponseWriter, HouseStyle>();
services.AddZeroWeb();
```

`status` is supplied only where HTTP itself decides it — 415 for a body the binder cannot
read, 413 for one too large — and is null everywhere else, which is where the writer
chooses. `IRequestBinder` is the same seam on the way in, for an API that speaks something
other than JSON.
