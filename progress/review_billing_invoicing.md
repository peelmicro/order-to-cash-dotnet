# Review — `billing_invoicing` (id 21, phase 10)

**Verdict: REJECTED** — 3 blocking defects, 6 non-blocking findings, 1 advisory. Two of the three blocking defects are payload-corruption survivals on a fully green suite: the exact class `F9` exists to catch, in branches `F9` did not enumerate.

**Reviewed:** `CLAUDE.md` **on disk** (which carries three clauses absent from the injected copy — L90 the ledger-guard arming rule, L182 the provenance/expected-value clause, L191 the negative-claim enumeration rule; all three bear on this review), `progress/impl_billing_invoicing.md` including the coordinator's appended `H1`–`H4` section, `progress/spec_billing_invoicing.md`, `specs/billing_invoicing/{requirements,design,tasks}.md`, `CHECKPOINTS.md`.

---

## 1. What I ran, and what I did not

Per `CLAUDE.md`'s *"probe the claims, do not re-run the world"*: I re-ran in full only where the claim under test is about a full suite, and probed the rest.

| Run | Result | Why |
|---|---|---|
| `dotnet test tests/Billing.UnitTests` | **199 / 199** | Confirms the report's figure exactly |
| `dotnet test tests/Orders.UnitTests` | **280 / 280** | Confirms the report's figure exactly |
| `dotnet test tests/Billing.IntegrationTests` (full, real Testcontainers) | **74 / 74**, 4 m 7 s | Run **with probe 4's mutation in place** — see D2 |
| `dotnet test tests/Architecture.Tests` | **16 / 16** | C3 domain purity, run not eyeballed |
| `dotnet build OrderToCash.sln --no-incremental` | succeeded, 0 warnings | Forced rebuild after every restore |
| `./init.sh` | **exit 0** | 30/55 done, backlog tripwire clean, lockstep clean |

**Not re-run:** `./quality.sh` end to end. The implementer's claim about it is a full-suite claim and I did not duplicate it; I ran four of its thirteen projects independently, plus an independent `--no-incremental` solution build and `diff -rq specs/shared` against the #7 checkout. The coverage figures (88.6 % Billing domain, 93.9 % Billing overall) are the implementer's, unverified by me — they are not load-bearing for this verdict.

**Six mutation probes of my own**, across both mutation families, each with a backup copy taken, `touch` + forced rebuild before the failing run, restore verified by `cmp` against the backup **and** by re-reading the changed line, and a forced `--no-incremental` rebuild before the confirming green run. No `git checkout --` was used on anything.

| # | Family | Mutation | Result |
|---:|---|---|---|
| 1 | structural | `InvoiceState.cs:56` `private protected` → `protected` | **KILLED** — `InvoiceStateTests.BI23_…` › `constructor Void .ctor() must be private protected (IsFamilyAndAssembly).` |
| 2 | corruption | `Invoice.cs:188-189` swap `RetailerCode`/`CompanyCode` on the `InvoiceIssued` raise | Unit suite **199/199 green**; **KILLED** at integration — `InvoiceIssueTests.R45_…` › `Expected: "CarrefourEs" / Actual: "IBERFOODS"` at `InvoiceIssueTests.cs:90` |
| 3 | corruption | `Invoice.cs:335-336` `ValueDate` → `+1 day`, `Source` → `"ARM-CONSTANT-SOURCE"` on the `PaymentReceived` raise | **SURVIVED — 199/199 green. → D1** |
| 4 | corruption | `BillingRpcResponder.cs:261` `request.Discount` → `0L` | **SURVIVED — 199/199 unit AND 74/74 integration green. → D2** |
| 5 | deletion | `SagaCommandRequestFactory.cs:55` `order.InitialDiscount.MinorUnits` → `0L` | **KILLED** — `SagaCommandPayloadTests.BI21_…`, 1 failed / 279 passed |
| 6 | language | the `abstract record` sketch of `design.md` §3.2, compiled and reflected over in a scratch project | See §3 — the sketch **compiles**; its synthesised copy constructor is `IsFamily` (protected), not `IsFamilyAndAssembly` |

---

## 2. `CHECKPOINTS.md` — every applicable box, walked

### C1 — The harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 — run, exit code read.

### C2 — State is coherent

- [x] At most one feature `in_progress` — zero were, id 21 being `in_review`; restored to `in_progress` by this verdict.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [x] `progress/current.md` describes the active session.
- [x] Every `blocked` feature records why — none are blocked.

### C3 — Architecture is respected

- [x] No framework reference inside any `Domain/` folder — **NetArchTest suite run, 16/16 green**, plus an independent `grep -rln` over `src/*/Domain/` for the six banned namespaces returning zero files.
- [x] No cross-service database access — Billing's new code reads only `otc_billing`; `RetailerCode`/`CompanyCode`/`OrderReference` are carried as business identifiers, no FK crosses a boundary.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts` and `src/Cqrs` — `Billing.csproj` references exactly those three.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — grep clean, and the architecture test covers it.
- [x] `src/SharedKernel` still has zero `PackageReference` entries — the one `grep -c` hit is inside the explanatory comment; the file has no entry.
- [x] No `decimal` in domain arithmetic — `grep -rn "decimal" src/Billing/Domain/` returns zero lines; `Money` is `long` minor units throughout, and `grep -n "(int)"` across the four new persistence/domain files on the units path returns zero (ledger `L6`).
- [x] Every interaction classifiable — `billing.invoice.issue` / `billing.invoice.list` are NATS RPC, `invoice.issued.v1` / `payment.received.v1` are Kafka facts through the outbox. `BI1`'s host-level assertion proves Billing registers no consumer.
- [x] No stray debug logging, no context-free TODOs in the new code.

### C4 — Verification is real

- [ ] **`./quality.sh` passes** — not re-run by me (see §1). The implementer reports exit 0 over the whole solution. **Left unmarked deliberately**: it is the one C4 box I did not independently observe, and D1/D2 show the suite being green is not the question that matters here.
- [x] Domain tests are pure — `InvoiceTests`, `InvoiceStateTests`, `InvoiceFactTests` reference no framework, no DB, no broker.
- [x] Integration tests use Testcontainers against real MsSql / Kafka / NATS — I ran all 74 against live containers.
- [ ] **Coverage thresholds met** — the implementer's figures, not re-derived by me. Not blocking, but not verified.
- [x] No Jest anywhere.

### C5 — The session closed cleanly

- [x] No suspicious untracked files — `git status` shows 88 changes, all attributable to feature 20 (uncommitted) or feature 21.
- [ ] **`progress/history.md` has an entry for the feature just finished, including its effort record** — absent, correctly: the feature is not finished. Not counted against the implementer.
- [x] `feature_list.json` reflects the true state — after this verdict's single-line edit.
- [x] The human has been told what was done and how to test it manually — `impl_billing_invoicing.md` carries a runnable recipe; note N3 about its internal contradiction.
- [x] Claude did not commit.

### C6 — Spec-Driven Development

- [x] `specs/billing_invoicing/` has all three of `requirements.md`, `design.md`, `tasks.md`.
- [x] `requirements.md` uses EARS notation, every requirement carrying a `BI<n>` id; shared `R45`/`R46` are referenced rather than restated, per the file's own preamble.
- [ ] **Every task ticked `[x]`** — all 59 boxes carry `[x]`, but `H1`–`H4` are ticked **over text that still says "NOT PERFORMED this session"**. The box is ticked; the sentence under it says the opposite of the truth. `tasks.md`'s own preamble calls that out by name: *"A tick over stale text is the failure mode."* See N1.
- [ ] **Every `R<n>` covered by at least one concrete named test** — `BI22`'s row in `requirements.md:163` still reads `TODO — NOT performed this session`, while `impl_billing_invoicing.md:339` claims it was flipped. See N2. Every other `BI<n>` and both shared rows check out — mapping in §5.
- [x] The spec commit precedes the implementation commit — `specs/billing_invoicing/` and the code are both uncommitted in one working tree; the human commits the spec first per the repository's history discipline.

### C7 — Spec-reuse fidelity and benchmark honesty

- [x] **`specs/shared/` still byte-identical to #7's except `test-matrix.md`** — verified with a real `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared`: **`test-matrix.md` is the only file that differs**. The residual non-Status differences inside it (#7's later `R56` ratification and its per-assessment mechanism paragraph) predate this feature and are #7's own post-copy evolution, not a #8 fork.
- [x] Every deviation is a recorded amendment — this feature amends nothing under `specs/shared/` beyond `test-matrix.md`'s Status column and counts, exactly as `H5` scopes it. Verified from the diff.
- [x] The `R<n>` ids are #7's — `R45`/`R46` genuinely satisfied, see §5.
- [ ] `n8n/workflows/*.json` unchanged and green against the .NET Gateway — **not applicable**, no Gateway yet (feature 25).
- [ ] The black-box API script proves the same saga steps — **not applicable**, feature 28.
- [ ] **`progress/history.md` effort records complete and honest** — no entry for this feature yet; correct while rejected.
- [ ] The README's benchmark section — not this feature's, unchanged.

---

## 3. The invoice-state closure, and the deviation — verified independently

**The language claim is substantially right and its wording is slightly wrong.** I built the `design.md` §3.2 sketch verbatim in a scratch `net10.0` project. It **compiles cleanly** — so the report's *"That sketch does not compile with the intended accessibility"* overstates it. What is true, and is the whole of the argument, is that the *intended accessibility cannot be expressed*: reflecting over the compiled record gives

```
Void .ctor()                  | IsFamilyAndAssembly=True   IsFamily=False
Void .ctor(T.InvoiceState)    | IsFamilyAndAssembly=False  IsFamily=True     ← the synthesised copy constructor
```

and attempting to declare that copy constructor `private protected` gives `error CS8878: A copy constructor 'S.S(S)' must be public or protected because the record is not sealed.` A `protected` copy constructor is reachable from a derived type in **any** assembly, so an external assembly holding a leaked `InvoiceState` could add a third case via `base(original)` — precisely the closure `BI23` exists to rule out. The plain-class rendering has no synthesised member, so the explicit `private protected InvoiceState()` really is the only constructor. **The deviation is correct and the property is strengthened, not weakened.** Wording nit only, recorded as A1 below.

**The guard is armed on the FINAL rendering, not only against the abandoned sketch.** Probe 1 above widened `private protected` → `protected` in the shipped `src/Billing/Domain/InvoiceState.cs:56` and `InvoiceStateTests.BI23_…` failed with the exact message the report records. Restored, `cmp`-identical, forced rebuild, 199/199 green. The nested `Issued`/`Paid` cases are `sealed`, so their implicitly-public constructors cannot widen the hierarchy — closure holds.

## 4. The rename, and its stop condition — verified from the diff, not the claim

`E4`'s condition is *"the three existing responder test files are green afterwards with **no assertion changed**"*. I read `git diff` on each of the three rather than trusting the report. **The complete set of changed lines is nine, and every one is a type-name token:**

- `CreditResponderConcurrencyTests.cs` — 3 lines: `CreditRpcResponder` → `BillingRpcResponder`, `CreditResponderOptions` → `BillingResponderOptions`, the logger's generic argument.
- `CreditResponderHeaderTests.cs` — 2 lines, both `CreditRpcResponder.DispatchAsync` → `BillingRpcResponder.DispatchAsync`.
- `CreditResponderShutdownTests.cs` — 4 lines, the same three plus `typeof(CreditRpcResponder)` → `typeof(BillingRpcResponder)` in the `_inFlight` reflection.

**Zero assertion text changed, zero expected value changed, zero case renamed.** The stop condition holds. `BI31`'s own guard (`BillingResponderSubjectCoverageTests`) asserts the concrete `IHostedService` set is **exactly** `{OutboxRelayBackgroundService, BillingRpcResponder}` from the real built host, plus a source scan for all five subject constants inside `ExecuteAsync` — the container half is the strong one and it cannot pass with a second responder registered.

## 5. `R<n>` → test mapping verified

| Requirement | Named test | Verified how |
|---|---|---|
| **R45** (shared) | `tests/Billing.UnitTests/InvoiceTests.cs` › `R45_…_TheAggregateHalf`; `tests/Billing.IntegrationTests/InvoiceIssueTests.cs` › `R45_…_TheIntegrationHalf` | Both run green; probe 2 killed by the integration half at `InvoiceIssueTests.cs:90`, so it genuinely reads the published payload |
| **R46** (shared) | `tests/Billing.UnitTests/InvoiceTests.cs` › `R46_AllowsOnlyTheTransitionFromIssuedToPaid_…` | Runs green; **but see D1** — it asserts the fact's count and type and one field, not the payload |
| **R40** (already DONE, first live caller) | `InvoiceRepositoryTests.BI7_…` + `InvoiceIssueTests.R45_…`, whole-table outbox delta | Live evidence confirms neutrality: `consume` rows written for ORD-000007…10 and ORD-000013, `credit.released.v1` count unchanged at 6 |
| **BI1** | `BillingConsumesNoFactsTests.BI1_…` | Read; asserts the exact `IHostedService` set from the real host |
| **BI2** | `InvoiceResponderValidationTests.BI2_…` (entry observed, dispatcher never called) + `InvoiceIssueTests.BI2_…` | Read; the unit half observes the entry, the integration half's comment states it does not prove placement — #7's `N10` correctly inherited |
| **BI3 – BI6** | `InvoiceIssueTests.BI3_… / BI4_… / BI5_… / BI6_…` | Read; `BI5` re-reads the two pre-seeded ledger rows after the refusal (#7's `N3` closed) |
| **BI7** | `InvoiceRepositoryTests.BI7_…` | Read; whole-table outbox delta, not `correlation_id`-scoped (#7's `N7` closed) |
| **BI8** | `InvoiceIssueServiceTests.BI8_…` (three-element ordered log) + `InvoiceIssueRaceTests.BI8_…` | Read; the log records all **three** locks (#7's `N4` closed), and the race file's header states it cannot see an inversion |
| **BI9 – BI16** | as tabulated in `requirements.md` §2 | Each named test located and read; all green in the runs above |
| **BI17** | `CentsRuleFixtureGuardTests` (3 cases) | Read; the non-vacuity case writes a real scratch `.cs` into the real test root and asserts **exactly one** hit at the exact line and value — a genuine proof, not an empty assertion |
| **BI20** | comment-only | Verified from the diff: the stale *"bound today"* claim is gone |
| **BI21** | `SagaCommandPayloadTests.BI21_…` | **Probe 5 killed it**; `git diff --stat src/Orders` shows **zero** files, as `G1` requires |
| **BI22** | live boot | **Row still `TODO` on disk — N2** |
| **BI23** | `InvoiceStateTests.BI23_…` | **Probe 1 killed it** on the final rendering |
| **BI24 – BI31** | as tabulated | Each located and read; `BI31` verified from the diff in §4 |

---

## 6. Blocking defects

### D1 — `payment.received.v1`'s payload survives corruption on a green suite, and there is no other guard that could see it

**File:** `src/Billing/Domain/Invoice.cs:325–336` (the sole `Raise(new PaymentReceived(...))`). **Guard:** `tests/Billing.UnitTests/InvoiceTests.cs:117–118`.

**Probe:** `ValueDate: input.ValueDate` → `input.ValueDate.AddDays(1)` **and** `Source: input.Source` → `"ARM-CONSTANT-SOURCE"`; `touch`; rebuild; `dotnet test tests/Billing.UnitTests` → **`Passed! - Failed: 0, Passed: 199, Skipped: 0, Total: 199`**.

**Why nothing else covers it.** The `BI14`/`R46` case asserts the event count, the event type and `PaymentReference` — nothing else. `BillingFactPayloadMapperTests.ToPayload_MapsPaymentReceived_…` maps a **hand-built** `PaymentReceived` and therefore cannot see how `MarkPaid` populated one. `F9`(c) corrupted the **mapper's** `PaymentReference`, which the mapper test catches; the *raise site's* field wiring was never mutated. And `PaymentReceived` has **no live caller** — there is no integration path to this branch at all, so the unit suite is the only reachable guard and it is silent.

**Why it matters.** `requirements.md` `BI14` states the fact carries *"the payment's own reference, amount, currency, **value date and source**"*. Four of those five clauses have no assertion, so the named test does not exercise its own requirement. `CLAUDE.md` is explicit that the fact-emission obligation applies **"with double force where the branch has no live caller yet, because integration harnesses cannot reach it"**, and that a guard must answer *both* questions — is the row absent, and is a field wrong. This one answers only the first. Feature 22 is the caller that inherits this seam.

**To close:** assert `ValueDate`, `Source`, `Amount`, `OrderReference` and `InvoiceReference` on the raised `PaymentReceived` against the `MarkPaidInput` that produced it, drive them from values the test supplies (per `CLAUDE.md` L182 — a corruption probe only bites on a field whose expected value the test supplied), and arm both corruptions above.

### D2 — the request's `discount` can be dropped at the responder with **both** Billing suites fully green

**File:** `src/Billing/Presentation/BillingRpcResponder.cs:261`.

**Probe:** `request.Discount,` → `0L,`; `touch`; rebuild; `dotnet test tests/Billing.UnitTests` → **199/199 passed**; `dotnet test tests/Billing.IntegrationTests` → **`Passed! - Failed: 0, Passed: 74, Skipped: 0, Total: 74, Duration: 4 m 7 s`** against real Testcontainers MS-SQL / NATS / Kafka.

**Root cause.** No Billing integration test ever issues an invoice with a non-zero discount. `BillingHostFixture.IssueRequest` (`BillingHostFixture.cs:273–291`) defaults `discount = 0` and then maps `0` to `null`, so the `discount` key is *omitted from every issue request the suite ever sends*. The assertions that would catch a drop are `InvoiceIssueTests.cs:58` `Assert.Equal(0, invoiceRow.Discount)` and `:88` `Assert.Equal(0, factPayload.Discount)` — expectations a dropped discount satisfies exactly. This is `CLAUDE.md`'s own clause fired verbatim: *"a corruption probe only bites on a field whose expected value the test supplied."* Enumerating command and complete output:

```
$ grep -rn "Discount" tests/Billing.IntegrationTests/*.cs
tests/Billing.IntegrationTests/InvoiceIssueTests.cs:58:        Assert.Equal(0, invoiceRow.Discount);
tests/Billing.IntegrationTests/InvoiceIssueTests.cs:88:        Assert.Equal(0, factPayload.Discount);
tests/Billing.IntegrationTests/RoundTripTests.cs:120:                Discount = 500,
tests/Billing.IntegrationTests/RoundTripTests.cs:175:                Discount = 0,
tests/Billing.IntegrationTests/InvoiceReadRepositoryTests.cs:42:        Assert.Equal(500, paidView.Discount);
tests/Billing.IntegrationTests/UniqueConstraintTests.cs:184:            Discount = 0,
tests/Billing.IntegrationTests/InvoiceNumberAllocatorTests.cs:136:            Discount = 0,
tests/Billing.IntegrationTests/BillingHostFixture.cs:230:            Discount = discount,
```

Classification, one line per hit: `:58` and `:88` are the two assertions, both against `0`; `RoundTripTests:120` and `InvoiceReadRepositoryTests:42` are **directly seeded rows**, never a request through the responder; `RoundTripTests:175`, `UniqueConstraintTests:184` and `InvoiceNumberAllocatorTests:136` are seeded zeros; `BillingHostFixture:230` is `SeedInvoiceAsync`'s parameter. **Zero hits are an issue request carrying a non-zero discount.** The whole chain responder → `IssueInvoiceCommand` → `InvoiceIssueService` → `Invoice.Issue`'s `totalAmount = amount − discount` → the `invoices.discount` column → the fact's `discount` key is exercised only at zero.

**Why it matters.** Task `F3`'s ticked prose is *"the published payload's `lines`, `amount`, **`discount`**, `totalAmount`, `retailerCode` and `companyCode` **equal the request's**"* — a countable claim whose `discount` half cannot fail. And this is the **receiving** half of exactly the property `BI21` guards on the sending side: *`Σ(unitPrice × units) − discount` == the credit hold amount == the order total*. `BI21` now proves Orders sends the discount; nothing proves Billing reads it. A silently dropped discount makes the invoice total exceed the hold that was consumed against it — a money defect on a live path, since Orders genuinely sends non-zero discounts. Live corroboration: all ten rows of `otc_billing.invoices` carry `discount = 0`, so this path has never run non-zero anywhere, in any test or on the live stack.

**To close:** give `InvoiceIssueTests`' `R45` case a **non-zero** discount so `amount`, `discount` and `totalAmount` are three distinct numbers; assert `factPayload.Amount`, `factPayload.Discount`, `factPayload.TotalAmount`, `invoiceRow.Discount` and the `consume` ledger amount against the request's own values; then arm the `request.Discount` → `0L` drop and record the failing named test.

### D3 — `specs/shared/test-matrix.md`'s coverage summary is wrong by three rows

**File:** `specs/shared/test-matrix.md`, the Coverage summary table (group 5 row and the Total row). **Task:** `H5`, *"update the coverage summary counts"*.

Recomputed from the Status column one row at a time, which is what the table's own preamble requires (*"Counted from the Status column as it actually stands, one row at a time"*):

```
group 1: total 10 done 9  scoped 1 (R1)
group 2: total  8 done 7  todo 1
group 3: total 11 done 9  scoped 2 (R24, R29)
group 4: total  8 done 7  scoped 1 (R61)
group 5: total  8 done 8  todo 0          ← table says 5 green / 3 not yet green
group 6: total  5 done 2  todo 3
group 7: total  6 done 0  todo 6
group 8: total  6 done 0  todo 6
group 8.1: total 1 done 0 todo 1
TOTAL done 42 of 63                        ← table says 39
```

`R42`, `R43` and `R44` were flipped to `DONE` by feature 20 in this same working tree without their group's count moving; this feature's `H5` then **incremented the Total by +2 by hand instead of recomputing from the rows**, so the error survived into a shared trilogy artefact. Group 5 must read `8 | 8 | 0 | 0` and the Total `63 | 42 | 4 | 17`.

---

## 7. Non-blocking findings

**N1 — `tasks.md` `H1`–`H4` are ticked over text that says "NOT PERFORMED this session"** (`specs/billing_invoicing/tasks.md:96–99`). The coordinator performed and verified them; the box bodies were never reworded. `tasks.md`'s own preamble: *"A tick over stale text is the failure mode; a tick over a corrected sentence is the behaviour this checkpoint exists to produce."* Reword each of the four to what actually happened, and say the coordinator did it.

**N2 — `requirements.md:163` still reads `BI22 … TODO — NOT performed this session`** while `progress/impl_billing_invoicing.md:339` states *"`BI22`'s traceability row is flipped from `TODO` on this evidence."* The record and the disk disagree, and task `H6` requires the flip. The evidence for the flip is sound (§8) — only the edit is missing.

**N3 — `progress/impl_billing_invoicing.md` is internally contradictory about the live boot.** Its § *"Live boot (`H2`–`H4`) — NOT performed this session"*, its Deviations item 4 and its § *"What I could not do"* all still assert the live boot was not done, while the appended coordinator section says it was, with timings. The appended section should say, at the head of each of those three places, that it supersedes them.

**N4 — `BI30`'s enumerating command does not enumerate its own candidate set.** The recorded command is `grep -rn 'reply\.Items\|\.Items\[' --include=*.cs tests`, which by construction matches only variables literally named `reply`, plus indexer accesses. It returns **6 lines**. The candidate set for a claim about *"every integration test that deserialises an RPC reply"* is `grep -rn '\.Items\b' --include=*.cs tests`, which returns **30**, including twelve reply variables the recorded command cannot see (`byCompany`, `byProduct`, `belowThreshold`, `page1`, `filteredByCompany`, `byRetailer`, `paidOnly`, `issuedOnly`, `byOrder`, `olderThan`, `payload`, `completed`). `CLAUDE.md` L191 makes the enumerating command the artefact precisely so a missed hit is visible as an unclassified line. **The conclusion survives my wider enumeration** — I classified all 30 and every one is preceded by a discriminating-field assertion (`Page`/`Page.Total`, or `Assert.NotNull(x.Items)` where the reply type has no second field, or `Assert.NotNull(actual.Totals)` in the Seed comparisons) — so this is a defect in the evidence, not in the code. Replace the recorded command and output with the wider pair.

**N5 — task `A1` says `AlwaysApproveCreditDecisionTests` is untouched; it was touched.** `tests/Billing.UnitTests/AlwaysApproveCreditDecisionTests.cs` changed by two lines — the class summary, and a case rename `BC15_ApprovesEveryRequest_AndIsTheOnlyRegistrationFeature20Replaces` → `…_AndWasTheOnlyRegistrationFeature20Replaced`. The change is in `A1`'s spirit and harmless, but it contradicts the box's own text and is absent from the report's Deviations list.

**N6 — the live stack now carries a deliberately corrupted fixture, and nothing says so.** `ORD-000011` sits at `despatched` with its hold released and a permanently `rejected` `invoice.issue` row. Every future live boot will re-observe it. Name it in `design.md` §18's hand-over so phase 11 does not read it as a regression.

**A1 (advisory) — the wording of the `InvoiceState` deviation, and one test hygiene point.** (a) *"That sketch does not compile"* should read *"that sketch compiles, but cannot express the intended accessibility"* — see §3; the argument is unaffected and the code is right. (b) `CentsRuleFixtureGuardTests.TheScanGenuinelyFiresAgainstAScratchFixture…` writes a `.cs` file into `tests/Billing.IntegrationTests/` and deletes it in a `finally`; a hard kill inside that window leaves a file that breaks the next compile. Passing a `Directory.CreateTempSubdirectory()` root to `FindUnguardedLiterals` proves the same thing without touching the source tree.

---

## 8. The coordinator's `H1`–`H4`, verified independently — and the answer to the question asked

I re-read the live databases directly rather than accepting the appended section. **Every observation in it is confirmed.**

```
otc_orders.saga_commands (command='invoice.issue'):
  ORD-000007 sent     12    ORD-000008 sent 12    ORD-000009 sent 12
  ORD-000010 sent     12    ORD-000011 rejected 13 (terminal business rejection (PRECONDITION_FAILED))
  ORD-000013 sent      0
otc_orders.orders: ORD-000007..10 invoiced · ORD-000011 despatched · ORD-000013 invoiced
otc_billing.invoices: INV-000006→ORD-000007, INV-000007→ORD-000008, INV-000008→ORD-000009,
                      INV-000009→ORD-000010, INV-000010→ORD-000013 — all issued
otc_billing.credit_items: consume rows at 2026-09-06 07:12:52.978–07:12:53.329 for ORD-000007..10,
                          and 07:14:34.110 for ORD-000013
otc_billing.outbox: invoice.issued.v1 ×10 (5 seeded + 5 new) · payment.received.v1 ×5 (seed)
                    credit.released.v1 ×6 (unmoved) · otc_orders.orders at 'paid' = 0 · payments = 5
```

Four parked commands resolved to `sent` with four invoices, the allocator continued past the seed's `INV-000005` to `INV-000006` (`BI12`/`L27` observed live), the fifth resolved terminal, and the control order `ORD-000013` traversed the chain to `invoiced` with `INV-000010` at total `5547`. `H3`'s negative half holds exactly.

### **Yes, I agree with your read — and it is not a symptom of an upstream defect.** Here is the evidence that closes it.

`ORD-000011`'s released hold is **the recorded residue of feature 19's own live-boot task `I4`**, not a saga action. `progress/impl_billing_credit.md:128` says so in its own words: *"`I4` — the raw `billing.credit.release` walkthrough (no production caller until feature 41). Released `ORD-000011`'s 1000-unit hold over raw NATS"*, with the reply body quoted. Three independent confirmations that no saga path produced it:

1. **`otc_orders.saga_commands` for `ORD-000011` holds exactly four rows** — `stock.reserve`, `credit.hold`, `despatch.create`, `invoice.issue` — and **no `credit.release` row**. A saga compensation would have written one; the orchestrator has no other way to reach Billing.
2. **The order was never cancelled.** Its status is `despatched` and its outbox holds exactly two facts, `order.placed.v1` and `order.confirmed.v1` — no `order.cancelled.v1`.
3. **The `reason: "order_cancelled"` in the `credit.released.v1` payload is the release RPC's own request field**, supplied by the hand-typed probe, not derived from any order state — which is exactly why it disagrees with the order's actual status. The other five `credit.released.v1` rows all read `invoice_paid` and all belong to the phase-7 seed.

So the sequence is: a reviewer-era manual probe consumed the only order-scoped state Billing owns, and eleven hours later the invoicing responder correctly found the two write models in disagreement, answered `PRECONDITION_FAILED` / `NO_ACTIVE_HOLD`, and **feature 42's classification stopped the row instead of retrying it forever**. That is `BI5`'s justifying reasoning working end to end — *"the two write models disagree, a retry cannot fix it, a human must see it"* — and it is the first live observation of the terminal path. **You are reading a feature, not a symptom.** The only follow-up it needs is N6: say in the hand-over that this fixture is deliberately corrupt, so phase 11 does not spend a round rediscovering it.

---

## 9. Confound column — would #7's standard have caught each defect?

| Defect | Would #7's standard have caught it? | Evidence |
|---|---|---|
| **D1** | **No — raised bar** | #7 has the identical gap: `apps/billing/src/domain/invoice.spec.ts:109–111` asserts `events).toHaveLength(1)` and `eventType === 'payment.received.v1'` and no payload field at all. The payload-corruption family is #8's own rule, adopted after feature 17 |
| **D2** | **No — raised bar** | #7's `invoice-issue.integration.spec.ts` uses `discount: 0` on its success path (line 368) and `discount: 2_001` only to prove the refusal (line 252). #7 never issued an invoice with a non-zero discount either. What makes it #8's defect is that #8's task `F3` *claims* the payload's `discount` equals the request's, and `CLAUDE.md` L182 forbids exactly this expected-value degeneracy |
| **D3** | **Yes** | #7's own Phase 25 traceability pass found and fixed the identical drift, and the rule it wrote — *"derived from the rows, never incremented by hand"* — is in the copied file's own preamble |
| N1, N2, N3, N5 | **Yes** | #7 was **rejected** on this feature partly for `0 of 60` boxes ticked; box/record hygiene is squarely #7's standard |
| N4 | **No — raised bar** | The enumerating-command rule (`CLAUDE.md` L191) is #8's, written after three prose sweeps were disproved |

**The reuse dividend that shows up as absence, and must not be omitted from the numbers.** #7 was rejected on this feature with two blocking findings, **neither a functional defect in shipped behaviour**: `N2` (the discount check's placement inside the transaction) and `N1` (`0 of 60` boxes ticked). **#8's spec pre-empted both by design** — `BI2`'s placement clause was written into `requirements.md` and discharged at the **unit** level by observing the entry (`F1`), which also absorbs #7's round-2 `N10`; and `tasks.md`'s preamble makes ticking-with-honest-rewording an explicit close criterion. #7's non-blocking `N3`, `N4` and `N7` were likewise written in from the start (the ledger-row re-read, the three-element lock log, the whole-table outbox delta) and I confirmed all three present. **That is a genuine reuse dividend of one full review round plus one fix pass, and it is invisible in any count of defects found.** It should be stated in the effort record as *rounds avoided*, not left to be inferred from a smaller defect list.

**Both of this round's blocking defects are of a class #7 shipped too.** That is the interesting benchmark result here: #8's round 1 is not finding #7's mistakes over again, it is finding a class #7's process could not see, using rules #8 wrote after its own feature 17. It cost two probes.

---

## 10. What must change before re-review

1. **D1** — assert `PaymentReceived`'s `ValueDate`, `Source`, `Amount`, `OrderReference` and `InvoiceReference` at the `MarkPaid` raise site, driven from test-supplied values; arm both corruptions and record the failing named test and verbatim message.
2. **D2** — issue an invoice with a **non-zero** discount in `InvoiceIssueTests`' `R45` case so `amount`, `discount` and `totalAmount` are three distinct numbers; assert all three plus `invoiceRow.Discount` and the `consume` amount against the request; arm the `request.Discount` → `0L` drop.
3. **D3** — recompute `specs/shared/test-matrix.md`'s coverage summary **from the rows**: group 5 → `8 | 8 | 0 | 0`, Total → `63 | 42 | 4 | 17`.
4. **N1** — reword `tasks.md` `H1`–`H4` to what was actually done, naming the coordinator.
5. **N2** — flip `requirements.md` `BI22` from `TODO`, citing the live evidence.
6. **N3** — mark the three superseded live-boot passages in `impl_billing_invoicing.md`.
7. **N4** — replace `BI30`'s enumerating command and output with the wider `grep -rn '\.Items\b' --include=*.cs tests` and its complete 30-line output, one classification line per hit.
8. **N5** — disclose the `AlwaysApproveCreditDecisionTests` edit on box `A1`, or revert it.
9. **N6** — add the `ORD-000011` fixture note to `design.md` §18.
10. **A1** — the two wording/hygiene points, at the implementer's discretion.

Items 1 and 2 are code-and-test work. Items 3–10 are edits to spec and record files, none of which touches `src/`.

## 11. Bookkeeping performed

- `feature_list.json` id 21 `billing_invoicing`: `in_review` → **`in_progress`** (single-line edit; `git diff` verified to show only that line).
- `feature_list.json` id 55: **left `pending`.** All six of its sites genuinely hold — I verified each by reading `CreditListTests.cs` and `StockListTests.cs`, and my wider enumeration found no remaining violation anywhere in the repository. But the entry's own third acceptance line is *"an enumerating command and its complete output are recorded — the claim is a search result, not a reading"*, and the recorded command does not enumerate the candidate set (N4). It closes the moment N4's command replaces it, in the same round that closes D1–D3.
- **No `history.md` entry and no effort record** — correct while rejected; both are owed at approval.
- Nothing committed. No `git checkout --` run on any file.

---
---

# ROUND 2 — re-review of the fix round (2026-09-06)

> **This section is additive. Round 1 above is untouched and not reopened** — its findings, its probe table and its REJECTED verdict stand as the record of what was submitted the first time. Everything below concerns only what changed after it.

**Verdict: APPROVED**, with **two record corrections owed before the human commits** (N1 and N2 from round 1, deliberately scoped out of the fix round by the coordinator — see §R2.8). All three blocking defects are closed and each was re-verified by my own mutation, not by reading the fix report. Backlog **id 55 is closed** on combined evidence, with a correction to the evidence standard I myself prescribed in round 1.

---

## R2.1 What I ran, and what I did not

| Run | Result | Why |
|---|---|---|
| `dotnet build OrderToCash.sln --no-incremental` (×3, after every restore) | succeeded, **0 warnings, 0 errors** | The arming protocol's forced rebuild — a `cmp`-clean restore proves nothing about the binary |
| `dotnet test tests/Billing.UnitTests` (final, post-restore) | **199 / 199** | My own run. Matches the report |
| `dotnet test tests/Billing.IntegrationTests` (full, real Testcontainers MsSql/Kafka/NATS) | **74 / 74**, **4 m 22 s** | My own run, after a forced solution rebuild. Matches the report |
| `dotnet test tests/Architecture.Tests` | **16 / 16** | C3 domain purity — run, not eyeballed |
| `./init.sh` | **exit 0** | Backlog tripwire clean, session file in lockstep |

**Not re-run:** `./quality.sh` end to end, and the ten other test projects. The fix round touched two test files, one shared spec file, one design file and one progress file — no production source at all (verified below) — so a thirteen-project re-run would re-prove feature 19's and 20's work, not this round's claims. I ran the three projects whose behaviour could have moved, plus an independent `--no-incremental` solution build. The 873-test whole-solution figure is the implementer's and I did not duplicate it; **it is not load-bearing for this verdict.**

**Six mutation probes of my own this round**, all at the sites the fix claims to have closed, each with my own backup taken first, `touch` + `--no-incremental` rebuild before the failing run, restore verified by `cmp` **and** by re-reading the changed lines, and a forced solution rebuild before the confirming green run. No `git checkout --` was run on anything.

| # | Family | Mutation | Result |
|---:|---|---|---|
| 1 | corruption | `Invoice.cs:335` `ValueDate: input.ValueDate` → `.AddDays(1)` **and** `:336` `Source` → `"ARM-CONSTANT-SOURCE"` | **KILLED** — `InvoiceTests.R46_…` at `InvoiceTests.cs:132`, `Expected: 2026-09-08… / Actual: 2026-09-09…`, 1 failed / 198 passed |
| 2 | corruption | `Invoice.cs:336` `Source` → `"ARM-CONSTANT-SOURCE"` **alone** (ValueDate restored) | **KILLED** — `Expected: "bank-file-import" / Actual: "ARM-CONSTANT-SOURCE"`. Proves `Source` is independently guarded and was not merely shadowed by probe 1's first-failing assertion |
| 3 | corruption | `Invoice.cs:332` `InvoiceReference: InvoiceReference` → `"INV-ARMED"` | **KILLED** — `Expected: "INV-000001" / Actual: "INV-ARMED"` |
| 4 | corruption | `Invoice.cs:334` `Amount: input.Amount` → `new Money(1L, …)` | **KILLED** — `Expected: 2000 / Actual: 1` |
| 5 | corruption | `BillingRpcResponder.cs:261` `request.Discount` → `0L` | **KILLED** — `InvoiceIssueTests.R45_…_TheIntegrationHalf` at `InvoiceIssueTests.cs:62`, `Expected: 8000 / Actual: 9000`, against real containers |
| 6 | corruption | `BillingFactPayloadMapper.cs:71` `Discount: issued.Discount.MinorUnits` → `0L` | **KILLED** — same test at `InvoiceIssueTests.cs:98`, `Expected: 1000 / Actual: 0`. A **second, independent** corruption site for the same field: probe 5 attacks the inbound wiring, probe 6 the outbound payload, and each dies on a different assertion |

A structural non-probe worth recording: attempting to swap `OrderReference`/`InvoiceReference` at the raise site **does not compile** (`CS1503` — `OrderNumber` versus `string`). That pair is closed by the type system, not by the test, which is a stronger guarantee and is why I substituted probe 3.

## R2.2 D1 — closed, and re-armed at the raise site rather than the mapper

Round 1's miss was that `F9`(c) had corrupted the **mapper**, whose own test builds a `PaymentReceived` by hand and therefore cannot see how `MarkPaid` populates one. **All four of my probes this round are at `src/Billing/Domain/Invoice.cs`'s sole `Raise(new PaymentReceived(...))`, lines 325–336 — the raise site — and all four are killed by `InvoiceTests.R46_…`.**

The test now supplies its own values and they are genuinely separated from everything else in scope (`tests/Billing.UnitTests/InvoiceTests.cs:109–117`): `paidInstant` is `2026-09-10T08:00Z`, the class-level `_ctx.OccurredAt` is `2026-09-05T12:00Z`, and the supplied `valueDate` is `2026-09-08T00:00Z` — three distinct instants, so `ValueDate` cannot coincidentally match either context timestamp. `source` is `"bank-file-import"`, which appears nowhere else in the file (`BuildInput`'s strings are `INV-000001`, `ORD-000001`, `CarrefourEs`, `IBERFOODS`, `SKU-1`, `EUR`; the neighbouring `BI14` cases use `"robot"`). `CLAUDE.md`'s clause — *a corruption probe only bites on a field whose expected value the test supplied* — is satisfied for both fields, and probes 1–4 demonstrate it rather than assert it.

**One residual degeneracy, structural and not a defect (advisory R2-A1).** `Assert.Equal(markPaidInput.Amount.MinorUnits, fact.Amount.MinorUnits)` cannot fail to a raise-site *swap* of `input.Amount` for the aggregate's `TotalAmount`, because `MarkPaid`'s own guard at `Invoice.cs:318–321` throws unless the two are already equal. No test input can separate them; the swap is semantically identity. Probe 4 shows the assertion is not vacuous — it dies to a *constant* — which is the corruption family that can actually occur. Recorded so no future round re-derives it.

## R2.3 D2 — closed; the fixture change does make corruption visible

The claim under test was that the integration case now issues a non-zero discount so amount, discount and total are three distinct numbers. **Checked, not accepted:**

- `InvoiceIssueTests.cs:36–38` — `grossAmount = 9_000`, `discount = 1_000`, `totalAmount = 8_000`. **No two coincide**, and none is zero. The request's lines (`SKU-1` 3 × 2 000, `SKU-2` 1 × 3 000) sum to exactly `9_000`, so the gross is request-derived rather than a literal that happens to agree.
- `:39` — the credit hold is seeded at `8_000`, the **discounted** total, so `consumeEntry.Amount` at `:79` is also request-derived. Under the round-1 fixture the hold equalled the gross and a dropped discount would have consumed the right number by accident.
- **The assertions read the field the mutation changes.** Probe 5 dies at `:62` (`payload.TotalAmount`, the RPC reply) and probe 6 at `:98` (`factPayload.Discount`, the published outbox payload). Together with `:68` `invoiceRow.Discount` and `:69` `invoiceRow.TotalAmount`, the whole chain responder → command → `Invoice.Issue` → the `invoices` columns → the fact payload is now exercised at a non-zero discount.

Round 1's enumerating command re-run for completeness — `grep -rn "Discount" tests/Billing.IntegrationTests/*.cs` — the two degenerate `Assert.Equal(0, …)` expectations at `InvoiceIssueTests.cs:58` and `:88` are gone, replaced by comparisons against `discount`/`grossAmount`/`totalAmount`.

**Record inaccuracy (R2-N2, non-blocking).** `progress/impl_billing_invoicing.md:390` states the mutation *"fails at `invoiceRow.TotalAmount`, the first of the now-distinct-valued assertions the corruption reaches."* It does not: it fails at `InvoiceIssueTests.cs:62`, `payload.TotalAmount` — the RPC reply, seven lines earlier. The verbatim `Expected: 8000 / Actual: 9000` in the report is correct; only the attribution is wrong. It matters slightly because the reply assertion is the *stronger* of the two — it proves the discount survives the round trip back out over NATS, not merely into the database.

## R2.4 D3 — re-derived independently, by a different method, to the same numbers

I did not re-run the implementer's script. A script that reproduces a wrong answer confidently is worse than a hand-adjusted total, so I wrote my own with a different parsing strategy: the implementer's hardcoded group→id ranges are themselves an assumption about the file, so **mine attributes each row to a group by the `##` section heading it falls under**, and classifies by reading rather than by keyword.

```
total rows parsed: 63     duplicate ids: []     missing 1..63: []
rows per section: 1→10  2→8  3→11  4→8  5→8  6→5  7→6  8(+8.1)→7
```

The section walk finds the same 63 rows with no gaps and no duplicates — which the range-based script could not have detected, since it assumes the answer. I then read every Status cell that could be scoped-in-disguise rather than trusting a regex: the four scoped rows are **R1** (`DOMAIN HALF DONE`), **R24** (`INTEGRATION HALF DONE`), **R29** (`RETRY-CLAUSE ROW DONE`) and **R61** (`DOMAIN UNIT HALF DONE`), in groups 1, 3, 3 and 4 — matching the table's per-group scoped counts of 1, 2 and 1. I checked the three cells whose wording could have hidden a shortfall behind a plain `DONE` — **R36** (`DONE — domain: …`), **R43** and **R44** (`DONE — domain half …`) — and none states a shortfall; all three enumerate a full domain-plus-integration realisation. Group 5's eight cells are unqualified `DONE`.

**Independent result: group 5 = `8 | 8 | 0 | 0`, Total = `63 | 42 | 4 | 17`.** `specs/shared/test-matrix.md:76` and `:81` now read exactly that. **D3 is closed and the number is right by a method that did not inherit the wrong one's assumptions.**

**One thing my group walk surfaced that round 1 did not check, and it is not a defect.** Matrix group 6 is titled `billing_invoicing` and spans **R45 – R49**, of which R47/R48/R49 read `TODO`. Traceability rule 3 says a feature may not be `done` while one of its rows is `TODO`, so on a literal reading this approval would breach it. It does not, because the matrix group is broader than the backlog feature: R47–R49 are the remittance intake, which is **backlog id 22 `billing_remittance_intake`**, and `specs/billing_invoicing/requirements.md:7` and `:174`, `design.md:20` and `tasks.md` `H5` all say so explicitly and in advance. Two precedents confirm the reading rather than my inventing one: **this repository's own group 4** (`fulfillment_stock`, R30–R36+R61) closed feature 17 with R36 owed to feature 18; and **#7 did exactly this** — `../order-to-cash-nestjs/specs/shared/test-matrix.md:172` shows R47–R49 flipped by its own `payment-register.integration.spec.ts` in feature 22, a whole feature after its `billing_invoicing` closed. Resolved from #7's checkout rather than raised as a question.

## R2.5 Backlog id 55 — the evidence, judged rather than counted

**The recorded command enumerates a proxy, not the candidate set — and the proxy is one I prescribed in round 1.** `BC32`'s claim is *"every integration test that deserialises an RPC reply asserts its discriminating field first"*. `grep -rn '\.Items\b' --include=*.cs tests` enumerates every site touching a member **named `Items`**, which is a strict subset: a reply type whose collection is named anything else is invisible to it. Round 1's finding N4 was that the previous command did not enumerate its own candidate set; my replacement narrowed the gap without closing it, and I own that.

The genuine candidate set is rooted at the deserialisation, so I enumerated **that** instead:

```
$ grep -rn "Deserialize<" --include=*.cs tests | ... | sort | uniq -c
```

and then read every reply payload record for a collection member. **Three reply types carry collections not named `Items`**, and between them they account for roughly nineteen deserialisation sites the recorded command cannot see:

| Reply type | Collection member | Deser. sites in tests |
|---|---|---:|
| `StockReserveReplyPayload` | `Reservations`, `Shortages` | 13 |
| `StockCheckReplyPayload` | `Lines` | 3 |
| `DespatchCreateReplyPayload` | `Lines` | 3 |

Enumerating those sites and reading each one: `StockCheckTests.cs:31` and `:87` are preceded by `Assert.True/False(payload.Available)`; `DespatchCreateTests.cs:40` by `Assert.True(payload.Created)` and `Assert.Matches(…, payload.DespatchReference)`; `StockReserveTests.cs:40`, `:116` and `:167` by `Assert.Equal("accepted"/"rejected"/"already_reserved", payload.Outcome)`. **Every one asserts its reply's own discriminating field before touching the collection. Zero new violations.**

**Judgement: id 55 is closed `done`.** Acceptance line 1 was verified site-by-site in round 1 (all six correct). Acceptance line 3 — *"an enumerating command and its complete output are recorded — the claim is a search result, not a reading"* — is satisfied **by the record as a whole**: the implementer's 34-hit classification covers the `Items`-named half and this section covers the three types it structurally could not reach. Closing on the combined record rather than re-rejecting is the same call the entry's own `notes` field already made for its predecessor round, and re-rejecting against a bound I wrote and got wrong would be goalpost-shifting. **Recorded as R2-N3 so the lesson survives the closure: an enumerating command must be rooted at the thing the claim quantifies over — here `Deserialize<…>`, the act of deserialising a reply — never at the shape the known instances happen to have.** That is the third time in this build a negative claim has been checked against a proxy for its own candidate set.

## R2.6 The `CS8878` correction and N6

- **`CS8878`, both occurrences.** `progress/impl_billing_invoicing.md:37` and `:260` now both say the sketch **compiles but cannot express the intended `private protected` accessibility**, with `:37` naming the language rule (a non-sealed record's synthesised copy constructor must be at least `protected`) and attributing the correction. This matches what I built and reflected over in round 1 §3. **Correct in both places.**
- **N6.** `specs/billing_invoicing/design.md:788` names `ORD-000011` in the §18 hand-over: `despatched`, hold released, `invoice.issue` permanently `rejected` with `PRECONDITION_FAILED` / `NoActiveHoldError`; states it was released by hand over raw NATS during feature 19's task `I4`; cites the two negative facts that rule out a saga cause (no `credit.release` row, order never cancelled); and instructs phase 11 to read it as the first live observation of feature 42's terminal path. **Accurate against everything I verified independently in round 1 §8, and it names the right consumer.**

## R2.7 A defect in the fix round's own evidence — R2-N1

**`progress/impl_billing_invoicing.md:537` offers `git diff --stat src/Billing/Domain/Invoice.cs src/Billing/Presentation/BillingRpcResponder.cs` → empty as proof that both files were restored byte-identical after arming. That check cannot fail.** Both files are **untracked** — `git status --porcelain` reports `?? src/Billing/Domain/Invoice.cs` and `?? src/Billing/Presentation/BillingRpcResponder.cs`, because the whole feature is uncommitted — and `git diff` never reports untracked paths. The command returns empty output whether the file is pristine, still armed, or deleted.

The restore was in fact correct — I verified it before arming anything, by reading `Invoice.cs:325–336` and `BillingRpcResponder.cs:261` on disk, and again afterwards by `cmp` against backups I took myself — so **this is a defect in the evidence, not in the code.** It is worth its own finding because it is the guard-that-does-not-guard shape appearing *inside the arming protocol's own restore step*, and because `CLAUDE.md` already documents the adjacent half of it: the protocol warns that `git checkout --` fails on untracked paths and silently restores nothing. The same untrackedness that breaks the restore breaks the verification of the restore, and only the first is written down. The protocol's own prescriptions — `cmp` against a backup you took, plus re-reading the changed line, plus a forced rebuild — are the checks that work here, and the report performed all three at steps 6–7; step 8's `git diff` adds nothing and reads as if it adds the most.

## R2.8 Round 1's non-blocking findings — what is closed and what is still owed

| Round-1 finding | State | Note |
|---|---|---|
| **N4** (`BI30`'s enumerating command) | **Closed**, with R2-N3 appended | See §R2.5 |
| **N6** (`ORD-000011` hand-over note) | **Closed** | `design.md:788` |
| **A1(a)** (the `CS8878` wording) | **Closed** | Both occurrences |
| **N1** (`tasks.md` `H1`–`H4` ticked over *"NOT PERFORMED this session"*) | **STILL OPEN** | `specs/billing_invoicing/tasks.md:96–99`, verified on disk this round — all four boxes still carry the stale sentence |
| **N2** (`requirements.md` `BI22` row still `TODO`) | **STILL OPEN** | `specs/billing_invoicing/requirements.md:163` still reads `TODO — NOT performed this session`, contradicting `impl_billing_invoicing.md`'s own §`H1`–`H4` |
| **N3** (three superseded live-boot passages unmarked) | **STILL OPEN** | `impl_billing_invoicing.md` §§ *Live boot*, Deviations 4, *What I could not do* |
| **N5** (`AlwaysApproveCreditDecisionTests` edit undisclosed on box `A1`) | **STILL OPEN** | |
| **A1(b)** (the scratch `.cs` written into the test root) | **STILL OPEN** | No stray file present now — I checked; `tests/Billing.IntegrationTests/*.cs` is 31 files and `find`ing `.bak` outside the scratchpad returns nothing |

All five were **explicitly scoped out of the fix round by the coordinator** and the report says so at `impl_billing_invoicing.md:345`. That is a legitimate deferral, not a silent omission, and none of them is a code defect — so **they do not block approval**, exactly as round 1 classified them. Re-escalating a finding I myself marked non-blocking would be goalpost-shifting.

**But N1 and N2 must be corrected before the human commits, and this approval is stated as conditional on that.** Not for process tidiness: `billing_invoicing` is about to be committed as `done` in a repository whose entire product is process evidence, and it would carry into that record four task boxes ticked over the sentence *"NOT PERFORMED this session"* about work that **was** performed and which I independently confirmed line by line in round 1 §8, plus a traceability row reading `TODO` for a requirement whose evidence exists. `tasks.md`'s own preamble names this exactly — *"A tick over stale text is the failure mode; a tick over a corrected sentence is the behaviour this checkpoint exists to produce"* — and `CLAUDE.md` adds *"a tick is not evidence the assertion exists."* The correction is five sentences and one table cell, needs no re-review, and is `test_maintainer`-tier work. It is the cheapest item in this whole feature and the only one that would be permanently false if skipped.

## R2.9 `CHECKPOINTS.md` — round 2

Boxes whose state changed since round 1, or which this round re-checked. Every box not listed stands as round 1 marked it.

### C2 — State is coherent
- [x] At most one feature `in_progress` — **zero**, and zero `in_review`, after this verdict's two edits.
- [x] Every status is in `rules.valid_status` — re-validated by parsing the JSON: 55 features, 32 `done`, 23 `pending`.
- [x] Every `done` feature has passing tests associated with it — for id 21, from my own runs below.

### C3 — Architecture is respected
- [x] No framework reference inside any `Domain/` folder — **NetArchTest re-run this round, 16/16 green**, after a forced solution rebuild.
- [x] No cross-service DB access, no shared runtime code beyond `SharedKernel`/`Contracts`/`Cqrs` — unchanged this round; the fix touched no production source.
- [x] Kafka-fact versus NATS-RPC classification unchanged.

### C4 — Verification is real
- [ ] **`./quality.sh` passes** — **still not re-run by me** (see §R2.1), and left unmarked for the second time deliberately. The implementer reports exit 0 across thirteen projects; I independently ran three of them plus a `--no-incremental` solution build.
- [x] Domain tests are pure — `InvoiceTests` still references only `OrderToCash.Billing.Domain*`, `OrderToCash.SharedKernel` and `Xunit`; the D1 fix added assertions, no framework.
- [x] Integration tests use Testcontainers against real MsSql / Kafka / NATS — I ran all **74** against live containers, and probes 5 and 6 died inside them.
- [ ] **Coverage thresholds met** — the implementer's figures, not re-derived. Not blocking.
- [x] No Jest anywhere.

### C5 — The session closed cleanly
- [x] No suspicious untracked files — 89 uncommitted changes, all attributable to features 20 and 21; no `.bak` anywhere outside the scratchpad; no leftover scratch `.cs` in any test root.
- [x] **`progress/history.md` has an entry for the feature just finished, including its effort record** — appended by this verdict.
- [x] `feature_list.json` reflects the true state — two single-line edits, `git diff` read line by line (§R2.10).
- [x] The human has been told what was done and how to test it manually.
- [x] Claude did not commit.

### C6 — Spec-Driven Development
- [x] `specs/billing_invoicing/` has all three files, `requirements.md` in EARS with `BI<n>` ids.
- [ ] **Every task ticked `[x]`** — all 59 boxes carry `[x]`; `H1`–`H4` still tick over *"NOT PERFORMED this session"*. **Unmarked for the second round** — N1, §R2.8.
- [ ] **Every `R<n>` covered by at least one concrete named test** — every `BI<n>` and both shared rows verified; `BI22`'s row still reads `TODO` on disk. **Unmarked** — N2, §R2.8. The *evidence* for `BI22` exists and I confirmed it independently in round 1 §8; only the cell is unflipped.
- [x] The spec commit precedes the implementation commit — both uncommitted in one tree; the human commits the spec first.

### C7 — Spec-reuse fidelity and benchmark honesty
- [x] **`specs/shared/` still matches #7's except `test-matrix.md`** — the fix round touched `test-matrix.md`'s Coverage summary only, which is column-5-derived and explicitly per-assessment under the file's own reuse recipe. Verified by re-deriving the numbers (§R2.4), not by reading the diff.
- [x] Every deviation is a recorded amendment — no amendment to `specs/shared/` beyond the derived counts.
- [x] The `R<n>` ids are #7's — `R45`/`R46` genuinely satisfied; R47–R49 correctly left to feature 22, per #7's own precedent (§R2.4).
- [x] **`progress/history.md` effort records complete and honest** — appended with the confound column, #7's baseline, and the reuse dividend stated as absence.
- [ ] `n8n` workflows / black-box API script — **not applicable**, features 25 and 28.

## R2.10 Bookkeeping performed

- `feature_list.json` line **329**, id 21 `billing_invoicing`: `in_review` → **`done`**.
- `feature_list.json` line **754**, id 55 `bc32_universal_claim_is_false_at_six_more_sites`: `pending` → **`done`** (§R2.5).
- Both were **single-line `sed` edits on the specific line number**, never a parse-and-rewrite. `git diff feature_list.json` read in full afterwards: it shows my two lines plus the coordinator's own earlier uncommitted edits (id 20's flip and id 56's new entry), which I left untouched. The file re-parses as valid JSON, 55 features, and its 35 non-ASCII em-dashes are intact.
- **No `git checkout --` was run on `feature_list.json` or on anything else.**
- `progress/history.md` — effort record appended.
- Nothing committed, nothing pushed.

## R2.11 Test figures, read off my own runs

**`Billing.UnitTests` 199 / 199 · `Billing.IntegrationTests` 74 / 74 (4 m 22 s, real containers) · `Architecture.Tests` 16 / 16 · solution build 0 warnings, 0 errors · `./init.sh` exit 0.**

**Addendum to §R2.10.** Setting id 21 `done` left `progress/current.md` naming a feature while none is active, which `init.sh`'s lockstep check (section 4) correctly failed on — the expected post-approval state, since the session file must go idle between features. I updated its `**Feature:**` and `**Status:**` lines only, to `none — awaiting the next feature` and an idle status recording the approval, the id 55 closure and the two record corrections owed before commit. `./init.sh` then returns **exit 0** with `32/55 features done`, `no feature in_progress`, lockstep clean and the backlog tripwire clean. No other line of `current.md` was touched.
