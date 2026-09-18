# Id 109 `otel_instruments_never_declare_a_unit` — disposition record (phase 25, LIGHT, leader-direct)

## Decision

**The dashboard's current suffix-less metric names (`otc_saga_completion_ms`, `otc_outbox_lag_ms`, `otc_request_latency_ms`, `otc_fact_processing_latency_ms`, `otc_dlq_depth`) are declared the correct, permanent shape.** `src/`'s `Meter.CreateHistogram`/`CreateGauge` calls are **not** changed to add a `unit:` parameter, and #7's `_milliseconds`/`_ratio`-suffixed naming convention is **not** followed here.

## Why

- The dashboard already queries the real, currently-emitted metric names and all five panels are confirmed non-empty against live data (id 35, phase 22, `progress/history.md` "Id 35" entry and `progress/evidence/observability_dashboards_grafana.png`). Nothing is broken today.
- Adding `unit:` to match #7 would require two coordinated changes — every affected `Meter.Create*` call across however many of the six services declare these instruments, **and** reverting the dashboard's queries back to the suffixed form id 35 deliberately moved away from — to arrive at a naming convention with no functional benefit over the one already working. That is churn for parity's own sake, not a defect being fixed.
- This is a naming-convention difference between the trilogy's two stacks, not a correctness gap: both #7 and #8 export a millisecond histogram / ratio gauge with the right semantics under their own SDK's naming convention (JS OTel auto-appends a unit suffix when `unit` is set; #8 never set it). The benchmark this repository is building (`progress/history.md`, README's Benchmark section) measures effort and defects, not metric-name spelling, so this difference carries no benchmark weight either way.
- Re-verifying five dashboard panels a second time (as the "if `unit:` is added" branch of this entry's own acceptance criteria would require) buys nothing when nothing about their query or their data changes.

## The ported-idiom ledger line this entry's acceptance criteria asks for

**#7 relied on:** the JS OTel SDK auto-appending a unit suffix (`_milliseconds`, `_ratio`) to every exported Prometheus metric name whenever an instrument's creation call passes a `unit` option — a property of the SDK, not something #7's own code asserts or tests.

**In #8 that property is supplied by:** nothing. None of #8's `Meter.CreateHistogram`/`CreateGauge` calls pass a `unit:` parameter, so .NET's OTel SDK never appends a suffix — confirmed by `grep` across every `otc_*` instrument declaration in `src/` (id 35's implementer, re-confirmed independently by the leader at the time, per `progress/history.md`'s "Id 35" entry). This was surfaced as a real defect once (id 35: the dashboard, copied byte-for-byte from #7, queried the suffixed names that #8 never produces, and 4 of 5 panels were empty until fixed). **The guard against regression is the dashboard's own queries plus the five-panel non-empty check id 35 already performed** — there is no compile-time or test-time guard tying an instrument's name to a dashboard query in either repository, and none is being added here: a mismatch here fails open (an empty panel), not silently wrong data, and is caught by looking at the dashboard, the same way id 35 found it.

## Disposition

`done`, `"ACCEPTED, NOT FIXED"` is not the right note here — this is not a residual defect being deferred, it is a considered decision that the current, already-working state is correct and no code change is warranted. Re-open trigger: if `src/`'s `Meter.Create*` calls are ever changed to pass `unit:` for an unrelated reason (e.g. an OTel SDK upgrade that makes it the default), the dashboard's queries must be re-verified against the new metric names at that time.
