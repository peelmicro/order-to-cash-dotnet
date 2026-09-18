# impl_n8n_workflows — feature 33 (phase 20, LIGHT)

`sdd: false`. Brief: `/tmp/.../scratchpad/brief_33.md`. No `R<n>` is satisfied
by this feature (`specs/shared/n8n-workflows.md` §9 says so explicitly) —
traceability below is "what was verified", not test-matrix rows.

This phase found `n8n/workflows/*.json` already byte-identical to #7's (leader
fact, not re-derived) and `docker-compose.infra.yml`'s `n8n`/`n8n-init`
services already written, reused from #7. The actual work was: run the
never-yet-run import mechanism against #8's real stack, solve the
host-vs-container reachability gap `scripts/dev-stack.sh` creates (unique to
#8 — #7 never had this problem, see below), port the manual
import/export scripts, and add the two `n8n:import`/`n8n:export`
`package.json` shortcuts `README.md` already lists as waiting on this phase.

## What #7 did, and why it didn't answer the reachability question

`../order-to-cash-nestjs` has no `scripts/dev-stack.sh` at all
(`ls scripts/dev-stack.sh` → "No such file or directory") and its
`progress/impl_n8n_workflows.md` / `progress/review_n8n_workflows.md` never
mention `host.docker.internal` or a host-vs-container gap. #7 always ran its
own Gateway as a container inside `docker-compose.apps.yml`, so the `n8n`
service's hardcoded `http://gateway:${GATEWAY_PORT}` always resolved on the
shared compose network from day one. #8 does not have that luxury yet:
`docker-compose.apps.yml` does not exist until phase 23, and
`scripts/dev-stack.sh` runs the six .NET services (including the Gateway) as
host processes. #7's own history therefore does not answer this question —
it never had to ask it. This confirms the brief's own framing rather than
contradicting it.

## Reachability mechanism chosen, and why

Docker's host-gateway mechanism: `extra_hosts: ["host.docker.internal:host-gateway"]`
on the `n8n` service, plus making `OTC_GATEWAY_URL` overridable
(`${OTC_GATEWAY_URL:-http://gateway:${GATEWAY_PORT:-3001}}` — nested
interpolation, confirmed supported by this Docker Compose build, see below)
instead of the previous hardcoded `"http://gateway:${GATEWAY_PORT:-3001}"`.
The **default value is unchanged** — nothing about the phase-23 full-stack
behaviour moves — the value is only overridden explicitly at `docker compose
up` invocation time, for local `dev-stack.sh`-based verification:

```
OTC_GATEWAY_URL=http://host.docker.internal:3001 docker compose -f docker-compose.infra.yml --profile n8n up -d n8n
```

Chosen over any other approach because it is the standard Linux mechanism
(confirmed live below — `host.docker.internal` is not built into the Linux
Docker Engine the way it is on Docker Desktop, so `extra_hosts` is required,
not optional, on this machine), it needs no change to the workflow JSON
files (the brief's own constraint), and it degrades to a no-op once
`docker-compose.apps.yml` gives `gateway` a real container hostname — the
`extra_hosts` mapping and the `${OTC_GATEWAY_URL:-...}` fallback both stay
inert and harmless from phase 23 onward.

**Live proof the mechanism works, from a cold state**, before relying on it:

```
$ docker exec otcnet-n8n node -e "require('node:dns').lookup('host.docker.internal', (e,a)=>console.log(e?'ERR':'RESOLVED:'+a))"
RESOLVED:172.17.0.1
```

Confirmed nested `${VAR:-${OTHER:-default}}` interpolation is honoured by
this Docker Compose build (29.8.1) with a throwaway test compose file before
relying on it in the real one — default resolves to
`http://gateway:3001` with no shell var set, and to
`http://host.docker.internal:3001` with `OTC_GATEWAY_URL` exported.

**Considered and rejected**: hardcoding `host.docker.internal` as the new
permanent default — rejected because it would silently break the moment
`docker-compose.apps.yml` gives `gateway` a real container hostname, and the
brief's own facts establish the current hardcoded value is deliberately
"wired for the FULL compose stack." Making it overridable, not replacing it,
keeps that property while unblocking this phase's own verification.

## Files touched

- `docker-compose.infra.yml` — `n8n` service only: `OTC_GATEWAY_URL` made
  overridable (default value unchanged) and a new `extra_hosts` entry, both
  with explanatory comments. Nothing else in the file changed.
- `scripts/import-n8n-workflows.sh`, `scripts/export-n8n-workflows.sh` — new,
  ported from #7's own scripts of the same name (`../order-to-cash-nestjs/scripts/`),
  adapting only the container name (`otcnet-n8n`, not #7's `otc-n8n` — see
  `docker-compose.infra.yml`'s own "Why `name: otcnet`" comment for why the
  two stacks never share a namespace) and the error message pointing at
  `pnpm dc:up:infra` (#8 has no `dc:up:apps` yet). Logic, comments and the
  export script's Python portability-stripping step are otherwise unchanged.
- `package.json` — two new scripts, `n8n:import` / `n8n:export`, alongside
  the existing `dc:logs:n8n` line (matching #7's own placement).
- `.env.example` — **not touched**. No new variable was genuinely needed:
  the reachability override is a one-shot invocation-time value
  (`OTC_GATEWAY_URL=http://host.docker.internal:3001 docker compose ... up -d n8n`),
  not a standing local default — defaulting it in `.env.example` would mean
  every fresh clone silently points `n8n` at `host.docker.internal` even
  after phase 23 makes `gateway` a real container hostname, unless someone
  remembers to remove it. The override is documented in the
  `docker-compose.infra.yml` comment instead.
- `n8n/workflows/*.json` — **not touched**, confirmed unchanged (see Gate
  results below): the brief's own leader-verified fact that these are
  already byte-identical to #7's held throughout.
- `feature_list.json` — status line only, `pending` → `in_review`. Confirmed
  via `git diff feature_list.json` that no other line changed.
- `README.md` — **not touched**, deliberately: the brief's own "Touch only"
  list does not name it, even though line 148 ("`n8n:import`/`n8n:export`
  wait for phase 20") is now stale. Flagging this as a finding for the
  leader/maintainer to route (a one-line README update, or fold into a
  future phase) rather than widening this feature's scope past what the
  brief authorised.
- Screenshots for the README — **not done**. Judged out of the light-process
  budget for a verification-focused phase; the live evidence below (exact
  commands and JSON responses) stands in for them. Flagging per the brief's
  own "optional... note whether you did this and why not."

## Verification performed, in order, with commands and output

All from a cold state, confirmed first: `docker ps` empty, `pgrep` for
dev-stack processes empty, before starting.

### 1. Import mechanism, cold start

```
$ pnpm dc:up:infra          # docker compose --profile n8n up -d, from cold
... 18 containers, all Healthy
$ docker logs otcnet-n8n-init
n8n-init: importing n8n/workflows/*.json (mounted at /home/node/workflows)...
Importing 4 workflows...
Successfully imported 4 workflows.
n8n-init: done. Workflows are imported INACTIVE — activate from the n8n UI to run them.
$ docker inspect otcnet-n8n-init --format '{{.State.ExitCode}}'
0
```

Queried n8n's own SQLite directly (`node:sqlite`, built into the n8n image's
Node runtime — no `sqlite3` CLI present):

```json
[
  {"id":"otcBurst","name":"4 - Burst (Order-To-Cash)","active":0,"isArchived":0},
  {"id":"otcOrderGenerator","name":"1 - Order Generator (Order-To-Cash)","active":0,"isArchived":0},
  {"id":"otcPaymentRobot","name":"2 - Payment Robot (Order-To-Cash)","active":0,"isArchived":0},
  {"id":"otcStockReplenishment","name":"3 - Stock Replenishment (Order-To-Cash)","active":0,"isArchived":0}
]
```
COUNT 4 — the four committed ids, all inactive, no strays.

### 2. Idempotency — ran `pnpm dc:up:infra` a second time

`otcnet-n8n-init` re-ran (one-shot, `restart: "no"`, same
"reassert-on-every-`up`" shape as `kafka-init`): logs show the import output
twice, exit code `0` again. Re-queried the same SQLite table afterward —
**still exactly 4 rows, same 4 ids, still all `active=0`**. No duplicates.

### 3. Manual import/export scripts, ported and proven

```
$ cp n8n/workflows/*.json <snapshot>/
$ pnpm n8n:import
Importing n8n workflows from /home/node/workflows into container otcnet-n8n...
Importing 4 workflows...
Successfully imported 4 workflows.
$ pnpm n8n:export
Exporting n8n workflows from container otcnet-n8n...
  otcPaymentRobot -> n8n/workflows/2-payment-robot.json
  otcBurst -> n8n/workflows/4-burst.json
  otcOrderGenerator -> n8n/workflows/1-order-generator.json
  otcStockReplenishment -> n8n/workflows/3-stock-replenishment.json
$ diff -r <snapshot> n8n/workflows
(no output)
$ git status --short n8n/
(no output)
```

Round-trip is a byte-identical no-op — `n8n/workflows/*.json` genuinely
unchanged by this feature, as the brief required.

### 4. A workflow runs against #8's real stack

Chose the **burst** workflow (webhook trigger) over the three schedule
workflows: it can be fired synchronously and deterministically (`POST
/webhook/otc-burst`) rather than waiting out a 45s/120s/300s interval, and
all four workflows share the identical login/Gateway-call/`$env` pattern —
one webhook run proves the shared mechanism (reachability, auth, the
`OTC_GATEWAY_URL` override) that all four depend on identically.

```
$ pnpm dc:up:infra                                    # (already up from step 1/2)
$ scripts/dev-stack.sh start                          # six .NET services + web, HOST processes
  [OK] Gateway answers http://localhost:3001/health/ready
  [OK] web answers http://localhost:3010/login
$ OTC_GATEWAY_URL=http://host.docker.internal:3001 \
  docker compose -f docker-compose.infra.yml --profile n8n up -d n8n   # recreate with the override
$ docker exec otcnet-n8n env | grep OTC_GATEWAY_URL
OTC_GATEWAY_URL=http://host.docker.internal:3001
$ docker exec otcnet-n8n wget -qO- http://host.docker.internal:3001/health/ready
{"status":"up","checks":{"rpcTransport":{"status":"up"},"readModel":{"status":"up"}}}
```

Published `otcBurst` via the n8n CLI, restarted the container (this n8n
version's own CLI warning — "changes will not take effect if n8n is
running... restart n8n" — held here; unlike #7's reviewer's finding for
webhooks on their instance, my first attempt without a restart 404'd, so I
restarted and re-confirmed rather than assuming):

```
$ docker exec otcnet-n8n n8n publish:workflow --id=otcBurst
$ docker restart otcnet-n8n   # ... healthy
$ curl -s -X POST http://localhost:5678/webhook/otc-burst -d '{"count":3,"compensationRatio":0}'
{"enabled":true,"requested":3,"placed":1,"refused":2,"orderReferences":["ORD-000043"]}
```

Confirmed the order is real, through the Gateway API (not just the
workflow's own claim):

```
$ GET /orders/ORD-000043 (via list + reference match)
{
  "orderReference": "ORD-000043", "status": "invoiced", "currency": "GBP",
  "retailer": {"code":"AldiGb"}, "company": {"code":"DUTCHGOODS"},
  "totals": {"totalAmount": 9707}
}
```

Order count went 69 → 70 across this one call. This is the live proof the
reachability mechanism and the workflow both work end to end against #8's
real stack, not just "should work."

**Restored state**: `n8n unpublish:workflow --id=otcBurst`, restarted the
container again, re-confirmed `active=0` for all four workflows and the
webhook 404s again — matching the state `dc:up:infra` originally produced.

### 5. Removing n8n does not break the stack

```
$ docker rm -f otcnet-n8n otcnet-n8n-init
$ pnpm dc:up:infra:no-n8n
$ docker ps --format '{{.Names}}' | wc -l
12    # mssql, mongodb, kafka, kafka-console, kafka-exporter, kafka-init,
      # nats, mailpit, otel-collector, jaeger, prometheus, grafana — none unhealthy
```

Placed and paid a real order through the Gateway REST API directly (no n8n
involved anywhere in this sequence, confirmed by `docker ps` showing zero
`n8n`-named containers throughout):

```
$ POST /orders {retailerCode: AldiDe, companyCode: IBERFOODS, currency: EUR, lines:[{PRD-0002, qty:2}]}
→ ORD-000044, status: placed
... saga runs ...
→ status: invoiced, invoice INV-000030 issued
$ POST /invoices/{id}/payments {paymentReference: PAY-INV-000030, amount:{3698,EUR}, source: robot}
→ 201, outcome: accepted
$ GET /orders → ORD-000044: status "completed"
```

`ORD-000044` reached `completed` with n8n never started during this
sequence.

**No other service depends on n8n**: `grep -n "depends_on" docker-compose.infra.yml -A2`
shows exactly one hit naming `n8n:` — `n8n-init`'s own dependency on `n8n`
(itself an `n8n`-profile service; expected). No other service in the file
references `n8n` or `n8n-init` in a `depends_on:` block.

`docker compose -f docker-compose.infra.yml config --services` (no profile)
lists the same 12 non-n8n services with and without the profile flag being
absent — `n8n`/`n8n-init` only appear with `--profile n8n`.

## Acceptance criteria — live evidence

| Criterion | Evidence |
|---|---|
| "order generator, bank robot, stock replenishment, burst" | Pre-existing, unchanged — four committed workflows, byte-identical to #7's, confirmed by leader's `diff -rq` and re-confirmed by this phase's own untouched round-trip (`git status --short n8n/` empty after import+export) |
| "auto-imported on container startup" | `otcnet-n8n-init` ran on both `pnpm dc:up:infra` invocations (cold start and idempotency re-run), exit 0 both times, exactly 4 workflow rows both times, no duplicates |
| "removing n8n does not break the stack" | 12 containers healthy with zero n8n; `ORD-000044` placed, paid, reached `completed` through the real Gateway with n8n never started |

## Gate results

- `./init.sh` — exits 0, both before and after this session's changes.
- `docker compose -f docker-compose.infra.yml --profile n8n config -q` —
  valid.
- Round-trip (`pnpm n8n:import` → `pnpm n8n:export`) — no diff, `git status`
  on `n8n/` unchanged throughout.
- Did not run `pnpm quality` / `dotnet test` — no `src/`, `tests/` or
  `apps/web` file touched by this feature (LIGHT classification, brief's own
  scope); nothing new for the backend/web test suites to cover.
- Teardown: `scripts/dev-stack.sh stop` (all six services + web confirmed
  stopped, no port held), `docker compose -f docker-compose.infra.yml
  --profile n8n --profile sonar down` (all containers removed, network
  removed). Confirmed after: `docker ps -a | grep otcnet` → none;
  `pgrep -fal "dotnet.*src/(Gateway|Orders|...)"` → none; `pgrep -fal "next
  start|next-server"` → none.

## What I could not fully do, and why

- Did not exercise the three schedule-triggered workflows (order generator,
  payment robot, stock replenishment) live end-to-end — justified above:
  they share the identical login/env/Gateway-call shape with the burst
  workflow that was run live, and the one thing genuinely unique to them
  (schedule-trigger activation needing a restart, vs. webhook activation)
  was itself observed directly while proving the burst case (the CLI's
  restart warning held for the webhook here too, on this n8n version/
  instance — noted as a difference from #7's own review finding for
  webhooks specifically, not assumed away).
- Screenshots for the README — not produced; see "Files touched" above for
  why, and the disposition (out of the light-process budget; live command
  evidence substitutes).

## Surprises

1. **`host.docker.internal` did not resolve without `extra_hosts` on this
   Linux Docker Engine** — confirmed via `getent`/`node:dns` before adding
   the mapping, consistent with Docker's own documentation that the name is
   a Docker Desktop convenience, not a Linux Engine default; `extra_hosts:
   host.docker.internal:host-gateway` is the documented way to get it on
   Linux, and it was necessary here, not just belt-and-braces.
2. **n8n 2.36.2's `publish:workflow` restart requirement held for the
   webhook trigger on this instance**, where #7's own re-review
   (`progress/review_n8n_workflows.md`, D8) found the opposite for
   `otcBurst` specifically ("Webhook... returned 200 and executed with no
   restart"). Rather than assume #7's finding transfers, I tested it live
   here first (got a 404), then restarted and re-confirmed success — the
   restart-then-verify path is the one this progress record relies on, not
   the assumption.
3. **Nested `${VAR:-${OTHER:-default}}` interpolation needed a standalone
   check** before trusting it in the real compose file — confirmed working
   on Docker Compose 29.8.1 via a throwaway test file first.

## Not done (explicit scope boundary, per the brief)

Did not touch `n8n/workflows/*.json`, `specs/shared/`, `CLAUDE.md`,
`src/`/`tests/`, or `.env`. Did not run any git command that writes the
index or working tree. Did not commit.
