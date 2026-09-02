---
id: zero.persistence.entity-framework
title: Let the provider hold Entity Framework, and keep it out of everything above
package: IQOne.Zero.Persistence.EntityFramework
applies-to: ["**/*.cs"]
---

This package is the only place in a Zero application that knows Entity Framework exists.
Handlers take `IReadRepository<T>` or `IRepository<T>` and hand them specifications; the
evaluator here turns those into queries.

## Do

```csharp
public sealed class ShopContext(
    DbContextOptions<ShopContext> options,
    IEnumerable<IModelConvention<ModelBuilder>> model,
    IEnumerable<IEntityFilterConvention> filters)
    : ConventionDbContext(options, model, filters)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();
}
```

```csharp
services.AddZeroEntityFramework<ShopContext>(options => options.UseSqlServer(connectionString));
services.AddZeroTransactions();
```

That is the whole registration: the context, the unit of work, the open-generic
repositories, the specification evaluator and the interceptors.

## Don't

Do not reference `Microsoft.EntityFrameworkCore` from a handler, a module, or a contracts
project. The moment one does, changing provider stops being a registration change.

Do not call `SaveChanges` yourself. `TransactionBehavior` commits when a command succeeds
and rolls back when it fails; a handler that saves halfway has already committed part of
its work by the time it returns a failure.

## Filters read through the context, never through a captured value

A filter convention is handed the context instance for a reason:

```csharp
public LambdaExpression? Build(Type entityType, object context)
    => (Expression<Func<ITenantScoped, bool>>)(e => e.TenantId == ((ShopContext)context).TenantId);
```

Capturing the tenant into a local instead bakes it into the compiled query, and every later
request reuses the first request's value. There is a test for exactly that; if you write a
filter, write one too.

## Which engine

This package names none. Add the engine's own EF package — `Microsoft.EntityFrameworkCore.SqlServer`,
`.Npgsql`, `.Sqlite` — and select it in the options delegate. That is the only line that
changes when the engine does.

## Testing

Use Sqlite in memory, not the InMemory provider. Sqlite translates real SQL, so a query the
provider cannot translate fails in the test rather than in production.
