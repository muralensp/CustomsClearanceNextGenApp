# Runbook: run the system locally

Everything below was executed on a real stack (Windows 11, Git Bash, Docker Desktop 29.8, .NET SDK
10.0.401 building `net8.0`). See `Phase-1_Architecture_Engineering_Prep/Day44_Execution_Log.md` for the
evidence. Architecture background: [docs/Architecture/README.md](../Architecture/README.md).

## 1. Prerequisites

| Need | Why | Notes |
|---|---|---|
| Docker Desktop (Compose v2) | The dev environment is Docker Compose (ADR-0015); not local Kubernetes | Start Docker Desktop before anything else |
| .NET SDK 8 or newer | Build + unit/integration tests (projects target `net8.0`) | A newer SDK works; it was 10.0.x when this was verified |
| Git Bash (Windows) | The scripts in `build/scripts/` are bash | On Windows, run them from Git Bash |
| `curl` | The smoke/E2E scripts and the examples below | Ships with Git Bash |

Free host ports (defaults): 8080, 5432, 5672, 15672, 1025, 8025, 3000, 9090, 3100, 4317, 3200. If another
project on the machine already holds one of them (5432 has clashed before, Day 36), either stop that project
or use a slot (section 7).

No `.env` file, no secrets to create: every credential in the dev stack is a committed dev-only placeholder.
The JWT key can optionally be overridden with `JWT_KEY` in your shell (compose passes it to both services as
`Jwt__Key`); unset, the committed placeholder is used, which is accepted only because compose runs the
stack in Development.

## 2. Start the stack

From the repository root:

```bash
docker compose up -d --build
```

The first build takes several minutes (image builds + upstream pulls); later runs are fast.

**Expect this on a fresh volume:** the first `up` may exit non-zero with
`dependency failed to start: container ...-rabbitmq-1 is unhealthy`. That is the known RabbitMQ
first-boot race (ADR-0028); it was reproduced when verifying this runbook. Just run it again:

```bash
docker compose up -d
```

`restart: on-failure:5` has already restarted the broker; the second `up` succeeds. The Api also retries its
broker connection for up to 40 s, so `/health` can return 502 through the Gateway for roughly the first
half-minute after `up` returns. Poll instead of guessing:

```bash
for i in $(seq 1 40); do
  code=$(curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/health)
  [ "$code" = 200 ] && break
  sleep 3
done
echo "health=$code"
```

Check state: `docker compose ps`. Logs: `docker compose logs -f api` (JSON lines).

## 3. Stop the stack

```bash
docker compose down        # keep data (named volumes postgres-dev-data, rabbitmq-dev-data)
docker compose down -v     # also delete the data - a clean slate
```

The smoke and E2E scripts always run `down -v` when they finish.

## 4. URLs and ports (default slot 0)

| What | URL | Notes |
|---|---|---|
| Application entry (Gateway) | http://localhost:8080 | The only published app port |
| Gateway liveness | http://localhost:8080/__gateway/health | Gateway's own |
| Api health (via Gateway) | http://localhost:8080/health | |
| Swagger UI | http://localhost:8080/swagger | Development only, served by the Api through the Gateway |
| Mailpit inbox | http://localhost:8025 | Receives all notification email |
| RabbitMQ management | http://localhost:15672 | guest / guest (dev) |
| Grafana | http://localhost:3000 | No login (anonymous Admin, dev only). Explore: Loki, Prometheus, Tempo; dashboard "api-gateway-overview"; Alerting |
| Prometheus | http://localhost:9090 | |
| Loki ready | http://localhost:3100/ready | |
| Tempo ready | http://localhost:3200/ready | |
| PostgreSQL | `localhost:5432`, db `customsclearance_dev`, user `customsclearance`, password `devlocalonly` | Dev placeholder |

The Api host is **not** reachable directly (the `api` service in `docker-compose.yml` defines no `ports:`); `http://localhost:8080` is the Gateway.

## 5. Dev personas (DEV-ONLY)

Seeded into the `Users` table by `src/Api/Identity/IdentitySeeder.cs` when the Identity module runs in
Development (only if the table is empty). These are committed placeholders, never real credentials, and
must not exist in any non-dev environment.

| Username | Password | Role |
|---|---|---|
| `broker` | `Broker-Dev-Pw-123!` | Broker |
| `supervisor` | `Supervisor-Dev-Pw-123!` | Supervisor |
| `compliance` | `Compliance-Dev-Pw-123!` | ComplianceOfficer |
| `admin` | `Admin-Dev-Pw-123!` | Admin |

All four logins returned HTTP 200 when verified.

Get a token and call something:

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"Admin-Dev-Pw-123!"}' | grep -o '"accessToken":"[^"]*"' | cut -d'"' -f4)

curl -s http://localhost:8080/api/reference/countries/IN -H "Authorization: Bearer $TOKEN"   # 200, placeholder data
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8080/api/config                    # 401 without a token
curl -s http://localhost:8080/api/config -H "Authorization: Bearer $TOKEN"                   # 200 (Admin)
```

Role per route is listed in the module table of the architecture overview. Swagger UI can also be used:
log in, then paste the token into "Authorize".

The personas and the login flow did not change in Day 41. The only change that affects local work: the audit
table is now accessed through a restricted `audit_writer` database role, created automatically by the
Development bootstrap, so if you run the Api outside compose against your own Postgres, set both
`ConnectionStrings:AuditDb` (role `audit_writer`) and `ConnectionStrings:AuditAdminDb` (the owner login), as
`src/Api/appsettings.Development.json` does.

## 6. Tests

All commands from the repository root. Run each unpiped and read the exit code directly (a piped command
reports the pipe's exit status, not the command's).

| What | Command | Needs the stack? | Verified result |
|---|---|---|---|
| Build | `dotnet build CustomsClearance.sln` (or `bash build/scripts/build.sh`) | No | exit 0; 2 warnings, both NU1902 on `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.9.0 (known, ADR-0022) |
| Unit tests + coverage gates | `bash build/scripts/test.sh` | No | exit 0, 12 projects, all passed; aggregate line 98.7%, branch 95.2% |
| One unit project | `dotnet test tests/Unit/Platform.RulesEngine.Tests` | No | enforces that project's own threshold (ADR-0007) |
| Integration tests | `bash build/scripts/integration-test.sh` | No - in-process, `TestApiFactory` blanks all connection strings | exit 0, 14 passed |
| Lint | `bash build/scripts/lint.sh` (`dotnet format --verify-no-changes --severity warn`) | No | see the CRLF caveat below |
| Container smoke test | `bash build/scripts/smoke-test.sh` | Builds and starts its own stack, then `down -v` | exit 0 |
| End-to-end smoke test | `bash build/scripts/e2e-smoke-test.sh` | Builds and starts its own stack, then `down -v` | exit 0, 20/20 PASS; evidence in `TestResults/e2e-evidence.txt` (override with `E2E_EVIDENCE_FILE`) |
| API contract tests | `bash build/scripts/contract-test.sh` | Starts its own stack; **hard-coded to `localhost:8080`**, so slot 0 only | not run for this runbook |

Notes:

- The smoke and E2E scripts start and destroy their own stack. Do not run them while you are using a stack
  you care about in the same compose project (their `down -v` deletes its data).
- **Lint caveat (Windows):** if your checkout converted line endings to CRLF (`core.autocrlf=true`, no
  `.gitattributes`), `dotnet format --verify-no-changes` reports an `ENDOFLINE` error on essentially every
  file because `.editorconfig` requires LF. Observed in a fresh git worktree. The meaningful checks still
  pass: `dotnet format style CustomsClearance.sln --verify-no-changes` and
  `dotnet format analyzers CustomsClearance.sln --verify-no-changes` both exit 0. Do not pass `--nologo`
  to `dotnet format`.
- Coverage thresholds: 95% line for core logic (rules engine, security, audit, messaging...), 80% for
  CRUD/master-data libraries; a failing gate fails `dotnet test` for that project.

## 7. Parallel slots (several stacks on one machine)

`build/scripts/agent-env.sh <slot>` prints the environment for an isolated copy of the stack: compose project
`cc-slot-N`, image prefix `cc-slot-N`, and every host port shifted by N x 1000 (SMTP by N). Slot 0 = the
defaults. Full rules: [docs/PARALLEL-WORK.md](../PARALLEL-WORK.md).

```bash
eval "$(bash build/scripts/agent-env.sh 3)"     # slot 3: gateway on 11080, Mailpit UI on 11025, ...
docker compose up -d --build
bash build/scripts/e2e-smoke-test.sh             # talks to slot 3's gateway/mailpit
```

Slot 3 ports: gateway 11080, Postgres 8432, RabbitMQ 8672 / mgmt 18672, Mailpit SMTP 1028 / UI 11025,
Grafana 6000, Prometheus 12090, Loki 6100, Tempo OTLP 7317 / HTTP 6200. Shell state does not persist between
tool calls in some agent harnesses; set the variables in the same command as `docker compose`. (When this
runbook was verified from an agent harness that blocks `eval "$(...)"`, the variables were exported
explicitly instead, with identical values.)

Limit: a full stack is ~12 containers; two or three at once is the practical ceiling. Unit and integration
tests need no stack and parallelise freely.

## 8. Run the Api outside Docker (optional)

`src/Api/appsettings.Development.json` points at `localhost` for Postgres (5432), RabbitMQ (5672), SMTP
(1025) and the OTLP endpoint (4317), so you can run just the infrastructure and debug the Api directly:

```bash
docker compose up -d postgres rabbitmq mailpit tempo
dotnet run --project src/Api --launch-profile http   # listens on http://localhost:5278 per launchSettings.json
```

This path was not exercised when writing this runbook (the compose path was). It only works with the
default (slot 0) ports.

## 9. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `docker compose up` ends with `rabbitmq ... is unhealthy` / `.erlang.cookie: eacces` in `docker compose logs rabbitmq` | One-time first-boot race on a brand-new volume (ADR-0028). Reproduced during verification | Run `docker compose up -d` again. If it persists across fresh volumes, the local image may be corrupted: `docker rmi -f rabbitmq:3.13-management-alpine` and pull again (Day 33). Also look for stray/orphaned containers or volumes from other projects (Days 26-28) |
| `/health` through the Gateway returns 502 shortly after `up` | The Api is still retrying the RabbitMQ connection (healthcheck passes before AMQP accepts connections) | Wait; poll as in section 2 (up to 40 s of retry) |
| `bind: address already in use` for 5432 (or 8080, ...) | Another project holds the port (`saferte_postgres` did, Day 36) | Stop it, or use a slot (section 7), or export `POSTGRES_PORT=...` etc. for a one-off override. Do not edit the committed compose file |
| Scripts report success but something failed | Exit code of a piped command is the pipe's, not the command's | Never `cmd \| tail`; capture `$?` directly |
| Windows: a test/env override "does nothing" | `Environment.SetEnvironmentVariable(name, "")` deletes the variable on Windows | Use a single space as the "blank" value (this is what `TestApiFactory` does) |
| Whole Api crashes at start-up with a DI/bindability error after adding an endpoint | Minimal API validates every endpoint's parameters at route-map time | Guard the route mapping with the same condition as the service registration (see ADD-A-MODULE.md) |
| Integration test starts failing with long timeouts after adding a DbContext | New connection string not blanked in `TestApiFactory` | Add `Environment.SetEnvironmentVariable("ConnectionStrings__<X>Db", " ")` there |
| "relation ... does not exist" for a later module's table | Table creation order / `EnsureCreated` only creates the database | Keep `Diagnostics` (Order 10) first; use `DatabaseBootstrap.EnsureSchemaAsync` (Day 23) |
| Tables look stale after a model change | No migrations: tables are created once and never altered | `docker compose down -v` to recreate the database |
| Grafana shows alerts but nothing is delivered | No contact point is provisioned by design (ADR-0023) | Look at state in Grafana Alerting |
| Dashboard/Explore empty right after `up` | Promtail/Prometheus need a scrape cycle (15 s) and some traffic | Send a few requests, wait one scrape interval |
| Expired token (401 after a while) | Tokens are time-limited (`expiresAtUtc` in the login response) | Log in again |
| Lint: `ENDOFLINE` on every file | CRLF checkout, see section 6 | Use the `style` and `analyzers` subcommands, or normalise line endings |

If anything differs from this document, the document is wrong: fix it and note it in the day's execution log.
