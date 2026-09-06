# `billing_remittance_intake` (feature 22, phase 10, `sdd: false`) — adversarial review

**Verdict: APPROVED.** 0 blocking defects. **3 findings of substance** (N1 medium-high, N2 medium, N3 informational-parity) and 3 advisories. Nine of my own mutations run, **eight killed by a named test, one survived** — that survivor is N2, and it is the only place in this feature where a wire field can be corrupted with the whole suite green.

Per the scope rule, `./quality.sh` was **not** re-run wholesale; the implementer ran it minutes before this review and reported it green. What I ran instead is listed in full below, including one suite I did re-run end to end because the claim under test was about that suite's count.

This closes Phase 10 and closes the order-to-cash cycle end to end for the first time in this repository. The phase's closing assessment is appended to `progress/history.md`, not repeated here.

---

## 1. What I ran, in full

| Command | Purpose | Result |
|---|---|---|
| `dotnet build OrderToCash.sln --no-incremental` (×5, once per probe cycle) | probe hygiene — the arming protocol's forced rebuild | **0 warnings, 0 errors** each time |
| `dotnet test tests/Billing.UnitTests --no-build` | the 225-test claim | **225/225 passed** |
| `dotnet test tests/Billing.IntegrationTests --no-build` (whole project, real MS-SQL/NATS/Kafka) | the "83/83, up from 74" claim **is** a claim about a whole suite, so it was re-run in full | **83/83 passed, 4 m 25 s** |
| `dotnet test tests/Billing.IntegrationTests --filter PaymentRegisterTests` (×5, under mutations) | probes 1–4 | see §3 |
| `dotnet test tests/Architecture.Tests --no-build` | C3 domain purity, run not eyeballed | **16/16 passed** |
| `./init.sh` | C1/C2 | **exit 0**, 55 features, backlog tripwire clean |
| `diff -rq specs/shared/ <#7 checkout>/specs/shared/` | C7 spec-reuse fidelity | only `test-matrix.md` differs — the Status column, as C7 permits |
| `git diff specs/shared/test-matrix.md` | scope of the matrix edit | only the R47/R48/R49 Status cells and the two derived count cells |
| 9 read-only `sqlcmd` batches against `otcnet-mssql` (`otc_billing`, `otc_orders`) | the live walkthrough, both halves, from the databases | see §5 |
| Repo-wide timer enumeration across `*.cs`, `*.json`, `*.yml/yaml`, `*.ts/tsx`, `*.sql`, `*.sh`, `*.props` + `AddHostedService` + `*Options` class sweeps | the "no internal payment timer anywhere" claim, candidate set judged rather than output | see §4 |
| `python3` YAML parse of `specs/shared/asyncapi.yaml` `PaymentRegister*Payload` | wire transcription | field-for-field identical, `required` list identical |

Every source mutation was restored from a scratchpad backup, `cmp`-verified byte-identical, `touch`ed, rebuilt `--no-incremental`, and re-run green. `src/Billing/Application/PaymentRegisterService.cs` carries a review-time mtime of **12:36** for that reason; its md5 is `5eab17898014e01cb81bd9c0d2fe2f34`, identical to the submitted version. No `git checkout --` was run on anything, at any point.

---

## 2. Traceability — `R<n>` → named test, verified non-vacuous

| Req | Test(s) I verified exist, are named as claimed, and are non-vacuous | Status |
|---|---|---|
| **R47** — unseen reference → payment recorded, invoice `paid`, `payment.received.v1` **then** `credit.released.v1`, same transaction | Unit `tests/Billing.UnitTests/PaymentRegisterServiceTests.cs:72` › `R47_LocksTheCreditLineBeforeTheInvoiceRow_CallsMarkPaidAndRelease_PersistsTheInvoiceBeforeTheCreditLine_AndRepliesAccepted` — one shared `callLog` both hand-built repository fakes append to (`:74`, `:84`, `:89`), so the assertion at `:114` is a **call-order** assertion across two aggregates, not a row-existence one; **re-armed by me, dies on the swap**. Integration `tests/Billing.IntegrationTests/PaymentRegisterTests.cs:25` › `R47_RecordsThePayment_MovesTheInvoiceToPaid_ReleasesTheCreditHold_AndEmitsPaymentReceivedThenCreditReleasedInThatOrder` — asserts real MS-SQL `seq` order (`:90–93`), both payload bodies field by field (`:106–120`), the ledger `release` entry (`:75–78`) and the `availableCredit` identity (`:81`); **same swap kills it too** | **PASS** |
| **R48** — repeated reference → original outcome, no second payment, no second fact | Unit `:152` fast path (`unitOfWork.ExecuteCount == 0`), `:177` authority re-read under the invoice lock (`MarkPaidCallCount == 0`, `SaveChangesCallCount == 0` — *entry* counters, not post-rollback residue). Integration `:126` sequential (**asserts the ORIGINAL `paidAt` at `:152`** — armed by me, probe 2), `:164` concurrent `Task.WhenAll` (`outcomes == ["accepted","duplicate"]`, `Assert.Single(paymentRows)`, and a whole-table fact count of exactly 2), `:203` N11 cross-invoice sequential, `:249` the UNIQUE-constraint backstop under **two different credit lines** so `BI8`'s lock cannot serialise the race | **PASS** |
| **R49** — amount / currency / different-reference-against-`paid` → machine-readable refusal, invoice and ledger unchanged, no fact | Unit `:205`, `:226`, `:247` — all three assert `MarkPaidCallCount == 0` **and** `SaveChangesCallCount == 0` (nothing *attempted*, the stronger form). Integration `:310` (amount: invoice unchanged, `payments` empty, ledger row count unchanged, **whole-table** outbox count unchanged) and `:348` (currency). Wire mapping for all three in `BillingErrorMapper.cs:89/95/101`, each with its own unit test (`BillingErrorMapperTests.cs:114/124/134`) | **PASS** (see advisory A1) |

`specs/shared/test-matrix.md`: the R48/R49 rows honestly record that coverage sits one layer below the sketch's `API` level because the Gateway endpoint (features 25/29) does not exist — the same substitution #7's counterpart made and its reviewer accepted. The two derived count cells (`billing_invoicing 2→5`, `Total 42→45 / 17→14`) I recomputed from the Status column myself: correct.

---

## 3. My own mutation probes — two families, nine mutations

| # | Family | Mutation | Named test that failed | Verbatim |
|---|---|---|---|---|
| **P1a** | ordering | `credits.SaveChangesAsync` moved above `invoices.MarkPaidAsync` | `R47_LocksTheCreditLineBeforeTheInvoiceRow_…` (unit) | `R47: invoices.MarkPaid must run (and its outbox row insert) BEFORE credits.SaveChanges.` |
| **P1b** | ordering | the same swap, at the integration level | `R47_RecordsThePayment_…AndEmitsPaymentReceivedThenCreditReleasedInThatOrder` | `Assert.Equal() Failure: Strings differ / Expected: "payment.received.v1" / Actual: "credit.released.v1"` |
| **P2** | payload | duplicate reply's `paidAt` returned as `PaidAtOrNull?.AddSeconds(1)` | `R48_ASequentialRepeatOfTheSamePaymentReferenceAnswersDuplicate_…` (integration; the unit suite stayed 225/225) | `Expected: 2026-09-06T10:31:34.107+00:00 / Actual: 2026-09-06T10:31:35.107+00:00` |
| **P3** | payload | `CreditReleaseReason.InvoicePaid` → `OrderCancelled` — the **second** fact's `reason`, i.e. feature 17's exact defect shape | `R47_RecordsThePayment_…` (1 failed / 8 passed — correct discrimination) | `Expected: "invoice_paid" / Actual: "order_cancelled"` |
| **P4** | payload | `command.ValueDate` → `command.ValueDate.AddDays(42)` | **NONE — 225/225 unit and 9/9 integration stayed green** | *(see N2)* |

P1a/P1b answer the brief's first question directly: the ordering property is structural (call order → one awaited raw-SQL `INSERT` at a time → `outbox.seq` `bigint IDENTITY`), and **a test asserting merely that both facts exist would pass with the order reversed** — this one does not, at either level. I verified the chain link by link rather than inferring it: `EfCoreInvoiceRepository.MarkPaidAsync:250` drains `payment.received.v1` through `InsertOutboxRowAsync` (a single awaited `ExecuteSqlInterpolatedAsync`, never `AddRange`) *inside* `MarkPaidAsync`, and `credits.SaveChangesAsync` does the same for `credit.released.v1` afterwards.

P2 answers the brief's "repeat registration returns the **original** `paidAt`" point: that claim is guarded, and only at the integration level.

The idempotency **absence** half (brief item 2) holds on three independent mechanisms, and I checked each is armed rather than assumed: the fast path (implementer probe C), the authority re-read under the invoice lock (probe D — which fails by *throwing* `InvoiceAlreadyPaidError`, i.e. the aggregate's `B8` is a genuine second line of defence), and the `payments.payment_reference` UNIQUE constraint (probe E, and it is the *only* arbiter in the two-credit-line test at `:249`, which is why that test's fixture matters). Non-vacuity of the counting helpers is established across tests rather than assumed: `PaymentsOfAsync` returns `Single` at `:66`, `Empty` at `:243` for a second invoice in the same run; `WholeTableOutboxCountAsync` is an unfiltered `CountAsync()` proven to move by `:86`'s `outboxCountBefore + 2`.

**Concurrency, checked at the engine rather than from the passing test.** All four `otc_*` databases have `is_read_committed_snapshot_on = 1`. The authority re-read (`FindPaymentByInvoiceIdAsync`) is a plain `AsNoTracking` `SELECT`, so under RCSI it takes a statement-level snapshot at statement start — which is **after** `LockForOrderAsync`'s `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` on `dbo.credits` has been granted, i.e. after the other contender committed and released. Same-invoice contenders share a credit line by construction, so they are serialised before either can reach `payments`. The read-then-write check is therefore performed under a lock that provably serialises the contenders, and the UNIQUE constraint is a true backstop on that path, never the primary mechanism. This is #8's counterpart of #7's REPEATABLE-READ analysis, and it lands in the same place.

---

## 4. The timer-absence claim — I judged the candidate set, not the output

The implementer's enumeration is `.cs`-only under `src/`. Mine is wider on purpose, because the claim is *"anywhere"*:

```
grep -rniE "timer|cron|schedule|Task\.Delay|IHostedService|BackgroundService|Quartz|Hangfire|setInterval|setTimeout|Recurring|Elapsed|Timeout\.Infinite" \
  --include=*.cs --include=*.json --include=*.yml --include=*.yaml --include=*.props --include=*.ts --include=*.tsx --include=*.sql --include=*.sh . \
  | grep -viE "(^|/)(bin|obj|node_modules|TestResults)/"
```

61 files matched. Everything the implementer classified is in it, plus four classes it could not reach and which I checked myself:

- **`n8n/workflows/*.json` (4 files).** `2-payment-robot.json` carries `n8n-nodes-base.scheduleTrigger` on `PAYMENT_ROBOT_INTERVAL_SECONDS` (default 120 s) and POSTs to the Gateway's `/invoices/{id}/payments`. `specs/shared/n8n-workflows.md:189` defines it as *"the **outside world** that moves invoices from `issued` to `paid`"*. This is the external caller the design requires, in a container that is not running, against an endpoint that does not exist yet — not an internal timer under any reading. The other three workflows (order generator, stock replenishment, burst) contain `setTimeout` only inside their own JS `sleep` helpers.
- **`specs/shared/openapi.yaml` / `asyncapi.yaml`** — `issuedBeforeMinutes` and schedule prose describing that same external robot. No producer-side timer.
- **Every `AddHostedService` in `src/` (10 registrations).** Billing has exactly **two**: `OutboxRelayBackgroundService` (the pre-existing publish loop from feature 14 — publishes facts already written; it can neither originate nor schedule a payment) and `BillingRpcResponder` (request-driven). Nothing was added here.
- **Every `*Options` class in `src/Billing/` (6).** `BillingOptions`, `BillingResponderOptions`, `NatsOptions`, `KafkaOptions`, `OutboxRelayOptions`, `CreditSimulatorOptionsLoader` — **no interval, age, sweep or retry knob for payments exists to be configured.** There is also no `appsettings*.json` in `src/Billing` at all.

`Quartz` / `Hangfire` / `System.Timers` / `Recurring`: zero hits anywhere. **The acceptance bullet holds, and the only trigger for `issued → paid` is an inbound `billing.payment.register` request.**

---

## 5. The live walkthrough, re-verified from the databases (both halves)

Queried by me, not read off the report:

```
otc_billing.invoices     INV-000006 | ORD-000007 | paid   | 49998 EUR | paid_at 2026-09-06 10:07:07.056
otc_billing.payments     PAY-LIVE-1788689225553 -> INV-000006 | 49998 | EUR | source=test | value_date 2026-09-06 10:07:05.000
otc_billing.credit_items ORD-000007: hold 49998 (09-05 17:47) | consume 49998 (09-06 07:12) | release 49998 (09-06 10:07:07.300)
otc_billing.outbox       seq 10015 payment.received.v1  eventId 53E52910…  causationId E95C9176…  published 10:07:07.599
                         seq 10016 credit.released.v1   eventId F2746BC0…  causationId E95C9176…  published 10:07:07.599
otc_orders.orders        ORD-000007 | completed | 2026-09-06 10:07:07.057
```

Recomputed independently: `CR-000001` limit 500 000, `Σhold − Σrelease` after = 49 998, available = 450 002 — exactly `+49 998` against the reported 400 004 before, and the release equals the hold to the cent. **The order crossed `invoiced → paid → completed` unattended with zero changes to `src/Orders/`** (confirmed by `git status`: no Orders file is modified or untracked in this feature).

**The negative half, also from the databases:** `INV-000007` is still `status='issued'`, `paid_at IS NULL`, **zero** rows in `payments` for it, and `ORD-000008` is still `invoiced` — the one-cent-short refusal moved nothing. The `value_date` on the live payment row equals the request's `valueDate` to the millisecond, so the field is *correct* in production; what N2 says is that no test would have noticed if it were not.

**#7's N13 did not recur.** #7 stranded `ORD-000007` permanently by using a random UUID as the correlation id; #8 used the order's own id (`8B0670D1-…`, and the outbox rows above confirm it), so both facts routed and the order completed. The lesson was inherited, not rediscovered. The pre-existing hand-corrupted fixture `ORD-000011` (feature 19's `I4`) is untouched and still at `despatched`, exactly as the brief required.

---

## 6. Brief item 3 — the two methods that were uncalled until now

**`Invoice.MarkPaid` (feature 21).** Its three refusals are `B8` (already `paid`), `B10` amount, `B10` currency; all three fire from inside the aggregate before either repository is touched, and all three are now exercised through a real caller: amount and currency end to end over NATS (`:310`, `:348`) and live (the 5546-vs-5547 refusal), already-paid at the service level (unit `:247`, armed by implementer probe A) with its wire mapping proven separately (`BillingErrorMapperTests.cs:114`). **`B9` — `paidAt` set exactly when the status becomes `paid`** — is now proven through the live path in three places at once (the reply, the `invoices.paid_at` column, and the fact payload), which its own feature could not do. The one assumption the real caller *does* introduce is new and is guarded: `MarkPaidAsync` refuses an invoice this repository instance did not load through `LockByIdAsync` (`EfCoreInvoiceRepository.cs:236`), which is the `_currentLockedRow` identity-map discipline `EfCoreBuyerCreditRepository` already established. No guard written in feature 21 encodes an assumption this caller breaks.

**`BuyerCredit.Release` (feature 19).** Live-verified: exactly one `release` entry equal to the hold, `availableCredit` back to its pre-hold value, `credit.released.v1` carrying `reason: invoice_paid` (armed by my P3). Its `B5` underflow guard remains unreachable by construction (the release amount is *derived* from the ledger, never from the payment amount — the payment amount is not an input to `Release` at all, so a full release drives that order's exposure to exactly 0 for any order, always). **Its null-return branch is the one thing the new caller treats differently from the old one — see N3.**

---

## 7. Findings

### N1 — non-blocking, medium-high — **amendment A1's producing half is missing: the two facts are siblings, not a chain, and they tie on `occurredAt`**

**File:** `src/Billing/Application/PaymentRegisterService.cs:108–109`.

```csharp
var ctx = new InvoiceContext(clock.UtcNow, command.RequestId);
var creditCtx = new CreditContext(ctx.OccurredAt, command.RequestId);   // <- causationId = the request id, for BOTH facts
```

Live evidence, from the outbox rows above: `payment.received.v1` and `credit.released.v1` carry **the same `causationId` (`E95C9176…`)** and **the same `occurredAt` (`10:07:07.057`)**. They are therefore a tie group under the rule the inherited shared spec states at `specs/shared/openapi.yaml:1474–1483`: within a tie on `occurredAt`, entries are ordered by the recorded causal edge (`c.causationId == p.eventId`), and *"where no such recorded edge exists … they fall back to `eventId` ascending — a deterministic but causally arbitrary order (amendment A1)"*. With no edge recorded, the completion pair will render in a coin-flip order.

`specs/shared/test-matrix.md:122` (column 2, the shared contract, byte-identical to #7's) requires for **R24** that *"the completion triple (`payment.received.v1`, `credit.released.v1`, `order.completed.v1`)"* be *"visible in the timeline in **causal order** (amendment A1, `projector_read_model` PR10/PR30–PR33)"*.

**#7 faced this and fixed it — in this exact file, and the fix is on disk.** `apps/billing/src/application/payment-register.handler.ts:174–202`: `markPaid` **returns** `paymentEventId`, and `releaseHold` is called with `{ ...ctx, causationId: paymentEventId }`, under a comment that names the amendment and its reason (*"Before this change both facts carried the SAME causationId (`cmd.requestId`) and were therefore siblings, not a chain — the projector's causal-edge timeline rule (PR10) could not order them"*). #7 paid for that as **rework in a later phase** (commit `bf59af9 fix(projector): order tied timeline entries by causal edge, not by luck`), long after its own feature 22 shipped. #8's `Invoice.MarkPaid` returns `void` (`src/Billing/Domain/Invoice.cs:303`), so the seam #7's fix needed does not exist here yet.

**Why it is not blocking.** The specification of record for this feature is R47–R49 plus id 22's three acceptance bullets, and none of them mentions causation; R47 asks only for the two facts *in that order in the same transaction*, which is satisfied and armed. #7's own feature 22 shipped without it and its reviewer approved. This is a requirement owned by R24 and by the unbuilt projector, not by feature 22.

**Why it should not wait.** #8 inherited the *amended* spec, so the decision is already made and the evidence is free — this is precisely the *"did #7 face this, and what did it do?"* test, and the answer is on disk. The change lands in **Billing** (`Invoice.MarkPaid` returning its event id, one line in `PaymentRegisterService`, one guard test asserting `credit.released.v1.causationId == payment.received.v1.eventId`), so deferring it to Phase 12 means reopening a closed Billing feature from inside the projector's pass — which is the shape of rework this repository exists to avoid. **Owner: leader — a backlog entry before the first Phase 11 implementer is dispatched.** I have not filed it myself; backlog entries are the leader's, and only one agent may write `feature_list.json`.

### N2 — non-blocking, medium — **`valueDate` can be corrupted by 42 days with the entire Billing suite green**

**Files:** `src/Billing/Application/PaymentRegisterService.cs:114`; `tests/Billing.IntegrationTests/PaymentRegisterTests.cs:104–112`.

Enumerating command and complete output (probe P4):

```
mutation: command.ValueDate  ->  command.ValueDate.AddDays(42)
dotnet build OrderToCash.sln --no-incremental        =>  0 Warning(s), 0 Error(s)
dotnet test tests/Billing.IntegrationTests --filter PaymentRegisterTests
    Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 35 s
dotnet test tests/Billing.UnitTests
    Passed!  - Failed: 0, Passed: 225, Skipped: 0, Total: 225, Duration: 1 s
```

`valueDate` is a **required** field of `PaymentReceivedPayload` in `specs/shared/asyncapi.yaml`, it is `domain-model.md:472`'s fact-11 payload, and it is the `payments.value_date` column. The R47 integration test opens the published payload and asserts six of its seven fields (`:107–112`) under a comment that reads *"The published payload's fields equal the REQUEST's, field by field — not merely a row count (feature 17's own lesson)"* — **that comment is not true of `valueDate`**, and neither the reply, nor the `payments` row assertion at `:66–70`, nor any unit test covers this feature's request → command → `MarkPaidInput` hop for it.

To be precise about the size of the hole: `valueDate` **is** guarded at the raise site (`InvoiceTests.cs:132`) and at the domain-event → wire-payload mapper (`BillingFactPayloadMapperTests.cs:45`), both from feature 21. What is unguarded is exactly the seam feature 22 added. A one-line assertion in the R47 integration test — `Assert.Equal(valueDate, paymentReceivedPayload.ValueDate)` — closes it, plus one on `paymentRow.ValueDate`. **Owner: implementer**, as a follow-up, not a re-review condition. It is recorded here because a false completeness claim in a test comment is how this class survives: the next reader has no reason to re-check a line that says *field by field*.

### N3 — informational, parity with #7 — **a payment against an order with no outstanding exposure emits no `credit.released.v1`, and the order can then never complete**

**File:** `src/Billing/Application/PaymentRegisterService.cs:128`.

`BuyerCredit.Release` returns `null` — no ledger entry, no fact — when the order's outstanding exposure is `<= 0` (`src/Billing/Domain/BuyerCredit.cs:212–216`, `BC11`/`B5`). `CreditReleaseService.cs:37–46` handles that return value explicitly and answers `released: false`. **`PaymentRegisterService` discards it.** If it is ever `null`, the transaction commits with `payment.received.v1` alone; Orders moves the order to `paid` and then waits forever for `credit.released.v1`, which is its precondition for `Complete`.

**This is not a #8 regression: #7 is byte-for-byte the same shape** (`payment-register.handler.ts:199` calls `credit.releaseHold(...)` with no assignment). It is also not reachable through any live path today — the hold is released on compensation only, and a compensated order is never invoiced — which is why neither assessment has a test for it. It is reachable through exactly one thing that exists in this repository right now: a hand-corrupted fixture of `ORD-000011`'s kind. Recorded, not actioned, because acting on it unilaterally would be a divergence from #7 in a benchmark repository; if it is ever to be closed it should be closed in both.

### Advisories

- **A1 — the third R49 refusal has no responder-level test.** *Already-paid under a different `paymentReference`* is proven in two halves (unit `PaymentRegisterServiceTests.cs:247`, armed; mapping `BillingErrorMapperTests.cs:114`) but never end to end, unlike the other two. The test-matrix row says so rather than claiming otherwise, which is why this is an advisory and not a finding.
- **A2 — backlog id 54's scope grew by one site.** Id 54 (still `pending`) is the missing ported-idiom ledger row for the un-hinted in-transaction re-read in Billing. `FindPaymentByInvoiceIdAsync` adds a second instance of exactly that pattern. It is **correct** here — I checked it at the engine (§3, RCSI + the credit-row `UPDLOCK` ordering) and the doc comment at `EfCoreInvoiceRepository.cs:176–181` names it explicitly rather than leaving it implicit — but whoever closes 54 must now write two rows, not one.
- **A3 — the ledger's blind spot, and this feature sat in it.** `CLAUDE.md` binds the ported-idiom ledger to *"every `design.md` for a feature that ports a #7 mechanism"*. This feature ports #7's handler wholesale and, being `sdd: false`, has no `design.md` — so the rule did not bite, and N1 is precisely the class the ledger exists to catch (*#7 relied on a `markPaid` that returns its event id; in #8 that property is supplied by — nothing*). Phases 11–13 are three more `sdd: false`-heavy services ported from the same source. Carried into the phase assessment.

### #7's findings, checked for inheritance

- **N11 (its one non-blocking finding of substance) — inherited as prevention, and I verified it rather than taking the report's word.** `PaymentRegisterService.IdentityMatches` (`:143–156`) compares whichever of `invoiceId`/`invoiceReference` the caller supplied against the invoice the `paymentReference` lookup resolved, on the fast path, before any lock. The sequential cross-invoice case that #7 answered with a success-shaped `duplicate` naming a different invoice now raises `PaymentReferenceConflictError` → `PRECONDITION_FAILED`, and **the concurrent form answers the identical code** (`:249`, the two-credit-line test, where only the UNIQUE constraint can arbitrate). #7's asymmetry — the same logical condition answering differently depending on who won a race — is gone in both directions, and both directions have a named test. Deliberately never `CONFLICT`, because `NatsSagaCommandsAdapter` classifies that as terminal for a different reason (`BC27`); the mapper's own comment says so.
- **N12 (impl record count off by one) — did not recur** where I could check: 225 unit and 83 integration are exact, the five `issued` invoices in the pre-state are the five that were in the database, and the test-matrix count cells recompute correctly.
- **N13 (a permanently stranded order in shared demo data) — did not recur.** See §5.

---

## 8. `CHECKPOINTS.md` — every applicable box walked

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer
- [x] every agent definition declares its model (reviewer declares deliberate inheritance in its description)
- [x] `./init.sh` exits 0 — re-run by me this pass

### C2 — state is coherent
- [x] at most one feature `in_progress` — zero; this one was `in_review` → `done`
- [x] every status is in `rules.valid_status`
- [x] every `done` feature has passing tests associated with it
- [x] `progress/current.md` describes the active session (feature 22, phase 10) — no leftovers
- [x] no `blocked` feature lacks a reason (there are none)

### C3 — architecture is respected
- [x] no framework reference in any `Domain/` folder — **`Architecture.Tests` run, 16/16**, not eyeballed (`DomainPurityTests`, `DomainDecimalTests`, `CqrsDomainPurityTests`, `SharedKernelHasNoPackagesTests`, the two fact-confinement suites)
- [x] no cross-service DB access — Billing touches `otc_billing` only; the only occurrences of other database names in `src/Billing` are doc-comment cross-references. `RetailerCode`/`CompanyCode`/`OrderReference` travel as business identifiers, no FK crosses a boundary
- [x] no shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — this feature adds no project and no package
- [x] no `Domain/` namespace references `OrderToCash.Cqrs` — enforced by `CqrsDomainPurityTests`, green
- [x] `src/SharedKernel` still has zero `PackageReference` — enforced, green
- [x] no `decimal` in domain arithmetic — `Money` is `long` minor units throughout the new path (`MarkPaidInput`, `PaymentSnapshot`, `payments.amount` `bigint`); `DomainDecimalTests` green
- [x] every interaction classifiable — `billing.payment.register` is a **NATS RPC** (a request expecting a reply; correctly not a fact); `payment.received.v1` / `credit.released.v1` are **Kafka facts** via the outbox. No Kafka-as-request-bus, no RPC-for-facts
- [x] one `BackgroundService` per transport — the sixth subject went onto the existing `BillingRpcResponder`, not a fourth responder class; `BI31` coverage test updated to six
- [x] no stray debug logging, no context-free TODOs in the diff

### C4 — verification is real
- [ ] `./quality.sh` (format + build + test + coverage) — **not re-run wholesale this pass**, per the scope rule; the implementer ran it to completion and reported exit 0. I ran the whole-solution build (0 warnings), `Billing.UnitTests` 225/225, `Billing.IntegrationTests` 83/83 end to end, `Architecture.Tests` 16/16, and five forced `--no-incremental` rebuilds
- [x] domain/unit tests are pure — `PaymentRegisterServiceTests` imports no framework, no DB, no broker: hand-built `RecordingInvoiceRepository`/`RecordingBuyerCreditRepository`/`FakeUnitOfWork`/`FakeClock` only
- [x] integration tests use Testcontainers against real MS-SQL / NATS / Kafka — verified by running them; the concurrency tests use two real NATS connections and real row locks
- [ ] coverage ≥80% domain / ≥60% overall — **not independently re-measured this pass**; the enforcing gate is feature 34 and does not exist yet, which the impl record states plainly rather than implying otherwise
- [x] no Jest anywhere — xUnit throughout

### C5 — the session closed cleanly
- [x] no suspicious untracked files — the 9 untracked paths are this feature's 6 source/test files, its impl record and this verdict; my probes left nothing behind (`git status` clean of scratch files, and the one mutated source file is `cmp`-identical to its backup)
- [x] `progress/history.md` has an entry for this feature **including its effort record** — appended by me at approval, with the Phase 10 closing assessment
- [x] `feature_list.json` reflects the true state — id 22 flipped `in_review` → `done` by me, as a single-line edit; `git diff` shows exactly that one line
- [x] the human has been told what was done and how to test it — the impl record carries the reproducible live walkthrough
- [x] **Claude did not commit** — no `git commit`, no `git push`, and no `git checkout --` on any file

### C6 — Spec-Driven Development
- n/a — `sdd: false`. No `specs/billing_remittance_intake/` is required or expected; the specification of record is `specs/shared/requirements.md` R47–R49 plus id 22's three acceptance bullets, and all six clauses are traced in §2 and §4. (Advisory A3 notes what that costs.)

### C7 — spec-reuse fidelity and benchmark honesty
- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md`'s Status column — verified by a real `diff -rq` against the #7 checkout, and by reading the `git diff`: only the three R47–R49 Status cells and the two derived count cells changed
- [x] no silent fork — no amendment was made this feature
- [x] the `R<n>` ids are #7's, and the .NET realisation genuinely satisfies the same requirements — §2
- [ ] `n8n/workflows/*.json` unchanged and all four green against the .NET Gateway — **unchanged** (verified, and `2-payment-robot.json` read in full for §4), but **not runnable**: no Gateway exists until Phase 11. Box left open honestly rather than half-ticked
- [ ] the black-box API script proves the same saga steps as #7's — **not yet**; no API surface exists. The equivalent proof at this phase is the live NATS-level walkthrough in §5
- [x] `progress/history.md` effort records complete and honest, including the features that were not faster — the Phase 10 assessment states a ≈1.5–1.7× total plainly and separates the gate-ordered work rather than netting it off
- [x] the README's benchmark section gives what reuse saved, did not save, and cost — owed a Phase 10 refresh at wrap-up (leader's, at commit time)

### `feature_list.json` id 22 acceptance
- [x] **idempotent by `paymentReference`** — §3: three independent mechanisms, each armed; sequential, concurrent-same-invoice and concurrent-cross-invoice all covered; absence proven by row counts and a whole-table fact count
- [x] **no internal payment timer anywhere** — §4: enumerated across the whole repository and every file type, plus `AddHostedService` and `*Options` sweeps; every hit classified
- [x] **emits PaymentReceived and CreditReleased** — §3: both armed by deletion (implementer probes A/B) *and* by payload corruption (my P3, implementer's F), in the required order (my P1a/P1b), verified live in the outbox with both rows published

---

## 9. Notes on this review's own footprint

Five mutations rewrote `src/Billing/Application/PaymentRegisterService.cs`; each was restored from a scratchpad backup, `cmp`-verified empty, `touch`ed and rebuilt `--no-incremental` before the confirming run, and the final state is md5 `5eab17898014e01cb81bd9c0d2fe2f34` — identical to the submitted version. The confirming green run after the last restore was `Billing.UnitTests` 225/225 and `PaymentRegisterTests` 9/9, followed by the full `Billing.IntegrationTests` 83/83. That file's mtime (12:36) is review activity, not implementation activity. No other file was modified. No fix was applied by me: I have no Write or Edit tool, by design, and the three findings above are the implementer's and the leader's to act on.

---

## 10. Bookkeeping done at approval

- `feature_list.json` id 22 `in_review` → **`done`**, as a **single-line edit** (`sed` on line 342, never a JSON round-trip); `git diff` shows exactly that one line changed and the file still holds 55 features. No `git checkout --` was run on it, at any point, for any reason.
- `progress/history.md`: the feature's entry appended **with its effort record** (1 implementation session + 1 review pass, ≈56 min + ≈32 min, ≈1 h 28 min first artefact → verdict against #7's ≈1 h 01 min, ≈1.44×), followed by the **Phase 10 closing assessment** — the clock with gate-ordered work separated from baseline-comparable work, the confound column across all four features, what the ported-idiom ledger is now worth including its two new failure modes, the backlog attachment map's result, and a plain answer on the adequacy of the widened commit-message rule.
- `progress/current.md`: the `**Feature:**` / `**Status:**` header reset to idle. Closing id 22 left **no** feature `in_progress` or `in_review`, and `init.sh`'s coherence check correctly failed on a `current.md` that still claimed one — a guard firing exactly as designed, on state my own transition had just created. Only those two lines changed; the session's decisions and the live backlog attachment map are untouched, because the leader needs them for the wrap-up. **`./init.sh` re-run afterwards: exit 0.** The previous header also carried a reminder that two record corrections were owed from feature 21 (`tasks.md` `H1`–`H4` ticking over *"NOT PERFORMED this session"*, and `requirements.md`'s `BI22` row reading `TODO`); I checked before dropping it — `grep -rn "NOT PERFORMED" specs/billing_invoicing/` returns nothing and the `BI22` row now reads `DONE` with its evidence, so both landed and the reminder is spent.
- **Not done, deliberately, and owed to the leader:** the backlog entry for **N1** (amendment A1's causal edge) before the first Phase 11 implementer is dispatched, and a status correction for backlog **id 54**, whose work landed in feature 19 and whose entry still reads `pending`. Backlog entries are the leader's, `feature_list.json` is a single-writer file, and a reviewer's bookkeeping mandate covers the feature under review and nothing else.
