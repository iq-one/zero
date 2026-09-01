# ZERO451 — A request is both authorized and anonymous

**Severity:** error · **Category:** Zero.Authorization

One request carries `[Authorize]` and `[AllowAnonymous]` together.

```csharp
[Authorize("invoices.close")]
[AllowAnonymous]
public sealed record CloseInvoice(int Id) : ICommand;      // ZERO451 — reachable by anyone
```

`[AllowAnonymous]` wins, as it does in every framework that has both, so the `[Authorize]`
does nothing at all. The request is public while reading as though it were protected, and
nothing at run time reports the contradiction because each attribute is individually valid.

That is the definition of a silent mistake: the source says one thing, the behaviour says
another, and no test that does not already suspect it will notice.

## Fix

Remove whichever one is wrong. The compiler cannot tell which — that is why this is reported
rather than resolved.

```csharp
[Authorize("invoices.close")]
public sealed record CloseInvoice(int Id) : ICommand;
```

## Why not just make [Authorize] win

Because the precedence is not the problem. Whichever way it resolved, one of the two
attributes would be a lie about what the request does, and a reader would have to know the
rule to tell which. Refusing to guess is the only outcome that leaves the source honest.
