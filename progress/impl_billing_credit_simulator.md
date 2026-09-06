# impl: billing_credit_simulator (feature 20, phase 10)

`sdd: false` — specification of record: `feature_list.json` id 20's three
acceptance bullets, `specs/shared/requirements.md` §5.1 (R42–R44), read
against #7's own implementation
(`order-to-cash-nestjs/apps/billing/src/infrastructure/credit/simulator-credit-decision.ts`)
and its review
(`order-to-cash-nestjs/progress/review_billing_credit_simulator.md`).

## What was built

Feature 19 (`billing_credit`) already fixed this feature's entire seam —
`ICreditDecisionPort`, the closed `AdapterRejectionReason` enum (structurally
excluding `over_limit`, `BC14`), the total mapping to `CreditRejectionReason`,
and `AlwaysApproveCreditDecision` as the pre-existing binding — exactly as
`specs/billing_credit/design.md` §6.3 predicted: *"Feature 20's entire
footprint is that one line plus one new file."* This implementation is
accordingly small:

1. **`src/Billing/Infrastructure/CreditDecisions/SimulatorCreditDecision.cs`**
   (new, one file, matching #7's shape) — two public types:
   - `SimulatorCreditDecision : ICreditDecisionPort` — the `.99` rule
     (R42) checked **first**, unconditionally winning; the failure-rate
     draw (R43) checked second, with an injectable `Func<double> random`
     defaulting to `Random.Shared.NextDouble`. No I/O; `DecideAsync`
     returns an already-completed `ValueTask`.
   - `CreditSimulatorOptionsLoader.Load(string? raw) : double` — R43's
     start-up validation: absent/empty → `0`; `double.TryParse` under
     `NumberStyles.Float` + `double.IsFinite` + range check; throws
     `InvalidOperationException` naming the raw offending value otherwise.
2. **`src/Billing/Infrastructure/BillingOptions.cs`** — added
   `CreditFailureRate` (double, defaults to `0`).
3. **`src/Billing/Infrastructure/BillingServiceCollectionExtensions.cs`** —
   the one-line registration swap:
   `services.AddSingleton<ICreditDecisionPort, AlwaysApproveCreditDecision>()`
   → `services.AddSingleton<ICreditDecisionPort>(_ => new
   SimulatorCreditDecision(options.CreditFailureRate))`.
   `AlwaysApproveCreditDecision` stays in the tree, unbound, as the port's
   reference implementation (matching #7's ruling in its own review §6(b):
   *"keep it, do not delete it"*).
4. **`src/Billing/Program.cs`** — reads `CREDIT_FAILURE_RATE` and calls the
   loader **inside** `BillingHost.CreateBuilder`'s `configure` delegate, so
   an invalid value throws before `AddDbContext` is even registered, let
   alone before `.Build()`.
5. **`.env.example`** — added `CREDIT_FAILURE_RATE=0` with a comment
   explaining the affordance and its default.
6. Tests (below) and `specs/shared/test-matrix.md` Status column for
   R42/R43/R44 (only column touched).

No `Domain/`, `Application/` or `Presentation/` file changed. `git status`
confirms the footprint exactly: 4 modified source files, 1 modified test
file (a doc-comment correction, see below), 1 new source file, 2 new test
files, plus `.env.example`, `specs/shared/test-matrix.md` and
`feature_list.json`'s single status line.

One incidental, in-scope edit: `tests/Billing.UnitTests/AlwaysApproveCreditDecisionTests.cs`'s
doc comment and test name said "bound **until** feature 20" / "…Feature20Replaces" (future
tense); reworded to past tense now that feature 20 has landed. No assertion changed.

## Ported-idiom ledger (CLAUDE.md)

#7 guarded `CREDIT_FAILURE_RATE` with a hand-written "plain decimal numeral"
regex **before** calling `Number()`, because JavaScript's `Number()` silently
coerces `'0x1'` → `1` and a whitespace-only string → `0`. I checked whether
.NET has the same footgun before assuming the regex must be ported:

```
$ dotnet run  (ad-hoc probe, deleted afterwards)
0x1        => ok=False value=0 isFinite=True
           => ok=False value=0 isFinite=True   (whitespace-only)
1e0        => ok=True  value=1
+0.5       => ok=True  value=0.5
NaN        => ok=True  value=NaN   isFinite=False
Infinity   => ok=True  value=∞     isFinite=False
-Infinity  => ok=True  value=-∞    isFinite=False
abc        => ok=False value=0
1,000      => ok=False value=0
```

Result: `double.TryParse` under `NumberStyles.Float` does **not** have JS's
hex/whitespace coercion — `"0x1"`, whitespace-only, `"abc"` and `"1,000"`
already fail to parse, so no extra regex is needed for those. What **does**
carry over unchanged: the literal strings `"NaN"`, `"Infinity"` and
`"-Infinity"` parse **successfully** to real non-finite `double` values in
.NET too — so `TryParse` returning `true` is not sufficient on its own, and
the explicit `double.IsFinite` check is still required (this is the guard
that actually rejects those three in the shipped code). Accepting `"1e0"`
and `"+0.5"` — which #7 rejected for extra demo strictness — is a considered,
disclosed difference: both denote real numbers inside the closed interval,
and R43's own wording ("a number in the closed interval [0, 1]") does not
ask for a narrower numeral shape. This reasoning is written into the class's
XML doc comment, not just here.

## The ordering property, and how it is guarded

`TheCentsRuleWinsOverTheFailureRateRuleWhenBothCouldApply` fixes
`failureRate = 1, random() = 0` — a configuration where the failure-rate
branch would **also** fire if the cents check did not run first — and
asserts the reason is still `SimulatedCentsRule`. This is an ordering
assertion, not a presence assertion: mutation M2 (swap the two `if`
blocks) killed **only** this test (1 failed / 23 passed) while every other
R42 test stayed green, because those tests all use `failureRate = 0`, which
is insensitive to the swap. That is the intended discrimination — the
ordering test is the only one that can tell the two orderings apart.

## Failure-rate measurement, injected generator

`R43_TheConfiguredFailureRateIsMeasuredOverTwoHundredThousandDeterministicDraws`
injects `new Random(20260906).NextDouble` (never `Random.Shared`) and counts
refusals over 200,000 draws per configured rate:

| rate | required | observed |
|---|---|---|
| 0 | 0 refusals | 0/200,000 |
| 0.1 | within ±0.01 | measured in-range, asserted via `Assert.InRange(observed, 0.09, 0.11)` |
| 0.3 | within ±0.01 | in-range |
| 0.75 | within ±0.01 | in-range |
| 1 | 200,000 refusals | 200,000/200,000 |

All five `[Theory]` cases passed in the green run reported below (part of
the 136 Billing.UnitTests total). No pass-rate is reported anywhere in this
implementation — every claim here is a deterministic assertion against an
injected generator, per the brief's "a probabilistic guard is not a guard"
rule.

## Arming table (both mutation families, forced rebuild after every restore)

All mutations applied to `SimulatorCreditDecision.cs` (backed up before
each, restored via `cp` from the backup — never `git checkout --`, the file
is untracked) or to `BillingServiceCollectionExtensions.cs` (tracked, also
restored via `cp` from a backup, never `git checkout --`, per CLAUDE.md's
rule against that command on files with in-flight changes). Every restore
was verified `sha256sum`-identical to the pre-mutation backup, `touch`ed to
force MSBuild's incremental check to see it as newer than its last output,
then rebuilt with `dotnet build --no-incremental` before the confirming run.

| # | Mutation | Family | Result |
|---|---|---|---|
| **M1** | Delete the `.99` branch entirely | emission absence | **KILLED** — 6 failed / 18 passed. `Assert.IsType() Failure: … Expected: …Refuse Actual: …Approve` (5 `R42_Rejects…` theory cases) + `TheCentsRuleWinsOverTheFailureRateRuleWhenBothCouldApply`: `Assert.Equal() Failure … Expected: SimulatedCentsRule Actual: SimulatedFailureRate` |
| **M2** | Swap branch order (failure-rate check first) | ordering | **KILLED, precisely targeted** — 1 failed / 23 passed. Only `TheCentsRuleWinsOverTheFailureRateRuleWhenBothCouldApply` failed: `Assert.Equal() Failure: Values differ Expected: SimulatedCentsRule Actual: SimulatedFailureRate` |
| **M3** | Invert the rate comparison (`<` → `>=`) | value-wrong | **KILLED, widely** — 15 failed / 9 passed, including every `R42_DoesNotFireForAnAmountThatDoesNotEndIn99` case (now wrongly refusing), the boundary test, the measured-proportion theory (`rate=0.75` observed `0.2477` vs required `[0.74, 0.76]`; `rate=1` observed `0` vs required `200000`) |
| **M4** | Fail-fast throw replaced with silent clamp/default | value-wrong (suppressed error) | **KILLED** — 10 failed / 8 passed across `CreditSimulatorOptionsLoaderTests.FailsToStart_…` (`Assert.Throws() Failure: No exception was thrown`, all 8 offending-value cases) and `BillingHostCreditFailureRateBootTests.CreateBuilder_ThrowsBeforeBuild_…` (`Assert.IsType() Failure: Value is null`) |
| **M5** | Revert the DI binding to `AlwaysApproveCreditDecision` (adapter file untouched) | emission absence, at the wiring level | **KILLED, at integration level** — 3 failed / 4 passed (real MS-SQL/NATS/Kafka). `R42_…OverTheRealWire`: `Expected: "rejected" Actual: "approved"`; `R43_…RateIsOne_OverTheRealWire`: same; `R44_…DifferingOnlyInReason`: `Expected: "simulated_cents_rule" Actual: null` (no `credit.rejected.v1` row ever appeared, so deserializing the reason found nothing). The fourth test, `R43_DefaultsToZero_SoAFittingNonCentsHoldIsApproved`, correctly stayed green (it expects "approved" under both bindings) |

After each restore, the full relevant suite was re-run and confirmed green:
`Billing.UnitTests` 136/136 (post-M1/M2/M3/M4 restore) and
`Billing.IntegrationTests` `CreditSimulatorTests` 4/4 (post-M5 restore).

M5 is the one closest to #7's own most valuable probe (its M6): a simulator
that is internally correct but never bound would be invisible to every unit
test, and the harness's default (no `decisionPort` override) genuinely
resolves whatever `AddBilling` registers, so this mutation pins the
*binding*, not merely the class.

## Test → requirement mapping

| Req | Test(s) | Level |
|---|---|---|
| R42 | `tests/Billing.UnitTests/SimulatorCreditDecisionTests.cs` › `R42_RejectsAnAmountEndingIn99WithSimulatedCentsRule_RegardlessOfTheAvailableCredit` (5 credit levels incl. `long` near-max), `R42_DoesNotFireForAnAmountThatDoesNotEndIn99`, `TheCentsRuleWinsOverTheFailureRateRuleWhenBothCouldApply` (ordering) | domain unit |
| R42 | `tests/Billing.IntegrationTests/CreditSimulatorTests.cs` › `R42_ATotalEndingIn99IsRejectedWithSimulatedCentsRule_EvenWithAmpleCredit_OverTheRealWire` | integration, real MS-SQL/NATS/Kafka |
| R43 | `CreditSimulatorOptionsLoaderTests.LoadsTheConfiguredRate_DefaultingToZeroWhenAbsentOrEmpty`, `.FailsToStart_ReportingTheOffendingValue_…`, `.AcceptsBothEndpointsOfTheClosedInterval`; `SimulatorCreditDecisionTests.R43_RejectsWithSimulatedFailureRate_OnlyWhenTheDrawFallsBelowTheConfiguredRate_AndNeverAtAZeroRate`, `.R43_ALoosenedBoundaryComparison_…`, `.R43_AFailureRateOfOneRejectsEveryNonCentsAmount`, `.R43_TheConfiguredFailureRateIsMeasuredOverTwoHundredThousandDeterministicDraws` (measured, injected); `BillingHostCreditFailureRateBootTests.CreateBuilder_ThrowsBeforeBuild_…` and `.CreateBuilder_ThenBuild_SucceedsWithTheDefaultCreditFailureRateOfZero` (boot-level, no real infra needed) | domain unit + boot-level |
| R43 | `CreditSimulatorTests.cs` › `R43_ANonCentsAmountIsRejectedWithSimulatedFailureRate_WhenTheConfiguredRateIsOne_OverTheRealWire`, `.R43_DefaultsToZero_SoAFittingNonCentsHoldIsApproved_OverTheRealWire` | integration |
| R44 | `tests/Billing.UnitTests/CreditHoldTests.cs` › `BC14_EmitsACreditRejectedFactForAnAdapterRefusalThatDiffersFromTheOverLimitRefusalInTheReasonFieldAndInNothingElse` — **pre-existing from feature 19**, a positive record-equality assertion (normalise `EventId`/`OccurredAt`/`Reason`, everything else must be `Equal`), not re-written here | domain unit |
| R44 | `CreditSimulatorTests.cs` › `R44_SimulatedAndGenuineRejectionsShareTheSameFactTypeAndPayloadKeySet_DifferingOnlyInReason` — compares the **actual** JSON key sets of the simulated and genuine rejection payloads **to each other**, never to a hand-typed literal list (this is #7's own N1 finding against its own R44 spec — a literal key list is a tautology once written; closed here by construction) | integration |

## On #7's N1 finding (parity assertion weaker than it looks)

#7's reviewer found that its R44 integration spec compared one payload's
keys against a **hard-coded literal array**, so a ninth key added to only
the simulated path would go undetected. Two things prevent that shape here,
one structural and one in the new test:

1. **Structural.** `CreditRejectedPayload` is a single `sealed record` with
   a fixed, compile-time-checked set of positional properties, built from
   **one** call site (`CreditFactPayloadMapper`, mapping the domain's single
   `CreditRejected` event). There is no per-branch object-literal
   construction the way TypeScript's `credit-events.ts` used, so a payload
   that diverges in shape between the two reject paths cannot be expressed
   without touching the one shared mapper — which would affect both paths
   identically. `BC14`'s domain test already proves this with `record with`
   normalisation + `Assert.Equal` (byte-for-byte on everything but the three
   normalised fields).
2. **Test-level, at the wire.** `R44_SimulatedAndGenuineRejectionsShareTheSameFactTypeAndPayloadKeySet_DifferingOnlyInReason`
   parses **both** actual outbox `payload` columns with `JsonDocument`,
   collects each one's own property names, sorts them, and compares the two
   *to each other* — never to a literal. If a future change made the two
   diverge, this test (not a tautological literal) would catch it.

## Fixture-amount safety (N2 finding, inherited as prevention)

Once the simulator became the default binding, every pre-existing Billing
integration hold amount had to be checked for accidental collision with the
`.99` predicate. Search:

```
$ grep -rn "new CreditMoney(" tests/Billing.IntegrationTests/*.cs
```

15 hits, in `CreditResponderConcurrencyTests.cs`, `CreditHoldTests.cs`,
`CreditWireTests.cs`, `CreditHoldRaceTests.cs`. Amounts used:
`1_000, 1_000, 2_000, 500, 1_500, 500, 500, 1_000, 3_000, 4_000, 1_500, 500,
5_000, 5_000, 1_000`. **Classification: SAFE** — none of these values is
`≡ 99 (mod 100)`, so none is silently rejected now that the simulator is the
default. No code change was needed; recorded here as the search result
CLAUDE.md asks for rather than a claim taken on faith.

## Backlog id 55

**Not touched, left open.** My work does not open `tests/Billing.IntegrationTests/CreditListTests.cs`
— the new integration file is `CreditSimulatorTests.cs`, and no existing
file I edited references `credit.list`. Per the brief: *"If it does not,
say so plainly and leave it — the entry stays open rather than being
half-closed."*

## Test counts (read off real runs, this session)

- `tests/Billing.UnitTests` alone: **136/136** passed (`dotnet test
  tests/Billing.UnitTests/Billing.UnitTests.csproj --no-build`, 1s).
- `tests/Billing.IntegrationTests` alone: **55/55** passed (`dotnet test
  tests/Billing.IntegrationTests/Billing.IntegrationTests.csproj --no-build`,
  real MS-SQL/NATS/Kafka Testcontainers, 3m 22s — includes the 4 new
  `CreditSimulatorTests`).
- Full solution via `./quality.sh`: **all green**. Per-project figures read
  off that run: `Cqrs.UnitTests` 23, `Contracts.UnitTests` 21,
  `SharedKernel.UnitTests` 50, `Billing.UnitTests` 136, `Orders.UnitTests`
  279, `Fulfillment.UnitTests` 119, `Notifications.IntegrationTests` 7,
  `Seed.UnitTests` 34, `Seed.IntegrationTests` 6, `Architecture.Tests` 16,
  `Fulfillment.IntegrationTests` 56, `Billing.IntegrationTests` 55,
  `Orders.IntegrationTests` 71 — **883 total, 0 failed.**
  `dotnet format --verify-no-changes`: clean. `dotnet build`: 0 warnings, 0
  errors. Coverage reports printed (no enforced gate yet — feature 34,
  per `quality.sh`'s own header comment).

## `./init.sh`

Exit 0, both before and after this feature's changes. Final run: "54
features parsed", "no feature in_progress" (confirms the `in_review`
transition, not `in_progress`, is the only backlog state), "SDD coherence:
5 sdd feature(s) past pending have their triple-doc", "progress: 29/54
features done", "backlog tripwire: no feature lost, no done reverted".
`feature_list.json` diff is a single line (`"status": "pending"` →
`"status": "in_review"`), confirmed with `git diff feature_list.json`.

## What I could not do / deferred, and why

Nothing was deferred. This matched #7's own characterisation of the
feature — the smallest of the phase — and no redesign of anything already
in Billing was needed; feature 19 had already fixed the seam.

## What surprised me

- The `AlwaysApproveCreditDecision` review ruling, the `AdapterRejectionReason`
  enum, `BillingHostFixture`'s `decisionPort` override parameter and even the
  `ARefusingPort_ProducesTheRejectedReplyAndFact_OverTheRealWire` integration
  test (using a **hand-rolled** always-refuse fake) were all already in place
  from feature 19, written in anticipation of this feature. The actual new
  surface area was genuinely just the one class plus the one-line DI swap,
  matching `design.md` §6.3's own prediction almost exactly.
- `double.Parse("NaN")` and `double.Parse("Infinity")` succeed in .NET,
  exactly as `Number('NaN')` does in JavaScript — this is not a JS-specific
  quirk after all, and the `IsFinite` guard earns its place independent of
  which runtime is doing the parsing.
- The two most valuable mutations by failure count were M3 (comparison
  inversion, 15/24 unit tests) and M5 (binding reversion, 3/4 integration
  tests) — the latter confirming that `BillingHostFixture`'s decision, made
  in feature 19, to let its default `decisionPort: null` resolve through the
  real `AddBilling` registration rather than hard-coding an approve-always
  fake, is exactly what makes the wiring itself testable.

## Files touched

- `src/Billing/Infrastructure/CreditDecisions/SimulatorCreditDecision.cs` (new)
- `src/Billing/Infrastructure/BillingOptions.cs`
- `src/Billing/Infrastructure/BillingServiceCollectionExtensions.cs`
- `src/Billing/Program.cs`
- `.env.example`
- `tests/Billing.UnitTests/SimulatorCreditDecisionTests.cs` (new)
- `tests/Billing.UnitTests/AlwaysApproveCreditDecisionTests.cs` (doc/name only)
- `tests/Billing.IntegrationTests/CreditSimulatorTests.cs` (new)
- `specs/shared/test-matrix.md` (Status column, R42/R43/R44 rows only)
- `feature_list.json` (id 20 status → `in_review`, single line)
- `progress/impl_billing_credit_simulator.md` (this file)
