# ZERO302 — A routed request does not say who may make it

**Severity:** warning · **Category:** Zero.Web

A request declares a route but neither a `Policy` nor `AllowAnonymous`.

```csharp
[Post("/orders/{id:int}/cancel")]                 // ZERO302
public sealed record CancelOrder(int Id) : ICommand;
```

Zero refuses an unauthenticated caller by default, so an endpoint someone forgot to protect
fails loudly rather than opening. That is the dangerous silence, and it is already gone.

What remains silent is this one: an endpoint that should have required `orders:write` and
instead accepts **any authenticated caller**. It looks right, it answers 200, and nothing
about its behaviour says the wrong people can reach it.

## Fix

Name what it needs:

```csharp
[Post("/orders/{id:int}/cancel", Policy = "orders:write")]
public sealed record CancelOrder(int Id) : ICommand;
```

Or say that any authenticated caller really is the rule:

```csharp
[Get("/orders/mine", Policy = ZeroPolicies.Authenticated)]
public sealed record MyOrders : IQuery<OrderModel[]>;
```

Or that it is public:

```csharp
[Get("/health", AllowAnonymous = true)]
public sealed record Health : IQuery<string>;
```

## Why a warning

Requiring authentication and nothing more is a legitimate answer for a great many endpoints,
and an error would force a ceremonial attribute onto every one of them. The rule exists so
the answer is written down rather than arrived at by omission — which is the same reason
`ZeroWebOptions.RequireAuthorizationByDefault` defaults to requiring.

To silence it for a project that genuinely treats "authenticated" as the default, set the
severity to `none` in `.editorconfig` and say so where the next reader will find it.
