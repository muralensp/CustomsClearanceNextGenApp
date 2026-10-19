#!/usr/bin/env bash
# Secret scan (Day 43, ADR-0038) - gitleaks over the FULL git history.
#
# Portable: invoked identically by GitHub Actions and Azure Pipelines
# (ADR-0005). Uses a local `gitleaks` binary if one is on PATH, otherwise the
# pinned docker image (docker is present on ubuntu-latest runners and on the
# dev machine; no binary install needed).
#
# Scans a throwaway clone of the repo rather than the working directory:
#   - a git worktree's `.git` is a pointer file, which does not work when the
#     directory is mounted into a container;
#   - it makes the scan independent of untracked build output (bin/, obj/).
# Consequence: only COMMITTED content is scanned (which is what a PR contains).
# CI must therefore check out with fetch-depth: 0, or history is not scanned.
#
# Allowlist for the committed LOCAL-DEV-ONLY placeholders: .gitleaks.toml.
#
# Exit codes: 0 clean, 1 leaks found, 2 could not run the scanner (this is
# deliberately a failure too - a secret scan that silently did not run is
# worse than none).
set -euo pipefail

GITLEAKS_IMAGE="${GITLEAKS_IMAGE:-zricethezav/gitleaks:v8.21.2}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

echo "== Secret scan (gitleaks, full history) =="

if [ "$(git -C "$REPO_ROOT" rev-parse --is-shallow-repository)" = "true" ]; then
  echo "WARNING: shallow checkout - only the fetched history is scanned. Use fetch-depth: 0 in CI."
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
git clone --quiet --no-local "$REPO_ROOT" "$WORK/repo"
cp "$REPO_ROOT/.gitleaks.toml" "$WORK/gitleaks.toml"
mkdir -p "$REPO_ROOT/TestResults"

COMMON_ARGS=(detect --no-banner --redact --verbose
  --config "$WORK/gitleaks.toml"
  --report-format json --report-path "$WORK/report.json")

set +e
if command -v gitleaks >/dev/null 2>&1; then
  echo "(using local gitleaks binary: $(gitleaks version))"
  (cd "$WORK/repo" && gitleaks "${COMMON_ARGS[@]}" --source .)
  rc=$?
elif command -v docker >/dev/null 2>&1; then
  echo "(using docker image $GITLEAKS_IMAGE)"
  # pwd -W gives a Windows path under Git Bash; plain pwd elsewhere.
  to_host() { (cd "$1" && (pwd -W 2>/dev/null || pwd)); }
  HOST_WORK="$(to_host "$WORK")"
  MSYS_NO_PATHCONV=1 docker run --rm -v "$HOST_WORK:/work" "$GITLEAKS_IMAGE" \
    detect --no-banner --redact --verbose --source /work/repo \
    --config /work/gitleaks.toml --report-format json --report-path /work/report.json
  rc=$?
else
  echo "ERROR: neither gitleaks nor docker is available - secret scan could not run." >&2
  exit 2
fi
set -e

[ -f "$WORK/report.json" ] && cp "$WORK/report.json" "$REPO_ROOT/TestResults/gitleaks-report.json" || true

if [ "$rc" -eq 0 ]; then
  echo "Secret scan PASSED - no leaks found."
  exit 0
elif [ "$rc" -eq 1 ]; then
  echo "Secret scan FAILED - leaks found (values redacted above; see TestResults/gitleaks-report.json)." >&2
  exit 1
fi
echo "ERROR: scanner failed to run (exit $rc)." >&2
exit 2
