#!/usr/bin/env bash
#
# Packs the shipping projects, then consumes the resulting .nupkg files from a
# throwaway project exactly the way a real user would.
#
# This catches packaging faults that a ProjectReference-based test suite cannot see:
#   * build/$(PackageId).props|targets landing at a path NuGet won't honour
#     (a trailing backslash in PackagePath packs to "build//" on Linux/macOS)
#   * the analyzer failing to load or generate from a real package
#   * MSBuild properties not reaching the generator through the packaged .targets
#   * Roslyn/compiler dependencies leaking into consumers' dependency graphs
#
# Usage: build/verify-packages.sh [version]

set -euo pipefail

VERSION="${1:-1.0.0-verify}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK_DIR="$(mktemp -d)"
FEED="${WORK_DIR}/feed"
APP="${WORK_DIR}/ConsumerApp"

cleanup() { rm -rf "${WORK_DIR}"; }
trap cleanup EXIT

fail() { printf 'FAIL: %b\n' "$*" >&2; exit 1; }
pass() { echo "  ok: $*"; }

echo "==> Packing ${VERSION} into a local feed"
mkdir -p "${FEED}"
for proj in \
    src/DependencyModules.Runtime/DependencyModules.Runtime.csproj \
    src/DependencyModules.SourceGenerator/DependencyModules.SourceGenerator.csproj \
    src/DependencyModules.SourceGenerator.Impl/DependencyModules.SourceGenerator.Impl.csproj \
    src/DependencyModules.xUnit/DependencyModules.xUnit.csproj \
    src/DependencyModules.xUnit.NSubstitute/DependencyModules.xUnit.NSubstitute.csproj; do
    dotnet pack "${REPO_ROOT}/${proj}" -c Release -o "${FEED}" \
        "/p:PackageVersion=${VERSION}" --nologo -v quiet
done

echo "==> Checking package layout"

# NuGet only auto-imports build/<PackageId>.props|targets at that exact path.
for id in DependencyModules.SourceGenerator DependencyModules.SourceGenerator.Impl; do
    entries="$(unzip -Z1 "${FEED}/${id}.${VERSION}.nupkg")"
    for ext in props targets; do
        grep -qx "build/${id}.${ext}" <<<"${entries}" \
            || fail "${id}: expected 'build/${id}.${ext}' in package, got:\n${entries}"
    done
    grep -q '//' <<<"${entries}" && fail "${id}: package contains a doubled path separator:\n${entries}"
    pass "${id} build/ files are at the convention path"
done

# The analyzer must ship where Roslyn looks for it.
unzip -Z1 "${FEED}/DependencyModules.SourceGenerator.${VERSION}.nupkg" \
    | grep -qx 'analyzers/dotnet/cs/DependencyModules.SourceGenerator.dll' \
    || fail "analyzer assembly is not at analyzers/dotnet/cs/"
pass "analyzer assembly is at analyzers/dotnet/cs/"

# Placeholder metadata must never ship.
for pkg in "${FEED}"/*.nupkg; do
    id="$(basename "${pkg}" ".${VERSION}.nupkg")"
    nuspec="$(unzip -p "${pkg}" "${id}.nuspec")"
    grep -q '<description>Package Description</description>' <<<"${nuspec}" \
        && fail "${id}: ships NuGet's placeholder description"
    grep -q '<description>' <<<"${nuspec}" || fail "${id}: has no description"
    grep -q '<readme>' <<<"${nuspec}" || fail "${id}: has no readme"
    grep -q '<license ' <<<"${nuspec}" || fail "${id}: has no license"
done
pass "all packages carry real description/readme/license metadata"

# The generator is a compile-time concern; Roslyn is supplied by the host.
unzip -p "${FEED}/DependencyModules.SourceGenerator.${VERSION}.nupkg" \
    DependencyModules.SourceGenerator.nuspec \
    | grep -q 'id="Microsoft.CodeAnalysis' \
    && fail "DependencyModules.SourceGenerator leaks a Microsoft.CodeAnalysis dependency to consumers"
pass "analyzer package does not leak compiler dependencies"

echo "==> Building a consumer project against the packed feed"
mkdir -p "${APP}"
cat >"${APP}/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear/>
    <add key="local" value="${FEED}"/>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>
  </packageSources>
</configuration>
EOF

cat >"${APP}/ConsumerApp.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Consumed through the packaged build/*.targets; the generator only sees this
         if CompilerVisibleProperty was declared, which only happens if the targets loaded. -->
    <ExcludeGeneratedCodeFromCoverage>false</ExcludeGeneratedCodeFromCoverage>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DependencyModules.Runtime" Version="${VERSION}"/>
    <PackageReference Include="DependencyModules.SourceGenerator" Version="${VERSION}"/>
    <!-- BuildServiceProvider lives in the DI implementation package, not Abstractions. -->
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1"/>
  </ItemGroup>
</Project>
EOF

cat >"${APP}/Program.cs" <<'EOF'
using DependencyModules.Runtime;
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace ConsumerApp;

public interface IGreeter {
    string Greet();
}

[SingletonService]
public class Greeter : IGreeter {
    public string Greet() => "hello from a packaged generator";
}

[DependencyModule]
public partial class ConsumerModule;

public static class Program {
    public static int Main() {
        var services = new ServiceCollection();
        services.AddModule<ConsumerModule>();

        var provider = services.BuildServiceProvider();
        var greeter = provider.GetService<IGreeter>();

        if (greeter is null) {
            Console.Error.WriteLine("FAIL: IGreeter was not registered by the generated module");
            return 1;
        }

        Console.WriteLine(greeter.Greet());
        return 0;
    }
}
EOF

dotnet build "${APP}/ConsumerApp.csproj" -c Release --nologo -v quiet \
    || fail "consumer project failed to build against the packed feed"
pass "consumer project builds"

# Prove the generator actually ran, rather than the build merely succeeding.
generated="$(find "${APP}/generated" -name '*.g.cs' 2>/dev/null || true)"
[ -n "${generated}" ] || fail "generator produced no output in the consumer project"
grep -rq 'PopulateServiceCollection' "${APP}/generated" \
    || fail "generated output is missing the module registration code"
pass "generator emitted module registration code"

# ExcludeGeneratedCodeFromCoverage=false must reach the generator via build/*.targets.
if grep -rq 'ExcludeFromCodeCoverage' "${APP}/generated"; then
    fail "ExcludeGeneratedCodeFromCoverage=false did not reach the generator; build/*.targets was not imported"
fi
pass "MSBuild properties reach the generator through build/*.targets"

echo "==> Running the consumer app"
output="$(dotnet run --project "${APP}/ConsumerApp.csproj" -c Release --no-build --nologo)"
[ "${output}" = "hello from a packaged generator" ] \
    || fail "unexpected consumer output: ${output}"
pass "resolved a generated registration at run time"

echo
echo "All package verification checks passed."
