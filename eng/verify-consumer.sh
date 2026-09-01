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

grep -q RGF009 build.log \
  || { echo "FAIL: expected RGF009, got:"; cat build.log; exit 1; }

echo "--- rule files ship inside the packages ---"
unzip -l "$packages/IQOne.Zero.Abstractions.$version.nupkg" | grep -q "zero/rules/IQOne.Zero.Abstractions/" \
  || { echo "FAIL: the package carries no rule files"; exit 1; }

echo "OK"
