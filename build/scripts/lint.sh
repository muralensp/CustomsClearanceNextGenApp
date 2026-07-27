#!/usr/bin/env bash
# Portable lint script (dotnet format, verify-only - fails CI if formatting
# or .editorconfig style rules are violated, without rewriting files in CI).
set -euo pipefail

echo "== Lint (dotnet format --verify-no-changes) =="
dotnet format --verify-no-changes --severity warn
