#!/usr/bin/env bash
#
# Installs the packed .nupkg files into a throwaway project and asserts that a consumer
# gets what the README promises: generated registrations, and rules the compiler enforces.
#
# This is the only check that exercises the packaging itself. A project reference would
# pass even if the analyzer were excluded from the package, which has happened before.

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
packages="$root/artifacts/packages"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

version="$(basename "$packages"/IQOne.Zero.[0-9]*.nupkg | sed -E 's/^IQOne\.Zero\.(.+)\.nupkg$/\1/' | head -1)"
echo "Verifying IQOne.Zero $version"

cd "$work"

cat > nuget.config <<XML
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packages" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
XML

cat > Consumer.csproj <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="IQOne.Zero" Version="$version" />
  </ItemGroup>
  <ItemGroup>
    <Compile Remove="generated/**/*.cs" />
  </ItemGroup>
</Project>
XML

cat > Services.cs <<'CS'
using IQOne.Zero.DependencyInjection.Descriptors;

namespace Consumer;

public interface IInvoiceStore : IScoped;

public sealed class InvoiceStore : IInvoiceStore;
CS

# Restored into a throwaway folder, never the machine's global cache: repacking the same
# version number leaves the old extraction in place there, and the check would then verify
# the previous build instead of this one.
export NUGET_PACKAGES="$work/packages"

echo "--- registrations are generated ---"
dotnet build --nologo -v q
generated="$(find generated -name Module.g.cs)"
grep -q "AddScoped<global::Consumer.IInvoiceStore, global::Consumer.InvoiceStore>" "$generated" \
  || { echo "FAIL: the registration was not generated"; cat "$generated"; exit 1; }

echo "--- rules are enforced ---"
cat >> Services.cs <<'CS'

public interface IReportCache : ISingleton;

public sealed class ReportCache(IInvoiceStore store) : IReportCache
{
    public IInvoiceStore Store { get; } = store;
}
CS

if dotnet build --nologo -v q > build.log 2>&1; then
  echo "FAIL: a captive dependency did not fail the build"
  exit 1
fi

grep -q ZERO009 build.log \
  || { echo "FAIL: expected ZERO009, got:"; cat build.log; exit 1; }

echo "--- commands and queries dispatch through a generated table ---"
cat > Messaging.cs <<'CS'
using System.Threading;
using System.Threading.Tasks;
using IQOne.Zero.Messaging;
using IQOne.Zero;

namespace Consumer;

public sealed record GetInvoice(int Id) : IQuery<string>;

public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, string>
{
    public Task<Result<string>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Success($"invoice {query.Id}"));
}

public sealed record Orphan : ICommand;
CS

# Services.cs still holds the captive dependency from the previous step.
git init -q . 2>/dev/null || true
sed -i.bak '/IReportCache/,$d' Services.cs && rm -f Services.cs.bak

dotnet build --nologo -v q
generated="$(find generated -name Module.g.cs)"

grep -q "RequestPipeline.RunAsync<global::Consumer.GetInvoice, string>" "$generated" \
  || { echo "FAIL: the dispatch row was not generated"; cat "$generated"; exit 1; }

grep -q "builder.Declare(typeof(global::Consumer.Orphan));" "$generated" \
  || { echo "FAIL: an unhandled request was not declared, so startup could not report it"; exit 1; }

echo "--- a routed request becomes a real endpoint ---"
# Web is not in the metapackage on purpose, so this step adds it the way a consumer would.
dotnet add package IQOne.Zero.Web --version "$version" > add.log 2>&1 \
  || { echo "FAIL: the web package could not be added"; cat add.log; exit 1; }
cat > Web.cs <<'CS'
using IQOne.Zero.Messaging;
using IQOne.Zero.Web;

namespace Consumer;

[Get("/invoices/{id:int}", Tag = "Invoices")]
public sealed record GetInvoiceByRoute(int Id) : IQuery<string>;
CS

dotnet build --nologo -v q
generated="$(find generated -name Module.g.cs)"

grep -q 'ZeroEndpoint.RunAsync<global::Consumer.GetInvoiceByRoute, string>' "$generated" \
  || { echo "FAIL: the endpoint was not generated"; cat "$generated"; exit 1; }

echo "--- rule files ship inside the packages ---"
# Listed to a file first: with pipefail, `grep -q` exiting early would SIGPIPE unzip and
# fail the pipeline even on a match.
unzip -l "$packages/IQOne.Zero.Abstractions.$version.nupkg" > contents.txt
grep -q "zero/rules/IQOne.Zero.Abstractions/" contents.txt \
  || { echo "FAIL: the package carries no rule files"; cat contents.txt; exit 1; }

echo "--- the analyzer reaches a consumer of the metapackage ---"
unzip -p "$packages/IQOne.Zero.$version.nupkg" IQOne.Zero.nuspec > meta.xml
grep -q 'id="IQOne.Zero.Generators".*include="All"' meta.xml \
  || { echo "FAIL: the metapackage excludes the analyzer, so no rule would be enforced"; cat meta.xml; exit 1; }

echo "OK"
