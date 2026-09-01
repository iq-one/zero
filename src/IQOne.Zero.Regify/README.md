# IQOne.Zero.Regify

Zero's source generator and analyzers. Referenced automatically by the metapackage; there is
nothing to configure.

Generates, per module assembly:

- the `Module` declaration, with dependencies derived from the assembly reference graph
- the service registration table, from the lifetime markers types implement

Reports as compiler diagnostics: duplicate registrations, ambiguous service types,
lifetime markers on non-registrable types, and captive dependencies.

Part of [Zero](https://github.com/keremcanaktas/zero) by IQOne.
