# ADR-0023: Alerting Basics (Day 29)

**Status:** Accepted
**Date:** 2026-09-16
**Deciders:** User (Product Owner / Implementer), Claude (design/build support)

## Context

Phase 0, Week 6 (Observability), Day 29 of the Day-Wise Plan ("Alerting Basics") - the last of four
days explicitly marked parallelizable with Days 26/27/28, merged on Day 30. Calls for: basic
alerting rules on top of the metrics already collected. Deliverable: alerts that fire on real
conditions (service down, elevated error rate), not just a dashboard someone has to keep watching.

Days 26-28 already built the three observability signals (logs/Loki, metrics/Prometheus, traces/
Tempo) and provisioned a Grafana dashboard (Day 27) surfacing exactly the two conditions this day
needs to alert on: service health (`up`) and error rate (5xx). No new metrics/instrumentation were
needed - this day's real scope is turning existing, already-proven queries into evaluated alert
rules.

Two decisions confirmed with the user:
1. **Grafana's built-in unified alerting** over a separate Prometheus Alertmanager container - it's
   already running (Day 26's Grafana image), evaluates the same datasource queries Day 27's
   dashboard already proved correct, and needs no new service - same "prefer what's already
   provisioned" reasoning Day 26 used for Loki over ELK and Day 28 used for Tempo over Jaeger.
2. **No real notification contact point** (no SMTP/webhook/Slack destination) - this project has no
   external notification target set up yet, consistent with ADR-0008/0015's deliberate
   no-cloud-hosting-decision-yet posture. Alerts are proven via their evaluated **state**
   (Normal/Pending/Alerting) in Grafana's own Alerting UI/API, not via a delivered notification.
   Revisit once a real destination exists.

## Decision

1. **`observability/alerting/rules.yaml`** - Grafana's file-based alert rule provisioning (same
   `apiVersion: 1` provisioning pattern as Days 26-28's datasources/dashboards), mounted into the
   Grafana container at `/etc/grafana/provisioning/alerting/rules.yaml`. Two rules, in a
   `CustomsClearance Service Health` group, evaluated every 30s:
   - **Service Down** (`severity: critical`, `for: 30s`) - `up{job=~"customsclearance-.*"}` below 1
     for either job, i.e. the same `up` signal Day 27's "Service Up" panel already displays,
     evaluated as a condition instead of just a visual.
   - **High 5xx Error Rate** (`severity: warning`, `for: 1m`) -
     `sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
     by (job)` above 0, the same query family as Day 27's "Error Rate (5xx)" panel.
2. **`observability/grafana-datasources.yaml`** - the Prometheus datasource gained an explicit
   `uid: Prometheus` (matching Tempo's existing explicit-uid pattern from Day 28), so the alert
   rules can reference it deterministically instead of relying on Grafana's auto-generated uid.
3. **`docker-compose.yml`** - one new volume mount on the existing `grafana` service; no new
   container, since Grafana's alert scheduler runs inside the process already provisioned for
   dashboards (Day 27) and logs (Day 26).

## Verification

Full `docker compose up -d --build`, then a genuine **state-transition proof**, not just "the rules
provisioned without error":
- `GET /api/v1/provisioning/alert-rules` confirmed both rules loaded (`cc-service-down`,
  `cc-high-error-rate`).
- `GET /api/prometheus/grafana/api/v1/rules` confirmed both rules evaluating with `health: ok` and
  state `inactive` (Normal) under normal conditions - both services genuinely up, no 5xx traffic.
- **Stopped the `api` container** (`docker compose stop api`) - Prometheus's own `/api/v1/targets`
  confirmed the real scrape target went `down`; waited through the scheduler's 30s tick plus the
  rule's own `for: 30s` window; re-queried the rules API and got **`Service Down` in state
  `firing`/`Alerting`**, correctly labeled `severity: critical`, `activeAt` timestamped - proving
  the rule genuinely evaluates and transitions on a real condition, not just parses cleanly.
- **Restarted the `api` container** (`docker compose start api`) - re-queried after the scrape/eval
  cycle and confirmed the rule returned to `inactive`/Normal, proving the full round trip
  (Normal -> Alerting -> Normal), not a one-way transition.
- No RabbitMQ startup-race recurrence this day (the Days 26-28 stray-volume/orphaned-container
  issue did not reappear) - full-solution `dotnet build`/`dotnet format --verify-no-changes`/
  `dotnet test` and `build/scripts/smoke-test.sh` (the actual CI script) all re-confirmed clean
  with unpiped exit codes (0/0/0/0).

## Alternatives Considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Prometheus Alertmanager | Prometheus-native, industry-standard alerting path | New container, alert rules split across `prometheus.yml` + a separate Alertmanager routing config - more moving parts for a dev-scale stack that already has Grafana's alerting engine running | User declined |
| Provision a real contact point (e.g. a webhook to a local test receiver) to prove actual notification delivery | Proves the full alert -> notify path, not just alert state | Adds scope/moving parts (a receiver to run and inspect) for a project with no real notification destination decided yet; alert *state* is the deliverable's core claim ("alerts that fire"), not delivery infrastructure that doesn't exist in this project yet | User declined - deferred until a real destination exists |

## Consequences

- Alert rules reuse Day 27's already-verified PromQL query shapes rather than inventing new ones -
  same discipline as Day 27's "verify real metric names before writing queries," applied here as
  "reuse already-verified queries rather than writing new unverified ones."
- No contact point/notification policy is provisioned - Grafana's built-in default remains in
  place, unconfigured. This is a deliberate, documented gap (same category as Day 27's
  unauthenticated `/metrics`, Day 21's deferred cloud/hosting decision): revisit once a real
  SMTP/webhook/Slack destination is decided, at which point `observability/alerting/` gains a
  `contact-points.yaml` + `policies.yaml` pair alongside `rules.yaml`.
- `noData` handling differs deliberately between the two rules: `Service Down` uses `NoData` (if
  Prometheus itself can't reach a target's `up` series at all, that's itself worth flagging), while
  `High 5xx Error Rate` uses `OK` (no request traffic in the evaluation window is normal at dev
  scale, not an error condition, so it should not read the same as "confirmed error-free").
- Alerting completes the "three pillars + alerting" observability story for Week 6. Day 30 is the
  mandatory sequential wiring/review step across Days 26-29's four parallelizable pieces.

## Related

[[ADR-0021]] (Day 27 - the Prometheus datasource and dashboard queries these alert rules reuse),
[[ADR-0020]] (Day 26 - the Grafana provisioning pattern this ADR extends with a fourth provisioning
category), [[ADR-0022]] (Day 28 - the most recent prior Observability-week ADR, same
zero-manual-setup discipline), Day-Wise Plan Week 6 (Days 26-29, explicitly parallelizable, merged
on Day 30).
