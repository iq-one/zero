# Zero.Sample.Orders

Every Zero package, composed into one modular application: a small ordering API over SQLite.

```bash
dotnet run --project samples/Zero.Sample.Orders/Host
```

## Five projects, five modules

| | |
| --- | --- |
| `Host` | Turns the framework on, says how this deployment connects and who its callers are, names the modules. Nothing else. |
| `Data` | The context every module stores through, and the conventions applied to all of it. Names no entity. |
| `Catalog` | What can be ordered, and how it is read. Seeds the shelf in its own initialize phase. |
| `Ordering` | Placing, paying for and expiring orders. Owns its policies, its options and its scheduled sweep. |
| `Pricing` | What things cost, according to somewhere else. |

**Nothing states a startup order.** `Ordering` references `Catalog` and `Pricing`, so it is
configured after them — derived from the assembly reference graph, which cannot drift from
what the projects actually reference.

## Each module does its own work

`Host/Program.cs` has three jobs: turn on the capabilities, say how *this deployment*
connects, name the modules. No handler, validator, endpoint, subscriber, policy, job,
convention or service is registered there.

`Ordering/Module.cs` is the whole of that module's wiring:

```csharp
partial void OnConfigureServices(IModuleServiceContext context)
{
    context.Services.AddZeroOptions<OrderingOptions>();

    foreach (var permission in OrderPolicies.All)
        context.Authorization().AddPolicy(permission, new MustHavePermission(permission));

    context.Services.AddRecurringCommand<ExpireUnpaidOrders, int>(
        "expire-unpaid-orders",
        JobSchedule.Every(TimeSpan.FromMinutes(1)),
        run => new ExpireUnpaidOrders(run.ScheduledFor));
}
```

The policies live next to the routes they guard, not in a host with no reason to know what
`orders:place` protects.

`Pricing/Module.cs` is one line, and that is the point: `FlakyPricingService` implements
`IPricingService`, which carries `IScoped`, so the generator already knows both the service
type and the lifetime.

`Data` has no `Module.cs` at all and is still listed in `AddModules` — its generated module
is what registers the soft-delete filter and the audit stamps.

## The context names no entity

Each module contributes an `IModelConvention<ModelBuilder>` that maps its own. Adding a
module does not mean editing a shared context, which is what "modular" has to mean if it is
to mean anything.

## Trying it

Authentication is by header, for the sample only, so the authorization is something you can
exercise:

```bash
curl localhost:5000/api/products

curl -X POST localhost:5000/api/orders \
  -H 'X-Customer: alice' -H 'X-Permissions: orders:place,orders:pay' \
  -H 'Content-Type: application/json' \
  -d '{"reference":"REF-0001","items":[{"productCode":"DESK-01","quantity":2}]}'

curl localhost:5000/api/orders/REF-0001 -H 'X-Customer: alice' -H 'X-Permissions: orders:pay'
curl -X POST localhost:5000/api/orders/REF-0001/pay -H 'X-Customer: alice' -H 'X-Permissions: orders:pay'
```

## Where each capability earns its place

Nothing here is present to be demonstrated. A sample that bolts a capability on teaches that
the capability is decoration.

| | |
| --- | --- |
| **Results** | `Error.Conflict` becomes 409 and `Error.NotFound` a 404 without a handler mentioning HTTP. |
| **Messaging** | Four use cases, each a request and one handler, reached only through `ISender`. |
| **Validation** | `PlaceOrderValidator` states what an order may contain. Every rule runs. |
| **Persistence** | Queries are named classes. No handler mentions Entity Framework. |
| **…EntityFramework** | The provider. `Host` is the only project that names SQLite. |
| **Events** | `OrderPlaced` fans out to the ledger and the mailer. The mailer refuses large orders on purpose, and the order still stands. |
| **Caching** | The catalogue is read-through cached for a minute, with the page in the key. |
| **Authorization** | Routes name a policy; `MustOwnOrder` decides whether *this* caller may see *this* order. |
| **Observability** | Every request is logged, traced and timed. No handler mentions any of the three. |
| **Resilience** | `FlakyPricingService` fails the first call about any product. The order still succeeds. |
| **BackgroundWork** | Expired orders are swept every minute, by *sending a command*. |
| **Configuration** | `OrderingOptions` is bound and validated at startup, by the module that owns it. |

## What else to notice

**The caller chooses the reference, and the command returns it.** That is what makes
`PlaceOrder` safe to retry, and it is why nothing needs to read a database-generated id
back — an identity column does not exist until the insert happens, which is after the
handler returns.

**The uniqueness check is a hint; the unique index is the rule.** Two requests can pass a
validator at the same moment.

**One place says who may call.** A route attribute *is* an authorization declaration, so the
endpoint and the pipeline read the same thing. The endpoint requires authentication; the
named policy is evaluated by the pipeline, whose registry is the only place it exists.
