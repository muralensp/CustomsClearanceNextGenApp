#!/usr/bin/env bash
# Portable API contract test script (Day 14) - invoked identically by
# GitHub Actions today and Azure Pipelines later, per the same portability
# discipline as the other build/scripts/*.sh files (ADR-0005).
#
# Runs the Postman collection (postman/CustomsClearance.postman_collection.json)
# via Newman against the docker-compose dev environment (Day 13), rather
# than against WebApplicationFactory (that's the integration-test.sh
# layer). This is the "black box" contract-testing layer: it hits the API
# purely over HTTP, exactly as an external caller (or the OpenAPI contract
# in openapi/*.yaml, Day 7) would, with no in-process shortcuts.
#
# Requires Node.js (for `npx newman`) - available on GitHub Actions'
# ubuntu-latest runners by default, no separate setup step needed.
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
READY=false
for i in $(seq 1 $MAX_ATTEMPTS); do
  if curl -sf http://localhost:8080/health > /dev/null 2>&1; then
    echo "Api healthy after ${i} attempt(s)."
    READY=true
    break
  fi
  echo "Attempt ${i}/${MAX_ATTEMPTS}: not ready yet, retrying in 3s..."
  sleep 3
done

if [ "$READY" != "true" ]; then
  echo "Api did not become healthy after ${MAX_ATTEMPTS} attempts."
  docker compose logs
  exit 1
fi

echo "== Running Postman contract tests (Newman) =="
npx --yes newman run postman/CustomsClearance.postman_collection.json \
  -e postman/CustomsClearance.postman_environment.json \
  --reporters cli,junit \
  --reporter-junit-export ./TestResults/newman-results.xml
