# ADR-0008: CD Pipeline, Containerization & Environment Promotion Path

**Status:** Accepted
**Date:** 2026-07-30
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Day 13 of the Day-Wise Plan calls for a deployment pipeline to a containerized dev environment, plus a defined dev → staging → prod promotion path. No cloud provider or hosting target has been chosen yet — that decision correctly belongs to Phase 0, Day 21 (Environment & Containerization infra buildout), not Phase -1. The user was asked directly how far to take "dev environment" at this stage, given that gap, and chose: containerize the Api host, and prove it via a docker-compose-based CI smoke test — not a persistently hosted environment, and not a new cloud account/commitment.

## Decision

1. **Containerize the placeholder Api host** (`src/Api/Dockerfile`, multi-stage: SDK build → ASP.NET runtime), matching ADR-0001's stack (.NET) and kept lean via `.dockerignore`.
2. **Define "dev environment" as a `docker-compose.yml`** at the repo root: an `api` service (the containerized placeholder) and a `postgres` service (per ADR-0001), runnable identically on a developer's machine or in CI.
3. **CD "deployment" for now means a CI smoke test, not a hosted environment**: `build/scripts/smoke-test.sh` builds the compose stack, polls the Api's `/health` endpoint until it responds (or times out), then tears everything down. This proves the container genuinely starts and serves traffic — not just that `docker build` succeeds — without provisioning any persistent infrastructure.
4. **Define the environment promotion path as a policy now, even though only "dev" is implemented today** (see table below), so the mechanism (branching, tagging, gates) is agreed before real staging/prod targets exist.

## What Was Verified Locally Before Being Wired Into CI

| Check | Result |
|---|---|
| `docker build` on `src/Api/Dockerfile` | Succeeds, multi-stage image produced |
| `docker run` of the built image, direct `curl` to `/health` | Returns `{"status":"Healthy","service":"CustomsClearance.Api"}` |
| `docker compose up -d --build` (full stack: Api + Postgres) | Both containers start and reach a running/healthy state |
| `bash build/scripts/smoke-test.sh` (the actual CI-invoked script, not just manual steps) | Detects healthy Api within 2 polling attempts, exits 0, tears down cleanly |
| Deliberate failure test (health check pointed at a closed port) | Script correctly retries on schedule and would exit 1 on exhaustion; teardown (`trap cleanup EXIT`) fires even when the health check fails |

Consistent with the project's practice since Day 11: no CI step is wired around a command that hasn't been proven to work locally first.

## Important Scope Boundary (Not a Gap — a Deliberate Line)

- **No cloud provider has been chosen.** The `postgres` service in `docker-compose.yml` is part of the ADR-0001 stack and belongs in a "dev environment" definition, but the Api placeholder does **not** yet connect to it (no EF Core wired up — that's later Phase 0/3 work). Its presence should not be read as claiming database integration exists.
- **Nothing is persistently hosted anywhere.** The "dev environment" lives only for the duration of the CI job (or a developer's local `docker compose up`), then is torn down. A real, reachable, always-on dev environment is a Phase 0, Day 21 decision (which cloud/hosting target, container registry, etc.) — deliberately not made here, matching how Day 11 proved the CI mechanism against a minimal placeholder rather than real business logic.
- **No image is published anywhere** (no GHCR/Docker Hub/ACR push). The image is built fresh in each CI run and discarded after the smoke test. Publishing a versioned image to a registry is part of the real deployment pipeline once a hosting target is chosen.

## Environment Promotion Path (Policy, Defined Now for Later Use)

| Environment | Trigger | Gate Before Promotion | Status |
|---|---|---|---|
| **Dev** | Every push/PR to `main` (per ADR-0003 trunk-based development) | CI green: build, lint, test+coverage, containerize+smoke-test (this ADR) | **Implemented** (ephemeral, CI-only — see scope boundary above) |
| **Staging** | Cutting a `release/*` branch (per ADR-0003) | All Dev gates, plus: SIT (System Integration Testing), regression suite, manual review of the release branch's diff against `main` | Not yet implemented — awaits a real hosting target (Phase 0, Day 21) |
| **Production** | Tagging a Semantic Version release (`vMAJOR.MINOR.PATCH`, per ADR-0003) from a `release/*` branch | All Staging gates, plus: UAT sign-off, load/performance testing, security testing (DAST/pen-test per `adr/0004` Item 16), operational runbook review, explicit manual approval (never automatic, given this is a regulatory customs platform) | Not yet implemented |

This mirrors the Quality Gates already defined in the Phase-wise Plan v2 (Database/Backend/API/Frontend/System/Production stages) rather than inventing a new gating philosophy — this ADR maps those existing gates onto the three environments explicitly.

## Consequences

- `docker-compose.yml` and `src/Api/Dockerfile` will need real updates as more bounded contexts get their own projects (Phase 3+) — each new context that needs to run as part of the Api host, or as its own container, extends these files rather than replacing them.
- The Staging and Production rows above are placeholders until Phase 0, Day 21 makes a hosting decision; this ADR should be revisited (not silently outdated) once that decision is made, to fill in the actual deployment mechanism (not just the gate policy).
- `build/scripts/smoke-test.sh` follows the same portability discipline as `build.sh`/`lint.sh`/`test.sh` (ADR-0005): the logic lives in the script, and both `ci.yml` and `azure-pipelines.yml` call it identically, kept in lockstep in the same change.
- Because Postgres is present in the dev environment definition but unused by the Api today, the first time EF Core is actually wired up (Phase 0/3), this ADR's "scope boundary" section should be revisited and the caveat removed rather than left stale.

## Related

[[ADR-0001]] (stack — PostgreSQL is why `postgres` is the chosen dev-environment database service). [[ADR-0003]] (branching model — `release/*` and Semantic Versioning tags are the promotion triggers here). [[ADR-0004]] (security testing — DAST/pen-test referenced as a Production gate). [[ADR-0005]] (CI platform strategy — the lockstep discipline this ADR's CI changes follow). Phase-wise Plan v2 (Quality Gates table this promotion path maps onto). Day 11 (the "prove the mechanism against a minimal placeholder first" pattern this ADR repeats for CD).
