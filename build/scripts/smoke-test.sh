#!/usr/bin/env bash
# Portable CD smoke-test script (Day 13) - invoked identically by GitHub
# Actions today and Azure Pipelines later, per the same portability
# discipline as build.sh/lint.sh/test.sh (ADR-0005).
#
# Builds the docker-compose "dev environment" (Api + Postgres, per
# docker-compose.yml), waits for the Api container to report healthy via
# /health, then tears everything down. This proves the container actually
# starts and serves traffic - not just that `docker build` succeeds.
set -euo pipefail

echo "== Building and starting dev environment (docker compose) =="
docker compose up -d --build

cleanup() {
  echo "== Tearing down dev environment =="
  docker compose down -v
}
trap cleanup EXIT

echo "== Waiting for Api health check =="
MAX_ATTEMPTS=20
for i in $(seq 1 $MAX_ATTEMPTS); do
  if curl -sf http://localhost:8080/health > /tmp/health_response.json 2>/dev/null; then
    echo "Api healthy after ${i} attempt(s):"
    cat /tmp/health_response.json
    echo ""
    exit 0
  fi
  echo "Attempt ${i}/${MAX_ATTEMPTS}: not ready yet, retrying in 3s..."
  sleep 3
done

echo "Api did not become healthy after ${MAX_ATTEMPTS} attempts."
docker compose logs
exit 1
