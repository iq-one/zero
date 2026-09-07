# ZERO012 — The module generator failed

**Severity:** error · **Category:** Zero.Registration

A bug in the module generator, reported instead of failing the compilation outright.

## Why this rule exists at all

This generator writes the **whole module**: every service registration, every request handler,
every route. A throw in it normally fails the build with CS8785 and produces nothing — and
generated code cannot be edited, because it is written during compilation and there is no file
the compiler reads back. So a framework bug would leave an application unable to build with
nothing for its author to try but a new framework release.

## Carry on: declare the module yourself

The generator stands down for an assembly that declares its own module. The practical route is
to recover the last good output and correct it:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Build once against the previous framework version, take `generated/.../Module.g.cs`, move it
into the project as an ordinary file, and remove the property. The generator finds a
hand-declared `Module` and writes nothing; the file is now yours to fix.

Two things to know about doing that. It is not a small file, and it is not maintained for you
any more — a new handler or route will not appear in it until you add it. So treat it as a way
to keep shipping, and delete it once the framework is fixed.

## Then report it

The message carries the exception type and text, which with the assembly name is usually
enough to reproduce.

## The one failure this cannot catch

A generator that fails while *loading* — a missing dependency, an analyzer built against a
different compiler — never reaches an assembly, so there is nothing to report. There the answer
is to pin the previous framework version.
