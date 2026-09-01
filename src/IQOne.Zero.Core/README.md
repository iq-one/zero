# IQOne.Zero.Core

Zero's application lifecycle and module system.

`Application` drives the phases — initialize, pre-run, configure services, build the
provider, post-run — sequentially and explicitly, so a failure surfaces where it happens
and can be awaited.

Modules declare what they depend on; the host sorts them topologically. There is no order
number to maintain, and a cycle is reported by name.

Part of [Zero](https://github.com/keremcanaktas/zero) by IQOne.
