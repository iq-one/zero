# ZERO011 — A type contradicts the lifetime its abstraction declares

**Severity:** error · **Category:** Zero.Registration

The abstraction says one lifetime; the implementation says another.

```csharp
public interface IInvoiceStore : IScoped;

public sealed class InvoiceStore : IInvoiceStore, ISingleton;   // ZERO011
```

Both are visible, and neither can be picked without making something else untrue:

- **Let the type win** and the abstraction lies. A consumer writing
  `SomeService(IInvoiceStore store)` reads `IScoped` from the interface, reasons about
  per-request state, and has no way to learn the implementation chose otherwise.
- **Let the abstraction win** and something the author wrote on purpose is ignored, silently.

So it is reported, and the author decides.

## Fix

**If the abstraction is right,** remove the marker from the implementation. It was already
scoped; the interface said so.

```csharp
public sealed class InvoiceStore : IInvoiceStore;
```

**If the implementation is right and every implementation would be,** change the
abstraction. That is where lifetime belongs, and it is what a consumer reads.

```csharp
public interface IInvoiceStore : ISingleton;
```

**If this one implementation genuinely differs,** say so with the attribute:

```csharp
[Singleton]
public sealed class CachedInvoiceStore : IInvoiceStore;
```

The attribute exists exactly for the lifetime no abstraction can express — a decorator that
caches, an adapter that holds a connection. It is louder than a marker interface, which is
the point: the next reader sees that the difference was deliberate.

## Which lifetime did I inherit?

The message names the interface. Only lifetimes reached through your **own** base list count:
a marker two interfaces away still reaches you, and the message says which of your interfaces
carried it.

## The other one

Two lifetime markers written directly on one type is **ZERO006** — a different mistake, and
the fix there is simply to keep one.
