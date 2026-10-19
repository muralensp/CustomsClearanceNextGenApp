# Day 41 Execution Log - Security Hardening

Branch worktree: `worktree-agent-a62c45bbd7b94104c` (base 27acf47). Compose slot 1 (`cc-slot-1`). Not committed.
Decisions: `adr/0036-security-hardening.md`. Checklist: `docs/security/PHASE0_SECURITY_CHECKLIST.md`.

## What was done

1. Read the Day 10 standard (extracted from the .docx), ADR-0004/0011/0017/0019/0031/0032/0001, Gate 1/2.
2. JWT key hardening: `Platform.Security/JwtKeyValidator.cs`, optional `IHostEnvironment` on both auth
   registrations, call sites in `src/Api/Program.cs` and `src/Gateway/Program.cs`, `JWT_KEY` env in compose.
3. Duplicate `X-Correlation-Id`: `CorrelationIdMiddleware` sets the response header in `OnStarting`.
4. Audit immutability: `AuditStoreGrants` (library), `AuditModule` bootstrap (owner connection, role,
   grants), `AuditAdminDb` setting, compose + appsettings + `TestApiFactory` updated, assertion added to
   `smoke-test.sh`.
5. NU1902: OpenTelemetry packages 1.9.0 to 1.15.x.
6. Notification PII: redaction of the address in failure text/logs.
7. Dockerfiles default to `Production`.
8. Checklist, ADR, this log.

## Problems found

- Both Dockerfiles baked in `ASPNETCORE_ENVIRONMENT=Development`: a deployed image would have run with the
  placeholder key, seeded users and Swagger. Fixed (the validator alone would not have helped).
- OpenTelemetry 1.11.2 (first attempt) is still flagged NU1902; the fix is 1.15.3. The whole set moved
  without breakage.
- `AuditStoreGrants` needed `Microsoft.EntityFrameworkCore.Relational` (provider-agnostic) for the table name
  and raw SQL.
- Sonar S6605 (`Any` on an array) and S6667 (log in catch without exception) fixed/suppressed with a
  justification respectively.
- `dotnet format --verify-no-changes` exits 2 in this worktree **only** from ENDOFLINE on files that were
  not touched (the checkout has CRLF while `.editorconfig` wants LF; e.g. `PlaceholderModuleMarker.cs`).
  Zero non-ENDOFLINE findings. A fresh checkout should be re-checked by the integrator.
- Tooling note: the sandbox refused `eval "$(...agent-env.sh 1)"`, so the slot variables were set from a
  scratchpad file instead; values are identical to `agent-env.sh 1` output.
- RabbitMQ first-boot race hit once in the first smoke run and once in the final one; the known retry
  (ADR-0028) recovered both.

## Evidence

All exit codes captured directly, unpiped.

| Command | Exit | Key numbers |
|---|---|---|
| `dotnet build CustomsClearance.sln` | 0 | 0 warnings, 0 errors (NU1902 gone) |
| `dotnet format CustomsClearance.sln --verify-no-changes` | 2 | all 8,677 findings are ENDOFLINE on the CRLF checkout; 0 other |
| `dotnet test CustomsClearance.sln` | 0 | 13 test assemblies, 0 failures; Security 18 tests 95.09% line/85% branch; Logging 8 tests 100%/100%; Audit.Persistence 19 tests 98.67%/91.89%; Notifications 20 tests 99.38%/95%; Api.IntegrationTests 14 passed |
| `bash build/scripts/smoke-test.sh` (slot 1, fresh volumes) | 0 | includes new "Audit immutability verified" step |
| `bash build/scripts/e2e-smoke-test.sh` (slot 1) | 0 | 20/20, evidence `TestResults/e2e-evidence-w-a.txt` (`result=PASS`, `checks_passed=20`); check 7 now also asserts exactly one `X-Correlation-Id` response header |

Live proofs (slot 1 stack):

- Role created and limited: log `Audit store: role audit_writer limited to INSERT and SELECT on the audit
  table.` (also after an `api` restart, so the bootstrap is idempotent). `\dp "AuditEvents"` shows
  `customsclearance=arwdDxt/customsclearance` and `audit_writer=ar/customsclearance`. `audit_writer` has
  rolsuper/createdb/createrole all false.
- psql as `audit_writer`: `select count(*)` exit 0; manual `INSERT` exit 0 (`INSERT 0 1`);
  `UPDATE`, `DELETE`, `TRUNCATE` each `ERROR: permission denied for table AuditEvents`, exit 1;
  `select ... from "Users"` also denied. A config create through the Gateway wrote its audit event, and
  `GET /api/audit` as the compliance officer returned it (the app works with the restricted role).
- Correlation: `curl -i` through the Gateway on `/health` returned exactly one `X-Correlation-Id` (both with a
  supplied id, echoed as `d41-hdr`, and generated).
- JWT: gateway image, `ASPNETCORE_ENVIRONMENT=Production`: no key supplied (placeholder from appsettings)
  fails with the placeholder message; `Jwt__Key=short` fails with the length message; a strong key starts
  and listens on 8080. Api image in Production with no key fails with "Jwt:Key is required".
  `docker compose config` with `JWT_KEY` set shows the override on both services.
- OpenTelemetry 1.15.x: Prometheus targets `customsclearance-api` and `customsclearance-gateway` both `up`;
  `http_server_request_duration_seconds_count` returned 9 series; a Tempo search by `correlation_id` found
  the trace for a request sent through the Gateway.

## Not proven / open

- Not re-run live after the final code change: a manual check of `/metrics` reachability through the
  Gateway (checklist 7.5 is from reading the code), and the "before" state of the duplicate header.
- Docker images were rebuilt by the smoke/E2E runs after the Dockerfile change; the E2E evidence is from
  that build.
- Items needing a decision, CI changes and production-only requirements are in the checklist summary.
