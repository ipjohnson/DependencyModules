#!/usr/bin/env bash
#
# Runs every test project with coverage collection and merges the results into one report.
#
# Outputs (under artifacts/coverage):
#   Summary.txt / SummaryGithub.md  human-readable summaries
#   badge_linecoverage.svg          the badge published to the badges branch by CI
#   Cobertura.xml                   merged report
#   index.html                      browsable HTML report
#
# Usage:
#   scripts/coverage.sh              report only
#   scripts/coverage.sh 70           report and fail below 70% line coverage

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
THRESHOLD="${1:-0}"
OUT="${REPO_ROOT}/artifacts/coverage"
RAW="${REPO_ROOT}/artifacts/coverage-raw"

rm -rf "${OUT}" "${RAW}"
mkdir -p "${OUT}" "${RAW}"

# Every suite contributes: the integration projects exercise a great deal of the generator
# that the unit tests never reach directly.
PROJECTS=(
    "tests/DependencyModules.Tests/DependencyModules.Tests.csproj"
    "integ-tests/SutProject.Tests/SutProject.Tests.csproj"
    "integ-tests/web/WebApiApp.Tests/WebApiApp.Tests.csproj"
)

TEST_LOG="${RAW}/test-output.log"

for project in "${PROJECTS[@]}"; do
    echo "==> ${project}"
    dotnet test "${REPO_ROOT}/${project}" \
        --configuration Release \
        --collect:"XPlat Code Coverage" \
        --settings "${REPO_ROOT}/tests/coverlet.runsettings" \
        --results-directory "${RAW}" \
        --nologo | tee -a "${TEST_LOG}"

    # tee masks the exit status, so consult the pipeline's first element.
    status="${PIPESTATUS[0]}"
    [ "${status}" -eq 0 ] || exit "${status}"
done

# xUnit drops a test case whose unique ID collides with one already discovered, and it does so
# without failing the run: the suite stays green while tests quietly stop executing. Promote that
# warning to a build failure so it can never pass unnoticed.
if grep -q "duplicate ID" "${TEST_LOG}"; then
    echo >&2
    echo "FAIL: xUnit skipped a test case with a duplicate unique ID. Tests were silently dropped." >&2
    grep "duplicate ID" "${TEST_LOG}" >&2
    exit 1
fi

if ! command -v reportgenerator >/dev/null 2>&1; then
    echo "==> Installing reportgenerator"
    dotnet tool install --global dotnet-reportgenerator-globaltool >/dev/null
    export PATH="${PATH}:${HOME}/.dotnet/tools"
fi

echo "==> Merging coverage"
# Only the shipping libraries count. The SutProject/WebApiApp fixtures under integ-tests exist to
# be compiled by the generator, and CSharpAuthor is a vendored third-party dependency.
reportgenerator \
    "-reports:${RAW}/**/coverage.cobertura.xml" \
    "-targetdir:${OUT}" \
    "-reporttypes:Html;Cobertura;TextSummary;MarkdownSummaryGithub;Badges" \
    "-title:DependencyModules" \
    "-assemblyfilters:+DependencyModules.Runtime;+DependencyModules.SourceGenerator;+DependencyModules.xUnit;+DependencyModules.xUnit.NSubstitute" \
    "-classfilters:-CSharpAuthor.*" \
    >/dev/null

cat "${OUT}/Summary.txt"

LINE_RATE="$(python3 - "${OUT}/Cobertura.xml" <<'EOF'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
print(round(float(root.get('line-rate', 0)) * 100, 1))
EOF
)"

echo
echo "Line coverage: ${LINE_RATE}%"

if [ "${THRESHOLD}" != "0" ]; then
    if python3 -c "import sys; sys.exit(0 if float('${LINE_RATE}') >= float('${THRESHOLD}') else 1)"; then
        echo "Meets the ${THRESHOLD}% threshold."
    else
        echo "FAIL: line coverage ${LINE_RATE}% is below the ${THRESHOLD}% threshold." >&2
        exit 1
    fi
fi
