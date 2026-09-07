# impl: `completion_pair_has_no_causal_edge` (backlog id 57)

`sdd: false` — specification of record is `feature_list.json` id 57's four acceptance bullets. Ported from #7's commit `bf59af9` (`fix(projector): order tied timeline entries by causal edge, not by luck`), Billing-only slice (the `apps/billing` diff in that commit, not the projector side, which is #8's phase 12).

## The change

Both files are in `src/Billing/`.

**`src/Billing/Domain/Invoice.cs`** — `Invoice.MarkPaid` changed signature from `void` to `UniqueId`. It now captures the `PaymentReceived` fact it builds into a local, raises it, and returns `fact.EventId` — the same "capture then return `event.eventId`" shape #7's `invoice.ts` used.

**`src/Billing/Application/PaymentRegisterService.cs`** — `RegisterAsync`'s transactional delegate now:
1. calls `var paymentEventId = invoice.MarkPaid(markPaidInput, ctx, UniqueId.New);` (previously discarded the return value, which didn't exist)
2. builds `creditCtx` **after** that call, as `new CreditContext(ctx.OccurredAt, paymentEventId)` — previously `new CreditContext(ctx.OccurredAt, command.RequestId)`, built *before* `MarkPaid` ran

So `credit.released.v1`'s `causationId` is now `payment.received.v1`'s own `eventId`, not the shared `command.RequestId`. `payment.received.v1` itself is unaffected — it still carries `ctx.CausationId = command.RequestId`, per R47/BI13; only the *sibling* relationship between the two facts changes, matching #7's fix exactly (`apps/billing/src/application/payment-register.handler.ts:167`, `credit.releaseHold(..., { ...ctx, causationId: paymentEventId }, ...)` in #7's diff).

## The `occurredAt` decision (acceptance bullet 2)

Bullet 2 allows either: a distinct `occurredAt` for the two facts, or "the ordering is otherwise recoverable from the envelope alone." **Kept `occurredAt` identical** — no change made to that field. Reasons:

- #7's own fix (`bf59af9`) did **not** touch `occurredAt` for this pair either — it changed only `causationId`, in both `payment-register.handler.ts` and `invoice.ts`. The projector-side "distinct-timestamp" idea (`statusRank` as a second sort key) was tried by #7 for a *different* problem (the compensation pair, `stock.released.v1`/`order.cancelled.v1`, which are cross-service and genuinely share no other edge) and was **rejected** — it "fixed the compensation pair and broke the completion triple deterministically," per #7's own commit message, precisely because a status-bearing fact can causally precede a status-less one under a status-rank tiebreak.
- With the causal edge now real (`credit.released.v1.causationId == payment.received.v1.eventId`), the ordering **is** "otherwise recoverable from the envelope alone" — a consumer walks the edge, not the clock. Forcing a distinct `occurredAt` would mean fabricating a time delta between two facts written inside the *same* database transaction, which is false precision the two facts' own atomicity contradicts.
- `clock.UtcNow` is read exactly once for this transaction (`BI13`'s own invariant, preserved) — the invoice's `paidAt` and the credit ledger's release-entry date must agree exactly. A distinct `occurredAt` for the two facts would break that identity for no correctness gain now that the edge exists.

This mirrors #7's actual shipped state: it never gave this pair a distinct `occurredAt` either, only the causal edge.

## The guard, and how it observes the payment's `eventId` (acceptance bullet 4)

Two guards, one per layer, both **reading the actual runtime-assigned `eventId`**, never merely asserting the two ids differ (`Assert.NotEqual` proves non-collision, not provenance — CLAUDE.md's own clause on this):

- **Unit** — `tests/Billing.UnitTests/PaymentRegisterServiceTests.cs::Backlog57_CreditReleasedCausationIdIsPaymentReceivedsOwnEventId_NotTheRequestIdBothFactsUsedToShare`. Captures the actual `PaymentReceived`/`CreditReleased` domain events raised through fakes, reads `paymentReceived.EventId` (the value the domain assigned, not one the test invented) and asserts `Assert.Equal(paymentReceived.EventId, creditReleased.CausationId)`. Corroborating (not load-bearing alone): `Assert.NotEqual(command.RequestId, creditReleased.CausationId)` and `Assert.Equal(command.RequestId, paymentReceived.CausationId)` (payment's own causation is untouched).
- **Integration** — `tests/Billing.IntegrationTests/PaymentRegisterTests.cs::R47_RecordsThePayment_MovesTheInvoiceToPaid_ReleasesTheCreditHold_AndEmitsPaymentReceivedThenCreditReleasedInThatOrder` (extended, not a new test — it already owned the outbox-row assertions this bullet needs). Added, reading the **real outbox rows** over the live NATS→responder→MS-SQL path:
  ```csharp
  Assert.Equal(orderedFacts[0].EventId, orderedFacts[1].CausationId);
  Assert.NotEqual(requestId.Value, orderedFacts[1].CausationId);
  Assert.Equal(requestId.Value, orderedFacts[0].CausationId);
  ```
  `requestId` is captured into a local (`var requestId = UniqueId.New();`) before `BuildHeaders(correlationId, requestId)`, rather than generated inline and discarded, specifically so the corroborating assertion has a real value to compare against — the test previously generated the request id inline and never kept it.
- This is deliberately **on the envelope** (`OutboxMessage.CausationId`/`EventId` columns, which are exactly what the Kafka relay republishes byte-for-byte as the wire envelope's `causationId`/`eventId`), never on `Seq`/row order — bullet 4's own distinction. The pre-existing `orderedFacts[0].Seq < orderedFacts[1].Seq` assertion a few lines above is untouched and is **not** what proves this bullet.

### Arming — both mutation families, both guards

Backup taken before mutating: `cp src/Billing/Application/PaymentRegisterService.cs` to the scratchpad, restored with `cp` (never `git checkout --`, and the file *is* tracked, so the CLAUDE.md caution about untracked files restoring nothing doesn't even apply here — `cp` was still used, per the protocol's own preference). Every restore was force-rebuilt with `dotnet build --no-incremental` before the confirming green run, and the restore was verified two ways: `cmp` against the backup (bytewise identical) **and** re-reading the changed line.

**Mutation 1 — revert the causal stamp to the request id** (deletes the fix, reproducing the pre-fix defect):
```csharp
var creditCtx = new CreditContext(ctx.OccurredAt, command.RequestId);  // was: paymentEventId
```
- Unit guard (`Backlog57_...`) — **FAILED**:
  ```
  Assert.Equal() Failure: Values differ
  Expected: 0afd8f2d-ae0d-4860-98eb-739c492e3838
  Actual:   ff18672f-db2c-4c2c-9577-800d7b8b93cb
  ```
- Integration guard (`R47_RecordsThePayment_...`) — **FAILED**:
  ```
  Assert.Equal() Failure: Values differ
  Expected: 81936722-7d65-46d8-aa5a-73e5616b59d6
  Actual:   1803f1a9-efa6-423d-8947-d054c7618c97
  ```
- Restored via `cp` from backup, forced rebuild (`dotnet build --no-incremental src/Billing`), re-read line 132 — confirmed `paymentEventId` again. Confirming green run: unit test 11/11 pass, integration test 9/9 pass (both `PaymentRegisterServiceTests`/`PaymentRegisterTests` files in full, not only the one method).

**Mutation 2 — corrupt to a different valid-looking id** (a fresh, unrelated `UniqueId.New()` — neither the request id nor the payment's own eventId — the "corruption" mutation family CLAUDE.md distinguishes from deletion, and the one that actually tests provenance rather than "not the old value"):
```csharp
var creditCtx = new CreditContext(ctx.OccurredAt, UniqueId.New());  // was: paymentEventId
```
- Unit guard — **FAILED**:
  ```
  Assert.Equal() Failure: Values differ
  Expected: 9496599f-2b24-4c44-a877-a9be4b44cc20
  Actual:   7c321cb9-e630-4bfc-9cd5-7230b206cb06
  ```
- Integration guard — **FAILED**:
  ```
  Assert.Equal() Failure: Values differ
  Expected: 02edbfc0-da70-4b72-8636-7805eb49833c
  Actual:   9c509fa5-001b-480b-8f15-6406d95d6c46
  ```
- Restored via `cp` from backup, forced rebuild, re-read line 132 — confirmed `paymentEventId` again (also `cmp`'d byte-identical to the backup, as with mutation 1). Confirming green run after this second restore: `dotnet build --no-incremental src/Billing` succeeded, `Billing.UnitTests::PaymentRegisterServiceTests` 11/11 pass, `Billing.IntegrationTests::PaymentRegisterTests` 9/9 pass.

Both mutation families, on both guards, at the envelope (not row-order) — satisfies bullet 4 exactly.

## The sweep for other tie-group pairs (bullet — "if the sweep finds a second pair… stop and report")

**Claim under test:** no *other* transactional unit in the solution raises two-or-more domain facts inside one transaction while both share one `causationId`/context — the shape that made `payment.received.v1`/`credit.released.v1` a tie group.

Command 1 — enumerate every transactional unit (candidate sites):
```
grep -rn "unitOfWork.ExecuteAsync(" src/*/Application/*.cs
```
Output (8 hits):
```
src/Billing/Application/CreditReleaseService.cs:21:        unitOfWork.ExecuteAsync(
src/Billing/Application/CreditHoldService.cs:25:        unitOfWork.ExecuteAsync(
src/Billing/Application/PaymentRegisterService.cs:79:        return await unitOfWork.ExecuteAsync(
src/Billing/Application/InvoiceIssueService.cs:43:        return await unitOfWork.ExecuteAsync(
src/Fulfillment/Application/StockReplenishService.cs:15:        unitOfWork.ExecuteAsync(
src/Fulfillment/Application/StockReservationService.cs:26:        unitOfWork.ExecuteAsync(
src/Fulfillment/Application/StockReservationService.cs:90:        return await unitOfWork.ExecuteAsync(
src/Fulfillment/Application/DespatchCreationService.cs:52:        return await unitOfWork.ExecuteAsync(
```

Command 2 — enumerate every fact-emission call site (every `Raise(...)`/`RecordOrderFact(...)` in every `Domain/` folder, the complete candidate set of possible tie-group members):
```
grep -rn "\.Raise(\|^\s*Raise(\|RecordOrderFact(" src/*/Domain/*.cs
```
Output (13 hits, one is the `RecordOrderFact` method's own signature, not a call site):
```
src/Fulfillment/Domain/OrderStockReservation.cs:172:            carrier.RecordOrderFact(rejectedFact);
src/Fulfillment/Domain/OrderStockReservation.cs:195:        carrier.RecordOrderFact(reservedFact);
src/Fulfillment/Domain/OrderStockReservation.cs:245:        carrier.RecordOrderFact(fact);
src/Billing/Domain/BuyerCredit.cs:149:        Raise(new CreditApproved(
src/Billing/Domain/BuyerCredit.cs:187:        Raise(new CreditRejected(
src/Billing/Domain/BuyerCredit.cs:234:        Raise(new CreditReleased(
src/Billing/Domain/Invoice.cs:179:        invoice.Raise(new InvoiceIssued(
src/Billing/Domain/Invoice.cs:348:        Raise(fact);
src/Fulfillment/Domain/DespatchAdvice.cs:98:        advice.Raise(fact);
src/Orders/Domain/Order.cs:136:        order.Raise(new OrderPlaced(
src/Orders/Domain/Order.cs:406:            Raise(buildEvent());
src/Fulfillment/Domain/StockItem.cs:174:    public void RecordOrderFact(FactEvent fact)   [method signature, not a call site]
src/Fulfillment/Domain/StockItem.cs:181:        Raise(fact);                                [the actual emission StockItem.RecordOrderFact wraps]
```

Classification, one line per candidate transactional unit, read against its own source to see which and how many of the above it can trigger in a single run:

| Transactional unit | Facts it can raise in ONE run | Sibling pair (same tx, shared causationId)? |
|---|---|---|
| `CreditReleaseService` | `CreditReleased` only (`BuyerCredit.cs:234`) | No — single fact |
| `CreditHoldService` | `CreditApproved` **or** `CreditRejected`, mutually exclusive (each `switch` branch `return`s immediately — read the full file, every branch returns before falling through) | No — exactly one, branches never co-execute |
| `PaymentRegisterService` | `PaymentReceived` (`Invoice.cs:348`) **then** `CreditReleased` (`BuyerCredit.cs:234`), both inside the SAME `ExecuteAsync` delegate | **Yes — this is the id 57 pair, now fixed by this change** |
| `InvoiceIssueService` | `InvoiceIssued` (`Invoice.cs:179`) only — the same transaction also calls `BuyerCredit.Consume`, whose own doc comment states it "Emits NOTHING — `invoice.issued.v1` is feature 21's `Invoice` fact"; confirmed no `Raise`/`RecordOrderFact` call anywhere in `Consume`'s body | No — one fact; `Consume` is silent by design (R40) |
| `StockReplenishService` | none — writes no outbox row at all ("a top-up is an operational act, not a saga-visible fact", R61, confirmed in the file's own doc comment and absence of any repository call that would insert an outbox row) | No — zero facts |
| `StockReservationService` (`Reserve`, line 26) | `StockReserved` **or** `StockRejected`, mutually exclusive (`OrderStockReservation.Reserve` returns immediately after either `RecordOrderFact` call; read the full method, lines 109–197) | No — exactly one |
| `StockReservationService` (`Release`, line 90) | `StockReleased`, or none if `allReleased.Count == 0` | No — at most one |
| `DespatchCreationService` | `DespatchAdvice`'s own fact (`DespatchAdvice.cs:98`), or `NoReservations` (no fact) | No — at most one |

**Result: `PaymentRegisterService` was the only transactional unit in the solution whose single run could raise two facts sharing one causation context — and it is the pair this feature fixes.** No second pair exists to fix in this same pass; nothing outside `src/Billing/` was touched, consistent with the "if the sweep finds a second pair outside Billing, stop and report" instruction (it didn't).

## Traceability

| Acceptance bullet | Test |
|---|---|
| 1 — `credit.released.v1`'s `causationId` is `payment.received.v1`'s `eventId`, not the shared request id | `PaymentRegisterServiceTests::Backlog57_CreditReleasedCausationIdIsPaymentReceivedsOwnEventId_NotTheRequestIdBothFactsUsedToShare` (unit); `PaymentRegisterTests::R47_RecordsThePayment_...` (integration, live outbox rows) |
| 2 — no identical `occurredAt`, or ordering otherwise recoverable | Decided: `occurredAt` stays identical; ordering is recoverable via the causal edge bullet 1's tests prove. No new `occurredAt` assertion needed — the pre-existing `R47_...` test already asserts the pair's `occurredAt`/`Seq` ordering is unaffected by this change (unchanged assertions still pass) |
| 3 — a consumer reading only the two envelopes can determine which caused which | Same two tests — `causationId` on the envelope (`OutboxMessage.CausationId`, which is what the relay republishes) is what a consumer reads; no reliance on `Seq`/arrival order in either new assertion |
| 4 — armed, on the envelope not row order | Both mutation families (revert-to-request-id, corrupt-to-unrelated-id), both guards, verbatim failures recorded above; `Seq`-based assertions are pre-existing and untouched, the new assertions read `CausationId`/`EventId` only |

`specs/shared/test-matrix.md` — not updated: this feature is `sdd: false` (no `R<n>` of its own; it strengthens the existing R47 guard rather than adding a new requirement row).

## Test counts (read off the real runs, this session)

- `dotnet test tests/Billing.UnitTests` (full project, post-fix, post-restore): **226/226 passed**.
- `dotnet test tests/Billing.IntegrationTests` (full project, post-fix, post-restore, real Testcontainers MS-SQL/NATS/Kafka): **83/83 passed**.
- `./quality.sh` (full solution, format + build + every test project + coverage gate): **exit 0**. Per-project counts read off that run: SharedKernel.UnitTests 50, Cqrs.UnitTests 23, Contracts.UnitTests 21, Notifications.UnitTests 58, Fulfillment.UnitTests 119, Billing.UnitTests 226, Orders.UnitTests 280, Seed.UnitTests 34, Seed.IntegrationTests 6, Architecture.Tests 16, Notifications.IntegrationTests 12, Fulfillment.IntegrationTests 56, Billing.IntegrationTests 83, Orders.IntegrationTests 71 — **sum 1055/1055 passed, 0 failed** (the reviewer's last recorded count was 1054; this feature adds exactly one new test method, `Backlog57_...`, and extends one existing method rather than adding a second).
- `./init.sh`: exits 0 — "SDD coherence", "backlog tripwire: no feature lost, no done reverted", "commit-msg hook installed and matches the tracked copy" all OK; 1 feature `in_progress` reported at the time it ran (before I flipped id 57 to `in_review` — expected, `init.sh` was run mid-session).

## Files touched

- `src/Billing/Domain/Invoice.cs` — `MarkPaid` returns `UniqueId` (the fact's `eventId`), was `void`.
- `src/Billing/Application/PaymentRegisterService.cs` — `creditCtx`'s `CausationId` is now `paymentEventId`, built after `MarkPaid` returns, was `command.RequestId` built before.
- `tests/Billing.UnitTests/PaymentRegisterServiceTests.cs` — one new test, `Backlog57_CreditReleasedCausationIdIsPaymentReceivedsOwnEventId_NotTheRequestIdBothFactsUsedToShare`.
- `tests/Billing.IntegrationTests/PaymentRegisterTests.cs` — `requestId` captured into a local (was generated inline and discarded); three new assertions added to the existing `R47_RecordsThePayment_...` test, reading `CausationId`/`EventId` off the real outbox rows.
- `feature_list.json` — id 57's `status` line only, `in_progress` → `in_review` (verified via `git diff` showing exactly that one line).
- `progress/impl_completion_pair_has_no_causal_edge.md` — this file.

## What I could not do / did not do

- Did not update `specs/shared/test-matrix.md` — this feature carries no `R<n>` of its own (`sdd: false`, and #7's own equivalent amendment A1 open point 2 for this exact Billing edge was likewise not given a fresh `R<n>`; it strengthens R47's existing guard).
- Did not touch anything under `src/Orders/`, `src/Fulfillment/`, `src/Gateway/`, `src/Notifications/`, `src/Projector/`, or `apps/web/` — the sweep found no second tie-group pair anywhere in the solution, so there was nothing to fix outside `src/Billing/`.
- Did not touch the projector (it doesn't exist yet — phase 12, per the task framing) or `specs/shared/asyncapi.yaml`'s envelope shape (`causationId` was already a first-class envelope field before this change; only which *value* Billing puts in it, for this one fact, changed).

## Nothing surprising

The port from `bf59af9` was close to mechanical once the #7 diff was read — the shapes (`Invoice.markPaid` returning the new fact's id, the handler building the dependent context after the call rather than before) translate directly into C#, and #7's own commit message had already done the harder work of explaining *why* `occurredAt` was deliberately left alone and why a `statusRank`-style tiebreak was rejected. The one piece of local judgement was the sweep — confirming, by reading every transactional unit's actual branches rather than assuming, that no *other* service in #8 has grown a second same-transaction sibling pair since #7 shipped its own version of this fix.
