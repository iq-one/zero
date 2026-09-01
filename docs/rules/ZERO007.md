# ZERO007 — Service type could not be determined

**Severity:** error · **Category:** Zero.Registration

A class carries a lifetime marker, but no interface was found to register it under.

Registration defaults to the interface whose name matches the class: `InvoiceStore` is
registered as `IInvoiceStore`. When that interface does not exist, and the class implements
either none or several unrelated interfaces, the service type is genuinely ambiguous.

## How the default is chosen

1. An extension point the framework resolves by closed generic — `IValidator<T>`,
   `IRequestHandler<T, R>`, `IPipelineBehavior<T, R>`, `IRequirementHandler<T>` — wins outright.
2. Otherwise, among the interfaces the class names in its own base list: the one called
   exactly `I` + the class's name. The match is exact, so `UserService : IService, IUserService`
   resolves to `IUserService`, not to `IService`.
3. Failing that, the single interface, when there is exactly one.
4. When the class names no interface of its own, the same three steps are applied to the
   interfaces its base class names.

Anything else is this diagnostic.

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
