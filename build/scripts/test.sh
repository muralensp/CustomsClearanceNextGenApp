#!/usr/bin/env bash
# Portable test script - invoked identically by GitHub Actions today and
# Azure Pipelines later. Coverage thresholds enforced in Phase 0 Day 12
# (Code Scanning & Coverage Gates) - this script just runs tests for now.
set -euo pipefail

echo "== Test =="
dotnet test --no-build --configuration Release --logger "trx;LogFileName=test-results.trx" --results-directory ./TestResults
