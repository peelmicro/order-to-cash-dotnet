# `observability_dashboards` (id 35, phase 22) — verification report

LIGHT-classified (CLAUDE.md "Cost discipline" — infra/observability verification, same
shape as phase 20's n8n work). Sole implementer, no separate reviewer round; this
report is the evidence the leader checks directly.

## Summary

**PASS**, with one real, small, justified fix applied to observability config (not
`src/`): four of the dashboard's five PromQL queries named custom-metric suffixes
(`_milliseconds`, `_ratio`) that #8's .NET OTel instruments never emit, so those
panels were empty before the fix. Fixed by editing the four `expr` strings (and one
`description` string) in `infra/grafana/dashboards/order-to-cash-overview.json` to
the metric names actually exported. All five panels now show real, non-empty data
from a real order I placed and drove through the full saga; the dashboard is
auto-provisioned (confirmed present via the Grafana API before touching anything,
and confirmed to auto-reload the fix within its own 30 s `updateIntervalSeconds`,
never a manual import); and one real distributed trace — 42 spans, 6 services,
depth 26 — was captured spanning the whole automatic saga (HTTP → NATS → MS-SQL →
Kafka → every consumer), corroborated independently at the database level (not
merely trusted from Jaeger's own grouping).

## What I verified, and how

### 1. Cold start, no pre-existing state

`docker ps -a` and `pgrep` before starting showed nothing of this project's running
(only long-exited containers from unrelated work, and no `dotnet`/`node` process for
this repo). `scripts/dev-stack.sh start` brings up `docker-compose.infra.yml` with
**no `--profile` flag** — confirmed by reading `docker-compose.infra.yml:421` (`n8n`)
and `:514` (`n8n-init`), both `profiles: ["n8n"]` — so n8n correctly stayed off;
`docker ps` after start listed 11 `otcnet-*` containers, no `otcnet-n8n`. Grafana
bound `3030:3000` (`GRAFANA_HOST_PORT` default, `.env.example:160` and
`docker-compose.infra.yml:394`) — confirmed, not assumed.

### 2. Real orders through the full saga

Logged in via `POST /auth/login` (operator / `otc_operator_dev_password_change_me`,
`.env.example:206-207`), then placed two real orders through the real Gateway HTTP
API (no test doubles):

- **Happy path**: `CarrefourEs` / `IBERFOODS` / EUR, `PRD-0002` × 2 @ 1849 → total
  3698 (not `.99`). `orderId a0295171-339f-4a74-88e3-035684e1ad98`,
  `ORD-000045`. Reached `invoiced` immediately on poll, registered a real payment
  (`POST /invoices/{id}/payments`, source `operator`), reached `completed`. Read
  back via `GET /orders/{id}`: the full event timeline (`order.placed.v1` →
  `stock.reserved.v1` → `credit.approved.v1` → `order.confirmed.v1` →
  `order.despatched.v1` → `invoice.issued.v1` → `payment.received.v1` →
  `credit.released.v1` → `order.completed.v1`), `DES-000032`, `INV-000031`.
- **`.99` compensation path**: same retailer/company, `PRD-0001` × 1 @ 24999 (ends
  in `99`, `SimulatorCreditDecision`'s R42 unconditional-refusal rule). `orderId
  51b0f2d0-28ae-4206-bc5b-0d2dc13d620c`, `ORD-000046`. Reached `cancelled`
  immediately on poll.

### 3. Grafana — auto-provisioned, panels real before and after a defect fix

**Auto-provisioned, confirmed before touching anything**: `GET
/api/search?type=dash-db` (Basic auth admin/`otc_grafana_dev_password`,
`.env.example:161-162`) returned the dashboard (`uid: otc-overview`) immediately
after the stack came up — no UI import step taken by me, none possible
(`allowUiUpdates: false` in `infra/grafana/provisioning/dashboards/dashboards.yml`).

**A real, live defect found and fixed** (small, justified, observability-config
only, per the brief's explicit allowance — "a mislabeled Prometheus scrape target"
is its own example; this is the dashboard-JSON equivalent):

- `curl .../api/v1/label/__name__/values` against the live Prometheus showed the
  real exported metric names: `otc_saga_completion_ms_bucket`,
  `otc_request_latency_ms_bucket`, `otc_fact_processing_latency_ms_bucket`,
  `otc_outbox_lag_ms`, `otc_dlq_depth` — **none** carrying the `_milliseconds` or
  `_ratio` suffix the dashboard's four PromQL `expr` strings named
  (`otc_saga_completion_ms_milliseconds_bucket`,
  `otc_request_latency_ms_milliseconds_bucket`,
  `otc_fact_processing_latency_ms_milliseconds_bucket`,
  `otc_outbox_lag_ms_milliseconds`, `otc_dlq_depth_ratio`). Direct query of the
  original `expr` strings against live Prometheus returned `"result": []` — empty,
  confirmed, not assumed.
- **Root cause, read from code, both sides**: none of #8's six `Meter.CreateHistogram`/
  `CreateGauge` calls (`src/*/Infrastructure/Observability/Telemetry.cs`,
  `src/Orders/Infrastructure/Observability/SagaCompletionRecorder.cs`) pass a
  `unit:` parameter, so the OTel Collector's Prometheus exporter never appends a
  unit suffix. #7's own equivalent (`apps/orders/src/infrastructure/observability/
  metrics.ts:43,50,57,64`) passes `unit: 'ms'` / `unit: '1'` on every instrument,
  which the JS OTel SDK does carry through to the same collector's Prometheus
  exporter, producing the `_milliseconds`/`_ratio` suffixes #7's own dashboard
  correctly queries against (`order-to-cash-nestjs/infra/grafana/dashboards/
  order-to-cash-overview.json`, same five `expr` strings, byte-identical to what
  #8's dashboard had before this fix). **#7 relied on its metric-creation calls
  declaring an OTel `unit`; #8's port of the dashboard JSON carried the byte-identical
  PromQL forward without carrying the byte-identical `unit:` declarations in
  `src/`, and #8's `src/` code was never asked to.** This is a ported-idiom gap
  between the dashboard artefact and #8's own metric-emission code, not a
  dashboard-authoring typo local to #8 — recorded here since id 35 is `sdd: false`
  (no `design.md` to carry the ledger row).
- **The fix, scoped to observability config only** (never `src/`, per the brief's
  explicit rule): five `expr` strings in
  `infra/grafana/dashboards/order-to-cash-overview.json` had their spurious
  `_milliseconds`/`_ratio` suffixes removed (saga duration ×2 queries,
  per-service-latency ×2 queries, outbox lag, DLQ depth — 6 `expr` edits across 5
  panels since the per-service-latency panel has two targets), plus one
  `description` string (DLQ depth panel) that also named the wrong metric.
  `python3 -c "import json; json.load(...)"` confirmed the JSON stayed valid.
  `git diff --stat` on the file: `14 +++++++-------` (7 insertions, 7 deletions,
  i.e. 7 lines touched) — matches the 6 `expr` + 1 `description` count exactly.
- **Verified corrected, twice**: (a) directly against Prometheus's own API
  (`histogram_quantile(0.95, sum(otc_saga_completion_ms_bucket) by (le, outcome))`
  → real `cancelled`/`completed` series with real p95/p50 values; the other four
  corrected queries likewise real, non-empty, keyed by `exported_job` = `gateway`/
  `orders`/`billing`/`fulfillment`/`notifications`/`projector` as appropriate); (b)
  through Grafana's own datasource proxy (`/api/datasources/proxy/uid/
  PBFA97CFB590B2093/api/v1/query`, the exact path a rendered panel uses) — same
  real results.
- **Auto-reload proven, not assumed**: polled `GET /api/dashboards/uid/otc-overview`
  every 3 s after saving the file; still served the OLD (`_milliseconds`/`_ratio`)
  `expr` strings for two polls, then served the corrected ones on the third — inside
  `infra/grafana/provisioning/dashboards/dashboards.yml`'s own
  `updateIntervalSeconds: 30`. **No Grafana restart, no manual re-import, no
  container recreation** — the running `otcnet-grafana` container picked the edit
  up from its existing read-only bind mount on its own schedule.
- **A real screenshot of the rendered dashboard**, captured with a real headless
  browser (`@playwright/test`, already vendored under `apps/web/node_modules`) doing
  a real form login against Grafana and navigating to the dashboard URL, saved to
  `progress/evidence/observability_dashboards_grafana.png`. All five panels render
  visible, non-empty series: Saga duration (`cancelled p95`/`p50`, `completed
  p95`/`p50` — the `.99` order's fast cancellation and the happy-path order's full
  saga are visibly different durations, proving the `outcome` label split works);
  Per-service latency (six real Gateway endpoints plus more below the fold);
  Kafka consumer lag (real consumer groups/topics, all caught up at 0, one
  transient spike visible); Outbox lag (`billing`/`fulfillment`/`orders`, all 0 —
  fully drained); DLQ depth (`orders` at a nonzero value from unrelated prior
  activity on this machine's persistent dev Kafka volume — a real broker-reported
  count, not fabricated, demonstrating the panel renders a genuine nonzero series
  correctly, though it predates and is unrelated to this session's own two orders).

### 4. Jaeger — one real distributed trace, captured

**Every hop confirmed to share one trace id, at the database level — never merely
inferred from Jaeger's own grouping** (this repository's phases-15/16 history
records a real fast-path trace-context gap that once produced three different trace
ids for one order; feature 27/`observability_reliability` fixed it and is recorded
APPROVED in `progress/history.md` — re-verified live here, not assumed still true):

```
otc_orders.outbox:      order.placed.v1     00-aa04c80f...-0ea6c0a2...  (happy-path order)
otc_orders.outbox:      order.confirmed.v1  00-aa04c80f...-7edf1964...  (SAME trace id)
otc_fulfillment.outbox: stock.reserved.v1   00-aa04c80f...-ff5e78d3...  (SAME trace id)
otc_fulfillment.outbox: order.despatched.v1 00-aa04c80f...-e5e92df5...  (SAME trace id)
otc_billing.outbox:     credit.approved.v1  00-aa04c80f...-de8a0a2f...  (SAME trace id)
otc_billing.outbox:     invoice.issued.v1   00-aa04c80f...-5069803c...  (SAME trace id)
otc_orders.outbox:      order.completed.v1  00-b63bb34d...-15b76e40...  (DIFFERENT — see below)
otc_billing.outbox:     payment.received.v1 00-b63bb34d...-811c8f40...  (matches completed's trace)
otc_billing.outbox:     credit.released.v1  00-b63bb34d...-811c8f40...  (matches completed's trace)
```

Queried directly against the real MS-SQL rows (`docker exec otcnet-mssql
/opt/mssql-tools18/bin/sqlcmd ... SELECT correlation_id, event_type, trace_parent
FROM outbox WHERE correlation_id = '<orderId>'`), not from any in-app claim. The
`.99` order's two facts (`order.placed.v1`, `order.cancelled.v1`) likewise share
ONE trace id (`dcb7cd8e...`), confirmed the same way.

**The `order.completed.v1`/`payment.received.v1`/`credit.released.v1` trace being
DIFFERENT from the automatic-saga trace is correct, not a gap**: the payment step
is a genuinely separate external stimulus — a second, distinct `POST
/invoices/{id}/payments` HTTP call the operator makes later — and W3C trace context
correctly starts a new root trace for a new inbound request with no parent context
carried over. This is exactly what `SagaEndToEndVerificationTests`
(`Criterion5_TraceContext...`) asserts scope over too: it checks `order.placed.v1`/
`stock.reserved.v1`/`credit.approved.v1` — the automatic chain — share one trace,
never claiming the payment-triggered tail does.

**Jaeger UI confirms the same grouping independently**: `GET
/api/traces/aa04c80fa13a41b1f79f1e5240f23ee8` returned **one trace, 42 spans,
across all 6 services** (`gateway`, `orders`, `fulfillment`, `billing`, `projector`,
`notifications`) — every service this repository runs. A real screenshot of the
Jaeger UI's own trace-detail view, captured the same way as the Grafana screenshot
(a real headless browser navigating to `/trace/<id>`), is saved to
`progress/evidence/observability_dashboards_jaeger_trace.png` — it shows the
service list, **Depth 26**, **Total Spans 42**, and the full expanded span tree
from `gateway: POST /orders` down through `orders rpc orders.create` → `fulfillment
rpc fulfillment.stock.check` → write-model transactions → `outbox.publish` →
`consume <fact>` fan-out to `projector`/`notifications`/`orders` → `orders dispatch
<Command>` → the next service's `rpc` → … all the way to `invoice.issued.v1`'s
three-way fan-out.

### Span count/depth vs. #7's 22/11 — a real, explained difference, not a defect

#8: **42 spans, depth 26, 6 services.** #7 (per the external Plan doc's own
comparison target, and independently re-derived from `order-to-cash-nestjs/
progress/impl_observability_dashboards.md:78-97`'s own span tree): **22 spans,
depth 11 (its own fix round; before that fix it was `1 span, 1 service` — a
production gap `progress/impl_observability_dashboards.md` there documents finding
and repairing), 6 services.**

The service count matches (both assessments run the same six services and the same
trace visits all of them). **The span count/depth difference is instrumentation
granularity, read directly off #8's own captured span tree, not assumed:**

- #7's own tree (`order-to-cash-nestjs/progress/impl_observability_dashboards.md:83-97`)
  creates spans at exactly two points per hop: the outbox relay's `outbox.publish
  <eventType>` (a manual `SpanKind.PRODUCER` span) and each consumer's `fact.consume
  <eventType>` — explicitly, by its own documentation, **no span for the NATS RPC
  hop itself** ("the RPC hop itself still creates no span, consistent with every
  service's existing convention").
- #8's real, captured tree additionally instruments: (a) the **NATS RPC call**
  itself as its own span (`rpc orders.create`, `rpc fulfillment.stock.check`, `rpc
  fulfillment.stock.reserve`, `rpc billing.credit.hold`, `rpc
  fulfillment.despatch.create`, `rpc billing.invoice.issue` — six of them across
  the trace); (b) the **write-model DB transaction** as its own span
  (`writemodel.transaction`, tagged `mssql`) separate from the `outbox.publish`
  span that follows it; (c) the **saga command dispatch** as its own span
  (`dispatch StockReserve`, `dispatch CreditHold`, `dispatch DespatchCreate`,
  `dispatch InvoiceIssue`), parenting the next service's RPC span. That is three
  additional span layers per hop #7 does not create, applied across roughly the
  same number of hops — which is sufficient on its own to explain both the near
  doubling of span count (22 → 42) and of depth (11 → 26) without any gap in
  propagation. Every one of #8's additional spans is a genuine child of its real
  parent in the SAME trace (confirmed both by Jaeger's own tree rendering and, for
  the outbox-boundary spans, independently by the database `trace_parent` values
  above) — this is #8 being MORE granular, not #8 dropping continuity anywhere #7
  had it.

**Assessment: comparable and explained, not a defect.** Both traces are single,
continuous, genuinely propagated distributed traces across all six services; #8's
is finer-grained by design (more instrumented boundaries per hop), which is a
legitimate implementation choice already reflected in `src/*/Infrastructure/
Observability/` and not something this LIGHT verification pass should alter.

## Findings not fixed (routed, not silently worked around)

**None outstanding.** The one real finding (the metric-name-suffix mismatch) was
small, scoped to `infra/grafana/dashboards/order-to-cash-overview.json` only (never
`src/`), verified fixed against live data before and after, and is disclosed above
with its root cause traced into `src/` (without editing `src/`) per the brief's
"report what you changed and why" rule. The underlying `src/` question — whether
#8's OTel instruments should also declare a `unit:` so a future, unrelated
Prometheus/Grafana consumer matches #7's naming convention byte-for-byte — is a
genuine ported-idiom gap worth a line in a future full-classified pass's ledger, but
is NOT a defect against this feature's own three acceptance bullets, all of which
are met by the dashboard-side fix alone. Not filed as a separate backlog id per this
brief's scope (LIGHT, no separate finding-routing infrastructure invoked); left here
for the leader to decide whether it merits one.

## Teardown

`scripts/dev-stack.sh stop` — all seven of this run's processes (`Orders`,
`Fulfillment`, `Billing`, `Notifications`, `Projector`, `Gateway`, `web`) reported
`[OK] ... stopped`, plus its own "nothing left running, no port this stack bound is
still held" confirmation. `docker compose -f docker-compose.infra.yml down` — all 11
`otcnet-*` containers and the `otcnet-net` network stopped/removed. Confirmed after
both: `docker ps --format '{{.Names}}' | grep otcnet` → no output (exit 1);
`scripts/dev-stack.sh status` → all seven processes `stopped`; `logs/dev-stack/*.pid`
→ no files left.

## Files touched

- `infra/grafana/dashboards/order-to-cash-overview.json` — the metric-name-suffix
  fix described above (observability config, not `src/`).
- `feature_list.json` — id 35's `status` line only, `pending`/`in_progress` →
  `in_review` (confirmed by `git diff --stat`: 1 line changed).
- `progress/evidence/observability_dashboards_grafana.png`,
  `progress/evidence/observability_dashboards_jaeger_trace.png` — the two real,
  live-captured screenshots this report cites.
- This file.

No `src/`, `apps/web/` (source), `specs/shared/`, or `CLAUDE.md` changes.
