# review_operator_note_reaches_the_timeline

**Feature id 66, phase 13, `sdd: false`. Reviewed 2026-09-09. Every figure below is from my own run.**

## Verdict: **APPROVED** — with one mandatory routing obligation the leader must file

The code is correct, the chain is real on all four services, and the guards have teeth in all three mutation families. Nine independent mutations, every one killed by a named test.

**But bullet 1 is closed only for the immediate cancellation branch, and the residual must not leave this review as a sentence.** The leader is to file **backlog entry `operator_note_survives_the_compensation_branches`** (next free id **71**) — see F1, which also carries the finding that the residual is **cheaper and less spec-shaped than either the implementer or the dispatching brief believed**: #7 already solved it inside Orders, with no wire change and no `SA-3`.

**Recommendation to the human:** approve the close, file entry 71, and read F1 and F3 together — F3 is a second, larger defect that entry 71's fix would close as a by-product.

---

## The central question the brief asked me to rule on

> Is the delivered scope a **correct reading** of bullet 1, or a **partial closure** of it?

**It is a partial closure — and the correct one to have delivered.** Both halves of that matter.

**Partial.** Bullet 1 is unqualified. On an order in `stock_reserved`, `credit_approved` or `confirmed`, `POST /orders/{id}/cancel` with a note returns 202, compensation runs, the order reaches `cancelled`, a timeline entry is written, and **the note is not on it**. `specs/shared/openapi.yaml:1345-1347` types `CancelOrderRequest.note` as *"Free-text operator note recorded on the timeline entry"* — a promise about where the value ends up, made for every cancellation request, not only the immediate one. So the criterion is unmet for two of the three starting states.

**Correct.** The feature's own title is *"the acceptance bullet **SA-2** makes satisfiable"*, and that is the exact boundary delivered. SA-2 gave the **fact** a field to carry a note (`asyncapi.yaml:2620-2622`). It did not give the **saga** anywhere to remember one. On the two compensation branches the `OrderCancelled` fact is raised by a **different transaction** — `SagaFactHandler.cs:167`, reacting to `stock.released.v1`/`credit.released.v1` — which never sees the RPC request. Nothing in SA-2's scope, and nothing in `src/Contracts/`+`src/Orders/`+`src/Projector/` as the brief bounded them, closes that. I verified the disclosure is factually exact rather than convenient:

```
$ grep -n -A12 "^    StockReleaseRequestPayload:"  specs/shared/asyncapi.yaml   # :3328-3340 — orderReference, reason. No note.
$ grep -n -A12 "^    CreditReleaseRequestPayload:" specs/shared/asyncapi.yaml   # :3503-3515 — orderReference, retailerCode, companyCode. No note.
$ sed -n 129,146p specs/shared/domain-model.md                                   # Order aggregate: no field able to hold a pending note.
```

The disclosure is in the source itself (`CancelOrderCommandHandler.cs:64-85`), not only in the report, and it names the two payloads and the reason. That is the standard this repository asks for.

**Where I correct the implementer's framing, and it changes the routing.** The report calls the residual out-of-scope because closing it "would require a new field on `StockReleaseRequestPayload`/`CreditReleaseRequestPayload` (an `asyncapi.yaml` change beyond SA-2)". **That is one route, and it is the expensive one. It is not the only one, and #7 took the other.** See F1.

---

## F1 — ROUTING (mandatory). Backlog entry `operator_note_survives_the_compensation_branches`, id 71

`CLAUDE.md`: *a disclosure whose root cause is `specs/shared/` becomes a numbered backlog entry or an `SA-n` proposal, always — never a sentence in a review.* This is the third time this exact acceptance criterion has reached a reviewer; it will not be the third deferral to nobody.

**Proposed entry (for the leader to file — I have not written `feature_list.json` beyond id 66's own status):**

- **id:** 71
- **name:** `operator_note_survives_the_compensation_branches`
- **phase:** 14
- **sdd:** false
- **title:** Carry the operator's cancellation note across the async saga boundary so `stock_reserved`/`credit_approved`/`confirmed` cancellations reach the timeline with it, closing the unqualified half of id 66's bullet 1
- **acceptance:**
  1. `POST /orders/{id}/cancel` with a note against an order in `stock_reserved` lands that note on the read-model timeline entry after the real compensation chain completes, proved end to end
  2. the same for `credit_approved` and `confirmed`
  3. a saga-decided cancellation (`stock_rejected`, `credit_rejected`) still carries no note and no `note` key
  4. armed in all three families: deleting the retention, corrupting the retained text, and substituting the persisted column/field for a sibling

**And the entry must record this, because it is the part that was got wrong twice:** *no `SA-3` is required.*

- `specs/shared/` **does not prescribe the `saga_commands` table**. Enumerated:
  ```
  $ grep -rn "saga_commands\|saga command" specs/shared/*.md
  saga.md:31, requirements.md:564, domain-model.md:275, domain-model.md:475, test-matrix.md:127
  ```
  Five hits, all prose about saga progression, DLQ policy and the `order.saga_failed.v1` fact. **None describes a column.** That table is implementation-owned, per-assessment.
- **#7 carries the note there already**, and it is a working precedent in a checkout on this machine: `apps/orders/src/application/cancel-order.handler.ts:272-283` — `buildTriggeringEnvelope(orderId, requestId, note)` puts `...(note !== undefined ? { note } : {})` into the saga command's `triggeringEventEnvelope`, on **both** compensation branches (`:175` and `:234`), persisted to `triggering_event_envelope` (`saga-commands.schema.ts:61`). No wire field, no shared-spec change.
- So the #8 shape is: an Orders-local column on `saga_commands`, plus `SagaFactHandler.cs:167` passing it into the `note:` parameter **this feature already added** to `Order.Cancel`. Wholly inside `src/Orders/`.

This is the *"never hand the human an open question you could have closed"* rule applied: #7 faced this and answered it, so it is not a gate decision.

## F2 — the ledger claim is right in its conclusion and overstated in its evidence (non-blocking; correction belongs in entry 71)

The report says: *"None. #7 never built this. There is no #7 mechanism to port."*

**The conclusion — no ledger row — is correct, and I verified it rather than accepting it.** The three hops this feature actually adds have no #7 counterpart:

```
$ grep -n -A8 "OrderCancelledPayload" packages/contracts/src/generated/asyncapi.types.ts   # :780-786 — no note
$ sed -n 93,94p apps/projector/src/domain/summaries.ts   # detail: { cancellationReason, compensationSteps } — no note
```

**The sentence is wrong, and it is the half that cannot fail and that #9 inherits.** #7 *did* build note plumbing, past the point where #8 drops it — the `buildTriggeringEnvelope` mechanism above. The claim was written from `CLAUDE.md`'s own prose about #7's 39 commits, not from #7's source, which is precisely the failure `CLAUDE.md` names: *a row's history half is a claim about #7's source, read out of #7's checkout with a file and line.* It applies to a claim that **no** row is owed just as much as to a row.

**The required enumeration of #7's guards for this mechanism** (`CLAUDE.md`: *port its guards — enumerate #7's tests, classify each assertion*), which the report did not run:

```
$ find . -type f -name '*.spec.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' -print0 \
    | xargs -0 grep -n "note" | grep -vi "notes\|// note\|/\* note"
```
Ten hits; six are prose comments containing the word "note". The four that supply a cancellation note, classified one line each:

| # | Hit | Classification |
|---|---|---|
| 1 | `orders-cancel.integration.spec.ts:308` — `requestCancel({… note: 'wire test' })` | Supplies a note, **asserts nothing about it** (status/reason/compensationPlanned only). Nothing to port. |
| 2 | `gateway/orders.integration.spec.ts:234` — `.send({ note: 'demo cancel' })` | Supplies a note, **asserts nothing about it** (202 + `compensationPlanned`). Nothing to port. |
| 3 | `cancel-order.handler.spec.ts:135` — immediate branch | Supplies a note, **asserts nothing about it**. Nothing to port. |
| 4 | `cancel-order.handler.spec.ts:195` — credit branch | Supplies a note; asserts `input.triggeringEventEnvelope.eventType` (`:220`) and **never the payload's note**. Nothing to port. |

```
$ find . -name '*.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' -print0 | xargs -0 grep -n "triggeringEventEnvelope"
```
17 hits; the only two assertions (`cancel-order.handler.spec.ts:178,220`) check `eventType` alone.

**So #7's own note retention is unguarded in #7.** That is worth carrying into entry 71: #8 must not port the mechanism without the guard #7 never wrote. **No guard was dropped in translation here** — which breaks phase 13's run of three consecutive rejections for exactly that.

## F3 — adjacent and larger: #8's `saga_commands` cannot satisfy R29's outstanding dead-letter clause (non-blocking; recommend folding into entry 71 or feature 27's brief)

Found while checking F1's premise, and it is not this feature's defect.

```
$ grep -rn "TriggeringEvent" src/Orders --include='*.cs' | grep -v '/bin/\|/obj/'
```
Seven hits, all `TriggeringEventId` / `triggering_event_id`. `ISagaCommandStore.EnqueueAsync` takes an **id**; `SagaCommand.cs` has `Payload` (the *outgoing* RPC request) and no envelope. `ProcessedEvent.cs` is id-only. **Orders stores the triggering fact's bytes nowhere.**

`specs/shared/test-matrix.md:127` records R29's dead-letter row as outstanding and owned by feature 27 `observability_reliability`, whose text is *"publishes the triggering fact to the source topic's `.dlq` **exactly once**"* — verbatim republication, which needs the bytes. #7 stores them (`saga-commands.schema.ts:61`) and republishes from them (`saga-first-park-dead-letter-handler.ts:44`). Feature 27 will discover this as a schema change; better it is known now, and one column serves both it and entry 71.

---

## `CHECKPOINTS.md` walk

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (6 definitions)
- [x] every agent definition declares its model — `init.sh` §2 green for all six
- [x] `./init.sh` exits 0 — my own run; §3, §4, §5, §5b, §5c, §5d all `[OK]`; the only warnings are the expected mid-session pair (71 uncommitted changes; "run quality.sh before closing")

### C2 — state is coherent
- [x] at most one feature `in_progress` — `init.sh`: *no feature in_progress* (66 was `in_review`)
- [x] every status in `rules.valid_status`
- [x] every `done` feature has passing tests — full suite green, below
- [x] `progress/current.md` describes the active session (names id 66, phase 13)
- [x] no `blocked` feature

### C3 — architecture is respected
- [x] no framework reference inside any `Domain/` folder — **run, not eyeballed**: `Architecture.Tests` **16/16 passed**. The feature adds domain code in two `Domain/` folders (`Order.cs`, `OrderCancelled.cs`, `Summaries.cs`); `string?` is BCL
- [x] no cross-service DB access — the new e2e test boots two hosts in one process, each with its **own** connection string and its own Mongo database; neither reads the other's store. The note crosses the boundary as a **Kafka fact**, which is the only crossing this feature adds
- [x] no shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs` — the one new `ProjectReference` is **test-only** and is judged below
- [x] no `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `src/SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic — this feature adds no money path
- [x] every interaction classifiable — Gateway→Orders is **NATS RPC** (`orders.cancel`, a request expecting a reply); Orders→Projector is a **Kafka fact** (`order.cancelled.v1`). Correct on both counts; no Kafka-as-request-bus, no RPC-for-facts
- [x] no stray debug logging, no context-free TODOs — swept the eight changed source files for `TODO|FIXME|HACK|Console.WriteLine|Debug.WriteLine`: **zero hits**

### C4 — verification is real
- [x] `./quality.sh` passes — **my own full run, exit 0: 1623 passed, 0 failed, 0 skipped across 18 test projects.** Summed off the 18 `Passed!` lines myself (`… | grep -oE "Passed: *[0-9]+" | … | paste -sd+ | bc` → `1623`). Reconciles to the 1610 baseline + 13
- [x] domain tests are pure — the new domain tests construct an aggregate and read `DomainEvents`; no framework, no DB, no mock
- [x] integration tests use Testcontainers against real infrastructure — verified by running them: the Orders outbox tests drive real MS-SQL + real Kafka; the timeline tests a real `mongo:8.3.8`; the e2e test real MS-SQL + NATS + Kafka + Mongo with two **real, unmodified** hosts. **No mocked broker on any hop**
- [~] coverage thresholds — **not applicable yet, by a named deferral, not silently.** `quality.sh:80-83` carries `TODO(feature 34 — sonarqube_quality_gates, phase 21)` with `CLAUDE.md`'s own *"verified to fail when breached"* cited as the reason not to add an unproven gate. Per-assembly line coverage is reported as INFO (domain-heavy assemblies 82.6 / 88.7 / 90.2 / 95.8 / 97.2%). Recorded, not waived
- [x] **no Jest anywhere** — xUnit throughout; this feature adds no web code

### C5 — the session closed cleanly
- [x] no suspicious untracked files — the untracked set is exactly this phase's `progress/*.md`, the six `*ProgramConfiguration.cs` from id 56 and their tests
- [x] `progress/history.md` has the entry **including the effort record** — appended by me at close, with the phase-13 closing assessment
- [x] `feature_list.json` reflects true state — id 66 set `done` by me, single-line edit, diff read back
- [x] the human is told what was done and how to test it manually — in my return message
- [x] **Claude did not commit** — I ran no `git commit`, no `git push`, and no `git checkout --` on any path

### C6 — SDD
Not applicable: id 66 is `sdd: false`. The specification of record is `feature_list.json` id 66's four acceptance bullets plus `asyncapi.yaml`'s `OrderCancelledPayload` as amended by SA-2. There is no `specs/operator_note_reaches_the_timeline/` and none is owed.
- [x] the SDD invariant still holds repo-wide — `init.sh`: *SDD coherence: 7 sdd feature(s) past pending have their triple-doc*

### C7 — spec-reuse fidelity and benchmark honesty
- [x] **`specs/shared/` byte-identical to #7's, except `test-matrix.md`** — my own `cmp`, file by file, not `init.sh`'s word for it:
  ```
  IDENTICAL asyncapi.yaml   IDENTICAL openapi.yaml   IDENTICAL requirements.md
  IDENTICAL domain-model.md IDENTICAL saga.md        DIFFERS   test-matrix.md (exempt by design, init.sh:284-296)
  ```
- [x] **every deviation is a recorded amendment in both repositories** — SA-2's three lines are present at `asyncapi.yaml:2620-2622` here and at `:2620-2622` in `../order-to-cash-nestjs`, and the two files are `cmp`-identical. SA-2 is recorded in `progress/history.md`. It is still uncommitted in both repositories and owes its own commit
- [x] **the `R<n>` ids are #7's** — this feature claims **none**, and that is verified rather than assumed: `grep -n -i "note" specs/shared/requirements.md` returns 2 hits, both the English word "note" in prose (`:538` a filing note, `:548` a section heading). No EARS requirement covers the operator note; `test-matrix.md` has no row for it. No id was reused, and none was invented
- [ ] n8n workflows unchanged and green — not exercised by this feature; unchanged since id 25 closed
- [ ] the black-box API script proves the same saga steps — not exercised by this feature
- [x] `progress/history.md` effort records complete and honest, **including what was not faster** — this feature has no #7 counterpart at all, which is itself the measurement; recorded that way
- [x] the README benchmark section — the leader's close, unchanged by this review

---

## Acceptance-bullet → named-test mapping (verified, not accepted)

There are no `R<n>` to trace. The four acceptance bullets are the specification of record, and each maps to concrete named tests I ran and mutated.

| Bullet | Named test(s) | Verified |
|---|---|---|
| **1** note reaches the timeline, end to end through real Gateway, Orders, Kafka, Projector | `OperatorNoteReachesTimelineEndToEndTests.PostOrdersCancelWithANote_ThroughTheRealFourServiceChain_LandsOnTheRealMongoTimelineEntry` | Ran green (9 s); killed by probes G and H. Four real services, no stand-in. **Partially closed — see F1** |
| **1** (per-hop) | `OrderCancellationTests.SA2_Cancel_WithANoteSupplied_RaisesOrderCancelledCarryingTheExactNoteText`; `CancelOrderCommandHandlerTests.Placed_CancelsImmediately_ThreadsTheSuppliedNoteOntoOrderCancelled`; `OutboxWireParityTests.SA2_PublishedCancelledEnvelope_CarriesTheNoteKeyWithTheExactSuppliedText`; `SummariesTests.SA2_OrderCancelled_WithANote_PopulatesTheNoteDetailKeyWithTheExactText`; `TimelineProjectionTests.SA2_ACancellationCarryingANote_ProducesATimelineEntryWhoseDetailNoteIsTheExactText` | All ran; each killed by at least one probe. Every one **brackets the exact string the test itself supplies** — no `NotNull`, no non-empty |
| **2** optional everywhere; saga-decided cancellation carries none and the entry is still correct | `OrderCancellationTests.SA2_Cancel_WithNoNoteSupplied_RaisesOrderCancelledWithNoteAbsent` (uses `StockRejected`); `JsonWireOptionsTests.SA2_OrderCancelledPayload_NoteOmittedWhenNull`; `OutboxWireParityTests.SA2_PublishedCancelledEnvelope_OmitsTheNoteKeyWhenNoneWasSupplied` (real Kafka, `stock_rejected`); `SummariesTests.PR16_OrderCancelled` (new `ContainsKey` assertion); `TimelineProjectionTests.SA2_ACancellationCarryingNoNote_ProducesATimelineEntryWithNoNoteKeyInDetail` (real Mongo) | **This was the brief's risk #1 and it survives it — see probe I.** The optionality claim is *not* carried by key-absence assertions alone; forcing the key to be written kills two independent guards including the golden-envelope oracle |
| **3** Contracts, Orders emission and Projector projection move together; replay of an OLD envelope without the field still projects | `JsonWireOptionsTests.SA2_OrderCancelledPayload_DeserialisesAnOldEnvelopeWithNoNoteKey_LeavingNoteNull`; `TimelineProjectionTests.SA2_ReplayingAnOldEnvelopeWithNoNoteKeyAtAll_StillProjects` | **The brief asked which was used, and it is the right one.** Both use a **hand-written raw JSON string** whose `payload` object ends at `"compensationSteps":[]` — the `note` key is genuinely absent, not `note: null`. The Mongo test additionally asserts `Assert.Null(envelope.Payload.Note)` *before* projecting, so a fixture that regressed to present-and-null would fail rather than pass silently |
| **4** armed in BOTH directions | deletion → `OutboxWireParityTests.SA2_…CarriesTheNoteKey…`; corruption → `SummariesTests.SA2_OrderCancelled_WithANote_…` | Both re-run by me, verbatim messages reproduced, **plus seven more of my own** |

---

## My own mutation probes — nine, all three families, all killed

Every probe: mutate → **forced rebuild** (`dotnet build … --no-incremental`, plus `touch` on the source) → run the named test → restore from a `cp` backup (never `git checkout --`) → `cmp` against the backup → re-read the changed line → forced rebuild → confirming green.

| # | Family | Mutation | Named test that died |
|---|---|---|---|
| A | corruption | `Summaries.cs:99` `detail["note"] = note + "-CORRUPTED"` | `SummariesTests.SA2_OrderCancelled_WithANote_…` — *Expected: Buyer changed their mind before despatch. / Actual: …-CORRUPTED* (reproduces the implementer's record verbatim) |
| B | **substitution** | `detail["note"]` → `detail["notes"]` (a real sibling key — `OrderPlacedPayload.Notes` renders as `notes`) | same test, `KeyNotFoundException` shape |
| C | deletion | `Order.cs:259` `Note: note` → `Note: null` | **two** tests: `OrderCancellationTests.SA2_Cancel_WithANoteSupplied_…` **and** `CancelOrderCommandHandlerTests.Placed_CancelsImmediately_ThreadsTheSuppliedNoteOntoOrderCancelled` |
| D | optionality | `Summaries.cs` writes `detail["note"]` **unconditionally** (present-and-null instead of absent) | `SummariesTests.PR16_OrderCancelled` — `Assert.False() Failure` |
| E | deletion | `OrderFactPayloadMapper.cs:69` — drop `Note: cancelled.Note` | `OutboxWireParityTests.SA2_…CarriesTheNoteKey…` — `KeyNotFoundException` at `JsonElement.GetProperty`, **over real MS-SQL and real Kafka** (reproduces the implementer's record verbatim) |
| F | corruption | same site, `Note: … + "-WIRE-CORRUPTED"` | same test — and the sibling omission test correctly stayed green, because `null` was preserved |
| G | corruption | same site, `+ "-E2E-CORRUPTED"` | **`OperatorNoteReachesTimelineEndToEndTests.PostOrdersCancelWithANote_…`** — proves bullet 1's own four-service proof reads the payload rather than counting a document |
| H | **substitution** | `OrdersCreateResponder.cs:167` — `new CancelOrderCommand(request.OrderId!.Value, request.OrderReference)`: a **real sibling field of the same nullable type on the same payload**, correct mechanism aimed at the wrong source | the same e2e test. This is the family `CLAUDE.md` says hides *correct behaviour aimed at the wrong target*, and the call site is guarded |
| I | optionality **at the wire** | `[property: JsonIgnore(Condition = Never)]` on `OrderCancelledPayload.Note` — the key becomes mandatory | **three** guards, in two projects: `JsonWireOptionsTests.SA2_…NoteOmittedWhenNull`, **`GoldenEnvelopeParityTests.OrderCancelledV1_IsByteExactSemanticallyEqualAndRoundTrips`** (the oracle built from #7's twelve captured envelopes), and `OutboxWireParityTests.SA2_…OmitsTheNoteKeyWhenNoneWasSupplied` over real Kafka |

**Probe I is the answer to the brief's sharpest concern.** The worry was that *"nulls are omitted on the wire, so no serialised-key assertion can see a missing optional field"* — a guard that cannot fail. It can fail, in the direction that matters: make the field non-optional and #7's own golden bytes reject it. The parity oracle is doing real work on this feature.

**Restore evidence** (all five mutated files are tracked, so `git diff` is meaningful here — I did **not** rely on it where it could not fail):
```
$ cmp <backup> <file>   → exit 0 for OrderCancelledPayload.cs, Summaries.cs, Order.cs, OrderFactPayloadMapper.cs, OrdersCreateResponder.cs
$ git diff --stat  (the five files)  → 4 files, 40 insertions(+), 12 deletions(-) — OrdersCreateResponder.cs absent, i.e. back to HEAD exactly
```
followed by a forced solution-wide rebuild and: `Contracts.UnitTests 24/24`, `Orders.UnitTests 365/365`, `Projector.UnitTests 106/106`, `Architecture.Tests 16/16`, the two Orders SA-2 outbox tests **2/2**, the e2e test **1/1** — all 0 failed, 0 skipped.

---

## The test-only `ProjectReference` to `src/Orders/` — sound

`tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj:65-73`. **Judged sound, and it matches the precedent rather than merely claiming to.**

- The two references immediately above it exist for the identical reason and were reviewed and approved: `src/Fulfillment` (id 25, booting `FulfillmentHost` for `FulfillmentStockEndToEndTests`) and `src/Projector` (id 26, booting `ProjectorHost` for `StreamProjectorEndToEndTests`). Adding `src/Orders` completes the set rather than widening the pattern.
- **The direction that would matter is the other one, and it is clean.** `src/Gateway` references no service project: the shared-code rule constrains **runtime** code, and this edge exists only in a test assembly. Verified by reading `src/Gateway/Gateway.csproj`'s references and by `Architecture.Tests` staying green.
- The alternative — a stand-in `orders.cancel` responder — is exactly what bullet 1 forbids, and is the shape #7's own `gateway_rest_auth` was rejected for twice: *both sides of the seam tested only against the wire each preferred*. A real responder is the point.
- **Will it bite?** The realistic cost is build coupling and container time, both already paid twice. I checked the one thing that would be a genuine smell — a test project quietly reaching into another service's **database** — and it does not: the e2e test creates its own database per run and drives Orders only through `OrdersHost`'s own DI container and its public HTTP/NATS surfaces.

**One honest caveat, already disclosed in the test's own header (`:48-59`) and not only in the report:** the precondition order is seeded through the real `IOrderRepository`/`IUnitOfWork` resolved from the running `OrdersHost` rather than through `POST /orders`, because a real placement needs a live `fulfillment.stock.check` responder. **The cancel path this feature changed runs entirely for real**; only the precondition is a shortcut, and probes G and H confirm the whole chain is load-bearing. Accepted.

---

## What must change before the next feature (nothing blocks this one)

1. **The leader files backlog entry 71** as specified in F1, carrying F2's #7 citation and F3's finding. This is the deliverable that makes the third disclosure different from the first two.
2. **F2's one-sentence correction** to `progress/impl_operator_note_reaches_the_timeline.md`'s ledger section: #7 built note plumbing to `cancel-order.handler.ts:272-283` and no further, its retention is unguarded there, and the conclusion (no row owed for this feature's three new hops) stands on the enumerations above rather than on `CLAUDE.md`'s prose.
3. **SA-2 still owes its own commit**, in both repositories, before anything else touches `asyncapi.yaml`.
