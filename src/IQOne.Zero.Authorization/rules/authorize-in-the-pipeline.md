---
id: zero.authorization.authorize-in-the-pipeline
title: Declare authorization on the request, and let the pipeline enforce it
package: IQOne.Zero.Authorization
applies-to: ["**/*.cs"]
enforced-by: [ZERO450, ZERO451, ZERO452]
---

Every request says who may make it. The pipeline reads that before the handler runs, so a
caller who may not make a request never reaches the code that would answer it.

## Do

```csharp
[Authorize]                                   // anyone signed in
public sealed record WhoAmI : IQuery<Profile>;

[Authorize("invoices.close")]                 // a named policy
public sealed record CloseInvoice(int Id) : ICommand;

[Authorize(Roles = "admin, auditor")]         // either role is enough
public sealed record ReadLedger : IQuery<Ledger>;

[AllowAnonymous]                              // deliberately public
public sealed record GetPublicPriceList : IQuery<PriceList>;
```

Several `[Authorize]` attributes may sit on one request and all of them must pass. Roles
inside one attribute are alternatives. That is the only way to say "an admin, **and** in the
finance policy" without inventing an expression language.

## Every request declares something

A request with neither attribute is **refused**, and that is **ZERO450**.

Treating "no attribute" as "no restriction" makes forgetting indistinguishable from
deciding, and the two failures are not symmetrical: a public request accidentally locked
down is a bug report within the hour, while a private one accidentally opened is found by
somebody else. The default is the one whose failure mode is loud.

`AuthorizationOptions.Unannotated` can be set to `RequireAuthentication` — a reasonable
setting for an existing application partway through being annotated — or to `Allow`, for a
host where authorization genuinely lives somewhere else. Neither silences ZERO450: that
setting says what an undeclared request *does*, and the diagnostic says the decision should
be written down either way.

## Don't authorize inside a handler

```csharp
public async Task<Result<Unit>> HandleAsync(CloseInvoice command, CancellationToken ct)
{
    if (!user.IsInRole("finance")) return Error.Forbidden(...);   // belongs on the request
    ...
}
```

A handler that authorizes has to be trusted to do it — and the next handler, and the one
after that. It also runs after whatever the handler already loaded, which is work done for a
caller who was never allowed to ask.

## Rules are classes

A rule with any shape to it is a requirement plus a handler, not a role name:

```csharp
public sealed record WithinLimit(decimal Amount) : IAuthorizationRequirement;

public sealed class WithinLimitHandler(ILimitStore limits) : IRequirementHandler<WithinLimit>
{
    public async ValueTask<AuthorizationDecision> CheckAsync(
        WithinLimit requirement, ICurrentUser user, CancellationToken ct)
        => await limits.ForAsync(user.Id!, ct) >= requirement.Amount
            ? AuthorizationDecision.Allowed
            : AuthorizationDecision.Deny("invoice.over-limit", "This is above your approval limit.");
}
```

```csharp
services.AddZeroAuthorization(options =>
    options.AddPolicy("invoices.close", new RolesRequirement("finance"), new WithinLimit(10_000m)));
```

The handler is a class with a constructor, so it can be tested with a fake store and no
pipeline at all. That is the whole point of it not being an `if`.

Every requirement in a policy must hold. A policy that should admit alternatives is one
requirement whose handler knows about them — otherwise "and" and "or" would look identical
at the call site.

## Resource rules belong in the handler, and here is why

"May this caller close **this** invoice" needs the invoice, and the invoice is loaded inside
the handler. A pipeline behaviour that wanted to decide it would have to load the invoice
itself: a second read, guessed from the request's shape, wrong for every request whose
resource is not one row addressed by one id.

So the *question* is asked from the handler and the *rule* still is not:

```csharp
public sealed class CloseInvoiceHandler(IInvoiceRepository invoices, IResourceAuthorizer authorizer)
    : ICommandHandler<CloseInvoice>
{
    public async Task<Result<Unit>> HandleAsync(CloseInvoice command, CancellationToken ct)
    {
        var invoice = await invoices.FindAsync(command.Id, ct);

        if (invoice is null) return Error.NotFound("invoice.missing", "No such invoice.");

        var allowed = await authorizer.AuthorizeAsync(new MustBeOwner(), invoice, ct);

        if (allowed.IsFailure) return allowed.Errors;

        return invoice.Close();
    }
}
```

```csharp
public sealed class MustBeOwnerHandler : IRequirementHandler<MustBeOwner, Invoice>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        MustBeOwner requirement, Invoice resource, ICurrentUser user, CancellationToken ct)
        => new(resource.OwnerId == user.Id
            ? AuthorizationDecision.Allowed
            : AuthorizationDecision.Deny("invoice.not-owner", "Only the owner may close this."));
}
```

Two things follow, and both are on purpose. Keep the coarse check on the request — `CloseInvoice`
still carries `[Authorize("invoices.close")]`, so a caller with no business closing invoices at
all is turned away before anything is read. And ask the resource question immediately after the
load and before anything is changed.

Whether the caller learns "forbidden" or "not found" for an invoice that is not theirs is the
application's decision, not the framework's: returning `NotFound` hides the invoice's existence
and is right for a multi-tenant surface; returning `Forbidden` is clearer and right inside one
organisation.

## Unauthorized and forbidden are not interchangeable

`ErrorKind.Unauthorized` — 401 — means the caller could not be identified. Signing in might
change the answer.

`ErrorKind.Forbidden` — 403 — means they were identified and the answer is still no.

Returning the first where the second is true sends people round a login loop that cannot
succeed. Returning the second where the first is true hides a missing token behind what
reads as a permissions problem. The behaviour picks between them from
`ICurrentUser.IsAuthenticated`, so a requirement never has to; `AuthorizationDecision.Deny`
is always a 403, because a requirement only ever runs for a caller who is already known.

## Everything unclear is a refusal

A policy that was never declared, a requirement with no handler registered, a handler that
threw: all three are refusals, none of them is an exception, and none of them lets the
request through. There is no configuration that turns any of them into a pass.

A cancelled request is not a refusal, and is left to travel as `OperationCanceledException`.

## Telling the framework who the caller is

Register `ICurrentUser`. The identifier is a `string?` because it is a `Guid` in one
application and an `int` in the next; parse it where you know which.

```csharp
services.AddScoped<ICurrentUser>(sp => new ClaimsPrincipalCurrentUser(
    sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User ?? new ClaimsPrincipal()));
```

Register nothing and every caller is nobody, so protected requests are refused rather than
served to no one in particular. Set `options.RoleClaimType` to whatever your tokens actually
use — an OpenID Connect provider usually issues `roles`, not the WS-Federation URI that is
the default — or every role check quietly finds nothing.

## The route attribute is not this attribute

`[Get(..., Policy = "x")]` and `[Get(..., AllowAnonymous = true)]` configure the **ASP.NET
endpoint**. `[Authorize]` and `[AllowAnonymous]` configure the **pipeline**, in every host.
Put the authorization on the request with these attributes, and let the route attribute
describe the route.
