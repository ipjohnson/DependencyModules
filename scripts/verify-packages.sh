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

# Every target framework the shipping libraries claim. Each one gets its own consumer project
# below, because "it packs" and "a consumer on that TFM can actually resolve and run it" are
# different claims, and only the second one is what ships.
TFMS=(net8.0 net10.0)

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
    src/DependencyModules.Testing/DependencyModules.Testing.csproj \
    src/DependencyModules.xUnit/DependencyModules.xUnit.csproj \
    src/DependencyModules.NUnit/DependencyModules.NUnit.csproj \
    src/DependencyModules.NSubstitute/DependencyModules.NSubstitute.csproj \
    src/DependencyModules.Moq/DependencyModules.Moq.csproj \
    src/DependencyModules.FakeItEasy/DependencyModules.FakeItEasy.csproj; do
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
    | grep -qx "analyzers/dotnet/cs/DependencyModules.SourceGenerator.dll" \
    || fail "DependencyModules.SourceGenerator: analyzer assembly is not at analyzers/dotnet/cs/"

# No lib/. An analyzer that reaches a consumer's output ships compiler machinery to run time.
unzip -Z1 "${FEED}/DependencyModules.SourceGenerator.${VERSION}.nupkg" | grep -q '^lib/' \
    && fail "DependencyModules.SourceGenerator: ships a lib/ folder, so the analyzer would flow to consumer output"

pass "DependencyModules.SourceGenerator analyzer assembly is at analyzers/dotnet/cs/ with no lib/"

# The shipping libraries multi-target. A missing lib/ folder means one TFM quietly stopped being
# produced, and consumers on it would resolve no assembly at all.
LIB_PACKAGES=(
    DependencyModules.Runtime
    DependencyModules.Testing
    DependencyModules.xUnit
    DependencyModules.NUnit
    DependencyModules.NSubstitute
    DependencyModules.Moq
    DependencyModules.FakeItEasy
)
for id in "${LIB_PACKAGES[@]}"; do
    entries="$(unzip -Z1 "${FEED}/${id}.${VERSION}.nupkg")"
    for tfm in "${TFMS[@]}"; do
        grep -qx "lib/${tfm}/${id}.dll" <<<"${entries}" \
            || fail "${id}: no lib/${tfm}/${id}.dll in package, got:\n${entries}"
    done
    pass "${id} ships lib/ for every target framework"
done

# Framework-matched dependency groups. This is the whole reason the libraries multi-target: a
# net10.0 group pinning an 8.x Microsoft.Extensions package puts that older assembly into a .NET 10
# consumer's output in place of the one its shared framework already supplies.
for id in "${LIB_PACKAGES[@]}"; do
    unzip -p "${FEED}/${id}.${VERSION}.nupkg" "${id}.nuspec" | python3 -c '
import re, sys
nuspec = sys.stdin.read()
for tfm, body in re.findall(r"<group targetFramework=\"([^\"]+)\">(.*?)</group>", nuspec, re.S):
    match = re.match(r"net(\d+)\.", tfm)
    if not match:
        continue
    want = match.group(1)
    for dep, ver in re.findall(r"id=\"(Microsoft\.Extensions\.[^\"]+)\" version=\"([^\"]+)\"", body):
        if ver.split(".")[0] != want:
            sys.exit(f"  {tfm} group depends on {dep} {ver}, expected {want}.x")
' || fail "${id}: dependency groups are not framework-matched"
    pass "${id} dependency groups are framework-matched"
done

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
unzip -p "${FEED}/DependencyModules.SourceGenerator.${VERSION}.nupkg" "DependencyModules.SourceGenerator.nuspec" \
    | grep -q 'id="Microsoft.CodeAnalysis' \
    && fail "DependencyModules.SourceGenerator leaks a Microsoft.CodeAnalysis dependency to consumers"
pass "DependencyModules.SourceGenerator does not leak compiler dependencies"

for TFM in "${TFMS[@]}"; do

echo "==> Building a ${TFM} consumer project against the packed feed"

APP="${WORK_DIR}/ConsumerApp-${TFM}"

# Match the DI implementation package to the consumer's framework. Referencing 8.0.1 from a net10.0
# app would mask the very regression this script exists to catch, by pinning the old assembly from
# the consumer side rather than letting the package's own dependency group decide.
case "${TFM}" in
    net8.0)  DI_VERSION="8.0.1" ;;
    net10.0) DI_VERSION="10.0.0" ;;
    *)       fail "no Microsoft.Extensions.DependencyInjection version mapped for ${TFM}" ;;
esac

mkdir -p "${APP}"
cat >"${APP}/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear/>
    <add key="local" value="${FEED}"/>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>
  </packageSources>
  <!--
    A private extraction cache, so this really consumes the packages just packed. NuGet keys the
    global cache on id/version alone: with a fixed VERSION like 1.0.0-verify, an extraction left
    over from an earlier run shadows the new .nupkg entirely and the script grades stale bits.
    That silently hid a package whose lib/ layout had changed underneath it.
  -->
  <config>
    <add key="globalPackagesFolder" value="${WORK_DIR}/nuget-cache"/>
  </config>
</configuration>
EOF

cat >"${APP}/ConsumerApp.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>${TFM}</TargetFramework>
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
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="${DI_VERSION}"/>
  </ItemGroup>
</Project>
EOF

cat >"${APP}/Program.cs" <<'EOF'
using DependencyModules.Runtime;
using DependencyModules.Runtime.Attributes;
using DependencyModules.Runtime.Conventions;
using Microsoft.Extensions.DependencyInjection;

namespace ConsumerApp;

public interface IGreeter {
    string Greet();
}

[SingletonService]
public class Greeter : IGreeter {
    public string Greet() => "hello from a packaged generator";
}

// Registered by convention rather than by attribute. IRuleBase is reached through interface
// inheritance, which the convention matches by declaration; the base-class hop needs an opt-in.
public interface IRuleBase { }

public interface IRule : IRuleBase {
    string Name { get; }
}

public class FirstRule : IRule {
    public string Name => "first";
}

public class SecondRule : IRule {
    public string Name => "second";
}

[DependencyModule]
public partial class ConsumerModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll<IRuleBase>().AsSingleton();
    }
}

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

        var rules = provider.GetServices<IRuleBase>().OfType<IRule>().Select(r => r.Name).OrderBy(n => n).ToArray();

        if (rules.Length != 2 || rules[0] != "first" || rules[1] != "second") {
            Console.Error.WriteLine(
                "FAIL: conventions registered [" + string.Join(", ", rules) + "], expected first and second");
            return 1;
        }

        Console.WriteLine(greeter.Greet());
        return 0;
    }
}
EOF

dotnet build "${APP}/ConsumerApp.csproj" -c Release --nologo -v quiet \
    || fail "${TFM} consumer project failed to build against the packed feed"
pass "${TFM} consumer project builds"

# The package has a lib/ folder per TFM, and NuGet picks one. Assert it picked this consumer's own
# rather than falling back to an older compatible one, which would still build and still run.
grep -qF "lib/${TFM}/DependencyModules.Runtime.dll" "${APP}/obj/project.assets.json" \
    || fail "${TFM} consumer did not resolve lib/${TFM}/ of DependencyModules.Runtime"
pass "${TFM} consumer resolved lib/${TFM}/"

# Prove the generator actually ran, rather than the build merely succeeding.
generated="$(find "${APP}/generated" -name '*.g.cs' 2>/dev/null || true)"
[ -n "${generated}" ] || fail "generator produced no output in the consumer project"
grep -rq 'PopulateServiceCollection' "${APP}/generated" \
    || fail "generated output is missing the module registration code"
pass "generator emitted module registration code"

# Conventions are generated by DependencyModules.SourceGenerator rather than by a package of their
# own, so a consumer referencing only the one analyzer package must still get them.
grep -rq 'ConventionDependencies' "${APP}/generated" \
    || fail "convention generator produced no registrations in the consumer project"
pass "convention registrations are generated by the main analyzer package"

# The contracts are DependencyModules.Runtime's public API. Emitting them into the consumer as well
# is what previously forced them to be internal, and made CS0436 unavoidable between two assemblies
# that both emitted them and referenced each other.
grep -rq 'interface IConventionModule' "${APP}/generated" \
    && fail "convention contracts were emitted into the consumer; they ship in DependencyModules.Runtime"
pass "convention contracts are not duplicated into the consumer"

# ExcludeGeneratedCodeFromCoverage=false must reach the generator via build/*.targets.
if grep -rq 'ExcludeFromCodeCoverage' "${APP}/generated"; then
    fail "ExcludeGeneratedCodeFromCoverage=false did not reach the generator; build/*.targets was not imported"
fi
pass "MSBuild properties reach the generator through build/*.targets"

echo "==> Running the ${TFM} consumer app"
output="$(dotnet run --project "${APP}/ConsumerApp.csproj" -c Release --no-build --nologo)"
[ "${output}" = "hello from a packaged generator" ] \
    || fail "unexpected ${TFM} consumer output: ${output}"
pass "${TFM} resolved a generated registration at run time"

done

echo
echo "All package verification checks passed for: ${TFMS[*]}"
