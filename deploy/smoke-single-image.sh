#!/usr/bin/env bash
# Smoke check for the single image (deploy/Dockerfile) against a throwaway PostgreSQL, using DATABASE_URL like Render does.
# Usage: deploy/smoke-single-image.sh <image> [host-port]     Cleans up its containers and network on exit.
set -euo pipefail
image="${1:?image name}"; port="${2:-18080}"; id="cafepos-smoke-$$"
cleanup() { docker rm -f "$id-api" "$id-db" >/dev/null 2>&1 || true; docker network rm "$id" >/dev/null 2>&1 || true; }
trap 'docker logs "$id-api" 2>&1 | tail -n 30 || true; cleanup' ERR
trap cleanup EXIT
docker network create "$id" >/dev/null
docker run -d --name "$id-db" --network "$id" -e POSTGRES_USER=smoke -e POSTGRES_PASSWORD=smoke_pw -e POSTGRES_DB=smoke postgres:16-alpine >/dev/null
docker run -d --name "$id-api" --network "$id" -p "$port:8080" \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e "DATABASE_URL=postgresql://smoke:smoke_pw@$id-db:5432/smoke?sslmode=disable" \
  -e Jwt__Key=smoke-test-signing-key-0123456789-0123456789 \
  -e Database__MigrateOnStartup=true \
  "$image" >/dev/null
base="http://localhost:$port"
for _ in $(seq 1 60); do
  [ "$(curl -s -o /dev/null -w '%{http_code}' "$base/health/ready" || true)" = 200 ] && break; sleep 2
done
check() { # name expected actual
  if [ "$2" = "$3" ]; then echo "ok   $1 ($3)"; else echo "FAIL $1: expected $2, got $3"; exit 1; fi
}
check "/health/ready" 200 "$(curl -s -o /dev/null -w '%{http_code}' "$base/health/ready")"
check "/ is index.html" 200 "$(curl -s -o /tmp/smoke-root.html -w '%{http_code}' "$base/")"
grep -q '<div id="root"' /tmp/smoke-root.html || { echo "FAIL / is not the SPA index.html"; exit 1; }
check "/orders deep link" 200 "$(curl -s -o /tmp/smoke-deep.html -w '%{http_code}' "$base/orders")"
cmp -s /tmp/smoke-root.html /tmp/smoke-deep.html || { echo "FAIL /orders did not return index.html"; exit 1; }
check "/api/v1/nope" 404 "$(curl -s -o /tmp/smoke-nope.txt -D /tmp/smoke-nope.hdr -w '%{http_code}' "$base/api/v1/nope")"
grep -qi '^content-type: application/.*json' /tmp/smoke-nope.hdr || { echo "FAIL /api/v1/nope is not a JSON problem response"; exit 1; }
! grep -qi '<!doctype' /tmp/smoke-nope.txt || { echo "FAIL /api/v1/nope returned the SPA"; exit 1; }
echo "ok   /api/v1/nope is a JSON problem, not the SPA fallback"
echo "smoke OK"
