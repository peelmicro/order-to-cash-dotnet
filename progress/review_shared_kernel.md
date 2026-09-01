# Review — feature 7, `shared_kernel`

**Verdict: REJECTED**

One blocking defect (**D1**), found by arming, in the single place the brief said to concentrate: the test that exists to prove `Money` offers no non-integer representation checks one third of the invariant it cites. Everything else in this feature is good work — the two defects already found and fixed during the feature are genuinely fixed and I re-armed both independently; the GLN check digits are real; R2, R3 and R4 all die when their guard is deleted; `quality.sh` is green; the disclosure of the `git checkout --` near-miss is exemplary. D1 is a small fix and this should come back quickly.

---

## 1. What I ran, and what I deliberately did not

**Ran (independently):**

- Full solution baseline before touching anything — `dotnet test OrderToCash.sln` → `SharedKernel.UnitTests` 31/31, `Architecture.Tests` 11/11.
- Eight arming probes of my own (table in §3), each mutation applied to source, suite run, source restored and re-verified byte-identical by `cmp` against a pre-edit copy.
- An independent GS1 mod-10 computation of every "valid" GLN in `GlnTests.cs`, in two textually-unrelated formulations (§4).
- `./quality.sh` end to end → **exit 0**, format clean, build clean, 31/31 + 11/11, line coverage 91.3% on the SharedKernel-covering run.
- `./init.sh` → exit 0, "no feature in_progress", "progress: 6/42 features done".
- `git diff specs/shared/test-matrix.md` and `git diff feature_list.json` against HEAD.
- A standalone probe binary against the real `SharedKernel.csproj` to observe `Quantity.From(1e18)`'s actual exception type (§7, D4).

**Did NOT re-run, and why:**

- **The leader's `decimal`-in-`Money` arming probe.** The brief recorded it as established (`public decimal LeaderArmingProbe` → `DomainDecimalTests` 1 failed / 0 passed, removal returns green). Repeating it is duplicated cost. I did arm `decimal` against a *different* guard — the R1 unit test — which the leader had not covered (probe P1).
- **The implementer's `Newtonsoft.Json` D3 arming (`Directory.Build.props` route).** Re-staging a temporary package reference across two central-package files is high-blast-radius for a claim I can assess from the code plus the implementer's negative result. I assessed the guard by reading it and by reasoning about `AssemblyRef` emission (§6), and I probed the *other* D3-adjacent claim — a forbidden framework reference in a SharedKernel **sub**-namespace — which is the case neither the implementer nor the leader had armed (probe P6).
- **Re-arming the five `DomainPurityTests` rules I did not touch** (EF Core, Kafka, NATS, MongoDB, ASP.NET Core). They are structurally identical single-line `HaveDependencyOn` calls over the *same* shared selector; my probe P6 exercised that selector against SharedKernel, which is the property this feature changed. My predecessor made the same call for the same reason in round 1 of feature 6.
- **`git` recovery verification of the `feature_list.json` near-miss.** The coordinator states it verified the tree; I confirmed only that `git diff feature_list.json` is exactly the two intended status lines, which it is.

---

## 2. `CHECKPOINTS.md` boxes walked

### C1 — The harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model — re-verified by `init.sh`'s own model-pin checks, all `[OK]`.
- [x] `./init.sh` exits 0.

### C2 — State is coherent

- [x] At most one feature `in_progress` — zero, in fact, at the moment of review (feature 7 was `in_review`).
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [x] `progress/current.md` holds the template, not leftovers.
- [x] No `blocked` features.

### C3 — Architecture is respected

- [x] No EF Core / Kafka / NATS / MongoDB / ASP.NET Core reference inside any `Domain/` folder — **verified by running the NetArchTest suite, and by arming it in a SharedKernel sub-namespace (probe P6), not by eye.** This box moves from `[ ]` (feature 6, blocked on D1/D2) to `[x]`: the selector now covers sub-namespaces *and* covers SharedKernel, and both properties are guarded by tests that I broke and watched fail.
- [x] No cross-service database access — n/a at this feature, nothing persists yet; `SharedKernel` references no store.
- [x] No shared runtime code beyond `src/SharedKernel` and `src/Contracts`.
- [x] `src/SharedKernel` still has zero `PackageReference` entries — `SharedKernel.csproj` is 12 lines and declares none; guarded twice (grep + compiled-assembly `AssemblyRef`).
- [ ] **No `decimal` in domain arithmetic — `Money` is `long` minor units; `decimal` only at presentation boundaries.** The `decimal` half is now genuinely guarded and now genuinely scans `Money` (that is this feature's own fix, and it is real). **The box stays open on D1**: `CLAUDE.md` states the rule as *"`long` minor units (cents) only. **Never a float**, never `decimal` in domain arithmetic"*, and `domain-model.md` §2.1 M1 states it as *"A decimal, **floating-point** or fixed-point major-unit representation is never used"*. A `double` major-unit accessor on `Money` passes the entire suite green (probe P2). I will not tick a box on a guard I have demonstrated does not guard — the same standard my predecessor applied to this same box in feature 6 round 1.
- [x] Every inter-service interaction classifiable as Kafka-fact or NATS-RPC — n/a, no interactions exist at this feature.
- [x] No stray debug logging; the two `TODO`s in the tree are context-full and name their owning feature.

### C4 — Verification is real (partially applicable)

- [x] `./quality.sh` passes — exit 0, verified by me after restoring every armed file.
- [x] Domain tests are pure — `SharedKernel.UnitTests.csproj` carries only the test SDK, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector` and one `ProjectReference` to `SharedKernel.csproj`. No DB, no broker, no mock framework, no other project.
- [ ] Integration tests use Testcontainers — **not applicable at this feature**, no integration tests exist yet. Left unticked rather than ticked vacuously.
- [ ] Coverage thresholds enforced — **feature 34's**, per `quality.sh`'s own `TODO(feature 34 — sonarqube_quality_gates, phase 21)`. The number is printed (91.3%) and is above the 80% domain bar, but the *gate* is deliberately inert, so the box is not tickable here.
- [x] No Jest anywhere — xUnit only; `grep -ri jest` over the repository returns nothing.

### C5 — The session closed cleanly

- [x] No suspicious untracked files. `git status --short` shows only expected untracked work (`src/`, `tests/`, `OrderToCash.sln`, the two props files, `quality.sh`, the three `progress/*.md`) plus three tracked modifications. `bin/`, `obj/` and `TestResults/` are gitignored. Every temporary file from my own probes was removed and the tree re-verified.
- [ ] **`progress/history.md` has an entry for the feature just finished, including its effort record.** Not applicable on a rejection — no entry is appended, and none should be. This box is deferred to re-review.
- [x] `feature_list.json` reflects the true state — set back to `in_progress` by this review.
- [ ] The human has been told what was done and how to test it manually — the leader's job on the rejection report.
- [x] **Claude did not commit.** No `git commit`, no `git push`, in this review or in the implementation session.

### C6 — Spec-Driven Development

**Not applicable.** Feature 7 is `"sdd": false`; the contract is `feature_list.json`'s 4-item `acceptance` array plus `specs/shared/{requirements,domain-model,test-matrix}.md`, exactly as #7 treated it.

### C7 — Spec-reuse fidelity (the assessment-specific section, walked for the files this feature touched)

- [x] **`specs/shared/` is still byte-identical to #7's, except `test-matrix.md`'s Status column.** Verified with a real `cmp` against the #7 checkout at `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`: `requirements.md`, `domain-model.md`, `saga.md`, `asyncapi.yaml`, `openapi.yaml` all IDENTICAL. `test-matrix.md` differs as designed.
- [x] **Only column 5 of rows R1–R4 changed in `test-matrix.md`.** `git diff` is exactly 4 insertions / 4 deletions, all four on rows R1–R4, all four confined to the fifth `|`-delimited cell. Columns 1–4 of those rows are character-identical to the removed lines, and no other row, rule, table or paragraph moved. **But see D2** — the coverage-summary counts that the document says are derived from column 5 were *not* updated, and now disagree with it.
- [x] **The `R<n>` ids are #7's, and the behaviour genuinely matches.** R2, R3 and R4 are proven by tests I armed myself; R1's domain half likewise. One structural divergence from #7 is legitimate and correctly handled: #7's shared kernel also carried the §7.1 fact envelope and flipped **R11** here; #8 places the envelope in `Contracts` (feature 8), so R11 correctly stays `TODO`. That is a placement difference, not a coverage claim.
- [ ] n8n workflows / black-box API parity — not reachable at this feature.
- [ ] README benchmark section — not this feature's.

---

## 3. My own arming table

Every probe: mutate source → run the named suite → restore from a pre-edit copy → `cmp` byte-identical → rebuild → confirm green. **All eight restores verified by `cmp`; final `quality.sh` exit 0.**

| # | Mutation | File | Named test expected to die | Result | Verbatim evidence |
|---|---|---|---|---|---|
| P1 | Added `public decimal ReviewerDecimalProbe => (decimal)MinorUnits / 100m;` | `src/SharedKernel/Money.cs:43` | `R1_Money_…AndOffersNoDecimalRepresentation` | **FAILED (correct)** | `Failed … MoneyTests.R1_Money_RepresentsOneThousandTwoHundredFortyTwoPoint50EurosAsOneHundredTwentyFourThousandTwoHundredFiftyMinorUnitsAndOffersNoDecimalRepresentation` — 1 failed / 30 passed |
| P2 | Added `public double ReviewerDoubleProbe => MinorUnits / 100.0;` — a floating-point **major-unit** accessor, exactly what M1 forbids | `src/SharedKernel/Money.cs:43` | *anything* | **PASSED — NOTHING DIED. This is D1.** | `Passed! - Failed: 0, Passed: 31 … SharedKernel.UnitTests` and `Passed! - Failed: 0, Passed: 11 … Architecture.Tests`. 42/42 green with the violation live in `Money`. |
| P3 | Deleted the body of `EnsureSameCurrency` (the cross-currency guard) | `src/SharedKernel/Money.cs:89-93` | the two `R2_…` tests | **FAILED (correct)** | `Failed … R2_Money_RelationalOperatorsRaiseDomainErrorAcrossCurrencies` **and** `Failed … R2_Money_RaisesDomainErrorOnCrossCurrencyAddSubtractAndCompareWithNoImplicitConversion` — 2 failed / 29 passed |
| P4 | Weakened `if (value <= 0)` to `if (value < 0)` (zero quantity constructs) | `src/SharedKernel/Quantity.cs:90` | `R3_Quantity_RefusesZeroNegativeAndFractionalValuesAndCreatesNoValueObject` | **FAILED (correct)** | 1 failed / 30 passed |
| P5 | Removed `value != Math.Floor(value)` from `Quantity.From` (fractional accepted) — the leg P4 does **not** reach | `src/SharedKernel/Quantity.cs:109` | same test | **FAILED (correct)** | 1 failed / 30 passed |
| P6 | Disabled check-digit enforcement (`if (false && actualCheckDigit != expectedCheckDigit)`) | `src/SharedKernel/GLN.cs:26` | `R4_Gln_RefusesWrongLengthNonDigitsAndABadCheckDigit` | **FAILED (correct)** | 1 failed / 30 passed |
| P7 | Added a `System.Text.Json` call in a SharedKernel **sub**-namespace (`OrderToCash.SharedKernel.Errors`) — a forbidden reference that is **not** `decimal`, in the sub-namespace nobody had armed | new `src/SharedKernel/Errors/ReviewerPurityProbe.cs` | `DomainMustNotDependOnSystemTextJson` | **FAILED (correct)**, and only that one of the six purity rules | 1 failed / 10 passed |
| P8 | Removed `typeof(OrderToCash.SharedKernel.Money).Assembly` from `DomainAssemblies.All` | `tests/Architecture.Tests/DomainAssemblies.cs:44` | `DomainAssembliesAllContainsExactlyTheSevenServicesPlusSharedKernel` | **FAILED (correct)** | 1 failed / 10 passed |
| P9 | Removed the `^OrderToCash\.SharedKernel(\.\|$)` alternative from `DomainNamespacePattern`, **keeping** the assembly in the list — the *other* half of the coverage defect | `tests/Architecture.Tests/DomainAssemblies.cs:65` | `DomainNamespaceSelectorYieldsAtLeastOneTypePerServiceAssembly` | **FAILED (correct)** | `…must select at least one type in every assembly in DomainAssemblies.All … Assemblies with zero matching types: OrderToCash.SharedKernel` |

P8 and P9 together are the important result: the SharedKernel-coverage fix is guarded on **both** of the two axes on which it was originally broken. Removing either the assembly or the namespace alternative now fails a named test. It is not special-cased to survive one probe.

---

## 4. GLN check digits — computed independently, not taken on trust

I recomputed the check digit of every "valid" GLN in `tests/SharedKernel.UnitTests/GlnTests.cs:20-24` from its own 12-digit body, in two textually-unrelated formulations of the same algorithm (weights 3,1 from the **right** of the body, as `domain-model.md` §2.4 words it; and weights 1,3 from the **left**, the classic EAN-13 wording), without reading `GLN.cs`'s implementation:

| GLN in the test | 12-digit body | Computed (right-weighted) | Computed (left-weighted) | Stated | Verdict |
|---|---|---|---|---|---|
| `4006381333931` | `400638133393` | 1 | 1 | 1 | **genuinely valid** |
| `4890123456787` | `489012345678` | 7 | 7 | 7 | **genuinely valid** |
| `9520012345605` | `952001234560` | 5 | 5 | 5 | **genuinely valid** |
| `1234567890128` | `123456789012` | 8 | 8 | 8 | **genuinely valid** |
| `0000000000000` | `000000000000` | 0 | 0 | 0 | **genuinely valid** |
| `4006381333930` (negative case) | `400638133393` | 1 | 1 | 0 | **genuinely invalid** — the negative case is a real one |

All five accepted vectors carry correct check digits, so `R4_Gln_AcceptsARealValidGlnWithACorrectCheckDigit` is asserting what it claims, and the rejected vector is genuinely wrong rather than accidentally right. `4006381333931` is additionally the externally-published EAN-13 worked example, so at least one vector has an oracle outside this repository. The implementer's claim here holds under independent computation.

The `0000000000000` vector is worth a word: it is trivially valid under any weighting and would survive a swapped-weights bug. It does no harm sitting alongside four non-trivial vectors, and P6 shows the guard is live, but it is not evidence on its own. #7 solved this more strongly with an exhaustive single-digit-mutation sweep justified by `gcd(3,10)=1` — that pattern is in #7's own "Notes for #8 and #9" and is worth adopting when this is reopened. **Not a defect; a free upgrade while the file is open.**

---

## 5. R1–R4 → test traceability walk

| Req | EARS obligation | Named test | Verified how | Status |
|---|---|---|---|---|
| **R1** | Represent every monetary amount as an integer count of minor units + ISO 4217 alpha-3, in the write model, in every fact payload, in the read model and in every API response | `MoneyTests.cs:15` › `R1_Money_RepresentsOneThousandTwoHundredFortyTwoPoint50EurosAsOneHundredTwentyFourThousandTwoHundredFiftyMinorUnitsAndOffersNoDecimalRepresentation` | Read in full; armed P1 (fires on `decimal`) and P2 (**does not fire on `double`**) | **FAILS REVIEW — D1.** The positive half (124250 + "EUR") is correct and `Money` is genuinely `long`-backed with no `decimal` surface. The *absence* half proves only `decimal`, not the `floating-point` M1 names. Splitting the row's API half out is correct and I endorse it (see D3 on ratification). |
| **R2** | Cross-currency add/subtract/compare raises a domain error, no implicit conversion | `MoneyTests.cs:77` › `R2_Money_RaisesDomainErrorOnCrossCurrencyAddSubtractAndCompareWithNoImplicitConversion`; `MoneyTests.cs:100` › `R2_Money_RelationalOperatorsRaiseDomainErrorAcrossCurrencies`; supported by `MoneyTests.cs:112` › `R2_Money_HasNoImplicitOrExplicitCurrencyConversionOperator` | Armed P3 — both named tests die when the guard is deleted; operands asserted unmutated; `op_Implicit`/`op_Explicit` proven absent by reflection | **PASSES.** Both legs of the EARS sentence are covered: the refusal *and* the "SHALL NOT perform any implicit conversion". |
| **R3** | Quantity from a non-strictly-positive-integer raises a domain error and creates no value object | `QuantityTests.cs:62` › `R3_Quantity_RefusesZeroNegativeAndFractionalValuesAndCreatesNoValueObject` | Armed P4 (zero leg) and P5 (fractional leg) separately — each kills the test on its own | **PASSES.** Three legs (zero, negative, fractional) asserted with stable `Code`s. The `From(double)` overload is a justified addition, not padding: an `int` parameter makes "fractional" unrepresentable, so without it the matrix's fractional case could not be written at all. See D4 for its one rough edge. |
| **R4** | GLN not exactly 13 digits, or wrong GS1 mod-10 check digit, raises a domain error and creates no value object | `GlnTests.cs:25` › `R4_Gln_AcceptsARealValidGlnWithACorrectCheckDigit` (5 vectors); `GlnTests.cs:33` › `R4_Gln_RefusesWrongLengthNonDigitsAndABadCheckDigit` | Armed P6; all five "valid" vectors independently recomputed (§4) | **PASSES.** Both refusal grounds in the EARS sentence (length/charset, check digit) are covered, and the "valid" vectors are real. |

**Feature 7 `acceptance` array:**

| Acceptance item | Evidence | Verdict |
|---|---|---|
| zero `PackageReference` entries, asserted by an architecture test | `SharedKernel.csproj` declares none; `SharedKernelCsprojDeclaresZeroPackageReferences` + `SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework` | **MET** (see §6 on the residual gap) |
| GLN check digit validated with real and invalid GLNs | §4 — five genuinely valid vectors, one genuinely invalid, plus length and charset cases; armed P6 | **MET** |
| `Money` rejects cross-currency arithmetic and holds `long` minor units | `Money.cs:38` `public long MinorUnits`; armed P3 | **MET** |
| pure xUnit unit tests, no framework references | `SharedKernel.UnitTests.csproj` — test SDK + xunit + coverlet + one `ProjectReference`; no DB, broker, HTTP or mocking package | **MET** |

All four acceptance items are met. The rejection is not on the acceptance array — it is on `specs/shared/` R1/M1 and `CLAUDE.md`, which the brief and the state machine make equally binding.

---

## 6. D3's closure — my explicit judgement, as asked

**The `.csproj` grep plus the `GetReferencedAssemblies()` assertion are, together, adequate. D3 is closed.** Stated plainly because the brief asked for an explicit answer either way.

The implementer's reasoning is sound and I reach the same conclusion independently. The two guards partition the space by *route*, not by accident:

- `SharedKernelCsprojDeclaresZeroPackageReferences` covers a package declared in SharedKernel's own project file — the literal wording of `CLAUDE.md`'s rule, and the only route a person editing that file can take.
- `SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework` covers every other route (`GlobalPackageReference`, a `Directory.Build.props` `ItemGroup`, a transitive pin) **provided the package is actually consumed**.

The residual gap is exactly "reaches SharedKernel by a non-`.csproj` route **and** is never used by any type in the assembly". I agree with the implementer that this is not the failure mode the rule protects against: `CLAUDE.md`'s "zero `PackageReference`" exists so that **SharedKernel does not depend on anything at runtime** — it is a statement about the shared kernel's dependency surface, not about lockfile hygiene. A reference no code calls into adds no runtime dependency, no type, no behaviour and no version constraint on a consumer. And the gap is self-closing in the direction that matters: the moment anyone *uses* the thing — the only way it can hurt — the second guard fires. The implementer discovered this by arming rather than assuming, watched the guard stay green against an unused `GlobalPackageReference`, and wrote the negative result down instead of quietly picking a probe that worked. That is the right conduct and it is why I trust the boundary they drew.

Two things I want on the record against the day someone reopens this:

1. **Closing it fully is a different technique, not a bigger version of this one** — parsing `project.assets.json` or asserting on `dotnet list package --include-transitive`. That is a real option if SharedKernel ever grows, but it buys coverage of a case with no runtime consequence, and it couples an architecture test to NuGet's restore-output format. Not worth it today; I would not accept "we might as well" as the reason to add it later either.
2. **The guard's docstring slightly over-claims.** `SharedKernelHasNoPackagesTests.cs:130-138` says a package "shows up here even if no `.csproj` text search would have found it", with no mention of the used/unused condition the implementer proved by experiment. The limitation is fully documented in `progress/impl_shared_kernel.md` but not in the file a future reader will actually be looking at when they trust the test. **Fold one sentence of the report's §"D3 vs. the `GetReferencedAssemblies()` gap" into that XML doc** — this is the cheapest possible defence against the next person reading the docstring as absolute. Non-blocking, but do it in the same pass as D1.

---

## 7. Defects

### D1 — REJECT-level. `R1`'s absence assertion covers `decimal` but not floating-point, and `Money` is unguarded against a `double` major-unit accessor

**Where:** `tests/SharedKernel.UnitTests/MoneyTests.cs:33-74` (`AssertNoDecimalSurfaceOnMoney`, the helper the R1 test delegates its absence claim to) and, for the same reason, `tests/Architecture.Tests/DomainDecimalTests.cs:39-93` (`FindDecimalOffences`). Both compare against `typeof(decimal)` and nothing else.

**Proven, not suspected.** Probe P2: adding

```csharp
public double ReviewerDoubleProbe => MinorUnits / 100.0;
```

to `Money.cs` leaves the **entire suite green — 31/31 unit tests and 11/11 architecture tests**. A floating-point major-unit representation now lives on `Money` and no test in this repository notices.

**Why it matters — three independent authorities say floating-point, and all three are unguarded:**

- `specs/shared/domain-model.md` §2.1, invariant **M1**, which R1 is the EARS statement of: *"A **decimal, floating-point or fixed-point** major-unit representation is never used, at rest, on the wire, or in arithmetic."* Two of the three named forms are unchecked.
- `CLAUDE.md`, coding conventions, Money row: *"**`long` minor units (cents) only. Never a float**, never `decimal` in domain arithmetic."* "Never a float" is stated first and is the unguarded half.
- `src/SharedKernel/Money.cs:8-12` — the type's **own XML doc** claims *"There is deliberately no decimal, floating-point or fixed-point major-unit representation anywhere on this type"*. The test whose stated purpose (`MoneyTests.cs:26-32`) is *"proving the absence rather than narrating it"* proves one third of the sentence directly above it in the source.

**Why it is blocking rather than a nice-to-have.** This is the third instance in three phases of the dominant defect class in this build — the guard that fires for the shape it was written against and misses the shape the requirement names. Feature 6 was rejected for it (`ResideInNamespaceEndingWith` covering `*.Domain` but not `*.Domain.ValueObjects`); feature 5 was caught at the human gate for it (`grep -c jetstream` returning 1 either way); and this feature's own reopen was another instance (the rules never scanning SharedKernel at all). The cost of not fixing it is concrete and near: from feature 8 onward `Money` crosses a JSON boundary and reaches a Gateway that must render "€1,242.50" for a human, and the cheapest wrong way to do that is a `double` accessor on `Money` — which would sail through `quality.sh`. Fixing it now, before a single consumer exists, is a few lines; fixing it after six services import the shortcut is a refactor.

**What must change:**

1. `AssertNoDecimalSurfaceOnMoney` must reject `float` and `double` as well as `decimal`, and should cover **public fields** (it currently walks properties, methods and constructors only) and conversion-operator return types. Rename it to match what it then asserts — `MoneyTests.cs` is cited verbatim in `test-matrix.md`, so if the **test method** name changes, change the matrix cell in the same commit (matrix rule 4).
2. `DomainDecimalTests.FindDecimalOffences` should do the same, so the ban is enforced across every domain type and not only on `Money`. If you prefer to keep that rule literally named for `decimal`, add a second named architecture test for floating-point rather than widening this one silently — a rule whose name says `decimal` and whose body also rejects `double` is its own small trap.
3. **Arm both.** Re-run P2 (`public double … => MinorUnits / 100.0;`) and record in `progress/impl_shared_kernel.md` which named test failed and with what message. Arm `float` too — they are different `Type` instances and a check written for one does not cover the other.

### D2 — Medium. `test-matrix.md`'s coverage summary now contradicts the Status column it is defined as counting

**Where:** `specs/shared/test-matrix.md:72` (`| 1. orders_aggregate | R1 – R10 | 10 | 0 | 0 | 10 |`) and `:81` (`| **Total** | **R1 – R63** | **63** | **0** | **0** | **63** |`).

Rows R2, R3 and R4 are now `DONE` and R1 is half-done, but the summary still reports **0 green / 0 scoped / 63 not yet green**. The document is explicit that this is not decoration: line 66 introduces the table with *"Counted from the Status column **as it actually stands**, one row at a time"*, and the scope note at line 6 says *"The Green/Scoped counts in the coverage summary **are derived from column 5** and are per-assessment in the same way"* — which is precisely why the four-step reset recipe has resetting those counts as its own step (2), separate from resetting the Status cells (step 1). They are part of the realisation record, not part of the shared contract.

On the evidence I verified, the correct figures are `orders_aggregate` → **10 rows / 3 green / 1 scoped / 6 not yet green**, and Total → **63 / 3 / 1 / 59**.

**Why it matters.** The summary is the only place in 63 rows where a reader gets a number, and it is the number a benchmark reader will quote. It currently understates — which is the safe direction, and is why this is Medium and not REJECT-level on its own — but a summary that drifts from its rows is a summary nobody can rely on in either direction, and #9 inherits this file's discipline along with its recipe.

**There is a genuine instruction conflict here and it needs the leader, not the implementer.** The brief for this feature said *"Only column 5 of rows R1–R4 may have changed"*, and the implementer complied exactly. The document says the counts track column 5. Both cannot hold. **Resolve it explicitly**: either update the counts whenever a Status cell flips (my recommendation — it is what the document says, it is one line per feature, and it keeps the file self-consistent at every commit), or record a written convention that counts are refreshed at phase boundaries and say so in the file so the next reader is not misled. Do not leave it implicit.

### D3 — Minor. R1's scoped row states its shortfall but names no ratifier

**Where:** `specs/shared/test-matrix.md:89`, the R1 Status cell.

The cell reads `DOMAIN HALF DONE — … API half (api/money-representation.spec) outstanding — no Gateway/API surface exists yet (feature 7 is shared_kernel only)`. Splitting the row rather than claiming it whole is **exactly right** and is the single most important thing about that cell — it is the `scoped`-abuse the brief warned about, avoided. Matrix rule 3(a) is satisfied: the cell names which leg is unproven, in the requirement's own words.

Rule 3(b) is not: *"The cell names the ratification: who accepted the deferral, and in which pass or record they accepted it. A shortfall disclosed only by whoever wrote the row is **not** ratified — that is the author marking their own homework."*

This does **not** block feature 7 — R1's row belongs to the matrix's `orders_aggregate` group, whose gate is a later backlog feature, not this one. It will block *that* gate, and the ratification is far cheaper to record now, while the reasoning is fresh, than to reconstruct in ten phases.

**As reviewer I ratify the deferral now, on this record**, and the cell should say so. Suggested addition to the cell, no other change: `Scoped deferral ratified by the reviewer in progress/review_shared_kernel.md (feature 7); closed by the gateway feature, which owns api/money-representation.spec.` Note this makes R1 a **ratified scoped** row, which is what makes the D2 count `1 scoped` rather than a second not-yet-green.

### D4 — Minor. `Quantity.From(double)` leaks a framework exception instead of a domain error

**Where:** `src/SharedKernel/Quantity.cs:114` — `return new Quantity(checked((int)value));`.

Observed directly, not inferred — I built a probe against the real `SharedKernel.csproj`:

```
Quantity.From(1e18)  →  System.OverflowException | is DomainError: False
```

`1e18` is integral, positive and finite, so it passes every guard in `From`, then overflows the `checked` cast. The refusal is correct in outcome but arrives as a `System.OverflowException` with no stable `Code`, from inside the domain layer.

**Why it matters.** `requirements.md`'s own vocabulary defines a domain error as *"a refusal raised inside the domain layer **carrying a stable code**"*, and `CLAUDE.md` requires domain errors to extend `DomainError` and carry a `Code`. A caller branching on `Code` — which is the whole reason the type exists, and is how errors will cross the RPC boundary from feature 9 onward — cannot branch on this one. R3's letter arguably does not reach it (`1e18` *is* a positive integer), which is why this is Minor and not part of the rejection, but the `From(double)` overload exists precisely to guard an unvalidated upstream number (a parsed EDI field, an inbound JSON value), and an unvalidated upstream number is exactly where a `1e18` comes from.

**Suggested fix:** an explicit range check before the cast, throwing `QuantityMustBePositiveError`. One `if`.

### D5 — Advisory, parity. #7's sibling business references have no home yet, and the report does not say so

`specs/shared/domain-model.md` §2.3 places the sibling references in the shared kernel section — *"Sibling references follow the same shape: `DES-######` (despatch advice), `INV-######` (invoice), `CR-######` (credit line)"* — and `CLAUDE.md`'s conventions table lists all four together. #7 built all four types in `packages/shared-kernel` (its history entry names `DespatchReference`, `InvoiceReference`, `CreditLineReference` explicitly). #8 built `OrderNumber` alone.

This is **not a defect against feature 7's contract**: the feature title enumerates exactly the seven types delivered, and the acceptance array does not mention the siblings. It is a **#7↔#8 parity divergence with a real consequence** — either Fulfillment and Billing each grow their own copy of the same zero-padded-prefix formatter and `Parse`, or someone comes back here later. The `OrderNumber` implementation is already shaped as a general prefix + `MinimumSequenceDigits` formatter, so generalising it is small.

The gap is that `progress/impl_shared_kernel.md`'s "What I could not do, and why" section lists three items and this is not one of them, so a divergence from #7 leaves no trace. **Add a line to the report recording the decision and where the siblings will live** — that is all this needs. `progress/history.md` should carry it too when this feature closes, because "what the reuse did not save" is the thing this repository exists to measure.

### D6 — Advisory, process. Restoring an armed file with a timestamp-preserving copy can leave the *armed* binary in place

Not a defect in the delivery — a hazard I hit myself during this review, which bears directly on how much any arming table in this repository is worth.

After my last probe I restored `tests/Architecture.Tests/DomainAssemblies.cs` with `cp -a` and confirmed the restore with `cmp` — byte-identical, correct. `quality.sh` then **failed**: `DomainNamespaceSelectorYieldsAtLeastOneTypePerServiceAssembly`, `Assemblies with zero matching types: OrderToCash.SharedKernel`. The source was right; the *assembly* was not. `cp -a` preserves the backup's mtime, MSBuild's incremental check saw the source as older than its output, skipped the rebuild, and the test ran against the **previously armed** compiled constant. `touch` on the restored files plus a rebuild returned `quality.sh` to exit 0, 31/31 + 11/11, which is the state the tree is in now.

In my case it produced a false **red**, which is harmless. The same mechanism can produce a false **green** — restore a file, fail to actually apply it, and let a stale-but-correct binary vouch for source that is still armed. `progress/impl_shared_kernel.md` describes restores as *"restored `DomainAssemblies.cs` from a pre-edit copy and diffed it byte-for-byte against that copy"* — a source-level check with no rebuild-forcing step. **The arming protocol should say: after restoring, force the rebuild (`touch` the file, or `dotnet build --no-incremental`) before the confirming green run.** Worth a line in `docs/PROCESS.md` §11 alongside the `grep -c jetstream` finding — it is the same lesson (a check that cannot fail, or a check run against the wrong artefact, proves nothing) in a new disguise.

---

## 8. On the disclosed `git checkout --` near-miss

**The disclosure is adequate for the record, and better than adequate as conduct.** Judged on its own terms rather than on the outcome:

- It names the **causal mistake**, not just the symptom — running `git checkout -- feature_list.json` without first checking whether the file carried other uncommitted work — and states plainly that this is "exactly the kind of destructive-without-checking-first action CLAUDE.md's git safety protocol warns against, and I did not follow it here". No hedging, no passive voice, no "the file was reverted".
- It names the **antecedent** mistake that created the pressure: rewriting the whole file through `json.dump` with `ensure_ascii` defaulted on, Unicode-escaping every em-dash across unrelated features. Most reports would have stopped at the `checkout`.
- It states **what was actually lost** (feature 6's `done` and feature 7's `in_progress`, both uncommitted), **how it was recovered** (from the `git diff` output printed immediately before the checkout), and **how the recovery was verified** (`git diff` shows exactly two lines, the file parses, `init.sh` reports the expected counts).
- It was **kept as written** when the coordinator confirmed nothing was lost, rather than tidied into a footnote. The addendum says so explicitly.

I verified only the cheap half — `git diff feature_list.json` against HEAD is exactly the two status lines, and the file is valid JSON — because the coordinator states it verified the tree and re-verification is duplicated cost. Nothing further is required. If anything is owed, it is generalisation rather than more disclosure: the lesson ("never `git checkout --` a file without `git diff`-ing it first, and prefer a targeted edit to a whole-file rewrite") belongs in `docs/PROCESS.md` §11 next to D6, where the next agent will read it — a report nobody re-reads is where lessons go to die.

---

## 9. What must change before re-review

**Blocking:**

1. **Fix D1.** `AssertNoDecimalSurfaceOnMoney` must reject `float` and `double` as well as `decimal`, and must cover public fields. Do the same for `DomainDecimalTests.FindDecimalOffences`, or add a separate named floating-point architecture rule. **Arm both** — re-run probe P2 (`public double ReviewerDoubleProbe => MinorUnits / 100.0;` on `Money`) and a `float` variant, and record in `progress/impl_shared_kernel.md` which named test failed and with what message. If a test method is renamed, update its `test-matrix.md` citation in the same change (matrix rule 4).

**Do in the same pass, all small:**

2. **D2** — resolve the coverage-summary conflict explicitly. Either bring `test-matrix.md:72` and `:81` in line with the Status column (`10 / 3 / 1 / 6` and `63 / 3 / 1 / 59` on the evidence verified here) or record the deferral convention in the file. **This is the leader's call, not the implementer's** — the brief and the document currently disagree.
3. **D3** — add the ratification sentence to R1's Status cell, citing this review. Column 5 only.
4. **D4** — range-check before `checked((int)value)` in `Quantity.From` so the refusal is a `QuantityMustBePositiveError` with its stable `Code`.
5. **D5** — one line in `progress/impl_shared_kernel.md` recording where `DES-`/`INV-`/`CR-` will live, since #7 put them here and #8 did not.
6. **D3-guard docstring** (§6, item 2) — one sentence in `SharedKernelHasNoPackagesTests.cs:130-138` noting that `GetReferencedAssemblies()` sees only *consumed* references.

**Free upgrade while the file is open, not required:** #7's exhaustive single-digit-mutation GLN sweep (its own "Notes for #8 and #9"). Five hand-picked vectors, one of which is `0000000000000`, is weaker evidence than 117 mutations, and the pattern was left for you deliberately.

**Explicitly do not re-touch:** `src/SharedKernel/Money.cs`, `Quantity.cs` (beyond D4's `if`), `GLN.cs`, `OrderNumber.cs`, `UniqueId.cs`, `Entity.cs`, `AggregateRoot.cs`, `DomainError.cs`, `Errors/*`; `DomainAssemblies.cs`; `DomainPurityTests.cs`; the R2/R3/R4 tests. All are correct, all are armed, and all are verified by this review. The SharedKernel-coverage fix in particular is **general, single-definition and doubly guarded** (P7, P8, P9) and must not be re-opened.

`feature_list.json` feature 7 set back to `in_progress`. No entry appended to `progress/history.md` — that is the reviewer's act on approval, and this feature has not been approved. No commit, no push.

---

# RE-REVIEW — round 2 (D1–D6 addressed)

**Verdict: APPROVED**

Everything above this line is the round-1 record and stays visible: the rejection was real, D1 was a genuine guard-that-does-not-guard on the subtlest requirement in the feature, and all six defects are now closed. I re-armed the one claim in the implementer's own table that had **no** arming row — the conversion-operator path — and it holds. I found one new advisory (**D7**), non-blocking, recorded below rather than smoothed away.

The single most important thing about this round: the implementer **found and fixed a second bug of the identical shape while fixing the first**, without being asked. That is the harness starting to catch its own defect class one layer earlier again, which is the same trajectory feature 6's review recorded.

## What I verified myself this round, and what I took as established

**Took as established, per the coordinator (not re-proven):** that `double`/`float` accessors on `Money` now fail both suites, and that restore + forced rebuild returns 32/32 + 12/12. The coordinator armed those directly under the D6-corrected protocol, verified the probe compiled cleanly first so a build failure could not masquerade as a fired guard, and disclosed that its own *first* attempt reported a false "still green" because two project paths were passed to `dotnet test`. I have applied exactly the suspicion that finding warrants to the implementer's own green-restore claims (see "on the arming table" below).

**Verified myself:**

| # | Check | Method | Result |
|---|---|---|---|
| Q1 | **The conversion-operator claim — the one row missing from the implementer's arming table.** They narrowed the `IsSpecialName` skip to `get_`/`set_` so operator return types stop being silently excluded, and documented it, but **armed no probe for it**. | Added `public static explicit operator double(Money value) => value.MinorUnits / 100.0;` to `Money.cs`; `touch`; `dotnet build` → **Build succeeded, 0 Warning(s)** (so the failures below are guards firing, not a compile error); full `dotnet test`. | **BOTH FIRE.** `R1_…AndOffersNoDecimalOrFloatingPointRepresentation` **FAILED**, `R2_Money_HasNoImplicitOrExplicitCurrencyConversionOperator` **FAILED** (2/32), and `NoDomainTypeHasAFloatingPointFieldPropertyParameterOrReturnType` **FAILED** (1/12). The architecture rule has no path to an operator other than the narrowed `IsSpecialName` branch, so this is direct proof the second bug is genuinely fixed — **and proof the new floating-point rule really scans SharedKernel**, since `Money` lives there. |
| Q2 | **D4 by my own probe, not their test.** | Standalone binary against the real `SharedKernel.csproj`, three values. | `1E+18` → `QuantityMustBePositiveError \| isDomainError=True \| code=quantity.must_be_strictly_positive_integer`; `-1E+18` → same; `2147483648` (`int.MaxValue + 1`) → same. **`System.OverflowException` is gone.** |
| Q3 | **D2 counts.** | `git diff specs/shared/test-matrix.md`. | `orders_aggregate` → `10 \| 3 \| 1 \| 6`, Total → `63 \| 3 \| 1 \| 59` — **exactly the figures I derived in round 1**. Only those two summary lines plus the four R1–R4 Status cells moved; columns 1–4 character-identical, no other row, rule or paragraph touched. |
| Q4 | **D3 ratification.** | Read R1's Status cell. | My suggested sentence appended **verbatim and nothing else**; the cell still splits the row honestly rather than claiming it whole. R1 is now a *ratified* scoped row, which is what makes the D2 `1 scoped` count legitimate. |
| Q5 | **Matrix rule 4 — the citation tracks the rename.** | `grep -c` of the cited method name in `MoneyTests.cs`. | The R1 cell cites `R1_Money_…AndOffersNoDecimalOrFloatingPointRepresentation` and that string **occurs literally, once**, in the file. Renamed and re-cited in the same change, as required. |
| Q6 | **Scope — "not re-touched" means byte-identical, not "mostly".** | `cmp` against my round-1 pre-edit copies. | `src/SharedKernel/Money.cs` **UNCHANGED**, `GLN.cs` **UNCHANGED**, `tests/Architecture.Tests/DomainAssemblies.cs` **UNCHANGED**. `Quantity.cs` differs by **exactly** the D4 range check plus its comment — the one line the round-1 review permitted inside that file. Nothing else in the shared kernel moved. |
| Q7 | **Tree hygiene.** | `git status --short`; `find src tests -name "*Temp*" -o -name "*Probe*"`. | Zero stray probe files. `git status` shows exactly the round-2 scope plus the coordinator's own `CLAUDE.md` and `docs/PROCESS.md` edits, which are not this feature's. |
| Q8 | **The gate itself, after restoring my probes with a forced rebuild.** | `touch` + `dotnet build` + `./quality.sh` + `./init.sh`. | `quality.sh` **exit 0** — format clean, build 0 warnings / 0 errors, **32/32 + 12/12**, 91.3% line coverage on the SharedKernel-covering run. `init.sh` exit 0, coherent. |
| Q9 | **D7 (new, advisory)** — is the new floating-point exemption as narrow as it claims? | Added `public static Quantity From(float value) => From((double)value);` to `Quantity.cs`; forced rebuild; architecture suite. | **12/12 green with a `float` parameter live on a domain type.** See D7. |

## On D1's fix *shape* — checked against what I specified, item by item

| Round-1 requirement | Delivered? | Evidence |
|---|---|---|
| Helper rejects `float` and `double` as well as `decimal` | **Yes** | `MoneyTests.cs` — `forbiddenTypes = { decimal, float, double }`, one set checked against every member kind. |
| Helper covers **public fields** | **Yes, and better than asked** | It now walks `Public \| NonPublic \| Instance \| Static \| DeclaredOnly` fields, so a *private* `double` field is caught too. The docstring explains why a field check matters even though `Money`'s fields are all auto-property backing today. |
| Helper covers **conversion-operator return types** | **Yes — and this exposed a second, unreported bug** | The original code skipped *every* `IsSpecialName` method to avoid double-counting property accessors, which silently also skipped `op_Implicit`, `op_Explicit` and every other operator — so the original docstring's claim to cover conversion operators was false in **both** files. Narrowed to `get_`/`set_` prefixes only, with a comment recording why. Armed by me as Q1, since their table did not. |
| **Separate named** architecture test for floating-point, not a widened rule named `decimal` | **Yes, exactly as specified** | `NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType` (`{decimal}`) and `NoDomainTypeHasAFloatingPointFieldPropertyParameterOrReturnType` (`{float, double}`), sharing `FindTypeOffences` so the two bans cannot drift on *what* they inspect while staying distinct on *what they forbid*. That is a better resolution than either option I offered. |
| Matrix citation updated in the same edit if renamed | **Yes** | Q5. |
| Both armed, with the failing test named and its message recorded | **Yes** | Rows A1, B, C, D of their table, plus my Q1 for the operator path they missed. |

## On the arming table — did D6 land as behaviour, or only as prose?

The coordinator was right to make this the test that matters after this much rework, and it passes. Every row of the new table names `dotnet build --no-incremental` as an explicit column, and the paragraph beneath it states the sequence in full — `cp` → `cmp` → **`touch` + forced rebuild** → `dotnet test` — and says outright that *"a `cmp` match was never treated as sufficient on its own in this session"*. That is the finding internalised as a procedure, not quoted back at me.

Two things raise my confidence beyond the table's own say-so:

- It reports the **restore** run as green after *every* row, not only the last — which is the discipline, since a stale binary is exactly what makes an unverified intermediate restore worthless.
- It volunteers a **new, unflattering** failure mode discovered while applying the fix: `dotnet build --no-incremental` at *solution* scope hit a parallel-build race (`CS0006: Metadata file '.../ref/OrderToCash.SharedKernel.dll' could not be found` across seven projects), and the report explains why that is a *different* failure from D6's, why no arming row was affected, and that a plain incremental build after a `touch` is sufficient because the mtime bump is the actual mechanism the fix depends on. An agent inventing compliance does not go and find an extra footgun in the procedure it was told to follow. I have not independently reproduced the race — it is transient by nature and irrelevant to any result — but the reasoning is correct and it is a useful note for whoever finalises `docs/PROCESS.md` §11.2.

My own three probes this round used the corrected protocol independently and all behaved as the protocol predicts, which is the strongest available cross-check on the table.

## D1–D6 closure

| Defect | Status | Basis |
|---|---|---|
| **D1** — R1's absence assertion covered `decimal` only; a `double` accessor on `Money` passed 42/42 | **CLOSED** | Coordinator armed `double` and `float` (both suites fire); I armed the operator path (Q1). Fix shape matches specification on every point, including the two I was most likely to be fobbed off on: fields and operator return types. |
| **D2** — coverage summary contradicted the Status column | **CLOSED** | Q3. Coordinator's ruling applied; my figures reproduced exactly; the document is self-consistent again. |
| **D3** — R1 scoped row named no ratifier | **CLOSED** | Q4. Rule 3(b) satisfied. The §6 docstring over-claim is also fixed — the guard now states in its own XML doc that `GetReferencedAssemblies()` sees only *consumed* references, cites where that was proven and where the residual gap was endorsed. |
| **D4** — `Quantity.From` leaked `System.OverflowException` | **CLOSED** | Q2 (my own probe) plus a new regression test asserting both the stable `Code` and `IsAssignableFrom<DomainError>`, armed by reverting the range check. |
| **D5** — #7's sibling references (`DES-`/`INV-`/`CR-`) have no home | **CLOSED as recorded, correctly not built** | The report now carries the divergence, its consequence (duplicated prefix formatters in Fulfillment and Billing, or a later generalisation of `OrderNumber`'s existing `Prefix` + `MinimumSequenceDigits` shape), and the reason not to build ahead of a contract that does not ask for it. Carried into `progress/history.md` below, which is where "what the reuse did not save" has to live. |
| **D6** — restore-without-rebuild can validate the wrong artefact | **CLOSED and promoted** | Now a `CLAUDE.md` arming protocol and a `docs/PROCESS.md` §11.2 entry (the coordinator's edits), and applied throughout this round's table. |

## D7 — NEW, advisory, non-blocking. The floating-point exemption key omits the parameter type

**Where:** `tests/Architecture.Tests/DomainDecimalTests.cs:46-50` — `_reviewedFloatingPointBoundaryParameters` is a `HashSet<(string TypeFullName, string MethodName, string ParameterName)>` holding one tuple, `("OrderToCash.SharedKernel.Quantity", "From", "value")`.

**Proven (Q9).** Adding `public static Quantity From(float value) => From((double)value);` to `Quantity.cs` leaves the architecture suite **12/12 green**. The exemption key identifies a member by type, method *name* and parameter *name* — not by parameter type and not by arity — so it silently covers every present and future overload of `Quantity.From` whose parameter is called `value`, `float` as well as `double`.

**Why it is advisory rather than blocking.** The exemption itself is right, and the way it was reached is right: a blanket floating-point ban would have broken `Quantity.From(double)` on the first run — the implementer hit that directly and says so — and the round-1 review had explicitly forbidden touching that signature. Exempting one named tuple rather than exempting all method parameters is the correct call, it is documented in a 16-line XML comment that explains itself, and it was armed narrow (their probe E: a *different* method's `double` parameter still fails). The residual breadth is one method name on one type, in the least dangerous of the five member kinds checked — an input boundary, not a representation — and the exemption applies only to the floating-point rule, never to `decimal`, and never to fields, properties, return types or constructor parameters.

**But it is worth naming**, because an allowlist inside the fix for a guard-that-did-not-guard is exactly where the next instance of this defect class will live, and because the key is one field short of identifying the member it was reviewed against. **Fix when this code is next touched** (feature 8 or 9, not now): add the parameter type to the tuple, so the exemption names `Quantity.From(double value)` and nothing else. Two lines.

## `CHECKPOINTS.md` — boxes that moved since round 1

- [x] **C3 — No `decimal` in domain arithmetic; `Money` is `long` minor units.** **Now ticked.** In round 1 I refused this box because `CLAUDE.md`'s "Never a float" and `domain-model.md` §2.1 M1's "decimal, **floating-point** or fixed-point" were unenforced and I had proven it. Both halves are now enforced by two separately named architecture rules plus the R1 unit helper, across fields, properties, return types, parameters, constructor parameters **and operators**, over the seven service domains union SharedKernel. Armed by the coordinator (`double`, `float`) and by me (conversion operator, Q1).
- [x] **C4 — `./quality.sh` passes.** Re-verified by me after restoring my own probes with a forced rebuild: exit 0, 32/32 + 12/12.
- [x] **C5 — `progress/history.md` has an entry for the feature just finished, including its effort record.** Appended by this review, below.
- [x] **C5 — `feature_list.json` reflects the true state.** Feature 7 set `done` by this review.
- [x] **C5 — Claude did not commit.** No `git commit`, no `git push`, in either round.
- [ ] **C4 — Integration tests use Testcontainers.** Still not applicable; no integration tests exist at this feature. Left unticked rather than ticked vacuously.
- [ ] **C4 — Coverage thresholds enforced.** Still feature 34's. The number is printed (91.3%, above the 80% domain bar) and the gate is deliberately inert with a `TODO` naming its owning feature.

Every other box walked in round 1 stands as marked there; nothing that was ticked has been invalidated, which Q6's `cmp` evidence establishes directly — the approved code is byte-identical to the code I verified.

## What I did NOT re-run this round, and why

- **The `double`/`float` probes on `Money`.** The coordinator armed them under the corrected protocol and verified the probe compiled first. Re-running them would be pure duplication; I spent the budget on Q1 instead, which nobody had covered.
- **R2, R3, R4 arming (round-1 probes P3–P6) and the SharedKernel-coverage probes (P7–P9).** `cmp` proves `Money.cs`, `GLN.cs` and `DomainAssemblies.cs` are byte-identical to the files I armed in round 1, and `Quantity.cs` differs only by D4's guard clause — which strengthens R3's refusal rather than weakening it, and whose own effect I re-probed directly (Q2). Re-arming unchanged code against unchanged tests would prove nothing new.
- **The GLN check-digit recomputation.** `GlnTests.cs` and `GLN.cs` are untouched since I recomputed all five vectors in round 1 §4.
- **The implementer's historical per-row restore runs.** Unreproducible after the fact by construction. I verified the thing those runs were evidence *for* — the final state — myself, with a forced rebuild, at Q8.
