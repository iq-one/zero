# Zero.Sample.Invoices

A complete application on Zero, small enough to read in one sitting.

```bash
dotnet run --project samples/Zero.Sample.Invoices
```

| | |
| --- | --- |
| `GET /api/invoices` | every invoice |
| `GET /api/invoices/{id}` | one invoice, or 404 |
| `POST /api/invoices` | raise one, or 400 with every reason it was refused |
| `POST /api/invoices/{id}/pay` | settle one: 204, or 409 if it was already settled |

## What to notice

**Nothing registers a handler, a validator or an endpoint.** `Program.cs` is four `Add`
calls and a `MapZeroEndpoints`. Read `generated/` to see what the compiler wrote instead.

**No handler mentions HTTP.** They return `Result<T>`; `Error.NotFound` becomes a 404 and
`Error.Conflict` a 409 at the edge. The same handlers would serve a queue unchanged.

**Validation is not in the handlers.** `CreateInvoiceValidator` states the shape rules and
`UniqueReferenceValidator` asks the store — two validators for one request, both run, and
every failure comes back at once.

**The uniqueness check is a hint, not a guarantee.** Two requests can pass it at the same
moment, so `CreateInvoiceHandler` still checks. In a real application the database
constraint is what actually enforces it; the validator exists to give a good message in the
common case.

**Every route says `AllowAnonymous = true`, and the secure default stays on.** Zero refuses
an unauthenticated caller by default and warns (ZERO302) when a routed request declares
neither a policy nor anonymous access — an endpoint that should have required
`invoices:write` and instead accepts any authenticated caller looks correct and has no
symptom. There is an opt-out switch on `ZeroWebOptions`; this sample does not use it,
because answering the question on each endpoint is better than switching the question off.

**The store is in memory** so the sample runs with nothing installed. A real application
would take `IRepository<Invoice>` from `IQOne.Zero.Persistence`; the handlers would not
change.
