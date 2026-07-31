#!/usr/bin/env bash
# Portable UNIT test script - invoked identically by GitHub Actions today
# and Azure Pipelines later.
#
# Scoped to tests/Unit/** only (Day 14) - integration tests
# (tests/Integration/**) run separately via integration-test.sh, so a
# slower or docker/DB-dependent integration test never blocks or dilutes
# the fast unit+coverage feedback loop, and so coverage thresholds (which
# are a unit-test-coverage policy per the Phase-wise Plan v2, not an
# integration-test policy) are only measured against the layer they're
# meant to apply to.
#
# Coverage collection and threshold enforcement (Day 12) are declared per
# test project (see each *.csproj's <CollectCoverage>/<Threshold>/...
# properties, per the classification convention in
# adr/0007-static-analysis-and-coverage-gates.md) - this script does not
# hardcode a single global threshold, since core business/rules logic
# (95%) and CRUD master-data services (80%) carry different bars.
#
# dotnet test exits non-zero if any project's coverage falls below its own
# declared threshold (coverlet.msbuild), which is what makes this a real
# gate rather than a report.
#
# IMPORTANT: do NOT pass --no-build here. coverlet.msbuild hooks its
# instrumentation into dotnet test's own build step; skipping that build
# (even if build.sh already built the solution) silently produces an
# empty/misleading coverage report (all packages present, zero classes
# tracked) instead of failing loudly. Verified locally (Day 12) - this is
# not a hypothetical concern.
set -euo pipefail

echo "== Unit Tests (with coverage gate) =="
for proj in tests/Unit/*/*.csproj; do
  echo "--- Testing: $proj ---"
  dotnet test "$proj" --configuration Release \
    --logger "trx;LogFileName=$(basename "$proj" .csproj).trx" \
    --results-directory ./TestResults
done

echo "== Coverage Report =="
dotnet tool restore
dotnet reportgenerator \
  -reports:"**/TestResults/**/coverage.cobertura.xml" \
  -targetdir:"./TestResults/CoverageReport" \
  -reporttypes:"Html;TextSummary" || echo "No coverage files found yet - skipping report (expected until more test projects exist)."

if [ -f "./TestResults/CoverageReport/Summary.txt" ]; then
  cat ./TestResults/CoverageReport/Summary.txt
fi
