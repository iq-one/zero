# Working on Zero

Rules for anyone changing this repository — human or agent. They are short because each one
was written after something went wrong.

Zero's own guidance for its *consumers* lives inside the packages, under
`src/<Package>/rules/`. This file is different: it is about building the framework itself.

## Before you add a capability

Read `docs/capability-contract.md` in full. It is the contract every package keeps, and the
reason a fourteenth capability is as mechanical to add as a second. If your change does not
fit it, say so and argue — do not quietly deviate.

`src/IQOne.Zero.Validation/` is the reference implementation of the shape. When in doubt
about file layout, comment style, options, registration or tests, copy what it does.

## The rules

**Every public type and member needs an XML doc comment.** The build treats warnings as
errors and `GenerateDocumentationFile` is on, so a missing one fails. The docs are not
ceremony: they are what a consumer's IntelliSense shows and what a coding agent reads.

**Comments explain why, not what.** A comment that restates the line below it is noise.
A comment that says why the obvious approach was rejected is the most valuable thing in the
file. Delete the first kind on sight.

**The public API is English.** Comments in this repository's build files are Turkish; the
shipped surface is not.

**Guidance must compile.** Every C# snippet in a `rules/*.md` and every `capability.json`
example is compiled against the real assemblies by `tests/IQOne.Zero.Guidance.Tests`. Four
blocking defects once shipped as guidance describing an API nobody had written. Run it.

A snippet that shows a shape rather than code can opt out by fencing it
```` ```csharp illustrative ````. It is awkward to type on purpose.

**The public surface is locked.** `tests/IQOne.Zero.ApiSurface.Tests` renders it to text and
compares. Do not edit an `.approved.txt` to make a test pass — regenerate it, read the diff,
and say in the commit what changed and why. Removing a member is breaking; the file is where
that becomes visible.

**A capability's `Add` must be sufficient on its own.** Contract §1. Register everything the
capability needs in `Add`; leave `Complete` to seal. There is a conformance test per
capability that calls only `Add`, builds the provider with `ValidateOnBuild` and
`ValidateScopes` on, and resolves the public types.

**Diagnostics state the fix, not just the violation.** `'X' is invalid` is a bad message.
`'X' is invalid; write Y instead` is the whole point — a diagnostic is the fastest feedback
loop a person or an agent has. Every id needs a page under `docs/rules/` and a row in the
owning project's `AnalyzerReleases.Unshipped.md`.

**Banned APIs are banned.** `BannedSymbols.txt` is enforced: no `.Result`, `.Wait()`,
`.GetAwaiter().GetResult()`, `Task.Run`, `DateTime.Now`/`UtcNow` (take `TimeProvider`).

**Do not add a dependency to `Directory.Packages.props` casually.** Every one of them lands
in a consumer's dependency graph.

## Tests

`dotnet test IQOne.Zero.slnx` must be green before a commit. Beyond that:

- **Test the behaviour, not the helper.** A test that hand-builds the registry proves the
  registry works, not that the framework does. The blocking defect in `AddModules` survived
  precisely because every test built its own registry and none called the documented entry
  point. `tests/Zero.Sample.Invoices.Tests` drives a real host over HTTP; keep it working.
- **Mutation-check a fix you care about.** Revert it and confirm the test fails. Several
  tests in this repository were written that way and it is worth the two minutes. For a
  GENERATOR, check the mutation compiled: warnings are errors here, so a mutation that leaves
  unreachable code fails to build and the test project silently runs against the previous
  analyzer DLL — which looks exactly like a test that does not work. Mutate in a way that
  still compiles.
- Name tests as sentences: `A_failed_command_rolls_back_and_never_saves`.
- Watch the FluentAssertions overload trap: `Equal("a", "because…")` reads the reason as a
  second element. Write `Equal(["a"], "because…")`. It has caught three people here.

## Every generator needs a way out

Generated code **cannot be edited**: it is written during compilation and there is no file the
compiler reads back (`EmitCompilerGeneratedFiles` writes a copy for you to read, and nothing
reads it). So a bug in a generator does not merely produce something wrong — it stops the build
of everybody using that version, with nothing they can change. Two things follow, and a new
generator does not ship without both.

**A throw must be caught per unit of work.** Uncaught it fails the compilation with CS8785 and
produces nothing at all. Wrap the work in `Guard.Run` with a diagnostic in the capability's
range, so one declaration reports and the rest are written as usual. What this cannot cover is
a generator that fails while *loading*; there the only answer is pinning the previous version,
which is worth remembering when deciding how much a generator should carry.

**Generation must be declinable.** Whatever a generator writes, its user must be able to write
instead. `[NoGenerate]` is that marker and a new generator honours it: on its target, on an
enclosing type, or on the assembly — the last being how a project declines all of it while
keeping the packages. Beyond it, each generator also stands down for a hand-written result:

| generator | also stands down for |
| --- | --- |
| module registration | a `Module` that implements `IModule` — a `partial` part is the extension point, not a takeover, and treating it as one would silently stop generating for everybody using `[DependsOn]` |
| `[Projection]`, `[Mapping]` | the attribute being removed |
| `Map<TSource, TDestination>` | `Selector` being declared |

Standing down is silent. There is nothing to report — the hand-written member is in the same
file — and what the author has taken on is named by the compiler: an unimplemented abstract
member, a partial method with no body. Say this in the rule page too: the page for a
generator's failure diagnostic is where somebody whose build just stopped will look, so it
carries the recovery, not only the cause.

## Diagnostic id ranges

Reserved per capability in `docs/capability-contract.md`. Ids are never reused; a retired
one keeps its number and its page.

## Commits

Say what changed and **why it was wrong before**. A commit that says "fix binder" is worth
less than one that says the binder trusted `Content-Length`, which is absent under chunked
encoding, so the body was silently never read.
