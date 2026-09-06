# review: billing_credit_simulator (feature 20, phase 10)

**Verdict: APPROVED** — 0 blocking defects, 3 non-blocking findings, **7 hostile mutations run, 6 killed by a named test, 1 survived** (P7 — a pre-existing, project-wide class this feature did not author, recorded as A1 with backlog wording).

Reviewed against `feature_list.json` id 20's three acceptance bullets (`sdd: false`, so there is no `specs/billing_credit_simulator/` and none is required), `specs/shared/requirements.md` §5.1 (R42–R44) and its boxed warning, `CLAUDE.md` **read from disk this session**, and #7's own verdict on the same feature (`order-to-cash-nestjs/progress/review_billing_credit_simulator.md`, six non-blocking findings the implementer was told to inherit as prevention).

---

## 1. Scope discipline — what I ran myself, and what I did not

The declared footprint is one new adapter file, one option property, one DI line, one `Program.cs` line, `.env.example`, two new test files, one test doc/name correction, three `test-matrix.md` Status cells and one `feature_list.json` line. `git status --porcelain` and `git diff --stat` confirm it **exactly**: 8 modified files (**42 insertions, 13 deletions**), 4 untracked (the adapter, two test files, the impl report). **Nothing outside `src/Billing/`, `tests/Billing.*`, `.env.example`, `specs/shared/test-matrix.md`, `feature_list.json` and `progress/`.** The diff cannot reach Orders, Fulfillment, Seed, Notifications, Contracts, Cqrs or SharedKernel, so I did not re-run their suites.

| Run | Mine or claimed | Result |
|---|---|---|
| `dotnet test tests/Billing.UnitTests` | **mine**, 3× (baseline + confirming runs) | **136/136 passed** — matches the claim |
| `dotnet test tests/Billing.IntegrationTests` (full, real MS-SQL + NATS + Kafka Testcontainers) | **mine** | **55/55 passed, 3 m 45 s** — matches the claim, and this is the run that matters: it is the whole Billing suite executing with the simulator as the **default** binding |
| `dotnet test tests/Billing.IntegrationTests --filter CreditSimulatorTests` | **mine**, 3× (two mutated, one clean) | 4/4 clean; see P3/P4 |
| `dotnet test tests/Architecture.Tests` (NetArchTest) | **mine** | **16/16 passed** — domain purity run, not eyeballed |
| `dotnet format --verify-no-changes` (solution) | **mine** | exit 0, clean |
| `dotnet build src/Billing --no-incremental` | **mine**, after every restore | 0 warnings, 0 errors |
| `./init.sh` | **mine** | **exit 0**, 54 features parsed, backlog tripwire clean, lockstep OK |
| `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` | **mine** | only `test-matrix.md` differs, and this session's diff touches only the R42/R43/R44 Status cells — **C7 box 1 holds** |
| `./quality.sh` end to end | **implementer's**, log inspected (`scratchpad/quality.log`, 06:12) | format clean, 13 projects, 0 failures, **873 tests** — see finding **N1**: the report says 883 |

I did **not** re-run `quality.sh` myself (its format + build + test pass duplicates what I ran project by project, and the claim under test is not about the other eleven projects). Everything below is a probe, a grep, a diff or a run I performed, quoted with its command or its verbatim failure message.

---

## 2. Acceptance list (the specification of record)

| Acceptance bullet | Verdict | Evidence |
|---|---|---|
| rejects `totalAmount % 100 === 99` deterministically | **MET** — `SimulatorCreditDecision.cs:60-63`, reading `AmountMinorUnits` **only**; five available-credit levels including `long`-near-max, and six non-`.99` amounts, all asserted | P1, P3; killed by the implementer's M1 and by my P3 at the wire |
| `CREDIT_FAILURE_RATE` defaults to 0 | **MET** at three levels — the loader (`null`/`""` → `0`), the option (`BillingOptions.CreditFailureRate` has no initialiser), and the wire (a fitting non-`.99` hold is approved under the default host) | P4 and P5 both killed, at the wiring and at the loader |
| sits behind the credit port | **MET** — no `Domain/`, `Application/` or `Presentation/` file changed; the class implements `ICreditDecisionPort`, is constructed only in `BillingServiceCollectionExtensions.cs:50`, performs no I/O, and holds no client, clock or connection | `git diff --stat`; `AdapterRejectionReason` (the closed enum that structurally excludes `over_limit`) is unchanged from feature 19 |

---

## 3. R → test mapping (verified by mutation, not by reading the matrix)

| Req | Test I confirmed exercises it | How I confirmed |
|---|---|---|
| **R42** (`.99` → `simulated_cents_rule` regardless of credit, no ledger entry, `credit.rejected.v1`) | `SimulatorCreditDecisionTests.R42_RejectsAnAmountEndingIn99WithSimulatedCentsRule_RegardlessOfTheAvailableCredit` (5 cases), `.R42_DoesNotFireForAnAmountThatDoesNotEndIn99` (6 cases), `.TheCentsRuleWinsOverTheFailureRateRuleWhenBothCouldApply`; integration `CreditSimulatorTests.R42_ATotalEndingIn99IsRejectedWithSimulatedCentsRule_EvenWithAmpleCredit_OverTheRealWire` (asserts `Assert.Empty(ledger)` **and** `Assert.Single(outboxRows)` **and** the payload's `Reason`, `RequestedAmount`, `AvailableCredit`) | **P1** (ordering swap) and **P3** (reason corrupted, killed at the wire) |
| **R43** (proportion, default 0, fail to start reporting the value) | `.R43_RejectsWithSimulatedFailureRate_OnlyWhenTheDrawFallsBelowTheConfiguredRate_AndNeverAtAZeroRate`, `.R43_ALoosenedBoundaryComparison_…`, `.R43_AFailureRateOfOneRejectsEveryNonCentsAmount`, **`.R43_TheConfiguredFailureRateIsMeasuredOverTwoHundredThousandDeterministicDraws`** (5 rates, injected `new Random(20260906).NextDouble`), `CreditSimulatorOptionsLoaderTests.*` (14 cases), `BillingHostCreditFailureRateBootTests.*`; integration `.R43_ANonCentsAmountIsRejectedWithSimulatedFailureRate_WhenTheConfiguredRateIsOne_OverTheRealWire` and `.R43_DefaultsToZero_SoAFittingNonCentsHoldIsApproved_OverTheRealWire` | **P2** (rate halved — the measured theory is the only thing that catches it), **P4** (default flipped to 1, killed at the wire), **P5** (loader default flipped to 0.5), **P6** (`IsFinite` guard removed); **P7 survived** — see A1 |
| **R44** (indistinguishable but for `reason`; R37 not bypassed) | `CreditHoldTests.BC14_EmitsACreditRejectedFactForAnAdapterRefusalThatDiffersFromTheOverLimitRefusalInTheReasonFieldAndInNothingElse` (pre-existing, feature 19) + integration `.R44_SimulatedAndGenuineRejectionsShareTheSameFactTypeAndPayloadKeySet_DifferingOnlyInReason` | Read the test: it parses **both** outbox `payload` columns with `JsonDocument`, sorts each one's own key names and compares **the two arrays to each other** — never to a hand-typed literal. This is exactly #7's finding **N1** closed by construction. **P3** killed it too, on the `reason` value |

Matrix hygiene: the three Status cells flipped are R42, R43, R44 and nothing else; the sketch column already reads `billing/infrastructure/credit-simulator.spec` (#7's finding **N6** was back-ported before the copy), so no `specs/shared/` prose was touched here. **C7 box 1 verified by real `diff -rq`, not from memory.**

---

## 4. My probes (7)

Every mutation was applied to a file backed up first with `cp`, restored with `cp` from that backup (**never `git checkout --`**), verified `sha256sum`-identical, `touch`ed, and followed by `dotnet build --no-incremental` before the confirming run.

| # | Mutation | Family | Result |
|---|---|---|---|
| **P1** | **Swap the two `if` blocks** so the failure-rate draw is evaluated before the `.99` predicate (`SimulatorCreditDecision.cs:60/70`) | ordering | **KILLED, and precisely** — `Failed: 1, Passed: 135`. Only `TheCentsRuleWinsOverTheFailureRateRuleWhenBothCouldApply` fell: `Assert.Equal() Failure: Values differ`. Every other R42 test is written at `failureRate = 0` and is insensitive to the swap, which is the correct discrimination: the ordering property has exactly one guard and that guard is not a coincidence |
| **P2** | Halve the effective rate (`_random() < _failureRate * 0.5`) | **value-wrong** | **KILLED, 6 failed / 130 passed** — including all three interior rates of the measured theory: `Assert.InRange() Failure: Value not in range / Range: (0.09 – 0.11)`, `(0.29 – 0.31)`, `(0.74 – 0.76)`, plus `rate = 1` on `Assert.Equal()`. No boundary test alone catches this; **the 200 000-draw measurement is the guard that does** |
| **P3** | Cents branch returns `AdapterRejectionReason.SimulatedFailureRate` — the fact is still emitted, only its **reason field is wrong** | **value-wrong, on the wire** | **KILLED at integration level, 2 failed / 2 passed** (real containers): `R42_…OverTheRealWire` and `R44_…DifferingOnlyInReason`, both `Expected: "simulated_cents_rule" / Actual: "simulated_failure_rate"`. The reason is genuinely **read** off the RPC reply and out of the outbox `payload` column, not merely counted |
| **P4** | `BillingOptions.CreditFailureRate` defaults to `1` instead of `0` | value-wrong, at the wiring | **KILLED, 1 failed / 3 passed** — only `R43_DefaultsToZero_SoAFittingNonCentsHoldIsApproved_OverTheRealWire`: `Expected: "approved" / Actual: "rejected"`. Acceptance bullet 2 is guarded end to end, not just in the loader |
| **P5** | Loader returns `0.5` for an absent/empty `CREDIT_FAILURE_RATE` | value-wrong | **KILLED, 2 failed / 134 passed** — `LoadsTheConfiguredRate_DefaultingToZeroWhenAbsentOrEmpty(raw: null)` and `(raw: "")` |
| **P6** | Delete the `!double.IsFinite(rate)` clause — **the one hand-built property the ported-idiom ledger names** | ledger claim | **KILLED, 1 failed / 135 passed** — exactly `FailsToStart_ReportingTheOffendingValue_…(raw: "NaN")`. `"Infinity"`/`"-Infinity"` are caught by the range comparison, so `NaN` is the whole load of that clause, and the theory case for it exists. The ledger row is not decorative |
| **P7** | Delete `options.CreditFailureRate = CreditSimulatorOptionsLoader.Load(Environment.GetEnvironmentVariable("CREDIT_FAILURE_RATE"));` from `src/Billing/Program.cs:23` | emission absence, at the production call site | **SURVIVED — build succeeded, `Billing.UnitTests` 136/136 green.** See finding **A1**: no test project in this repository compiles any `Program.cs`, so this is pre-existing structural state rather than a defect feature 20 authored |

---

## 5. The ported-idiom ledger — claims checked, not counted

`sdd: false`, so there is no `design.md`; the ledger lives in `progress/impl_billing_credit_simulator.md` §"Ported-idiom ledger" and, more durably, in the XML doc comment on `CreditSimulatorOptionsLoader` (`SimulatorCreditDecision.cs:91-113`). That is the right place for it — a `progress/` file is not read as normative, a doc comment on the class is.

It carries one row: *"#7 relied on a hand-written plain-decimal-numeral regex before `Number()`, because JS coerces `'0x1'` → `1` and whitespace → `0`; in #8 that property is supplied by `double.TryParse` under `NumberStyles.Float` — except for `NaN`/`Infinity`, which parse successfully in **both** runtimes and are therefore still hand-guarded by `double.IsFinite`."*

I checked the claim rather than its existence. The theory `FailsToStart_…` covers `"0x1"`, `"  "`, `"abc"`, `"1,000"`, `"NaN"`, `"Infinity"`, `"-Infinity"`, `"1.5"`, `"-0.1"` and passes, so the "no regex needed" half is a **tested** claim, not an assumed one. The hand-built half — `IsFinite` — is the row most likely to be assumed, so it is the one I mutated (**P6**): it has a guard and the guard fails when the property is removed. #7's finding **N3** (the `Number()` coercion quirks it shipped unfixed) is therefore closed here, and closed by the ledger doing exactly the job it was adopted for.

One deliberate, disclosed divergence: `"1e0"` and `"+0.5"` are **accepted** here and were rejected by #7's stricter regex. R43 says "a number in the closed interval `[0, 1]`" and fixes no numeral shape, so this is inside the requirement. It is disclosed in the doc comment and in the report. Not a defect; it is the kind of difference that must be written down rather than discovered, and it was.

---

## 6. Backlog id 55 — the honesty check

**The implementer's verdict is honest, and id 55 stays `pending`.** Its six sites are `tests/Billing.IntegrationTests/CreditListTests.cs:39,73,77` and `tests/Fulfillment.IntegrationTests/StockListTests.cs:33,38,44`. `git status --porcelain` on both paths returns **empty** — neither file was opened by this feature, the new integration file is `CreditSimulatorTests.cs`, and nothing in the diff references `credit.list` or `stock.list`. The entry itself is byte-unchanged in `feature_list.json` (the only line that moved there is id 20's status). The instruction was *"close it only if your work genuinely opened that file, otherwise say so and leave it alone"*; the report says so plainly and leaves it alone. **I have not set id 55 `done`.** It remains attached to feature 21 (`billing_invoicing`), which is the next feature likely to open `CreditListTests.cs`.

---

## 7. Did the feature stay small?

**Yes, and measurably so.** 42 insertions / 13 deletions across 8 tracked files, plus one 135-line adapter (of which ~75 lines are doc comment) and two test files. No new abstraction, no new interface, no change to `AdapterRejectionReason`, to `CreditRejectionReason`, to `BuyerCredit`, to the mapper, to the responder or to any repository. The only structure added beyond #7's shape is `BillingOptions.CreditFailureRate` — one auto-property whose existence is forced by #8's options object, where #7 read its config directly in a `useFactory`. #7's record calls this feature *"the smallest possible proof that feature 19's port was cut in the right place"*; that is still what it is.

Where #8 **is** larger than #7 is in tests, deliberately and in the right direction: the 200 000-draw measured theory and the boot-level `BillingHostCreditFailureRateBootTests` are **committed tests here**, whereas in #7 the equivalent measurement was a reviewer's throwaway probe (P2) that left the repository the moment the review ended. A guard that ships is worth more than a probe that does not, and it is the direct cause of P2 above being killed rather than surviving.

---

## 8. Findings (3, none blocking)

**A1 — R43's start-up clause is guarded everywhere except at the line that actually runs in production.** `src/Billing/Program.cs:23` is the only place `CREDIT_FAILURE_RATE` is ever read. **P7 deleted that line and the suite stayed green** (`Billing.UnitTests` 136/136, build clean). The named guard in the matrix, `BillingHostCreditFailureRateBootTests.CreateBuilder_ThrowsBeforeBuild_…`, writes its **own** `configure` delegate and calls the loader itself, so it proves the loader throws from inside a configure delegate before `Build()` — a real property — but it cannot notice if the shipped composition stops calling the loader at all. **Why it matters:** this is the exact shape the harness exists to catch — a countable claim ("the service fails to start") whose guard cannot fail when the behaviour is removed. **Why it is not blocking:** it is not this feature's defect. `grep -rln "GetEnvironmentVariable" tests/` returns **one** file, and it is unrelated; **34 env reads across `src/Orders/Program.cs` (11), `src/Billing/Program.cs` (12) and `src/Fulfillment/Program.cs` (11) are all equally unguarded**, and no test project in the repository compiles any `Program.cs`. #7 was in the same position and its reviewer said so explicitly (*"verified by reading the wiring; I did not re-boot the live stack"*). Recommended backlog wording for the leader: *"no test reaches any service's `Program.cs` env wiring; deleting a `GetEnvironmentVariable` line leaves every suite green. Either the composition root is exercised by a test that sets the variable and asserts the resulting option, or the reads move into a testable `*HostOptionsLoader` that `Program.cs` merely calls — acceptance: the deletion of the production read is killed by a named test, in at least Billing, Orders and Fulfillment."* Three more services (Notifications, Projector, Gateway) will add to this surface in phases 11–13, which is why it is worth filing now rather than at the end.

**N1 — the report's headline test total is wrong: 873, not 883.** `progress/impl_billing_credit_simulator.md:229` states *"**883 total, 0 failed**"*, but its own per-project itemisation on the two lines above sums to **873**, and the implementer's own `quality.sh` log agrees: `grep -oP "Passed:\s+\K\d+(?=, Skipped)" quality.log | paste -sd+ | bc` → **873**, with `Failed!` appearing **0** times. The cross-check confirms which number is right: feature 19 closed at 827, and this feature adds 42 Billing unit + 4 Billing integration = 46 → 873. **Why it matters:** it is a countable claim, it is the number that flows into `progress/history.md`, `README.md` and `docs/PROCESS.md` at wrap-up, and a benchmark whose test counts drift by ten is a benchmark nobody can audit later. Not blocking — the suite is green either way and every per-project figure is correct — but the total must read **873** wherever it is quoted. (This is the one finding #7's standard would have caught: #7's reviewer re-ran everything and compared counts.)

**N2 — #7's finding N2 was inherited as a one-off search, not as the durable guard N2 asked for.** The report's §"Fixture-amount safety" records `grep -rn "new CreditMoney(" tests/Billing.IntegrationTests/*.cs`, 15 amounts, all safe. I re-enumerated independently and more widely — every integer literal in every Billing integration test file, flagged where `≡ 99 (mod 100)` — and the only hits are the **intentional** `24_999`s in `CreditSimulatorTests.cs` (the other hits are the order reference `ORD-000099` and prose in comments). So the claim is true today, and the full 55/55 integration run I did myself is the empirical proof. But #7's N2 was specifically about **rot**: *"the protection is a prose list … which rots the moment a fifth spec is added"*, and its suggested fix was a cheap durable guard (a test asserting no integration amount literal is `≡ 99 (mod 100)`, or a `harness.Amount()` helper that refuses such values). #8 reproduced the search and the prose, not the guard. **Why it matters:** with the simulator now the unconditional binding for every Billing integration test, the next implementer who picks `19_999` gets an inexplicable `simulated_cents_rule` in a test about invoicing — and feature 21 is next. Non-blocking because the failure is loud rather than silent, and because the fix belongs to whoever opens those files next; recommended for the leader's backlog rather than a rejection round.

For the record, the other four of #7's six findings **were** genuinely inherited: **N1** is closed by construction (key set compared to key set, never to a literal — `CreditSimulatorTests.cs:173-175`), **N3** by the ledger and the `IsFinite` guard (P6), **N5** by the retention comment being trimmed to the honest claim (`BillingServiceCollectionExtensions.cs:44-47` — "reference implementation and a future harness's override point", which is the wording #7 asked for), and **N6** by the sketch column already reading `billing/infrastructure/…` in the copied matrix. **N4** was a #7-internal finding the #7 leader disproved; nothing to inherit.

---

## 9. `CHECKPOINTS.md` — walked

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`)
- [x] every agent definition declares its model — `init.sh` §2 all OK
- [x] `./init.sh` exits 0 — **my run**, exit 0

### C2 — state is coherent
- [x] at most one feature `in_progress` — **zero**; id 20 was `in_review` and is set `done` by this review
- [x] every status is in `rules.valid_status` — `init.sh` §3 OK
- [x] every `done` feature has passing tests — Billing 136 unit + 55 integration + Architecture 16 re-run here; the other ten projects verified from the implementer's `quality.sh` log, unreachable by this diff
- [x] `progress/current.md` describes the active session — updated by this review to name the closed feature, per the lockstep rule `init.sh` §4 enforces
- [x] no `blocked` features

### C3 — architecture is respected
- [x] no EF Core / Confluent.Kafka / NATS / MongoDB / ASP.NET reference inside any `Domain/` — **`Architecture.Tests` 16/16, run, not eyeballed**
- [x] no cross-service DB access — this feature performs no database access at all; the adapter has no dependencies but a `double` and a `Func<double>`
- [x] no shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — no new shared code; the adapter lives in `src/Billing/Infrastructure/`
- [x] no `Domain/` namespace references `OrderToCash.Cqrs` — unchanged, architecture suite green
- [x] `src/SharedKernel` still has zero `PackageReference` — untouched by this diff
- [x] no `decimal` in domain arithmetic — `grep -rn "\bdecimal\b" src/Billing/Domain/` returns nothing; the new `double` is a **probability**, not money, and every money value on this path stays `long` minor units
- [x] every interaction is Kafka-fact or NATS-RPC — **no new interaction**: an in-process port call inside the existing `billing.credit.hold` NATS-RPC handler, whose fact reaches Kafka through the pre-existing outbox
- [x] no stray debug logging, no context-free TODOs — grep of all six touched source/test files: zero `TODO`, `FIXME`, `Console.`, `Debug.Write`

### C4 — verification is real
- [x] format check + build + test pass — `dotnet format --verify-no-changes` exit 0 (mine), `dotnet build` 0 warnings / 0 errors (mine), 873 tests green across 13 projects (implementer's `quality.sh` log, per-project figures verified)
- [x] domain/unit tests are pure — `SimulatorCreditDecisionTests.cs` imports only `Xunit` and two local namespaces; no DB, no broker, no mock framework, randomness **injected** and `Random.Shared` appearing only in doc prose
- [x] integration tests use Testcontainers against real MS-SQL / NATS / Kafka — `CreditSimulatorTests` starts a real host and a real `NatsConnection`, reads the real outbox table; **P3 and P4 prove they execute the real binding**, because mutating production code failed them
- [ ] coverage thresholds ≥80% domain / ≥60% overall — **not enforced anywhere yet**; `quality.sh` defers the gate to feature 34 and says so in its own header. Pre-existing project state, unchanged by this feature, and the same box the last two reviews left open for the same reason
- [x] no Jest anywhere

### C5 — the session closed cleanly
- [x] no suspicious untracked files — the four untracked paths are this feature's own artefacts; my seven mutations are restored and `sha256sum`-verified, my scratch files live outside the repository
- [x] `progress/history.md` has an entry with its **effort record** — appended by this review, §10
- [x] `feature_list.json` reflects true state — id 20 → `done`, **one line changed**, verified with `git diff`
- [x] the human will be told what was done and how to test it manually — from the impl report's verification section, via the leader
- [x] Claude did not commit — no `git commit`, no `git push`, and no `git checkout --` on any file in this review

### C6 — Spec-Driven Development
- n/a for this feature: `"sdd": false`, so no `specs/billing_credit_simulator/`, no spec phase and no human gate — by design, exactly as #7. The global boxes still hold: `init.sh` reports *"SDD coherence: 5 sdd feature(s) past pending have their triple-doc"*
- [x] every `R<n>` is covered by at least one concrete named test recorded in `specs/shared/test-matrix.md` — R42/R43/R44 flipped `TODO` → `DONE` with real class and method names, each verified by a mutation above

### C7 — spec-reuse fidelity and benchmark honesty
- [x] **`specs/shared/` still byte-identical to #7's apart from `test-matrix.md`'s Status column** — verified by real `diff -rq` against the #7 checkout this session; the only file that differs is `test-matrix.md`, and this session's edit is confined to three Status cells
- [x] every deviation is recorded — the one behavioural divergence from #7 (accepting `"1e0"` / `"+0.5"`) is inside R43's own wording and is disclosed in the shipped doc comment, not just in `progress/`
- [x] the `R<n>` ids are #7's, and the .NET realisation genuinely satisfies them — checked by mutation, not by label
- n/a `n8n/workflows/*.json` — untouched; the `.99` affordance they depend on is now live, which strengthens rather than changes them
- n/a the black-box API script — not this feature's surface (Gateway, phase 13); the affordance those tests need is what this feature ships
- [x] `progress/history.md` effort records complete and honest, **including the confound column and a ratio that is not flattering** — §10
- n/a README benchmark section — leader-owned, at wrap-up

---

## 10. Effort record (appended to `progress/history.md`)

**Sessions:** 1 implementation session + 1 review pass (this one). No spec session, no human gate — `sdd: false`, matching #7 exactly.

**Wall-clock, from artefact mtimes (local CEST, 2026-09-06),** bracketed at the start by the previous commit `19c623e` (*docs(process): PROCESS.md and README after the credit service*) at **05:10**:

- **Implementation ≈05:41 → 06:15, ≈34 min** — `progress/current.md` 05:41:30, `Program.cs` 05:47:36, `.env.example` 05:47:43, `SimulatorCreditDecisionTests.cs` 05:50:58, `CreditSimulatorTests.cs` 05:52:09, `test-matrix.md` 05:55:26, `BillingServiceCollectionExtensions.cs` 06:01:53, `feature_list.json` 06:03:04, `quality.sh` log 06:12:35, impl report 06:15:02. On the same bracket #7 used (previous commit → impl report) the figure is **≈65 min**, of which ≈31 min elapsed between the commit and the first artefact write.
- **Review ≈06:16 → ≈06:45, ≈29 min** — of which ≈5 min is Testcontainers wall-clock (the full 55-test Billing integration run at 3 m 45 s plus two filtered mutated runs at ~15 s each) and ≈4 min is seven `--no-incremental` rebuilds.
- **Total ≈05:41 → ≈06:45, ≈1 h 04 min** from first artefact to verdict; **≈1 h 35 min** on the commit bracket.

*Note, as #7's own record did: `SimulatorCreditDecision.cs`, `BillingOptions.cs` and `Program.cs` now carry review-time mtimes (06:17–06:31) from my mutation restores. All three are `sha256sum`-identical to the submitted versions and those timestamps are not implementation activity.*

**Against #7's baseline** (≈20 min implementation on the commit bracket, ≈18 min review, ≈39 min total, first-pass approval):

| Bucket | #8 | #7 | Ratio |
|---|---|---|---|
| Implementation, commit-bracketed | ≈65 min | ≈20 min | **≈3.3×** |
| Implementation, first-artefact-to-report | ≈34 min | ≈15 min | **≈2.3×** |
| Review | ≈29 min | ≈18 min | **≈1.6×** |
| **Total, first artefact → verdict** | **≈1 h 04 min** | **≈39 min** | **≈1.6×** |

**This is one of the few clean comparisons in the build — same scope, same `sdd: false` process, same first-pass approval — and #8 is slower on every bucket.** The honest reading, without softening it in either direction: roughly half the gap is work #8 chose to do that #7 did not (the 200 000-draw measurement is a **committed test** here and was a throwaway reviewer probe there; the boot-level test class, the ledger row and its `IsFinite` guard, and the explicit re-verification of four inherited #7 findings have no #7 counterpart), and roughly half is the ≈31 min between the previous commit and the first artefact, which is dispatch overhead this harness pays per feature and #7 paid in 5 min. Neither half is a language effect. What is **not** available as an excuse: the seam was pre-cut by feature 19 in both assessments, so the "design.md predicted the footprint" advantage is common to both and cancels.

**Confound column — for each defect, would #7's standard have caught it?**

| Finding | #7's standard? | Why |
|---|---|---|
| **N1** (873 vs 883) | **Yes** | #7's reviewer re-ran every suite and compared per-project counts to the claim; this is that check, and it fired |
| **A1** (production env read unguarded) | **No — raised bar** | #7's reviewer explicitly declined the equivalent check (*"verified by reading the wiring; I did not re-boot the live stack"*). It took a mutation of a production line no test project compiles. It is also **not** a #8-vs-#7 language difference: #7's call site had no test setting the variable either |
| **N2** (N2 inherited as a search, not a guard) | **Yes, in the weak sense** | #7 raised it; #8 was told to inherit it and inherited half. Catching the half-inheritance required diffing the finding's suggested fix against what shipped |

One of three defects is attributable to the harness rather than the language; the other two are bookkeeping and inheritance discipline. Nothing here is a .NET penalty, and nothing here is a #8 win either — the ratio is what it is.

---

## 11. Integrity of the tree after this review

Seven mutations across three files, every one restored from a `cp` backup and `sha256sum`-verified against the submitted version:

```
e8ba51fc7f04136a5e67ca80c0d24ee49bc1527fd262daa336a6c4c38e711f57  src/Billing/Infrastructure/CreditDecisions/SimulatorCreditDecision.cs
644c1aaf55f8868cffbebbf35060eb7c191be00dc5ab2ba249bedbe88fd349da  src/Billing/Infrastructure/BillingOptions.cs
d5ddffe503cc042edc5605a7854ae950f2c138b64a94533350c83b79f37d5bfb  src/Billing/Program.cs
```

Final confirming runs after the last restore and a forced rebuild: `Billing.UnitTests` **136/136**, `Billing.IntegrationTests` **55/55**, `Architecture.Tests` **16/16**, `dotnet format` clean, `./init.sh` exit 0. `git status --porcelain` is identical to the pre-review state apart from this file, `feature_list.json`'s single status line, `progress/history.md`'s appended entry and `progress/current.md`'s two-line close.

**Verdict: APPROVED.** `billing_credit_simulator` → `done`. Two items are handed to the leader as backlog wording (**A1**, **N2**) and one as a correction to carry into the wrap-up documents (**N1**: the total is **873**).
