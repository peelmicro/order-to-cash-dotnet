# review: orders_stock_check_rpc_error_discriminator (feature 46)

**Verdict: REJECTED.**

The fix itself is correct, correctly placed and genuinely guarded — both mutation families killed under my own independent probes, not just the implementer's. It is rejected for two things it did *not* do, both of which are the class this feature exists to close, one file over and one report section over:

1. **The central design decision — forwarding the responder's own `code` onto the `orders.create` wire — was taken without asking what #7 did, and #7 did the opposite.** `orders.create` can now answer with an `RpcError.code` that `asyncapi.yaml`'s enum does not permit. I proved it end-to-end: a stand-in responder answering `NOT_A_CONTRACT_CODE` produced exactly that code on the `orders.create` reply, and the test still passed.
2. **The null-dereference sweep the brief asked for was reported as clear, and it is not.** `tests/Fulfillment.IntegrationTests/StockCheckTests.cs:55-57` identifies an unexpected `RpcError` reply solely by throwing on a null collection — the exact shape of the bug this feature fixes, in a file the implementation report lists as read and cleared.

Neither is a "the code is wrong" defect. Both are "the check that was asked for was recorded as done and was not done", which is the failure mode this harness is built around.

---

## What I ran, and what I did not

Per `CLAUDE.md` ("probe the claims, do not re-run the world"), I did **not** re-run `./quality.sh` or the full `Orders.IntegrationTests` project. The implementer's 628/628 and 68/68 stand as its own claim, and the leader independently confirmed `init.sh` exit 0 / 50 features.

What I ran myself:

| Run | Result |
|---|---|
| `dotnet test tests/Architecture.Tests` (NetArchTest — the C3 box, run not eyeballed) | 16/16 passed |
| `dotnet test tests/Orders.UnitTests` (baseline + probe 3 + confirming restore) | 256/256 passed |
| `dotnet test tests/Orders.IntegrationTests --filter NatsStockAvailabilityCheckerTests` | 2/2 passed |
| `dotnet test tests/Orders.IntegrationTests --filter OrdersCreate_MapsAStockCheckErrorReply…` | 1/1 passed |
| `diff -rq` `specs/shared` against the #7 checkout | only `test-matrix.md` differs — C7 satisfied |
| 5 independent mutation probes (below) | 5 killed, 1 deliberate survivor that is defect **D2** |

Every probe used a scratchpad backup copy (never `git checkout --`), a `touch` on the restored file to force the rebuild, a source re-read, and a confirming green run. The working tree at the end of this review is byte-identical to the state the implementer left (`git diff --stat` on `src/Orders/` matched the pre-probe stat line for line).

---

## Mutation probes — mine, not the implementer's

The implementer's arming table covers deletion of the discriminator and corruption of the code *in the mapper*. I re-armed the corruption family at a **different site** (the checker itself, where the code is first read off the wire), plus the shared helper the refactor created.

| # | Family | Mutation | File | Test | Outcome |
|---|---|---|---|---|---|
| P1 | corruption | `StockCheckBusinessError(…, error.Code, …)` → `(…, "UNAVAILABLE", …)` — discriminator intact, the code it forwards is wrong | `NatsStockAvailabilityChecker.cs:70` | `NatsStockAvailabilityCheckerTests.AnRpcErrorReply_…` | **KILLED** 2/2 — `Assert.Equal() Failure: Strings differ` |
| P2 | corruption | `error.Message` → the constant `"the stock check failed"` | `NatsStockAvailabilityChecker.cs:70` | same | **KILLED** 2/2 — `Assert.Equal() Failure: Strings differ` |
| P3 | deletion, at the shared site | `RpcJson.IsErrorBody` returns `false` unconditionally | `RpcJson.cs:34-41` | `Orders.UnitTests` (full) | **KILLED** — 14 failures, all `NatsSagaCommandsAdapterTests` (feature 42's guards), proving the promotion of the private predicate to shared code did not orphan its coverage |
| P4 | deletion, observed at the checker | same mutation as P3 | `RpcJson.cs:34-41` | `NatsStockAvailabilityCheckerTests` | **KILLED** 2/2 — `Expected: StockCheckBusinessError / Actual: System.ArgumentNullException … at System.Linq.ThrowHelper` — i.e. the original bug reproduced exactly, confirming the report's `ArgumentNullException`-not-`NullReferenceException` note |
| P5 | contract | responder answers `code: "NOT_A_CONTRACT_CODE"`, in the checker test *and* end-to-end through `orders.create` | test literals only | **SURVIVED — by design.** Both tests pass, and the `orders.create` wire reply carries `"code": "NOT_A_CONTRACT_CODE"`. This is defect **D2** |

P3 is worth keeping in the record for its own sake: the refactor that promoted `IsRpcErrorBody` out of `NatsSagaCommandsAdapter` into `RpcJson` is behaviour-preserving (`git diff` shows the body moved character-for-character) **and** still fully covered by feature 42's fourteen theory cases. That part of the change is unambiguously good.

---

## Acceptance-bullet → test mapping (`sdd: false`; the four bullets of `feature_list.json` id 46 are the specification of record)

| Bullet | Named test(s) | Verified |
|---|---|---|
| 1 — "an RpcError-shaped reply … is discriminated before the typed deserialisation, exactly as `NatsSagaCommandsAdapter` does" | `NatsStockAvailabilityCheckerTests.AnRpcErrorReply_ThrowsStockCheckBusinessErrorCarryingTheRespondersCodeAndMessage_NeverANullReferenceException` (2 cases, real NATS broker, real stand-in responder answering raw `RpcError` bytes) | **YES** — and literally the same code path: `RpcJson.IsErrorBody`, one shared implementation, P3 proves both callers depend on it |
| 2 — "the resulting error carries the responder's own code and message, and `orders.create` maps it to a meaningful RPC error rather than `INTERNAL_ERROR`" | `OrdersCreateErrorMapperTests.Map_StockCheckBusinessError_MapsToTheRespondersOwnCodeAndMessageNotInternalError` (2 codes) + `OrdersCreateAcceptanceTests.OrdersCreate_MapsAStockCheckErrorReplyToTheRespondersOwnCodeAndMessageNotInternalErrorAndPersistsNoOrder` (end-to-end over the real responder, asserts no order and no outbox row persisted) | **Satisfied as written — but see D2.** The bullet is silent on *which* meaningful code, and the reading chosen diverges from #7 without saying so |
| 3 — "no path through the checker can throw a bare `NullReferenceException` on a well-formed error reply" | same integration test; P4 shows the pre-fix path threw `ArgumentNullException` from `Enumerable.Select`'s null-source guard | **YES for a well-formed error reply.** A *malformed* reply body is still unguarded — advisory A3 |
| 4 — "armed: removing the discriminator makes a named test fail" | implementer's arming table rows 1–4; independently reproduced here as P1–P4 | **YES**, and stronger than the bullet asks — both mutation families, at two sites each |

No `R<n>` maps to this feature and `specs/shared/test-matrix.md` is correctly untouched. I agree with that call: this is a defect fix, not a requirement realisation, and inventing an id would be a false parity claim.

---

## Defects

### D1 (blocking) — the null-dereference check was reported clear and is not

**File:** `tests/Fulfillment.IntegrationTests/StockCheckTests.cs:45-57`, and the claim about it at `progress/impl_orders_stock_check_rpc_error_discriminator.md:36`.

`FS22_AnswersAnUnknownProductWithAvailableZeroAndSufficientFalse_NeverWithAnRpcError` asserts a **negative** — "never with an `RpcError`" — and asserts it like this:

```csharp
var payload = RpcJson.Deserialize<StockCheckReplyPayload>(reply.Data!);
Assert.False(payload.Available);
var line = Assert.Single(payload.Lines);
```

`StockCheckReplyPayload` is `(bool Available, IReadOnlyList<StockCheckReplyLine> Lines)` — non-nullable positional (`src/Fulfillment/Infrastructure/Messaging/Rpc/StockRpcPayloads.cs:26`). An `RpcError` body deserialises into it with `Lines = null`, and the claim in the test's own name is then detected only by `Assert.Single` throwing on a null argument. **That is the same shape as the bug this feature fixes**: a typed success deserialisation applied to an error body, discovered by a null throw rather than by discrimination. My own feature-17 review already recorded it (`progress/review_fulfillment_stock.md:579`, guard **G2**: "Guarded, if indirectly — and the indirection is itself feature 46's bug shape").

The report states: *"I found no test currently in the tree that identifies an error reply by a null dereference"*, having listed `StockCheckTests.cs` among the files read. The statement is false, and it is false about the single most on-point test in the tree — the one guarding the very interaction this feature is about. The guard is weak rather than absent (it does fail, with an opaque message), so the code impact is small. The record impact is not: a confident negative closes the question permanently, and this repository's most expensive recurring failure is a check that was recorded as done.

**Why it matters beyond this feature:** `FS22` is the test that keeps the `stock.check` error path off the ordinary route. If Fulfillment ever starts answering an unknown product with `NOT_FOUND` instead of `available: 0`, `FS22` reports `ArgumentNullException: Value cannot be null` from an assertion helper — which reads as a broken test, not as a changed contract, and will be "fixed" with a `!` by whoever is on that day.

### D2 (blocking) — `orders.create` can now emit an `RpcError.code` the contract does not permit, and the decision that made it possible was taken without consulting #7

**Files:** `src/Orders/Infrastructure/Messaging/NatsStockAvailabilityChecker.cs:70`, `src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs:51-55`.

The responder's `code` string is carried through `StockCheckBusinessError.RpcErrorCode` and written **verbatim** into the outbound `RpcErrorPayload.Code`, with no validation anywhere on the path. Proven, not inferred (probe P5): a stand-in responder answering `code: "NOT_A_CONTRACT_CODE"` produces exactly that value on the `orders.create` reply, end-to-end through the real responder, and the acceptance test passes.

`specs/shared/asyncapi.yaml:2837-2851` declares `RpcError.code` as a closed twelve-value `enum`. Emitting anything else is a wire-contract violation by Orders, on the one field the whole error contract is machine-read by. The file's own unit-test class states the invariant that is now false — `tests/Orders.UnitTests/OrdersCreateErrorMapperTests.cs:10-15`: *"every failure the `orders.create` responder can observe, mapped to `asyncapi.yaml`'s **closed** `RpcError.code` enum"*. After this change the mapper is no longer closed over that enum, and neither the doc nor a guard was updated.

**And #7 faced precisely this decision and answered differently.** `apps/orders/src/infrastructure/messaging/nats-stock-availability.adapter.ts:98-99`:

```ts
if (isRpcErrorReply(body)) {
  throw new StockCheckTransportError(this.subject, `responder returned ${body.code}: ${body.message}`);
}
```

— the responder's code goes into the **message**, and the existing `StockCheckTransportError` is reused, which `apps/orders/src/presentation/rpc-error-mapper.ts:52-58` maps to **`UNAVAILABLE`**. So for the identical scenario #7 answers `code: UNAVAILABLE`, #8 answers `code: NOT_FOUND`. #7 structurally cannot emit an out-of-enum code; #8 can.

The implementation report's decision 2 (`progress/impl_…md:28`) argues the pass-through entirely from "the enum is the same closed set on both sides", and never mentions #7 — in a report whose *next section* cites `nats-stock-availability.adapter.ts` and its `isRpcErrorReply` guard by name. So #7's answer was in hand and was not applied to the decision it answers. That is the exact omission `CLAUDE.md`'s "Never hand the human an open question you could have closed" and the ported-idiom ledger both exist to prevent, repeated inside the feature whose purpose is to repair an earlier instance of it.

**To be fair to the decision on its merits, it is not simply wrong.** #7's collapse loses information: a terminal `PRECONDITION_FAILED` from Fulfillment reaches #7's caller as `UNAVAILABLE`, which reads as "transient, retry" for a refusal that retrying can never resolve. #8's pass-through preserves that distinction, and for the codes Fulfillment's `StockErrorMapper` actually produces today (`VALIDATION_FAILED`, `NOT_FOUND`, `PRECONDITION_FAILED`, `UNAVAILABLE`, `DOMAIN_ERROR`, `INTERNAL_ERROR`) it is defensible: a forwarded `VALIDATION_FAILED` is nearly always about client-supplied data anyway, since the `stock.check` request is derived from the client's own line items.

So the required change is **not** "go back to #7's shape". It is: **make the boundary closed, and record the divergence as a divergence.** An external string must not reach a wire enum field unvalidated — that is what `NatsSagaCommandsAdapter` already does one file over, in the very adapter bullet 1 names as the model (`NatsSagaCommandsAdapter.cs:144-156` reasons explicitly about "a code outside this closed set" and picks a deliberate default). This feature copied that file's discrimination step and not its closed-set discipline.

---

## The three questions the brief asked me to judge

**1. Decision one — declining feature 42's terminal-versus-transient split. Correct, and for the right reason.** The split exists to let `SagaCommandDispatcher`'s retry loop short-circuit; `fulfillment.stock.check` is called synchronously from `PlaceOrderCommandHandler` with `orders.create`'s caller already blocked, and there is no loop to short-circuit. Adding the split here would create a second, drifting copy of `IsTerminalRpcErrorCode` for no consumer. Accepted without reservation. Note only that its soundness rests on the *client* being able to tell terminal from transient — which is an argument for pass-through, and therefore an argument that D2's fix should preserve the distinguishing information rather than flatten it.

**2. Decision two — forwarding the responder's own code.** Judged above: defensible in substance, unclosed at the boundary, and undocumented as a divergence from #7. It **can** produce a reply the contract does not permit (proven). Whether a client "misreads" it is more marginal than it first looks — `NOT_FOUND` collides with Orders' own `ReferenceDataNotFoundError` arm and `VALIDATION_FAILED` with Orders' own request-validation arm, but `details.subject` distinguishes them and the client's remedy is the same in each case. The contract breach is the blocking half; the collision is worth a sentence in the type's doc, not a redesign.

**3. The `UNAVAILABLE`-versus-`TIMEOUT` split — intact, and this is a genuine third case.** `NatsStockAvailabilityChecker.cs:35-58` is untouched: `NatsNoRespondersException` → `StockCheckTransportError` → `UNAVAILABLE`, `NatsNoReplyException` and a null `Data` → `StockCheckTimeoutError` → `TIMEOUT`. The new arm is added *after* both, on a path that only exists when a reply body actually arrived, and `OrdersCreateErrorMapper`'s two pre-existing arms are byte-unchanged in the diff. Feature 15's blocking defect stays paid. The one wrinkle is D2's: Fulfillment answers `UNAVAILABLE` for its own `SqlException`/deadlock cases, so a forwarded `UNAVAILABLE` and Orders' own "no responder subscribed" are now indistinguishable by code (they differ in `message`, and both are correctly transient). Not a re-cut of the split, and not blocking.

**4. The null-dereference detection — not fixed, and reported as absent.** See D1.

---

## Advisories (not blocking, but the code is open — fold them into the same round if cheap)

- **A1 — a malformed reply body is still an opaque `INTERNAL_ERROR`.** `RpcJson.IsErrorBody` (`RpcJson.cs:36`) calls `JsonDocument.Parse`, which throws `JsonException` on a non-JSON body; nothing catches it, so it reaches `OrdersCreateErrorMapper`'s catch-all. #7 guards exactly this at `nats-stock-availability.adapter.ts:90-96` — *"reply payload was not valid JSON"* → `StockCheckTransportError`. **That is a second unported guard from the same thirty-line method, still missing after a feature dedicated to porting the guards from that method** — which is the strongest single piece of evidence in this review for the sweep question below.
- **A2 — `{"code": null, "message": null}` is treated as an error body.** `TryGetProperty` returns `true` for a null-valued property, so `IsErrorBody` says yes, `RpcErrorPayload.Code` deserialises to `null`, and the outbound reply omits `code` entirely under the nulls-omitted wire options — an `RpcError` missing a `required` field. Pre-existing from feature 42; the D2 clamp closes it for free.
- **A3 — `StockCheckBusinessError` is misnamed** for what it now carries. A forwarded `UNAVAILABLE` (Fulfillment's deadlock/`SqlException` arm) or `INTERNAL_ERROR` is not a business refusal. The XML doc is honest about the scope; the name is not. Cosmetic, but this type's name will be read by whoever ports the same seam into #9.
- **A4 — the stand-in responder's `StartErrorAsync` answers the harness's own `PROBE` request with an `RpcError` too** (`StandInFulfillmentStockCheckResponder.cs:55-56`). Harmless today because `WaitUntilSubscribedAsync` only checks `reply.Data is not null`, but it couples the readiness probe to a body shape it does not inspect. One line to make explicit.

---

## CHECKPOINTS walk

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 — the leader's run before this review; re-run after this review's status write, see the bookkeeping note.

### C2 — state is coherent
- [x] At most one feature `in_progress` (id 46, set back by this review; nothing else is in flight).
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [x] `progress/current.md` describes the active session.
- [x] Every `blocked` feature records why — none are blocked.

### C3 — architecture is respected
- [x] No infrastructure package referenced inside any `Domain/` folder — `dotnet test tests/Architecture.Tests`, **16/16 passed**, run not eyeballed.
- [x] No cross-service database access — this feature touches Orders only; no Fulfillment schema is read.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — unchanged; the promoted `IsErrorBody` went to `Orders`' own `Infrastructure/Messaging/Rpc`, correctly **not** to a shared project.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs`.
- [x] `src/SharedKernel` still has zero `PackageReference` entries.
- [x] No `decimal` in domain arithmetic — none introduced.
- [x] Every interaction classifiable as Kafka-fact or NATS-RPC — `fulfillment.stock.check` is a synchronous read over NATS RPC per `saga.md` §2; correct row of the matrix.
- [x] No stray debug logging, no context-free TODOs.

### C4 — verification is real
- [x] Domain tests are pure — none added; no domain code changed.
- [x] Integration tests use real containers — `NatsStockAvailabilityCheckerTests` drives a real NATS broker and a real second-connection responder; the acceptance test adds real MS-SQL. No mocked `INatsConnection` anywhere in this feature.
- [x] No Jest.
- [ ] `./quality.sh` passes — **not re-run by this review, deliberately** (`CLAUDE.md`: probe the claims, do not re-run the world). The implementer's run is on record at 628/628 across 12 projects; I verified 16 + 256 + 3 of those directly, and every probe's restore returned to green.
- [ ] Coverage thresholds — standing gap, not this feature's: the coverlet gate is feature 34 and has not landed. Recorded, not charged here.

### C5 — the session closed cleanly
- [x] No suspicious untracked files — the two new files are `progress/impl_…md` and `tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs`, both intended. My probe backups live in the scratchpad, outside the repository.
- [ ] `progress/history.md` entry with effort record — **correctly absent**: the feature is not closing. To be written by the review that approves it.
- [x] `feature_list.json` reflects the true state — id 46 set back to `in_progress` by this review.
- [ ] The human has been told what was done and how to test it manually — the leader's step, after re-review.
- [x] Claude did not commit.

### C6 — spec-driven development
Not applicable: `sdd: false`. The four acceptance bullets are the specification of record and are mapped test-by-test above.

### C7 — spec-reuse fidelity and benchmark honesty
- [x] `specs/shared/` still byte-identical to #7's except `test-matrix.md` — verified this session with a real `diff -rq` against the #7 checkout, not from memory. Only `test-matrix.md` differs, and this feature did not touch it.
- [x] Every deviation is a recorded amendment — no `specs/shared/` change here.
- [x] The `R<n>` ids are #7's — none claimed, correctly.
- [ ] n8n workflows / black-box API script — out of scope for this feature; unchanged.
- [ ] `progress/history.md` effort records complete — pending the closing review.
- [ ] README benchmark section — pending.
- **Note against this section:** D2 is a behavioural divergence from #7 on a wire-visible field, currently unrecorded anywhere. C7's amendment box is about `specs/shared/`, so this does not un-tick it — but "no silent fork" is the spirit of the section, and an `orders.create` that answers `NOT_FOUND` where #7 answers `UNAVAILABLE` is a fork of behaviour that the benchmark should not have to discover later.

---

## Required before re-review

1. **Close the wire boundary (D2).** `orders.create` must not be able to emit an `RpcError.code` outside `asyncapi.yaml`'s twelve-value enum, whatever a responder sends. Where the clamp lives is the implementer's call — the mapper arm is the natural place, since that is the wire boundary — and forwarding recognised codes may stay. Requirements: a named test that pins the closed set (a `[Theory]` including at least one out-of-enum code, asserting the emitted `code` is one of the twelve), **armed in both families** — remove the clamp and watch it fail, and corrupt the clamp's fallback and watch it fail. Update `OrdersCreateErrorMapperTests`' class doc so its "closed enum" claim is true again.
2. **Record the divergence from #7 (D2), with the citation.** One ledger line in `progress/impl_…md`, in the ported-idiom form: *"#7 relied on collapsing every `RpcError` reply to `StockCheckTransportError` → `UNAVAILABLE` (`nats-stock-availability.adapter.ts:98-99`, `rpc-error-mapper.ts:52-58`), so an out-of-enum code was structurally impossible; in #8 that property is supplied by `<the clamp>`, guarded by `<the test>`."* This is the artefact the ledger exists to produce, and this feature is the best possible occasion to produce one.
3. **Fix `FS22` and correct the record (D1).** `tests/Fulfillment.IntegrationTests/StockCheckTests.cs:45-57` should assert its own negative — discriminate the reply body explicitly (the body has no `code`/`message` pair) before the typed deserialisation — so "never with an `RpcError`" is asserted rather than incidental to a null throw. Then replace the "searched, not found as described" section of the implementation report with what is actually there. This is `test_maintainer`-sized work; it does not need the implementer.
4. **Re-run `./quality.sh`** after the above and record the counts. Nothing else needs re-proving: P1–P4 stand.

Advisories A1–A4 are not conditions. A1 is a one-`try` change in code that will already be open and I would take it now; the other three are judgement calls for the implementer.

---

## The sweep question — my judgement

**A sweep is warranted, but only a narrow one, and this review produced the evidence that decides it.**

The argument against a sweep is the usual one and it is not weak: four instances is a small sample, the ledger now protects every future translation, and an open-ended re-read of five services is expensive with a low prior of finding anything.

What defeats it is advisory **A1**. This feature was dispatched specifically to port a missing guard from `nats-stock-availability.adapter.ts`, the implementer read that file, and a **second** missing guard from the same thirty-line method — #7's malformed-JSON catch — is still absent. Two unported guards in one method, one of them still unported after a dedicated repair pass, is not a base rate you can argue away. It says the class is denser than four instances suggest and that reading a file for one gap does not surface the next.

The second thing that decides it is that the class has a **cheap signature**, which the general worry does not. Every instance so far — key ordering, money width, the counter seed, this one, A1 — sits at a **boundary where a value crosses into or out of the process**: a decode, a serialise, a column write, an upsert. Those sites are enumerable rather than open-ended. In the shipped services they are roughly: each NATS RPC client's reply decode, each responder's request decode, the Kafka consumer envelope decode, the outbox relay's payload read-and-republish, and the projector's Mongo read. That is on the order of eight sites, each a five-minute comparison against #7's counterpart with one question asked: *what did #7's version of this decode do that this one does not?*

So: not a sweep of the services, a sweep of the **boundaries**. Time-boxed, output is a retroactive ledger plus one backlog entry per real gap found — never fixes in place, because a fix inside a sweep is a change nobody reviewed against a spec.

Backlog entry wording, for the leader to add (I have not added it — one writer at a time, and the entry is the leader's to scope):

> **`retroactive_ported_idiom_ledger_decode_boundaries`** — Walk every value-crossing boundary in the services ported before the ledger was adopted (Phase 8): each NATS RPC client reply decode, each RPC responder request decode, the Kafka consumer envelope decode, the outbox relay's payload read-and-republish, and the Mongo projector's read path. For each, read #7's counterpart and answer one question in one line: *what did #7's version guarantee here, and what supplies that in #8?* Produce a retroactive ledger section per service; file a separate backlog entry for every gap found and **fix nothing in place**. Time-box the read; a boundary that takes more than ten minutes to compare is itself a finding. Justification: feature 46 closed the fourth instance of the class and left a fifth in the same thirty-line method (#7's malformed-JSON guard, `nats-stock-availability.adapter.ts:90-96`), which is direct evidence that a dedicated repair pass on a file does not surface the next gap in it. The ledger protects new translations only; four services were ported before it existed.

---

## Bookkeeping

- `feature_list.json` id 46 set back to **`in_progress`**. Single-line edit, diff read and confirmed to contain that line and nothing else. No `git checkout --` was run on this file, and no other agent was writing it.
- **No `progress/history.md` entry** — the feature is not closing, and a rejected feature has no effort record to write. When it closes, the record should say plainly that **#7 has no counterpart**: #7 never had this defect, its adapter carried the union decode from the start, so this feature's entire cost is #8-specific rework and must be recorded as such rather than benchmarked against an invented baseline. The re-review round caused by this rejection belongs in that number too.
- Working tree left exactly as the implementer produced it; all five probes restored, rebuilt and re-confirmed green.

---

# Round 2 — re-review of the fix round

> Round 1 above is unchanged and not reopened. This section judges only the fix round appended to `progress/impl_orders_stock_check_rpc_error_discriminator.md`.

**Verdict: APPROVED.**

Both blocking defects are closed, and closed in the way the round-1 verdict asked rather than in the cheapest way that would satisfy the sentence. D2's clamp is complete against `asyncapi.yaml` (checked programmatically, code by code), it is the only path by which an external string can reach a wire `code` field in Orders (checked by enumerating every `new RpcErrorPayload(` in `src/`), and my own containment-deletion probe now kills three unit cases **and** the end-to-end acceptance test — the P5 survivor of round 1 is dead. D1's `FS22` fix is real: I armed it myself (the implementer did not) and it fails with its own message rather than an opaque throw.

Two things are recorded rather than charged. The leader's `R31` question resolves **against** the charge, on evidence rather than opinion. And the class D1 belongs to survives at three other sites, none of which is D1 and none of which is this feature's to fix — with backlog wording below, and a mechanism recommendation, because this is the third sweep-reported-clear in three features.

## What I ran this round, and what I did not

Per `CLAUDE.md`, I did **not** re-run `./quality.sh`; the implementer's 636/636 across twelve projects stands as its own claim, and the leader independently has `init.sh` exit 0. What I ran myself, all of it after a forced `--no-incremental` rebuild where a mutation was involved:

| Run | Result |
|---|---|
| `dotnet test tests/Architecture.Tests` (C3, run not eyeballed) | 16/16 passed |
| `dotnet test tests/Orders.UnitTests` (full project, restored) | 262/262 passed |
| `dotnet test tests/Orders.IntegrationTests --filter` clamp-e2e + `NatsStockAvailabilityCheckerTests` | 4/4 passed |
| `dotnet test tests/Fulfillment.IntegrationTests --filter StockCheckTests` (restored) | 2/2 passed |
| Programmatic diff of the mapper's `_contractRpcErrorCodes` against `specs/shared/asyncapi.yaml`'s `RpcError.code` enum | 12/12, same set, same order, no duplicates |
| Enumeration of every `new RpcErrorPayload(` in `src/` and every reader of a responder-supplied code in `src/Orders/` | one wire boundary, and it is clamped |
| #7's `nats-stock-availability.adapter.ts` and `rpc-error-mapper.ts` read directly at the cited lines | citations accurate — see below |
| 5 mutation probes, both families | 4 killed, **1 deliberate survivor** = advisory A5 |

Every probe used a scratchpad `cp` backup (never `git checkout --`), `touch` + `--no-incremental` before the confirming run, a source re-read, and a byte-`cmp` against the backup after restore. Both probed files `cmp` clean at the end of this review.

## Mutation probes — mine, not the implementer's

| # | Family | Mutation | File | Test(s) | Outcome |
|---|---|---|---|---|---|
| P6 | deletion, at the clamp | `if (!_contractRpcErrorCodes.Contains(code))` → `… && code.Length < 0` (containment made unreachable without leaving the field unused, so the build shape is unchanged) | `OrdersCreateErrorMapper.cs:121` | `Map_StockCheckBusinessError_NeverEmitsACodeOutsideAsyncApisClosedRpcErrorEnum` (2 of 5 cases) + `…_ClampsToUnavailableAndPreservesTheOriginalCodeInDetails` | **KILLED** 3/16 — `Assert.Contains() Failure: Item not found in set` |
| P7 | deletion, end-to-end — **round 1's P5, re-run** | same mutation, observed through the real `orders.create` responder | same | `OrdersCreateAcceptanceTests.OrdersCreate_ClampsAnOutOfContractStockCheckErrorCodeToUnavailableAndPreservesTheOriginalCodeInDetails` | **KILLED** 1/1 — `Expected: "UNAVAILABLE" / Actual: "NOT_A_CONTRACT_CODE"`. Round 1's survivor is dead; the contract is now closed on the wire, proven over a real broker and a real MS-SQL |
| P8 | corruption, of the set's **contents** | removed `"CONFLICT"` from `_contractRpcErrorCodes` — a legitimate contract code silently dropped from the mapper's copy | `OrdersCreateErrorMapper.cs:31` | full `tests/Orders.UnitTests` | **SURVIVED — 262/262 green.** This is advisory **A5** |
| P9 | corruption, on the wire | `FS22`'s request replaced with `new StockCheckRequestPayload("", [])`, so the **real** Fulfillment responder answers a real `RpcError` where the test's name says it never does | `tests/Fulfillment.IntegrationTests/StockCheckTests.cs:53` | `FS22_…_NeverWithAnRpcError` | **KILLED** 1/1 — `fulfillment.stock.check answered an unknown product with an RpcError-shaped reply, not a StockCheckReplyPayload: code=VALIDATION_FAILED, message=…` |
| P10 | corruption, on the wire | same mutation applied to `R31`'s request instead | `tests/Fulfillment.IntegrationTests/StockCheckTests.cs:26` | `R31_AnswersPerLineWithoutMutatingAStockItemAndWithoutEmittingAFact` | **KILLED at line 30** — `Assert.True() Failure / Expected: True / Actual: False`, stack frame `StockCheckTests.cs:line 30`. This is the evidence for the `R31` judgement below |

**P9 is the arming the fix round owed and did not supply.** The round-2 arming table has six rows and not one of them touches D1: the `FS22` guard was written, ticked and never seen to fail. That is precisely the *"a tick is not evidence the assertion exists"* pattern `CLAUDE.md` names, in the round whose own D1 section diagnoses a failure of the same family. It is not blocking because I armed it here and it holds — with a legible, self-describing message that names the code and the message it found, which is a strict improvement on the `ArgumentNullException` it replaced. But **the next fix round that repairs a guard must arm the repaired guard**, and a review should not have to supply that.

## 1. D1, and whether the fix went one line deep — the `R31` charge does not hold, and I can show why

**`StockCheckTests.cs:29-31` is not the same defect.** The distinguishing property is not "does this test deserialise an error body into the typed success payload" — every one of them does, because none of them discriminates. It is **"is the first assertion after the deserialisation satisfied by an all-defaults object?"**

Deserialising `{"code":"NOT_FOUND","message":"…"}` into `StockCheckReplyPayload(bool Available, IReadOnlyList<StockCheckReplyLine> Lines)` under `JsonWire.Options` yields `Available = false, Lines = null` — verified in a scratch program on this SDK, because `RespectRequiredConstructorParameters` is not set in `JsonWire.Build()`, so absent constructor parameters take their defaults and `Deserialize` returns a non-null object. Therefore:

- **`FS22`'s** first assertion was `Assert.False(payload.Available)` — **satisfied** by the all-defaults object. The test sails past it and the *next* line, `Assert.Single(payload.Lines)`, throws. The negative in the name was proven by an accident. That was D1.
- **`R31`'s** first assertion is `Assert.True(payload.Available)` — **not** satisfied. P10 proves it: the test fails at `StockCheckTests.cs:line 30`, one line before `Assert.Single`, with a real assertion failure. The error body is caught by an assertion about content, not by a null dereference.

So a happy-path test that asserts a *positive* about the payload is different in kind, and `R31` is one. The fix did not stop at the line my verdict cited; it stopped where the defect stops. Charge dismissed, with the caveat that `R31`'s message (`Expected: True / Actual: False`) says nothing about *why* — that is a legibility gap, not a detection gap, and not worth a round.

**But the leader's underlying worry is right, and I found where.** One `grep` over the test tree — `grep -rn "Deserialize<Stock\|Deserialize<Orders\|Deserialize<Saga" tests/ -A 3` — enumerates every site in about ten seconds and classifies them by that one question. Three sites fail it, none of them D1 and none of them in scope here:

| Site | Shape | Why it is not D1 |
|---|---|---|
| `tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs:159` — `RpcJson.Deserialize<OrdersCreateReplyPayload>(replyMsg.Data!); // the request succeeded` | **Vacuous.** An `RpcError` body constructs an all-defaults `OrdersCreateReplyPayload` and `Deserialize` returns it non-null, so this line asserts *nothing at all* — worse than D1, which at least threw. The comment is the whole claim | Pre-dates this feature (feature 15's test), asserts nothing about feature 46's behaviour, and no acceptance bullet reaches it |
| `tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:52` — `var item = Assert.Single(payload.Items);` as the first assertion | Detected only by `Assert.Single(null)` throwing — D1's mechanism exactly | Feature 17's test; its name claims no negative about reply shape |
| `tests/Fulfillment.IntegrationTests/StockListTests.cs:27` — `Assert.Equal(2, byCompany.Items.Count)` | Detected only by a null dereference on `.Count` | Same |

Every other site — nine of them — opens with an `Assert.Equal("released"/"accepted"/"rejected"/"already_reserved", payload.Outcome)` on a `string` that an error body leaves `null`, and fails legibly. The tree is in better shape than three-in-three suggests; the three that are not are worth one backlog entry, not a rejection.

## The mechanism question — yes, it needs one, and here it is

You asked whether the pattern now needs a mechanism rather than another correction. It does, and the three instances agree on what kind.

**All three sweeps failed the same way: a claim about an absence was reported as prose, from a read.** Feature 17's A6 ("no instrument reads the schema as text"), this feature's round-1 sweep ("no test identifies an error reply by a null dereference"), and the round-2 residue above. In each case the reading was genuine and the criterion was wrong, so re-reading harder would not have helped — and in this one the missed instance had **already been written down in this repository**: `progress/history.md`'s Phase-9 dependency note, committed at 11:30 today, eleven minutes before this feature started, says verbatim *"The same shape appears in `FS22`'s own assertion, which detects an `RpcError` only by throwing at `Assert.Single(null)`."* A prose record did not stop a prose sweep from missing it. That is the argument against fixing this with better prose.

**The mechanism: a sweep for an absence is not reportable as prose. Its deliverable is a command, its output, and one classification line per hit.**

Concretely, a brief that asks for a sweep and a report that answers one must both carry:

1. **The enumerating command** — the exact `grep`/`rg` that produces the candidate set, chosen so that *false positives are cheap and false negatives are structurally hard*. For this class: every call site of a typed deserialiser in `tests/`, `-A 3`.
2. **Its full output, pasted.** Not a summary.
3. **One line per hit** stating the classification and the reason — here, *"first assertion is `Assert.Equal("accepted", …)` on a string → an error body fails it legibly"*.

Then a missed instance is **visible** as a hit with no classification line, instead of invisible as a sentence. It is also independently re-runnable: I re-ran the command in ten seconds and it produced three sites the prose sweep did not mention. Compare the cost — the round-1 sweep read five files and was wrong; the command reads the whole tree and cannot silently skip one.

Keep the implementer's own proposal (*"construct one candidate a correct search should have found and check it lands in the positive list"*) as a second line of defence. It is good, and it caught nothing here only because it was formulated after the fact. But it depends on imagination, and the command does not — so **the command is the mandatory half**.

Suggested wording for `CLAUDE.md`, under Testing conventions, next to the arming protocol:

> **A negative claim about the repository is a search result, not a reading.** "No test does X", "no instrument does Y", "nothing else has this shape" — a claim of absence is reportable only as (a) the exact command that enumerates the candidate set, (b) its complete output, and (c) one classification line per hit. Prose sweeps have been reported clear and disproved within minutes three times (features 17, 46 round 1, 46 round 2), each time by someone who ran a command instead of re-reading. A missed hit must be visible as an unclassified line, not invisible as a sentence.

## 2. D2's containment, and whether the cure carries the disease

**The set is complete and correct.** Parsed out of `specs/shared/asyncapi.yaml:2839-2851` and compared with the twelve literals at `OrdersCreateErrorMapper.cs:29-34`: same twelve values, same order, no duplicates, no extras. The clamp is also the *only* exposure — `new RpcErrorPayload(` appears eleven times in `src/Orders/`, ten with hard-coded literals, and `OrdersCreateErrorMapper.cs:127` is the single site fed by an external string. `NatsSagaCommandsAdapter` reads responder codes too, but only into an exception message and a terminal/transient switch, never onto a wire `code`. Containment is complete for this service.

**And yes, the cure carries the disease — in the mildest form, and I have a probe for it.** P8: delete `"CONFLICT"` from the mapper's copy and the entire `Orders.UnitTests` project stays green at 262/262. A responder answering the perfectly legal `CONFLICT` would then be clamped to `UNAVAILABLE` — a transient code for a terminal refusal, with the truth demoted to `details.responderCode` — and nothing would say so. The guard test cannot see it because it holds **its own hand-retyped copy** of the same twelve values (`OrdersCreateErrorMapperTests.cs:87-92`), so the test and the mapper can only disagree if someone edits one; a *narrowing* of both, or a narrowing of the mapper toward a value the test never exercises, is invisible.

That is A6-of-feature-17 exactly, which is backlog id **51**, and **it should be folded into id 51's scope** rather than filed separately: same class, same repository-wide remedy, same natural moment. Two details make the fold cheap and the case stronger than A6's:

- The established pattern is one file away in the same test project — `RpcSubjectsTests.cs:32-33` uses `RepositoryPaths.Find(Path.Combine("specs","shared","asyncapi.yaml"))` + `File.ReadAllText`. Deriving the twelve codes here is a ten-line change reusing existing helpers.
- There are now **three** hand-retyped copies of this one closed enum: the mapper's set, the test's set, and `NatsSagaCommandsAdapter.IsTerminalRpcErrorCode`'s nine-plus-three split (`NatsSagaCommandsAdapter.cs:150-156`). Feature 17 already had to write a guard that reads the terminal set *out of the adapter's own source* rather than retyping it a fourth time (history, L7) — which is the tell that the retyping has become load-bearing.

Wording to append to id 51's `acceptance` (the leader's to add — I have added nothing):

> **also derives `asyncapi.yaml`'s twelve-value `RpcError.code` enum by parsing the spec, and asserts the three hand-retyped copies of it agree with the parsed set — `OrdersCreateErrorMapper._contractRpcErrorCodes`, `OrdersCreateErrorMapperTests._contractRpcErrorCodes`, and `NatsSagaCommandsAdapter.IsTerminalRpcErrorCode`'s terminal/transient split, whose union must be the closed set; armed by deleting one code from the production set in a scratch copy and watching a named test fail (feature 46's review probe P8 removed `CONFLICT` and the whole 262-test project stayed green)**

## 3. Re-arming the contract containment — done, both directions

Round 1's P5 survived; its successor P7 dies, end-to-end, over a real broker and a real MS-SQL, with the exact message the fix promises. And the guards fail for the **containment** rather than only for the discriminator: P6 leaves the discriminator, the `StockCheckBusinessError` arm and the pass-through of recognised codes completely intact and only makes the closed-set check unreachable — three named unit cases and the acceptance test fail on that alone. The fallback *value* is separately guarded: the implementer's row 3 (`UNAVAILABLE` → `INTERNAL_ERROR`, an in-set corruption the containment `Theory` cannot see) is killed only by the dedicated `…_ClampsToUnavailableAndPreservesTheOriginalCodeInDetails` fact, and its report says so explicitly instead of claiming both tests do the work. That is the right shape.

## 4. The #7 comparison, and where A1 landed

**The ledger's claims check out, line by line, against the checkout.** `apps/orders/src/infrastructure/messaging/nats-stock-availability.adapter.ts:98-99` is `if (isRpcErrorReply(body)) { throw new StockCheckTransportError(this.subject, \`responder returned ${body.code}: ${body.message}\`); }`, and `apps/orders/src/presentation/rpc-error-mapper.ts:52-58` maps `StockCheckTransportError` → `UNAVAILABLE`. So #7 collapses **every** error reply, recognised or not, to `UNAVAILABLE`, and an out-of-enum code on its `orders.create` wire is structurally impossible — the ledger line's central claim, confirmed by reading rather than by citation-copying. The round-2 report is also more honest about the divergence than round 1 demanded: #8 now matches #7 **exactly** for the unrecognised-code case and differs only for recognised ones, where it is deliberately more informative. That narrowing is correct, and it is recorded in `progress/impl_…md` **and** in `MapStockCheckBusinessError`'s own XML doc, where the next translator will actually meet it. Both guard tests the ledger names exist and I ran them.

**A1 landed in the feature, and that was the right call.** It is in the same thirty-line method the feature already had open, `NatsStockAvailabilityChecker.CheckAsync:83-93`, it uses #7's own error type and very nearly #7's own words (`nats-stock-availability.adapter.ts:90-96`, *"reply payload was not valid JSON"*), and it is guarded over a real broker by `AMalformedNonJsonReply_ThrowsStockCheckTransportError_NeverABareJsonException`, which I ran. Closing it here **removes** a site from id 52's sweep rather than duplicating it, which is the correct relationship between a repair and a sweep. The implementer's note that id 52's `notes` field now contains a stale sentence (*"still absent"*) is correct and is the leader's to amend; the sweep's justification does not rest on it, and my round-1 reasoning that A1 was the deciding evidence stands as a statement about what was true when the sweep was recommended.

## Defects

**None blocking.** Round 1's D1 and D2 are both closed and independently re-probed.

## Advisories (round 2 — none blocking, none a condition of approval)

- **A5 — the clamp's set is hand-retyped, and narrowing it is undetectable.** `OrdersCreateErrorMapper.cs:29-34` and `OrdersCreateErrorMapperTests.cs:87-92`. Proven by P8 (`CONFLICT` removed, 262/262 green). Fold into backlog id 51 with the wording above; not a defect today, because the set is currently exact.
- **A6 — the A1 `try` is wider than #7's, and its message can now be false.** `NatsStockAvailabilityChecker.cs:61-93` wraps the discriminator **and both typed deserialisations**; #7 wraps only `replyCodec.decode`. So a reply that is valid JSON of the wrong shape (`{"available":"yes"}`) is reported as `"reply payload was not valid JSON."` — a true-sounding sentence about a body that parsed fine, classified transient. It is still better than #7, which would reach `body.lines.map` and throw a `TypeError`; the cost is one misleading message on a path no test takes. One sentence in the message, or a narrower `try`, whenever that file is next open.
- **A7 — the fix round did not arm its own D1 repair.** Six arming rows, none for `FS22`. Supplied here as P9. Recorded because the pattern (*a repaired guard is a guard*) is the one this feature exists to fix.
- **A8 — `progress/current.md` is stale, and after this approval it makes `init.sh` fail.** It names feature 46 and `in_progress` (now `done`) while its Goal, Decisions and Notes sections still describe `fulfillment_stock` opening Phase 9. With id 46 `done` there is no active feature, so `init.sh` now reports `[FAIL] progress/current.md claims a feature while none is active` and exits 1 — the check firing correctly, on a file this review does not own. **Reset it to the template at its own foot (or write the next feature's session) and `init.sh` returns to 0**; every other check in that run is green. Leader's file, leader's step — flagged, not touched.
- Round 1's A2, A3 and A4 were not conditions and remain open by the implementer's judgement. A2 (`{"code":null,"message":null}`) is now **closed for free** by the clamp: a null `Code` is not in the set, so it clamps to `UNAVAILABLE` and the wire always carries a required `code`. A3 (the name `StockCheckBusinessError`) and A4 (the stand-in's `PROBE` reply) stand.

Backlog wording, if you want the three residual null-detection sites carried rather than dropped (**I have added nothing to `feature_list.json` beyond id 46's status**):

> **`test_reply_shape_assertions_that_only_throw`** — Three integration tests detect a wrong-shaped RPC reply only by a null dereference or not at all: `tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs:159` deserialises an `OrdersCreateReplyPayload` and asserts **nothing** (the comment `// the request succeeded` is the whole claim, and an `RpcError` body constructs an all-defaults object without throwing); `tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:52` and `StockListTests.cs:27` open with `Assert.Single(payload.Items)` / `Items.Count` on a collection that is `null` for any error body. Each should assert the reply's own discriminating field first — the shape `FS22` now uses after feature 46's D1. Found by feature 46's review round 2 with one `grep` over every typed deserialiser call site in `tests/`, after a prose sweep of the same question had been reported clear; nine other sites pass the same check because they open with an `Assert.Equal` on a `string` field. Not a production defect: the responders do answer correctly, and each test does fail today — opaquely, or in `OrdersCreateAcceptanceTests`' case, not at all.

## CHECKPOINTS walk — round 2

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [ ] `./init.sh` exits 0 — **it exited 0 before this review and exits 1 after it**, and the failure is advisory **A8** firing as designed: `[FAIL] progress/current.md claims a feature while none is active: "**Feature:** `orders_stock_check_rpc_error_discriminator` (id 46, phase 9)"`. Setting id 46 `done` leaves no feature active, so `progress/current.md` must be reset to the template it carries at its own foot (or rewritten for the next feature). **That is a leader-owned session-close step and I have deliberately not taken it** — `progress/current.md` has one writer, and this review is not it. Nothing about feature 46 is unresolved by it; the box is unticked because that is exactly the state on disk. **Re-run `./init.sh` after resetting the file and it returns to 0** — every other check in the run is green, including the backlog tripwire (`no feature lost, no done reverted`) and the 51-feature parse.

### C2 — state is coherent
- [x] At most one feature `in_progress` — **zero** after this review; id 46 goes to `done`.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it — id 46's are named in the mapping above and were run this session.
- [x] `progress/current.md` describes the active session — its header does; its body is stale (**advisory A8**), which is a leader-owned bookkeeping gap and not a state incoherence.
- [x] Every `blocked` feature records why — none are blocked.

### C3 — architecture is respected
- [x] No infrastructure package inside any `Domain/` folder — `dotnet test tests/Architecture.Tests` **16/16 passed**, run this round, not eyeballed.
- [x] No cross-service database access — round 2 touched one Fulfillment **test** file and no Fulfillment production code or schema.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — unchanged; `RpcJson.IsErrorBody` stayed in Orders' own `Infrastructure/Messaging/Rpc`.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs`.
- [x] `src/SharedKernel` still has zero `PackageReference` entries.
- [x] No `decimal` in domain arithmetic — none introduced.
- [x] Every interaction classifiable as Kafka-fact or NATS-RPC — `fulfillment.stock.check` is NATS-RPC per `saga.md` §2.
- [x] No stray debug logging, no context-free TODOs — the new comments all cite the review item they answer.

### C4 — verification is real
- [x] Domain tests are pure — no domain code changed.
- [x] Integration tests use real containers — real NATS for the checker tests, real NATS + MS-SQL for the clamp end-to-end, real NATS + MS-SQL + Kafka for `FS22`. No mocked `INatsConnection`.
- [x] No Jest.
- [ ] `./quality.sh` passes — **not re-run by this review, deliberately**. The implementer's run is on record at 636/636 across twelve projects with `dotnet format --verify-no-changes` clean; I independently verified 16 + 262 + 4 + 2 of those and every probe restored to green.
- [ ] Coverage thresholds — standing gap, not this feature's: the coverlet gate is feature 34 and has not landed.

### C5 — the session closed cleanly
- [x] No suspicious untracked files — three: the two `progress/` reports and `tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs`, all intended. My probe backups are in the scratchpad, outside the repository.
- [x] `progress/history.md` entry with effort record — written by this review; see below.
- [x] `feature_list.json` reflects the true state — id 46 → `done`, single-line edit, diff read.
- [ ] The human has been told what was done and how to test it manually — the leader's step.
- [x] Claude did not commit.

### C6 — spec-driven development
Not applicable: `sdd: false`. The four acceptance bullets are the specification of record, mapped test-by-test in round 1 and re-verified here; the round-2 additions (the clamp, A1) are review-imposed requirements, not new bullets, and both are named-and-armed.

### C7 — spec-reuse fidelity and benchmark honesty
- [x] `specs/shared/` untouched by round 2 — the round-1 `diff -rq` against the #7 checkout stands and no file under `specs/` appears in `git status`.
- [x] Every deviation is a recorded amendment — the divergence from #7 is now recorded in the ledger form, in the report **and** in the source's own XML doc. Round 1's note against this section is discharged: `orders.create` no longer answers a code the contract forbids, and where it deliberately differs from #7 (forwarding recognised codes) the difference is written down with citations.
- [x] The `R<n>` ids are #7's — none claimed by this feature, correctly; `specs/shared/test-matrix.md` untouched.
- [ ] n8n workflows / black-box API script — out of scope, unchanged.
- [x] `progress/history.md` effort records complete — this feature's is appended, and it says plainly that **#7 has no counterpart**.
- [ ] README benchmark section — pending, phase-level.

## Bookkeeping

- `feature_list.json` id 46 → **`done`**. Single-line edit (`sed` on the one `"status"` line inside id 46's block), `git diff` read afterwards and confirmed to contain that line and nothing else. **No `git checkout --` was run on this file**, and no other agent was writing it. The backlog still holds **51** features; I added none, and the two wordings above are the leader's to add or discard.
- `progress/history.md` — entry appended with the effort record, including the statement that **#7 has no counterpart for this feature** and that the whole cost, both review rounds included, is #8-specific rework.
- **`./init.sh` exits 1 after this review, by design and not by breakage** — see C1 and advisory A8. The one failing check is `progress/current.md` still claiming an active feature now that none is; resetting that leader-owned file clears it. The backlog tripwire, the 51-feature parse, the status validity and the SDD coherence checks are all green in the same run.
- Working tree left byte-identical to the state the fix round produced: `OrdersCreateErrorMapper.cs` and `StockCheckTests.cs` both `cmp`-clean against my backups after their probes, both rebuilt `--no-incremental`, both re-run green.
