# Zero

An application framework for .NET 10 that resolves registration, dispatch and mapping
while the compiler is still running.

Registration tables, dispatch maps and entity mappings are generated source you can open
and read. Startup does no assembly scanning, and a wrong name is a compiler error rather
than a stack trace someone reads on a Sunday.

```bash
dotnet add package IQOne.Zero
```

## Packages

| Package | Contents |
| --- | --- |
| `IQOne.Zero` | Metapackage covering the common set |
| `IQOne.Zero.Abstractions` | Fundamentals and injection contracts |
| `IQOne.Zero.Core` | Application lifecycle and modules |
| `IQOne.Zero.Configuration` | Validated options |
| `IQOne.Zero.Messaging` | Request, response and handler contracts |
| `IQOne.Zero.Messaging.Dispatch` | Dispatch registry |
| `IQOne.Zero.Data` | Entities, repositories, unit of work, conventions |
| `IQOne.Zero.Data.EntityFramework` | Entity Framework implementation |
| `IQOne.Zero.Data.EntityFramework.SqlServer` | SQL Server configuration |
| `IQOne.Zero.Web` | Web application base |
| `IQOne.Zero.Web.Api` | Endpoint routing and API host |
| `IQOne.Zero.Regify` | Source generators and analyzers |
| `IQOne.Zero.Tool` | The `zero` command line tool |

Generators and analyzers configure themselves. Nothing is added to your project file.

## Rules for AI agents

Zero encodes its architecture rules twice, from one source: as analyzers the compiler
enforces, and as rule files an agent can read. The rule files travel **inside the
packages**, so they are always the same version as the code they describe.

```bash
dotnet tool install --global IQOne.Zero.Tool
zero rules init
```

This writes `AGENTS.md`, `CLAUDE.md` and editor rule files composed from every Zero
package the project references. Re-run it after a version upgrade.

## Status

Early. The API will change before 1.0; releases follow semantic versioning from the first
published package. If you adopt Zero now, pin the version.

Built and maintained by IQOne.
