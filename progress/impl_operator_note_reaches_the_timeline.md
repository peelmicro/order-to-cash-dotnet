# impl_operator_note_reaches_the_timeline

Feature id 66, phase 13, `sdd: false`. Status set to `in_review` as the final step.

## What was built

The chain feature 41's acceptance bullet 4 left disclosed-but-unsatisfiable in
both assessments — `openapi.yaml` promised the operator's cancellation note
reached the read-model timeline; `asyncapi.yaml`'s `OrderCancelledPayload`
had no field to carry it. `SA-2` (already applied, read-only, untouched by
this feature) adds an **optional** `note` to that schema. This feature wires
the five hops between the RPC request and the Mongo timeline document:

1. **`Order.Cancel`** gained an optional trailing `string? note = null`
   parameter (`src/Orders/Domain/Order.cs`), carried onto the raised
   `OrderCancelled` domain event.
2. **`OrderCancelled`** (`src/Orders/Domain/Events/OrderCancelled.cs`) gained
   an optional trailing `Note` field, defaulting `null` — every fact-driven
   caller (`SagaFactHandler`) leaves it at that default.
3. **`CancelOrderCommandHandler`**'s immediate/`default` branch (the ONE
   branch that calls `Order.Cancel` synchronously and therefore has a note
   to carry) now passes `command.Note` through. Its own `<remarks>`, and
   `CancelOrderCommand`'s, were rewritten to retire the "does not reach the
   timeline" disclosure and state precisely what still doesn't (see "Scope
   boundary" below).
4. **`OrderFactPayloadMapper.ToOrderCancelledPayload`**
   (`src/Orders/Infrastructure/Outbox/OrderFactPayloadMapper.cs`) maps
   `cancelled.Note` onto the Contracts payload's `Note`.
5. **`OrderCancelledPayload`** (`src/Contracts/Facts/Payloads/OrderCancelledPayload.cs`)
   gained `string? Note = null` as its trailing optional member — matching
   the declaration convention every other optional payload field already
   uses (`OrderPlacedPayload.Notes`, `StockRejectedPayload.RetailerCode`,
   etc. — verified by reading all six).
6. **`Summaries.OrderCancelled`** (`src/Projector/Domain/Summaries.cs`) adds
   a `note` key to the timeline entry's `detail` dictionary **only when the
   fact carries one** — never a JSON-`null` placeholder key. This is the
   only Projector change; `FactProjection.ProjectCancellation` and
   `DeltaToPipeline`'s generic `Detail` → BSON conversion needed no changes
   at all, because both already treat `Detail` as an opaque
   `IReadOnlyDictionary<string, object>?`.

## Scope boundary — disclosed, not fixed

The note reaches the timeline only on the **immediate** cancellation branch
(an order in `placed`, or a terminal status `Order.Cancel` itself refuses).
The `stock_reserved` and `credit_approved`/`confirmed` branches enqueue
compensation and let a **later**, fact-driven call to `Order.Cancel`
(`SagaFactHandler`, reacting to `stock.released.v1`/`credit.released.v1` in
an independent transaction) complete the cancellation — that caller has no
access to the original request's note, because neither
`StockReleaseRequestPayload` nor `CreditReleaseRequestPayload` carries one;
SA-2 touched only `OrderCancelledPayload`. A note supplied against one of
those two branches is genuinely not carried to the eventual timeline entry.
This is stated in `CancelOrderCommandHandler`'s own `<remarks>` and is out of
this feature's bounded scope (`src/Contracts/`, `src/Orders/`,
`src/Projector/`; no new wire field on either release request was asked
for). The acceptance bullets do not name these two branches, and bullet 1's
own end-to-end proof exercises the immediate branch — a `placed` order.

## Files touched

**Contracts**
- `src/Contracts/Facts/Payloads/OrderCancelledPayload.cs` — `Note` field.

**Orders**
- `src/Orders/Domain/Order.cs` — `Cancel`'s `note` parameter.
- `src/Orders/Domain/Events/OrderCancelled.cs` — `Note` field.
- `src/Orders/Application/Commands/CancelOrderCommand.cs` — remark rewrite.
- `src/Orders/Application/Commands/CancelOrderCommandHandler.cs` — threads
  `command.Note`; remark rewrite (see "Scope boundary").
- `src/Orders/Infrastructure/Outbox/OrderFactPayloadMapper.cs` — maps `Note`.

**Projector**
- `src/Projector/Domain/Summaries.cs` — `note` detail key, conditional.

**Tests**
- `tests/Orders.UnitTests/OrderCancellationTests.cs` — two new domain tests
  (note absent by default; note carried verbatim, bracketed).
- `tests/Orders.UnitTests/CancelOrderCommandHandlerTests.cs` — one new
  handler test (note threaded onto the raised event).
- `tests/Orders.IntegrationTests/OutboxWireParityTests.cs` — two new tests,
  over the real MS-SQL → outbox → Kafka wire (note present with exact text;
  note key omitted entirely when absent).
- `tests/Contracts.UnitTests/JsonWireOptionsTests.cs` — three new tests
  (omitted-when-null, written-when-present, and an explicit "old envelope
  with no `note` key at all" deserialisation proving bullet 3's Contracts
  half).
- `tests/Projector.UnitTests/SummariesTests.cs` — one new test (exact note
  text in `detail`), plus one added assertion on the existing
  `PR16_OrderCancelled` test (no `note` key when absent).
- `tests/Projector.IntegrationTests/TestSupport/EnvelopeBuilders.cs` — added
  an optional `note` parameter to the `OrderCancelled` builder.
- `tests/Projector.IntegrationTests/TimelineProjectionTests.cs` — three new
  tests against a REAL `mongo:8.3.8` container: note present with exact
  text; note key absent from `detail` when none supplied; a hand-written raw
  JSON envelope genuinely missing the `note` key (not payload-then-stripped)
  still projects (bullet 3, at the Projector level).
- `tests/Gateway.IntegrationTests/OperatorNoteReachesTimelineEndToEndTests.cs`
  (new file) — bullet 1's own named proof: `POST /orders/{id}/cancel`
  through the REAL Gateway (real Kestrel), over a REAL NATS RPC round trip
  to the REAL Orders `orders.cancel` responder (`OrdersHost.CreateBuilder`,
  unmodified, real MS-SQL), producing a REAL `order.cancelled.v1` on Kafka,
  consumed by the REAL, unmodified `ProjectorHost`, landing on a REAL
  MongoDB document — all four services, no stand-in on any hop. The order
  under test is seeded directly through the real `IOrderRepository`/
  `IUnitOfWork` resolved from the real, running `OrdersHost`'s own DI
  container (the same code `PlaceOrderCommandHandler` itself uses once a
  stock check has passed) rather than through `POST /orders`, because
  placing an order for real needs a live `fulfillment.stock.check`
  responder, which is outside this feature's bounded scope to stand up —
  the CANCEL path this feature actually changed runs entirely for real;
  only the precondition (an existing `placed` order) is a shortcut. Stated
  in the file's own header remarks, not only here.
- `tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj` — one new
  `ProjectReference` to `src/Orders/Orders.csproj`, matching the existing,
  reviewed precedent of the two `ProjectReference`s to `src/Fulfillment` and
  `src/Projector` immediately above it (both there for the identical
  reason: booting another service's real host from this project). No change
  to `src/Gateway/` production code — the brief's "do not touch `src/Gateway/`
  unless a test-only change is genuinely required, and say so if it does"
  is why this paragraph exists: a test-only change was required (bullet 1
  names the real Gateway explicitly) and it is exactly this, nothing more.

## R<n> traceability

None. This feature carries no `EARS` requirement id — it closes feature 41's
acceptance bullet 4 under the `SA-2` amendment, and `specs/shared/test-matrix.md`
has no row for it (checked: no `operator_note_reaches_the_timeline`, no
`SA-2`, no "note...timeline" hit in that file). No change made to
`test-matrix.md`.

## Ported-idiom ledger

None. `#7` never built this — its own reviewer disclosed the identical gap
and ruled that "the next feature to touch `OrderCancelledPayload` must close
it," but #7's own `asyncapi.yaml` was never touched again (`CLAUDE.md`'s own
account of this exact history: 39 commits followed, none touching that
file). There is no #7 mechanism to port, so no ledger row is written —
stated explicitly per the brief rather than inventing one.

## Arming — both directions, per acceptance bullet 4

**Deletion** — `src/Orders/Infrastructure/Outbox/OrderFactPayloadMapper.cs`,
removed `Note: cancelled.Note` from `ToOrderCancelledPayload`. Forced
rebuild (`dotnet build --no-incremental` on `Orders.csproj` and
`Orders.IntegrationTests.csproj`). Ran
`OutboxWireParityTests.SA2_PublishedCancelledEnvelope_CarriesTheNoteKeyWithTheExactSuppliedText`
— **FAILED**, verbatim:
```
System.Collections.Generic.KeyNotFoundException : The given key was not present in the dictionary.
   at System.Text.Json.JsonElement.GetProperty(String propertyName)
```
Restored from a `cp` backup (never `git checkout --`), `cmp`-verified byte-
identical (`cmp` exit 0), forced rebuild again, re-ran the same test —
**PASSED**.

**Corruption** — `src/Projector/Domain/Summaries.cs`, changed
`detail["note"] = note;` to `detail["note"] = note + "-CORRUPTED";`. Forced
rebuild on `Projector.csproj` and `Projector.UnitTests.csproj`. Ran
`SummariesTests.SA2_OrderCancelled_WithANote_PopulatesTheNoteDetailKeyWithTheExactText`
— **FAILED**, verbatim:
```
Assert.Equal() Failure: Values differ
Expected: Buyer changed their mind before despatch.
Actual:   Buyer changed their mind before despatch.-CORRUPTED
```
Restored from a `cp` backup, `cmp`-verified byte-identical, forced rebuild,
re-ran — **PASSED**. This is the corruption half the brief calls "the one
that matters" — the note is a value the test itself supplies (bracketed,
not merely asserted non-empty), so a wrong value on the wire is caught, not
only a missing one.

Both probes also propagate to the real end-to-end and real-Mongo tests
listed above (not separately armed there — the two unit/integration-level
guards above are the named, minimal, single-responsibility probes per
CLAUDE.md's arming protocol; the broader tests would fail identically on
either mutation, which is the redundancy the acceptance bullet's "both
directions" language is asking to exist, not asking to be independently
re-armed test-by-test).

## Test run

- `tests/Orders.UnitTests` — 365/365 passed (was 362; +3).
- `tests/Orders.IntegrationTests` — 97/97 passed (was 95; +2).
- `tests/Contracts.UnitTests` — 24/24 passed (was 21; +3).
- `tests/Projector.UnitTests` — 106/106 passed (was 105; +1 test, +1
  assertion on an existing test).
- `tests/Projector.IntegrationTests` — 55/55 passed (was 52; +3).
- `tests/Gateway.IntegrationTests` — 49/49 passed (was 48; +1), including the
  new real four-service end-to-end test (11s standalone,
  `PostOrdersCancelWithANote_ThroughTheRealFourServiceChain_LandsOnTheRealMongoTimelineEntry`).

`./quality.sh`: format check clean, build succeeded, **1623 passed, 0
failed, 0 skipped** across all 18 test projects, coverage collected (exit
0). **Reconciled against 1610**: this feature adds exactly 13 new tests
(3 + 2 + 3 + 1 + 3 + 1, enumerated above), and 1610 + 13 = 1623 — the
figure the run itself produced, not a target it was made to match.

`./init.sh`: exit 0. `SDD coherence`, `backlog tripwire` and `shared spec
byte-identical to #7` all green; the only warnings are the expected
mid-session ones (uncommitted changes, "run quality.sh before closing").

## What could not be done, and why

The two-branch scope limit under "Scope boundary" above — a note supplied
against a `stock_reserved`/`credit_approved`/`confirmed` cancellation is not
carried to the eventual timeline entry, because doing so would require a new
field on `StockReleaseRequestPayload`/`CreditReleaseRequestPayload` (an
`asyncapi.yaml` change beyond `SA-2`, which this feature has no mandate to
make) and a place to persist the note across the async saga boundary. Out of
this feature's bounded scope; disclosed rather than silently narrowed.

## Things that surprised me

- The real four-service end-to-end test needed to seed its precondition
  order by calling the real `IOrderRepository`/`IUnitOfWork` in-process
  (bypassing `POST /orders`) rather than adding a Fulfillment stock-check
  stand-in — the narrower, more honest choice given the feature's bounded
  scope names three services, not four.
- The first version of the two new `OutboxWireParityTests` matched the
  published Kafka message by aggregate id alone; because `Order.Place` then
  `Cancel` on the same aggregate both publish under that same key, the first
  run picked up the `order.placed.v1` envelope instead of
  `order.cancelled.v1` and failed with `KeyNotFoundException` on `note` —
  fixed by also matching `eventType`. Recorded here because it is exactly
  the kind of thing a reviewer would otherwise have to re-derive.
