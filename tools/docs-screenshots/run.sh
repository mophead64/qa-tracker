#!/usr/bin/env bash
#
# Regenerate the documentation screenshots (docs/screenshots/*.png) from scratch:
#   build the app image -> start a throwaway stack (separate ports and volumes) ->
#   seed the fictional demo data -> capture every shot -> tear the stack down.
#
# Usage (from anywhere):
#   tools/docs-screenshots/run.sh                    # everything
#   tools/docs-screenshots/run.sh dashboard defect   # just these shots (fresh stack and data)
#   tools/docs-screenshots/run.sh --keep             # leave the stack up afterwards (app on :8090)
#   tools/docs-screenshots/run.sh --reuse defect     # re-take shots against a stack left up by --keep
#   tools/docs-screenshots/run.sh --skip-build       # reuse the existing qatracker:docs image
#   tools/docs-screenshots/run.sh --down             # tear down a stack left up by --keep
#
# Signing in to a kept stack: priya.raman@example.com (QA) or marcus.chen@example.com (Dev),
# password Demo!Passw0rd.
#
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(cd ../.. && pwd)"
IMAGE="qatracker:docs"
APP_URL="http://localhost:8090"
compose() { docker compose -f "$ROOT/tools/docs-screenshots/docker-compose.yml" "$@"; }
tool() { dotnet run --project "$ROOT/tools/docs-screenshots" -c Release --no-build -- "$@"; }

KEEP=0; REUSE=0; SKIP_BUILD=0; DOWN=0; SHOTS=()
for arg in "$@"; do
  case "$arg" in
    --keep) KEEP=1 ;;
    --reuse) REUSE=1; KEEP=1 ;;
    --skip-build) SKIP_BUILD=1 ;;
    --down) DOWN=1 ;;
    -h|--help) sed -n '3,18p' "$0"; exit 0 ;;
    -*) echo "unknown option: $arg" >&2; exit 2 ;;
    *) SHOTS+=("$arg") ;;
  esac
done

say() { printf '\n\033[1;36m>>> %s\033[0m\n' "$*"; }

wait_ready() {
  for _ in $(seq 1 60); do
    curl -fsS "$APP_URL/health/ready" >/dev/null 2>&1 && return 0
    sleep 2
  done
  echo "the app did not become ready at $APP_URL" >&2
  compose logs app | tail -40 >&2
  return 1
}

if [ "$DOWN" = 1 ]; then
  compose down -v
  exit 0
fi

if [ "$KEEP" = 0 ]; then
  trap 'say "Tearing down"; compose down -v >/dev/null 2>&1 || true' EXIT
fi

if [ "$REUSE" = 0 ]; then
  if [ "$SKIP_BUILD" = 0 ] || ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    say "Building $IMAGE"
    docker build -t "$IMAGE" \
      --build-arg "APP_VERSION=$(date -u +%Y.%m.%d)" \
      --build-arg "GIT_BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD)" \
      --build-arg "GIT_COMMIT=$(git -C "$ROOT" rev-parse HEAD)" \
      --build-arg "GIT_COMMIT_SHORT=$(git -C "$ROOT" rev-parse --short=7 HEAD)" \
      --build-arg "BUILD_DATE=$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
      "$ROOT"
  fi

  say "Starting a fresh stack"
  compose down -v >/dev/null 2>&1 || true
  compose up -d
  wait_ready
fi

say "Building the screenshot tool"
dotnet build "$ROOT/tools/docs-screenshots/DocsScreenshots.csproj" -c Release -v quiet -nologo
# Playwright's Chromium (a no-op when it's already installed).
PW="$ROOT/tools/docs-screenshots/bin/Release/net10.0/playwright.ps1"
if command -v pwsh >/dev/null; then
  pwsh "$PW" install chromium >/dev/null
else
  echo "pwsh not found — if Chromium isn't installed yet, install PowerShell and run: pwsh $PW install chromium" >&2
fi

if [ "$REUSE" = 0 ]; then
  say "Seeding the demo data"
  tool seed
fi

say "Capturing screenshots"
tool shots ${SHOTS[@]+"${SHOTS[@]}"}

# The SSO-only login page needs the app restarted with local sign-in switched off.
if [ ${#SHOTS[@]} -eq 0 ] || printf '%s\n' "${SHOTS[@]}" | grep -qx 'login-sso-only'; then
  say "Capturing the SSO-only login page"
  QATRACKER_DOCS_LOCAL_AUTH=false compose up -d app
  wait_ready
  tool login-sso-only
  compose up -d app
  wait_ready
fi

say "Done — screenshots are in ${QATRACKER_DOCS_OUT:-docs/screenshots}/"
