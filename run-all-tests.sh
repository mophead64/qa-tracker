#!/usr/bin/env bash
#
# Run the FULL test suite locally, exactly the way CI does:
#   restore -> build -> unit tests -> build the Docker image -> stand up
#   Postgres + Garage + the app as containers -> run the Playwright E2E suite.
#
# Usage:
#   ./run-all-tests.sh                 # everything, fresh, tears down at the end
#   ./run-all-tests.sh --keep          # leave the stack running afterwards
#   ./run-all-tests.sh --filter 'Name~Admin'   # only some E2E tests (forwarded to dotnet test)
#   ./run-all-tests.sh --skip-unit     # E2E only
#   ./run-all-tests.sh --skip-build    # reuse the existing build output + image
#   ./run-all-tests.sh --rebuild-image # docker build --no-cache
#   ./run-all-tests.sh --down          # just tear down a leftover stack and exit
#
# Env overrides:
#   APP_PORT   host port the app listens on (default 8080)
#
set -euo pipefail
cd "$(dirname "$0")"

# ---- config -----------------------------------------------------------------
APP_PORT="${APP_PORT:-8080}"
IMAGE="qatracker:e2e-local"
# Hyphens, not underscores: the Garage container name becomes the S3 endpoint host and the
# AWS SDK rejects underscores in a hostname ("Invalid Request (invalid hostname)").
NET="qat-e2e-net"
DB="qat-e2e-db"
GARAGE="qat-e2e-garage"
APP="qat-e2e-app"
ADMIN_EMAIL="e2e@qatracker.local"
ADMIN_PASSWORD='E2eP@ssw0rd!'
BASE_URL="http://localhost:${APP_PORT}"

KEEP=0; SKIP_UNIT=0; SKIP_BUILD=0; REBUILD_IMAGE=0; DOWN_ONLY=0
E2E_FILTER=()

while [ $# -gt 0 ]; do
  case "$1" in
    --keep)          KEEP=1 ;;
    --skip-unit)     SKIP_UNIT=1 ;;
    --skip-build)    SKIP_BUILD=1 ;;
    --rebuild-image) REBUILD_IMAGE=1 ;;
    --down)          DOWN_ONLY=1 ;;
    --filter)        shift; E2E_FILTER=(--filter "${1:?--filter needs an expression}") ;;
    -h|--help)       sed -n '3,17p' "$0"; exit 0 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

say() { printf '\n\033[1;36m>>> %s\033[0m\n' "$*"; }

teardown() {
  docker rm -f "$APP" "$DB" "$GARAGE" >/dev/null 2>&1 || true
  docker network rm "$NET" >/dev/null 2>&1 || true
}

on_exit() {
  local rc=$?
  if docker inspect "$APP" >/dev/null 2>&1; then
    mkdir -p TestResults
    docker logs "$APP" > TestResults/app.log 2>&1 || true
    [ $rc -ne 0 ] && say "FAILED (exit $rc) — full app log in TestResults/app.log; tail:" \
      && docker logs --tail 40 "$APP" 2>&1 | sed 's/^/    /' || true
  fi
  if [ "$KEEP" = 1 ] && [ "$DOWN_ONLY" = 0 ]; then
    cat <<EOF

>>> --keep: the stack is still up.
    App:    ${BASE_URL}   (admin: ${ADMIN_EMAIL} / ${ADMIN_PASSWORD})
    Re-run one E2E test without rebuilding:
      QATRACKER_E2E_BASEURL=${BASE_URL} \\
      QATRACKER_E2E_EMAIL=${ADMIN_EMAIL} QATRACKER_E2E_PASSWORD='${ADMIN_PASSWORD}' \\
      dotnet test tests/QaTracker.E2ETests -c Release --no-build --filter 'Name~YourTest'
    Tear down when done:  ./run-all-tests.sh --down
EOF
  else
    teardown
  fi
  exit $rc
}
trap on_exit EXIT

# ---- --down: clean up and stop -------------------------------------------------
if [ "$DOWN_ONLY" = 1 ]; then
  say "Tearing down any leftover e2e stack"
  teardown
  trap - EXIT
  exit 0
fi

# ---- preflight --------------------------------------------------------------
for tool in docker dotnet pwsh; do
  command -v "$tool" >/dev/null || { echo "required tool not found: $tool" >&2; exit 1; }
done
docker info >/dev/null 2>&1 || { echo "docker daemon not reachable" >&2; exit 1; }

# ---- restore / build / unit tests -----------------------------------------
if [ "$SKIP_BUILD" = 0 ]; then
  say "Restoring"
  dotnet restore QaTracker.slnx
  say "Building (Release)"
  dotnet build QaTracker.slnx --no-restore -c Release
fi

if [ "$SKIP_UNIT" = 0 ]; then
  say "Unit tests"
  dotnet test tests/QaTracker.UnitTests/QaTracker.UnitTests.csproj \
    --no-build -c Release \
    --logger "trx;LogFileName=unit.trx" --results-directory TestResults
fi

# ---- build the image the E2E suite runs against --------------------------
if [ "$SKIP_BUILD" = 0 ] || ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
  say "Building Docker image ($IMAGE)"
  build_args=(
    --build-arg "APP_VERSION=$(date -u +%Y.%m.%d)"
    --build-arg "GIT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo unknown)"
    --build-arg "GIT_COMMIT=$(git rev-parse HEAD 2>/dev/null || echo unknown)"
    --build-arg "GIT_COMMIT_SHORT=$(git rev-parse --short=7 HEAD 2>/dev/null || echo unknown)"
    --build-arg "BUILD_DATE=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  )
  [ "$REBUILD_IMAGE" = 1 ] && build_args+=(--no-cache)
  docker build "${build_args[@]}" -t "$IMAGE" .
fi

# ---- stand up Postgres + Garage + the app --------------------------------
say "Starting Postgres, Garage and the app"
teardown                       # clear any previous run
docker network create "$NET" >/dev/null

docker run -d --name "$DB" --network "$NET" \
  -e POSTGRES_DB=qatracker -e POSTGRES_USER=qatracker -e POSTGRES_PASSWORD=qatracker \
  --health-cmd 'pg_isready -U qatracker -d qatracker' \
  --health-interval 3s --health-timeout 5s --health-retries 20 \
  postgres:17-alpine >/dev/null

# --single-node --default-bucket makes Garage bootstrap its own layout, access key and
# bucket on first boot — no separate `mc mb`-style init container needed (unlike MinIO).
docker run -d --name "$GARAGE" --network "$NET" \
  -v "$(pwd)/deploy/garage/garage.toml:/etc/garage.toml:ro" \
  -e GARAGE_RPC_SECRET=5b066687bbd5cbf03e78d89fe62fab6034272dbd5888f88ff52d6e894e862e67 \
  -e GARAGE_DEFAULT_ACCESS_KEY=qatracker-dev \
  -e GARAGE_DEFAULT_SECRET_KEY=qatracker-dev-secret-key \
  -e GARAGE_DEFAULT_BUCKET=qatracker-attachments \
  dxflrs/garage:v2.4.1 /garage server --single-node --default-bucket >/dev/null

echo "Waiting for Postgres..."
for _ in $(seq 1 30); do
  [ "$(docker inspect -f '{{.State.Health.Status}}' "$DB")" = healthy ] && break
  sleep 2
done

echo "Waiting for Garage..."
for _ in $(seq 1 20); do
  docker exec "$GARAGE" /garage health >/dev/null 2>&1 && break
  sleep 2
done

docker run -d --name "$APP" --network "$NET" -p "${APP_PORT}:8080" \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e QATRACKER_DB_HOST="$DB" \
  -e QATRACKER_DB_NAME=qatracker \
  -e QATRACKER_DB_USER=qatracker \
  -e QATRACKER_DB_PASSWORD=qatracker \
  -e QATRACKER_ADMIN_EMAIL="$ADMIN_EMAIL" \
  -e QATRACKER_ADMIN_PASSWORD="$ADMIN_PASSWORD" \
  -e QATRACKER_AUTH_PROVIDER= \
  -e QATRACKER_TELEMETRY_PROVIDER= \
  -e QATRACKER_STORAGE_PROVIDER=S3 \
  -e QATRACKER_S3_BUCKET=qatracker-attachments \
  -e QATRACKER_S3_REGION=us-east-1 \
  -e QATRACKER_S3_ENDPOINT="http://${GARAGE}:3900" \
  -e QATRACKER_S3_ACCESS_KEY=qatracker-dev \
  -e QATRACKER_S3_SECRET_KEY=qatracker-dev-secret-key \
  -e QATRACKER_S3_FORCE_PATH_STYLE=true \
  "$IMAGE" >/dev/null

echo "Waiting for the app at ${BASE_URL} ..."
for _ in $(seq 1 45); do
  curl -fsS "${BASE_URL}/health/ready" >/dev/null 2>&1 && { echo "app is ready"; break; }
  sleep 2
done
curl -fsS "${BASE_URL}/health/ready" >/dev/null 2>&1 || { echo "app did not become ready" >&2; exit 1; }

# ---- Playwright browsers + E2E ------------------------------------------
say "Installing Playwright browsers (chromium)"
PW="tests/QaTracker.E2ETests/bin/Release/net10.0/playwright.ps1"
if [ "$(uname -s)" = "Darwin" ]; then
  pwsh "$PW" install chromium
else
  pwsh "$PW" install --with-deps chromium
fi

say "E2E tests"
QATRACKER_E2E_BASEURL="$BASE_URL" \
QATRACKER_E2E_EMAIL="$ADMIN_EMAIL" \
QATRACKER_E2E_PASSWORD="$ADMIN_PASSWORD" \
dotnet test tests/QaTracker.E2ETests/QaTracker.E2ETests.csproj \
  --no-build -c Release \
  --logger "trx;LogFileName=e2e.trx" --results-directory TestResults \
  ${E2E_FILTER[@]+"${E2E_FILTER[@]}"}

say "All tests passed. trx logs in ./TestResults/"
