# IQOne.Zero.Core

Zero's application lifecycle and module system.

`AddModules(...)` orders the modules and runs their configure-services phase during the
call, so it works the same in an ASP.NET host as it does under Zero's own `Application`.

`Application` drives the phases — configure services, build the provider, initialize,
pre-run, and post-run on shutdown — sequentially and explicitly, so a failure surfaces
where it happens and can be awaited.

Modules declare what they depend on; the host sorts them topologically. There is no order
number to maintain, and a cycle is reported by name.

Part of [Zero](https://iqone.solutions/zero) by IQOne.
