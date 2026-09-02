# ZERO302 — retired

**This id is retired and is never reported.** It is kept, with its number, so that a
suppression written against it stays readable and the number is never reused.

It reported a routed request that declared neither a policy nor anonymous access. That is
now **[ZERO450](ZERO450.md)**, for a reason worth stating: a route attribute is an
`IAuthorizationDeclaration`, so the authorization analyzer sees the declaration directly —
and it applies in every host, not only an HTTP one. A worker that handles a request nobody
wrote permissions for has the same problem and used to get no diagnostic at all.

Two diagnostics for one mistake is noise. The one that belongs to the package owning the
concept is the one that stayed.
