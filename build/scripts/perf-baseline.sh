#!/usr/bin/env bash
# Performance baseline (Day 42, ADR-0037) - run ON DEMAND, deliberately not a CI job: shared CI runners are
# too noisy for numbers anyone should compare against.
#
# Brings up the real docker-compose stack with the Gateway rate limit raised (so the test measures the
# system, not the limiter), runs the k6 scenarios in tests/Performance/baseline.js from inside the compose
# network, samples container CPU/memory while it runs, and writes everything to TestResults/perf/.
#
# Run it on an otherwise idle machine; close other Docker stacks and heavy programs first. The report
# records the machine so numbers are only compared like for like.
#
#   bash build/scripts/perf-baseline.sh
#   PERF_STEADY_SECONDS=10 PERF_VU_LEVELS=1,10 bash build/scripts/perf-baseline.sh    # quick check
#   KEEP_STACK=1 bash build/scripts/perf-baseline.sh                                  # leave the stack up
#
# Slots (docs/PARALLEL-WORK.md): honours COMPOSE_PROJECT_NAME and the other slot variables.
set -uo pipefail

OUT_DIR="TestResults/perf"
mkdir -p "${OUT_DIR}"
rm -f "${OUT_DIR}"/k6-summary.json "${OUT_DIR}"/baseline-table.txt "${OUT_DIR}"/baseline-rows.json \
      "${OUT_DIR}"/docker-stats.csv "${OUT_DIR}"/environment.txt

# The limiter is raised for the test stack only. The compose default (100 per 10 s) is unchanged.
export GATEWAY_RATE_LIMIT_PERMITS="${GATEWAY_RATE_LIMIT_PERMITS:-1000000}"
export GATEWAY_RATE_LIMIT_WINDOW_SECONDS="${GATEWAY_RATE_LIMIT_WINDOW_SECONDS:-10}"
GATEWAY_PORT_HOST="${GATEWAY_PORT:-8080}"
STATS_PID=""

cleanup() {
  [ -n "${STATS_PID}" ] && kill "${STATS_PID}" 2>/dev/null
  if [ "${KEEP_STACK:-0}" = "1" ]; then
    echo "== KEEP_STACK=1: leaving the stack running =="
  else
    echo "== Tearing down (volumes removed) =="
    docker compose --profile perf down -v
  fi
}
trap cleanup EXIT

echo "== Environment =="
{
  echo "date_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "git_commit=$(git rev-parse --short HEAD 2>/dev/null)"
  echo "os=$(uname -s 2>/dev/null) $(uname -r 2>/dev/null)"
  echo "docker_server=$(docker version --format '{{.Server.Version}}' 2>/dev/null)"
  docker info --format 'docker_cpus={{.NCPU}}
docker_memory_bytes={{.MemTotal}}' 2>/dev/null
  echo "rate_limit_permits=${GATEWAY_RATE_LIMIT_PERMITS} per ${GATEWAY_RATE_LIMIT_WINDOW_SECONDS}s"
  echo "steady_seconds=${PERF_STEADY_SECONDS:-30}"
  echo "vu_levels=${PERF_VU_LEVELS:-1,10,25,50}"
  echo "other_running_containers_before=$(docker ps -q | wc -l | tr -d ' ')"
} | tee "${OUT_DIR}/environment.txt"

echo "== Starting the stack =="
for attempt in 1 2 3; do
  docker compose up -d --build && break
  echo "docker compose up failed (attempt ${attempt}/3) - known RabbitMQ first-boot race, retrying (ADR-0028)"
  sleep 5
done

echo "== Waiting for the Gateway to proxy a healthy Api =="
READY=0
for _ in $(seq 1 60); do
  if [ "$(curl -s -o /dev/null -w '%{http_code}' "http://localhost:${GATEWAY_PORT_HOST}/health")" = "200" ]; then
    READY=1
    break
  fi
  sleep 2
done
if [ "${READY}" != "1" ]; then
  echo "FAIL: stack did not become healthy" >&2
  docker compose logs --tail=100 api gateway >&2
  exit 1
fi

# One warm-up pass so JIT, connection pools and caches are not part of the measured numbers.
echo "== Warm-up (not measured) =="
for _ in $(seq 1 50); do
  curl -s -o /dev/null "http://localhost:${GATEWAY_PORT_HOST}/health"
  curl -s -o /dev/null -X POST "http://localhost:${GATEWAY_PORT_HOST}/diagnostics/widgets?name=warmup"
done

echo "== Sampling container CPU/memory every 5 s =="
(
  echo "timestamp,container,cpu_percent,mem_usage"
  while true; do
    docker stats --no-stream --format '{{.Name}},{{.CPUPerc}},{{.MemUsage}}' 2>/dev/null \
      | sed "s/^/$(date -u +%H:%M:%S),/"
    sleep 5
  done
) > "${OUT_DIR}/docker-stats.csv" &
STATS_PID=$!

echo "== Running k6 =="
# MSYS_NO_PATHCONV: Git Bash on Windows would rewrite the container path /scripts/... into a Windows path.
MSYS_NO_PATHCONV=1 docker compose --profile perf run --rm k6 run /scripts/baseline.js
K6_EXIT=$?
echo "k6_exit=${K6_EXIT}" | tee -a "${OUT_DIR}/environment.txt"

if [ "${K6_EXIT}" -ne 0 ] || [ ! -s "${OUT_DIR}/baseline-table.txt" ]; then
  echo "FAIL: k6 did not complete or wrote no results" >&2
  exit 1
fi

echo "== Results: ${OUT_DIR}/ =="
cat "${OUT_DIR}/baseline-table.txt"
