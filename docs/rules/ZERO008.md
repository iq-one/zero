# ZERO008 — Registration target must be concrete

**Severity:** error · **Category:** Zero.Registration

A lifetime marker sits on a type the container cannot construct — an abstract class, or an
open generic.

## Fix

Move the marker to the concrete class. A lifetime marker on an abstraction declares the
lifetime of the types implementing it; the abstraction itself is never registered.

```csharp
public interface IExportFormat : ITransient;          // declares, not registered

public abstract class ExportFormat : IExportFormat;    // not registered either

public sealed class CsvExportFormat : ExportFormat;    // this is what gets registered
```

For an open generic, register it explicitly in the module's `OnConfigureServices`, where the
open type can be named:

```csharp
context.Services.AddScoped(typeof(IValidator<>), typeof(DataAnnotationsValidator<>));
```
