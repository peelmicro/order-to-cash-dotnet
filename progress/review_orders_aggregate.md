# Review — `orders_aggregate` (feature 13, phase 8)

**Verdict: APPROVED.** Zero blocking defects. **One non-blocking defect (D1)** — found by my own mutation probe, not by reading — and **five advisories**. This is the first `"sdd": true` feature in this repository, so **C6 is walked in full for the first time**, and the walk is below.

Reviewed at 2026-09-02, 11:04 → 11:30 local (~0.43h). Evidence in this file is my own unless a line says otherwise.

---

## 1. What I re-ran, what I did not, and why

CLAUDE.md's reviewer clause: *"probe the claims, do not re-run the world"*. Applied as follows.

**Ran myself:**

- `dotnet build OrderToCash.sln --no-incremental` — 0 warnings, 0 errors, seven times (once per arming cycle).
- `tests/Orders.UnitTests` — **24/24 green** on the restored tree, and once per probe.
- `tests/Architecture.Tests` — **13/13 green**, including the new thirteenth rule.
- **Six independent mutation probes** (§4), five of them re-arming rows the implementer claimed, one of them a probe nobody asked for — which is the one that found D1.
- `./quality.sh` — **exit 0 in 153 s wall-clock** on an otherwise idle host (`docker ps` showed the standing compose stack up, no contending test containers). Format clean, build clean, **191 tests / 0 failures across 10 projects**, `coverage.cobertura.xml` produced for all ten including `Orders.UnitTests`.
- `./init.sh` — **exit 0 in 2 s**; SDD coherence passes with 1 sdd feature past `pending` holding its triple-doc.
- `dotnet format OrderToCash.sln --verify-no-changes` — clean (twice: inside `quality.sh`, and again after my last restore).
- Independent coverage arithmetic from the cobertura XML, filtered to `OrderToCash.Orders.Domain.*` classes whose `filename` is under `Orders/Domain`: **830/942 lines = 88.1%**, above the ≥ 80% domain target.
- `git diff specs/shared/test-matrix.md`, `git diff OrderToCash.sln`, `git diff src/Orders/Infrastructure/**`, `git status --untracked-files=all` — the scope walk of §6.
- The four load-bearing `design.md` citations, read in **#7's own checkout** at `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` (§5).

**Did not re-run, deliberately:**

- The four container-backed integration suites (`Orders`, `Billing`, `Fulfillment`, `Notifications`, `Seed`) **a second time**. They ran inside my own `quality.sh` and none of them is a claim this feature makes — the feature adds no infrastructure code and touched no schema. Re-running them standalone would have been ~90 s of duplicated container startup proving nothing new.
- The `OrdersDomainMustNotDependOnContracts` arming probe. The coordinator armed it twice and reported the result, including that the first (bare-`using`) mutation was too weak and the second (a real `typeof(OrderPlacedPayload)` field) failed 1 of 13. I confirmed the rule is present, correctly scoped and green, and did not repeat a probe already run twice by another party.
- Any comparison of #8's seed or master data against #7's. That is feature 12's closed claim and nothing here touches it.

---

## 2. CHECKPOINTS walk

### C1 — the harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 (verified, 2 s).

### C2 — state is coherent

- [x] At most one feature `in_progress` — in fact **zero**: 29 `pending`, 13 `done`, 1 `in_review` (this feature) before my write.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it (191 green solution-wide).
- [x] `progress/current.md` describes the active session — feature 13, the dispatcher ruling, the `bigint` ruling, the `feature_list.json` race. No leftovers.
- [x] No `blocked` feature exists, so nothing to justify.

### C3 — architecture is respected

- [x] No banned framework reference in any `Domain/` folder — **verified by running the NetArchTest suite (13/13)**, not by eye; corroborated by a grep of `src/Orders/Domain/` for `EntityFrameworkCore|Confluent|NATS.|MongoDB.|AspNetCore|System.Text.Json|OrderToCash.Contracts` returning nothing.
- [x] No cross-service database access. This feature adds no persistence code at all; `design.md` §8 designs the mapping and builds none of it.
- [x] No shared runtime code beyond `src/SharedKernel` and `src/Contracts`. `src/Orders/Domain/` references `SharedKernel` only; `Orders.UnitTests` additionally references `Contracts`, which is a **test** project and is explicitly permitted by `design.md` §11.4 — with an in-file comment saying so.
- [x] `src/SharedKernel` still has zero `PackageReference` (its own architecture test is in the green 13).
- [x] **No `decimal` in domain arithmetic.** A grep of `src/Orders/Domain/` for `decimal|float|double` returns nothing at all. The single cast in the folder is `((int)status)` in `OrderStatuses.DescribeUndefinedValue` — an enum-to-underlying cast for an error message, not a money value. **No narrowing cast on money anywhere**, which after feature 44 is a `CLAUDE.md`-level rule and not a style preference. `Money.MinorUnits` stays `long` end to end and `design.md` §8.5 records the columns as `bigint`.
- [x] Kafka-fact / NATS-RPC classification: N/A — this feature performs no inter-service interaction. It *fixes* which transitions bear a fact, which §5 checks against #7.
- [x] No stray debug logging, no context-free TODOs (grep clean).

### C4 — verification is real

- [x] `./quality.sh` passes — exit 0, 153 s, my own run.
- [x] **Domain tests are pure.** `Orders.UnitTests.csproj` has exactly four packages — `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`. **No mocking library**, no Testcontainers, no `DbContext`, no broker. The suite runs in **77–93 ms** with no container runtime, which is itself the evidence.
- [x] Integration tests still use real Testcontainers — unchanged by this feature and green in my `quality.sh`.
- [x] Coverage: Orders domain **88.1%** (830/942 lines), ≥ 80%. Overall gate remains inert by design until feature 34 — stated in `quality.sh` itself, not faked.
- [x] No Jest anywhere.

### C5 — the session closed cleanly

- [x] No suspicious untracked files. Every `??` entry is a legitimate artefact of feature 13 or feature 44; `TestResults/` is `.gitignore`d (`.gitignore:12`); no `*.tmp`, no build output outside the ignores. My six probe backups live in the session scratchpad, outside the repository.
- [x] `progress/history.md` has an entry for this feature **including its effort record** — appended by me as part of this approval (§8).
- [x] `feature_list.json` reflects the true state — set to `done` by me, and by me alone (the single-writer rule added to `CLAUDE.md` today).
- [x] The human has been told what was done and how to test it manually — `progress/impl_orders_aggregate.md` §"How to test this by hand".
- [x] **Claude did not commit.** `git log` still ends at `27f0dc6`; the tree is dirty and stays that way.

### C6 — Spec-Driven Development (first walk in this repository)

- [x] `specs/orders_aggregate/` holds all three of `requirements.md` (128 lines), `design.md` (546 lines), `tasks.md` (92 lines).
- [x] `requirements.md` uses strict EARS and every requirement carries an `R<n>` id. It is a **pointer document**: R5–R10 are reproduced verbatim from `specs/shared/requirements.md` §1 with only line-unwrapping, and §3 says outright *"This feature introduces no new `R<n>`"*. I spot-checked R5, R7 and R10 against the shared file — word for word identical.
- [x] **Every task in `tasks.md` verified genuinely done, not read as ticked.** 52 boxes, 0 unticked. The walk is §3 below.
- [x] Every `R<n>` covered by a concrete, named, non-vacuous test, recorded in `specs/shared/test-matrix.md`. The mapping is §7; the non-vacuity is established by the probes in §4, not by reading the assertions.
- [~] **The spec commit precedes the implementation commit.** *Unsatisfiable by any agent in this repository* — Claude never runs `git commit`, so nothing here can order two commits that do not exist. `specs/orders_aggregate/` is untracked, as is the implementation. The human must commit `specs/orders_aggregate/` **first**, then the implementation, and the box becomes `[x]` at that moment. Marked `[~]` exactly as #7's own review of this same feature marked it.

### C7 — spec-reuse fidelity

Mostly N/A at this phase (no n8n run, no API script, no README benchmark section to check for a domain-only feature). Two boxes **are** live and both pass:

- [x] **`specs/shared/` still byte-identical to #7's except `test-matrix.md`'s Status column.** `diff -rq` against the #7 checkout reports **one** differing file: `test-matrix.md`. Its differences are the pre-existing #8 ones (the per-assessment `#7 mechanism` paragraph, whose own text licenses its deletion; the recomputed count table; #7's later R56/R58 amendment blocks that postdate the copy). **What feature 13 changed is exactly the six R5–R10 Status cells plus the two count rows** — `git diff` confirms columns 1–4 and every other row are untouched.
- [x] **The `R<n>` ids are #7's, and the .NET realisation genuinely satisfies the same requirement.** §5 checks this against #7's source rather than against #8's prose.

---

## 3. `tasks.md` — 52 boxes, walked

Not read as ticked. Each was confirmed against the artefact it names.

| Tasks | How I confirmed |
|---|---|
| 1.1–1.3 | `Orders.UnitTests.csproj` read: correct `RootNamespace`/`AssemblyName`, `IsPackable false`, four packages, no mocking library. In `OrderToCash.sln` **and** under the `tests` solution folder (`NestedProjects` line 309 — the impl report's claim, verified). `README_PLACEHOLDER.cs` present and unmodified (trap 8 avoided; its type is what `DomainAssemblies.cs` resolves the Orders assembly through). |
| 2.1–2.4 | Nine-member and three-member enums with explicit token tables, `StringComparison.Ordinal`, no `ToString().ToLower()`. `OrderStateMachine.LegalEdges` is a `FrozenSet` of eleven edges, each carrying its T-1 row number. |
| 2.5–2.7 | The three named tests exist and are green. 2.5 transcribes T-1 **from the specification** into a local `HashSet` and asserts `SetEquals` both ways plus `Count == 11` — trap 3 avoided. |
| 3.1 | Eleven error types. Ten carry **exactly** the `Code` strings of `design.md` §9.1 (checked one by one). The eleventh is declared — advisory A4. `OrderNotCancellableError : IllegalOrderTransitionError`, which is what lets the R9 test assert `ThrowsAny<IllegalOrderTransitionError>` over all 61 pairs without special-casing sixteen. |
| 3.2–3.8 | `OrderLine` sealed, `internal` ctor, every property get-only. `Order` — all setters `private`, `_lines` private with `IReadOnlyList` exposure, no `ApplyOrderDiscount`, no method takes an `OrderStatus`. Candidate-then-commit confirmed in all three mutators, freeze first in all three. |
| 3.9–3.13 | All five named tests present, asserting `Code` and not merely that something threw. |
| 4.1–4.2 | `OrderDomainEvent` + four sealed records; `Raise` wired into `Place`, `Confirm`, `Complete`, `Cancel` and **nothing else** — verified by reading all nine transition methods, and armed in §4. |
| 4.3–4.13 | All eleven named tests present and green. 4.6 asserts 72/11/61 explicitly. 4.7 asserts the second-`Cancel` refusal and empty steps for `stock_rejected`. 4.12 asserts against the real `FactCatalog`. |
| 5.1–5.4 | `Rehydrate` has **no totals parameters** (§below), bypasses the machine, raises nothing, re-derives all three totals, and performs all four §8.3 checks. Two of the four are untested — **D1**. |
| 5.5 | `OrdersDomainContractsTests.cs` present, correctly scoped to `^OrderToCash\.Orders\.Domain(\.|$)`, message names the offending types, green as rule 13 of 13. |
| 6.1–6.3 | Eleven arming rows recorded with verbatim messages and an explicit statement of the forced rebuild. Five re-armed by me and all five reproduced (§4). |
| 6.4–6.6 | Re-run by me: solution green, `quality.sh` exit 0, `init.sh` exit 0. |
| 6.7 | `git diff` on `test-matrix.md` — six Status cells and two count rows, nothing else. |
| 6.8–6.10 | Report present and unusually candid; feature set to `in_review`, not `done`; no commit was made. |

**The three checks the coordinator singled out:**

1. **The five silent edges emit nothing.** `MarkStockReserved`, `ApproveCredit`, `MarkDespatched`, `MarkInvoiced`, `MarkPaid` all pass `buildEvent: null`, and `TransitionTo` raises only `if (buildEvent is not null)`. The seven emitting edges are `Place`, `Confirm`, `Complete` and `Cancel`'s four sources. **Armed by me on a different edge from the implementer's** — probe P1 below.
2. **The snapshot carries no totals fields at all.** `Rehydrate`'s fourteen parameters contain no `initialAmount`, `initialDiscount` or `totalAmount`; totals come from `RecomputeTotals(orderedLines, currency)` on the same private routine `Place` uses. There is no `order.totals_inconsistent` code and no place to put a stored total. Matches #7's `OrderSnapshot`, whose own header comment says *"Carries **no totals fields** (OA3)"* — verified in #7's file, not taken from the design's prose.
3. **No narrowing cast on money, no `decimal`/`float`/`double` in the domain.** Grep-clean (C3 above).

---

## 4. My own arming table

Protocol, every row: copy-backup taken **before** anything (`scratchpad/backup/Order.cs.bak`, md5 `366bc060b48e3095aa18f2def04fd472`); mutate; `dotnet build --no-incremental`; run the named test; record the failure verbatim; **restore from the backup copy, never `git checkout --`**; `touch` the restored file; `dotnet build --no-incremental` again; confirm 24/24 green; re-read the restored lines and re-`md5sum` against the backup. Six probes, twelve forced full rebuilds.

| # | What I broke | Mutation | Test that fired | Verbatim failure |
|---|---|---|---|---|
| **P1** | **A silent edge emits nothing** (suppression direction — the one people forget). Deliberately a **different** edge from the implementer's row 5. | `MarkDespatched` given a real `buildEvent` raising `OrderConfirmed` | `OrderEventsTests.O8_Order_AppendsNoDomainEventOnTheFiveSilentEdgesOfTableT1` | `Assert.Empty() Failure: Collection was not empty` / `Collection: [OrderConfirmed { ... EventType = order.confirmed.v1 ... }]` |
| **P2** | **O4 line-freeze precedes the structural check** (invariant, arming row 8) | `EnsureLinesMutable()` moved *below* the O1 candidate-count check in `RemoveLine` | `OrderTests.R7_Order_RefusesToAddRemoveOrModifyALineOnceTheOrderIsConfirmedAndLeavesEveryFieldUnchanged` | `Assert.Throws() Failure: Exception type was not an exact match` / `Expected: typeof(OrderLinesAreFrozenError)` / `Actual: typeof(OrderMustHaveAtLeastOneLineError)` at `OrderTests.cs:line 147` |
| **P3** | **O3 candidate-then-commit** (invariant; a mutation the implementer did *not* run — its row 8 covers guard *order*, not the commit *point*) | the negative-total check moved to **after** the commit block in `CommitCandidateLines`, i.e. recompute-then-validate | `OrderTotalsTests.R6_Order_RejectsAMutationWhoseResultingTotalAmountWouldBeNegativeAndLeavesTheOrderUnchanged` | `Assert.Equal() Failure: Values differ` / `Expected: 100 EUR` / `Actual: 150 EUR` |
| **P4** | **O6 reason↔status pairing** (invariant, #7's OA4). Deliberately the *removal* direction rather than the implementer's single-arm deletion. | `_ => false` changed to `_ => true` in `IsReasonApplicable` — the pairing rule removed entirely | `OrderCancellationTests.R10_Order_RefusesACancellationReasonTableT1DoesNotPairWithTheCurrentStatus` | `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(CancellationReasonNotApplicableError)` |
| **P5** | **`Complete` raises `OrderCompleted`** (emission deletion — the row easiest to fake, since deleting a `Raise` on a method nothing calls yet costs nothing) | `buildEvent: null` on `Complete`, with the builder parked in a dead private method so the mutation was a genuine emission deletion and not a compile error | `OrderEventsTests.O8_Order_AppendsExactlyOneDomainEventForEachFactBearingEdgeOfTableT1` | `Assert.Single() Failure: The collection was empty` |
| **P6** | **`Rehydrate`'s O1 and O2 validations** — a probe nobody asked for | both the `lines.Count == 0` check and the whole `EnsureLineCurrencyMatches` loop **deleted** from `Rehydrate` | *(none)* | **SURVIVED — 24/24 still green.** This is **D1**. |

Five of five re-armed guards fired, each in the named test the design predicted, and the sixth probe found the one real gap. Final state after the last restore: `Order.cs` md5 identical to the pre-probe backup, `sed`-confirmed on the restored lines, `dotnet build --no-incremental` clean, **24/24 + 13/13 green**, `dotnet format --verify-no-changes` clean.

---

## 5. Did the implementation follow #7's citations, or quietly improve on them?

`design.md` cites #7 by file and line throughout, and those citations are the evidence that a decision was *inherited* rather than invented. I read them in #7's checkout rather than trusting the design's paraphrase. All four load-bearing ones hold:

| Citation in `design.md` | What #7 actually contains | #8 |
|---|---|---|
| §7.4 — `order-transitions.ts` sets `emits: null` on five edges (lines 36, 42, 54, 60, 66) | Confirmed exactly: `emits` is `null` at **36, 42, 54, 60, 66** and carries a fact type at 30, 48, 72, 79, 86, 92, 98 — seven emitting, five silent | `buildEvent: null` on the same five; `TransitionTo` gates on `buildEvent is not null`. **Same behaviour, same direction of guard.** |
| §6.1 — `order.ts:413-419` refuses `stock_rejected` unless `placed` and `credit_rejected` unless `stock_reserved` | Confirmed, and so is the **order** of checks: reason-in-closed-set, then `findTransition` (→ `OrderTransitionNotAllowedError`), then the two pairing `if`s | `Cancel` checks `IsLegal` (→ `OrderNotCancellableError`), then `IsReasonApplicable`; the closed-set check moves to `CancellationReasons.Parse` because a C# `enum` parameter cannot be out of set. **Same rule, same order, the one difference forced by the language and recorded in §6.2.** |
| §4.4 — `order-totals.ts:25`, `const orderDiscount = Money.zero(currency)` | Confirmed, including the comment explaining that the term stays in the formula so a future addition has an obvious home | `RecomputeTotals` writes `var orderDiscount = Money.Zero(currency);` into the sum. No field, no setter, nothing to persist. **Followed, not improved.** |
| §8.3 — `order-snapshot.ts` carries no totals, `order.ts:215` re-derives | Confirmed: the snapshot header reads *"Carries **no totals fields** (OA3)"* and `reconstitute` calls `computeOrderTotalsFor` | `Rehydrate` has no totals parameters and calls the same private `RecomputeTotals`. **Followed exactly.** |

I also read #7's `reconstitute` in full and compared it to `Rehydrate`: **the same four checks in the same order** — status in the closed set, lines non-empty, reason-iff-cancelled with each half refused separately. That is the level of fidelity the benchmark needs.

**Three deliberate divergences, all declared, none silent:**

1. `OrderStateMachine` is `public` where §3.1's snippet shows `internal` — because the transcription test asserts against `LegalEdges` from another assembly and `InternalsVisibleTo` would have opened `Infrastructure/` too. Recorded in the impl report *and* in a `<remarks>` block on the type. Sound: it exposes read-only data and weakens nothing about `Status`'s single writer.
2. `OrderLine` is replaced rather than mutated by `ChangeLine`, where #7 mutates in place. Behaviourally identical, structurally stricter, declared in the impl report with the reason.
3. `OrderLineRequest` is a new type not in §1's file layout, introduced because `OrderLine`'s constructor is `internal` and `Place` is a public entry point. Declared, and the type's own XML doc says so.

None of the three is an *improvement on #7 smuggled in as a fix*. The one place #8 is genuinely stricter than #7 — `Rehydrate` raising the aggregate's own `OrderLineCurrencyMismatchError` where #7's `reconstitute` lets the kernel's error escape, which was **#7's own review defect D3** — is stricter by the design's explicit instruction (§8.3 lists O2 among the four checks), so it is inherited-and-corrected rather than divergent. It is also the check my P6 probe found unguarded.

---

## 6. Scope

| Boundary | Result |
|---|---|
| `src/Orders/Infrastructure/**` unmodified by this feature | **Yes.** The only diff under it is feature 44's `int → long` on five properties across two entity files, already reviewed and closed in `progress/review_money_column_width.md`. Nothing feature 13 touched. |
| `src/Orders/Application/**` unmodified | **Yes** — no diff, no new file. No `IOrderRepository`, no handler, no port. §8 and §10 of the design remain designed-and-not-built, exactly as #7 drew the line. |
| Migration, seed, `SharedKernel/`, `Contracts/` untouched | **Yes** — no diff attributable to this feature. `Orders.csproj` and `Directory.Packages.props` are unmodified, so **no package was installed** by this feature. |
| `src/Orders/Domain/README_PLACEHOLDER.cs` survives | **Yes**, byte-unchanged (mtime 06:28, days older than the feature). |
| `tests/Architecture.Tests/` — one new rule only | **Yes.** One new file, no change to the other five files or to the `.csproj`. |
| `OrderToCash.sln` | One project added, with its twelve configuration lines and its `NestedProjects` entry under `tests`. Nothing else. |
| `specs/shared/test-matrix.md` | Six Status cells + two count rows. Columns 1–4 and every other row byte-identical. |
| Counts derive from column 5 | **Yes.** Feature-1 row `10 / 9 / 1 / 0` — R1 is the ratified scoped deferral (its cell still reads `DOMAIN HALF DONE … Scoped deferral ratified`), R2–R10 green. Total `63 / 9 / 1 / 53`, and 9 + 1 + 53 = 63. Arithmetic checked, not assumed. |

---

## 7. R5 – R10 traceability

Every row below was confirmed three ways: the test exists with that exact name, it is green, and — for the invariant it carries — a mutation of the production code makes it fail.

| Id | Named test(s) | Non-vacuity evidence |
|---|---|---|
| **R5** (O1) | `OrderTests.R5_Order_RefusesToCreateAnOrderWithNoLinesAndToRemoveTheLastRemainingLine` | Asserts `Code == "order.must_have_at_least_one_line"` in **both** legs and that the surviving line is still there. Not a bare `Assert.Throws`. |
| **R6** (O3) | `OrderTotalsTests.R6_Order_RecomputesInitialAmountInitialDiscountAndTotalAmountAfterEachMutation`, `OrderTotalsTests.R6_Order_RejectsAMutationWhoseResultingTotalAmountWouldBeNegativeAndLeavesTheOrderUnchanged` | Asserts all three totals after place, add, change **and** remove, with real arithmetic (2000/100/1900 → 3500/150/3350 → 2200/100/2100 → 2000/100/1900). The rejection leg asserts seven fields including `DomainEvents.Count` and `UpdatedAt`. **Probe P3 killed it.** |
| **R7** (O4) | `OrderTests.R7_Order_RefusesToAddRemoveOrModifyALineOnceTheOrderIsConfirmedAndLeavesEveryFieldUnchanged` | All three mutators × all six frozen statuses, asserting `Code` and five unchanged fields — plus the single-line sub-case where O1 and O4 are both violated and R7 says the frozen error wins. **Probe P2 killed it.** |
| **R8** | `OrderStateMachineTests.R8_Order_WalksEveryLegalEdgeOfTableT1`, `…R8_Order_ReachesCancelledOnlyFromPlacedStockReservedCreditApprovedAndConfirmed`, `…R8_Order_TreatsCompletedAndCancelledAsTerminal` | The walk starts at `Order.Place` and uses real transitions, never `Rehydrate`. The cancel test proves the four legal sources succeed and five sources raise `order.not_cancellable`. Terminality is 2 × 8 = 16 refused attempts. |
| **R9** | `OrderStateMachineTests.R9_Order_RaisesOnEveryFromToPairAbsentFromTableT1WithoutMutatingStateOrAppendingAnEvent` | All 72 pairs, with 72/11/61 asserted explicitly so a tenth status cannot slip in untested, and for each of the 61: throws, `Status` unchanged, `DomainEvents.Count` unchanged, `UpdatedAt` unchanged. The legal set is transcribed locally, not read from `LegalEdges`. |
| **R10** (O6) | `OrderCancellationTests.R10_Order_RequiresAReasonFromTheClosedSetRecordsItImmutablyAndCarriesItOnOrderCancelledV1`, `…R10_Order_RaisesWhenNoCancellationReasonIsSuppliedAndDoesNotChangeTheStatus`, `…R10_Order_RefusesACancellationReasonTableT1DoesNotPairWithTheCurrentStatus` | The reason lands on the aggregate **and** on the event, immutability is proved by the refused second `Cancel`, compensation steps are asserted empty for `stock_rejected` and non-empty for `credit_rejected`, and the pairing test asserts both legal pairings directly as well as the six refusals. **Probe P4 killed it.** |

Design-guard tests with no matrix row, all present and all green: `LegalEdges_Are_Exactly_The_Eleven_Status_To_Status_Edges_Of_Table_T1`, `OrderStatuses_And_CancellationReasons_RoundTrip_Their_Wire_Tokens`, `R10_CancellationReasons_Parse_RaisesWhenTheTokenIsMissingOrOutsideTheClosedSet`, `O2_Order_RefusesALineWhosePriceOrDiscountIsNotInTheOrdersCurrency`, `Order_RemoveLineAndChangeLine_RaiseOrderLineNotFoundErrorForAnUnknownLineId`, the six in `OrderEventsTests`, the four in `OrderRehydrationTests`. **24 cases total, matching the claim.**

---

## 8. Defects

### D1 (non-blocking) — `Rehydrate`'s O1 and O2 validations survive their own deletion

**File:** `src/Orders/Domain/Order.cs:338-346`. **Found by probe P6, not by reading.**

`Rehydrate` performs the four checks `design.md` §8.3 requires and `tasks.md` 5.1 ticks. **Two of them are guarded by nothing.** Deleting both the `lines.Count == 0` check and the entire `EnsureLineCurrencyMatches` loop leaves the suite at **24/24 green**. `tasks.md` 5.4 asks only for the status token and the two O6 halves, and that is exactly what `Order_Rehydrate_RefusesAStatusTokenOutsideTheClosedSetAndAReasonThatDoesNotMatchTheStatus` asserts, so the task list is honestly ticked — the gap is in the task list as much as in the tests.

**Why it matters, and why it is not blocking.** R5 and O2 are themselves well covered on the `Place`/`AddLine`/`RemoveLine` paths, so no requirement is unproven. But `Rehydrate` is the path **every** load takes from feature 15 onwards, and its currency check is precisely the hole #7's own reviewer raised as its defect **D3** (*"`Order.reconstitute` lets the kernel's `CurrencyMismatchError` escape instead of the aggregate's own … a real O2 hole on the path feature 15's adapter will use"*). #8 wrote the check #7 lacked and then did not arm it, so the inherited lesson is half-learned: the code is right and the guard is absent, which is the shape that lets the code silently become wrong later. It is not blocking because it is not a fact-emission branch (CLAUDE.md's mandatory arming rule does not reach it), no `R<n>` is left unproven, and `Rehydrate` has no live caller.

**Required before feature 15 closes** — not before this one: two cases on `OrderRehydrationTests`, one refusing an empty `lines` collection, one refusing a line whose `unitPrice` or `lineDiscount` is not in the order's currency and asserting `order.line_currency_mismatch` rather than `money.cross_currency`. Carry this into feature 14/15's brief.

### Advisories

**A1 — the impl report's test total is wrong.** `progress/impl_orders_aggregate.md:246-249` states *"178 tests, 0 failures"*. The true figure is **191**, and the report's own per-project breakdown (32 + 21 + 24 + 34 + 13 + 7 + 13 + 19 + 23 + 5) sums to 191. Each individual number is correct; only the total is not. Relatedly, the coverage line reports *"415/471 lines = 88.1%"* where the cobertura file gives **830/942** — the same ratio, half the counts. Neither error changes any conclusion, but a report whose headline number disagrees with its own table is exactly the artefact a later phase will quote.

**A2 — `CancellationReason` is assigned outside `TransitionTo`.** `src/Orders/Domain/Order.cs:239-255`. `design.md` §6.1 says the property is *"assigned exactly once inside `TransitionTo`'s accepted branch"*, and #7 does it inside, via `propsPatch: { cancellationReason: reason }`. #8 assigns it on the line **after** `TransitionTo` returns. There is no observable difference today — `TransitionTo` throws on a refused edge, and the event builder closes over the `reason` parameter rather than reading the property — but between the two statements the aggregate is momentarily `Cancelled` with a null reason, a state O6 says cannot exist, and any code added to the tail of `TransitionTo` would observe it. Cheap to move; worth moving before feature 16 adds anything to that method.

**A3 — a business error code reused for a load-time corruption.** `src/Orders/Domain/Order.cs:353-356`. `Rehydrate` raises `CancellationReasonNotApplicableError` (`order.cancellation_reason_not_applicable`) when a non-cancelled row carries a reason. `design.md` §9.1 defines that code as *"the reason does not pair with the current status per T-1's Trigger column"* — a refusal of a **cancellation request** — and §9.2 has feature 41 mapping the cancel-path errors to client-facing outcomes. #7 keeps the two apart: all three load-time refusals raise `InvalidOrderSnapshotError` (`ORDER_SNAPSHOT_INVALID`). As written, a corrupt stored row could surface to a caller as a business rejection of its own cancel request. Not blocking — `Rehydrate` has no caller yet — but features 14/15/41 should settle it before they branch on `details.code`.

**A4 — an eleventh error type where §9.1 fixes ten.** `src/Orders/Domain/Errors/UnknownOrderStatusError.cs` (`order.status_unknown`) is not in the design's table. It is **declared prominently** in the impl report and it realises a check §8.3 does mandate without naming its error, and #7 has the equivalent check with its own error type — so it is realisation, not re-decision, and I accept it. But §8.3's own argument against minting codes #7 lacks (*"a code #7 does not have would be a code features 15, 41 and 42 would have to branch on in only one of the two assessments"*) applies here in weakened form, and §9.1's table is now one row short of the code. Add the row when feature 15 first branches on the codes.

**A5 — the spec-outranks-brief precedent is recorded in only one place.** The conflict between the invocation brief and `tasks.md` §5.5 is described fully and accurately in `progress/impl_orders_aggregate.md` (top note plus §"What could not be done, and why"). `tasks.md` now carries no trace of it, which is right for a task list — a task list should end in the state "done". But the **precedent** is a process ruling of the kind this repository keeps in `docs/PROCESS.md`, whose Phase 8 entry currently records only the `feature_list.json` race. I have put it in `progress/history.md` as part of this approval; promoting it to `docs/PROCESS.md` is the leader's call and its file.

**On the process call itself, since I was asked to judge it: the implementer was right and so was the resolution.** Faced with a gate-approved `tasks.md` that named `tests/Architecture.Tests/` in its own "files this feature may touch" preamble and a brief that forbade it, the implementer stopped, left **both** files untouched, un-ticked the two boxes with an inline note and reported the conflict — rather than obeying the brief and shipping an incomplete feature, or obeying the spec and hiding a violated constraint. That is the correct behaviour and it is cheap: one round-trip. The alternative failure modes are both expensive and both silent. The resolution — the gate-approved spec outranks a leader's brief, because the brief's constraint list was not authorised by any gate — is the right precedent and the one worth having on the record. The record of it is adequate for this feature and thin for the repository, which is A5.

---

## 9. Benchmark reading — the reuse saved gate time, not implementation time

#7's own history entry for this feature: **~1.5h**, spec ~0.5h, implementation ~32 min, review ~25 min, **16 open points at its gate, 2 post-gate amendments and 1 addition**, which grew its `tasks.md` from 38 to 44 tasks; approved first pass with 6 minor defects and one surviving mutation probe.

#8: implementation ~0.7h (10:18 → 11:00, two passes), review ~0.43h, **zero open points survived to the gate**, zero amendments, zero additions, 52 tasks; approved first pass with 1 non-blocking defect and five advisories. My six probes: 5 killed, 1 survived.

**Does the evidence support the reading that what reuse saved here was gate time rather than implementation time? Yes, and specifically.** #7's 16 open points and #8's zero is not a difference in diligence — `requirements.md` §3 and §6 list the questions #8's spec pass actually raised, and they are **the same questions**: how to read O8 against T-1, the missing `order_discount` column, the missing ordering column, the missing version column, the totals-free snapshot, the reason↔status pairing. Every one of them was closed **on evidence from #7's code or its history file**, each with a file and line, before reaching the human. That is the whole mechanism, and it is visible in the artefact rather than asserted. The two questions that genuinely could not be closed that way — an `enum` parameter cannot be absent, and typestate would cost a nine-way switch on every load — are exactly the two where C# differs from TypeScript, and both were resolved inside the design.

Implementation time, by contrast, was **not** saved: ~0.7h against #7's ~32 min, roughly 1.3× slower, on a feature whose design was handed over complete. This is the sixth feature in a row to report some version of *the spec is free, the proof is not* — and it is a much better result than feature 12's 2.3×, because here the inherited artefact was a **design with citations**, not a dataset. The citations are what made the gate cheap, and they are the thing #9 should ask for.

**What it cost.** The spec pass itself was long in elapsed terms (`requirements.md` created 06:02, `design.md` finalised 09:58, interleaved with the human gate and with the parallel `money_column_width` correction), and part of that elapsed time was spent re-checking #7's code to close points that could have been carried to the gate in minutes. That trade was worth taking here — a gate round-trip costs a human — but it should be recorded as a cost and not as free.

---

## 10. Closure

- `feature_list.json`: feature 13 `in_review` → **`done`** (written by me, and by nothing else concurrently, per the single-writer rule).
- `progress/history.md`: entry appended with the effort record.
- **No commit made.** The human commits `specs/orders_aggregate/` **first**, then the implementation, which is what turns C6's last box from `[~]` into `[x]`.
