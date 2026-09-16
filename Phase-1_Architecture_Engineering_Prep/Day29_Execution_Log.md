# Day 29 Execution Log — Alerting Basics

Phase 0, Week 6 (Observability), Day 29 of the Day-Wise Plan - the last of four days explicitly
parallelizable with Days 26/27/28, merged on Day 30. Deliverable: basic alerting rules on top of
the existing metrics.

---

## Step 1 — Confirm alerting engine and notification posture before building

Asked the user directly, two decisions:
1. Grafana unified alerting vs. a separate Prometheus Alertmanager container. Confirmed **Grafana
   unified alerting** - already running (Day 26's Grafana image), no new container, evaluates the
   same datasource queries Day 27's dashboard already proved correct.
2. Real notification contact point (email/webhook/Slack) vs. state-only in Grafana's UI. Confirmed
   **state-only** - this project has no real SMTP/webhook/Slack destination set up yet, matching
   ADR-0008/0015's deliberate no-cloud-hosting-decision-yet posture.

Recorded as `adr/0023-alerting-basics.md`.

## Step 2 — Write the alert rules, reusing already-verified queries

`observability/alerting/rules.yaml` (Grafana's file-based alert provisioning, same `apiVersion: 1`
pattern as Days 26-28's datasources/dashboards) - two rules, evaluated every 30s:
- **Service Down** (`severity: critical`, `for: 30s`) - `up{job=~"customsclearance-.*"}` below 1.
- **High 5xx Error Rate** (`severity: warning`, `for: 1m`) - the same
  `http_server_request_duration_seconds_count{http_response_status_code=~"5.."}` rate query family
  as Day 27's dashboard panel, above 0.

Deliberately reused Day 27's already-verified PromQL rather than writing new unverified queries.

`observability/grafana-datasources.yaml` - Prometheus datasource gained an explicit `uid:
Prometheus` (matching Tempo's Day 28 explicit-uid pattern) so the alert rules can reference it
deterministically. `docker-compose.yml` - one new read-only volume mount on the existing `grafana`
service (`observability/alerting/rules.yaml` -> the provisioning path); no new container.

## Step 3 — Prove the deliverable end-to-end against real running containers

1. Fresh `docker compose up -d --build` - all 9 containers up. No RabbitMQ startup-race recurrence
   this time (the Days 26-28 stray-volume/orphaned-container issue did not reappear).
2. `docker logs grafana` confirmed `"finished to provision alerting"` with no errors.
3. `GET /api/v1/provisioning/alert-rules` - both rules loaded (`cc-service-down`,
   `cc-high-error-rate`).
4. `GET /api/prometheus/grafana/api/v1/rules` - both rules evaluating, `health: ok`, state
   `inactive` (Normal) under real normal conditions.
5. **Forced a real failure**: `docker compose stop api`. Confirmed via Prometheus's own
   `/api/v1/targets` that the `customsclearance-api` scrape target genuinely went `down`. Waited
   through the alert scheduler's tick plus the rule's `for: 30s` window, then re-queried the rules
   API: **`Service Down` was `firing`/`Alerting`**, correctly labeled `severity: critical`, with a
   real `activeAt` timestamp.
6. **Recovered**: `docker compose start api`. Waited through the next scrape/eval cycle and
   re-queried - `Service Down` returned to `inactive`/Normal, proving the full Normal -> Alerting ->
   Normal round trip, not just a one-way transition that happens to look right once.
7. `docker compose down -v`, then `build/scripts/smoke-test.sh` (the actual CI script) - unpiped
   exit code 0, Postgres/RabbitMQ round-trip checks (Days 18/24) unaffected.
8. Full-solution `dotnet build`/`dotnet format --verify-no-changes`/`dotnet test`, all clean with
   unpiped exit codes (0/0/0) - no code changes this day (pure YAML/docker-compose config), so no
   new test project needed; correctness proven entirely by the live state-transition test above.

## Outcome

**Deliverable met**: two real alert rules, provisioned with zero manual setup, proven to genuinely
evaluate and transition state on a real forced failure (not just parse without error) and recover
correctly once the failure was resolved. Notification delivery deliberately deferred (no real
destination exists yet) - documented as an open, revisit-later gap, not an oversight. **Decision
recorded**: `adr/0023-alerting-basics.md` (Accepted).

**Status**: Complete. Days 26-29 (Observability) are all now individually complete. Per the
Day-Wise Plan, **Day 30 — the mandatory sequential wiring/review step** across all four is next,
pending explicit instruction to begin.
