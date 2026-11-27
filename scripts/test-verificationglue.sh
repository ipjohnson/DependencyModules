#!/usr/bin/env bash
#
# Builds and runs the VerificationGlue.Tests projects.
#
# A VerificationGlue.Tests project is a Microsoft.Testing.Platform application. The VSTest
# `dotnet test` that the other test projects use stops on it with VGT1001, and global.json keeps
# the VSTest runner because the xunit.v3.mtp-off leg, the NUnit adapter and coverlet.collector need
# it. So this script runs each project's built application with `dotnet exec`. The application
# exits with a non-zero code when a test fails or when no test runs.
#
# Usage:
#   scripts/test-verificationglue.sh                 run the tests
#   scripts/test-verificationglue.sh <coverage-dir>  run them under coverlet.console and write
#                                                    <coverage-dir>/<project>/coverage.cobertura.xml
#   scripts/test-verificationglue.sh -p:XunitMajor=4 pass MSBuild properties to the build. They
#                                                    can come before or after <coverage-dir>.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COVERAGE_DIR=""
PROPERTIES=()

for argument in "$@"; do
    case "${argument}" in
        -p:*) PROPERTIES+=("${argument}") ;;
        *) COVERAGE_DIR="${argument}" ;;
    esac
done

PROJECTS=(
    "tests/DependencyModules.Specs/DependencyModules.Specs.csproj"
)

if [ -n "${COVERAGE_DIR}" ]; then
    (cd "${REPO_ROOT}" && dotnet tool restore >/dev/null)

    # A CI build sets ContinuousIntegrationBuild, which writes /_/... source paths into the PDBs.
    # coverlet.console cannot map them back to the checkout: it does not read the
    # CoverletSourceRootsMapping file that coverlet.collector's build targets write. It then finds
    # no local source for any module, skips them all, and reports 0%. Keep real source paths in
    # the coverage build.
    PROPERTIES+=("-p:DeterministicSourcePaths=false")
fi

for project in "${PROJECTS[@]}"; do
    name="$(basename "${project}" .csproj)"
    echo "==> ${project} ${PROPERTIES[*]:-}"

    dotnet build "${REPO_ROOT}/${project}" --configuration Release --nologo -v quiet ${PROPERTIES[@]+"${PROPERTIES[@]}"}

    assembly="$(dotnet msbuild "${REPO_ROOT}/${project}" -getProperty:TargetPath -p:Configuration=Release ${PROPERTIES[@]+"${PROPERTIES[@]}"})"

    if [ -z "${COVERAGE_DIR}" ]; then
        dotnet exec "${assembly}"
        continue
    fi

    mkdir -p "${COVERAGE_DIR}/${name}"

    # The same filters as tests/coverlet.runsettings, which coverlet.collector reads for the
    # VSTest projects.
    (cd "${REPO_ROOT}" && dotnet tool run coverlet "${assembly}" \
        --target "dotnet" \
        --targetargs "exec \"${assembly}\"" \
        --format cobertura \
        --output "${COVERAGE_DIR}/${name}/coverage.cobertura.xml" \
        --exclude "[*]CSharpAuthor.*" \
        --exclude-by-attribute "GeneratedCodeAttribute" \
        --exclude-by-attribute "CompilerGeneratedAttribute" \
        --exclude-by-attribute "ExcludeFromCodeCoverageAttribute" \
        --exclude-by-attribute "ObsoleteAttribute" \
        --skipautoprops)
done
