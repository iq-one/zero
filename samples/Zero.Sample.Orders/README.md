# Zero.Sample.Orders

Every Zero package, composed into one application: a small ordering API over SQLite.

```bash
dotnet run --project samples/Zero.Sample.Orders
```

Authentication is by header, for the sample only, so you can exercise the authorization:

```bash
curl localhost:5000/api/products

curl -X POST localhost:5000/api/orders \
  -H 'X-Customer: alice' -H 'X-Permissions: orders:place,orders:pay' \
  -H 'Content-Type: application/json' \
  -d '{"reference":"REF-0001","items":[{"productCode":"DESK-01","quantity":2}]}'

curl localhost:5000/api/orders/REF-0001 \
  -H 'X-Customer: alice' -H 'X-Permissions: orders:pay'

curl -X POST localhost:5000/api/orders/REF-0001/pay \
  -H 'X-Customer: alice' -H 'X-Permissions: orders:pay'
```

## Where each capability earns its place

Nothing here is present to be demonstrated. A sample that bolts a capability on teaches
that the capability is decoration.

| | |
| --- | --- |
| **Results** | Every operation that can fail says so in its signature. `Error.Conflict` becomes 409 and `Error.NotFound` a 404 without a handler mentioning HTTP. |
| **Messaging** | Four use cases, each a request and one handler, reached only through `ISender`. |
| **Validation** | `PlaceOrderValidator` states what an order may contain. Every rule runs, so a caller correcting a form gets the whole list. |
| **Persistence** | Queries are named classes — `UnpaidOrdersDueBefore`, `AvailableProducts`. No handler mentions Entity Framework. |
| **…EntityFramework** | The provider. `Program.cs` is the only file that names SQLite. |
| **Events** | `OrderPlaced` fans out to the ledger and the mailer. The mailer refuses large orders on purpose, and the order still stands. |
| **Caching** | The catalogue is read-through cached for a minute, with the page in the key. |
| **Authorization** | Routes name a policy; `MustOwnOrder` decides whether *this* caller may see *this* order, which nothing outside the handler could answer. |
| **Observability** | Every request is logged, traced and timed. No handler mentions any of the three. |
| **Resilience** | `FlakyPricingService` fails the first call about any product. The order still succeeds, and nothing in the handler mentions retrying. |
| **BackgroundWork** | Expired orders are swept every minute — by *sending a command*, so the sweep has one implementation whether a clock or an operator triggered it. |
| **Configuration** | `OrderingOptions` is bound and validated at startup. |

## What to notice

**`Program.cs` is eleven `Add` calls and a `MapZeroEndpoints`.** Nothing registers a
handler, a validator, an endpoint, a subscriber or a convention. Read `generated/` for what
the compiler wrote instead.

**No handler mentions HTTP, EF, caching, retries or logging.** Those are the pipeline's.
`PlaceOrderHandler` reads as the use case and nothing else.

**The caller chooses the reference, and the command returns it.** That is what makes
`PlaceOrder` safe to retry — the handler recognises an order it has already placed — and it
is why nothing needs to read a database-generated id back. An identity column does not exist
until the insert happens, which is after the handler returns.

**The uniqueness check is a hint; the unique index is the rule.** Two requests can pass a
validator at the same moment. The validator exists to give a good message in the common
case, not to guarantee anything.

**One place says who may call.** A route attribute *is* an authorization declaration, so
`[Get("/orders/{reference}", Policy = ...)]` is read by both the HTTP endpoint and the
pipeline. Saying it twice would let the two disagree.

**`ICurrentUser` is registered by the application.** Which claim carries the identity is not
something a framework can decide. Leave it out and every caller is anonymous — and Zero says
so, once, at startup.
