#!/usr/bin/env bash
#
# Runs the xUnit test projects against one xunit.v3 major. The default build and the coverage
# script use major 3. CI runs this for 4.
#
# Usage:
#   scripts/test-xunit.sh 4                        the versions Directory.Build.props sets for 4
#   scripts/test-xunit.sh 4 4.1.0-pre.12 4.0.0     a given xunit.v3.mtp-off and runner version

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAJOR="${1:?usage: scripts/test-xunit.sh <major> [xunit.v3 version] [runner version]}"

PROPERTIES=("-p:XunitMajor=${MAJOR}")
if [ -n "${2:-}" ]; then PROPERTIES+=("-p:XunitV3Version=$2"); fi
if [ -n "${3:-}" ]; then PROPERTIES+=("-p:XunitRunnerVersion=$3"); fi

PROJECTS=(
    "tests/DependencyModules.Tests/DependencyModules.Tests.csproj"
    "integ-tests/SutProject.Tests/SutProject.Tests.csproj"
    "integ-tests/web/WebApiApp.Tests/WebApiApp.Tests.csproj"
)

TEST_LOG="$(mktemp)"
trap 'rm -f "${TEST_LOG}"' EXIT

for project in "${PROJECTS[@]}"; do
    echo "==> ${project} (xunit.v3 major ${MAJOR})"
    dotnet test "${REPO_ROOT}/${project}" \
        --configuration Release \
        --nologo \
        "${PROPERTIES[@]}" | tee -a "${TEST_LOG}"

    # tee masks the exit status, so consult the pipeline's first element.
    status="${PIPESTATUS[0]}"
    [ "${status}" -eq 0 ] || exit "${status}"
done

# The same guard as scripts/coverage.sh: xUnit drops a test case whose unique ID collides with
# one already discovered, and the run stays green.
if grep -q "duplicate ID" "${TEST_LOG}"; then
    echo >&2
    echo "FAIL: xUnit skipped a test case with a duplicate unique ID. Tests were silently dropped." >&2
    grep "duplicate ID" "${TEST_LOG}" >&2
    exit 1
fi
