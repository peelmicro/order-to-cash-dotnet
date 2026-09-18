# Firing the three untested n8n workflows for real — phase 25 final-checkpoint gap (C7), LIGHT

Closes the gap the phase-25 final-checkpoint review found in `CHECKPOINTS.md`'s
C7 box: id 33's own closing record (`progress/history.md`, "Id 33") only ever
fired `4-burst.json` live against the real Gateway; `1-order-generator.json`,
`2-payment-robot.json` and `3-stock-replenishment.json` were inferred to work
rather than actually triggered. This session activated each of the three on
its own real schedule, against the real .NET Gateway, and observed a real
effect in each target service's own database, exactly as C7 claims.

No `src/`, `tests/`, `apps/web/`, `specs/shared/` or `feature_list.json` file
was touched. `n8n/workflows/*.json` were not touched (only imported/activated/
deactivated inside the running container, per the existing tooling id 33
built). This is verification, not a new feature — LIGHT, no separate
reviewer, per the brief.

## Result

**PASS.**

- `1-order-generator`: fired on its own 20s schedule (interval temporarily
  lowered, restored — see below) and placed a real order, `ORD-000009`,
  through the real Gateway `POST /orders`, confirmed by `GET /orders` (order
  count 8 → 9) and by reading the order back to `invoiced` with a real
  `stock.reserved.v1` → `credit.approved.v1` → `order.confirmed.v1` →
  `order.despatched.v1` → `invoice.issued.v1` event chain.
- `2-payment-robot`: fired on its own 20s schedule and paid two real unpaid
  invoices (`INV-000006`, `INV-000007`) through the real Gateway `POST
  /invoices/{id}/payments`, confirmed by `GET /invoices?status=issued`
  dropping from 2 → 0 and by both orders reading back `status: "completed"`
  with a real `payment.received.v1` event carrying the workflow's own
  deterministic `paymentReference` (`PAY-INV-000006`, `PAY-INV-000007`).
- `3-stock-replenishment`: fired on its own 20s schedule and topped up a real
  low-stock item in Fulfillment's own database, confirmed by `GET /stock`
  going `units: 15 → 115` (the workflow's `STOCK_REPLENISH_TOP_UP_UNITS=100`)
  for `(LONDONTOOLS, PRD-0010)`, the exact row driven below its
  `lowStockThreshold=20` moments earlier by a real order placed directly
  through the Gateway.

Each workflow was activated alone, observed firing, then deactivated and
confirmed quiescent (a second, full interval-length wait with no further
change) before the next was activated — so each effect is attributable to
the workflow that was live when it happened, not to overlap between two
active schedules.

## What was already true and reused, not re-derived

Per the brief's "facts already confirmed by the leader": all three are
`scheduleTrigger`-based (confirmed again here, incidentally, by watching them
actually fire on their configured intervals), `n8n/workflows/*.json` were
read but not edited, and the container-to-host bridge
(`OTC_GATEWAY_URL=http://host.docker.internal:${GATEWAY_PORT}` plus
`extra_hosts: host.docker.internal:host-gateway`) and the
publish/restart/unpublish/restart activation recipe are exactly id 33's own,
reused verbatim (`docker exec otcnet-n8n n8n publish:workflow --id=<id>` /
`n8n unpublish:workflow --id=<id>`, then `docker restart otcnet-n8n` — this
n8n version's own CLI warning that changes need a restart held again here,
consistent with id 33's finding for the burst workflow).

## Sequence run, with commands and numbers

1. **Cold-state confirmation.** `docker ps -a` / `pgrep` before starting:
   no `otcnet-*` containers running, no stack processes alive (only unrelated
   pre-existing stopped containers and this machine's own IDE/MSBuild
   daemons).
2. **Infra up with n8n**: `pnpm dc:up:infra` — 18 containers up, including
   `otcnet-n8n` and the one-shot `otcnet-n8n-init` (auto-import). Confirmed
   auto-import: `docker exec otcnet-n8n n8n list:workflow` → all four
   workflow ids present (`otcOrderGenerator`, `otcPaymentRobot`,
   `otcStockReplenishment`, `otcBurst`).
3. **Backend stack up as host processes** (dev-stack.sh's own build+seed+
   service-start path, web app skipped since only the Gateway REST API was
   needed for this verification — an equivalent, narrower invocation than
   `pnpm stack:start`):
   - `dotnet build OrderToCash.sln` — succeeded, 0 warnings, 0 errors.
   - `scripts/dev-stack.sh env dotnet run --no-build --project src/Seed` —
     `orders=7 orderItems=12`, `fulfillment: stock=215`,
     `billing: credits=154 invoices=5 payments=5` (clean baseline: 7 seeded
     orders, 0 invoices left `issued` — the 5 seeded invoices were already
     matched by 5 seeded payments).
   - `scripts/dev-stack.sh start-service <Orders|Fulfillment|Billing|
     Notifications|Projector|Gateway>` for each of the six services.
   - `GET /health/ready` → `{"status":"up", ...}`.
4. **Host bridge**: `OTC_GATEWAY_URL=http://host.docker.internal:3001 docker
   compose -f docker-compose.infra.yml --profile n8n up -d n8n` (recreates
   the n8n container with the override) — confirmed inside the container:
   `OTC_GATEWAY_URL=http://host.docker.internal:3001` and `wget -qO-
   http://host.docker.internal:3001/health/ready` answered `{"status":"up",
   ...}`.
5. **Interval speed-up, disclosed** (see "Interval change" below).
6. **Baseline reads**, authenticated as `operator` against the real Gateway:
   `GET /orders?pageSize=1` → `page.total: 7`; `GET
   /invoices?status=issued&pageSize=50` → `page.total: 0`; `GET
   /stock?belowThreshold=true` → `items: []` (nothing naturally low —
   the seed's `InitialUnitsOnHand=500` against a `LowStockThreshold=20`
   never gets an item there on its own).
7. **Manufactured a real low-stock condition**, through the real Gateway
   (not n8n, not a stub): a single manual `POST /orders` for
   `(retailer AldiGb/GBP, company LONDONTOOLS, product PRD-0010, quantity
   485)` against a row that started at `units: 500, reservedUnits: 0`.
   Placed as `ORD-000008` (orders 7 → 8), reached `status: "invoiced"`
   automatically, and drove `(LONDONTOOLS, PRD-0010)` to `units: 15,
   availableUnits: 15` — below its `lowStockThreshold: 20`, confirmed by
   `GET /stock?companyCode=LONDONTOOLS&productCode=PRD-0010`.
8. **`3-stock-replenishment` activated**: `docker exec otcnet-n8n n8n
   publish:workflow --id=otcStockReplenishment` + `docker restart
   otcnet-n8n`. Polled `GET /stock?companyCode=LONDONTOOLS&
   productCode=PRD-0010` every 3s: `units: 15` (attempt 1) →
   `units: 115` (attempt 2, ~6s after restart, matching the 20s schedule and
   the container's own startup/restart window) — `+100`, exactly
   `STOCK_REPLENISH_TOP_UP_UNITS`.
   **Deactivated**: `n8n unpublish:workflow --id=otcStockReplenishment` +
   restart; waited a further full 25s (> one interval) — `units` stayed at
   `115`, confirming it stopped firing.
9. **`1-order-generator` activated**: baseline `GET /orders` →
   `page.total: 8`. `docker exec otcnet-n8n n8n publish:workflow
   --id=otcOrderGenerator` + restart. Polled `GET /orders?pageSize=1` every
   3s: `total` stayed `8` for 12 attempts, then became `9` on attempt 13
   (~39s after restart — two 20s cycles, consistent with the container's own
   startup lag before the first tick). The new row: `ORD-000009`, retailer
   `LeroyMerlinEs`, company `OUTILFRANCE`, 4 lines, `status: "invoiced"`
   already (automatic saga chain completed within ~1s of placement).
   **Deactivated**: unpublish + restart; waited a further 25s — `total`
   stayed `9`, confirming it stopped firing.
10. **`2-payment-robot` activated**: baseline `GET
    /invoices?status=issued&pageSize=50` → `page.total: 2`
    (`INV-000006` for `ORD-000008`, `INV-000007` for `ORD-000009` — one from
    the manual low-stock order, one from the order-generator's own order,
    both genuinely unpaid). `docker exec otcnet-n8n n8n publish:workflow
    --id=otcPaymentRobot` + restart. Polled `GET
    /invoices?status=issued&pageSize=50` every 3s: `total: 2` (attempt 1) →
    `total: 0` (attempt 2, ~6s after restart) — both invoices paid in one
    run. Confirmed at the order level: both `ORD-000008` and `ORD-000009`
    now read `status: "completed"`, each carrying a real
    `payment.received.v1` event (occurred `15:00:00.4xx`) whose
    `paymentReference` (`PAY-INV-000006`, `PAY-INV-000007`) matches the
    workflow's own deterministic `` `PAY-${invoice.invoiceReference}` ``
    scheme exactly — this is the concrete tie from "an invoice got paid
    somehow" to "the payment-robot's own code path paid it", not just a
    status-field coincidence.
    **Deactivated**: unpublish + restart; waited a further 25s —
    `orders.total` stayed `9`, `issued invoices` stayed `0`, and
    `(LONDONTOOLS, PRD-0010).units` stayed `115` — the whole environment
    quiescent with all three back off.
11. **All four workflows confirmed inactive**, not just the three deactivated
    here: `docker exec otcnet-n8n n8n export:workflow --all --separate
    --output=/tmp/wfcheck` then grepped `"active":` in each exported file —
    `otcBurst: false`, `otcOrderGenerator: false`, `otcPaymentRobot: false`,
    `otcStockReplenishment: false`. Matches the state `dc:up:infra` produces
    on import (every imported workflow lands inactive by n8n's own default).
    The temporary export directory was removed from the container
    afterward.

## Interval change, disclosed and restored

To observe each schedule firing inside a practical verification window
rather than waiting out the committed 45s/120s(+2min age)/300s intervals,
`.env` (gitignored, not `.env.example`) was temporarily edited before step 4:

| Variable | Committed value | Temporary value |
|---|---|---|
| `ORDER_GENERATOR_INTERVAL_SECONDS` | 45 | 20 |
| `PAYMENT_ROBOT_INTERVAL_SECONDS` | 120 | 20 |
| `PAYMENT_AGE_MINUTES` | 2 | 0 |
| `STOCK_REPLENISH_INTERVAL_SECONDS` | 300 | 20 |

All four were restored to their committed values before finishing this
session. Confirmed two ways:

```
$ diff <(grep -E "ORDER_GENERATOR_INTERVAL_SECONDS|PAYMENT_ROBOT_INTERVAL_SECONDS|PAYMENT_AGE_MINUTES|STOCK_REPLENISH_INTERVAL_SECONDS" .env.example) \
       <(grep -E "ORDER_GENERATOR_INTERVAL_SECONDS|PAYMENT_ROBOT_INTERVAL_SECONDS|PAYMENT_AGE_MINUTES|STOCK_REPLENISH_INTERVAL_SECONDS" .env)
MATCH - .env restored to .env.example values
$ git diff .env.example
(no output)
```

`.env.example` itself was never edited (`git diff .env.example` was clean
before this session and stayed clean throughout — confirmed at the start and
the end). `n8n/workflows/*.json` were not edited either — only
published/unpublished and exported/deleted from a throwaway path inside the
running container, none of which touches the repository.

## Teardown, confirmed empty

```
$ scripts/dev-stack.sh stop
  [OK]   Gateway/Projector/Notifications/Billing/Fulfillment/Orders stopped
  [OK]   nothing left running, no port this stack bound is still held
$ docker compose -f docker-compose.infra.yml --profile n8n down
  (all 12 containers + otcnet-n8n removed, network otcnet-net removed)
$ docker ps --filter "name=otcnet"
CONTAINER ID   IMAGE   COMMAND   CREATED   STATUS   PORTS   NAMES
(none running)
$ docker ps -a --filter "name=otcnet"
otcnet-sonarqube   Exited (130)   (pre-existing, unrelated — was already
                                   stopped before this session started)
$ pgrep -fl "OrderToCash\.(Gateway|Orders|Fulfillment|Billing|Notifications|Projector)"
(no match)
$ pgrep -fl "next-server|next dev|next start"
(no genuine match — only this shell's own command line containing the
search string, a self-match, not a real process)
```

Environment left exactly as found: no `otcnet-*` container running, no
service or web process alive, `.env` and `.env.example` both back at their
committed values, `n8n/workflows/*.json` untouched
(`git status --short n8n/` empty throughout — not re-checked at the very end
since the files were never opened for writing this session, only read).

## Files touched this session

- `.env` — temporarily, then restored (gitignored, no commit needed or
  possible).
- `progress/impl_n8n_three_workflows_fired.md` — this file.

No `src/`, `tests/`, `apps/web/`, `specs/shared/`, `feature_list.json`, or
`n8n/workflows/*.json` file was touched, per the brief's rules.

## What I could not fully do, and why

- Could not read n8n's own execution history to attach an n8n-side execution
  id to each fired run — this n8n image ships no `sqlite3` binary and its
  CLI (checked with `n8n --help`) has no `list:execution`/`export:execution`
  command in this version. Not a gap in the proof: every claim above is
  independently confirmed at the Gateway/domain level (order counts, invoice
  status, stock units, and — for the payment-robot specifically — the
  deterministic `paymentReference` that only that workflow's own code
  produces), which is the same evidentiary standard id 33 used for the burst
  workflow.
- Did not re-verify the `depends_on: n8n` / removing-n8n-doesn't-break-the-
  stack claims — out of this brief's scope (already proven live by id 33 and
  not reopened by the C7 gap this session closes).

## Recommendation for `CHECKPOINTS.md`

C7 ("all four workflows fire green against the .NET Gateway") is now true as
stated, with all four workflows having been fired live at least once against
the real Gateway across id 33's session and this one: `4-burst` (id 33),
`1-order-generator`, `2-payment-robot`, `3-stock-replenishment` (this
session). Recommend updating C7's box to point at both records
(`progress/impl_n8n_workflows.md` for the burst workflow and this file for
the other three) rather than leaving it pointing only at id 33's partial
proof.
