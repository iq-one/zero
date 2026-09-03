# ZERO303 — A route attribute names no method

**Severity:** error · **Category:** Zero.Web

An attribute derives from `RouteAttribute` but not from any of `Get`, `Post`, `Put`,
`Patch` or `Delete`, so there is no HTTP method to read.

```csharp
public sealed class ServiceRouteAttribute(string pattern)
    : RouteAttribute("POST", pattern);          // the method is in the constructor body

[ServiceRoute("/shared/lookups/countries")]     // ZERO303
public sealed record GetCountries : IQuery<CountryModel[]>;
```

The generator reads attribute *arguments*, not the constructor body that forwards them, so
`"POST"` above is invisible to it. Left alone, the endpoint is simply never mapped: the
build is green and the URL 404s.

## Fix

Derive from whichever of the five carries the method you want, and pass the pattern
straight through — that argument is the only place the generator can read it:

```csharp
public sealed class ServiceRouteAttribute : PostAttribute
{
    public ServiceRouteAttribute(string pattern) : base(pattern)
        => Policy = pattern.TrimStart('/');
}

[ServiceRoute("/shared/lookups/countries")]
public sealed record GetCountries : IQuery<CountryModel[]>;
```

## Deriving is the point

An application whose routes share a shape — a prefix, a policy keyed by path, a tag per
area — should say it once. Recognition walks the base chain, so the method comes from
whichever of the five is in it.

Note what the generator can and cannot see. The **pattern** it reads at compile time, from
the first positional argument; that is why a derived attribute has to forward it rather than
compute it. Anything the attribute sets on **itself** — `Policy`, `Roles`, `Tag`,
`AllowAnonymous` — is read from the live attribute instance at runtime, so a constructor may
compute it freely. The example above is exactly that: one string, written once, with the
policy derived from it.
