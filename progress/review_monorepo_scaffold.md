# review_monorepo_scaffold — Feature 6 (phase 5)

## Verdict: REJECTED

The scaffold builds clean, the suite is green, scope discipline is perfect, and the implementer's arming evidence is **honest** — I reproduced five of its eight rows myself and every one failed exactly as reported. The rejection is not for anything the report claims falsely. It is for what the report never tested: **the six domain-purity rules only select types whose namespace *ends with* `.Domain`, so a violation one namespace deeper is invisible.** I placed a live `System.Text.Json.JsonSerializer` call and a live `MongoDB.Bson.ObjectId` field inside `OrderToCash.Orders.Domain.ValueObjects` and **all six purity tests passed**. That is the literal REJECT condition: a rule whose test passes while its violation is present.

This matters now rather than later because features 7 (`shared_kernel`) and 13 (`orders_aggregate`) are the very next code features, and CLAUDE.md's own layer description — "Aggregates, entities, value objects, domain events, state machines, domain errors" — names precisely the sub-namespaces that will be created. The single most important non-negotiable in this repository would stop being enforced on the first real domain type, while continuing to report green. Note the parity angle: #7 enforced the same rule with an ESLint `no-restricted-imports` scoped to the **path glob** `apps/*/src/domain/**`, which covers subdirectories for free. The .NET translation lost that property silently.

---

## My own arming results (independent — I did not reuse the implementer's runs)

Baseline before arming: `dotnet build OrderToCash.sln` → 0 warnings, 0 errors; `dotnet test` → Passed 8/8.

| # | Rule | Violation I introduced | Test run | Result | Message seen |
|---|---|---|---|---|---|
| 1 | `DomainMustNotDependOnMongoDb` | `MongoDB.Bson.ObjectId Id` on `OrdersDomainPlaceholder` (flat `…Orders.Domain`) + `MongoDB.Driver` PackageReference | `--filter FullyQualifiedName~DomainMustNotDependOnMongoDb` | **FAILED (correct)** | `Domain types must not depend on any MongoDB.* type. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder` |
| 2 | `DomainMustNotDependOnSystemTextJson` | `public string Serialize(object o) => System.Text.Json.JsonSerializer.Serialize(o);` (flat) | `--filter …SystemTextJson` | **FAILED (correct)** | `Domain types must not depend on System.Text.Json. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder` |
| 3 | `DomainMustNotDependOnAspNetCore` | `Microsoft.AspNetCore.Http.HttpContext? Ctx` (flat) + `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | `--filter …AspNetCore` | **FAILED (correct)** | `Domain types must not depend on Microsoft.AspNetCore.*. Offending types: OrderToCash.Orders.Domain.OrdersDomainPlaceholder` |
| 4 | `NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType` | `public decimal TotalAmount { get; set; }` (flat) | `--filter …NoDomainTypeHasADecimal` | **FAILED (correct)** | `decimal must not appear in domain arithmetic. Offences: …OrdersDomainPlaceholder.<TotalAmount>k__BackingField (field); …OrdersDomainPlaceholder.TotalAmount (property)` |
| 5 | `SharedKernelCsprojDeclaresZeroPackageReferences` | `<PackageReference Include="MailKit" />` added to `src/SharedKernel/SharedKernel.csproj` | `--filter …SharedKernelHasNoPackages` | **FAILED (correct)** | `src/SharedKernel/SharedKernel.csproj must declare zero PackageReference entries, found: <PackageReference` |
| **6** | **`DomainMustNotDependOnMongoDb` — NESTED** | same `MongoDB.Bson.ObjectId` field, but in namespace `OrderToCash.Orders.Domain.ValueObjects` | `--filter …DomainMustNotDependOnMongoDb` | **PASSED — DEFECT** | *(no failure; rule did not fire)* |
| **7** | **all six `DomainPurityTests` — NESTED** | `System.Text.Json.JsonSerializer.Serialize` in `OrderToCash.Orders.Domain.ValueObjects` | `--filter FullyQualifiedName~DomainPurityTests` | **PASSED 6/6 — DEFECT** | *(no failure; rules did not fire)* |
| 8 | `NoDomainTypeHasADecimal…` — NESTED | `public decimal Amount` in `OrderToCash.Orders.Domain.ValueObjects` | `--filter …NoDomainTypeHasADecimal` | **FAILED (correct)** | `…Offences: OrderToCash.Orders.Domain.ValueObjects.ReviewerNested.Amount (property)` — the decimal rule's regex **does** cover sub-namespaces, which is what proves the purity rules' selector is the outlier |

All violations were reverted; `diff` against pre-arming backups of `src/Orders/Domain/README_PLACEHOLDER.cs`, `src/Orders/Orders.csproj` and `src/SharedKernel/SharedKernel.csproj` shows **no residue**, the temporary `ValueObjects/` directory and my probe file are deleted, and the restored tree rebuilds at 0 warnings / 0 errors with 8/8 passing.

### Vacuity probe (brief item 2)

I added a temporary probe test that dumped the selector's actual output. `Types.InAssemblies(DomainAssemblies.All).That().ResideInNamespaceEndingWith(".Domain")` selects **7 types out of 28** — exactly one `…Domain.<Service>DomainPlaceholder` per service, all seven services present. The rules are **not** vacuous today, and namespaces are correct: `OrderToCash.<Project>.<Layer>` throughout, `RootNamespace`/`AssemblyName` = `OrderToCash.<Project>` in every `.csproj`. The probe file was deleted after use.

---

## CHECKPOINTS.md boxes walked

C4's coverage gate and C6 do not apply yet (no domain code, no `sdd: true` feature started); C7 does not apply to this feature. Both are recorded here as N/A rather than ticked.

### C1 — The harness is complete

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [x] Every agent definition declares its model.
- [x] `./init.sh` exits 0 — I ran it; exit 0, with only the expected "8 uncommitted change(s) — expected mid-session" warning.

### C2 — State is coherent

- [x] At most one feature `in_progress` — currently zero `in_progress`, one `in_review` (this feature).
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [x] `progress/current.md` describes the active session.
- [x] Every `blocked` feature records why — none are blocked.

### C3 — Architecture is respected

- [ ] **No EF Core / Kafka / NATS / MongoDB / ASP.NET Core reference inside any `Domain/` folder — verified by running the NetArchTest suite.** The suite runs and passes, but **it does not verify this claim for sub-namespaces of `Domain`** (defect D1). The box cannot be ticked on a guard I have demonstrated does not guard.
- [x] No cross-service database access — no data access code exists yet; no project references another service's project (`ProjectReference` sets are SharedKernel + Contracts only).
- [x] No shared runtime code beyond `src/SharedKernel` and `src/Contracts`.
- [x] `src/SharedKernel` has zero `PackageReference` entries — verified by reading the real file *and* by arming row 5.
- [ ] **No `decimal` in domain arithmetic.** The `DomainDecimalTests` rule itself is sound and covers sub-namespaces (arming rows 4 and 8 both fired). But it inherits `DomainAssemblies.All`, whose sufficiency is unguarded (defect D2), so the box is left open pending D2.
- [x] Every inter-service interaction classifiable as Kafka-fact or NATS-RPC — no interactions exist yet.
- [x] No stray debug logging, no context-free TODOs — the single TODO in `quality.sh:80` explicitly names feature 34 / phase 21.

### C4 — Verification is real (partial; coverage gate N/A this phase)

- [x] `./quality.sh` passes — I did not re-run it in full (see "What I did not re-run"); I read it line by line and independently confirmed its three gating steps pass.
- [x] Domain tests are pure — no domain tests exist yet; the placeholder types reference nothing.
- [ ] N/A — no integration tests exist yet (feature 9 onwards).
- [ ] N/A — coverage thresholds are feature 34's scope, correctly deferred.
- [x] No Jest anywhere — `grep` finds no Jest reference; xUnit 2.9.2 is the only runner.

### C5 — The session closed cleanly

- [x] No suspicious untracked files — `git status --untracked-files=all` lists only the 51 intended new files; all `bin/` and `obj/` output is `.gitignore`d (verified with `git check-ignore -v`).
- [ ] `progress/history.md` has an entry for the feature — **not applicable on a rejection**; no entry appended, and none should be until re-review passes.
- [x] `feature_list.json` reflects the true state — set back to `in_progress` by this review.
- [ ] The human has been told what was done and how to test it manually — the leader owns this after the rejection is addressed.
- [x] Claude did not commit — working tree still holds all changes uncommitted.

---

## Acceptance criteria (feature 6 has no triple-doc; this is the contract)

| Acceptance criterion | Verified? | Evidence |
|---|---|---|
| `dotnet build` works from the solution root | **Yes** | `dotnet build OrderToCash.sln --nologo` → `Build succeeded. 0 Warning(s). 0 Error(s).` |
| `global.json` honoured; `TreatWarningsAsErrors` on | **Yes** | `dotnet --version` → `10.0.111`, matching the pin. I proved enforcement rather than reading the property: I introduced `int x = 5;` unused-local and the build **failed** with `error CS0219` (not warning). I also confirmed `/warnaserror+` and `/analyzerconfig:…/.editorconfig` on the real `csc` command line. |
| NetArchTest fails on a deliberate Domain-layer violation of **each** forbidden reference | **No — D1** | True for types directly in `*.Domain`; **false** for any sub-namespace. Arming rows 6 and 7. |
| NetArchTest fails on a deliberate `decimal` in domain arithmetic | **Yes** | Arming rows 4 and 8 — fires at both flat and nested namespaces. |
| `./quality.sh` runs format check + build + test + coverage | **Yes** | Executable, four labelled sections, correct exit-code propagation on each gating step. |

---

## Defects

### D1 — REJECT-level. Domain-purity rules do not cover sub-namespaces of `Domain`

**File:** `tests/Architecture.Tests/DomainPurityTests.cs`, lines **18, 31, 44, 57, 70, 83** — every one of the six rules uses:

```csharp
.That().ResideInNamespaceEndingWith(".Domain")
```

**Why it matters.** `ResideInNamespaceEndingWith` is a literal suffix match. `OrderToCash.Orders.Domain` matches; `OrderToCash.Orders.Domain.ValueObjects`, `…Domain.Events`, `…Domain.Errors` do not. I proved the consequence twice (arming rows 6 and 7): a `MongoDB.Bson.ObjectId` and a `System.Text.Json.JsonSerializer` call sitting live in `OrderToCash.Orders.Domain.ValueObjects` left all six purity tests **green**. Today the repository has only flat placeholder namespaces, so the gap is invisible — and it becomes load-bearing at the exact moment feature 7 or 13 creates the first `Domain/ValueObjects/Money.cs`, which CLAUDE.md's layer description guarantees will happen. A green suite would then be actively certifying a non-negotiable it no longer checks. This is the guard-that-does-not-guard pattern named in CLAUDE.md's commit-discipline section and the reason CHECKPOINTS.md C3 insists the rule be "verified by running the NetArchTest suite, not by eye".

The corroborating evidence that this is a bug and not a deliberate narrowing: `DomainDecimalTests.cs:95` uses `[GeneratedRegex(@"(^|\.)Domain(\.|$)")]`, which **does** match sub-namespaces. The two rule families in the same project disagree about what "the domain layer" means. One of them is wrong, and it is not the regex.

**Required fix.** Replace the suffix selector with one that matches `Domain` as a namespace *segment* — e.g. `ResideInNamespaceMatching(@"\.Domain(\.|$)")` — for all six rules. Do **not** use `ResideInNamespaceContaining(".Domain")`, which would also match a hypothetical `.DomainServices`. Then add a **regression test that would fail if the selector regressed to a suffix match**: a fixture type in a `*.Domain.<sub>` namespace inside the test project's own assembly is not enough (it is not in `DomainAssemblies.All`), so the cleanest form is a test asserting the selector's type set includes a known sub-namespace type. Re-arm and record the nested case in `progress/impl_monorepo_scaffold.md`.

### D2 — Medium. Nothing prevents the rules from becoming vacuous

**Files:** `tests/Architecture.Tests/DomainAssemblies.cs` (whole file), consumed by `DomainPurityTests.cs:17,30,43,56,69,82` and `DomainDecimalTests.cs:21`.

**Why it matters.** I confirmed by probe that the selector matches 7 real types today, so the rules are not vacuous *now*. But no test asserts that. `DomainAssemblies.All` is a hand-maintained list keyed off placeholder types that features 7–13 are explicitly going to delete and replace; the selector's yield is a hand-maintained naming convention. If a future edit drops a service from the list, or renames a namespace, every rule keeps passing over a smaller — potentially empty — type set and reports success. A rule that would pass on an empty repository is worthless, and that is exactly the state a silent regression produces here.

**Required fix.** Add a named test asserting (a) `DomainAssemblies.All.Length == 7` with the seven expected assembly names, and (b) the `ResideInNamespace…` selector yields at least one type **per service assembly**. Arm it by removing a service from the list and confirm it fails.

### D3 — Minor / advisory. `SharedKernel` zero-dependency test checks only one channel

**File:** `tests/Architecture.Tests/SharedKernelHasNoPackagesTests.cs:17-25`.

The test reads the **real** `src/SharedKernel/SharedKernel.csproj` off disk via `RepositoryPaths.Find` (which walks up to `OrderToCash.sln`) — it is genuinely file-backed, not a hardcoded expectation, and I confirmed that by arming it. It is a good test. The advisory: it greps only that one file for `<PackageReference`, so a package reaching SharedKernel via a `GlobalPackageReference` in `Directory.Packages.props`, or via an `ItemGroup` in `Directory.Build.props`, would not be caught. Given `CentralPackageTransitivePinningEnabled=true` is on, this channel is live. Not a rejection ground for this feature — the acceptance list does not ask for it — but worth closing when feature 7 gives SharedKernel real content, ideally by also asserting the compiled assembly's `GetReferencedAssemblies()` contains nothing outside the shared framework.

---

## Observations that are NOT defects of this feature

**`CS1998` is inert on this SDK, and it is not this feature's fault.** CLAUDE.md states "CS1998, CS4014, CA2016 and CA2213 are **errors**, not suggestions — see `.editorconfig`", and `.editorconfig:107` does set `dotnet_diagnostic.CS1998.severity = error`. But an `async Task M()` with no `await` compiles at **0 warnings, 0 errors** in this repository. I chased it down: it reproduces in a bare scratch project outside the repository, with no `.editorconfig` and no `Directory.Build.props` in scope, on SDK `10.0.111` — while `CS0219` and `CS4014` both fire normally in the same probe. So the compiler on this SDK is not emitting CS1998 at all; the repository's wiring is correct and `TreatWarningsAsErrors` is provably effective. Flagging it because CLAUDE.md's async non-negotiable currently rests on one diagnostic that will never fire, which is worth knowing before the outbox relay and NATS responders are written. `.editorconfig` is out of scope for feature 6 and the implementer was right not to touch it — this belongs to whoever owns the analyzer configuration next.

**Scope discipline: clean.** `git status --untracked-files=all` shows changes confined to exactly the allowed paths — `OrderToCash.sln`, `Directory.Build.props`, `Directory.Packages.props`, `quality.sh`, `src/**`, `tests/**`, `progress/impl_monorepo_scaffold.md`, and `feature_list.json` (status flip only, a one-line diff). **Nothing** under `specs/**`, `infra/**`, `.editorconfig`, `global.json` or the harness files was touched. `specs/shared/` is untouched, so the read-only spec rule holds.

**`Directory.Build.props` vs `.editorconfig` split: correct, no duplication.** Enforcement (`TreatWarningsAsErrors`, `AnalysisLevel`, `EnforceCodeStyleInBuild`) lives in the props file; severities live in `.editorconfig`. I checked for overlap and found none — no `dotnet_diagnostic.*` in the props file, no `TreatWarningsAsErrors` in `.editorconfig`. The comment at `Directory.Build.props:3-9` states the rule for the next reader.

**`quality.sh` does not fake a coverage gate — this is exactly right.** The header comment (lines 4-9) says in terms that coverage is "COLLECTED and PRINTED here", that the gate is feature 34, and "do NOT fake a gate that does not gate: this script reports a number, it does not enforce one." The TODO at line 80 names feature 34, `sonarqube_quality_gates`, phase 21. Section 4 prints a percentage with an `[INFO]` prefix and never influences the exit code, and warns rather than passes silently if no Cobertura report is produced. This is the correct state for phase 5 and directly answers #7's finding that its own gate had been inert for twenty phases.

---

## What I did NOT re-run, and why

- **The full `dotnet test OrderToCash.sln` suite as a *verification* step, and `./quality.sh` end to end.** The implementer had just run both; re-running them wholesale duplicates cost without testing an independent claim. I did run `dotnet build OrderToCash.sln` and `dotnet test` once each as a **baseline and restore check** around my arming cycles, which is where their value lay — confirming my mutations were the only variable and that I left no residue. For `quality.sh` I read it line by line and separately confirmed each of its three gating commands (`dotnet format --verify-no-changes`, `dotnet build`, `dotnet test`) behaves as the script assumes.
- **`dotnet format --verify-no-changes` as a standalone run** — subsumed by the above and not a claim under dispute.
- **The remaining three purity rules at the flat namespace** (`EntityFrameworkCore`, `Confluent.Kafka`, `NATS`). I armed five of eight rows myself, deliberately including the two the brief flagged as most likely to be fake (`decimal`, `SharedKernel`), and both were genuine. The three I did not re-arm are structurally identical single-line `HaveDependencyOn` calls differing only in their string literal, and **D1 applies to all six equally** — the defect is in the shared selector, not in any one rule's argument. Re-arming them would confirm the flat-namespace behaviour I have already confirmed three times over.
- **C6 and C7 boxes** — no `sdd: true` feature has started, and C7 is a whole-assessment checkpoint, not a per-feature one.

---

## What must change before re-review

1. **Fix D1.** All six rules in `DomainPurityTests.cs` must select `Domain` as a namespace segment, not as a suffix. Prove it by arming a violation in a `*.Domain.<sub>` namespace and showing the named test fails — that nested arming row must appear in `progress/impl_monorepo_scaffold.md` alongside the existing flat ones.
2. **Fix D2.** Add a named non-vacuity test over `DomainAssemblies.All` and the domain selector, and arm it.
3. **Update `progress/impl_monorepo_scaffold.md`** so its arming table distinguishes flat from nested coverage. The current table is accurate but incomplete in a way that reads as stronger than it is; the next reader should not have to rediscover the distinction.
4. Leave everything else alone. `quality.sh`, the props/editorconfig split, the solution layout, the namespaces and the SharedKernel test are all correct and should not be re-touched. D3 is advisory and belongs to feature 7, not to this re-review.

---
---

# RE-REVIEW — round 2 (D1 and D2 addressed)

## Verdict: APPROVED

Everything above this line is the original round-1 record and stays visible: the rejection was real, D1 was a genuine REJECT-level guard-that-does-not-guard, and the fix is now independently verified. I re-armed my own breaking case, armed three rules **nobody** had armed nested, armed both halves of D2, and checked the shared-constant claim by reading the code rather than accepting the report. All of it holds.

The strongest single piece of evidence is round-2 arming row 7 below: when I deliberately broke the shared selector so it matched nothing, **the six purity tests and the decimal test all still passed** — 9 passed, 1 failed — and the only thing that caught it was D2's new non-vacuity test. That is the defect I raised in D2, reproduced live, with the new guard catching it. D2 was worth insisting on.

---

## Round-2 arming results (mine, independent of the implementer's table)

Baseline before arming: `dotnet build OrderToCash.sln` → 0 warnings / 0 errors; `dotnet test OrderToCash.sln` → `Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10`.

| # | What I armed | Where | Test that failed | Result | Message seen |
|---|---|---|---|---|---|
| 1 | `MongoDB.Bson.ObjectId Id` — **my exact round-1 breaking case** | nested `OrderToCash.Orders.Domain.ValueObjects.ReviewerNested` | `DomainMustNotDependOnMongoDb` | **FAILED — fixed** (was PASSED in round 1) | `Domain types must not depend on any MongoDB.* type. Offending types: OrderToCash.Orders.Domain.ValueObjects.ReviewerNested` |
| 2 | `System.Text.Json.JsonSerializer.Serialize` — **my exact round-1 breaking case** | same nested type | `DomainMustNotDependOnSystemTextJson` | **FAILED — fixed** (was PASSED in round 1) | `Domain types must not depend on System.Text.Json. Offending types: OrderToCash.Orders.Domain.ValueObjects.ReviewerNested` |
| 3 | `public decimal Amount` | same nested type | `NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType` | **FAILED (correct)** | `decimal must not appear in domain arithmetic. Offences: …ValueObjects.ReviewerNested.<Amount>k__BackingField (field); …ValueObjects.ReviewerNested.Amount (property)` |
| 4 | `Confluent.Kafka.IProducer<string,string>` — **never armed nested by anyone** | nested `OrderToCash.Orders.Domain.Events.ReviewerDeepNested` | `DomainMustNotDependOnConfluentKafka` | **FAILED (correct)** | `Domain types must not depend on Confluent.Kafka. Offending types: OrderToCash.Orders.Domain.Events.ReviewerDeepNested` |
| 5 | `NATS.Client.Core.NatsOpts` — **never armed nested by anyone** | same nested type | `DomainMustNotDependOnNats` | **FAILED (correct)** | `Domain types must not depend on any NATS.* type. Offending types: OrderToCash.Orders.Domain.Events.ReviewerDeepNested` |
| 6 | `Microsoft.AspNetCore.Http.HttpContext` — **never armed nested by anyone** | same nested type | `DomainMustNotDependOnAspNetCore` | **FAILED (correct)** | `Domain types must not depend on Microsoft.AspNetCore.*. Offending types: OrderToCash.Orders.Domain.Events.ReviewerDeepNested` |
| 7 | **D2(b)** — changed `DomainNamespacePattern` to `(^\|\.)DomainXX(\.\|$)` so the selector matches nothing | `DomainAssemblies.cs:35` | `DomainNamespaceSelectorYieldsAtLeastOneTypePerServiceAssembly` | **FAILED (correct)** — and note **9 passed / 1 failed**: all six purity rules and the decimal rule passed vacuously; only the new D2 test caught it | `…must select at least one type in every assembly in DomainAssemblies.All … Assemblies with zero matching types: OrderToCash.Gateway, OrderToCash.Orders, OrderToCash.Fulfillment, OrderToCash.Billing, OrderToCash.Notifications, OrderToCash.Projector, OrderToCash.Seed` |
| 8 | **D2(a)** — dropped **Billing** from `DomainAssemblies.All` (the implementer armed Seed; I deliberately chose a different one) | `DomainAssemblies.cs:17` | `DomainAssembliesAllContainsExactlyTheSevenExpectedServiceAssemblies` | **FAILED (correct)** | `DomainAssemblies.All must contain exactly {OrderToCash.Billing, …, OrderToCash.Seed}, found {OrderToCash.Fulfillment, OrderToCash.Gateway, OrderToCash.Notifications, OrderToCash.Orders, OrderToCash.Projector, OrderToCash.Seed}.` |
| 9 | **No-false-positive check** — `MongoDB.Bson.ObjectId` in `OrderToCash.Orders.DomainServices` (a namespace that merely *starts* with "Domain") | `src/Orders/DomainServices/NotDomain.cs` | — | **PASSED 10/10 (correct)** | The pattern is a segment match, not a substring match, exactly as its doc comment claims. A naive `ResideInNamespaceContaining(".Domain")` would have failed here. |

**Coverage achieved:** all six purity rules are now proven to fire at a nested namespace — rows 1, 2, 4, 5, 6 by me, plus EF Core by the implementer. That is 6/6, and it is the direct answer to brief item 2: the fix cannot be a per-rule patch, because three rules I picked myself and the implementer never touched behave identically.

**Restore verified.** Every armed file was reverted and `diff`'d byte-for-byte against pre-arming backups (`src/Orders/Orders.csproj`, `tests/Architecture.Tests/DomainAssemblies.cs`); temporary directories `src/Orders/Domain/ValueObjects/`, `src/Orders/Domain/Events/` and `src/Orders/DomainServices/` are deleted. `src/**` holds exactly **30** `.cs` files and the same 36 directories as the original submission — no residue.

---

## D1 — the fix is genuinely in one shared selector (brief item 4)

I checked this by grep rather than by reading the report. There is **exactly one** definition:

`tests/Architecture.Tests/DomainAssemblies.cs:35`

```csharp
public const string DomainNamespacePattern = @"(^|\.)Domain(\.|$)";
```

and **eight** consumers, all referencing that one symbol — `DomainPurityTests.cs` lines 18, 31, 44, 57, 70, 83 (`ResideInNamespaceMatching`), `DomainDecimalTests.cs:95` (`[GeneratedRegex(DomainAssemblies.DomainNamespacePattern)]`), and `DomainAssembliesTests.cs:55`. A repository-wide grep for the old `ResideInNamespaceEndingWith` and for any second copy of the regex literal returns **nothing**. The two rule families cannot drift apart again, because there is no second constant to drift — which was the precise risk the brief flagged. Row 7 above confirms the coupling is real and not cosmetic: changing that single constant demonstrably changed the behaviour of the NetArchTest rules *and* the reflection-based decimal rule together.

The doc comment at `DomainAssemblies.cs:23-34` also records *why* the pattern is neither a suffix nor a substring match, naming the failure mode. That is the right artefact to leave behind — the next person to touch it is told what the trap was.

## D2 — closed

`tests/Architecture.Tests/DomainAssembliesTests.cs` adds the two tests I asked for, and both are armed above (rows 7 and 8) rather than merely present. The second test uses `Types.InAssembly(assembly)` **per assembly** rather than over the whole set, so it detects one service going empty and not just the whole selection collapsing — a stronger form than I specified. Naming the empty assemblies in the failure message means a future failure is diagnosable without a debugger.

## D3 — correctly deferred, not silently dropped

The implementer left `SharedKernelHasNoPackagesTests` as-is and recorded the `GlobalPackageReference` / `Directory.Build.props` blind spot as a follow-up for feature 7, per my round-1 advice. That is the right call and the right way to record it. **Carry-forward for feature 7:** when `SharedKernel` gets real content, extend the check to the compiled assembly's `GetReferencedAssemblies()`.

---

## CHECKPOINTS.md — boxes re-walked (only those that changed)

### C3 — Architecture is respected

- [x] **No EF Core / Kafka / NATS / MongoDB / ASP.NET Core reference inside any `Domain/` folder — verified by running the NetArchTest suite, not by eye.** Now ticked: all six rules proven to fire at both flat and nested namespaces, 6/6 armed across the two rounds, and proven not to fire on `.DomainServices`.
- [x] **No `decimal` in domain arithmetic.** Now ticked: the rule fires at both nesting levels (round-1 rows 4 and 8, round-2 row 3), and D2's non-vacuity guard now protects the assembly set it depends on.

### C4 — Verification is real

- [x] `./quality.sh` passes. I did not re-run it in this round (unchanged file, and its three gating steps were each exercised directly); I ran `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` myself — 0/0, 10/10, format exit 0.

### C5 — The session closed cleanly

- [x] No suspicious untracked files — scope is exactly `feature_list.json` (modified), `Directory.Build.props`, `Directory.Packages.props`, `OrderToCash.sln`, `quality.sh`, `src/`, `tests/`, and the two `progress/` reports. Nothing under `specs/**`, `infra/**`, `.editorconfig` or `global.json`. The only new file versus the rejected submission is `tests/Architecture.Tests/DomainAssembliesTests.cs`.
- [x] `progress/history.md` has an entry for the feature, including its effort record — appended on this approval.
- [x] `feature_list.json` reflects the true state — set to `done`.
- [x] Claude did not commit.

All other C1/C2/C3/C4/C5 boxes from round 1 are unchanged and remain as marked above.

---

## What I did NOT re-run in this round, and why

- **`./quality.sh` end to end.** The file is byte-identical to the version I reviewed line by line in round 1, and I independently ran each of its three gating commands. Re-running it would re-execute the same three commands plus a coverage print that gates nothing.
- **`SharedKernelCsprojDeclaresZeroPackageReferences` arming.** Armed and verified genuine in round 1; the file was not touched in round 2, which I confirmed by reading it (the only occurrence of the string "PackageReference" is in a prose comment — a `grep -c "<PackageReference"` returns 0).
- **`TreatWarningsAsErrors` / `global.json` / props-vs-editorconfig split.** Verified in round 1 by making the build fail on `CS0219` and by inspecting the real `csc` command line for `/warnaserror+`. None of those files changed.
- **The `CS1998`-is-inert observation** stands unchanged from round 1 and is not a defect of this feature; it belongs to whoever next owns analyzer configuration.
