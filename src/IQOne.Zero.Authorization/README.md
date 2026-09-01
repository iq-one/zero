# IQOne.Zero.Authorization

Authorization as a pipeline behaviour. A request the caller may not make is refused before
anything reads data.

```csharp
[Authorize("invoices.close")]
public sealed record CloseInvoice(int Id) : ICommand;

[AllowAnonymous]
public sealed record GetPublicPriceList : IQuery<PriceList>;
```

```csharp
services.AddZeroAuthorization(options =>
    options.AddPolicy("invoices.close", new RolesRequirement("finance"), new WithinLimit(10_000m)));
```

The rule is a class, not an `if` in a handler:

```csharp
public sealed class WithinLimitHandler : IRequirementHandler<WithinLimit>
{
    public ValueTask<AuthorizationDecision> CheckAsync(
        WithinLimit requirement, ICurrentUser user, CancellationToken ct)
        => new(decimal.Parse(user.FindFirst("limit") ?? "0") >= requirement.Amount
            ? AuthorizationDecision.Allowed
            : AuthorizationDecision.Deny("invoice.over-limit", "This is above your approval limit."));
}
```

**A request that declares neither attribute is refused.** Forgetting to write one is
indistinguishable from deciding a request is public, and only one of those is ever
intended. ZERO450 reports the omission at compile time, so the refusal is a build error
rather than a surprise. An application can choose otherwise with
`options.Unannotated`.

Two refusals, and the difference matters: `Unauthorized` means we do not know who you are
and signing in may help; `Forbidden` means we do and it will not.

Nothing here references ASP.NET, so the same rules run in a worker, a console host and a
test. Tell the framework who the caller is by registering `ICurrentUser`; there is a
`ClaimsPrincipal` adapter for hosts that have one.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
