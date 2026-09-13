# review: architecture_rule_cannot_see_references_inside_async_lambdas (backlog id 83) — round 1

## Status header

- **Verdict: REJECTED.** One blocking defect (**D1**, record-only — no code change, no re-arming, no suite re-run needed), five advisories. The instrument itself is sound and I could not defeat it in five attempts; the defect is in the population claim the entry's bullets 1 and 5 are built on.
- **Transition I would make (I made no edit — the leader owns `feature_list.json`):** id 83 stays `in_progress`.
- **The central claim I was asked to attack — the depth hypothesis — HOLDS, and I strengthened its evidence.** The record's own probe table cannot carry the claim it makes (every missed case in it still contains the word `async`); two entirely synchronous shapes I measured do carry it. Verbatim table in §2, offered for paste into the record.
- **What I did not redo:** the leader's `./quality.sh` reading (1908/1908, 18 projects) and `./init.sh`. My findings are not about the full suite. I ran `Architecture.Tests` three times around my own mutations, built one throwaway Cecil/NetArchTest probe assembly of eleven isolated shapes, and instrumented the new guard to measure its own symbol resolution.

## CHECKPOINTS.md — boxes walked

**C1 — harness.** [x] files present; [x] `progress/current.md` and `history.md` present; [x] agent definitions unchanged by this feature; [x] `./init.sh` exits 0 — **not re-run by me**, taken from the leader's pre-dispatch verification and unaffected by anything in this round (no `feature_list.json` or harness file changed).

**C2 — state.** [x] exactly one `in_progress` (id 83), read from `feature_list.json` myself; [x] all statuses valid; [x] `current.md` describes this session (`:3` names id 83 `in_progress`, started 2026-09-12); [x] no `blocked` feature; [ ] **"every `done` feature has passing tests"** — unaffected, id 83 is not `done` and must not become `done` this round.

**C3 — architecture.** [x] NetArchTest suite run by me (`Architecture.Tests` 36/36, three separate runs) — domain purity, `SharedKernel` zero packages, Cqrs/domain separation all green; [x] no cross-service DB access introduced (this feature touches one test file); [x] **no shared runtime code added** — no new project, and `Microsoft.CodeAnalysis.CSharp` is a **test-project** package, confirmed as id 68's (`git show HEAD:Directory.Packages.props` has no CodeAnalysis line, and `CompositionRootDelegationWiringTests.cs:589` already parses C# with it); [x] no `decimal` in domain arithmetic touched; [x] no stray debug logging — I verified nothing of mine or the implementer's survives (`grep` for `ReviewProbe|armingProbeId83|PhantomApplicationFile` across `src` and `tests`: no hits).

**C4 — verification.** [x] `./quality.sh` green at 1908/1908 — **leader's reading, not re-run by me** (explicitly out of bounds for this round); [x] domain tests pure (NetArchTest pass above); [x] integration tests unaffected; [ ] **coverage thresholds** — not verified by me this round, no coverage-relevant production code changed; [x] no Jest.

**C5 — session close.** [x] no suspicious untracked files added by this feature (`git status --short | wc -l` is **180** before and after my probes, identical); [ ] **`progress/history.md` entry with effort record — NOT WRITTEN, and correctly so while the verdict is REJECTED.** See "Effort record" below for the anchors the eventual entry needs; [x] `feature_list.json` reflects true state (`in_progress`, untouched by the implementer and by me); [x] Claude did not commit.

**C6 — SDD.** **Not applicable**: id 83 is `"sdd": false`, so no `specs/<name>/` is owed. `specs/shared/` is untouched by this feature — the two dirty spec files (`specs/shared/test-matrix.md`, `specs/order_saga_orchestrator/design.md`) predate it and belong to ids 72 and 16.

**C7 — reuse fidelity.** [x] `specs/shared/` untouched by this feature; [x] no amendment raised or needed — nothing in the shared spec causes or constrains this guard, so there is **no `SA-n` routing item this round**; [ ] effort record complete — pending, see C5.

## Acceptance bullets — one row per bullet, with the evidence I produced myself

| Bullet | What it demands | Evidence I verified myself | Verdict |
|---|---|---|---|
| B1 | the covered population enumerated FIRST as a search result, `bin`/`obj` excluded by path, **"`unitOfWork.ExecuteAsync(async ct => ...)` and any sibling shape"**, one classification line per hit | The record's §1 command is `grep -c "async ct =>"`. Re-run with a predicate matching the **claim** rather than the dominant literal, it returns **12 sites in 11 files**, not 11 in 10 — the extra hit is `src/Projector/Application/ProjectionApplyService.cs:27`, `async (document, ct) =>` passed to `writer.ApplyAsync(...)`. It is a genuine member of the population, not an edge case: the lambda captures the method parameter `envelope` (used at `:53`), so it is a display class at depth one with its state machine at depth two — the exact Cecil-blind shape. **Proved live**: I placed a real `OrderToCash.Projector.Infrastructure.SystemClock` construction in it and the guard named `OrderToCash.Projector.Application.ProjectionApplyService` | **NOT MET — D1** |
| B2 | the boundary re-established by probe BEFORE any fix, recorded as observed shapes and never as a theory | Independently re-derived with my own Cecil walker and NetArchTest over eleven isolated shapes (§2). The record's conclusion is **correct**; its evidence is incomplete for the claim it makes, which is advisory A5 rather than blocking | **MET** |
| B3 | the gap closed by whatever mechanism the probe establishes; cost stated as a MEASURED figure | Roslyn `SemanticModel` scan folded into the same `[Fact]`. Cost re-measured by me on the restored tree: **36 passed, Duration 7 s reported, `wall=9.57s`** — matching §4's "≈7s reported (≈9.3–9.5s wall)" exactly. §4 is honest, including that it states the *added* ≈3 s rather than burying it | **MET** |
| B4 | armed with the async-lambda shape in TWO services, failure names both offending types, restore `cmp`-identical on a forced rebuild | The record's arming is verbatim and its **control** is the load-bearing part — Cecil alone reports `IsSuccessful=True Failing=[]` on the same mutated state (`:334-342`). I did not re-run the implementer's arm; I ran **my own three-service mutation** instead, and the message named all three types verbatim (§3). Restore protocol followed by me exactly: `cp` backups, `cmp` on all five files, `sha256sum` identical to pre-state, `touch`, `--no-incremental` rebuild, confirming 36/36 | **MET** |
| B5 | id 76's §5 boundary table updated to record the gap closed, **and this entry states how many real violations existed in the newly visible population — a count read off bullet 1's enumeration** | `progress/impl_application_layer_depends_on_infrastructure_unguarded.md:362-407` carries the GAP CLOSED note. The count **is** a count and not an assurance — it is stated twice and corroborated live (`closureOffenders.Count == 0` on every run). But it is read off a population that is missing one site, and the id 76 record propagates the error in its own words at `:403`: *"the 11 `unitOfWork.ExecuteAsync(async ct => …)` sites across Billing, Fulfillment and Orders"* — a service list that excludes the very service whose site was missed. The number survives (I checked the missed file independently: `grep -n "Infrastructure" src/Projector/Application/ProjectionApplyService.cs` returns nothing), but only because I checked | **NOT MET as stated — D1** |
| B6 (task-brief) | `CLAUDE.md`'s ten-row defeat list run against the NEW instrument | §5 below: I ran rows 1, 2, 3, 4, 6, 7 and 10 myself or verified them structurally, and state why 5, 8 and 9 cannot apply differently than the record says | **MET** |

## 1. The depth hypothesis — attacked, and it holds

I built a throwaway `net10.0` console assembly (NetArchTest.Rules 1.3.2 + Mono.Cecil 0.11.3, the same versions the test project resolves) containing eleven isolated `Probe.Application` classes, each with exactly one reference to `Probe.Infrastructure.InfraThing` in a different nesting position, then read the emitted IL for the **measured depth** of the reference and ran the identical NetArchTest rule shape over the same assembly. Depth and catch/miss correlate **11 out of 11**, with no exception:

| # | Shape | `async` present? | Measured depth of the reference | NetArchTest |
|---|---|---|---|---|
| A | sync lambda capturing a method parameter | no | 1 — `<>c__DisplayClass0_0` | **CAUGHT** |
| B | async lambda capturing a method parameter | yes | 2 — `<>c__DisplayClass0_0/<<Do>b__0>d` | **MISSED** |
| C | async lambda capturing nothing | yes | 2 — `<>c/<<Do>b__0_0>d` | **MISSED** |
| D | **sync lambda inside a local function**, capturing the parameter (the brief's own falsifier) | no | **1** — `<>c__DisplayClass0_1` | **CAUGHT** |
| E | sync lambda inside another sync lambda | no | **1** — `<>c__DisplayClass0_1` | **CAUGHT** |
| F | **sync ITERATOR local function** capturing the parameter | **no** | **2** — `<>c__DisplayClass0_0/<<Do>g__Gen\|0>d` | **MISSED** |
| G | **sync iterator local function inside a capturing sync lambda** | **no** | **2** — `<>c__DisplayClass0_1/<<Do>g__Gen\|1>d` | **MISSED** |
| H | async local function (not a lambda) capturing the parameter | yes | 2 — `<>c__DisplayClass0_0/<<Do>g__Inner\|0>d` | **MISSED** |
| I | async method on the outer type | yes | 1 — `<Do>d__0` | **CAUGHT** |
| J | sync lambda nested three lexical levels deep | no | **1** — `<>c__DisplayClass0_1` | **CAUGHT** |
| K | async lambda capturing only `this` | yes | 1 — `<<Do>b__1_0>d` | **CAUGHT** |

**The brief's proposed falsifier does not falsify, and the reason is worth recording**: D, E and J put a sync lambda one, two and three lexical levels deeper and every one is still **caught**, because C# emits display classes as *siblings* at depth one of the declaring type and chains them by field reference (`CS$<>8__locals`), never by type nesting. Lexical depth and IL depth are different quantities. So the hypothesis survives the test the brief asked for — but that test could never have refuted it either, and passing it is not evidence.

**F and G are the evidence.** Both are entirely synchronous — no `async`, no `await`, no async lambda anywhere — and both are **missed**, because an iterator local function inside a capturing scope puts its state machine at depth two exactly as an async lambda's does. That is the decisive result: it disproves "async" as the cause independently of the record's own argument, which rests solely on the non-capturing async lambda (shape C) — a case that is *still async*, so it can only show that capture is not the cause, never that `async` is not. **The record's conclusion is right and its evidence does not reach it.** Advisory A5 offers the two rows.

Also confirmed, against the record's §2 table row by row: the depth-one/depth-two boundary is exactly where it says (A, I, K caught; B, C missed), and `ResideInNamespaceMatching` never matches a compiler-generated type at any depth (`Namespace == ""` on every one of them, printed in my dump).

## 2. The instrument choice — sound, and I could not defeat it

The argument for Roslyn over a deeper Cecil walk is *"a depth-N walk closes depth two and opens depth N+1"*. My table above is the strongest support the record does not claim: the family of depth-two shapes is **larger than the record knew** (iterators and async local functions, not just async lambdas), and nothing bounds it — C# adds lowering forms between language versions. Against id 68's five-round escalation history, choosing an instrument for which the escalation does not exist is the right call, not the expensive one.

I attacked the semantic model at depths and shapes the record did not test, on the real six-service tree, three mutations in three services in one build and one run. The failure message named **all three**:

```
Offending types: OrderToCash.Billing.Application.CreditHoldService, OrderToCash.Orders.Application.Sagas.SagaFactHandler, OrderToCash.Projector.Application.ProjectionApplyService
```

| Probe | Shape, and why it is a candidate defeat | Result |
|---|---|---|
| **S1** | a **sync iterator local function** (§1's shape F/G — depth two with no `async` in it) declared **inside** the `unitOfWork.ExecuteAsync(async ct => …)` lambda of `Billing/Application/CreditHoldService.cs`, capturing `command`. Three lowering levels below the declaring type and invisible to any depth-two Cecil patch | **CAUGHT** |
| **S2** | a **file-scope `using` alias** — `using ReviewProbeAlias = OrderToCash.Orders.Infrastructure.SystemClock;` in `Orders/Application/Sagas/SagaFactHandler.cs`, used as `new ReviewProbeAlias()` inside the async lambda. The point of attack: **no Infrastructure text appears anywhere inside the type declaration**, and the scan enumerates `SimpleNameSyntax` only under `BaseTypeDeclarationSyntax`, so the using directive is out of its walk. `GetSymbolInfo` resolves the alias to its target anyway | **CAUGHT** |
| **S3** | the **`async (document, ct) =>` two-parameter site** the record's population never listed, in `Projector/Application/ProjectionApplyService.cs` — chosen to answer whether D1 is a coverage hole or only a record error | **CAUGHT** (so D1 is record-only) |
| **S4** | control against false positives: a comment **and** a raw string literal in `Notifications/Application/NotificationDispatchService.cs` carrying the violating text verbatim | **NOT named** — correctly silent |

## 3. The new instrument's own premises — measured, not reasoned

*Does it see what the compiler sees?* I instrumented `FindOffendingApplicationTypes` to report, per service, the compilation's error diagnostics and the number of `SimpleNameSyntax` nodes inside Application-namespace types for which `GetSymbolInfo` returns **neither** a symbol nor a candidate — the silently-unresolved node that would be a false green:

```
Gateway:       errorDiagnostics=0  []        appSimpleNames=742   unresolved=0
Orders:        errorDiagnostics=0  []        appSimpleNames=2108  unresolved=8   (all `nameof`)
Fulfillment:   errorDiagnostics=2  [CS8795]  appSimpleNames=687   unresolved=0
Billing:       errorDiagnostics=8  [CS8795]  appSimpleNames=954   unresolved=1   (`nameof`)
Notifications: errorDiagnostics=0  []        appSimpleNames=735   unresolved=1   (`nameof`)
Projector:     errorDiagnostics=0  []        appSimpleNames=132   unresolved=1   (`nameof`)
```

Three findings. **First, §3's residual-diagnostics claim is exactly right** — 8 in Billing and 2 in Fulfillment, all `CS8795`, and nothing else anywhere; I verified independently that the four `[GeneratedRegex]` validators are Presentation-only and that no Application file mentions `Regex` at all, so the source-generator gap cannot touch a reported type. **Second, resolution is genuinely complete today**: 11 unresolved nodes out of 5,358, every one the `nameof` identifier itself, which references nothing. So the premise holds as of this tree — the guard really is reading what the compiler reads. **Third, nothing asserts that it stays true** — advisory A1.

*Is it looking at the thing the claim is about?* Yes, with one bounded narrowing. It enumerates by **declared namespace** over the whole `src/<Service>/` tree rather than by folder, so an Application-namespace type in an unexpected folder is still covered; `Seed` is excluded consistently with the Cecil half's `_serviceAssemblies`. The narrowing is advisory A3.

## 4. Effort record (for the eventual `history.md` entry, which is not owed while REJECTED)

Derived from file mtimes, stated as such rather than as a reading off a clock: dispatch anchor `progress/current.md` **19:02:54**, implementation record last saved **19:44:02**, `quality_feature83.log` **19:43:15** — **one session, ≈41 min**. (`ApplicationInfrastructureLayeringTests.cs`'s mtime is **mine**, from this round's restore `touch`, and is not evidence about the implementer.) My review round 1: ≈50 min — one probe assembly of eleven shapes, four mutations across four services, one guard instrumentation, three `Architecture.Tests` runs, two `--no-incremental` builds.

## 5. Defeat list — rows run against the NEW instrument, by me

| # | Attack | What I did, and the result |
|---|---|---|
| 1 | Delete the behaviour | **Ran** as the inverse: three real violations introduced in three services, all three named; removing them restores 36/36. The record's own arm additionally proves the **control** (Cecil alone green on the same state) |
| 2 | Corrupt a payload field | **Ran in the form that applies here.** This guard's "payload" is the offending **type name** in the message. S1–S3 prove the message carries the *real* resolved type per service; S4 proves it does not carry a type whose only evidence is text |
| 3 | Substitute a valid sibling identifier | Verified structurally and by the record's verbatim failure: `_closureProbeServices` is paired 1:1 with `_infrastructureNamespaceRoots` and guarded by its own new `[Fact]`, whose message names both tables. The sibling family here (`OrderToCash.<Service>.Infrastructure`, six members) is exactly what that guard covers |
| 4 | Shadow the pattern from a comment | **Ran** (S4) — silent, as it must be: comment trivia produces no `SimpleNameSyntax` |
| 5 | Hide the real thing in a dead region | Not re-run: `ParseSourceFile:387-402` rejects region-liveness directives outright with a message naming the file and the directives, and the record captured that failure verbatim. A defeat of this shape fails **loudly**, which I confirmed by reading the code path rather than by re-mutating |
| 6 | Hide in a raw/verbatim string | **Ran** (S4, a `"""…"""` raw literal) — silent, correctly |
| 7 | Drop an optional element | Verified in the shipped code: `ReferencesInfrastructureRoot:332-352` iterates **every** candidate rather than `FirstOrDefault()`, and the doc comment records that the first draft did not. My S2 alias probe exercises the resolution path this protects |
| 8 | Compare a literal to a literal | N/A, and genuinely so: every comparison is against a symbol resolved from a real `CSharpCompilation` over the real source — which my §3 instrumentation confirms is actually resolving, rather than returning null and matching nothing |
| 9 | Satisfy one half of a two-part claim | **Ran, and this is the sharpest row for a combined assertion.** `Assert.True(netArchResult.IsSuccessful && closureOffenders.Count == 0, …)` could be carried by either half. My three mutations are all depth-two, so the Cecil half is **green on all three** and the closure half alone fails the test — the same separation the record's control demonstrates, reproduced independently on three different files |
| 10 | Let a build-output copy join the population | Verified structurally: `IsUnderBuildOutputDirectory:459-463` excludes by **path segment**, the shape `CompositionRootDelegationWiringTests:633` already uses, not a content filter over `grep` output. The record additionally ran it live |

## Defects

### D1 (BLOCKING) — the population sweep filters by a narrower predicate than the claim it makes, and misses one of twelve sites

**Where:** `progress/impl_architecture_rule_cannot_see_references_inside_async_lambdas.md:32-89` (the command, the table, and the "10 files, 11 sites, 3 of the 6 services" claim at `:54` and `:87`, repeated at `:373`), propagated into `progress/impl_application_layer_depends_on_infrastructure_unguarded.md:403`.

**What:** bullet 1's population is *"every Application-layer method … whose work happens inside an async lambda passed to another method (`unitOfWork.ExecuteAsync(async ct => ...)` **and any sibling shape**)"*. The sweep is `grep -c "async ct =>"` — the dominant literal, not the claim. A predicate matching the claim returns **12 sites in 11 files across 4 services**; the missing one is `src/Projector/Application/ProjectionApplyService.cs:27`, `async (document, ct) =>` passed to `writer.ApplyAsync(...)`.

**Why it matters, and why it is blocking rather than advisory.** The missed site is not a technicality: it captures the method parameter `envelope`, so it is a depth-two state machine — a full member of *"the code the guard currently cannot see into"*, which bullet 1 calls "the entry's own justification". The record's §1 is presented as complete (*"Classified one line per hit"*, *"this reconciles with the leader's dispatch list item by item"*), bullet 5's count is defined as *read off bullet 1's enumeration*, and the id 76 record now states in its own words that the count covers *"the 11 sites across Billing, Fulfillment and Orders"* — a sentence that both undercounts and names the wrong set of services. `CLAUDE.md`'s most-repeated rule is precisely this: a sweep must not filter by a predicate narrower than the claim, and a missed hit must appear as an **unclassified line** rather than vanish into a sentence. This repository has rejected feature 27 and id 72 for the same shape with smaller consequences, and the wrong number is one edit from entering `history.md` permanently.

**What it is not.** It is **not** a coverage hole. I proved the guard reaches the missed site (S3) and that the site is clean (`grep -n "Infrastructure" src/Projector/Application/ProjectionApplyService.cs` → no hits), so **the closing count of zero is still true**. No code change, no re-arming and no suite re-run are required to clear it.

## Advisories

- **A1 — the new instrument never asserts its own premise, and the sibling guard does.** `ApplicationInfrastructureLayeringTests.cs` contains no `GetDiagnostics` call anywhere (`grep`: no hits), while `CompositionRootDelegationWiringTests.ParseFile:594` asserts its parse diagnostics are clean. Today that premise holds — I measured it (§3) — but Billing's compilation already carries **8 error diagnostics**, so the instrument is demonstrably *not* error-free, and nothing notices. A silently unresolved symbol is a false green, in a guard whose whole purpose is that a violation cannot hide. One bounded assertion closes it: per service, either the unresolved-Application-name count is 0 (excluding `nameof`), or the error-diagnostic ids are a subset of a **literal** allow-list (`CS8795`) with the reason named. If it is not done in the fix round, it is a backlog entry — it must not be left as a sentence in this review.
- **A2 — `FindGeneratedGlobalUsingsFile:415-428` takes `candidates[0]` from an unordered `Directory.GetFiles`.** Notifications currently has **two** (`obj/Debug/...` and `obj/Release/...`); I diffed them and they are identical today, so nothing is wrong now — but the selection is arbitrary and the guard's whole reference set for a service hangs off it. One `OrderBy` plus an assertion that the candidates agree in content.
- **A3 — the Roslyn half checks only each service's OWN Infrastructure root, while the assertion message claims "any service's Infrastructure namespace".** `_closureProbeServices` pairs one root per service and `FindOffendingApplicationTypes` matches against that one; the Cecil half passes all six roots. Unreachable today — I checked every service `.csproj`, and no service references another service's project, so a cross-service Infrastructure reference cannot compile in the real build — but the narrowing should be stated in the doc comment rather than discovered later.
- **A4 — no ported-idiom ledger row, and there is one worth writing.** `sdd: false` puts the ledger in `progress/impl_<feature>.md`, and this feature has none. It ports no #7 mechanism, so the omission is defensible — but the row #9 would actually want is about the **instrument class**: #7 enforced layering with import-path linting over source (`order-to-cash-nestjs/eslint.config.mjs:250-287` at `63f130e`, a `no-restricted-imports` rule with `**/infrastructure/**` patterns, scoped to `apps/*/src/domain/**`), and an import-path rule over source text **cannot have a depth blind spot at all** — the property was free there. #8's id 76 rendered the same kind of rule over compiled IL, where it is not free, and this feature is what supplies it back, via a source-level semantic model. That is exactly a *"#7 relied on X; in #8 that property is supplied by Y"* line, and #9 — likely to reach for an AST import check — needs to know which half of the property its instrument gives it for nothing.
- **A5 — §2's conclusion is right and its own evidence cannot reach it.** Every missed shape in the record's table (`OnlyAsyncLambdaNoCapture`, `CapturesLocalParam`) still contains `async`, so the table can show that *capture* is not the cause but never that *`async`* is not. Two rows from §1 above close that gap and cost nothing to paste: a **sync iterator local function** capturing a parameter (depth two, missed) and the **same inside a capturing sync lambda** (depth two, missed) — neither contains `async`. Worth adding with the counter-observation that a sync lambda nested one, two or three lexical levels deeper is still **caught**, because display classes are siblings at depth one: lexical depth is not IL depth, and a reader who conflates them will re-derive the wrong rule.

## What must change before re-review

1. **D1 only.** Re-run the population sweep with a predicate that matches bullet 1's claim rather than the dominant literal, paste the command and its **complete** output, and add the twelfth site as its own classification row (`src/Projector/Application/ProjectionApplyService.cs:27`, `async (document, ct) =>`, no Infrastructure reference). Correct "10 files, 11 sites, 3 of the 6 services" to **11 files, 12 sites, 4 of the 6 services** at `:54`, `:59`, `:77`, `:82`, `:87` and `:373`, and correct the propagated sentence in `progress/impl_application_layer_depends_on_infrastructure_unguarded.md:403` — it currently names the wrong service set as well as the wrong count. State explicitly that the closing count of **zero** is unchanged, and why (the added site carries no Infrastructure reference, and the guard covers it — proved by this review's S3 probe).
2. **No code change, no re-arming, no `./quality.sh` re-run.** The guard, its arming, its control and its cost are verified. Do not touch `tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs` for D1.
3. **A1** either fixed in the same round (one assertion, one line of allow-list, armed by forcing an unresolved symbol) or filed as a numbered backlog entry. It must not be discharged by prose in the next record.
4. A2, A3, A4 and A5 are record-and-comment work, cheap enough to fold into the same round; none of them blocks.

## Restore evidence for my own probes

Five files mutated (`Billing/Application/CreditHoldService.cs`, `Orders/Application/Sagas/SagaFactHandler.cs`, `Projector/Application/ProjectionApplyService.cs`, `Notifications/Application/NotificationDispatchService.cs`, `tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs`), all restored from `cp` backups taken before mutation — never `git checkout --`. `cmp` clean on all five **and** `sha256sum` identical to the pre-mutation values (`4765410c…`, `9bdd8275…`, `5a308fc3…`, `c290e272…`, `fda7b1fc…`); `grep` for `ReviewProbe|REVIEW INSTRUMENTATION|reviewUnresolved` across `src` and `tests` returns nothing; `touch` on all five, `dotnet build --no-incremental` succeeded, confirming run **36/36 passed, 7 s** (`wall=9.57s`). `git status --short | wc -l` is **180**, exactly as I found it. One build or test run at a time throughout; `pgrep -fl "dotnet (build|test|format)"` was clear before the first. `feature_list.json` untouched by me.

---

# Re-review (round 2) — APPROVED

## Status header

- **Verdict: APPROVED.** D1 is genuinely cleared, A1 is fixed with a guard I armed myself and then attacked three ways, A2/A3/A5 are done and A4 I agree with the leader's ruling. Two new advisories (**A6**, **A7**), both record-level, neither blocking and neither requiring a further round.
- **Transition:** id 83 → `done`. **I did not edit `feature_list.json`** — the leader's bounds for this round forbade it and `CLAUDE.md` makes that file single-writer. The leader applies the transition and writes the `history.md` entry from the effort record below.
- **What I ran:** four probes (P1–P4) with three forced-rebuild cycles and six `Architecture.Tests` runs, plus an independent re-derivation of the population with my own predicate. **What I did not re-run:** `./quality.sh` (the 1908/1908 reading is the leader's, explicitly out of bounds, and nothing this round touched a project outside `Architecture.Tests`) and the round-1 official arming (the two-service Infrastructure-construction mutation), which was verified in round 1 and unchanged by this round.
- **No `SA-n` routing item.** Nothing in `specs/shared/` causes or constrains this guard; confirmed again that the shared spec is untouched by this feature.

## CHECKPOINTS.md — boxes walked (round 2)

**C1 — harness.** [x] files present; [x] `progress/current.md` and `history.md` present; [x] agent definitions unchanged; [x] `./init.sh` exits 0 — **not re-run by me**, taken from the leader's pre-dispatch verification; no harness file, agent definition or `feature_list.json` entry changed this round.

**C2 — state.** [x] exactly one `in_progress` (id 83), read from `feature_list.json` myself; [x] all statuses valid; [x] `current.md` describes this session; [x] no `blocked` feature; [x] **"every `done` feature has passing tests"** — now applicable, because this approval makes id 83 `done`: its tests are `Architecture.Tests` **36/36**, which I ran green three times today on the restored tree (9 s reported, 11.78 s wall).

**C3 — architecture.** [x] NetArchTest suite run by me (36/36, three runs on restored source, plus three armed runs); [x] no cross-service DB access; [x] no shared runtime code added — the round-2 change is additive inside one existing test file, no new project, no new package (`Directory.Packages.props` untouched by this round); [x] no `decimal` in domain arithmetic touched; [x] no stray debug logging — `grep` for my own probe markers (`SharedKernel.dll` exclusion, `P3 PROBE`) in the test file returns nothing, output recorded below.

**C4 — verification.** [x] `./quality.sh` green at 1908/1908 — **leader's reading, not re-run by me**, out of bounds and unaffected: nothing under `src/` changed this round; [x] domain tests pure; [x] integration tests unaffected; [ ] **coverage thresholds** — not verified by me, no production code changed in this feature at all; [x] no Jest.

**C5 — session close.** [x] no suspicious untracked files — `git status --short | wc -l` is **181** at the end of my round, identical to the count at its start; [x] **`progress/history.md` entry with effort record** — the record is supplied below and the entry is the leader's to write; this approval is conditional on it existing, per `CLAUDE.md`; [x] `feature_list.json` reflects true state (`in_progress` throughout my round, untouched by me); [x] Claude did not commit — I ran no git command that writes the index or working tree.

**C6 — SDD.** **Not applicable**: id 83 is `"sdd": false`.

**C7 — reuse fidelity.** [x] `specs/shared/` untouched by this feature; [x] no amendment raised or needed; [x] effort record complete — below, including the one figure that did not reconcile.

## D1 — cleared, and the corrected population is as wide as the claim

I re-derived the population myself rather than re-running the implementer's command, and deliberately with a **different** predicate, because a corrected count that is still narrow is the same defect twice.

```
$ find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
    | xargs -0 grep -nE 'async[[:space:]]*(\([^)]*\)|[A-Za-z_][A-Za-z0-9_]*)[[:space:]]*=>' | sort
```

Restricted to `/Application/`, this returns **exactly the same twelve lines** the corrected §1 lists — the eleven `async ct =>` sites plus `src/Projector/Application/ProjectionApplyService.cs:27`'s `async (document, ct) =>` — over **11 distinct files** and **4 services** (`cut -d: -f1 | sort -u | wc -l` → 11). My first attempt at this predicate was itself too narrow (`[^)]*` cannot cross a `)`, so it missed the two-parameter form) and is worth recording as evidence that the shape D1 names is easy to reproduce rather than careless.

I then widened past the arrow form altogether, to test whether the corrected population is still narrower than "any sibling shape":

- `async delegate`, `static async`, `async static` in `src/*/Application/` → **no hits**.
- an `async (`-at-end-of-line multi-line lambda header → **no hits**.
- `yield return` in `src/*/Application/` → 14 hits, all in `src/Orders/Application/Sagas/SagaStepTable.cs`, all inside `BuildRows()` — which is declared at **four-space indentation as a type member** (`175: private static IEnumerable<KeyValuePair<string, IReadOnlyList<SagaStep>>> BuildRows()`), i.e. an iterator **method**, whose state machine sits at depth **one** and which my round-1 probe table shows is **caught**. It is therefore correctly *not* a member of the blind population — unlike shapes F and G in §1, which are iterator **local functions** inside a capturing scope.

**The twelfth site is classified, not merely listed.** `§1`'s table row gives it a reason — *"calls `signalPublisher.PublishAsync` (a port) and `logger.LogError`; no `OrderToCash.Projector.Infrastructure` construction or call anywhere in the lambda body"* — which I verified by reading the file: the lambda captures `envelope` (used at `:53`), makes two calls, and the file contains the string `Infrastructure` zero times. §1 is corrected **in place** at `:51`, `:52`, `:84`, `:114`, and §6's count at `:405` now reads "12 sites, 11 files, across 4 of the 6 services".

**The propagated sentence is corrected and the retired wording is gone.** I enumerated on the wording of the claim **being retired**, not the one being written — the trap that cost feature 24 a third round:

```
$ grep -rn "11 sites\|10 files\|3 of the 6 services\|Billing, Fulfillment and Orders" progress/*.md | grep -v '^progress/review_'
```

The only hit inside id 83's own two records is `impl_architecture_rule_cannot_see_references_inside_async_lambdas.md:536`, which is the deliberately preserved quotation *"originally: 'the 11 sites across Billing, Fulfillment and Orders'"* — the record of what was wrong, correctly retained. `impl_application_layer_depends_on_infrastructure_unguarded.md:402-414` now names Projector explicitly and carries 12/11/4. Every other hit belongs to a different feature's record making a different claim. **D1 is cleared.**

## A1 — armed by me, then attacked, and it survives both halves being removed in turn

The leader ruled FIX over file. I agree with the ruling and it is the right call: a semantic model that silently fails to resolve is a false green inside a guard whose entire purpose is closing a false green.

**P1 — the named arm, re-run by me.** Removing `.Where(nameNode => nameNode.Identifier.Text != "nameof")` (line 350), `dotnet build --no-incremental`, `dotnet test --no-build`:

```
Failed OrderToCash.Architecture.Tests.ApplicationInfrastructureLayeringTests.ApplicationMustNotDependOnInfrastructure [6 s]
   Orders's closure-probe compilation left 8 SimpleNameSyntax node(s) inside an Application-namespace type unresolved ...
   OrderToCash.Orders.Application.Ports.ConsumerNames — 'nameof' at .../src/Orders/Application/Ports/ConsumerName.cs: (27,51)-(27,57); ... (8 nodes, every one named with file and line span)
Failed!  - Failed: 1, Passed: 35, Skipped: 0, Total: 36, Duration: 6 s
```

Reproduces the record's arming exactly, and the message names the service, the count, the declaring type, the symbol text and the file/line span. The arm is real.

**P2 — can a symbol fail to resolve in a way the allow-list swallows?** I starved the reference set: one extra filter dropping `OrderToCash.SharedKernel.dll` from `references`, which is what a future broken reference, a renamed assembly or an SDK change actually looks like. Result — the compilation carried **287 error diagnostics** and `AssertCompilationPremiseHolds` failed loudly:

```
carries 287 error diagnostic(s) ... 148 x CS0246, 45 x CS0103, 43 x CS0234, 21 x CS1061, 16 x CS0029, 13 x CS1729, 1 x CS8121, 1 x CS8795
Failed!  - Failed: 1, Passed: 35, Skipped: 0, Total: 36
```

**The allow-list does not swallow it.** `CS8795` appears once in that list — exactly the one id that is allowed — and the other 286 diagnostics are on eight ids that are not, so the assertion fires. This is the correct shape for an allow-list: a **literal** expected set with everything else derived by subtraction, which is the form `CLAUDE.md` requires precisely because a violation must stay *in* the candidate set rather than removing itself from it.

**P3 — and would a *new* unresolvable construct appear as a diagnostic I allow rather than as an unresolved symbol I catch?** This is the leader's sharpest question, so I answered it by removing the half that would otherwise mask the answer: same starvation, but with the `AssertCompilationPremiseHolds(compilation, serviceFolder);` call commented out, simulating a future allow-list widened wrongly.

```
Orders's closure-probe compilation left 131 SimpleNameSyntax node(s) inside an Application-namespace type unresolved ...
   — 'var' at ...; — 'UniqueId' at ...; — 'From' at ...
Failed!  - Failed: 1, Passed: 35, Skipped: 0, Total: 36
```

**The two assertions are independent, and neither is load-bearing alone.** A construct that hides from this instrument must simultaneously (a) raise no error diagnostic outside the literal allow-list and (b) still resolve to a `Symbol` or at least one `CandidateSymbol`. That conjunction is a much stronger premise than the one advisory A1 found unasserted, and it is now asserted rather than observed. I could not construct a defeat.

**Is `CS8795` the only id that should be allowed?** Verified by enumeration rather than argument, because the allow-list's safety rests on a claim about *where* that diagnostic can arise:

```
$ grep -rln "GeneratedRegex" src --include='*.cs' | grep -v '/obj/' | sort
src/Billing/Presentation/Rpc/CreditRequestValidator.cs
src/Billing/Presentation/Rpc/InvoiceRequestValidator.cs
src/Billing/Presentation/Rpc/PaymentRegisterRequestValidator.cs
src/Fulfillment/Presentation/Rpc/StockRequestValidator.cs
src/SharedKernel/DomainEventEnvelope.cs
src/SharedKernel/OrderNumber.cs

$ find src -path '*/Application/*' -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n 'GeneratedRegex\|partial '
(three hits, all the words "partial success" inside a doc comment on IFactPublisher.cs — no partial member declaration in any Application file)
```

So today no Application-namespace type declares a source-generator partial, the four `[GeneratedRegex]` sites are all `Presentation/Rpc/*RequestValidator.cs`, and the doc comment's justification on `_allowedErrorDiagnosticIds:173-183` is accurate. That leaves the residual case as advisory **A7** below, not a defect.

## A2 — deterministic, and the agreement assertion actually fires

Ordering: `Directory.GetFiles(...).OrderBy(path => path, StringComparer.Ordinal)` at `:509`, so `candidates[0]` is `obj/Debug/...` deterministically (ordinal `Debug` < `Release`). Live population confirmed by me — Notifications is the only service with two candidates:

```
Gateway 1 / Orders 1 / Fulfillment 1 / Billing 1 / Notifications 2 / Projector 1
```

**P4 — would the agreement assertion fire if the two ever diverged?** I diverged the **non-chosen** candidate (appended one `global using` line to `obj/Release/net10.0/Notifications.GlobalUsings.g.cs`) and ran `dotnet test --no-build`, which does not regenerate it:

```
Notifications has 2 *.GlobalUsings.g.cs candidates under .../src/Notifications/obj whose content disagrees — chosen '.../obj...
Failed!  - Failed: 1, Passed: 35, Skipped: 0, Total: 36, Duration: 9 s
```

That is the right unit: the assertion compares **the others against the chosen one**, so it fires on a divergence in a file the guard does *not* read — which is exactly the case where silently picking either one would be wrong and invisible. Restored from my `cp` backup, `cmp` clean, 36/36 green again.

## A3 — the message now matches what each half does, with one harmless under-claim

Checked against the code, not against the record's description of it. The Cecil half calls `HaveDependencyOnAny(_infrastructureNamespaceRoots)` with all six roots (`:189-192`), and the message says so. The Roslyn half iterates `_closureProbeServices`, passing **one** root per service into `FindOffendingApplicationTypes` (`:263-266`, `:271`), and the message now says *"reading each service's own real source against its OWN Infrastructure root only"* (`:210-213`). Neither over-claims. The premise for calling it unreachable also holds: `grep -n "ProjectReference" src/*/*.csproj | grep -vE "SharedKernel|Contracts|Cqrs"` returns only `src/Seed/Seed.csproj`'s three references, so no service references another service.

One residual **under**-claim, deliberately left as an observation rather than a defect: the message says the closure-aware half catches references *"confined to a lambda closure or async state machine nested two or more levels below its declaring type"*, when in fact it catches **every** Application-namespace reference at any depth, nested or not. Under-claiming cannot mislead a reader into inferring coverage the code lacks, which is the failure mode A3 was about.

## A4 — I agree with the leader's ruling on the ledger row

The leader checked the `#7 relied on X` half at source, which is the half that cannot fail on its own and the half #9 inherits. I re-read the row at `:655-676` against that check and I agree it is sound: its claim is about a **property of the instrument class** — an import-path lint over source text has no lowering to hide behind, so depth-blindness cannot arise — and not a claim that #7 guarded the Application boundary. The row does not assert the latter, and the scoping to `apps/*/src/domain/**` is stated in the row itself. The `in #8` half is this feature's own shipped code, which I have now armed four ways. Nothing to add.

## New advisories (neither blocking; no further round)

- **A6 — §4 is titled "Measured cost — never an estimate" and now under-states the shipped cost by ≈29%.** `:284-288` records **7 s** reported / ≈9.3–9.5 s wall, measured *before* A1 added `compilation.GetDiagnostics()` — which forces full binding of all six per-service compilations and is not free. My own three green runs on the restored tree today report **9 s** and `wall=11.78s`, and the record's own confirming run at `:717` says 9 s, so the true figure is present in the record; but §4, which is the artefact acceptance bullet 3 is graded against, was not revisited, and `:647` then reasons *from* the stale number (*"a scan that already costs ≈3s per the measured §4 cost"*). It reconciles — 7 s + the diagnostics pass ≈ 9 s — so this is a stale cross-reference rather than an unexplained number, and I am not rejecting for it. **The leader should carry 9 s (11.78 s wall) into `history.md`, not 7 s**, and fold a one-line correction into §4 when next editing that file.
- **A7 — the `CS8795` allow-list is applied to all six services, and nothing asserts the diagnostic arises outside Application namespaces.** Verified safe today by the enumeration above (zero partial members in any `src/*/Application/` file; all four `[GeneratedRegex]` sites are Presentation). The residual case is narrow and future-tense: if an Application-layer type ever adopted a source-generator partial, `CS8795` would be allowed through *and* the generated implementation would be absent from the scan's syntax trees, so a reference inside generated Application code would be unseen. P3 shows the unresolved-symbol half would still catch a compilation whose symbols went missing, which bounds the exposure to generated code that resolves cleanly. Worth a sentence in the doc comment if that file is touched again; not worth a round now, and not worth a backlog entry on a construct that does not exist in the repository.

## Restore evidence for my own probes

One file mutated by me (`tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs`, three separate mutations: P1's `nameof` exclusion removal, P2's reference starvation, P3's starvation plus disabled diagnostics assertion) and one build-output file (`src/Notifications/obj/Release/net10.0/Notifications.GlobalUsings.g.cs`, P4). Both restored from `cp` backups taken before mutation — **never `git checkout --`**. `cmp` clean on both; `sha256sum` of the test file is `5e0d0d818b345f00219a2594f5dec91dfe9d05902d12d23e721508e391b998d4`, **identical to the value I took before my first mutation**; `grep` for my probe markers returns nothing; the `nameof` exclusion is confirmed present at line 350 by re-reading the line. `touch` then `dotnet build --no-incremental` then `dotnet test --no-build` → **36/36 passed, 9 s (wall=11.78s)**, and 36/36 again after P4's restore. `git status --short | wc -l` is **181**, the count I found at dispatch.

**`git diff` on the mutated test file printed zero lines, and that is worthless as restore evidence, not reassuring.** The file is **untracked** — `git ls-files tests/Architecture.Tests/ApplicationInfrastructureLayeringTests.cs` returns nothing — so `git diff` on it cannot fail whatever its contents, which is the guard-that-does-not-guard sitting inside the restore step of the arming protocol itself. I ran it because the brief asked for it and I record it as vacuous; the load-bearing evidence is `cmp` plus the `sha256sum` match plus re-reading line 350.

One build or test process at a time throughout; `pgrep -fl "dotnet (build|test|format)"` matched nothing of mine before each build (the only match was the invoking shell itself, which is the self-matching hazard `CLAUDE.md` names — I read it as "clear" on that basis rather than treating my own command line as a running build).

## Effort record (for `progress/history.md` — the entry cannot be written without it)

Wall-clock from artefact mtimes, stated as such rather than as a reading off a clock (local CEST, 2026-09-12):

- **Implementation: 1 session, ≈41 min** — 19:02:54 (dispatch anchor, `progress/current.md`) → 19:44:02 (implementation record last saved). These two anchors are **quoted from round 1 §4 and are no longer re-derivable**: the record has been rewritten twice since, so its mtime today is round 2's.
- **Review round 1: ≈19 min** — 19:44:02 → 20:03:11 (`progress/review_architecture_rule_cannot_see_references_inside_async_lambdas.md` saved). **This supersedes round 1's own "≈50 min" at `:97`, which does not reconcile with its own artefacts**: the review can only have begun after the implementation record's last save, and the review file was written 19 min 9 s later. Round 1's figure was an estimate presented as a measurement; the mtime-derived figure is the one for `history.md`. Verdict REJECTED — 1 blocking defect (record-only), 5 advisories.
- **Fix round 2: ≈10 min** — 20:03:11 → 20:13:10 (implementation record saved), with `progress/impl_application_layer_depends_on_infrastructure_unguarded.md` corrected at 20:06:20 en route. One code change (A1), one record correction (D1), four advisories folded in.
- **Review round 2 (this one): ≈12 min** — 20:13:10 → 20:25:45. Four probes (P1 the named arm, P2 reference starvation, P3 starvation with the diagnostics half disabled, P4 the A2 agreement assertion), three forced-rebuild cycles, six `Architecture.Tests` runs, one independent re-derivation of the population with a different predicate than the implementer's. Verdict APPROVED.
- **Total ≈82 min** from the first implementation artefact to this verdict, of which **≈31 min was review across two rounds** and **≈10 min was rework**.

**Sessions: 1 implementation + 1 review round + 1 fix round + 1 re-review round. REJECTED on round 1, APPROVED on round 2.**

**Counts for the entry, each read off a run in this session:** `Architecture.Tests` **36/36** (unchanged from round 1 — A1's two assertions and A2's one assertion were folded into methods the existing test already calls, so no new [Fact]); reported duration **9 s**, wall **11.78 s** — **not** the 7 s that §4 of the implementation record still carries, see advisory A6. Repository suite **1908/1908 across 18 projects** is the leader's pre-dispatch reading, not re-run by me, and unaffected: nothing under `src/` changed in either round of this feature.

**There is no #7 baseline for this feature and the entry should say so.** Id 83 exists because #8's own instrument — a Cecil/IL walk introduced by id 76 — has a depth blind spot that #7's source-text import lint could not have had. It is not a guard dropped in translation; it is the cost of the instrument #8 chose, and the ledger row at `:655-676` is what carries that lesson to #9.

## Transition

**id 83 → `done`, applied by the leader.** I did not write `feature_list.json`: the leader's bounds for this round forbade it, and `CLAUDE.md` makes that file single-writer precisely so a reviewer closing one feature cannot silently revert another writer's uncommitted entry. The approval is conditional on the `progress/history.md` entry existing with the effort record above — a feature without an effort record is not closeable in this repository.
