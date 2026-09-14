# Review — batch D5 (backlog ids 78 and 90)

**Verdict: id 78 APPROVED. id 90 APPROVED.**

Scoped review under the maintainer's budget ruling: I probed the claims rather than re-running the world. I did **not** re-run `./quality.sh` (the implementer's run is corroborated below by filesystem evidence). What I ran instead: five mutations of my own across three files, a full documentation-enabled solution build, two full project test runs, a pre-fix re-derivation on a slice of id 78's population taken from `HEAD` via `git archive` (read-only), and every prose sweep of bullets 5–7 re-executed.

## Probe 1 — the guard that could not fail (the verdict question)

Armed by me from scratch: `cp` backup → delete `Math.Max(1, …)` at `SagaCommandDispatchWorker.cs:69` → `dotnet build --no-incremental` → run the named tests → restore from backup → `cmp` identical → forced rebuild → confirming green.

- `DegreeOfParallelismZero_MustNotLeaveExecuteAsyncCompletedWhileTheHostStaysUpAndHealthy` **FAILED**, naming `task status RanToCompletion` and the consequence verbatim as the record records it.
- `DegreeOfParallelismBelowOne_IsClampedToOneRunningConsumerLoop` **FAILED for both cases** (`0` and `-3`), reporting `Observed degree of parallelism: 0`.
- `ACompletedBackgroundService_…` stayed green, correctly — it is the premise demonstration, not the guard.

**It is a change of kind, not a longer sleep.** The failing bullet-2 case resolved in **3 ms** against a **1 s** bound: with the floor the task never settles at all, without it it settles immediately. A sleep-based fix would have taken the full bound. Restored: `cmp` identical, line 69 re-read, `--no-incremental` rebuild, **498/498 green**.

## Probe 2 — the entry's premise, checked by experiment both ways

With the clamp deleted I set the bullet-2 test's degree to `-3` and re-ran: the failure printed **`task status Faulted`**. At `0` the same test printed **`RanToCompletion`**. So the entry's `<= 0` text is wrong and the record's correction is right: **only `0` is silent**; a negative faults `ExecuteTask`, which a real host's `BackgroundServiceExceptionBehavior.StopHost` notices. `ExecuteTask` was non-null in both runs, confirming `Enumerable.Range`'s exception lands *inside* the returned task rather than being thrown out of `ExecuteAsync`. The correction is written where it is read (`SagaCommandDispatchWorker` remarks, `AddOrdersSaga`'s comment), and it is the most valuable artefact in this batch.

## Probes I ran that the record did not

- **Substitution (attack #3) on the composition-time throw.** `options.Dispatch.DegreeOfParallelism < 1` → `options.Command.MaxAttempts < 1`, a real sibling of the same options tree: both theory cases **FAILED** with the "ACCEPTED … without throwing" message — which names the intended defect, so this is not the false-negative shape where a swap fails for an unrelated default.
- **Floor mutated into a ceiling (corruption, not deletion).** `Math.Max(1, x)` → always `1`: `DegreeOfParallelism_BoundsConcurrentDispatchesAcrossOrders` and `HeadOfLineBlocking_…` both **FAILED**, so the upper side really is pinned and `== 1` in the new test is not a one-way assertion. (A literal `var degreeOfParallelism = 1;` will not compile — CS9113, `options` unread — which is itself a small structural guard.)
- **Id 78 bullet 4, armed independently.** A `<see cref="ReviewerProbeNoSuchTypeAnywhereInThisRepository"/>` added to `src/Orders/Application/Sagas/SagaFactResult.cs:11` produced `error CS1574: … could not be resolved` and `Build FAILED` — an **error**, naming the cref and the file. Restored `cmp`-identical.

All five mutations were restored from `cp` backups and verified with `cmp`, never with `git diff` and never with any git command that writes the tree.

## Probe 3 — id 78's population

The 107 pre-fix sites are historical and cannot be re-derived from a tree in which they are fixed. What I verified instead:

- **The class is closed**: `dotnet build OrderToCash.sln --no-incremental` → **0 Warning(s), 0 Error(s)** with documentation generation on across all 28 projects.
- **Nothing was suppressed.** `NoWarn` carries `CS1591` and nothing else anywhere (`Directory.Build.props:42`, no `.csproj` override, no `#pragma warning disable` for any CS15xx/CS0419/CS1734, no `.editorconfig` severity entry). `TreatWarningsAsErrors` is untouched at `:17`.
- **The method reproduces on a slice.** I extracted `HEAD` into a scratch directory with `git archive` (read-only) and built the pre-fix `Contracts` and `Cqrs` projects with docs on: exactly **1** site in Contracts (`Envelope.cs(24,80) CS1574 'OrderPlacedPayload'`) and **2** in Cqrs (`IDispatcher.cs(52,20) CS1584` plus the knock-on `(52,73) CS1658`) — matching table rows 7, 8 and 9 including the record's claim that the CS1658 is the same line, not a second site. CS1570/CS1658 were genuinely surfaced and reported, not dropped.

## Probe 4 — bullets 5, 6 and 7, sweeps re-run

Every enumeration re-executed with path exclusion at the `find` level. `IGNORED` now has 1 hit, the unrelated `SagaFactHandlerTests.cs:171`; `never looks at it again`, `uncalled until`, `ships uncalled` and `has no caller until` all return **0**; `seam` returns **56**, matching the record's arithmetic; `delivered uncalled` returns only `PaymentRegisterService.cs:13`, which is a true past-tense statement.

**"Decided TRUE and left alone" is justified, not convenient.** `SagaFact.cs:41` keeps "consumed from" because `new SagaFact(` appears in exactly two files — `src/Orders/Presentation/SagaFactsConsumer.cs` and `tests/Orders.UnitTests/SagaStepTableTests.cs` — so the operator-cancel envelope never becomes a `SagaFact`. I re-read all four corrected texts (`PlaceOrderCommand`, `Invoice.cs:24`, `BillingFactPayloadMapperTests`, `ICreditDecisionPort.cs:61`) and checked their citations live: `BillingServiceCollectionExtensions.cs:64` does bind `SimulatorCreditDecision`, `AlwaysApproveCreditDecision` does remain in the tree, and `PaymentRegisterService.cs:131` does call `Invoice.MarkPaid`.

## Probe 5 — bullet 3's reasoning

It holds. The two guards cover genuinely different populations, and this is observable rather than argued: `SagaCommandDispatchWorkerTests.BuildProvider` constructs the worker through a bare `ServiceCollection` and never calls `AddOrdersSaga`, so the floor's population is non-empty today. Bullet 1 requires the floor to exist, so replacing it was not available. The throw names the option, the observed value and the consequence, and all three are asserted (`Contains("DegreeOfParallelism")`, `Contains($"configured as {configured}")`, `Contains("reports healthy")`), with `AddOrdersSaga_AtTheProductionDefault_DoesNotThrow` as the control against an unconditional throw. Bullet 4 verified independently: `DegreeOfParallelism` appears at 10 sites outside the unit tests, none of them an environment read, and the "deliberately not configurable" decision with its re-open trigger sits on the property's own `<remarks>`.

## Probe 6 — count reconciliation

Verified the two deltas, which are the claim: **Orders.UnitTests 498/498** and **Architecture.Tests 50/50**, both run by me after a forced rebuild. Baseline 2 034 confirmed in `progress/history.md`; 2 034 + 7 + 1 = 2 042 attributes correctly. The full `./quality.sh` run is corroborated without re-running it: **18** `coverage.cobertura.xml` files written between `00:27:03` and `00:39:21`, three minutes before the record was finalised at `00:42:30`.

One trap worth recording: `dotnet build a.csproj b.csproj` is rejected by MSBuild, and my first confirming run therefore executed a **stale armed binary** and reported 3 failures. The protocol's forced-rebuild step is what surfaced it.

## CHECKPOINTS walked

- **C1** — [x] harness intact; `./init.sh` exits 0 (re-run after my `feature_list.json` edit).
- **C2** — [x] statuses valid; nothing left `in_progress` after this close; `progress/current.md` describes this session.
- **C3** — [x] `Architecture.Tests` 50/50 green, NetArchTest suite included, run not eyeballed; no shared-runtime or domain-purity change in this batch; behaviour edits confined to `src/Orders/Infrastructure`.
- **C4** — [x] full doc-enabled solution build clean; 18-project coverage run corroborated; no mocked brokers introduced; no Jest.
- **C5** — [x] history entries appended with effort records; no commit by me; backups live only in the scratchpad.
- **C6** — n/a, both entries are `sdd: false`.
- **C7** — [x] `specs/shared/` untouched by this batch (`test-matrix.md` mtime 2026-09-12, before D5 began).

## Ported-idiom ledger

Both rows carry file-and-line citations into #7 and I re-derived both halves: `grep -rn "typedoc\|tsdoc\|jsdoc"` over #7's `package.json`, `eslint.config.mjs` and `tsconfig.base.json` returns nothing, so the doc-check row is correctly a **strengthening**; `apps/orders/src/application/commands/saga-dispatch.handlers.ts` has the per-command `@CommandHandler` shape claimed, with no parallelism number to get wrong; and `find apps/orders/src -name '*.spec.ts' | xargs grep -lin "dispatchworker|degreeofparallelism|clamp"` returns no file, so no #7 guard was dropped in translation.

## Defects

**None blocking.** Two advisories, named here and left for the leader — phase 14 is frozen at 13 items and I am filing nothing:

1. **A number in the record that does not reconcile.** `progress/impl_batch_d5_doc_comment_targets_and_dispatch_clamp.md:39` says the Orders build closure holds **38** of the 107 sites; `:382` says it holds **34**. Counting the record's own table gives **38** (rows 7, 8, 9 and 25–59). The 34 is unexplained. It changes nothing about the fix — the solution builds clean — but it is a count claim in a record, and this repository's own rule is that such a figure is either explained or withdrawn.
2. **`DocumentationGenerationTests` covers 11 assemblies, not 28 projects** (`tests/Architecture.Tests/DocumentationGenerationTests.cs:56-68`). The standing check is the build itself, so this is belt-and-braces rather than the guard — but a per-project `<GenerateDocumentationFile>false</GenerateDocumentationFile>` in one of the 17 uncovered test projects would silently exempt it without failing any test. None exists today; I enumerated every `.csproj`, `.props` and `.targets` and `Directory.Build.props:41` is the only setting of that property in the repository.
