# review: `completion_pair_has_no_causal_edge` (backlog id 57, phase 11 backlog, closed at the top of phase 12)

**Verdict: APPROVED** — 0 blocking defects, 1 advisory (the sweep's enumerating command; conclusion holds on my own enumeration, recorded below for the projector).

`sdd: false`; the specification of record is `feature_list.json` id 57's four acceptance bullets. Port of the Billing slice of #7's `bf59af9`. Reviewed 2026-09-07 ≈04:59 → ≈05:08 CEST.

## What I ran, and what I did not

Not re-run: `./quality.sh` and the full 1055-test solution — the claims under test here are about two guards and one four-file diff, not the suite, so the implementer's `quality.sh` exit 0 / 1055 figure is taken as reported and marked as such. Run by me: `Architecture.Tests` 16/16; `Billing.UnitTests` 226/226 (baseline and again after the restores); `Billing.IntegrationTests` filtered to the `PaymentRegisterTests` class, 9/9 on real Testcontainers MS-SQL/NATS/Kafka after the restores; `dotnet format OrderToCash.sln --verify-no-changes` exit 0; `./init.sh` exit 0 (58 features, 35 done, no `in_progress`); `diff -rq specs/shared` against the #7 checkout — only `test-matrix.md` differs, as C7 permits; two of my own mutation probes with forced `--no-incremental` rebuilds (below).

## Probe 1 — bullet four taken literally: the failure must be on the envelope, not row order

Backups of both production files taken to the scratchpad before arming; restores by `cp`, verified with `cmp` and by re-reading the line, md5 re-checked against the pre-mutation values (`Invoice.cs` `19ce38a7…`, `PaymentRegisterService.cs` `a0ecbc10…`), then `touch` + `dotnet build --no-incremental` on both test projects before the confirming green run.

**Probe A (the leader's): revert the stamp to the request id** — `PaymentRegisterService.cs:132` → `new CreditContext(ctx.OccurredAt, command.RequestId)`.

- Unit `Backlog57_CreditReleasedCausationIdIsPaymentReceivedsOwnEventId_NotTheRequestIdBothFactsUsedToShare` — FAILED at `PaymentRegisterServiceTests.cs:line 166`, which is `Assert.Equal(paymentReceived.EventId, creditReleased.CausationId)`: `Assert.Equal() Failure: Values differ / Expected: 1df1781e-… / Actual: 7a291720-…`.
- Integration `R47_RecordsThePayment_MovesTheInvoiceToPaid_ReleasesTheCreditHold_AndEmitsPaymentReceivedThenCreditReleasedInThatOrder` — FAILED at `PaymentRegisterTests.cs:line 105`, which is `Assert.Equal(orderedFacts[0].EventId, orderedFacts[1].CausationId)` — **not** line 94's `Seq` assertion and not a count: `Expected: c1181e43-… / Actual: d82dda6d-…`.

**Probe B (mine, a different family and a different layer): corrupt the returned id at the domain** — `Invoice.cs:350` → `return newId();`, so `MarkPaid` hands the service a fresh, valid-looking id that is neither the request id nor the raised fact's `eventId`. This is the mutation a guard satisfiable by "any two distinct GUIDs" would survive — and under it the corroborating `Assert.NotEqual(command.RequestId, …)` in both tests *passes*.

- Unit guard — FAILED, same line 166: `Expected: 6a475462-… / Actual: ae208fc1-…`.
- Integration guard — FAILED, same line 105: `Expected: 3bd48fb1-… / Actual: 7911a306-…`.

Confirming green after restore + forced rebuild: `Billing.UnitTests` 226/226, `PaymentRegisterTests` 9/9.

## Probe 2 — provenance, not non-collision

Unit guard: `paymentReceived` is `Assert.Single(invoices.MarkPaidInvoice!.DomainEvents)`, where `MarkPaidInvoice` is the real `Invoice` aggregate the service handed the fake repository (`Fakes.cs` / the test's `RecordingInvoiceRepository`), and its `EventId` was assigned by the `UniqueId.New` delegate the *service* passes into `MarkPaid` — captured, not invented by the test. The load-bearing assertion is `Assert.Equal(paymentReceived.EventId, creditReleased.CausationId)`. No assertion in either guard is satisfiable by two arbitrary distinct GUIDs: the two `NotEqual`s are corroborating and Probe B shows the `Equal` is what bites. Integration guard reads `EventId`/`CausationId` off the live `outbox` rows, and `OutboxWriter.cs:56,60` / `OutboxEnvelopeMapper.cs:32,36` confirm those columns are exactly what the relay republishes as the envelope's `eventId`/`causationId` — so bullet three's "a consumer reading only the two envelopes" is what the test observes.

## Probe 3 — the `occurredAt` decision

Kept identical; edge added. Judged correct. Bullet two is a disjunction and the second arm is now literally true: `credit.released.v1.causationId == payment.received.v1.eventId` makes the order recoverable from the two envelopes with no clock and no `seq`. #7's `bf59af9` made the same choice (its Billing diff touches only `causationId`), and #7's projector-side tiebreak is *causal depth on an `occurredAt` tie* — i.e. identical timestamps plus an edge is precisely the shape the phase-12 projector will be ported to handle. Fabricating a delta inside one transaction would also break `BI13`'s single `clock.UtcNow` read that keeps `paidAt` and the ledger entry's date equal. The reasoning holds once the edge exists; it would not have held without it.

## Probe 4 — the sweep for other same-transaction fact pairs (advisory A1)

**The submitted enumerating command was a proxy.** `grep -rn "unitOfWork.ExecuteAsync(" src/*/Application/*.cs` does not recurse, so it cannot see `src/Orders/Application/Commands/PlaceOrderCommandHandler.cs:80` or `src/Orders/Application/Sagas/SagaFactHandler.cs` (whose transaction is opened by `src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs:52`), nor Notifications' `IdempotentConsumer.cs:52`. A second pair in Orders would have been invisible to it — exactly the "missed hit as a sentence, not an unclassified line" failure `CLAUDE.md` names.

My enumeration — `grep -rn "ExecuteAsync(\|BeginTransaction\|SaveChangesAsync" src/*/Application src/*/Infrastructure --include=*.cs` — adds three transactional units to the implementer's eight, classified:

- `PlaceOrderCommandHandler.cs:80` — `Order.Place` raises `OrderPlaced` only (`Order.cs:136`). One fact.
- `SagaFactHandler.cs` (via `IdempotentConsumer.cs:52`) — one saga step per transaction; every step calls at most one fact-raising transition. The only step calling two transitions is `credit.approved.v1` → `ApproveCredit` + `Confirm` (`SagaStepTable.cs:86–89`), and `ApproveCredit` is `buildEvent: null` (`Order.cs:163–164`) so exactly one fact (`order.confirmed.v1`) is raised. `Complete` and `Cancel` raise one each; `MarkStockReserved`/`MarkDespatched`/`MarkInvoiced`/`MarkPaid` are silent. One fact per transaction.
- Notifications `IdempotentConsumer.cs:52` — no outbox, no `Raise(` anywhere under `src/Notifications` (grep: zero hits). Zero facts.

The recursive `Raise(`/`RecordOrderFact(` enumeration over `src/*/Domain` returns the same nine call sites the implementer listed, so that half of the sweep was complete. **Conclusion unchanged: `PaymentRegisterService` was the only same-transaction sibling pair in the solution.** Non-blocking because the claim is true and the acceptance bullets do not include the sweep — but the command as submitted could not have found a counter-example in Orders, and this file is now the enumeration of record for phase 12.

## Probe 5 — `Invoice.MarkPaid` now returns a value

`grep -rn "MarkPaid(" src tests`: the only production caller is `PaymentRegisterService.cs:131` (now consumes the return). `tests/Billing.UnitTests/InvoiceTests.cs` calls it six times as a statement (lines 119, 140, 152, 159, 165, 167) — a discarded return value compiles unchanged in C#, and `git diff --stat` confirms `InvoiceTests.cs` is untouched. The `Orders.Order.MarkPaid(DateTimeOffset)` hits are a different type. Nothing loosened.

## Acceptance bullets → tests

| Bullet | Test | Verified how |
|---|---|---|
| 1 `causationId` = payment's `eventId` | `PaymentRegisterServiceTests::Backlog57_…` line 166; `PaymentRegisterTests::R47_RecordsThePayment_…` line 105 | Probes A and B, both guards |
| 2 distinct `occurredAt` **or** recoverable from envelope | same two tests (the edge) | Probe 3 reasoning; `occurredAt` deliberately unchanged |
| 3 consumer reads only the envelopes | `PaymentRegisterTests` line 105 off live outbox rows; mapper lines 32/36 | Read |
| 4 armed, on the envelope not row order | failing lines 166 / 105 are `CausationId`/`EventId` equalities, not `Seq` (line 94) or a count | Probes A and B |

Ported-idiom ledger: not applicable — `sdd: false`, no `design.md`; the ported shape (capture the fact, return its id, build the dependent context after the call) relies on no engine-, language- or library-supplied property in either stack.

## CHECKPOINTS walked

- C1: [x] harness files present [x] `progress/current.md`/`history.md` exist [x] five agent definitions [x] models declared [x] `./init.sh` exit 0
- C2: [x] no `in_progress` (id 57 `in_review` → `done` on this approval) [x] every status valid (`init.sh`) [x] done features carry passing tests — as reported by the implementer's `quality.sh` 1055/1055, not re-run here [x] `current.md` describes this session [x] no `blocked` features
- C3: [x] domain purity — `Architecture.Tests` 16/16 run [x] no cross-service DB access (diff is Billing-only, no new ports) [x] shared code unchanged (`git status`) [x] no `Domain/`→`OrderToCash.Cqrs` (arch suite) [x] SharedKernel untouched [x] no `decimal` in `src/Billing/Domain` (grep: zero hits) [x] Kafka-fact/NATS-RPC unchanged — the change is a field value on an existing fact [x] no debug logging/TODOs in the diff
- C4: [x] `quality.sh` — reported exit 0, format independently verified clean [x] domain tests pure (the new test is an Application-service test using the existing fakes; no framework) [x] integration on real Testcontainers (`PaymentRegisterTests` 9/9, MS-SQL/NATS/Kafka) [x] coverage — from the reported `quality.sh` run [x] no Jest (grep of every `package.json`: zero hits)
- C5: [x] no stray files (`git status`: 6 tracked modifications + the impl report) [x] history entry with effort record appended by this review [x] `feature_list.json` true (id 57 → `done`, single-line diff) [x] human told what was done / how to test — in this file's closing section [x] no commit
- C6: n/a — `sdd: false`; the six sdd features' triples pass `init.sh`'s coherence check.
- C7: [x] `specs/shared` byte-identical to #7 except `test-matrix.md` (real `diff -rq`) [x] no deviation introduced [x] no `R<n>` reused (strengthens R47's existing guard) [ ] n8n workflows — not exercised, no Gateway change [ ] black-box API script — not exercised, no Gateway change [x] effort record honest, including the comparison that is not a ratio (below) [x] README benchmark — unchanged this feature

## For the human — what was done and how to test it

Four files under `src/Billing` and `tests/Billing.*`: `Invoice.MarkPaid` returns the `payment.received.v1` `eventId`; `PaymentRegisterService` builds the credit-release context from it after the call, so `credit.released.v1.causationId` names the payment fact rather than the request id both used to share. Manual check: boot Orders + Billing, register a payment against an issued invoice, then `SELECT event_type, event_id, causation_id FROM outbox WHERE correlation_id = <order id> ORDER BY seq` in the Billing database — the second row's `causation_id` equals the first row's `event_id`.

## Bookkeeping after this approval — one step outstanding for the leader

`feature_list.json` id 57 is `done` (single-line diff, checked), and `progress/history.md` carries the entry with its effort record. `./init.sh` re-run after the flip reports 36/58 done and no `in_progress`, and **fails on exactly one check**: `progress/current.md` still names id 57 as the active feature — *"claims a feature while none is active"*. That file is the leader's session record; it was correct while the feature was open and needs its `**Feature:**` line moved on (to idle, or to `projector_read_model` id 24) before the session advances. Not edited here.
