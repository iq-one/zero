# ZERO450 — A request declares no authorization

**Severity:** error · **Category:** Zero.Authorization

A command or query carries neither `[Authorize]` nor `[AllowAnonymous]`.

```csharp
public sealed record CloseInvoice(int Id) : ICommand;      // ZERO450 — who may do this?
```

The pipeline refuses a request whose permissions nobody wrote down. That is the safe answer,
but a poor place to learn it: the refusal names a request rather than a file, and it arrives
from a caller instead of from a build.

The rule exists because forgetting the attribute and deciding a request is public produce
identical source. Only one of the two is ever meant, and the two failures are not
symmetrical — a public request accidentally locked down is a bug report within the hour, and
a private one accidentally opened is found by somebody else.

## Fix

Say which it is:

```csharp
[Authorize("invoices.close")]
public sealed record CloseInvoice(int Id) : ICommand;
```

```csharp
[Authorize]                                   // anyone signed in, and nothing more
public sealed record WhoAmI : IQuery<Profile>;
```

```csharp
[AllowAnonymous]                              // deliberately public
public sealed record GetPublicPriceList : IQuery<PriceList>;
```

## What the runtime does instead

`AuthorizationOptions.Unannotated` decides what an undeclared request does at run time:
`Deny` (the default), `RequireAuthentication`, or `Allow`.

Changing it does not silence this diagnostic, on purpose. The setting says what an
undeclared request *does*; the diagnostic says the decision should be written on the
request either way, where the next reader can see it.

## Turning it off

An application partway through adopting the package can lower or disable the rule in
`.editorconfig` while it annotates:

```ini
dotnet_diagnostic.ZERO450.severity = warning
```

Pair that with `options.Unannotated = MissingAuthorization.RequireAuthentication`, which
closes the public hole immediately and leaves the per-request rules to follow.

## Scope

Reported on concrete classes and structs implementing `IRequest<T>` — which includes
`ICommand`, `ICommand<T>` and `IQuery<T>`. Abstract bases are not reported: the concrete
request is where the decision applies, and attributes are not inherited.

## An attribute that derives the answer

An attribute of your own may compute the policy instead of taking it as an argument — a
route whose permission is its path, for instance. The rule reads arguments, so it cannot
see a computed value; the attribute says so once, on itself:

```csharp
[DeclaresAuthorization]
public sealed class ServiceRouteAttribute : PostAttribute
{
    public ServiceRouteAttribute(string pattern) : base(pattern)
        => Policy = pattern.TrimStart('/');
}

[ServiceRoute("/shared/lookups/countries")]      // no ZERO450
public sealed record GetCountries : IQuery<CountryModel[]>;
```

The marker goes on the attribute type rather than being inferred from its constructor:
inference would work in the assembly that declares it and fail for one referenced as
metadata, so the rule would depend on where the attribute lives. It also suppresses nothing
else — the attribute still has to supply a policy at runtime, and one that carries the
marker while deciding nothing leaves its requests requiring only an authenticated caller.
