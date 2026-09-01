# ZERO007 — Service type could not be determined

**Severity:** error · **Category:** Zero.Registration

A class carries a lifetime marker, but no interface was found to register it under.

Registration defaults to the interface whose name matches the class: `InvoiceStore` is
registered as `IInvoiceStore`. When that interface does not exist, and the class implements
either none or several unrelated interfaces, the service type is genuinely ambiguous.

## Fix

Add the matching interface:

```csharp
public interface IInvoiceStore : IScoped;

public sealed class InvoiceStore : IInvoiceStore;
```

Or state the service types explicitly:

```csharp
[ServiceTypes(typeof(IInvoiceReader), typeof(IInvoiceWriter))]
public sealed class InvoiceStore : IInvoiceReader, IInvoiceWriter, IScoped;
```

To register the concrete class only, with no interface:

```csharp
[ServiceTypes(ServiceSelectorType = ServiceSelectorType.Self)]
public sealed class InvoiceStore : IScoped;
```
