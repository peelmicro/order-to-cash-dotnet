#!/usr/bin/env bash
# Order-To-Cash — MS-SQL Server entrypoint with database bootstrap.
#
# WHY THIS FILE EXISTS AT ALL
#
# The MySQL image #7 used runs anything dropped in /docker-entrypoint-initdb.d
# on first boot. The MS-SQL image has no such hook — it execs sqlservr as PID 1
# and offers no initialisation mechanism whatsoever. Everything below is the
# missing mechanism, and it is the single largest piece of genuinely new
# infrastructure work in assessment #8.
#
# WHAT IT DOES
#
#   1. starts sqlservr in the background, keeping its PID
#   2. waits until the engine actually answers a query (not merely opens a port)
#   3. runs infra/mssql/init/01-create-databases.sql through sqlcmd
#   4. hands the foreground back to sqlservr with `wait`, so the container's
#      lifetime and exit status remain the engine's, exactly as if it were PID 1
#
# THE FAILURE THIS IS DESIGNED AROUND
#
# SQL Server accepts connections *before* this script has created anything.
# A container healthcheck that only asks "is the engine up?" therefore reports
# healthy during the bootstrap window; Compose then releases every service
# gated on `service_healthy`, and the first migration job connects to a server
# with no otc_* databases and dies.
#
# This is the same trap #7 documented on its MySQL healthcheck — there, a
# socket-based ping passed while the temporary init server was running and the
# real TCP listener did not yet exist. Same shape, different engine, and it is
# the reason the healthcheck in docker-compose.infra.yml asserts that all four
# databases exist rather than that the engine responds. The engine being up is
# necessary and not sufficient; the check must be unsatisfiable until the
# bootstrap has finished.

set -euo pipefail

SQLCMD="${SQLCMD_BIN:-/opt/mssql-tools18/bin/sqlcmd}"
INIT_SQL="${MSSQL_INIT_SQL:-/init/01-create-databases.sql}"
READY_TIMEOUT_SECONDS="${MSSQL_READY_TIMEOUT_SECONDS:-90}"

DB_ORDERS="${MSSQL_DB_ORDERS:-otc_orders}"
DB_FULFILLMENT="${MSSQL_DB_FULFILLMENT:-otc_fulfillment}"
DB_BILLING="${MSSQL_DB_BILLING:-otc_billing}"
DB_NOTIFICATIONS="${MSSQL_DB_NOTIFICATIONS:-otc_notifications}"
APP_USER="${MSSQL_APP_USER:-otc_app}"
APP_PASSWORD="${MSSQL_APP_PASSWORD:?MSSQL_APP_PASSWORD must be set}"
SA_PASSWORD="${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD must be set}"

log() { printf '[otc-mssql-init] %s\n' "$1"; }

# -C trusts the server's self-signed certificate. sqlcmd from mssql-tools18
# defaults to Encrypt=Mandatory, so without it every connection below fails
# certificate validation against a container that only ever has a self-signed
# cert. This is a loopback connection inside the container to its own engine.
sq() { "$SQLCMD" -S localhost -U sa -P "$SA_PASSWORD" -C -b "$@"; }

# ── 1. start the engine ─────────────────────────────────────────────────
log "starting sqlservr"
/opt/mssql/bin/sqlservr &
SQLSERVR_PID=$!

# If the engine dies during bootstrap, do not sit in the readiness loop until
# the timeout: fail immediately with its exit status.
trap 'kill -TERM "$SQLSERVR_PID" 2>/dev/null || true' TERM INT

# ── 2. wait for it to answer a query ────────────────────────────────────
log "waiting up to ${READY_TIMEOUT_SECONDS}s for the engine to answer"
deadline=$(( SECONDS + READY_TIMEOUT_SECONDS ))
until sq -Q "SELECT 1" >/dev/null 2>&1; do
  if ! kill -0 "$SQLSERVR_PID" 2>/dev/null; then
    log "FATAL: sqlservr exited during startup"
    wait "$SQLSERVR_PID"
    exit $?
  fi
  if (( SECONDS >= deadline )); then
    log "FATAL: engine did not become answerable within ${READY_TIMEOUT_SECONDS}s"
    kill -TERM "$SQLSERVR_PID" 2>/dev/null || true
    exit 1
  fi
  sleep 1
done
log "engine is answering after ${SECONDS}s"

# ── 3. bootstrap ────────────────────────────────────────────────────────
# -b makes sqlcmd exit non-zero on the first SQL error, which `set -e` then
# turns into a dead container. A bootstrap that half-worked is worse than one
# that visibly failed: the healthcheck below would keep the container unhealthy
# forever with no explanation in the logs.
if [ ! -f "$INIT_SQL" ]; then
  log "FATAL: init script not found at $INIT_SQL"
  kill -TERM "$SQLSERVR_PID" 2>/dev/null || true
  exit 1
fi

log "running $INIT_SQL"
sq -i "$INIT_SQL" \
   -v DB_ORDERS="$DB_ORDERS" \
      DB_FULFILLMENT="$DB_FULFILLMENT" \
      DB_BILLING="$DB_BILLING" \
      DB_NOTIFICATIONS="$DB_NOTIFICATIONS" \
      APP_USER="$APP_USER" \
      APP_PASSWORD="$APP_PASSWORD"
log "bootstrap complete: $DB_ORDERS, $DB_FULFILLMENT, $DB_BILLING, $DB_NOTIFICATIONS"

# ── 4. hand the foreground back ─────────────────────────────────────────
log "handing over to sqlservr (pid $SQLSERVR_PID)"
wait "$SQLSERVR_PID"
