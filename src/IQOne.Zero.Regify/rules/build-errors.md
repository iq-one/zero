---
id: zero.regify.read-the-diagnostic
title: The build error states the fix
package: IQOne.Zero.Regify
applies-to: ["**/*.cs"]
---

Zero reports wiring mistakes as compiler diagnostics, and every message names the change to
make. Read the message rather than searching for the rule.

| Id | What happened | What to do |
| --- | --- | --- |
| RGF006 | A type implements two lifetime markers | Keep the one matching how it is used |
| RGF007 | No interface to register the class under | Add `I{ClassName}`, or state `[ServiceTypes(typeof(...))]` |
| RGF008 | A lifetime marker sits on an abstract or generic type | Move it to the concrete class |
| RGF009 | A singleton takes a scoped dependency | Take `IServiceScopeFactory`, or stop being a singleton |
| RGF010 | Two implementations register as the same service type | Separate them with a keyed `[ServiceTypes]` |

RGF006 to RGF009 are errors and stop the build. RGF010 is a warning: the container keeps
both registrations and returns the last one, which is occasionally what was wanted.

## Do not suppress these

Each of them marks a defect that has no reliable runtime symptom — a captive dependency
looks like intermittent cross-request data, not like a wiring bug. Suppressing the diagnostic
does not remove the defect, it removes the only place it is visible.

If a diagnostic is wrong, that is a framework bug worth reporting, not a case for
`#pragma warning disable`.
