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
| Api image definition | `src/Api/Dockerfile` | Multi-stage: `mcr.microsoft.com/dotnet/sdk:8.0` build, `aspnet:8.0` runtime; listens on `:8080` | Sets `ENV ASPNETCORE_ENVIRONMENT=Development` (see section 3) |
| Gateway image definition | `src/Gateway/Dockerfile` | Same pattern | Same `Development` default |
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

<!-- RECONCILE-AFTER-41-43: CI/CD facts after Day 43 - any new image scan / container job, whether an image is now built/tagged/published, the aggregate "CI passed" job and branch protection (link docs/BRANCH-PROTECTION.md). Update the table above and the "Not present" list if Day 43 changed them - from Worker doing Day 43 -->

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
   `EnsureCreated`-based bootstrap runs only in Development, `Program.cs`), and there is no EF Core migration
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

<!-- RECONCILE-AFTER-41-43: Security-driven deployment requirements from Day 41 - how JWT key / secrets must be supplied in non-dev, audit-store DB role and its provisioning, Gateway hardening, anything in docs/security/ that a deployment must satisfy - from Worker doing Day 41. Fold into item 3 and 4 above. -->

<!-- RECONCILE-AFTER-41-43: Performance baseline / capacity expectations from Day 42 that inform sizing (link the report) - from Day 42 (not started when this was written) -->

## 3. Pitfalls already visible in the images

- Both Dockerfiles bake `ASPNETCORE_ENVIRONMENT=Development`. In that mode the Api creates tables, seeds
  dev users, and exposes Swagger and `/diagnostics/*` endpoints. Running these images unmodified anywhere
  reachable is unsafe; the variable must be overridden.
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
