#!/usr/bin/env bash
# dev-stack.sh — run the whole #8 stack locally: infrastructure (docker compose),
# the seed, the six .NET services and the web app, with one log file and one
# PID file per process.
#
#   scripts/dev-stack.sh start     # infra up, build once, seed, services, web
#   scripts/dev-stack.sh stop      # stop every process this script started
#   scripts/dev-stack.sh status    # what is running, and where the logs are
#   scripts/dev-stack.sh stop-service Fulfillment    # stop one .NET service (e.g. to see a 503)
#   scripts/dev-stack.sh start-service Fulfillment   # start it again, without rebuilding
#                                                     (`web` serves the existing apps/web build)
#
# Environment, lowest to highest precedence: .env.example, then .env, then
# anything already set in your shell (`WEB_PORT=3020 scripts/dev-stack.sh start`)
# — the precedence docker compose applies. WEB_MODE=dev runs `next dev` instead of the
# production build (default: prod — `next build` then `next start`).
#
# The solution is BUILT ONCE, then every service starts with `dotnet run
# --no-build`: six concurrent `dotnet run`s would each build the shared
# projects at the same time, which CLAUDE.md forbids (two builds against the
# same projects at once). Stop the stack before running ./quality.sh — a running
# service holds its build output open.
#
# The Gateway listens on GATEWAY_PORT itself. An ASPNETCORE_URLS exported in
# your shell overrides it (an explicit URL wins), so leave that unset.

set -u

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RUN_DIR="$ROOT/logs/dev-stack"
SERVICES=(Orders Fulfillment Billing Notifications Projector Gateway)

load_env() {
  # Sourcing the files with `set -a` would OVERWRITE a variable the caller set
  # on the command line, so the caller's environment is captured first and
  # re-applied last. Without this, a WEB_PORT or GATEWAY_PORT given on the
  # command line was silently replaced by the value in .env.example.
  local caller_env
  # `export -p` prints `declare -x NAME=...`, and `declare` inside a function
  # makes a LOCAL variable that vanishes on return — so the lines are rewritten
  # to `declare -gx`, which re-exports at global scope.
  caller_env="$(export -p | sed 's/^declare -x /declare -gx /')"
  set -a
  # shellcheck disable=SC1091
  [ -f "$ROOT/.env.example" ] && . "$ROOT/.env.example"
  # shellcheck disable=SC1091
  [ -f "$ROOT/.env" ] && . "$ROOT/.env"
  set +a
  eval "$caller_env"
  export GATEWAY_PORT="${GATEWAY_PORT:-3001}"
  export WEB_PORT="${WEB_PORT:-3010}"
  export GATEWAY_BASE_URL="${GATEWAY_BASE_URL:-http://localhost:${GATEWAY_PORT}}"
}

is_running() {
  local pidfile="$1"
  [ -f "$pidfile" ] && kill -0 "$(cat "$pidfile")" 2>/dev/null
}

port_in_use() {
  (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null
}

# Waits for OUR process to answer: a URL answered by some other program that
# already holds the port is not success (found when another app took :3000, the port WEB_PORT used to default to).
wait_http() {
  local url="$1" label="$2" seconds="${3:-120}" pidfile="${4:-}"
  local deadline=$((SECONDS + seconds))
  until curl -fsS -o /dev/null "$url" 2>/dev/null; do
    if [ -n "$pidfile" ] && ! is_running "$pidfile"; then
      echo "  [FAIL] $label exited before answering $url — see its log in $RUN_DIR"
      return 1
    fi
    if [ "$SECONDS" -ge "$deadline" ]; then
      echo "  [FAIL] $label did not answer $url within ${seconds}s — see its log in $RUN_DIR"
      return 1
    fi
    sleep 1
  done
  if [ -n "$pidfile" ] && ! is_running "$pidfile"; then
    echo "  [FAIL] $url answers, but $label is not running — another program holds that port"
    return 1
  fi
  echo "  [OK]   $label answers $url"
}

start_process() {
  local name="$1"; shift
  local pidfile="$RUN_DIR/$name.pid" logfile="$RUN_DIR/$name.log"
  if is_running "$pidfile"; then
    echo "  [SKIP] $name already running (pid $(cat "$pidfile"))"
    return 0
  fi
  # setsid: each process leads its own process group, so `stop` can signal the
  # whole group (dotnet run and next both spawn children).
  setsid "$@" >"$logfile" 2>&1 < /dev/null &
  echo $! >"$pidfile"
  echo "  [OK]   $name started (pid $!, log $logfile)"
}

stop_process() {
  local name="$1"
  local pidfile="$RUN_DIR/$name.pid"
  [ -f "$pidfile" ] || return 0
  local pid
  pid="$(cat "$pidfile")"
  if kill -0 "$pid" 2>/dev/null; then
    kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null
    for _ in $(seq 1 30); do
      kill -0 "$pid" 2>/dev/null || break
      sleep 1
    done
    if kill -0 "$pid" 2>/dev/null; then
      kill -KILL -- "-$pid" 2>/dev/null || kill -KILL "$pid" 2>/dev/null
    fi
    echo "  [OK]   $name stopped (pid $pid)"
  fi
  rm -f "$pidfile"
}

cmd_start() {
  load_env
  mkdir -p "$RUN_DIR"
  echo "── infrastructure"
  # --no-build first: the locally built images (kafka-init, otel-collector) are
  # reused as they are; only a machine that lacks them falls back to building.
  docker compose -f "$ROOT/docker-compose.infra.yml" up -d --no-build >"$RUN_DIR/infra.log" 2>&1 \
    || docker compose -f "$ROOT/docker-compose.infra.yml" up -d >>"$RUN_DIR/infra.log" 2>&1 \
    || { echo "  [FAIL] docker compose — see $RUN_DIR/infra.log"; return 1; }
  echo "  [OK]   infrastructure up"

  echo "── build (once)"
  if ! dotnet build "$ROOT/OrderToCash.sln" --nologo -v quiet >"$RUN_DIR/build.log" 2>&1; then
    echo "  [FAIL] dotnet build — see $RUN_DIR/build.log"
    return 1
  fi
  echo "  [OK]   solution built"

  echo "── seed (migrations + master data, idempotent)"
  if ! (cd "$ROOT" && dotnet run --no-build --project src/Seed >"$RUN_DIR/Seed.log" 2>&1); then
    echo "  [FAIL] seed — see $RUN_DIR/Seed.log"
    return 1
  fi
  echo "  [OK]   seeded"

  echo "── services"
  # The ports THIS stack binds, recorded so `stop` checks exactly these — not
  # whatever WEB_PORT happens to be in the stopping shell, and not a port some
  # other program on this machine already held.
  printf '%s\n' "$GATEWAY_PORT" "$WEB_PORT" \
    "${ORDERS_HEALTH_PORT:-3002}" "${FULFILLMENT_HEALTH_PORT:-3003}" "${BILLING_HEALTH_PORT:-3004}" \
    "${NOTIFICATIONS_HEALTH_PORT:-3005}" "${PROJECTOR_HEALTH_PORT:-3006}" >"$RUN_DIR/ports"
  for service in "${SERVICES[@]}"; do
    cmd_start_service "$service"
  done
  wait_http "http://localhost:${GATEWAY_PORT}/health/ready" "Gateway" 180 "$RUN_DIR/Gateway.pid" || return 1

  echo "── web (${WEB_MODE:-prod})"
  if is_running "$RUN_DIR/web.pid"; then
    echo "  [SKIP] web already running (pid $(cat "$RUN_DIR/web.pid"))"
  elif port_in_use "$WEB_PORT"; then
    echo "  [FAIL] port $WEB_PORT is already in use — set WEB_PORT to a free port (e.g. WEB_PORT=3020)"
    return 1
  elif [ "${WEB_MODE:-prod}" = "dev" ]; then
    (cd "$ROOT/apps/web" && start_process web ./node_modules/.bin/next dev --port "$WEB_PORT")
  else
    if ! (cd "$ROOT/apps/web" && NEXT_TELEMETRY_DISABLED=1 ./node_modules/.bin/next build >"$RUN_DIR/web-build.log" 2>&1); then
      echo "  [FAIL] next build — see $RUN_DIR/web-build.log"
      return 1
    fi
    (cd "$ROOT/apps/web" && start_process web ./node_modules/.bin/next start --port "$WEB_PORT")
  fi
  wait_http "http://localhost:${WEB_PORT}/login" "web" 120 "$RUN_DIR/web.pid" || return 1

  echo
  echo "Open http://localhost:${WEB_PORT} — sign in as ${GATEWAY_OPERATOR_USERNAME:-operator} / \$GATEWAY_OPERATOR_PASSWORD."
  echo "Stop everything with: scripts/dev-stack.sh stop   (infrastructure containers are left running)"
}

cmd_stop() {
  stop_process web
  local i
  for ((i = ${#SERVICES[@]} - 1; i >= 0; i--)); do
    stop_process "${SERVICES[$i]}"
  done
  # Anything this script started that is still alive is a leak — say so.
  #
  # Two checks, because neither sees everything. By NAME: the `dotnet run`
  # wrappers and the OrderToCash.* executables they spawn, anchored to
  # src/<service>/bin so a test host (tests/*/bin) never matches. By PORT: the
  # web server cannot be found by name at all — Next.js rewrites its process
  # title to `next-server (vX)` — so the ports recorded at start are probed.
  local leftover="" port held=""
  # A command-line match alone also matches any SHELL whose command line merely
  # quotes one of these paths (the self-match hazard CLAUDE.md warns about), so
  # every candidate is confirmed by its EXECUTABLE: only dotnet, an OrderToCash.*
  # apphost or node counts as something this script started.
  local pid exe
  for pid in $(pgrep -f "dotnet run --no-build --project src/|/src/(Orders|Fulfillment|Billing|Notifications|Projector|Gateway)/bin/[^ ]*/OrderToCash\.[A-Za-z]+(\.dll)?( |$)|next (dev|start) --port" || true); do
    exe="$(basename "$(readlink "/proc/$pid/exe" 2>/dev/null)" 2>/dev/null)"
    case "$exe" in
      dotnet|OrderToCash.*|node) leftover="$leftover $pid" ;;
    esac
  done
  if [ -f "$RUN_DIR/ports" ]; then
    while IFS= read -r port; do
      [ -n "$port" ] && port_in_use "$port" && held="$held $port"
    done <"$RUN_DIR/ports"
  fi
  if [ -n "$leftover" ] || [ -n "$held" ]; then
    [ -n "$leftover" ] && echo "  [WARN] processes still alive after stop:$leftover"
    [ -n "$held" ] && echo "  [WARN] ports this stack bound are still held:$held"
    return 1
  fi
  rm -f "$RUN_DIR/ports"
  echo "  [OK]   nothing left running, no port this stack bound is still held"
}

cmd_start_service() {
  local service="$1"
  load_env
  mkdir -p "$RUN_DIR"
  if [ "$service" = "web" ]; then
    if port_in_use "$WEB_PORT"; then
      echo "  [FAIL] port $WEB_PORT is already in use — set WEB_PORT to a free port"
      return 1
    fi
    # Serves the existing production build (apps/web/.next) — run `next build` first.
    (cd "$ROOT/apps/web" && start_process web ./node_modules/.bin/next start --port "$WEB_PORT")
    return
  fi
  if [ "$service" = "Gateway" ] && port_in_use "$GATEWAY_PORT" && ! is_running "$RUN_DIR/Gateway.pid"; then
    echo "  [FAIL] port $GATEWAY_PORT is already in use — set GATEWAY_PORT to a free port"
    return 1
  fi
  (cd "$ROOT" && start_process "$service" dotnet run --no-build --project "src/$service")
}

cmd_status() {
  local name
  for name in "${SERVICES[@]}" web; do
    if is_running "$RUN_DIR/$name.pid"; then
      echo "  running  $name (pid $(cat "$RUN_DIR/$name.pid"), log $RUN_DIR/$name.log)"
    else
      echo "  stopped  $name"
    fi
  done
}

case "${1:-}" in
  start) cmd_start ;;
  stop) cmd_stop ;;
  status) cmd_status ;;
  start-service) cmd_start_service "${2:?service name}" ;;
  stop-service) stop_process "${2:?service name}" ;;
  # Runs one command in the FOREGROUND with the stack's environment loaded
  # (.env.example, then .env, then anything set in the calling shell). The
  # root package.json uses it, so `pnpm dev:orders` sees the same variables
  # that `start` gives the service.
  env)
    shift
    [ "$#" -gt 0 ] || { echo "usage: $0 env <command> [args...]"; exit 2; }
    load_env
    cd "$ROOT" && exec "$@"
    ;;
  *) echo "usage: $0 {start|stop|status|start-service <Name>|stop-service <Name>|env <command> [args...]}"; exit 2 ;;
esac
