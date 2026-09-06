# `billing_credit` (feature 19, phase 10) — review

**Verdict: REJECTED.** Four defects, all of them guards rather than production behaviour, all four found by mutation probes this review ran itself. The service, the cross-service refactor and the two gate rulings are otherwise implemented, verified and — on 30 of my 34 probes — genuinely armed. Feature 19 is set back to `in_progress`. Backlog ids **48**, **50**, **51** and **54** are verified as holding but are **left `pending`** so they close with the feature at re-review; **id 53 does not hold** and must not be closed as written; **id 52** stays open, correctly.

> **What this review re-ran, and what it did not.** I re-ran the **whole** suite, twice: once on the tree as submitted and once after all my mutations were restored — **634 unit + 178 integration, 812/812 green, my own runs** (per project: Billing 92, Contracts 21, Cqrs 23, Fulfillment 119, Orders 279, Seed 34, SharedKernel 50, Architecture 16; Orders.Integration 71, Fulfillment.Integration 56, Billing.Integration 51). The figures match the implementer's report exactly. I did **not** re-run `./quality.sh` end to end (its format + build + test + coverage pass duplicates what I ran); I ran `./init.sh` myself (exit 0) and `dotnet build OrderToCash.sln --no-incremental` (0 warnings, 0 errors). Everything else below is a probe, a grep or a diff I ran myself, quoted with its command.

---

## 1. Defects — each must change before re-review

### D1 (blocking) — `BC24` / ledger `L11`: the guard cannot fail. `tasks.md` H8 is ticked on an arming that could not have happened

**File:** `src/Billing/Infrastructure/Persistence/BuyerCreditRowMapper.cs:40`; guard `tests/Billing.IntegrationTests/BuyerCreditRepositoryTests.cs:105` (`BC24_RoundTripsALedgerEntrysInstantUnchanged_UnderANonUtcHostTimeZone`).

I applied **H8's own prescribed mutation** — drop `TimeSpan.Zero` from the read conversion, so the production read path becomes `new DateTimeOffset(row.CreditDate)` and silently applies the machine's local offset — rebuilt, and ran the suites:

```
Billing.UnitTests        Passed! - Failed: 0, Passed: 92, Total: 92
Billing.IntegrationTests Passed! - Failed: 0, Passed: 51, Total: 51
```

**Fully green under the mutation the task says must make `BC24` FAIL.** The reason is visible in the test: after the round trip it reads the raw row and re-implements the conversion itself —

```csharp
var readBackInstant = new DateTimeOffset(row.CreditDate, TimeSpan.Zero);
Assert.Equal(knownInstant, readBackInstant);
```

— so it asserts against its own arithmetic, never against `BuyerCreditRowMapper.ToDomain`'s. The mapped entity's instant is never asserted anywhere: `grep -rn --include=*.cs "EntryDate" src/Billing tests/Billing.*` returns seven hits, of which the only test hit is `BuyerCreditTests.cs:47` comparing two in-memory entries. `reloaded.Summary.ByOrder` carries no date, so nothing downstream of the mapper observes it either.

**Why this one matters more than its size.** `L11` is one of the three ledger rows §15.1's boundary enumeration *forced* into existence, and it is a ⚑ ARM row whose evidence sits in §4.2 — the half of the arming table the report honestly marks as *"mutation as prescribed, guard confirmed green today"* rather than reproduced. A guard being green today is exactly the evidence that cannot distinguish a guard from a decoration; this is the case where it hid one. The property itself is **correct in the code** — the ledger did its job — but the Guard column of `L11` currently claims something no test holds.

**Fix:** assert the instant obtained **through the production read path** (`LockForOrderAsync` → `BuyerCreditRowMapper.ToDomain` → `CreditLedgerEntry.EntryDate`), then arm H8's mutation and record the verbatim failure.

### D2 (blocking) — backlog id 53 / `BC32`: the three named sites are untouched, so the entry is not closed

**Files:** `tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs:160`, `tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:51`, `tests/Fulfillment.IntegrationTests/StockListTests.cs:27`.

```
$ git diff --stat tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs \
    tests/Fulfillment.IntegrationTests/StockReplenishTests.cs tests/Fulfillment.IntegrationTests/StockListTests.cs
 3 files changed, 119 insertions(+)
$ git diff -U0 <those three files> | grep -E "^-" | grep -v "^---"
(no output)
```

**Pure insertions: not one existing line changed.** `OrdersCreateAcceptanceTests.cs:160` is still `RpcJson.Deserialize<OrdersCreateReplyPayload>(replyMsg.Data!); // the request succeeded` — the exact line id 53's first acceptance clause names, still asserting nothing; `StockListTests.cs:27` is still `Assert.Equal(2, byCompany.Items.Count)` with no prior discriminating assertion; `StockReplenishTests.cs:51-52` still goes straight to `Assert.Single(payload.Items)`.

Three **new** `BC32_FailsOnItsOwnAssertion_WhenTheResponderAnswersAnRpcError` cases were added, and they do have the right shape — but a fourth test that sees is not a fix for three tests that are blind, and `BC32`'s own requirement text (*"every integration test that deserialises an RPC reply"*) is false while those three stand. `tasks.md` K2 says both *"give each of the three sites the shape"* and *"name the added case"*; the backlog entry it closes is not ambiguous, and it is the authority on what closure means.

**Fix:** put the assertion at the three named sites (the added cases stay), arm each, and only then close id 53.

### D3 (blocking, small) — the second mutation family on the wire: `credit.rejected.v1` and `credit.released.v1` identity fields are unguarded

**File:** `src/Billing/Infrastructure/Outbox/CreditFactPayloadMapper.cs:34` and `:45`.

I corrupted two payload fields at the mapper — the boundary with no unit test of its own — and rebuilt:

```
RetailerCode: "WRONG-CODE",   // on CreditRejectedPayload
CompanyCode:  "WRONG-CO",     // on CreditReleasedPayload
→ Billing.IntegrationTests  Passed! - Failed: 0, Passed: 51, Total: 51
```

(and, corrupting only the rejected fact's `retailerCode`, `--filter CreditHoldTests` → `Passed! 10/10`).

The enumeration that explains it: `grep -rn "RetailerCode" tests/Billing.IntegrationTests/*.cs` shows exactly **one** assertion of a fact's retailer code — `CreditHoldTests.cs:107`, inside `BC28_…`, on the **approved** fact. The rejected fact's assertions are `reason`, `requestedAmount`, `availableCredit` (`CreditHoldTests.cs:141-143`); the released fact's are `reason`, `releasedAmount`, `availableCreditAfter` (`CreditReleaseTests.cs:51-53`). `BC28` says *"the `retailerCode`, `companyCode`, `creditCode` and `currency` of **every emitted fact**"*, and its row is marked `DONE` on a test that covers one fact of three.

This is feature 17's class exactly, and the tasks it sits under (H2/H3/H10) are ⚑ countable claims. **Fix:** assert the identity fields on the rejected and released facts in the existing integration cases, and arm with a one-field mapper corruption per fact.

### D4 (blocking, small) — `BC1`: a **malformed** `x-request-id` is unguarded

**File:** `src/Billing/Presentation/Rpc/RpcMeta.cs:45-49`; guard `tests/Billing.UnitTests/CreditResponderHeaderTests.cs:21`.

Mutation: make a malformed request id tolerated by substituting a fresh one (`requestId = UniqueId.New();` in place of the refusal) →

```
Billing.UnitTests  Passed! - Failed: 0, Passed: 92, Total: 92
```

The `[Theory]`'s `HeaderCase.Malformed` malforms only `x-correlation-id` (`CreditResponderHeaderTests.cs:65`); there is no `MalformedRequestIdOnly`. The file's own comment records that this asymmetry was fixed for the *missing* case — the same fix was not carried to the *malformed* case. `BC1` says *"IF **either** header is absent **or is not a well-formed `UniqueId`**"*, and a silently substituted request id breaks the `R12` causation chain without any error. **Fix:** one `InlineData` case plus its arming.

---

## 2. Advisories — not blocking, recorded so they are not re-discovered

- **A1 — `BC29` / `L26` has a residual at the configuration boundary.** `src/Billing/Program.cs:18` is `Environment.GetEnvironmentVariable("BILLING_KAFKA_CLIENT_ID") ?? "otc-billing"`. I verified on this machine that .NET returns `""` (not `null`) for a variable set-but-empty, so `BILLING_KAFKA_CLIENT_ID=` yields an **empty** `ClientId` and librdkafka substitutes its default — the precise silence `BC29` says shall never happen (*"never to boot with an empty or shared one"*). The compile-time half is real and I armed it (§3). `src/Fulfillment/Program.cs:17` has the same shape and pre-dates this feature, so this is a cross-service one-liner (`is { Length: > 0 }`) and belongs in a backlog entry rather than in this feature's scope.
- **A2 — `BC23` does not cover the nested `CreditView`/`PageInfo` schemas.** Renaming `CreditViewPayload.CreditCode` leaves `Billing.UnitTests` 92/92 green (probe run). The theory's list is the six request/reply schemas plus `Money`, which is exactly what `tasks.md` G1 asked for — so this is a gap in `BC23`'s reach, not a deviation from the task.
- **A3 — an internal contradiction in the approved task list, resolved sensibly.** G1 says *"No literal key list anywhere in this file"*; F3, in the same list, requires the exact key set of an approved reply. `CreditRpcPayloadTests.cs:45` carries the literal list F3 needs while `BC23` parses. No action.
- **A4 — the `D5` negative result is exemplary and should survive into the re-review unchanged.** Removing the hint from step 3 does not fail anything, the report says so plainly, explains structurally why (step 1's exclusive lock serialises every writer first), adds a more targeted test, and records the negative rather than dressing it as a pass. That is the right handling of a defence-in-depth property.
- **A5 — a reviewer-side arming hazard worth recording, because I hit it.** `dotnet test <proj> --no-build` runs against the **test project's copy** of the service assembly, so a `dotnet build src/<Service>` after a mutation or a restore does **not** reach it: two of my probe runs reported the *previous* mutation's failures. Same family as `CLAUDE.md`'s stale-binary clause, one level further out. Every result quoted in this review comes from a run that rebuilt the test project.

---

## 3. What I verified as genuinely armed — 30 probes, all restored and re-confirmed

**The parity family (`OB1` / `BC17` / `L27`) — verified byte-level, then armed ten times.** I re-implemented the guard's normalisation independently (strip leading banner comments, `namespace` lines, `using` lines — plain and aliased) and compared all seven files across the three services:

```
OutboxRelay.cs Fulfillment:IDENTICAL Billing:IDENTICAL      OutboxWriter.cs  Fulfillment:IDENTICAL Billing:IDENTICAL
OutboxRelayOptions.cs …:IDENTICAL …:IDENTICAL               KafkaOptions.cs  Fulfillment:IDENTICAL Billing:IDENTICAL
OutboxRelayBackgroundService.cs …:IDENTICAL …:IDENTICAL     OutboxEnvelopeMapper.cs …:IDENTICAL …:IDENTICAL
KafkaFactPublisher.cs …:IDENTICAL …:IDENTICAL
```

Then, one at a time and each restored before the next: a marker line into **each of the seven** Billing copies → case 1 fails **naming that file and the first differing line**, seven times, seven different names; an in-body single-character divergence in a **Fulfillment** copy (`ORDER  BY` → `ORDER   BY`) → fails naming `src/Fulfillment/.../OutboxRelay.cs:82`; deleting `src/Billing/.../KafkaOptions.cs` → case 3 fails naming `Billing/KafkaOptions.cs` **and** case 1's non-vacuity assertion fires (`found 2: Orders, Fulfillment`); a service token in the canonical `src/Orders/.../OutboxWriter.cs` → case 2 fails naming the line. **The family is unified, not partially unified, and the guard fails on every member of it.** `tasks.md` A10's claim re-run by me: every remaining service-name hit in the seven canonical files is on a `using` or `namespace` line, zero elsewhere.

**The property that was nearly lost (`BC29` / `L26`).** `KafkaOptions.ClientId` is `required` with **no** default in all three copies; each service supplies its own (`otc-orders`, `otc-fulfillment`, `otc-billing`) and no other default exists (`grep -rn "ClientId" src/ --include=*.cs`, every hit classified). Deleting the initialiser from `OrdersOutboxOptions.cs` gives, verbatim:

```
src/Orders/Infrastructure/OrdersOutboxOptions.cs(10,42): error CS9035: Required member 'KafkaOptions.ClientId' must be set in the object initializer or attribute constructor.
```

**The checked arithmetic, both halves (`BC30` / `L25`).** `CreditExposure.Summarise` is explicit loops inside one `checked` region covering both the per-order and the cross-order accumulation, `Enumerable.Sum` is used nowhere (`grep -rn "\.Sum(" src/Billing/` → one doc-comment hit), and both `BC30` cases drive the function **directly** with hand-built snapshots. Armed by me: `checked` → `unchecked` fails **both** cases; reverting `Money.Add` to unchecked fails the matching `InlineData("add")` while the other two pass.

**The refactor changed no payload byte.** `OutboxWireParityTests`, `OutboxEnvelopeTests`, `OutboxAtomicityTests`, `OutboxRelayTests`, `IdempotentConsumerTests` (Orders integration, 71/71), `StockItemRepositoryTests` and `FulfillmentOutboxRelayTests` (Fulfillment integration, 56/56) and `GoldenEnvelopeParityTests` against the twelve captured #7 envelopes (`Contracts.UnitTests`, 21/21) are all green on my own runs. All 30 `new OutboxWriter(` call sites pass a payload mapper (`grep`: 30 lines, 30 with a mapper argument, zero one-argument calls).

**Other armings I reproduced myself, each failing the named test and only it:** `B3` (unknown ledger token → `CreditEntryTypes_Parse_Raises…`), `B4` (comparer → `Ordinal` → `BC28_Groups…`), `B10` (delete `Reconstitute`'s over-limit refusal), `C4` (add `OverLimit` to `AdapterRejectionReason` → `BC14_TypesThePort…`), `C7` (#7's `D1`: delete the `Refuse(...)` call on the port-refusal branch → the `BC14` handler case fails on the missing **fact**), `C8`-b (#7's surviving nit `N5`: `RequestedAmount + 1` → `R39_…` **and** `BC14_Returns…` fail), `C9` both directions (delete the `CreditReleased` emission → three tests; hard-code its `Reason` → two tests), `E6` (1205 → `CONFLICT` → three cases), `E7` **in both services** (revert `StopAsync` to `Task.WhenAll(pending)` → `BC22_…` fails in Billing and in Fulfillment), `E8` (hoist the DI scope → `BC21_Resolves…`), `E10` (`ValidateOnBuild = false`), `K1` (reuse the `NatsHeaders` instance → **only** `BC31_…` fails, 278 other Orders cases green — the entry's second acceptance clause), `BC23` (rename `CreditHoldReplyPayload.CreditCode` → the parsed-schema case fails).

**`D3`'s absence claim, re-run rather than re-read** — `grep -rn --include=*.cs` over `src/Billing/` for `IF NOT EXISTS`, `MERGE`, `.SumAsync(`, `AddRange(`, `(int)`, `.Update(`, `.Remove(`: the only hits are two doc comments (`EfCoreBuyerCreditRepository.cs:48`, `CreditExposure.cs:42`), both naming the construct in order to forbid it. Zero call sites.

---

## 4. `R<n>` → test mapping verified

Every method name cited in `specs/billing_credit/requirements.md` §2 was enumerated against the test tree with a script (44 names, `bin`/`obj` excluded): **44 of 44 exist, in the cited file.** The five shared rows are flipped correctly and the named cases exist:

| Requirement | Test |
|---|---|
| `R37` | `tests/Billing.UnitTests/BuyerCreditTests.cs:28` › `R37_KeepsActiveHoldsPlusOpenExposureWithinTheCreditLimitAndRaisesOnAnyUpdateOrDeletionOfALedgerEntry` (armed via B7's shape by C9/B9 probes) |
| `R38` | `tests/Billing.UnitTests/CreditHoldTests.cs:20`; integration `CreditHoldTests.cs:24` |
| `R39` | `tests/Billing.UnitTests/CreditHoldTests.cs:39` — **fails under my `RequestedAmount` corruption**; integration `CreditHoldTests.cs:115` |
| `R40` | `tests/Billing.UnitTests/CreditLedgerTests.cs:25` |
| `R41` | `tests/Billing.UnitTests/CreditLedgerTests.cs:39` — **fails under both my release-fact mutations** |

Local rows: `BC1` **stays TODO** (D4 — the malformed-request-id branch is unguarded), `BC24` **stays TODO** (D1), `BC28` **stays TODO** (D3 — *"every emitted fact"* is guarded on one fact of three), `BC32` **stays TODO** (D2). The other 28 rows are supported by tests I either armed myself or read against the requirement's distinguishing branch.

`specs/shared/` is untouched apart from the Status column: `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` reports exactly one differing file (`test-matrix.md`), and `git diff -U0 specs/shared/` contains no changed line outside Status text and the §1 summary counts.

---

## 5. `CHECKPOINTS.md`

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all present
- [x] `progress/current.md`, `progress/history.md` present
- [x] `.claude/agents/` holds the five roles; every definition declares or deliberately inherits a model
- [x] `./init.sh` exits 0 (my run)

**C2 — state coherent**
- [x] at most one feature `in_progress` (none while `in_review`; this review sets 19 back to `in_progress`)
- [x] every status valid; ids unique; 24/53 done
- [x] every `done` feature has passing tests — 812/812 on my runs
- [x] `progress/current.md` in lockstep (init.sh check 4)
- [x] no `blocked` feature

**C3 — architecture**
- [x] domain purity by running `Architecture.Tests` (16/16), which now includes `BillingDomainPlaceholder`'s assembly
- [x] no cross-service DB access — Billing names other schemas only in comments; no cross-service `ProjectReference` except `src/Seed`, which is by design
- [x] no shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs` — `Billing.csproj` references exactly those three
- [x] no `Domain/` namespace references `OrderToCash.Cqrs` (`CqrsDomainPurityTests`, green)
- [x] `src/SharedKernel` still has zero `PackageReference` (`SharedKernelHasNoPackagesTests`, green; `Money.cs` changed by three `checked` expressions and nothing else)
- [x] no `decimal` in domain arithmetic (`DomainDecimalTests`, now non-trivial over an all-money domain)
- [x] Kafka-fact / NATS-RPC classification holds: three request/reply subjects on NATS, three facts through Billing's own outbox to `otc.billing.facts.v1`
- [x] no stray debug logging or context-free TODOs found

**C4 — verification real**
- [x] build clean (`--no-incremental`, 0 warnings, 0 errors); `dotnet format` reported clean by the implementer's `quality.sh` run, which I did not repeat
- [x] domain tests pure — `Billing.UnitTests` domain cases construct aggregates and call `CreditExposure.Summarise` directly
- [x] integration tests hit real containers (`MsSqlContainerFixture`, `NatsContainerFixture`, `KafkaContainerFixture`); the responder tests boot the real `BillingHost` graph
- [ ] **coverage thresholds** — not enforced anywhere yet; `quality.sh`'s own header defers the gate to feature 34 and explicitly refuses to fake it. Pre-existing project state, not this feature's
- [x] no Jest anywhere
- [ ] **tests would fail if the behaviour regressed** — true for 30 of my 34 probes; D1, D3 and D4 are the exceptions, and they are why this is rejected

**C5 — session close** (not applicable to a rejection)
- [ ] `progress/history.md` entry with effort record — deliberately **not** written; it is added at approval
- [x] `feature_list.json` reflects true state after this review (19 → `in_progress`)
- [x] no suspicious untracked files: `git status --short` is 112 entries, all named by `tasks.md` J5's list
- [x] Claude did not commit

**C6 — SDD**
- [x] `specs/billing_credit/{requirements,design,tasks}.md` all present
- [x] EARS notation with `R<n>`/`BC<n>` ids throughout
- [ ] **every task genuinely done** — H8 and K2 are ticked but not done (D1, D2)
- [ ] **every `R<n>` covered by a test that exercises it** — `BC1`, `BC24`, `BC28`, `BC32` are not
- [x] the spec commit precedes implementation (spec written and gated 2026-09-05, both still uncommitted in this session's working tree, in the right order)

**C7 — reuse fidelity**
- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md`'s Status column — real `diff -rq` against the #7 checkout
- [x] no silent fork; `requirements.md` §3 records both open shared-contract observations with owners rather than editing anything
- [x] the `R<n>` ids are #7's and the .NET realisation satisfies them (`R41` is fuller than #7's, and says so and why)
- [x] `n8n/workflows/*.json` unchanged (absent from `git status`)
- [x] the black-box API path is unchanged by this feature; the live walkthrough in §3 of the report proves the saga end to end, including the first genuine compensation this repository has run
- [ ] effort record — pending, at approval
- [ ] README benchmark section — pending, at approval

---

## 6. The question Phase 9 left open: did the ledger prevent, or only review?

The parent asks for a judgement on the experiment, so here it is, on evidence rather than hope. **Qualified yes for the code; no for the guard column.**

- **Two properties would plausibly have been lost without their rows, and both are present in the code.** `L7`: the committed-exposure Σ is a hinted `SqlQueryRaw` and `SumAsync` appears nowhere but in a comment forbidding it — the obvious EF rendering drops the hint invisibly, and the row is what made that a decision instead of an accident. `L26`: the per-service Kafka client id, which the gate-ordered unification *removed*, is re-supplied as a `required` member and I confirmed the `CS9035` at the compiler. `L26` is the strongest single piece of evidence in the experiment, because it is a property that was destroyed and re-created inside one feature, and the ledger is what noticed.
- **`L12` (loud parse of the `type` token) is real and armed;** `L24` (an adapter structurally cannot say `over_limit`) is real and armed. Of the three rows the enumeration forced, `L7` and `L12` hold fully.
- **But `L11` — the third enumeration-forced row — is where the experiment's limit shows.** The property is correct in the code (the ledger's contribution) while its named guard cannot fail (D1). So the enumeration method works at spec time and produced a correct implementation; what it does not do, and cannot do, is make the Guard column true. **The ledger's guard column needs the same arming discipline as any other claim, and it did not get it here** — precisely because H8's evidence sat in the half of the arming table that was not reproduced.
- **Net for the record:** Phase 10 is the first phase where a ledger row demonstrably caused a property to exist that would otherwise have been lost (`L26`, and probably `L7`). That is the positive evidence Phase 9 asked for. It arrives alongside the first ledger row whose guard is decorative, which is a new failure mode for the same instrument and should be written into the convention: **a ledger row's Guard column is a countable claim, and a countable claim must be seen to fail.**

---

## 7. Backlog entries — verified one at a time

| Id | Holds? | Evidence |
|---:|---|---|
| **48** | **Yes** | `BC31_…` asserts `Assert.NotSame` on two captured `NatsHeaders`; I armed it by hoisting the instance to a field and clearing per call — only `BC31` failed, the other 278 Orders cases stayed green, which is the entry's second clause exactly. Zero production change, as the entry requires |
| **50** | **Yes** | Both `StopAsync` bodies await each in-flight task individually through `WrapAsync`; I armed the fault half in **both** services (revert to `Task.WhenAll(pending)`) and `BC22_…` failed in each. The drain half is asserted in the same test (`healthyRan` before return) |
| **51** | **Yes, with a note** | `AsyncApiSchema` parses `specs/shared/asyncapi.yaml` as text in all three test projects; the four instruments assert against it; the three false sentences are corrected (`StockRpcPayloadTests`' XML doc, `specs/fulfillment_stock/design.md` §6.3, its `tasks.md` C3). **Note:** the entry's literal wording is *"derive their expected property names by parsing"*, and the approved spec instead kept the retyped lists and added a case asserting them against the parsed set (G2/G3 say so explicitly). That closes the correlated-authoring-error the entry was filed for, so I read it as satisfied — but the difference is worth the leader's eye |
| **52** | **Correctly still open** | Its own acceptance forbids fixing in place; the report records the decision rather than omitting it |
| **53** | **NO** | D2. The three named sites are unchanged |
| **54** | **Yes** | `specs/fulfillment_stock/design.md` §15 gains row `L13` carrying all three acceptance clauses: the read is issued only after `LockForOrderAsync` is granted; `EfCoreUnitOfWork.cs:30` opens `ReadCommitted` under RCSI making the snapshot statement-scoped; and the #9 consequence (`REPEATABLE READ` → stale read → permanent `UNAVAILABLE`) stated explicitly |

All six statuses are **left untouched** in `feature_list.json`: 48, 50, 51 and 54 close with feature 19 at re-review; 53 needs D2 fixed first; 52 stands.

---

## 8. What must change before re-review

1. **D1** — make `BC24` assert the instant that comes back **through `BuyerCreditRowMapper`'s own read conversion**, then run H8's prescribed mutation and record the verbatim failure and the forced-rebuild restore.
2. **D2** — give `OrdersCreateAcceptanceTests.cs:160`, `StockReplenishTests.cs:51` and `StockListTests.cs:27` their own discriminating assertion, arm each by making the responder answer a real `RpcError`, and only then claim id 53.
3. **D3** — assert `orderReference`/`retailerCode`/`companyCode`/`creditCode`/`currency` on the `credit.rejected.v1` and `credit.released.v1` outbox payloads in the existing integration cases, and arm each with a one-field corruption in `CreditFactPayloadMapper`.
4. **D4** — add the `MalformedRequestIdOnly` case to `CreditResponderHeaderTests`' `[Theory]` and arm it.
5. **Re-walk `§4.2` of the report.** D1 was in that group, and its failure mode is *"the arming was never actually seen to fail"*. I independently re-armed B3, B4, B10, C4, C7, C8-b, C9, E6, E7 ×2, E8, E10, F3, G5's instrument and K1 from that same group and **all of them hold**, so this is a targeted request, not a blanket one: re-run and record verbatim any remaining `§4.2` row whose guard asserts a value that crossed a **production adapter** (the shape that failed here), and say in the report which rows were re-run.
6. Flip `BC1`, `BC24`, `BC28` and `BC32` back to `TODO` in `specs/billing_credit/requirements.md` §2 until their guards exist, per J2's own rule.

Nothing else changes. Groups A, B, C, D, E, F, G and I are accepted as they stand, and the two gate rulings are implemented as ruled.

---

## 9. For the effort record at approval (not written yet)

`progress/history.md` must, when this closes, separate three buckets rather than one number, because #7's `billing_credit` refactored no shipped service and touched no shared kernel:

1. **Baseline-comparable** — the Billing service itself (groups B–H, I), the row against #7's `billing_credit`.
2. **Gate-ordered, no #7 counterpart** — group A's seven-file cross-service refactor (`FactEvent`, `IFactPayloadMapper`, the canonical writer, the `KafkaOptions` unification and 30 call sites) and B13's `src/SharedKernel` change.
3. **Backlog closures, no #7 counterpart** — group K (ids 48, 53, 54) plus the id 50 and 51 work inside E7/G1–G5.

And the confound column agreed at Phase 9's close, for **this** rejection: of the four defects, **D3 is the one #7's standard would have caught** — it is #7's own `W3`/`N5` probe shape, which #7 ran, found and shipped as a non-blocking nit. **D1, D2 and D4 are #8-standard finds**: D1 required re-running a prescribed mutation rather than reading the arming table, D2 required diffing a backlog entry's named line numbers against the claim of closure, and D4 required mutating a validation branch the theory did not cover. That is three of four attributable to the raised bar, and it should be recorded that way so the final trilogy table does not read harness maturity as a language penalty.

---

*Reviewed on the tree as submitted (`git status --short` = 112 entries). Every mutation this review applied was taken from a backup copy, restored with `cp`, confirmed byte-identical with `diff`, force-rebuilt, and re-run green; no `git checkout --` was used on any file, and `feature_list.json` was edited one line at a time.*

---
---

# ROUND 2 — re-review of the fix round (`progress/impl_billing_credit.md` §13)

> **Round 1 above is unamended and unreopened.** Everything below is new work on the tree as re-submitted. Where round 1's finding still stands unchanged, it is cited, not rewritten.

**Verdict: APPROVED.** All four blocking defects are fixed, and each was re-proved by **my own** mutation — in every case the mutation that disproves *that defect's specific property*, not one that kills the test for an unrelated reason. Feature 19 is set `done`; backlog ids **48**, **50**, **51** and **53** are closed with it; **52** stays open by design; **54**'s row is confirmed landed. One new advisory (**A6**) is recorded below: it is a residual of `BC32`'s universal quantifier at six sites round 1 did not name, it is the *opaque-failure* tier rather than the *silent-pass* tier, and it is not a reason to reject a second time — round 1 itself defined the fix scope as three sites and said "nothing else changes".

> **What round 2 re-ran, and what it did not.** I did **not** re-run `./quality.sh`, and I did not re-run the whole suite: the claim under test here is four named guards, not the suite. My own runs: `./init.sh` (exit 0), `Billing.UnitTests` **94/94**, `Architecture.Tests` **16/16**, `Billing.IntegrationTests` **51/51**, plus targeted class runs of `StockListTests`+`StockReplenishTests` (**5/5**) and `OrdersCreateAcceptanceTests` (**10/10**) after every restore. The implementer's 636 + 191 = 827 is consistent with round 1's 634 + 178 = 812 once `Notifications.IntegrationTests` (7) and `Seed.IntegrationTests` (6) are counted and D4's two new theory cases are added; I did not re-derive the total independently and do not claim it. Eight mutations were applied and restored in this round, each from a backup taken first, each restored, `diff`-confirmed, force-rebuilt and re-run green.

## R2.1 The four defects, each re-proved by its own disproving mutation

### D1 — FIXED. H8's own prescribed mutation now fails `BC24`, at the mapper

This is the one that mattered, and it is the one that could have been fixed in a way that looks like a fix. It was not.

The guard no longer re-implements the conversion. `tests/Billing.IntegrationTests/BuyerCreditRepositoryTests.cs:148-150` now reads the instant back through `LockForOrderAsync` → `BuyerCreditRowMapper.ToDomain` → `CreditLedgerEntry.Reconstitute` → `ToSnapshot().Entries` and asserts *that* value. I applied **H8's prescribed mutation verbatim** — dropped `TimeSpan.Zero` at `src/Billing/Infrastructure/Persistence/BuyerCreditRowMapper.cs:40`, so the read becomes `new DateTimeOffset(row.CreditDate)`:

```
BuyerCreditRepositoryTests.BC24_RoundTripsALedgerEntrysInstantUnchanged_UnderANonUtcHostTimeZone [FAIL]
  Assert.Equal() Failure: Values differ
  Expected: 2026-06-15T09:30:00.0000000+00:00
  Actual:   2026-06-15T09:30:00.0000000-04:00
  at ...BuyerCreditRepositoryTests.cs:line 150
Failed! - Failed: 1, Passed: 0, Total: 1
```

Restored from backup, `sed`-confirmed at line 40, `dotnet build src/Billing --no-incremental`, re-run: `Passed! - Failed: 0, Passed: 1, Total: 1`. **The claim `L11`'s Guard column makes is now true**, and it is true of the production read path rather than of the test's own arithmetic. This is the feature-18 shape the parent asked me to watch for, and it is not present: the fix did not move the assertion somewhere a *different* mutation would catch, it moved it onto the value the prescribed mutation corrupts.

### D3 — FIXED. Both wire-identity fields fail on the wire, each on its own field

`src/Billing/Infrastructure/Outbox/CreditFactPayloadMapper.cs`, one field per fact, both corrupted at once so that a cross-kill would have been visible:

```
RetailerCode: "WRONG-CODE"  on CreditRejectedPayload  (line 34)
CompanyCode:  "WRONG-CO"    on CreditReleasedPayload  (line 45)

CreditHoldTests.R39_OverLimit_... [FAIL]  Expected: "CarrefourEs"  Actual: "WRONG-CODE"   CreditHoldTests.cs:line 149
CreditReleaseTests.BC25_...      [FAIL]  Expected: "IBERFOODS"    Actual: "WRONG-CO"     CreditReleaseTests.cs:line 58
Failed! - Failed: 2, Passed: 0, Total: 2
```

Each fails on **its own** field at **its own** line — the rejection fact on `retailerCode`, the release fact on `companyCode` — which is the pair the parent named and the pair round 1 corrupted to a fully green suite. The other three identity fields (`orderReference`, `creditCode`, `currency`) are asserted in the same block on the same deserialised payload, so the two probes establish that the block is reached and live. Mapper restored, `diff`-identical, force-rebuilt, both cases green.

### D4 — FIXED. A malformed `x-request-id` is refused, and only that case moves

Mutation at `src/Billing/Presentation/Rpc/RpcMeta.cs:45-49` — the malformed-request-id branch made to substitute a fresh id instead of refusing:

```
BC1_...(subject: "billing.credit.hold",    headerCase: MalformedRequestIdOnly) [FAIL]
BC1_...(subject: "billing.credit.release", headerCase: MalformedRequestIdOnly) [FAIL]
Failed! - Failed: 2, Passed: 6, Total: 8
```

**Exactly the two new cases fail and the six pre-existing ones stay green** — so the added `InlineData` discriminates the malformed-request-id branch specifically, and does not merely re-cover the missing-header branch. The theory's `expectedHeaderName` (`CreditResponderHeaderTests.cs:42`) makes the case assert that the error names `x-request-id`, not `x-correlation-id`, which is what closes the asymmetry round 1 named. Restored, force-rebuilt, 9/9 green.

### D2 — FIXED at all three named sites, armed by me at the responders

I forced each responder to answer a real `RpcError` for the exact request the test sends — a conditional throw ahead of the request decode, so no unreachable-code error (`if (data.Length > 0) { throw … }`; the naive unconditional `throw` fails the build under `TreatWarningsAsErrors` with `CS0162`, which is worth recording for whoever repeats this).

```
StockListTests.FS15_...            [FAIL] Assert.NotNull() Failure: Value is null   StockListTests.cs:line 27
StockReplenishTests.HappyPath_...  [FAIL] Assert.NotNull() Failure: Value is null   StockReplenishTests.cs:line 52
OrdersCreateAcceptanceTests.AcceptanceItem1_... [FAIL]
  Assert.Matches() Failure: Pattern not found in value
  Regex: "^ORD-[0-9]{6,}$"   Value: null                                            OrdersCreateAcceptanceTests.cs:line 161
```

All three fail **on their own named assertion at the line the backlog entry named**, none on a dereference and none by passing. The three `BC32_FailsOnItsOwnAssertion_…` cases added in round 1 also failed under the same mutations (`StockListTests.cs:76`, `StockReplenishTests.cs:96`, `OrdersCreateAcceptanceTests.cs:223`), so the pair now covers both. Restores: `src/Fulfillment/Presentation/StockRpcResponder.cs` byte-identical to backup and its only diff against `HEAD` is the pre-existing id-50 fix (`31 insertions(+), 3 deletions(-)`); `src/Orders/Presentation/OrdersCreateResponder.cs` shows **no diff against `HEAD` at all**. `grep -rn "ARMING PROBE" src/ tests/` → **0**.

## R2.2 The traceability rows — restored, and each cites a guard I armed myself

| Row | Restored to | The guard I armed in this round |
|---|---|---|
| `BC1` | `DONE` | `CreditResponderHeaderTests` › `HeaderCase.MalformedRequestIdOnly` ×2 — fail alone under the `RpcMeta` tolerance mutation |
| `BC24` | `DONE` | `BC24_RoundTripsALedgerEntrysInstantUnchanged_…` — fails under H8's own prescribed mutation |
| `BC28` | `DONE` | `R39_OverLimit_…` (rejected fact) and `BC25_…` (released fact) — each fails on its own identity field |
| `BC32` | `DONE` | the three originally-blind sites **and** the three `BC32_…` cases — all six fail under a forced `RpcError` |

`specs/billing_credit/requirements.md`: **30 `DONE`, 0 `TODO`.** `specs/billing_credit/tasks.md`: **87 ticked, 0 open**, and H8 and K2 are now ticked on armings that were seen to fail — by the implementer and, independently, by me. `specs/shared/test-matrix.md`'s `R37`–`R41` rows are unchanged from round 1 and correct.

## R2.3 Did the fix round disturb anything

- **The parity family is intact.** I re-ran my own normalisation (strip leading banner comments, `using` and `namespace` lines) over all seven files × three services: **7/7 `IDENTICAL` for both Fulfillment and Billing**. `OutboxRelayParityTests` 3/3 green on my run. *(Note for whoever repeats this: normalisation must strip the leading `// COPY OF — …` banner, or all seven report `DIFFERS` for a reason that is not a divergence.)*
- **Five production files were armed across the two rounds and all five are clean.** Two are tracked and prove it against `HEAD` (`OrdersCreateResponder.cs` clean, `StockRpcResponder.cs` = id-50 fix only); three are untracked Billing files and prove it against backups I took from the delivered tree, plus a direct read of each mutated line (`TimeSpan.Zero` present at `BuyerCreditRowMapper.cs:40`; both identity fields sourced from the event at `CreditFactPayloadMapper.cs:34/:45`; the refusal branch present at `RpcMeta.cs:45-49`).
- **Green after every restore, on my runs**: Billing unit 94/94, Billing integration 51/51, Architecture 16/16, `StockListTests`+`StockReplenishTests` 5/5, `OrdersCreateAcceptanceTests` 10/10, `OutboxRelayParityTests` 3/3.
- **`./init.sh` exit 0** on the tree as re-submitted.

## R2.4 New advisory

- **A6 — `BC32`'s universal quantifier still over-reaches its guards, at six sites, three of them in a file this feature authored.** `BC32` says *"every integration test that deserialises an RPC reply"*. It is now true at the three sites backlog id 53 named. It is **not** true at `tests/Billing.IntegrationTests/CreditListTests.cs:39, :73, :77` (`Assert.Equal(4, reply.Items.Count)`, `Assert.Single(filteredByCompany.Items)`, `Assert.Single(page1.Items)` — a file **new in feature 19**) nor at `tests/Fulfillment.IntegrationTests/StockListTests.cs:33, :38, :44` (the second, third and fourth replies of `FS15`, where only the first was named). Enumerated, not eyeballed — a script over every `tests/**/*.cs` that tracks each variable holding a deserialised reply and reports the first collection touch with no prior `Assert` on that variable; it reports exactly ten sites, of which the four fixed ones are reported because the fix's own `Assert.NotNull` **is** the first touch. **Why it is an advisory and not D5:** these are the *opaque-failure* tier — on an error body they throw `NullReferenceException`/`ArgumentNullException` and the test fails loudly, just uninformatively — not the *silent-pass* tier that made `OrdersCreateAcceptanceTests.cs:159` worth a `BC` id. Id 53's own note says as much about the sites it left alone. Round 1 fixed the scope at three sites and closed with *"nothing else changes"*; moving that line now would be goalpost-shifting. **Recommendation for the leader: one backlog entry naming these six sites**, closable inside whichever Phase 10 feature next opens `CreditListTests.cs` — or, if the sweep is not wanted, narrow `BC32`'s wording, because a `DONE` row on a universal claim that is false is the shape this feature spent a whole round on.
- **A7 — a deviation from the approved design at two of D2's three sites, disclosed here because the report describes it as something slightly stronger than it is.** `design.md` §10.5's table names the field to assert first: for `StockReplenishTests.cs:52` *"the reply's own `productCode` / outcome field"*, for `StockListTests.cs:27` *"the reply's `total` / paging field"*. What landed is `Assert.NotNull(payload.Items)` / `Assert.NotNull(byCompany.Items)` — a null check **on the collection itself**, not a different discriminating field asserted before it, and §13.2 calls it *"the reply's own discriminating-field assertion"*. For `StockReplenishReplyPayload(IReadOnlyList<StockViewPayload> Items)` there **is** no other top-level field, so the design's prescription is unachievable as written and what landed is the best available shape. For `StockListReplyPayload(Items, Page)` there is one — `Page.Total`, which `FS15` already asserts at line 45 — and it was not used, so `Page` remains unasserted at line 27. **Not blocking**: the acceptance clause that carries the property ("fails on its own assertion rather than on a dereference") is satisfied and I armed it. Recorded so the next reader does not have to re-derive why the code and the design table differ.

Round 1's A1–A5 stand as written. A1 (`BILLING_KAFKA_CLIENT_ID=` yielding an empty client id, same shape in `src/Fulfillment/Program.cs:17`) is still open and still belongs in a backlog entry rather than in this feature.

## R2.5 `CHECKPOINTS.md`, re-walked for round 2

Only the boxes whose state can have changed are re-walked; the rest stand as marked in round 1 §5.

**C4 — verification real**
- [x] build clean — `dotnet build src/Billing --no-incremental`, `src/Orders`, `src/Fulfillment`: 0 errors, 0 warnings, on my runs after every restore
- [x] integration tests hit real containers — every probe above ran against `MsSqlContainerFixture` / `NatsContainerFixture` / `KafkaContainerFixture`; the D2 armings ran through the real `BillingHost`/`FulfillmentHost`/`OrdersHost` graphs
- [ ] **coverage thresholds** — still not enforced anywhere; `quality.sh` defers the gate to feature 34 and says so. Pre-existing project state, unchanged by this feature, not a reason to hold the feature
- [x] **tests would fail if the behaviour regressed** — the four exceptions round 1 recorded are closed; **8/8** of this round's mutations were killed by the named test and, where the mutation was branch-specific, by that test **alone**
- [x] no Jest anywhere

**C6 — SDD**
- [x] **every task genuinely done** — H8 and K2 were the two round 1 refused; both are now armed, by the implementer and independently by me. 87/87 ticked
- [x] **every `R<n>` covered by a test that exercises it** — `BC1`, `BC24`, `BC28`, `BC32` restored to `DONE` on guards I armed; 30 `DONE`, 0 `TODO`. See **A6** for `BC32`'s over-broad wording, which is a scope statement rather than a coverage gap
- [x] the spec commit precedes implementation (spec 16:19–17:36, first source file 17:38 — both still uncommitted, in the right order)

**C5 — session close**
- [x] `progress/history.md` entry with effort record — appended at this approval, with the three-bucket split and the confound column
- [x] `feature_list.json` reflects true state — 19 `done`; 48, 50, 51, 53 `done`; 52 `pending`
- [x] no suspicious untracked files; `grep -rn "ARMING PROBE" src/ tests/` → 0
- [x] Claude did not commit
- [ ] **`progress/current.md` is now stale and `init.sh` check 4 will fail** — with feature 19 `done` there is **no active feature**, and check 4 then requires the `**Feature:**` line to read *none / idle / awaiting*; it still names `billing_credit`. That file is the leader's session file and resetting it is the leader's session-close step, so I have left it untouched rather than edit someone else's artefact — **but it must be reset before the next `init.sh` is trusted.**

**C7 — reuse fidelity**
- [x] `specs/shared/` untouched by the fix round — the only spec files it changed are `specs/billing_credit/requirements.md` (§2 rows)
- [x] effort record — appended below
- [ ] README benchmark section — the leader's wrap-up step, not the reviewer's

## R2.6 Backlog transitions — each verified independently

| Id | Transition | Evidence I hold |
|---:|---|---|
| **19** | → `done` | this section |
| **48** | → `done` | `NatsSagaCommandsAdapterTests.cs:85-101` captures both `NatsHeaders` instances and asserts `Assert.NotSame`; round 1 armed it (hoist + clear per call → only `BC31` failed, 278 other Orders cases green), which is the entry's second clause exactly. Re-read in round 2; zero production change, as the entry requires |
| **50** | → `done` | both `StopAsync` bodies await each in-flight task individually through `WrapAsync`; round 1 armed the fault half in **both** services (revert to `Task.WhenAll(pending)` → `BC22_…` failed in each) and the drain half is asserted in the same test. Re-read `src/Billing/Presentation/CreditRpcResponder.cs:59-70` in round 2 |
| **51** | → `done` | `AsyncApiSchema.cs` present and used in all three unit-test projects (`Billing`, `Orders`, `Fulfillment`), including `OrdersCreateErrorMapperTests` for the twelve-value `RpcError.code` enum clause; the three false sentences are corrected. **Closed with round 1's note on the record**: the entry says *"derive their expected property names by parsing"* and the approved spec instead kept the retyped lists and added a case asserting them **against** the parsed set. That closes the correlated-authoring-error the entry was filed for; the difference is deliberate and gate-approved, not a shortfall |
| **52** | stays `pending` | its own acceptance forbids fixing in place; correctly untouched |
| **53** | → `done` | **now** holds: the three named sites assert, and I armed each myself at the responder (R2.1 D2). See **A6** for the six sites the entry never named |
| **54** | stays as it was | `specs/fulfillment_stock/design.md:572` carries row `L13` with all three acceptance clauses — the read issued only after `LockForOrderAsync`, `EfCoreUnitOfWork.cs:30`'s `ReadCommitted` under RCSI making the snapshot statement-scoped, and the `REPEATABLE READ` → stale read → permanent `UNAVAILABLE` consequence for #9. Landed via task G3, verified by reading the row |

## R2.7 The ledger experiment — the verdict Phase 9 left open

Phase 9's close set the bar explicitly: *"If Billing ships without a translation defect, that is the first positive evidence the ledger prevents rather than merely documents."* Billing has now shipped. **The enumeration-first ledger earned its cost. Keep it, with the amendment this feature forced.**

The evidence, separated from the hope:

1. **A property was destroyed and re-created inside one feature, and the ledger is what noticed.** `L26`: the gate-ordered `KafkaOptions` unification *removed* the per-service Kafka client id; it came back as a `required` member with no default, which I confirmed at the compiler in round 1 (`CS9035` when the initialiser is deleted). Nothing else in this harness would have seen that — traceability was satisfied either way and the behaviour is identical on every tested path. That is the first observation in this build of the ledger doing the thing it was adopted to do.
2. **The enumeration produced rows recollection would not have.** `L7`, `L11` and `L12` exist because §15.1 walked the boundaries rather than remembering them. `L7`'s hinted Σ and `L12`'s loud token parse are real and armed. `L11` — the `DateTimeOffset` conversion at the `datetime2(3)` boundary — is a property #7 got from a `mysql2` pool option, and **it is correct in the code**; the ledger row is why the conversion was written correctly in the first place. Phase 9's failure mode was *the row nobody wrote*; enumeration is the answer to it, and it worked.
3. **And the instrument grew a new failure mode, in this same feature: the row whose Guard column was decorative.** `L11`'s property was right and its named guard could not fail. That is the ledger's own version of the guard-that-does-not-guard, one level up, and it is now a convention on disk (`CLAUDE.md`, the ported-idiom ledger section: *a ledger row's Guard column is itself a countable claim…*). The cost of finding it was one review round; the cost of the rule that prevents it is one sentence.

**Net:** the enumeration is cheap (it is done while the translation is being thought about anyway), it produced one demonstrable save and two rows nobody would have written from memory, and its one observed failure mode now has a rule. **Recommendation: enumerate for every remaining ported service (features 20–22, 41 and the Despatch/Notifications/Projector ports), and arm every Guard column as a countable claim.** The honest caveat stands and should not be dropped from the record: **you cannot observe a prevented defect**, so "the ledger prevents" remains an inference from `L26` rather than a measurement — but it is now an inference with one concrete instance behind it instead of none.

## R2.8 Effort and the confound, for the record appended to `progress/history.md`

Three buckets, because #7's `billing_credit` refactored no shipped service, touched no shared kernel and closed no backlog entries — a single ratio against it would be comparing different work:

| Bucket | #8 | #7 counterpart |
|---|---|---|
| Baseline-comparable — the Billing service (groups B–H, I) plus its spec, gate, review and fix rounds | **≈4 h 20 min** of the ≈5 h 39 min | ≈3 h 15 min |
| Gate-ordered, no #7 counterpart — group A's seven-file × three-service refactor and `src/SharedKernel`'s `checked` money | **≈45 min** | none |
| Backlog closures, no #7 counterpart — ids 48, 50, 51, 53, 54 | **≈35 min** | none |

Confound column for **round 1's** four defects, per the rule adopted at Phase 9's close: **D3 is the one #7's standard would have caught** — it is #7's own `W3`/`N5` probe shape, which #7 ran, found and shipped as a non-blocking nit. **D1, D2 and D4 are raised-bar finds**: D1 required re-running a task list's prescribed mutation instead of reading its arming table, D2 required diffing a backlog entry's named line numbers against a claim of closure, D4 required mutating a validation branch the theory did not cover. Three of four attributable to the harness, not to the language — and the round-2 cost (≈20 min) belongs to the same column.

---

*Round 2 reviewed on the tree as re-submitted. Eight mutations applied, each from a backup taken first, each restored, `diff`-confirmed byte-identical, force-rebuilt and re-run green; `grep -rn "ARMING PROBE" src/ tests/` returns 0. No `git checkout --` was used on any file. `feature_list.json` was edited one status line at a time and the resulting `git diff` read line by line. My probe restores rewrote the mtimes of `src/Billing/Infrastructure/Persistence/BuyerCreditRowMapper.cs` (21:45), `src/Billing/Infrastructure/Outbox/CreditFactPayloadMapper.cs` (21:47), `src/Billing/Presentation/Rpc/RpcMeta.cs` (21:47) and `src/Fulfillment/Presentation/StockRpcResponder.cs` (21:50) — reviewer activity, not implementation, and all four are byte-identical to the submitted versions.*
