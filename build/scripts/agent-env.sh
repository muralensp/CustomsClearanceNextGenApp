#!/usr/bin/env bash
# Prints the environment for running a full, isolated copy of the dev stack, so
# several people or agents can each run docker-compose.yml (and the smoke / E2E
# scripts) at the same time on one machine without clashing over container
# names, volumes, image tags or host ports.
#
#   eval "$(build/scripts/agent-env.sh 1)"       # slot 1
#   bash build/scripts/e2e-smoke-test.sh          # now runs against slot 1's stack
#
# Slot 0 is the normal stack: nothing is overridden and the original ports are
# used, so existing habits and CI are unchanged. Slots 1-9 each get their own
# compose project (cc-slot-N: its own containers, network and named volumes), their
# own image tags, and host ports offset by N * 1000. The stride is 1000 rather than
# something smaller because some default ports are only 100 apart (Loki 3100 /
# Tempo 3200), so a 100 stride would make slot 1's Loki collide with slot 0's Tempo.
#
# Exits non-zero on a bad slot. Prints only `export` lines on stdout (safe to eval);
# the human-readable summary goes to stderr.

set -u

SLOT="${1:-}"
if ! [[ "${SLOT}" =~ ^[0-9]$ ]]; then
  echo "usage: eval \"\$(build/scripts/agent-env.sh <slot 0-9>)\"" >&2
  exit 2
fi

if [ "${SLOT}" -eq 0 ]; then
  echo "unset COMPOSE_PROJECT_NAME IMAGE_PREFIX GATEWAY_PORT MAILPIT_SMTP_PORT MAILPIT_UI_PORT RABBITMQ_PORT RABBITMQ_MGMT_PORT POSTGRES_PORT LOKI_PORT GRAFANA_PORT PROMETHEUS_PORT TEMPO_OTLP_PORT TEMPO_HTTP_PORT"
  echo "slot 0: default stack (original ports, project name from the directory)" >&2
  exit 0
fi

OFFSET=$((SLOT * 1000))

echo "export COMPOSE_PROJECT_NAME=cc-slot-${SLOT}"
echo "export IMAGE_PREFIX=cc-slot-${SLOT}"
echo "export GATEWAY_PORT=$((8080 + OFFSET))"
# SMTP steps by 1, not 1000: 1025 + N*1000 would land on the Mailpit UI ports (8025 + N*1000).
echo "export MAILPIT_SMTP_PORT=$((1025 + SLOT))"
echo "export MAILPIT_UI_PORT=$((8025 + OFFSET))"
echo "export RABBITMQ_PORT=$((5672 + OFFSET))"
echo "export RABBITMQ_MGMT_PORT=$((15672 + OFFSET))"
echo "export POSTGRES_PORT=$((5432 + OFFSET))"
echo "export LOKI_PORT=$((3100 + OFFSET))"
echo "export GRAFANA_PORT=$((3000 + OFFSET))"
echo "export PROMETHEUS_PORT=$((9090 + OFFSET))"
echo "export TEMPO_OTLP_PORT=$((4317 + OFFSET))"
echo "export TEMPO_HTTP_PORT=$((3200 + OFFSET))"

echo "slot ${SLOT}: project cc-slot-${SLOT}, gateway http://localhost:$((8080 + OFFSET)), mailpit UI http://localhost:$((8025 + OFFSET))" >&2
