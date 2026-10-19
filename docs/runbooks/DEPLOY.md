# Runbook: deployment - what exists, and what does not

**There is no deployment procedure, because there is no deployment target.** No cloud provider, hosting
platform, container registry, managed database/broker, DNS, TLS or secrets store has been chosen (ADR-0008
deferred it to Day 21; ADR-0015 deferred it again). Nothing is hosted anywhere: the only running environment
is a developer's (or a CI job's) Docker Compose stack. This document lists what the repository does provide,
what a deployment would additionally need, and the decisions that are still open. It deliberately does not
invent steps.

Local operation: [RUN-LOCALLY.md](RUN-LOCALLY.md). Architecture: [../Architecture/README.md](../Architecture/README.md).

## 1. What exists

| Artifact | Where | What it is | Caveat |
|---|---|---|---|
| Api image definition | `src/Api/Dockerfile` | Multi-stage: `mcr.microsoft.com/dotnet/sdk:8.0` build, `aspnet:8.0` runtime; listens on `:8080` | Defaults `ENV ASPNETCORE_ENVIRONMENT=Production` since Day 41; compose overrides it to `Development` (see section 3) |
| Gateway image definition | `src/Gateway/Dockerfile` | Same pattern | Same `Production` default |
| Dev environment definition | `docker-compose.yml` | 10 services: gateway, api, postgres, rabbitmq, mailpit, loki, promtail, grafana, prometheus, tempo (plus 2 named volumes) | Dev defaults and committed placeholder credentials throughout; this is not a production topology |
| CI "containerize" job | `.github/workflows/ci.yml` job *Containerize + Dev Environment Smoke Test* (runs `build/scripts/smoke-test.sh`) | Builds both images via compose, starts the stack, checks health + Postgres/RabbitMQ/notification/audit/reference-data round trips, tears down | The image is built and discarded; nothing is published |
| CI end-to-end job | job *End-to-End Smoke Test* (`build/scripts/e2e-smoke-test.sh`) | 20-check cross-system pass, uploads an evidence file | Same: ephemeral |
| CI contract job | job *API Contract Tests* (`build/scripts/contract-test.sh`, Newman) | Black-box contract tests against the compose stack | Same: ephemeral |
| Azure DevOps mirror | `build/azure-pipelines.yml` | Parked, kept in lockstep with `ci.yml` (ADR-0005) | Not in use |
| Observability config | `observability/` | Prometheus scrape config, Promtail, Tempo, Grafana provisioning, alert rules | Targets compose service names |
| Infra placeholders | `infra/docker/`, `infra/kubernetes/`, `infra/terraform/` | Empty directories (`.gitkeep` only) | Nothing is implemented |
| Promotion policy | ADR-0008 table | Dev (implemented, ephemeral) -> Staging (`release/*` branch) -> Production (`vMAJOR.MINOR.PATCH` tag, manual approval) | Staging and Production rows are "not yet implemented" |

Not present at all: image publishing (no registry push anywhere), versioned image tags (compose builds
`customsclearance-api:dev` / `customsclearance-gateway:dev`), a CD workflow, environment-specific
configuration, database migrations, health/readiness probes definition for an orchestrator beyond `/health`
and `/__gateway/health`, backup/restore, rollback procedure.

Day 43 (ADR-0038) added, none of it yet run on GitHub: *Dependency Scan (SCA)* and *Secret Scan (gitleaks)*
jobs, a separate CodeQL workflow, Dependabot, and an aggregate *CI passed* job. It did not add image
publishing, image scanning or tagging, so the "Not present" list above is unchanged. Branch protection is
enabled on `main` since 2026-10-19 (PR required, `CI passed` and `Analyze (csharp)` must pass); see
[../BRANCH-PROTECTION.md](../BRANCH-PROTECTION.md).

## 2. What a first real deployment would need (not done)

This is a checklist of gaps found by reading the repository, not a procedure.

1. **Hosting target and topology**: where the Gateway and the Api run (container service, VMs, Kubernetes),
   and how the Api is kept off the public network (today compose does this by not publishing its port).
2. **Container registry and image versioning**: build once, tag with a version/commit, push, deploy by tag.
3. **Production configuration for both services**, supplied from the environment/secret store, not from
   committed `appsettings*.json`:
   - `ASPNETCORE_ENVIRONMENT` other than `Development`.
   - JWT settings (`Jwt:Key`, `Issuer`, `Audience`). The committed key is a dev placeholder; the Api only
     reads a `Jwt` section from `appsettings.Development.json`, the Gateway has one in its base
     `appsettings.json`. Both services must share the same values.
   - Connection strings `ConnectionStrings:<X>Db` for each of the seven modules (a module with a blank
     connection string is simply disabled).
   - `RabbitMq:*` (host, port, credentials, exchange, queue), `Email:*` (real relay), `Otlp:Endpoint`.
   - The Gateway's proxy destination (`ReverseProxy:Clusters:api-cluster:Destinations:...:Address`).
4. **Database lifecycle**: in non-Development environments the Api creates **no** schema (the
   `EnsureCreated`-based bootstrap runs only in Development, in each module's `InitializeAsync`), and there is no EF Core migration
   pipeline (ADR-0012, ADR-0025). A migration/bootstrap strategy, a non-owner runtime DB role, backups and a
   restore test are all needed.
5. **Seed data policy**: dev personas (`IdentitySeeder`) are seeded in Development only and must never exist
   elsewhere; a real user-management story (Identity is a minimal in-house stand-in, ADR-0017) is needed.
   Reference data is placeholder and unverified (ADR-0033).
6. **Managed services decisions**: PostgreSQL, message broker, SMTP relay, log/metrics/trace backends
   (compose runs single-node, local-disk Loki/Tempo and anonymous-admin Grafana).
7. **Edge concerns**: TLS termination, public DNS, rate-limit tuning (current 100 req / 10 s / IP is a dev
   default), CORS if a browser client appears.
8. **Alert delivery**: Grafana rules exist but no contact point (ADR-0023).
9. **Release process**: the Staging/Production gates in ADR-0008 (SIT, regression, UAT sign-off, load and
   security testing, runbook review, manual approval) need an environment to run in.
10. **Rollback and incident runbooks**: none.

**Security requirements added by Day 41** (ADR-0036; the full list is
`docs/security/PHASE0_SECURITY_CHECKLIST.md`, and sections 3 and 4 of that checklist are production
requirements that depend on the hosting decision):

- **Environment**: both Dockerfiles now default to `ASPNETCORE_ENVIRONMENT=Production` (they used to bake in
  `Development`). A non-Development host does no schema bootstrap, seeds no dev users and serves no Swagger.
- **JWT key**: `Jwt__Key` must be supplied from a secret store, at least 32 bytes, identical for Gateway and
  Api. Both hosts fail at start-up on the committed placeholder or a short key. There is no rotation process.
- **Audit store**: the table, the restricted login role (INSERT and SELECT only) and its grants must be
  provisioned by IaC or a DBA, since nothing bootstraps them outside Development. The running app must use
  that role and must never hold the owner credential; DBA access to the table must itself be audited.
- **Not done, so a deployment must address them**: TLS on every hop and HSTS, encryption at rest, security
  headers, login lockout, MFA or an external IdP, token refresh (the lifetime is now 60 minutes, set with
  `Jwt__AccessTokenMinutes`), and removing the recipient address from the notification event payload.
- **Metrics port (Day 45)**: the Gateway serves `/metrics` only on its internal port 9100 (`Metrics__InternalPort`),
  which must never be published or exposed through a load balancer; Prometheus scrapes `gateway:9100`. The image
  listens on 8080 and 9100 by default.

**Capacity and sizing.** A first baseline exists ([../performance/BASELINE-2026-10-19.md](../performance/BASELINE-2026-10-19.md)),
but it was measured on one laptop with every container on the same host, so it is **not** a sizing input. What
it does say: nothing failed up to 50 concurrent users; the write path (PostgreSQL write plus RabbitMQ publish)
stopped scaling at roughly 140-180 requests per second and its tail latency grew first; the Gateway rate limit is
now settable (`RateLimiting__PermitLimit`, `RateLimiting__WindowSeconds`, default 100 per 10 s per client IP) and
a production value still has to be chosen. Re-measure on the target hosting before sizing anything.

## 3. Pitfalls already visible in the images

- Since Day 41 both Dockerfiles default to `ASPNETCORE_ENVIRONMENT=Production`, and `docker-compose.yml`
  sets `Development` explicitly. In Development the Api creates tables, seeds dev users, and exposes Swagger
  and `/diagnostics/*` endpoints, so never set that value on anything reachable. In Production the hosts also
  refuse the committed JWT placeholder key, and with no connection strings supplied every database-backed
  module stays disabled.
- `docker-compose.yml` is the dev definition and also what CI exercises. Do not treat it as a deployment
  manifest, and do not point a production system at its credentials.
- `build/scripts/contract-test.sh` is hard-coded to `localhost:8080` and `docker compose down -v` in every
  smoke script deletes volumes: never run them against a stack whose data matters.

## 4. Open decisions (owner: the Product Owner)

| # | Decision | Where it was deferred |
|---|---|---|
| 1 | Cloud provider / hosting target for Staging and Production | ADR-0008, ADR-0015 |
| 2 | Container registry and image versioning scheme | ADR-0008 |
| 3 | Orchestration for non-dev (Kubernetes vs. simpler container hosting) | ADR-0015 |
| 4 | Secrets management and JWT key rotation | ADR-0004, ADR-0011 |
| 5 | Production identity: keep in-house login or adopt a managed IdP | ADR-0017 |
| 6 | Schema migration approach (EF Core migrations or other) and runtime DB roles | ADR-0012, ADR-0025, ADR-0032 |
| 7 | Managed broker (RabbitMQ service, Azure Service Bus, Kafka) | ADR-0012, ADR-0018 |
| 8 | Real SMTP relay and notification recipient resolution (DPDPA 2023) | ADR-0031, ADR-0004 |
| 9 | Observability backends at scale and alert contact points | ADR-0020 to ADR-0023 |
| 10 | Audit retention/partitioning (5 years, monthly per ADR-0004) and tamper-evidence | ADR-0032 |

When any of these is decided, record it in an ADR, then replace this document's relevant section with a
real, tested procedure. Per the working agreements, a deployment procedure is not "done" until it has been
run against the real target.
