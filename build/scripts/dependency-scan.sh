#!/usr/bin/env bash
# Dependency / SCA scan (Day 43, ADR-0038; ADR-0004 item 16, ADR-0007).
#
# Runs `dotnet list <sln> package --vulnerable --include-transitive` (NuGet's
# own advisory feed - GitHub Advisory Database data) and parses the JSON.
#   Critical / High  -> FAIL (exit 1)
#   Moderate / Low   -> visible WARNING, does not fail
#   unknown severity -> treated as High (fail closed)
#
# Fail closed on a broken scan too (exit 2): if NuGet could not fetch the
# advisory data (NU1900, offline, feed down) a "clean" result would be false
# assurance, so the scan refuses to report clean.
#
# Documented, time-boxed exceptions: build/dependency-scan-allowlist.txt, one
# per line:  GHSA-xxxx-xxxx-xxxx | YYYY-MM-DD expiry | reason
# An expired exception stops applying (and is reported), so exceptions cannot
# silently become permanent. Moderate/Low need no exception.
#
# Portable: no CI-platform logic (ADR-0005). Needs dotnet and python3/python.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"
SOLUTION="${SOLUTION:-CustomsClearance.sln}"
ALLOWLIST="${ALLOWLIST:-build/dependency-scan-allowlist.txt}"

PY=""
for c in python3 python; do
  if command -v "$c" >/dev/null 2>&1 && "$c" -c 'import sys; sys.exit(0)' >/dev/null 2>&1; then PY="$c"; break; fi
done
if [ -z "$PY" ]; then echo "ERROR: python3/python not found - cannot parse scan output." >&2; exit 2; fi

mkdir -p TestResults
OUT="TestResults/dependency-scan.json"
ERR="TestResults/dependency-scan.stderr.txt"

echo "== Dependency scan (dotnet list package --vulnerable --include-transitive) =="
dotnet restore "$SOLUTION" >/dev/null
set +e
dotnet list "$SOLUTION" package --vulnerable --include-transitive --format json >"$OUT" 2>"$ERR"
list_rc=$?
set -e
if [ "$list_rc" -ne 0 ]; then
  echo "ERROR: 'dotnet list package' exited $list_rc - scan did not run." >&2
  cat "$ERR" >&2 || true
  exit 2
fi

"$PY" - "$OUT" "$ERR" "$ALLOWLIST" <<'PYEOF'
import json, sys, os, re, datetime

out_path, err_path, allow_path = sys.argv[1:4]
raw = open(out_path, encoding="utf-8").read()
err = open(err_path, encoding="utf-8").read() if os.path.exists(err_path) else ""

if "NU1900" in raw or "NU1900" in err:
    print("ERROR: NuGet could not retrieve vulnerability data (NU1900) - scan is not trustworthy.", file=sys.stderr)
    sys.exit(2)
try:
    data = json.loads(raw)
except Exception as e:
    print(f"ERROR: could not parse dotnet output as JSON: {e}", file=sys.stderr)
    sys.exit(2)
projects = data.get("projects")
if not projects:
    print("ERROR: scan reported no projects - nothing was scanned.", file=sys.stderr)
    sys.exit(2)

# --- allowlist ---------------------------------------------------------
today = datetime.date.today()
allowed, expired = {}, []
if os.path.exists(allow_path):
    for line in open(allow_path, encoding="utf-8"):
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split("|", 2)]
        if len(parts) != 3 or not re.fullmatch(r"GHSA-[0-9a-z-]+", parts[0], re.I):
            print(f"ERROR: malformed allowlist line (want 'GHSA-... | YYYY-MM-DD | reason'): {line}", file=sys.stderr)
            sys.exit(2)
        exp = datetime.date.fromisoformat(parts[1])
        if exp < today:
            expired.append((parts[0], parts[1]))
        else:
            allowed[parts[0].upper()] = (parts[1], parts[2])

# --- collect -------------------------------------------------------------
findings = {}   # (id, version, severity, advisory) -> set(projects)
for p in projects:
    name = os.path.basename(p.get("path", "?"))
    for fw in p.get("frameworks", []):
        for key in ("topLevelPackages", "transitivePackages"):
            for pkg in fw.get(key, []):
                for v in pkg.get("vulnerabilities", []):
                    k = (pkg["id"], pkg["resolvedVersion"], v.get("severity", "Unknown"), v.get("advisoryurl", ""))
                    findings.setdefault(k, set()).add(name)

blocking, warnings, excepted = [], [], []
for (pid, ver, sev, url), projs in sorted(findings.items()):
    ghsa = url.rsplit("/", 1)[-1].upper()
    entry = (pid, ver, sev, url, sorted(projs))
    if sev in ("Moderate", "Low"):
        warnings.append(entry)
    elif ghsa in allowed:
        excepted.append(entry + (allowed[ghsa],))
    else:
        blocking.append(entry)   # High, Critical, anything unknown

def show(title, rows):
    print(f"\n{title} ({len(rows)})")
    for r in rows:
        print(f"  [{r[2]}] {r[0]} {r[1]}  {r[3]}")
        print(f"      in: {', '.join(r[4][:6])}{' ...' if len(r[4]) > 6 else ''}")
        if len(r) > 5:
            print(f"      exception until {r[5][0]}: {r[5][1]}")

print(f"Scanned {len(projects)} projects.")
for gid, exp in expired:
    print(f"NOTE: allowlist entry {gid} expired on {exp} and no longer applies.")
show("WARNING - Moderate/Low advisories (do not fail the build)", warnings)
show("EXCEPTED - High/Critical with a documented, unexpired exception", excepted)
show("BLOCKING - High/Critical advisories", blocking)
if blocking:
    print("\nDependency scan FAILED.", file=sys.stderr)
    sys.exit(1)
print("\nDependency scan PASSED (no un-excepted High/Critical advisories).")
PYEOF
