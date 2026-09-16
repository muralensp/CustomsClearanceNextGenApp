#!/usr/bin/env bash
# Portable CD smoke-test script (Day 13) - invoked identically by GitHub
# Actions today and Azure Pipelines later, per the same portability
# discipline as build.sh/lint.sh/test.sh (ADR-0005).
#
# Builds the docker-compose "dev environment" (Api + Postgres, per
# docker-compose.yml), waits for the Api container to report healthy via
# /health, then tears everything down. This proves the container actually
# starts and serves traffic - not just that `docker build` succeeds.
#
# Day 18 adds a real PostgreSQL round-trip check (POST + GET
# /diagnostics/widgets) via Platform.Persistence's DiagnosticsDbContext.
# This is the one CI job where a real Postgres container is actually
# available (the WebApplicationFactory-based Integration Tests job, Day
# 14, is in-process only and has no database) - so this is where the
# Persistence library's real-database behavior gets CI coverage, not just
# local manual verification.
#
# Day 24 adds a real event-bus round-trip check: the widget-creation call
# above already publishes a DiagnosticsWidgetCreated event through
# RabbitMqEventBus; this checks the Api's own logs for the "HANDLED
# event" line DiagnosticsWidgetCreatedHandler emits, carrying the same
# EventId, proving the real RabbitMQ publish/consume path (not just that
# PublishAsync returned without throwing) - same reasoning as the
# PostgreSQL check above, extended to the event bus.
#
# Day 34 finding: `docker compose up`'s own dependency-wait for rabbitmq's
# healthcheck can fail transiently on a brand-new volume's first boot
# (the .erlang.cookie eacces race documented in docker-compose.yml's
# rabbitmq service) - the container's own `restart: on-failure` policy
# self-heals it within seconds, but `up`'s exit code reflects the moment
# it gave up waiting, not the eventual healthy state. Retrying the whole
# `up` command (not just waiting longer) picks up the already-restarting
# container on the next attempt, same as a manual `docker start` retry
# was confirmed to fix this live.
set -uo pipefail

echo "== Building and starting dev environment (docker compose) =="
UP_MAX_ATTEMPTS=3
UP_SUCCEEDED=false
for attempt in $(seq 1 $UP_MAX_ATTEMPTS); do
  if docker compose up -d --build; then
    UP_SUCCEEDED=true
    break
  fi
  echo "docker compose up attempt ${attempt}/${UP_MAX_ATTEMPTS} failed (likely the rabbitmq first-boot race), retrying in 5s..."
  sleep 5
done

if [ "$UP_SUCCEEDED" != "true" ]; then
  echo "docker compose up did not succeed after ${UP_MAX_ATTEMPTS} attempts."
  docker compose logs
  exit 1
fi

set -e

cleanup() {
  echo "== Tearing down dev environment =="
  docker compose down -v
}
trap cleanup EXIT

echo "== Waiting for Api health check =="
MAX_ATTEMPTS=20
HEALTHY=false
for i in $(seq 1 $MAX_ATTEMPTS); do
  if curl -sf http://localhost:8080/health > /tmp/health_response.json 2>/dev/null; then
    echo "Api healthy after ${i} attempt(s):"
    cat /tmp/health_response.json
    echo ""
    HEALTHY=true
    break
  fi
  echo "Attempt ${i}/${MAX_ATTEMPTS}: not ready yet, retrying in 3s..."
  sleep 3
done

if [ "$HEALTHY" != "true" ]; then
  echo "Api did not become healthy after ${MAX_ATTEMPTS} attempts."
  docker compose logs
  exit 1
fi

echo "== Verifying real PostgreSQL round-trip (Platform.Persistence) =="
CREATE_RESPONSE=$(curl -sf -X POST "http://localhost:8080/diagnostics/widgets?name=CiSmokeTestWidget")
echo "Create response: ${CREATE_RESPONSE}"

WIDGET_ID=$(echo "${CREATE_RESPONSE}" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
if [ -z "${WIDGET_ID}" ]; then
  echo "Failed to extract widget id from create response."
  docker compose logs api
  exit 1
fi

GET_RESPONSE=$(curl -sf "http://localhost:8080/diagnostics/widgets/${WIDGET_ID}")
echo "Get response: ${GET_RESPONSE}"

if ! echo "${GET_RESPONSE}" | grep -q "CiSmokeTestWidget"; then
  echo "Widget round-trip failed: created widget was not readable back from PostgreSQL."
  docker compose logs api
  exit 1
fi

echo "PostgreSQL round-trip verified: widget created and read back successfully."

echo "== Verifying real RabbitMQ publish/consume round-trip (Platform.Messaging) =="
CREATE_EVENT_ID=$(echo "${CREATE_RESPONSE}" | grep -o '"eventId":"[^"]*"' | cut -d'"' -f4)
if [ -z "${CREATE_EVENT_ID}" ]; then
  echo "Failed to extract eventId from create response."
  exit 1
fi

EVENT_HANDLED=false
for i in $(seq 1 10); do
  if docker compose logs api 2>/dev/null | grep -q "HANDLED event.*${CREATE_EVENT_ID}"; then
    echo "Event ${CREATE_EVENT_ID} handled after ${i} attempt(s)."
    EVENT_HANDLED=true
    break
  fi
  echo "Attempt ${i}/10: event not yet handled, retrying in 1s..."
  sleep 1
done

if [ "${EVENT_HANDLED}" != "true" ]; then
  echo "DiagnosticsWidgetCreated event was never handled via RabbitMQ."
  docker compose logs api
  exit 1
fi

echo "RabbitMQ round-trip verified: event published and consumed successfully."
