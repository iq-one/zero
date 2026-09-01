---
id: zero.modules.no-numeric-order
title: Declare dependencies, never an order
package: IQOne.Zero.Core
applies-to: ["**/Module*.cs", "**/*Module.cs"]
---

Module execution order is derived by topological sort from what each module depends on.
There is no order number to pick, renumber or reconcile.

## Do

Let the dependency come from the project reference. The generator reads the assembly
reference graph, so a module that references another is ordered after it automatically:

```csharp
public sealed partial class Module
{
    partial void OnConfigureServices(IModuleServiceContext context)
        => context.Services.AddValidatedOptions<MailOptions>();
}
```

The `Module` class itself, its `Name` and its `Dependencies` are generated. Write only the
`OnConfigureServices` partial.

## When the reference graph does not express it

Some ordering requirements exist without a code reference — a module that must seed data
another module reads at startup. State those, and only those, explicitly:

```csharp
[DependsOn(typeof(Seeding.Module))]
public sealed partial class Module;
```

## Don't

Do not introduce an order number, a priority field, or a hand-maintained list of modules in
startup order. Every one of them has to be renumbered when a module is inserted, and none of
them can be checked against what the code actually does.

A cycle throws `ModuleDependencyCycleException` and names the participants.
