# Day 44 Execution Log - Documentation Pass (skeleton phase)

Worktree branch `worktree-agent-ad6721d82808d6bd5`, base 27acf47. Compose slot 3. Not committed (per the
coordinator's later instruction). ADR-0039 used for the documentation-structure decision.

## What was done

- `docs/Architecture/README.md`, `docs/C4/README.md`, `docs/runbooks/{RUN-LOCALLY,ADD-A-MODULE,DEPLOY}.md`
  written from the code/config (compose, both `Program.cs`, modules, Dockerfiles, scripts, observability).
- `adr/0039-phase-0-documentation-structure.md`.
- No product code left changed; CLAUDE.md untouched.

## Commands and results (all unpiped; slot 3 environment variables exported explicitly)

Note: the harness refused `eval "$(bash build/scripts/agent-env.sh 3)"`, `.`-sourcing and heredocs, so the
exact values the script prints for slot 3 were exported by hand in the same command as `docker compose`.

| Command | Exit | Result |
|---|---|---|
| `docker compose up -d --build` (slot 3, fresh) | 1 | `rabbitmq ... is unhealthy`, log shows `.erlang.cookie: eacces` (known first-boot race) |
| `docker compose up -d` | 0 | all 10 services running; `/health` 502 for ~30 s then 200 (Api retrying AMQP) |
| URL probes via curl | - | gateway health/`/health`/swagger 200, Mailpit 200, RabbitMQ mgmt 200, Grafana 200, Prometheus 200, Loki 200, Tempo 200, `/metrics` 200 |
| 4 persona logins | - | all 200; no-token `/api/config` 401; admin 200; `/api/reference/countries/IN` 200 |
| `dotnet build CustomsClearance.sln` | 0 | 2 warnings (NU1902, known) |
| `bash build/scripts/integration-test.sh` | 0 | 14 passed |
| `bash build/scripts/test.sh` | 0 | 12 unit projects passed; line 98.7%, branch 95.2% |
| `docker compose down -v`, then `bash build/scripts/e2e-smoke-test.sh` (E2E_EVIDENCE_FILE set) | 0 | 20/20 PASS |
| `bash build/scripts/smoke-test.sh` | 0 | all sections passed; no containers left |
| `dotnet format CustomsClearance.sln --verify-no-changes` | 2 | ENDOFLINE on every file: worktree files are CRLF, `.editorconfig` wants LF |
| `dotnet format style ... --verify-no-changes` / `analyzers ...` | 0 / 0 | clean (including the scratch files) |

### ADD-A-MODULE verification (throwaway `Scratch` module)

Added `src/Platform/Scratch` (entity, `PlatformDbContext`, service), `tests/Unit/Platform.Scratch.Tests`,
`src/Api/Modules/ScratchModule.cs` (Order 80, `ScratchDb`), plus the shared-file wiring from the runbook.

| Step | Exit | Result |
|---|---|---|
| `dotnet sln ... add` (2 projects); `dotnet add src/Api/... reference` | 0 / 0 | ok |
| Integration tests with module present but catalog test not yet updated | 1 | `ApiModuleCatalogTests.Discover_FindsEveryExistingCapability` failed, proving discovery found the module without editing `Program.cs` |
| After wiring Dockerfile, compose, appsettings, `TestApiFactory`, catalog test: `dotnet build` | 0 | 2 known warnings |
| `dotnet test tests/Unit/Platform.Scratch.Tests` | 0 | 2 passed, coverage gate met |
| `dotnet test tests/Integration/Api.IntegrationTests` | 0 | 15 passed (incl. new `/api/scratch` not-mapped case) |
| Live stack: `up --build` (1), `up` (0) | 1 / 0 | rabbit race again, then healthy |
| `/api/scratch` no token / POST with broker token / GET | - | 401 / 200 `{"id":..,"name":"hello"}` / 200 list with the item |
| `docker compose down -v` | 0 | |
| Removal: `git checkout --` 7 shared files; `rm -rf` module, library, tests | 0 / 0 | `git status --short` showed only the docs; `git diff --stat` empty; no `scratch` dirs under `src/Platform` or `tests/Unit` |

## What differed from the first draft

- Stack needs a second `up` on a fresh volume (RabbitMQ race) and ~30 s before `/health` is 200; both added
  to RUN-LOCALLY.
- The catalog test list and the "route not mapped" theory must be edited when adding a module; added to the
  runbook as step 4 (first draft omitted it).
- Lint instructions changed: plain `dotnet format --verify-no-changes` is unusable on a CRLF checkout.
- Draft claimed "Api port gave no response"; replaced with the verifiable fact that the `api` service has no
  `ports:`. Mermaid semicolons in sequence-diagram text and "HTTPS in a real deployment" (a guess) removed.
- Compose has 10 services, not 11 (DEPLOY.md corrected).

## Not verified

Running the Api with `dotnet run` outside Docker (RUN-LOCALLY section 8); `contract-test.sh` (slot 0 only);
event-handling and audit steps of ADD-A-MODULE; deliberately omitting each shared-file edit (only the
catalog-test failure was observed); Mermaid rendering on GitHub (syntax reviewed by eye only).

## RECONCILE markers left (`RECONCILE-AFTER-41-43`)

1. `docs/Architecture/README.md` section 2: Gateway response headers / route policies (Day 41)
2. `docs/Architecture/README.md` section 4: audit-store DB role / append-only enforcement (Day 41)
3. `docs/Architecture/README.md` section 9: performance baseline (Day 42)
4. `docs/Architecture/README.md` section 10: security posture, JWT key handling (Day 41)
5. `docs/Architecture/README.md` section 11: CI/CD scans, aggregate job, branch protection (Day 43)
6. `docs/C4/README.md` end: container/trust-boundary changes (Days 41/43)
7. `docs/runbooks/DEPLOY.md` after section 1: CI/CD facts (Day 43)
8. `docs/runbooks/DEPLOY.md` section 2: security deployment requirements (Day 41)
9. `docs/runbooks/DEPLOY.md` section 2: performance/capacity (Day 42)
10. `docs/runbooks/RUN-LOCALLY.md` section 5: dev JWT key/personas/login changes (Day 41)

## Stale or incorrect existing docs found (not changed)

- `README.md`: "Dev Environment: docker-compose.yml (Api + Postgres)" and the ADR list stopping at 0009; repo
  structure omits Gateway and the Platform libraries added since.
- `CLAUDE.md` Day 22 text says the gateway performs "no auth", superseded by Day 25 (history, not wrong);
  Day 24 says retry 10 x 2 s, later 20 x 2 s (Day 25 entry notes it). Day 18 verification says "7 projects"
  for unit tests; there are now 12. Day 20 says "all 17 contexts empty" which remains true.
- `src/Api/Program.cs` header comment says Platform.Audit etc. wired "by day" - fine; but the Api
  `appsettings.json` has no `Jwt` section (only Development), relevant to deployment, noted in DEPLOY.md.
- `docs/PARALLEL-WORK.md` suggests `eval "$(...)"`, which some agent harnesses block.
- `build/scripts/contract-test.sh` hard-codes `localhost:8080` and `down -v` (already documented there).
- `tests/Integration/.../TestApiFactory.cs` and `ApiModuleCatalogTests.cs` need edits per new module; not
  mentioned in PARALLEL-WORK.md's shared-file list for the catalog test.
- Build now reports 2 NU1902 warnings; older logs claim "0 warnings".

## ADR

ADR-0039 used (documentation structure and the "service" to "module" reinterpretation).
