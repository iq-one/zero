# IQOne.Zero.Results

An outcome type for operations that are expected to fail.

```csharp
public Result<Invoice> Get(int id) =>
    store.Find(id) is { } invoice
        ? invoice
        : Error.NotFound("invoice.missing", $"No invoice with id {id}.");
```

Errors are values with a stable code and a kind, so the failure is part of the signature.
Exceptions stay for what nobody planned for.

The compiler enforces it: a discarded result is **ZERO100**, reading `Value` without
checking is **ZERO101**, throwing a failure you promised to return is **ZERO102**.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
