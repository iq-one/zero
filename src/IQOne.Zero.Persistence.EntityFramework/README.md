# IQOne.Zero.Persistence.EntityFramework

Implements the Zero data contracts over Entity Framework Core.

```csharp
services.AddZeroEntityFramework<ShopContext>(options => options.UseSqlServer(connectionString));
services.AddZeroTransactions();
```

Specifications become queries, conventions are applied to every entity under their own
name, and write ownership is checked before a row is touched.

Names no database engine — add the engine's own EF package alongside this one.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
