#!/usr/bin/env bash
# Portable test script - invoked identically by GitHub Actions today and
# Azure Pipelines later.
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

echo "== Test (with coverage gate) =="
dotnet test --configuration Release \
  --logger "trx;LogFileName=test-results.trx" \
  --results-directory ./TestResults

echo "== Coverage Report =="
dotnet tool restore
dotnet reportgenerator \
  -reports:"**/TestResults/**/coverage.cobertura.xml" \
  -targetdir:"./TestResults/CoverageReport" \
  -reporttypes:"Html;TextSummary" || echo "No coverage files found yet - skipping report (expected until more test projects exist)."

if [ -f "./TestResults/CoverageReport/Summary.txt" ]; then
  cat ./TestResults/CoverageReport/Summary.txt
fi
