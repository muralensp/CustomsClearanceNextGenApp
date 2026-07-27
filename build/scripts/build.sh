#!/usr/bin/env bash
# Portable build script - invoked identically by GitHub Actions today and
# Azure Pipelines later (per user decision, Day 11). No CI-platform-specific
# logic here; the CI YAML files only orchestrate calling these scripts.
set -euo pipefail

echo "== Restore =="
dotnet restore

echo "== Build =="
dotnet build --no-restore --configuration Release
