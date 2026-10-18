#!/usr/bin/env bash
# Phase 0 end-to-end smoke test (Day 40) - invoked identically by GitHub
# Actions today and Azure Pipelines later (ADR-0005).
#
# ONE sequential pass over the whole foundation, through the Gateway, against
# the real docker-compose environment:
#
#   auth -> gateway -> sample service -> rules engine -> audit -> notification
#
# It is verify-only: it exercises the pieces built in Phase 0 together and
# adds no product code. Every request carries the same X-Correlation-Id, and
# where the system records a correlation ID (response header, event logs,
# audit row) the test asserts it is the one we sent. The per-feature checks
# in smoke-test.sh stay as they are; this script is the cross-system pass
# (and what Day 48's regression pass re-runs).
#
# Reporting: a numbered PASS/FAIL line per check, a summary, and an evidence
# file (default TestResults/e2e-evidence.txt) recording the IDs it touched.
# It stops at the FIRST failure and dumps the container logs: the chain is
# sequential, so later steps would only fail because an earlier one did.
#
# Not in scope (happy path only, by decision): negative/security cases,
# failure isolation, deactivated-rule behaviour - those live in the per-day
# tests and the earlier execution logs.
set -uo pipefail

BASE_URL="${E2E_BASE_URL:-http://localhost:${GATEWAY_PORT:-8080}}"
MAILPIT_URL="${E2E_MAILPIT_URL:-http://localhost:${MAILPIT_UI_PORT:-8025}}"
EVIDENCE_FILE="${E2E_EVIDENCE_FILE:-TestResults/e2e-evidence.txt}"

RUN_ID="$(date +%s)"
E2E_CORR="e2e-${RUN_ID}"
BODY_FILE="$(mktemp)"
HEADERS_FILE="$(mktemp)"
CHECKS=0
STARTED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

cleanup() {
  rm -f "${BODY_FILE}" "${HEADERS_FILE}"
  echo "== Tearing down dev environment =="
  docker compose down -v
}
trap cleanup EXIT

# --- reporting helpers -------------------------------------------------------

section() {
  echo
  echo "== $1 =="
}

pass() {
  CHECKS=$((CHECKS + 1))
  printf 'PASS [%02d] %s\n' "${CHECKS}" "$1"
}

fail() {
  printf 'FAIL [%02d] %s\n' "$((CHECKS + 1))" "$1"
  echo "--- last response (HTTP ${HTTP_CODE:-n/a}) ---"
  cat "${BODY_FILE}" 2>/dev/null || true
  echo
  echo "--- container logs ---"
  docker compose logs --tail=500
  echo
  evidence checks_passed "${CHECKS}"
  evidence failed_check "$((CHECKS + 1)): $1"
  evidence result FAIL
  echo "END-TO-END SMOKE TEST FAILED after ${CHECKS} passing check(s). Evidence: ${EVIDENCE_FILE}"
  exit 1
}

evidence() {
  mkdir -p "$(dirname "${EVIDENCE_FILE}")"
  echo "$1=$2" >> "${EVIDENCE_FILE}"
}

# --- request helpers ---------------------------------------------------------

# req METHOD PATH [JSON_BODY]  ->  sets HTTP_CODE, RESPONSE; uses TOKEN if set.
req() {
  local method="$1" path="$2" body="${3:-}"
  local args=(-s -o "${BODY_FILE}" -D "${HEADERS_FILE}" -w '%{http_code}' -X "${method}" "${BASE_URL}${path}" -H "X-Correlation-Id: ${E2E_CORR}")
  if [ -n "${TOKEN:-}" ]; then
    args+=(-H "Authorization: Bearer ${TOKEN}")
  fi
  if [ -n "${body}" ]; then
    args+=(-H 'Content-Type: application/json' -d "${body}")
  fi
  HTTP_CODE="$(curl "${args[@]}")"
  RESPONSE="$(cat "${BODY_FILE}")"
}

expect_code() {
  [ "${HTTP_CODE}" = "$1" ] || fail "$2 (expected HTTP $1, got ${HTTP_CODE})"
}

expect_contains() {
  echo "${RESPONSE}" | grep -q -F -- "$1" || fail "$2 (response did not contain: $1)"
}

# first string value of a JSON field in RESPONSE
json_string() {
  echo "${RESPONSE}" | grep -o "\"$1\":\"[^\"]*\"" | head -1 | cut -d'"' -f4
}

psql_q() {
  docker compose exec -T postgres psql -U customsclearance -d customsclearance_dev -tA -c "$1" | tr -d '\r'
}

# poll "description" max_attempts command...  (command succeeds => done)
poll() {
  local description="$1" attempts="$2"
  shift 2
  for i in $(seq 1 "${attempts}"); do
    if "$@"; then
      return 0
    fi
    sleep 1
  done
  fail "Timed out waiting for: ${description}"
}

api_log_has() { docker compose logs api 2>/dev/null | grep -q -E -- "$1"; }

# --- 0. environment ----------------------------------------------------------

section "0. Environment: build and start the docker-compose dev environment"
# Retry the whole `up` (RabbitMQ first-boot race, ADR-0028 / smoke-test.sh).
UP_MAX_ATTEMPTS=3
UP_SUCCEEDED=false
for attempt in $(seq 1 "${UP_MAX_ATTEMPTS}"); do
  if docker compose up -d --build; then
    UP_SUCCEEDED=true
    break
  fi
  echo "docker compose up attempt ${attempt}/${UP_MAX_ATTEMPTS} failed (likely the rabbitmq first-boot race), retrying in 5s..."
  sleep 5
done
[ "${UP_SUCCEEDED}" = "true" ] || fail "docker compose up did not succeed after ${UP_MAX_ATTEMPTS} attempts"

HEALTHY=false
for i in $(seq 1 30); do
  if curl -sf "${BASE_URL}/health" > /dev/null 2>&1; then
    HEALTHY=true
    break
  fi
  sleep 3
done
[ "${HEALTHY}" = "true" ] || fail "Gateway/Api did not become healthy"
pass "Environment up; ${BASE_URL}/health is healthy"

mkdir -p "$(dirname "${EVIDENCE_FILE}")"
{
  echo "# BOSS NextGen - Phase 0 end-to-end smoke test evidence"
  echo "started_at_utc=${STARTED_AT}"
  echo "git_commit=$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
  echo "run_id=${RUN_ID}"
  echo "correlation_id=${E2E_CORR}"
} > "${EVIDENCE_FILE}"

# --- 1. auth -----------------------------------------------------------------

section "1. AUTH: log in through the Gateway (Identity, ADR-0017)"
TOKEN=""
req POST /auth/login '{"username":"admin","password":"Admin-Dev-Pw-123!"}'
expect_code 200 "Admin login"
expect_contains '"role":"Admin"' "Admin login should report the Admin role"
ADMIN_TOKEN="$(json_string accessToken)"
[ -n "${ADMIN_TOKEN}" ] || fail "Admin login returned no accessToken"
pass "admin logged in; JWT issued with role Admin"

req POST /auth/login '{"username":"broker","password":"Broker-Dev-Pw-123!"}'
expect_code 200 "Broker login"
BROKER_TOKEN="$(json_string accessToken)"
[ -n "${BROKER_TOKEN}" ] || fail "Broker login returned no accessToken"
pass "broker logged in; JWT issued (used later to read the notification as its recipient)"

ADMIN_USER_ID="$(psql_q "select \"Id\" from \"Users\" where \"Username\"='admin'")"
[ -n "${ADMIN_USER_ID}" ] || fail "Could not read the admin user's id from the Users table"
evidence admin_user_id "${ADMIN_USER_ID}"

# --- 2. gateway --------------------------------------------------------------

section "2. GATEWAY: edge health, proxying, JWT enforcement and correlation ID (ADR-0016/0019)"
TOKEN=""
req GET /__gateway/health
expect_code 200 "Gateway own liveness endpoint"
pass "gateway's own /__gateway/health is up"

req GET /health
expect_code 200 "Proxied Api health"
pass "gateway proxies /health to the Api"

TOKEN="${ADMIN_TOKEN}"
req GET /diagnostics/secure
expect_code 200 "Admin-only endpoint with an admin JWT (must pass the Gateway policy AND the Api policy)"
expect_contains '"auditEventId"' "Secure endpoint should report the audit event it wrote"
pass "admin JWT accepted by both the Gateway and the Api on an Admin-only route"

ECHOED="$(grep -i '^x-correlation-id:' "${HEADERS_FILE}" | head -1 | tr -d '\r' | awk '{print $2}')"
[ "${ECHOED}" = "${E2E_CORR}" ] || fail "Gateway did not echo our correlation ID (sent ${E2E_CORR}, got '${ECHOED}')"
pass "correlation ID ${E2E_CORR} honoured and echoed end to end"

# --- 3. sample service -------------------------------------------------------

section "3. SAMPLE SERVICE: PostgreSQL write/read + RabbitMQ publish/consume (ADR-0012/0018)"
WIDGET_NAME="E2E-${RUN_ID}"
req POST "/diagnostics/widgets?name=${WIDGET_NAME}"
expect_code 200 "Create sample widget"
WIDGET_ID="$(json_string id)"
WIDGET_EVENT_ID="$(json_string eventId)"
[ -n "${WIDGET_ID}" ] && [ -n "${WIDGET_EVENT_ID}" ] || fail "Widget response lacked id/eventId"
pass "widget ${WIDGET_ID} written to PostgreSQL through the Gateway"

req GET "/diagnostics/widgets/${WIDGET_ID}"
expect_code 200 "Read the widget back"
expect_contains "${WIDGET_NAME}" "Widget read-back should return the name we wrote"
pass "widget read back from PostgreSQL"

poll "the widget event to be handled via RabbitMQ with our correlation ID" 20 \
  api_log_has "HANDLED event .*${WIDGET_EVENT_ID}.*CorrelationId=${E2E_CORR}"
pass "event ${WIDGET_EVENT_ID} published, consumed via RabbitMQ and handled with correlation ID ${E2E_CORR}"
evidence widget_id "${WIDGET_ID}"
evidence widget_event_id "${WIDGET_EVENT_ID}"

# --- 4. rules engine ---------------------------------------------------------

section "4. RULES ENGINE: author a rule via the admin API, evaluate it, read the persisted trace (ADR-0025..0029)"
RULE_CODE="E2E-DEM-${RUN_ID}"
req POST /api/rules "{\"ruleCode\":\"${RULE_CODE}\",\"name\":\"E2E de minimis\",\"description\":\"Day 40 end-to-end rule\",\"category\":\"DeMinimis\",\"priority\":1,\"effectiveFrom\":\"2000-01-01\",\"effectiveTo\":null,\"outcomeFactName\":\"ClearanceType\",\"outcomeValue\":\"DEM\",\"conditions\":[{\"factName\":\"ShipmentValue\",\"operator\":\"LessThan\",\"value\":\"5000\"}]}"
expect_code 201 "Create rule through the admin API"
RULE_ID="$(json_string id)"
[ -n "${RULE_ID}" ] || fail "Rule creation returned no id"
pass "rule ${RULE_CODE} created through the Admin-gated rules API"

req POST /diagnostics/rules/evaluate-facts '{"ShipmentValue":"2500"}'
expect_code 200 "Evaluate facts through the rules engine"
EVALUATION_ID="$(json_string evaluationId)"
[ -n "${EVALUATION_ID}" ] || fail "Evaluation returned no evaluationId"
expect_contains '"ClearanceType":"DEM"' "Facts should show ClearanceType=DEM after evaluation"
expect_contains "${RULE_CODE}" "The decision trace should name the rule that fired"
pass "ShipmentValue 2500 evaluated: ClearanceType=DEM, decided by ${RULE_CODE}"

req GET "/diagnostics/rules/trace/${EVALUATION_ID}"
expect_code 200 "Read the persisted decision trace"
TRACE_ROWS="$(echo "${RESPONSE}" | grep -o '"stepOrder"' | wc -l | tr -d ' ')"
[ "${TRACE_ROWS}" = "9" ] || fail "Expected 9 persisted trace rows (one per pipeline stage), found ${TRACE_ROWS}"
expect_contains "${RULE_ID}" "The persisted trace should reference the rule's id"
pass "decision trace persisted in PostgreSQL: 9 stage rows, winning stage references rule ${RULE_ID}"
evidence rule_code "${RULE_CODE}"
evidence rule_id "${RULE_ID}"
evidence evaluation_id "${EVALUATION_ID}"

# --- 5. audit ----------------------------------------------------------------

section "5. AUDIT: a configuration change is recorded with old/new value, user, timestamp (ADR-0030/0032)"
CONFIG_KEY="e2e.threshold.${RUN_ID}"
req POST /api/config "{\"key\":\"${CONFIG_KEY}\",\"value\":\"5000\",\"valueType\":\"Decimal\",\"category\":\"E2E\",\"isFeatureFlag\":false}"
expect_code 201 "Create the config value"
CONFIG_ID="$(json_string id)"
[ -n "${CONFIG_ID}" ] || fail "Config creation returned no id"

req PUT "/api/config/${CONFIG_KEY}" '{"value":"10000"}'
expect_code 200 "Change the config value"
pass "config ${CONFIG_KEY} changed 5000 -> 10000 without a redeploy"

req GET "/api/audit?entityId=${CONFIG_ID}"
expect_code 200 "Read the audit trail"
expect_contains '"total":2' "Audit trail should hold exactly the create and the update"
expect_contains '"previousValue":"5000"' "Audit record should carry the old value"
expect_contains '"newValue":"10000"' "Audit record should carry the new value"
expect_contains "\"performedBy\":\"${ADMIN_USER_ID}\"" "Audit record should name the admin user who made the change"
expect_contains "\"correlationId\":\"${E2E_CORR}\"" "Audit record should carry our correlation ID"
expect_contains '"occurredAt"' "Audit record should carry a timestamp"
pass "audit trail: ConfigCreated + ConfigUpdated, old 5000 / new 10000, by admin ${ADMIN_USER_ID}, correlation ID ${E2E_CORR}"
evidence config_entry_id "${CONFIG_ID}"

# --- 6. notification ---------------------------------------------------------

section "6. NOTIFICATION: event -> RabbitMQ -> in-app record + SMTP email (ADR-0018/0031)"
NOTIFY_EMAIL="e2e-${RUN_ID}@example.com"
req POST "/diagnostics/notifications/test-event?recipient=broker&emailAddress=${NOTIFY_EMAIL}"
expect_code 200 "Fire the NotificationRequested test event"
NOTIFY_EVENT_ID="$(json_string eventId)"
[ -n "${NOTIFY_EVENT_ID}" ] || fail "Test event response lacked eventId"
pass "NotificationRequested event ${NOTIFY_EVENT_ID} published"

# Polled, not read once: `docker compose logs` can lag the container stdout by a
# moment on a busy runner (found on the first CI run: an instant read missed a
# line that had just been written).
poll "the notification event publish log line carrying our correlation ID" 20 \
  api_log_has "Published event NotificationRequested .*${NOTIFY_EVENT_ID}.*CorrelationId=${E2E_CORR}"
pass "publish log line carries correlation ID ${E2E_CORR}"

deliveries_ready() {
  [ "$(psql_q "select count(*) from \"NotificationDeliveries\" d join \"Notifications\" n on n.\"Id\" = d.\"NotificationId\" where n.\"SourceEventId\"='${NOTIFY_EVENT_ID}'")" = "2" ]
}
poll "the handler to persist the notification and both deliveries" 20 deliveries_ready
DELIVERIES="$(psql_q "select string_agg(d.\"Channel\" || '=' || d.\"Status\", ',' order by d.\"Channel\") from \"NotificationDeliveries\" d join \"Notifications\" n on n.\"Id\" = d.\"NotificationId\" where n.\"SourceEventId\"='${NOTIFY_EVENT_ID}'")"
[ "${DELIVERIES}" = "Email=Delivered,InApp=Delivered" ] || fail "Expected Email=Delivered,InApp=Delivered, got '${DELIVERIES}'"
pass "notification persisted for the event; deliveries: ${DELIVERIES}"

NOTIFICATION_ID="$(psql_q "select \"Id\" from \"Notifications\" where \"SourceEventId\"='${NOTIFY_EVENT_ID}'")"
TOKEN="${BROKER_TOKEN}"
req GET /api/notifications/mine
expect_code 200 "Recipient reads their notifications"
expect_contains "\"id\":\"${NOTIFICATION_ID}\"" "The broker's own notification list should contain the notification"
pass "recipient (broker) sees notification ${NOTIFICATION_ID} via GET /api/notifications/mine"

mailpit_has_message() { curl -sf "${MAILPIT_URL}/api/v1/messages" | grep -q -F -- "${NOTIFY_EMAIL}"; }
poll "the email to reach the SMTP server (Mailpit)" 20 mailpit_has_message
pass "email to ${NOTIFY_EMAIL} received by the SMTP server over real SMTP"
evidence notification_event_id "${NOTIFY_EVENT_ID}"
evidence notification_id "${NOTIFICATION_ID}"
evidence notification_email "${NOTIFY_EMAIL}"
evidence notification_deliveries "${DELIVERIES}"

# --- done --------------------------------------------------------------------

evidence checks_passed "${CHECKS}"
evidence result PASS
echo
echo "END-TO-END SMOKE TEST PASSED: ${CHECKS}/${CHECKS} checks. Evidence: ${EVIDENCE_FILE}"
exit 0
