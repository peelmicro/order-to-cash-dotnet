# Audit — backlog id 52, retroactive ported-idiom ledger for pre-ledger services

`sdd: false`, LIGHT-classified, audit-only (per the invoking brief). No `src/`, `tests/`, `apps/web/`, `specs/shared/`, or `feature_list.json` edits were made. Read-only against `../order-to-cash-nestjs` throughout; no git command that writes either repository's index or working tree was run.

Worked from `feature_list.json`'s id 52 entry (five acceptance bullets + `notes`, read in full first) and the brief at `brief_id52_ledger_audit.md`.

---

## Step 1 — enumeration result

`grep -rl "ported.idiom ledger\|Ported-idiom ledger" specs/*/design.md progress/impl_*.md` returns 40 files. Cross-referencing against which `progress/impl_*.md` or `specs/<feature>/design.md` is the **original port** (not a later bugfix) for each of the six services:

| Service | Original port feature(s) | Phase | Ledger present in the original port doc? |
|---|---|---|---|
| Orders | `orders_aggregate` (13, sdd:true), `orders_acceptance` (15), `outbox_and_idempotency` (14, sdd:true), `order_saga_orchestrator` (16, sdd:true), `orders_saga_terminal_rejection_classification` (42) | 8 | **No** — none of the five phase-8 docs (`specs/orders_aggregate/design.md`, `progress/impl_orders_acceptance.md`, `progress/impl_outbox_and_idempotency.md`, `progress/impl_order_saga_orchestrator.md`, `progress/impl_orders_saga_terminal_rejection_classification.md`) contains the string "ported-idiom ledger" |
| Fulfillment | `fulfillment_stock` (17, sdd:true) | 9 | Yes — `specs/fulfillment_stock/design.md:21,554` states it is "the first to carry one," binding since the Phase 8 gate |
| Billing | `billing_credit` (19, sdd:true) | 10 | Yes — `specs/billing_credit/design.md:23` |
| Notifications | `notifications_service` (23) | 11 | Yes — `progress/impl_notifications_service.md:22` |
| Projector | `projector_read_model` (24, sdd:true) | 12 | Yes — `specs/projector_read_model/design.md:596`, 54 rows |
| Gateway | `gateway_rest_auth` (25) | 13 | Yes — `progress/impl_gateway_rest_auth.md:97` |

**Conclusion, against the entry's own hint:** the entry's `notes` speculate "four services were ported before it existed." That is **not what the evidence shows**. `specs/fulfillment_stock/design.md:21` is explicit that the ledger convention became binding "since the Phase 8 gate" and that `fulfillment_stock` (phase 9) — the very next feature after Orders' own phase 8 — "is the first to carry one." Every original service port from phase 9 onward (Fulfillment, Billing, Notifications, Projector, Gateway) already carries a substantive, boundary-enumerated ledger. **Only Orders' original port (all five phase-8 features) has zero ledger rows.** One service, not four. This is stated as a finding, not filed as a correction to `feature_list.json` (single-writer, leader's call).

Boundary applicability for Orders (the five boundary types the entry names): Orders has no MongoDB dependency, so "the Mongo projector's read path" does not apply to it — 4 boundary types apply, not 5. Walking the four surfaced a fifth site within one of the four types (a second NATS RPC client, distinct from the first) — see Step 2.

---

## Step 2 — per-boundary walk (ledger rows written to the owning feature's own progress report, matching how Fulfillment/Billing/Notifications/Gateway already keep theirs)

Five boundaries walked, four in ≤10 minutes, one (RL4) over budget and recorded as a finding per the entry's own acceptance bullet 5.

| Row | Boundary | File | Outcome | Ledger row written to |
|---|---|---|---|---|
| RL1 | NATS RPC client reply decode — `NatsStockAvailabilityChecker.CheckAsync` | `src/Orders/Infrastructure/Messaging/NatsStockAvailabilityChecker.cs` | No gap. Already carries the `JsonException` catch (Advisory A1, closed by feature 46's fix round) | `progress/impl_orders_acceptance.md` |
| RL2 | RPC responder request decode — `OrdersCreateResponder.HandleOrdersCreateAsync` | `src/Orders/Presentation/OrdersCreateResponder.cs` | No gap. #8 hand-builds the catch #7's Nest transport supplied for free (consistent with `fulfillment_stock` ledger row L9's bare-JSON-wire tradeoff) | `progress/impl_orders_acceptance.md` |
| RL3 | Kafka consumer envelope decode — `SagaFactsConsumer.HandleMessageAsync` | `src/Orders/Presentation/SagaFactsConsumer.cs` | No gap. `ValidateEnvelope`'s required-field list matches #7's `parseFactEnvelope` field-for-field; same log-and-acknowledge (never redeliver) policy | `progress/impl_order_saga_orchestrator.md` |
| RL4 | Outbox relay payload read-and-republish — `OutboxRelay.BuildPublishableFact` / `OutboxEnvelopeMapper.ToWireBytes` | `src/Orders/Infrastructure/Outbox/{OutboxRelay,OutboxEnvelopeMapper}.cs` | **Exceeded the 10-minute budget** — recorded as a finding per the entry's acceptance bullet 5. No #7-vs-#8 asymmetry was confirmed (both let a payload-decode failure escape the graceful publish-rollback path uncaught, and both are one layer up caught by a poll-loop-level catch-and-retry), but confirming whether that produces an *unbounded* poison-row retry loop on #7's side required reading `outbox-relay.service.ts` (the caller of `runOnce()`), which was not reached in time | `progress/impl_outbox_and_idempotency.md` |
| RL5 | NATS RPC client reply decode — `NatsSagaCommandsAdapter.SendAsync` (a **second**, sibling NATS RPC client site not surfaced by Step 1's file-level scan — found by reading the code for RL1's neighbour) | `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs:131-143` | **Confirmed gap.** No `try`/`catch (JsonException)` around `RpcJson.IsErrorBody`/`RpcJson.Deserialize<TReply>` — a malformed reply body throws a bare, uncaught `JsonException` instead of the classified `SagaCommandTransportError` #7's `nats-saga-commands.adapter.ts:170-178` guarantees. This is the exact shape feature 46 already fixed in the sibling `NatsStockAvailabilityChecker` (RL1) but explicitly left unfixed here, naming backlog id 52 as the place it would be picked up (`progress/impl_orders_stock_check_rpc_error_discriminator.md:102`) | `progress/impl_order_saga_orchestrator.md` |

Full one-line-per-row detail, with file:line citations into both checkouts, is in the three progress files listed above (search each for "Retroactive ported-idiom ledger (backlog id 52").

**Nothing was fixed in place.** RL5's gap is real and reachable (any Fulfillment/Billing responder returning a malformed reply body — a corrupted process, a bug, or in a future service using a different serializer — would crash `SendAsync` with a bare `JsonException` instead of the classified transport error the saga's retry/terminal-classification logic (`IsTerminalRpcErrorCode`, feature 42) expects to see), but per the brief and the entry's own acceptance bullet 4, closing it is out of scope for this audit.

---

## Step 3 — backlog-entry candidates for the leader to file

**Candidate A — fix the confirmed gap (RL5).**
- Name: `orders_saga_commands_adapter_reply_decode_guard` (phase-agnostic, retroactive)
- Title: Port the malformed-JSON reply-decode guard from `NatsStockAvailabilityChecker` (feature 46, Advisory A1) to its sibling `NatsSagaCommandsAdapter.SendAsync`
- One-line acceptance: a malformed (non-JSON) reply body on any saga-command subject (`stock.reserve`, `credit.hold`, `despatch.create`, `invoice.issue`, `stock.release`, `credit.release`) raises `SagaCommandTransportError`, never a bare `JsonException`, proved by an integration test over a real broker with a stand-in responder answering non-JSON bytes (mirroring `NatsStockAvailabilityCheckerTests.AMalformedNonJsonReply_...`).

**Candidate B — verify or guard the outbox relay's poison-row exposure (RL4).**
- Name: `outbox_relay_poison_payload_retry_loop_unverified` (phase-agnostic, retroactive)
- Title: Confirm whether a corrupted `outbox.payload` row (an application bug, not DB corruption — the column is raw bytes, not a native JSON type in MS-SQL) retries unboundedly through `OutboxRelay.RunOnceAsync`'s `READPAST` claim on every poll, with no poison-row circuit-breaker, and whether #7 has the identical exposure (`outbox-relay.service.ts`'s caller was not read within this audit's time-box)
- One-line acceptance: either a test demonstrating the row is skipped/parked after N failed publish cycles (a fix), or a recorded, cited comparison against `outbox-relay.service.ts` establishing genuine #7 parity (an "ACCEPTED, NOT FIXED" disposition, since the ledger's "None owed" rule applies only when the sibling half is actually checked, not assumed).

Both are new, phase-agnostic entries; neither was written into `feature_list.json` (single-writer, left to the leader this session).

---

## Ledger locations touched (append-only; no other content in these files was changed)

- `progress/impl_orders_acceptance.md` — new `## Retroactive ported-idiom ledger (backlog id 52, added 2026-09-18, audit-only)` section, rows RL1–RL2.
- `progress/impl_order_saga_orchestrator.md` — new matching section, rows RL3 and RL5.
- `progress/impl_outbox_and_idempotency.md` — new matching section, row RL4.

## What was NOT done, and why

- `specs/orders_aggregate/design.md` itself was not given a ledger section — the actual boundary-owning code lives in the three sibling feature reports above, and "match whichever the service's OTHER ledger rows already live in" (per the brief) means matching Fulfillment's pattern of one ledger per feature-that-built-the-mechanism, not one ledger per service hub. `orders_saga_terminal_rejection_classification` (42) was read (it only classifies already-decoded `RpcError.Code` values downstream of RL5 — no decode boundary of its own) and gets no ledger row.
- No fix was made for RL5 or RL4 — both are audit findings with proposed backlog entries, per the entry's own acceptance bullet 4 ("NOTHING is fixed in place").
- `outbox-relay.service.ts` (RL4's missing half) was deliberately not read once the 10-minute mark passed, per the entry's own stopping rule (acceptance bullet 5) — pushing through was the failure mode the rule exists to prevent.

## Surprises

- The entry's own speculative "four services" figure was wrong by a factor of four; the actual answer (one service, Orders) was cheap to establish directly from the file-level grep plus one `design.md` line ("this feature is the first to carry one"), which is exactly the kind of question `CLAUDE.md` says to close with evidence rather than carry to a gate as a given.
- Reading RL1's neighbour for context (rather than only the boundary Step 1's grep had already flagged) surfaced RL5, a **second** NATS-RPC-client site with the identical gap shape feature 46 fixed next to it — and feature 46's own report had already named backlog id 52 as the place this would be picked up. This matches the entry's `notes` precisely: "reading a file for one gap does not surface the next," and here the *next* file over had the same gap a phase later.

---

**PASS.** Boundaries walked: **5** (RL1–RL5). Gaps found: **1 confirmed** (RL5) **+ 1 unresolved/time-boxed finding** (RL4, itself the entry's acceptance-bullet-5 case) — two backlog-entry candidates filed above for the leader.
