#!/usr/bin/env bash
# Portable integration test script (Day 14) - invoked identically by
# GitHub Actions today and Azure Pipelines later.
#
# Scoped to tests/Integration/** only. These tests spin up the real Api
# host in-process via WebApplicationFactory (no docker needed for this
# layer) and exercise the actual HTTP pipeline. No coverage threshold is
# enforced here - the 95%/80% coverage policy (ADR-0007) is a unit-test
# policy; integration tests prove behavior across layers, not line
# coverage of a specific project.
set -euo pipefail

echo "== Integration Tests =="
for proj in tests/Integration/*/*.csproj; do
  echo "--- Testing: $proj ---"
  dotnet test "$proj" --configuration Release \
    --logger "trx;LogFileName=$(basename "$proj" .csproj).trx" \
    --results-directory ./TestResults
done
