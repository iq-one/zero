# ZERO008 — Open generic has no service type it can be registered under

**Severity:** error · **Category:** Zero.Registration

An open generic carries a lifetime marker, but there is no interface it can be paired with.

An open generic is registered as one unbound type to another —
`AddScoped(typeof(IService<>), typeof(Implementation<>))` — and that is only possible when the
two have the same shape: the implementation has to pass its own type parameters to the
interface, all of them, in order.

```csharp
public sealed class Cache<TKey, TValue> : ICache, IScoped;   // ZERO008
//                 ^ two type parameters, an interface that takes none
```

## Fix

Give it an interface of the same shape:

```csharp
public interface ICache<TKey, TValue>;

public sealed class Cache<TKey, TValue> : ICache<TKey, TValue>, IScoped;
```

That is generated as:

```csharp
services.AddScoped(typeof(ICache<,>), typeof(Cache<,>));
```

Or register it by hand in the module's `OnConfigureServices`, where a partly closed type can
be named:

```csharp
context.Services.AddScoped(typeof(ICache<string,>), ...);   // no such thing; write the closed pair
context.Services.AddScoped<ICache<string, Invoice>, Cache<string, Invoice>>();
```

## What this rule no longer reports

**An abstract class.** It is skipped without a diagnostic, and the concrete classes deriving
from it are registered in its place. This is the shape to write when a family of types shares
one lifetime:

```csharp
public interface IExportFormat : ITransient;          // declares the lifetime

public abstract class ExportFormat : IExportFormat;    // skipped

public sealed class CsvExportFormat : ExportFormat;    // registered as IExportFormat
```

`CsvExportFormat` names no interface of its own, so registration falls back to the ones its
base class names.

**An open generic that does have a matching interface.** A pipeline behaviour is the ordinary
case, and it is generated like any other registration:

```csharp
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>;
```

```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
```

**A marker a type only inherited.** The diagnostic points at the declaration that made the
choice, never at one that inherited it.
