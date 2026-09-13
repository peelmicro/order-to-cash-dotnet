# review: Slice A of phase 14's guard-hardening loop — id 67 (`design_time_dbcontext_factory_env_reads_are_unguarded`) and id 68 (`composition_root_delegation_and_wiring_are_unguarded`)

## Verdict

**Id 67 — APPROVED** (conditional on the effort record below being appended before close).
**Id 68 — REJECTED.** Two blocking defects, both in `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`, both of the class this loop exists to retire: *a guard whose assertion cannot detect the defect it names*.

The two ids are separable: id 67's artefacts are four `*DbContextFactoryTests.cs` files plus four collection definitions, none of which id 68's defects touch. The shared implementation record, however, is one document, so the fix round rewrites it for both.

Recommended transitions — **for the leader to make; I edited no `feature_list.json`**:

- **id 67: `in_review` → `done`**, once `progress/history.md` carries the entry with its effort record (sessions + wall-clock). That record does not exist yet and the implementation record supplies no data to build it from; `CHECKPOINTS.md` C5 and `CLAUDE.md` both make it a close condition, so it is a gate on closing, not a defect in the code.
- **id 68: `pending` → `in_progress`** for a fix round. Do not advance it to `in_review`/`done`.

## What I verified myself, and what I took on the leader's word

Re-run in full: nothing. The claim under test was never about the full suite, so per `CLAUDE.md` I probed claims instead. I ran **five mutations** of my own (one of them a defeat probe the implementer never attempted), plus two independent enumerations. I did **not** re-run `./quality.sh` and did **not** re-verify the 1880 → 1905 count reconciliation, the bounds cleanliness, or the `IDesignTimeDbContextFactory` typing — the leader verified those independently with commands and I found no reason to doubt them. `Gateway.IntegrationTests` was never run, so backlog id 85's port race did not arise.

## The decisive finding — id 68's guard is defeatable, and passes while the wiring is wrong

`ExtractNamedArgument` (`tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs:207-212`) runs `Regex.Match` over the **whole file text**, comments included, and takes the **first** match. The leader's own probe established that no `configure…:` token sits in a comment *today* — but that is a property of the files, not of the guard. I tested the guard.

Mutation applied to `src/Billing/Program.cs`: one comment line added above the call, and the live argument replaced with P17's exact no-op.

```csharp
// wiring note: configure: BillingProgramConfiguration.Configure
var builder = BillingHost.CreateBuilder(
    args,
    configure: static _ => { },
```

`dotnet build tests/Architecture.Tests --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`. Then:

```
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~BillingProgramCs_Delegates"
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 8 ms
```

**The guard is green while Billing's `configure:` argument is a live no-op** — that is P17, the measured defect id 68 was filed to close, surviving the guard written to close it. Restored from the `cp` backup, `cmp` identical, `git diff` empty, line re-read, forced `--no-incremental` rebuild, `CompositionRootDelegationWiringTests` 9/9 green.

Why this is blocking rather than a noted brittleness:

- **The defeating edit looks like documentation.** Every `Program.cs` in this repository already carries a header comment explaining what its delegate does — `src/Billing/Program.cs:4-12` and `src/Gateway/Program.cs:3-8` are exactly that, and `src/Gateway/Program.cs:5` already contains the words *"configure delegate itself lives in GatewayProgramConfiguration.Configure"*, one colon away from disarming the Gateway test. The next person who writes a wiring note in this house style disarms the guard and nothing anywhere says so.
- **It is undisclosed.** `grep -in "regex\|first match\|comment\|defeat" progress/impl_composition_root_delegation_and_design_time_factories.md` returns **zero hits**. The record presents the mechanism as sound without naming its one failure mode.
- **The fix is small.** Strip comments before matching, or require `Regex.Matches(...)` to yield exactly one hit per argument name, or match only on the line bearing the call. Any of the three keeps the mechanism and removes the defeat.

The opposite direction — failing while the wiring is right — also exists and is milder: the capture class `[A-Za-z0-9_.]+` swallows dots, so writing the target fully qualified (`OrderToCash.Billing.BillingProgramConfiguration.Configure`) fails the guard on correct wiring. That is a loud, immediate false red and I do not treat it as blocking, but it belongs in the record.

## Second blocking defect — the population meta-test restates the table it counts

`ThePopulationTableHoldsExactlyNineteenDelegatingArguments` (`:113-118`) sums the literal `_delegatingArguments` array and asserts the sum is `19`. It performs **no filesystem access at all**. Its own doc comment (`:102-112`) claims the opposite:

> *"If a future edit adds or removes a delegating argument anywhere and this table is not updated, THIS assertion — not a silent gap — is what fails."*

It cannot. A delegating argument added to any `Program.cs`, or an eighth service arriving with its own `Program.cs`, leaves the literal table untouched, the sum at 19, and the suite green — the precise silent gap the comment promises to prevent. The assertion can only fail if someone edits the table *and* forgets the constant, which is a typo check on the test's own literal, not a guard on the tree. This is the ledger-row pathology one level up: a countable claim whose named guard cannot fail for the reason it states, and the false claim is in a doc comment, which is how #9 will read the question as settled.

Two mitigations for the fix round, in order of preference: derive the table from disk (glob `src/*/Program.cs`, extract every `configure…:` argument, assert the derived set equals the literal expectation set — making the literal the *expected* set and the tree the *population*, which is `CLAUDE.md`'s own prescription for sweeps), or at minimum delete the false claim. The first also closes the "new service" gap outright.

What the table *does* catch, verified by reading the host signatures: `configureTelemetry` and `configureHealth` are optional (`= null`) on all five `*Host.CreateBuilder` overloads, so dropping one from a `Program.cs` compiles and silently disables telemetry or health probes — and `ExtractNamedArgument`'s `match.Success` assertion fails that by name. That is real value and the fix round should not lose it.

## Population (id 68 bullet 1) — re-derived independently, and it is correct

Unit: a **delegating argument** — an identifier bound to a `configureX:` named parameter inside a `Program.cs`. Not a line, not a file.

```
find . -name 'Program.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n 'configure[A-Za-z]*:'
```

19 hits: Billing 3, Gateway **2**, Fulfillment 3, Projector 3, Notifications 3, Orders 5 (`configureOutbox`/`configureAcceptance`/`configureSaga`/`configureTelemetry`/`configureHealth`, no plain `configure:`), Seed 0. Seven `Program.cs` files exist in total. **This matches the record's table and the test's literal table argument-for-argument.** The record was right to refuse the filed figure of ten.

One correction for the leader's own dispatch note, which the implementer caught and which should not propagate: the corrected bullet 1 says Gateway's count of 1 is a line-counting artefact because `src/Gateway/Program.cs:9` *"packs `configure:`, `configureTelemetry:` and `configureHealth:` onto one line"*. It packs **two**, and `GatewayHost.Build` (`src/Gateway/GatewayHost.cs:150`) has **no `configureHealth` parameter at all**. The record states this correctly at `:52-57`; the backlog entry's note is still wrong on that detail.

**A structural point worth carrying to #9**, which neither the entry nor the record states: within a single `Program.cs`, cross-wiring one configuration method into another's slot is a **compile error**, because every delegate parameter has a distinct options type (`Action<BillingOptions>`, `Action<TelemetryOptions>`, `Action<HealthOptions>`; Orders adds `OrdersOutboxOptions`/`OrdersAcceptanceOptions`/`OrdersSagaOptions`). Cross-service substitution is impossible too — no service project references another. So for this population the *substitution* family is supplied by the type system, and the only compilable wrong wiring is the no-op/lambda and the dropped optional argument. That is why the no-op mutation is the right one here, and it is a genuine ported-property observation the record should make rather than leave implicit.

## Id 67 — the 20 reads, and whether rotation-sampling is adequate

Counted mechanically: `grep -c 'Environment.GetEnvironmentVariable'` returns **5** for each of the four `*DbContextFactory.cs` files. 20 reads, confirmed.

**The sampling is adequate, and it is adequate structurally rather than by luck.** Each of the five reads per service lands in a *distinct asserted substring*: `Data Source=<host>,<port>` (host and port are separately distinguishable because the test sets `sql-box` and `14330`), `Initial Catalog=…`, `User ID=…`, `Password=…` in `CreateDbContext_ReadsEveryVariable_…`, and the throw in `CreateDbContext_Throws_WhenMsSqlAppPasswordIsNotSet`. Deleting any one of the 20 therefore changes an assertion that names it. The four files are structurally identical, which I confirmed by reading all four in full.

I did not rely on that reasoning alone. I ran a **combination the implementer never armed** — the arming table rotates host→Orders, port→Billing, user→Fulfillment, password→Notifications, so *Notifications × host deletion* is untested:

```
var host = "localhost";   // MSSQL_HOST read deleted
→ NotificationsDbContextFactoryTests.CreateDbContext_ReadsEveryVariable_WhenAllAreSetToNonDefaultValues [FAIL]
  Assert.Contains() Failure: Sub-string not found
  String:    "Data Source=localhost,14330;Initial Catal"···
  Not found: "Data Source=sql-box,14330"
```

RED, for the right reason. I also re-ran arming table entry #8, the substitution family:

```
Environment.GetEnvironmentVariable("MSSQL_DB_ORDERS") ?? "otc_notifications"   // sibling key substituted
→ CreateDbContext_ReadsMsSqlDbNotifications_FromItsOwnDistinctVariableName_NeverASiblingsKey [FAIL]
  Not found: "Initial Catalog=custom_notifications_db"
```

Verbatim match to the record. Critically, this fails on the **name** reason and not `CLAUDE.md`'s named false-negative: the test sets all four `MSSQL_DB_*` siblings to distinct non-default values first, so the swapped read cannot fall back to a default. Both restored (`cmp` identical, `git diff` empty, line re-read, forced rebuild, `NotificationsDbContextFactoryTests` 4/4 green).

Bullet 2 holds on inspection: `Factory` is typed `IDesignTimeDbContextFactory<TContext>` (`OrdersDbContextFactoryTests.cs:34`) and every test goes through `CreateDbContext(args)`, so the call resolves the way `dotnet ef`'s reflection does, not through a shared helper.

## Mutation #16 — the load-bearing cross-check, reproduced live

This is the proof that the new guard, and not `Seed.UnitTests`, closes R2-5. I reproduced both halves under one mutation of `src/Seed/Presentation/SeedRunner.cs:26`:

```
await using var ordersDb = OrdersSeedWriter.OpenDb(billingConnectionString);

→ CompositionRootDelegationWiringTests.SeedRunner_OpensEachWritersDbContext_WithThatWritersOwnConnectionString_NeverASiblings [FAIL]
  OrdersSeedWriter.OpenDb(...) is called with 'billingConnectionString', expected its OWN
  'ordersConnectionString' — a sibling's connection string was substituted.

→ dotnet test tests/Seed.UnitTests
  Passed!  - Failed: 0, Passed: 44, Skipped: 0, Total: 44
```

Exactly as recorded, and exactly R2-5's measurement. Restored, `cmp` identical, `git diff` empty, lines 26-28 re-read, forced rebuild, 9/9 green. **This part of id 68 is genuinely good work** — it converts a residual that two assessments' worth of prose had deferred into a guard that fails by name, and the failure message names the substituted identifier rather than an incidental reason (bullet 4 satisfied for this site). The rejection is not about this test.

## "What was not done" — judged

- **Declining the boot smoke test** (`WebApplicationFactory`/`TestServer`): **a real constraint, correctly routed.** `tests/Gateway.IntegrationTests/GatewayTestHost.cs:8-18` records the standing decision against that transport in terms, and id 56's review called reversing it a gate call. Deferring to the gate rather than reversing it inside a backlog entry is right.
- **Declining reflection over the compiled `Program` type**: **a real constraint.** Id 56's review verified by reflection that top-level statements compile to `Program.<Main>$`, with the lambda at `Program+<>c.<<Main>$>b__0_0` — names no C# source can express. The record's reason for preferring source text (reflection catches the inline-lambda reversion but not a different named delegate) is accurate as far as it goes, though as established above the compiler already makes the named-delegate substitution impossible here, so the two mechanisms are closer in power than the record claims. Not a defect; a reason that is weaker than stated.
- **No `[CollectionDefinition]` for the new Architecture test**: correct — it mutates no environment variable.
- **Not attempted, and it should have been: a defeat probe against its own mechanism.** A guard that asserts source text has one characteristic failure mode — text that is not code — and the record neither probes it nor names it. That omission is the root of both blocking defects.

## Ported-idiom ledger

**None owed, and the record should say so in one line rather than being silent.** Both entries guard constructs that have no #7 counterpart: `IDesignTimeDbContextFactory` is EF Core tooling #7 never had, and the `Program.cs` delegation shape exists only because #8's top-level statements are unreachable where #7's `main.ts` was an importable module. That property difference is already carried as a ledger row in id 56's record, where the port actually happened. `grep -in "ledger\|#7"` on this record returns zero hits; the fix round should add the single sentence stating that nothing is ported here and why.

## `R<n>` → test mapping

Neither id is `sdd: true` and neither claims an `R<n>`. `specs/shared/test-matrix.md` is correctly untouched. Verified: no new or changed row, and no `R<n>` referenced anywhere in the new test files.

## `CHECKPOINTS.md` — boxes walked

C1, C6 and C7 are not applicable to this slice (no harness change, no `sdd: true` feature, no `specs/shared/` or n8n/API-script change; `specs/shared/` correctly untouched).

- [x] **C2** — at most one `in_progress`; statuses valid; `feature_list.json` untouched by the implementer (leader-verified, `git diff --stat` empty).
- [x] **C3** — no architecture surface touched: no new shared runtime code, no cross-service DB access, no domain reference added. The new Architecture test reads source text and asserts nothing about layering.
- [x] **C3** — no stray debug logging or context-free TODO introduced.
- [x] **C4** — integration suites untouched; the new tests are pure unit/architecture tests with no mocked broker.
- [ ] **C4** — `./quality.sh` not run by me (deliberately, per the brief; the leader verified the 1905 count by name-level reconciliation). Not a defect, recorded so the reader can tell verification from assumption.
- [x] **C4** — no Jest anywhere; xUnit throughout.
- [x] **C5** — no suspicious untracked files; the 9 new test files are all legitimate and named for their features.
- [ ] **C5** — `progress/history.md` has **no entry** for this slice, and therefore **no effort record**. The implementation record supplies no sessions or wall-clock figures either (`grep -in "effort|wall-clock"` → nothing usable). **This blocks closing id 67 as much as id 68's defects block id 68.**
- [x] **C5** — `feature_list.json` reflects true state and was not written by implementer or reviewer.
- [x] **C5** — Claude did not commit. I ran no git command that writes the index or working tree; every restore was `cp` from a backup, per `CLAUDE.md:78-88`, which I read on disk before probing.

## Defects, with file and line

| # | Severity | Location | Defect | Why it matters |
|---|---|---|---|---|
| D1 | **BLOCKING** | `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs:209` | `Regex.Match` scans the whole file and takes the first hit, so a comment, string literal or doc-comment line containing `configure: <Target>` satisfies the assertion while the live argument is a no-op. **Measured: guard green, 1/1, with `configure: static _ => { }` live in `src/Billing/Program.cs`.** | It is P17 — the exact defect id 68 exists to close — surviving the guard. The defeating edit is a wiring-note comment in the house style already present in every `Program.cs`, and `src/Gateway/Program.cs:5` is one colon short of it today. Undisclosed in the record. |
| D2 | **BLOCKING** | `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs:102-118` | `ThePopulationTableHoldsExactlyNineteenDelegatingArguments` sums a literal and compares it to a literal, with no filesystem access; its doc comment claims it fails when the tree and table diverge. It cannot. | A guard whose assertion cannot detect the defect it names — this loop's own theme — with the false claim in a doc comment, where #9 will inherit it as settled. A new service, or a new delegating argument, is silently uncovered. |
| D3 | Minor | same file, `:209` | Capture class `[A-Za-z0-9_.]+` makes a fully-qualified target (`OrderToCash.Billing.BillingProgramConfiguration.Configure`) fail the guard on correct wiring. | False red, loud and immediate. Worth one sentence in the record, not a fix. |
| D4 | Record | `feature_list.json` id 68, acceptance bullet 1 | The leader's correction says Gateway's `Program.cs:9` packs three arguments including `configureHealth:`. It packs two, and `GatewayHost.Build` has no `configureHealth` parameter. | The implementer got this right; the entry is still wrong, and entries outlive dispatch notes. |
| D5 | Close condition | `progress/history.md`; `progress/impl_composition_root_delegation_and_design_time_factories.md` | No history entry and no effort record for either id; the implementation record carries no sessions/wall-clock data to build one from. | `CHECKPOINTS.md` C5 and `CLAUDE.md` make the effort record the point of this repository. Blocks closing id 67. |
| D6 | Record | implementation record, throughout | No ledger statement (zero mentions of a ledger or of #7). | The correct answer is "none owed", but silence is indistinguishable from omission, and this class has a 0-for-3 detection record here. |

## What must change before re-review (id 68)

1. **Fix D1.** Make `ExtractNamedArgument` match code rather than text: strip comments first, or require exactly one match per argument name, or anchor to the call's own line. Then **arm it with my probe** — comment above, no-op live — and record the verbatim failure. That probe belongs in the arming table permanently; it is the one mutation family a source-text guard is uniquely vulnerable to.
2. **Fix D2.** Derive the population from `src/*/Program.cs` at test time and compare the derived set against the literal expectation set, so the tree is the population and the table is the expectation. Arm it by adding a delegating argument the table does not list and showing the test go red. If the derivation is judged too costly, delete the false doc-comment claim instead — but the first option is what bullet 1's *"every delegating call site it finds is covered"* actually asks for over time.
3. **Disclose D3** in one sentence, and state the type-system observation: within a `Program.cs` the substitution family is prevented by the compiler, so the no-op and dropped-argument families are the whole of what a guard must catch here.
4. **Correct D4** in the backlog entry (leader), and **add D6's one-line ledger statement**.
5. **Supply the effort data (D5)** for both ids so the history entry can be written honestly at close.

Not required, and explicitly not asked for: no change to id 67's four test files, no change to the `SeedRunner` pairing guard, and no reversal of the `WebApplicationFactory` decision — that remains a gate call.

## Final state of the tree

Nothing mutated, nothing alive. All five of my mutations were restored from `cp` backups and confirmed three ways (`cmp` against backup, empty `git diff` on the tracked file, and re-reading the changed line), each followed by a forced `--no-incremental` rebuild and a green run. `git status --porcelain` over all four `*DbContextFactory.cs`, all seven `Program.cs` and `SeedRunner.cs` returns **empty**. `pgrep -fl "dotnet (build|test|format)"` returns no build, test or format process.

---

# Re-review (round 2) — id 68 only; id 67 unchanged

## Verdict

**Id 68 — REJECTED again.** D1 and D2 are genuinely fixed, and I verified both independently. But the fix's own doc comments make **two explicit claims about the new mechanism that I disproved by measurement**, and the failure they mis-describe is a *silent* false green on exactly the family this guard exists to catch. A third, separate defect: the record's D6 ledger statement is wrong about #7 on both of its halves, checked against #7's checkout with file and line.

**Id 67 — still APPROVED on the code** (untouched this round: I re-ran nothing of it and it needed nothing). Its close is now gated on **two** record conditions rather than one: the effort record (round 1's D5, now supplied and adequate) **and** the corrected ledger statement (D10 below), because id 67's history entry will be written from this record and the ledger sentence in it is false.

Recommended transitions — **for the leader to make; I edited no `feature_list.json`**:

- **id 68: stays `in_progress`** for a third fix round. It is already `in_progress`; no transition needed.
- **id 67: `in_review` → `done`** once `progress/history.md` carries its entry with the effort record **and** D10's ledger correction has landed in the shared implementation record. Both are record work, not code work, and neither touches id 68's test file.

## What I ran, and what I did not

Re-run in full: nothing. Per `CLAUDE.md` I probed the claims of this round. **Eleven mutations of my own**, ten of them new, each with its own `--no-incremental` build, one build at a time, nothing backgrounded. I did not run `./quality.sh`, did not re-run the 1905-test reconciliation, and never touched `Gateway.IntegrationTests`, so backlog id 85's port race did not arise. Baseline before probing: `Architecture.Tests` **35/35 green**, build 12 s.

## 1. The load-bearing check — my round-1 defeating edit now goes RED

Reproduced verbatim on `src/Billing/Program.cs`: the wiring-note comment above the call, and the live `configure:` argument replaced with `static _ => { }`.

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded. 0 Warning(s) 0 Error(s)
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~BillingProgramCs_Delegates"

[FAIL] CompositionRootDelegationWiringTests.BillingProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToBillingProgramConfiguration
  Error Message:
   src/Billing/Program.cs's 'configure:' argument is 'static', expected 'BillingProgramConfiguration.Configure' — the delegating site was repointed or replaced with a no-op.
  at ...CompositionRootDelegationWiringTests.cs:line 283
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 11 ms
```

**Matches the record's quoted message exactly**, names the argument and the file. Restored from the `cp` backup, `cmp` identical, `git diff --stat` empty, lines 13-17 re-read, forced `--no-incremental` rebuild, **35/35 green**. D1's headline defeat is closed.

The comment moved onto the **same line** as the live no-op (`configure: static _ => { }, // configure: BillingProgramConfiguration.Configure`) is also RED, same message. So the realistic, house-style defeat is closed in both placements.

## 2. Defeating the new guard by other means — two vectors, both silent, both measured

The stripper is hand-rolled, so I attacked it as the brief asked. The two that matter share a shape the fix did not anticipate: **the defeat does not need to out-vote a live argument, it only needs to replace an ABSENT one.** `configureTelemetry` and `configureHealth` are optional (`= null`) on every `*Host.CreateBuilder` overload, so dropping one compiles and silently disables telemetry or health probes — the review's own round-1 paragraph at `:59` names this as the value the table adds. When the argument is dropped, the surviving text is the *only* match, so `matches.Count == 1` is satisfied and the assertion passes on the dead text.

**P2b — a disabled preprocessor region.** `src/Billing/Program.cs` with `configureHealth:` removed from the live call, and above it:

```csharp
#if false
    configureHealth: BillingProgramConfiguration.ConfigureHealth,
#endif
var builder = BillingHost.CreateBuilder(
    args,
    configure: BillingProgramConfiguration.Configure,
    configureTelemetry: BillingProgramConfiguration.ConfigureTelemetry);
```

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded
dotnet test ... --filter "FullyQualifiedName~CompositionRootDelegationWiringTests"
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 37 ms
```

**All nine green with Billing's health probes unwired.** The population meta-test passes too, because it derives argument *names* from the same stripped text and finds `configureHealth` in the dead region.

**P3b — a raw string literal containing an embedded quote.** Same dropped argument, and instead of the `#if`:

```csharp
var note = """wiring: "configureHealth: BillingProgramConfiguration.ConfigureHealth" is wired.""";
Console.WriteLine(note.Length);
```

```
Build succeeded → Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 33 ms
```

**All nine green again**, by a second, independent mechanism: the scanner treats `"""` as an empty literal followed by a new one, so the text between the inner quotes is emitted as code.

Both restored; `cmp` identical, `git status --porcelain` empty on both files, forced rebuild, **35/35 green**.

**Why this is blocking rather than a noted brittleness.** Not because the constructs are likely — neither appears in any of the eight files this class parses (enumerated below) — but because **the file states in terms that they cannot happen**, and a reader, including #9, will believe it:

- `CompositionRootDelegationWiringTests.cs:68-71`: *"requires EXACTLY one surviving match rather than taking the first — so a defeating comment either disappears before matching, or (if it somehow survived stripping) produces a second match and fails loudly rather than silently winning."* **False.** With the argument dropped there is no second match to produce, and the dead text wins silently. P2b and P3b are direct disproofs.
- `:360-364`: *"if one is ever introduced the scanner fails LOUDLY — a broken downstream match — never silently."* **False**, and the disclosure is also incomplete: it names only a quote nested inside an interpolation, and says nothing about raw strings or preprocessor directives.

That is round-1 D2's pathology relocated: a claim in a doc comment that cannot fail, asserting a safety property the code does not have. This loop exists to retire exactly that.

**The fix is small and makes the existing claims true rather than softening them.** Enumerated, the eight files this class parses contain **no `#if`/`#else`/`#endif`, no `"""`, no `@"`, and no interpolation with a nested quote**:

```
find src -name 'Program.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n '#if\|#else\|#endif\|"""\|\$"\|@"'
src/Seed/Program.cs:12:    Console.WriteLine($"  orders:       currencies={summary.Orders.Currencies} products={summary.Orders.Products} " +
src/Seed/Program.cs:13-21: (nine further $"..." lines of the same shape, no nested quote in any)
src/Seed/Program.cs:27:    Console.Error.WriteLine($"[seed] FAILED: {ex}");

grep -n '#if\|#else\|#endif\|"""\|\$"\|@"' src/Seed/Presentation/SeedRunner.cs   → no hits (exit 1)
```

Classification: eleven hits, all in `src/Seed/Program.cs`, all plain interpolated strings with no nested quote, none containing a `configure…:` token — the record's *"checked by hand at arming time"* claim holds for the files as they stand. So a **precondition assertion** — fail the test if the stripped source of any file it reads contains `#if`, `#elif`, `#else`, `#endif` or `"""` — costs about four lines, is green today, and converts both silent defeats into the loud failure the doc comment already promises. Note that raw strings are already house idiom elsewhere in `src/` (nine files, all EF Core repositories), so this is not a hypothetical construct, only one that has not reached a `Program.cs` yet.

## 3. The exactly-one rule from the other side — a false red, measured and judged

Two **legitimate, correctly-wired** call sites in one file (a second `BillingHost.CreateBuilder` under `if (args.Contains("--probe"))`, all three arguments correct) fails the guard:

```
[FAIL] BillingProgramCs_Delegates...
   Found 2 occurrences of a 'configure:' named argument in src/Billing/Program.cs after stripping comments and literals — expected exactly one live call site.
```

**Judged acceptable, and not blocking.** It is loud, immediate, names the file and the argument, and the shape it rejects — two host-builder call sites in one `Program.cs` — is not one this repository has or wants. It is the correct trade against the silent defeat it prevents. But it is **undisclosed**: the file discloses only the fully-qualified-target false red (D3), and this one belongs in the same sentence. Minor, D8 below.

**No false red from innocent constructs.** With correct wiring plus a char literal `'"'`, a string containing `see http://example/docs // configure: BillingProgramConfiguration.Configure`, and an interpolated string built from both, the class is **9/9 green** — the stripper handles `//` inside a literal, a quote inside a char literal, and plain interpolation correctly, and the literal's `configure:` token does not become a second match.

## 4. D2 as an instrument — it reads the tree, and both arming claims reproduce

Confirmed by mutation, not by reading: `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk` genuinely derives both the file set and each file's argument names from disk.

**Argument-name direction** (my own probe, on real compiling code — dropping the optional `configureHealth:` from Billing with no dead text anywhere):

```
[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
   src/Billing/Program.cs: the table expects [configure, configureHealth, configureTelemetry], the source on disk has [configure, configureTelemetry] — a delegating argument was added, removed or renamed without updating this test's table.
[FAIL] BillingProgramCs_Delegates...
   Could not find a 'configureHealth:' named argument in src/Billing/Program.cs (comments and string/char literals stripped before matching).
```

Both tests fire, by name. This is also the answer to *"does commenting the argument out defeat it?"* — no: the comment is stripped, the argument is then absent, and this is the failure you get.

**The record's shape 1, reproduced verbatim** (`Noop(configureAudit: static _ => { });` plus its local function appended to `src/Gateway/Program.cs`): RED, message identical to the record's quote, naming the file and `configureAudit`.

**The record's shape 2, reproduced** — the artefact no longer exists, so I recreated it: `src/NewProbeService/Program.cs` containing `Console.WriteLine("probe");`, run with `--no-build`:

```
[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
   The set of Program.cs files on disk no longer matches the table's expectation — expected [src/Billing/Program.cs, src/Fulfillment/Program.cs, src/Gateway/Program.cs, src/Notifications/Program.cs, src/Orders/Program.cs, src/Projector/Program.cs, src/Seed/Program.cs], found [src/Billing/Program.cs, src/Fulfillment/Program.cs, src/Gateway/Program.cs, src/NewProbeService/Program.cs, src/Notifications/Program.cs, src/Orders/Program.cs, src/Projector/Program.cs, src/Seed/Program.cs]. A service was added or removed without updating this test's table.
```

`rm -rf src/NewProbeService` afterwards; `find src -name 'Program.cs'` returns the seven real files.

**One record defect falls out of this.** The record quotes shape 2's failure as containing *"(7 files)"* and *"(8 files)"*. The real message contains no such parentheticals — the counts are the record's own gloss inside a block presented as verbatim output. The elisions are marked with `...` and are fine; the fabricated counts are not. Minor, D9 below, but the arming protocol's word is *verbatim*, and a reader re-running the probe will not reproduce that text.

## 5. The `bin`/`obj` hazard — measured; I agree with the leader's classification

`Directory.GetFiles(srcRoot, "Program.cs", SearchOption.AllDirectories)` (`:163-164`) carries no path exclusion. Measured rather than reasoned: I placed a copy at `src/Billing/bin/Debug/net10.0/publish/Program.cs` and ran the population test with `--no-build`:

```
[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
   ... found [src/Billing/Program.cs, src/Billing/bin/Debug/net10.0/publish/Program.cs, src/Fulfillment/Program.cs, ...]
```

**Agreed: a false-red hazard, not a false-green one.** A build directory can only *add* entries to the discovered set; it cannot remove a real file or mask a wrong argument, so no defect can hide behind it. Deleted afterwards; `find src -name 'Program.cs'` is back to seven.

**On whether to fix now or file: fix now**, because the file is being reopened for §2 anyway and the fix is one `.Where(...)` clause. `CLAUDE.md` requires exclusion by path at the source, this repository has already paid for the `grep -v '/bin/'` version of this mistake, and `src/Seed` is a console project one `dotnet publish` away from tripping it. If the leader prefers, filing it is defensible — it is advisory on its own merits, and I would not have rejected for it alone.

## 6. D3, D5, D6 — judged

- **D3 — adequate.** Disclosed at `:76-81` in the class doc comment, correctly characterised as a loud false red. As round 1 asked. The type-system observation (`:82-91`) is also carried, accurately.
- **D5 — adequate, and honest.** One session; an evidenced **floor** of ≥38 minutes (13:42:51 → 14:20:12) with an explicit refusal to estimate the arming and verification time that left no artefact, and an explicit statement that id 67 and id 68 are not separable within the window. I concur with the leader: sufficient to write id 67's history entry, provided the entry carries the same bound rather than presenting ≥38 minutes as a total. The round-2 figure (≈11–15 minutes, upper bound approximate because the record was being written) is soft but honestly labelled.
- **D6 — NOT adequate. This is the round's second blocking defect**, and it is the class with the 0-for-3 detection record here.

## 7. D10 — the ledger statement is wrong about #7, on both halves

The record's D6 paragraph (`:496-509`) concludes *"none owed"* and gives two reasons. I checked both against #7's checkout, with file and line, as `CLAUDE.md` requires of any *"#7 relied on X"* claim.

**Reason 1 — *"#7 ... had no design-time factory concept at all (`IDesignTimeDbContextFactory<T>` is EF Core tooling unique to this stack)"*. False.** #7 has four of them:

```
find . -path ./node_modules -prune -o -name 'drizzle.config.ts' -print
./apps/orders/drizzle.config.ts
./apps/billing/drizzle.config.ts
./apps/fulfillment/drizzle.config.ts
./apps/notifications/drizzle.config.ts
```

`apps/billing/drizzle.config.ts:16-22` is a design-time configuration consumed by the migration CLI that reads **five** database environment variables with defaults — host, port, user, password, and a per-service database name from a sibling family (`MYSQL_DB_BILLING`). `src/Billing/Infrastructure/Persistence/BillingDbContextFactory.cs:22-34` reads the same five roles, with defaults, including the sibling-family key `MSSQL_DB_BILLING`. Four files each side, same purpose, same five reads. That is a ported mechanism by any reading, and id 67 is the feature that guards it.

**Reason 2 — *"#7's composition root was an importable `main.ts` module a test could call directly"*. False for five of six services.** `apps/billing/src/main.ts:13` declares `async function bootstrap()` with **no `export`**, and `:48` is a bare `void bootstrap();`. The same shape holds at `apps/gateway/src/main.ts:35`, `apps/fulfillment/src/main.ts:47`, `apps/notifications/src/main.ts:67` and `apps/projector/src/main.ts:85`. The single exception is `apps/orders/src/main.ts`, which exports a helper and guards its own bootstrap — and its comment at `:125` says why in terms: *"G6's export means this module can now be `import`ed (not just executed)"*, i.e. #7 had to **engineer** that property for one service precisely because it was not there by default. The record asserts as a general property of #7 something #7 built once, deliberately, and documented as an exception.

This is the failure mode `CLAUDE.md` names exactly: the guard half of a ledger row fails loudly if it is wrong, **the history half cannot fail at all**, nothing executes it, and it is the half #9 inherits and has no reason to re-derive. Round 1's own ledger paragraph (`:127-129`) reached the same wrong conclusion by the same route — from an assumption about the other stack rather than from its source — so this correction is against my own text as much as the implementer's, and I record that rather than leave it implicit.

**What the corrected row should say**, since the answer is now in hand and is more interesting than *"none owed"*:

> #7 relied on `apps/<service>/drizzle.config.ts` (e.g. `apps/billing/drizzle.config.ts:16-22`) to read five DB env vars at design time for `drizzle-kit generate`, and guarded it with **nothing** — `find . -name '*.spec.ts' -print0 | xargs -0 grep -ln "drizzle.config"` returns no hits. In #8 that property is supplied by `*DbContextFactory.cs`'s five reads per service, and the guard #7 never had is id 67's four `*DbContextFactoryTests.cs` (20 reads, rotation-armed, including a sibling-key substitution probe).

That is a row recording a **strengthening**, which is worth carrying to #9 far more than a bare *"none owed"*. Id 68's own delegation guard remains genuinely unported — a `Program.cs` top-level-statement composition root has no #7 counterpart — and the corrected paragraph should say that separately, with its reason stated as what #7's `main.ts` actually does, not what NestJS would plausibly have done.

## `R<n>` → test mapping

Unchanged from round 1 and re-confirmed: neither id is `sdd: true`, neither claims an `R<n>`, `specs/shared/test-matrix.md` is untouched, and no `R<n>` appears in either id's test files. Nothing to map.

## `CHECKPOINTS.md` — boxes walked this round

C1, C6 and C7 remain not applicable (no harness change, no `sdd: true` feature, `specs/shared/` untouched).

- [x] **C2** — at most one `in_progress` (id 68); id 67 `in_review`; statuses valid; `feature_list.json` untouched by the implementer and by me.
- [x] **C3** — no architecture surface touched this round: the only changed file is a test that reads source text. No shared runtime code, no cross-service DB access, no domain reference.
- [x] **C3** — no stray debug logging or context-free TODO introduced.
- [x] **C4** — integration suites untouched; the changed test is a pure source-reading unit test with no mocked broker.
- [ ] **C4** — `./quality.sh` not run by me (per the brief). `Architecture.Tests` 35/35 verified by me at baseline and after every restore. Recorded so the reader can tell verification from assumption.
- [x] **C4** — no Jest anywhere; xUnit throughout.
- [x] **C5** — no suspicious untracked files; `src/NewProbeService/` is gone and `find src -name 'Program.cs'` returns exactly seven.
- [ ] **C5** — `progress/history.md` still has **no entry** for either id. Blocks closing id 67 (with D10).
- [x] **C5** — `feature_list.json` reflects true state; not written by implementer or reviewer.
- [x] **C5** — Claude did not commit. I ran no git command that writes the index or working tree; every restore was `cp` from a backup, confirmed by `cmp`, by an empty `git diff` on the tracked file, and by re-reading the changed lines.

## Defects, with file and line

| # | Severity | Location | Defect | Why it matters |
|---|---|---|---|---|
| D7 | **BLOCKING** | `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs:68-71` and `:360-364` | Two stated safety claims are false: *"produces a second match and fails loudly rather than silently winning"* and *"the scanner fails LOUDLY … never silently"*. **Measured: 9/9 green with Billing's `configureHealth` unwired**, twice — once via a `#if false` region, once via a raw string containing an embedded quote. When an OPTIONAL argument is dropped there is no live match to out-vote, so surviving text is the sole match and passes. | A silent false green on the dropped-optional-argument family — the family the review's `:59` identifies as this table's main value, and the one that silently disables telemetry or health probes. The false claim sits in a doc comment, where #9 inherits it as settled. Fix is ~4 lines (reject `#if`/`#elif`/`#else`/`#endif`/`"""` in any parsed file), green today, and makes the existing promise true. |
| D8 | Minor | same file, `:316-318` | The exactly-one rule turns two **legitimate**, correctly-wired call sites in one `Program.cs` into a failure. Measured. | Correct trade and loudly signalled, but undisclosed — `:76-81` discloses only the fully-qualified-target false red. One sentence. |
| D9 | Minor | `progress/impl_…md:455-459` | D2 shape 2's *"verbatim"* failure block contains `(7 files)` and `(8 files)`; the real message has no such text. | The arming protocol's evidence is the verbatim message. A reader re-running the probe gets different text and cannot tell gloss from output. |
| D10 | **BLOCKING** | `progress/impl_…md:496-509` (and round 1's own `:127-129` of this review) | The ledger statement's *"none owed"* rests on two claims about #7 that its source disproves: #7 **has** four design-time env-reading configs (`apps/billing/drizzle.config.ts:16-22`, four services), and its `main.ts` is **not** importable in five of six services (`apps/billing/src/main.ts:13,48`; the one exception, `apps/orders/src/main.ts:125`, documents itself as a deliberate addition). | `CLAUDE.md` binds the ledger to the port and requires the *"#7 relied on X"* half to be read out of #7's checkout with a file and line. A wrong history half cannot fail and is exactly what #9 inherits. A row IS owed for id 67, and its true content — #7 guarded those reads with nothing, #8 guards them with 20 armed assertions — is worth more than the bare denial. |
| D4 | Record (closed) | `feature_list.json` id 68 bullet 1 | Corrected by the leader this round; the table of 19 stands. Re-derived once more this round via the population test's own discovery output. | Closed. |
| D5 | Close condition (satisfied) | `progress/impl_…md:516-577` | Effort data now supplied, honestly bounded. | Satisfied; the history entry must carry the same bound, not a total. |

## What must change before re-review (id 68)

1. **Fix D7.** Add the precondition assertion — any file this class parses whose stripped source still contains `#if`, `#elif`, `#else`, `#endif` or `"""` fails by name — or otherwise close the two vectors. Then **arm it with my two probes verbatim**: drop `configureHealth:` from `src/Billing/Program.cs` and supply it once from a `#if false` region, and once from a raw string with an embedded quote. Both must go RED, with the messages recorded. Correct the two doc-comment sentences so they describe what the code does.
2. **Fix or file the `bin`/`obj` exclusion** at `:163-164`. My recommendation is to fix it in the same pass; it is one clause, and `CLAUDE.md` asks for exclusion by path at the source.
3. **Disclose D8** in the same sentence as D3.
4. **Correct D9's quoted block** to the real message.
5. **Rewrite the ledger statement (D10)** as a row, not a denial, citing `apps/billing/drizzle.config.ts:16-22` and `apps/billing/src/main.ts:13,48` — and state separately, with its true reason, that id 68's delegation guard is unported.

Not required: no change to id 67's four test files, none to the `SeedRunner` pairing guard, none to the population test's derivation logic beyond item 2, and no reversal of the `WebApplicationFactory` decision.

## Final state of the tree

All eleven mutations restored from `cp` backups and confirmed three ways each (`cmp` against the backup, empty `git diff`/`git status --porcelain` on the tracked files, re-reading the changed lines), with a forced `--no-incremental` rebuild and a **35/35** green run after the last restore. `find src -name 'Program.cs'` returns exactly the seven real files; `src/NewProbeService/` does not exist; the probe copy under `src/Billing/bin/.../publish/` is deleted. `git status --porcelain src/ | grep -E "Program|SeedRunner"` is empty. `pgrep -fl "dotnet (build|test|format)"` returns nothing. Two builds were never in flight at once, and nothing was backgrounded.

---

# Re-review (round 3) — id 68 rejected a third time; id 67's D10 gate is met

## Verdict

**Id 68 — REJECTED.** Two more working exploits, both of which **compile**, both of which leave this class **9/9 green with Billing's health probes unwired**. Round 3's `AssertNoUnsupportedConstructs` is a **substring check over five literal spellings**, so it closes round 2's two exploits *as round 2 spelled them* and not the class they belong to. And for the third consecutive round the test file's own doc comment states a safety property the code does not have.

**Id 67 — APPROVED on the code, and its D10 gate is now MET.** I re-checked both halves of the corrected ledger row against #7's checkout myself (below); the row is accurate and its named guard genuinely executes the code the row is about. Its remaining gate is the `progress/history.md` entry, which still does not exist.

Recommended transitions — **for the leader; I made no `feature_list.json` edit**:

- **id 68: stays `in_progress`** (it already is) for a fourth fix round.
- **id 67: stays `in_review`.** Approved on code, D10 satisfied, D11 satisfied. I recommend **not** closing it separately: the record states in terms that id 67's and id 68's effort windows are not separable, so an entry written now must be rewritten when id 68 closes. Land both entries together at id 68's close.

## What I ran, and what I did not

I ran **all ten shapes** of `CLAUDE.md`'s defeat list in this one pass, as the brief required, and did not stop at the first defeat. Nine mutations plus two phantom-file placements, each with its own restore, one build/test at a time, nothing backgrounded. Baseline before probing: `Architecture.Tests` **35/35 green** (`dotnet build tests/Architecture.Tests --no-incremental` → 0 warnings, 0 errors). I did **not** run `./quality.sh`, did not re-run the 1905-test reconciliation, and never touched `Gateway.IntegrationTests`, so backlog id 85's port race did not arise. The record's own round-3 figures (1457 unit + 448 integration = 1905, `Architecture.Tests` 35) reconcile against rounds 1 and 2 and I accept them on that basis rather than re-measuring.

## The ten shapes, one per row

| # | Shape | Ran? | Result |
|---|---|---|---|
| 1 | Delete the behaviour | yes — P-G | **RED.** `SeedRunner.RunAsync()` → `Task.FromResult(0)` in `src/Seed/Program.cs` fails `SeedProgramCs_CallsSeedRunnerRunAsync_TheExtractedOrchestrationMethod`. |
| 2 | Corrupt a supplied field | yes — P-D | **RED.** `configure:` repointed to `static _ => { }` fails by name: *"…'configure:' argument is 'static', expected 'BillingProgramConfiguration.Configure'"*. |
| 3 | Substitute a valid sibling identifier | yes — P-F | **RED.** `OrdersSeedWriter.OpenDb(billingConnectionString)` (compiles; `dotnet build src/Seed` → Build succeeded) fails the pairing guard naming both the wrong and the expected variable. Within a `Program.cs` this family is compiler-prevented — verified by reading the six host signatures: every `configureX` takes a distinct options type. |
| 4 | Shadow the pattern from a comment | yes — P-D | **RED**, same probe as shape 2 with the wiring-note comment above the no-op. Round 1's exploit stays closed. |
| 5 | Hide it in a dead region | yes — **P-A** | **DEFEATED — see D12.** |
| 6 | Hide it in a raw/verbatim string | yes — **P-B** | **DEFEATED — see D13.** |
| 7 | Drop an optional element entirely | yes — P-C | **RED when dropped alone** (both `ThePopulation…` and the Billing test fire, naming the argument) — but shapes 5 and 6 are *only* exploitable because of it, so it is closed against the bare case and open against the combined one. |
| 8 | Compare a literal to a literal | yes — P-H | **RED.** `src/ZProbeService/Program.cs` is discovered and named in the failure. The population genuinely comes off disk. |
| 9 | Premise half goes stale | yes | **DEFECT — part of D13.** `:471-473` claims `ReadSource` fails *"on any of the constructs above"*; the constructs listed at `:460-463` include the nested-interpolation quote, which is **not** rejected. |
| 10 | Build-output copy joins the population | yes — P-E | **Clean.** Copies placed at `src/Billing/bin/Debug/net10.0/publish/Program.cs` **and** `src/Billing/obj/Release/Program.cs` (nine `Program.cs` on disk); 9/9 green, no false red. `IsUnderBuildOutputDirectory` excludes by path **segment**, as claimed. D9 closed. |

None skipped.

## D12 — BLOCKING: `# if false` (one space) compiles and defeats the precondition

`AssertNoUnsupportedConstructs` (`:389`) tests `source.Contains("#if")`. C# permits whitespace between `#` and the directive name, so `# if` is a legal directive that contains no `#if` substring.

`src/Billing/Program.cs`, `configureHealth:` removed from the live call and supplied only from the dead region:

```csharp
# if false
    configureHealth: BillingProgramConfiguration.ConfigureHealth,
# endif
var builder = BillingHost.CreateBuilder(
    args,
    configure: BillingProgramConfiguration.Configure,
    configureTelemetry: BillingProgramConfiguration.ConfigureTelemetry);
```

```
dotnet build src/Billing --no-incremental → Build succeeded. 0 Warning(s) 0 Error(s)
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~CompositionRootDelegationWiringTests"
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 31 ms
```

**Nine green, Billing's health probes unwired**, by the same mechanism round 2 reported, with one space added.

## D13 — BLOCKING: a quote inside an interpolation hole, a construct the file names and does not reject

`StripCommentsAndLiterals`'s doc comment (`:460-463`) lists three constructs it cannot read: a quote nested inside an interpolation, a preprocessor region, and a raw string. The precondition rejects the second and third. It does not reject the first, and `:471-473` nevertheless claims *"`ReadSource` … fails, by name, on any of the constructs above before this method ever sees them."*

Same file, same dropped argument, this line instead of the `#if`:

```csharp
Console.WriteLine($"wiring {"configureHealth: BillingProgramConfiguration.ConfigureHealth".Length}");
```

```
dotnet build src/Billing --no-incremental → Build succeeded. 0 Warning(s) 0 Error(s)
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~CompositionRootDelegationWiringTests"
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 28 ms
```

The scanner closes the string at the hole's opening quote, emits the hole's contents as live code, and reopens on the hole's closing quote. **This construct is not exotic**: `$"{(flag ? "a" : "b")}"` is everyday C#, and `src/Seed/Program.cs` already carries eleven interpolated strings — so the population most likely to acquire one is inside this guard's own population.

## Why these two are one finding, and what must change

The record says the fix was *"armed with both of the reviewer's own defeating constructs, verbatim."* It was, and that is precisely the failure `CLAUDE.md`'s new defeat-list rule names: **the fix was armed against two exploits rather than against the class they came from.** A list of five literal spellings is an enumeration whose membership predicate is the spelling under test — a violation that spells itself differently leaves the population instead of failing it, which is this loop's own theme once more, now inside the precondition written to close it.

So the requirement for round 4 is not *"add `# if` and `$"` to the list."* It is: **the precondition must be complete against the scanner's stated limits, and the doc comment's claim must be true of the code.** Two directions, both cheap, either acceptable:

1. **A positive precondition.** Reject any line whose first non-whitespace character is `#` (one regex, `^[ \t]*#`, catches every directive spelling including `# if`, `#   region` and anything future), and reject any interpolated string that contains a `"` inside a `{…}` hole. Note the second must not be a blanket `$"` rejection: `src/Seed/Program.cs` has eleven plain `$"…"` lines that must stay green — verify that before arming.
2. **Teach the scanner the constructs** and delete the corresponding sentences from the doc comment.

Either way, **arm it against the class, not the two literals**: at minimum `# if`, `#if`, `"""`, `$"…{"…"}…"`, and one construct nobody has used yet. And every sentence in the file asserting what cannot get past the scanner must be re-read against the code, since that sentence has now been false in three consecutive rounds.

## The false red, judged (brief item 1)

Measured, not reasoned. With the wiring **fully correct** and a legitimate `#if DEBUG` / `#endif` pair added to `src/Billing/Program.cs`, two tests fail:

```
[FAIL] ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk
[FAIL] BillingProgramCs_Delegates…
   src/Billing/Program.cs contains '#if', a construct StripCommentsAndLiterals does not understand and could silently misparse … — add explicit support for it in the scanner before it is used in this file …
```

**Judged acceptable, and not a defect.** It is loud, immediate, names the file, the construct and the remedy, and the trade — a false red on a construct no `Program.cs` uses, against a silent false green on the family this guard exists to catch — is the right one. It is also already disclosed in the doc comment. I record it so that round 4's fix is not softened on false-red grounds: widening toward `^[ \t]*#` makes this false red slightly broader and that remains the correct direction.

## Brief item 2 — every other optional element with the same exposure

Unit: an **optional** `configureX` parameter that a `Program.cs` currently passes. Enumerated from the six host signatures, not from the table:

```
grep -n "CreateBuilder(\|Build(" src/*/[A-Z]*Host.cs   → BillingHost:19, FulfillmentHost:19, NotificationsHost:19, ProjectorHost:19, OrdersHost:30, GatewayHost:28 and :150
```

- `configureTelemetry` — optional (`= null`) on **all six**; passed by all six. Exposed.
- `configureHealth` — optional on Billing, Fulfillment, Notifications, Projector, Orders; passed by all five. Exposed. `GatewayHost.Build` has **no** health parameter, so Gateway's absence is structural, not a drop.
- `configure` / `configureOutbox` / `configureAcceptance` / `configureSaga` — **required** parameters. Dropping one does not compile, so only the corrupt and comment-shadow families apply, and both are RED (P-D).

**11 of the 19 table rows are droppable**, and all 11 carry exactly the exposure D12 and D13 exploit on Billing's `configureHealth`. Nothing outside `Program.cs` has it: `SeedRunner`'s three `OpenDb` pairings take required positional arguments, and the Seed direct call takes none.

## D10 — the corrected ledger row, judged as a ledger row

**Accepted.** I checked both halves rather than the citations' existence.

**The *"#7 relied on X"* half — accurate, and I re-derived it.** `apps/billing/drizzle.config.ts:16-22` reads five env vars with defaults, including the sibling-family `MYSQL_DB_BILLING`; the family is real and complete — `grep -n "process.env" apps/*/drizzle.config.ts` returns four files × five reads, each with its own `MYSQL_DB_<SERVICE>` key. The *"guarded with nothing"* half is a search result, and I re-ran it: `find . -path ./node_modules -prune -o -name '*.spec.ts' -print0 | xargs -0 grep -ln "drizzle.config"` → no hits (exit 123). The `main.ts` claim I enumerated across **all six** services rather than accepting the sample: `apps/billing/src/main.ts:13,48`, `fulfillment:13,47`, `gateway:14,35`, `notifications:10,67`, `projector:14,85` are all bare `void bootstrap();`, and `apps/orders/src/main.ts:135-136` alone guards with `require.main === module`, with the explanatory comment at `:125-134`. Five of six, exactly as the row says.

**The guard half — it executes the code the row is about.** This is the half feature 19 got wrong, so I read it rather than counting it. All four `*DbContextFactoryTests.cs` declare `private static IDesignTimeDbContextFactory<T> Factory => new *DbContextFactory();` and go through `Factory.CreateDbContext(args)` — **the same interface `dotnet ef` reflects over** — then assert against `db.Database.GetConnectionString()`. The conversion is read *through* the production mapper, not re-implemented beside it. And Billing's substitution test sets `MSSQL_DB_BILLING` **and all three siblings** to distinct non-default values, so a repointed literal fails on the wrong database name and never on a fallback-to-default reason — `CLAUDE.md`'s named false-negative mode for substitution probes is handled explicitly.

**One nuance, non-blocking, for whoever next touches the row:** it says #8 reads *"the same five roles"*. True at role level, but #7's host key is per-service (`BILLING_DB_HOST`) where #8's is shared (`MSSQL_HOST`) — so the *host* read is a narrower sibling family in #8 than in #7. Half a sentence if the row is edited again; not a defect, and not worth reopening the record for.

**D11 — closed.** The fabricated `(7 files)`/`(8 files)` block is retained, struck through, flagged `CORRECTION`, and the real message sits beside it. I reproduced that message myself in P-H and it matches the corrected text.

## `R<n>` → test mapping

Unchanged and re-confirmed: neither id is `sdd: true`, neither claims an `R<n>`, `specs/shared/test-matrix.md` is untouched by either, and no `R<n>` appears in either id's test files. Nothing to map.

## `CHECKPOINTS.md` — boxes walked this round

C1, C6 and C7 remain not applicable (no harness change, no `sdd: true` feature, `specs/shared/` untouched).

- [x] **C2** — at most one `in_progress` (id 68); id 67 `in_review`; statuses valid; `feature_list.json` untouched by the implementer and by me.
- [x] **C3** — no architecture surface touched: the only changed file is a test that reads source text. No shared runtime code, no cross-service DB access, no domain reference, no stray debug logging or context-free TODO.
- [x] **C4** — integration suites untouched; the changed test is a pure source-reading unit test with no mocked broker; no Jest anywhere.
- [ ] **C4** — `./quality.sh` not run by me (per the brief). `Architecture.Tests` **35/35** verified by me at baseline and after the final restore. Recorded so the reader can tell verification from assumption.
- [x] **C5** — no suspicious untracked files; `src/ZProbeService/` and both phantom build-output copies are gone; `find src -name 'Program.cs'` returns exactly **7**.
- [ ] **C5** — `progress/history.md` has **no entry** for either id. Blocks closing id 67.
- [x] **C5** — `feature_list.json` reflects true state; not written by implementer or reviewer this round.
- [x] **C5** — Claude did not commit. No git command that writes the index or working tree was run; every restore was `cp` from a backup, confirmed by `cmp` **and** by an empty `git diff --stat` on the tracked file.

## Defects, with file and line

| # | Severity | Location | Defect | Why it matters |
|---|---|---|---|---|
| D12 | **BLOCKING** | `CompositionRootDelegationWiringTests.cs:389` | `AssertNoUnsupportedConstructs` matches the literal `"#if"`. `# if false` / `# endif` (one space, legal C#) compiles and passes the precondition; with `configureHealth:` dropped from the live call, **9/9 green with Billing's health probes unwired**. | The round-2 exploit is closed only in the spelling round 2 used. A precondition whose membership test is the spelling under test cannot fail on a differently-spelled violation — the loop's own theme, inside the guard written to close it. |
| D13 | **BLOCKING** | same file, `:389` and `:471-473` (claim), `:460-463` (the list it refers to) | A quote nested in an interpolation hole is named by the file as unreadable by the scanner and is **not** in the rejected list, while `:471-473` claims `ReadSource` fails *"on any of the constructs above"*. Measured: **9/9 green**, argument unwired, with a line that compiles and uses an everyday C# idiom. | Third consecutive round in which a doc-comment safety claim is false by measurement, and the first in which the file **names the exact construct** that defeats it two paragraphs above the claim. #9 inherits the sentence, not the measurement. |
| D14 | Advisory | `progress/impl_…md:736` (*"armed with both of the reviewer's own defeating constructs, verbatim"*) | The round-3 arming targeted the two reported exploits rather than their class. Both new defeats are one-character and one-idiom variations on them. | This is exactly what `CLAUDE.md`'s new *"run the defeat list against your own guard"* rule exists to stop, and id 68 is the feature cited in it. Round 4 must arm the class. |
| D9, D10, D11, D8 | Closed | — | `bin`/`obj` excluded by path segment (verified with two phantoms, no false red); ledger row corrected and both halves verified; fabricated block corrected in place; second false red disclosed at `:81-84`. | — |

## What must change before re-review (id 68)

1. **Fix D12 and D13 together, at the class.** Make the precondition complete against the scanner's stated limits — my recommendation is `^[ \t]*#` for directives plus a nested-quote-in-hole check that leaves `src/Seed/Program.cs`'s eleven plain interpolations green — or teach the scanner the constructs and delete the claims.
2. **Make every doc-comment sentence about the scanner true**, re-read against the code rather than against the previous round's intent. `:460-463`, `:471-473`, `:96-124`.
3. **Arm the class, not the literals**, per D14: at minimum `# if`, `#if`, `"""`, `$"…{"…"}…"`, and one construct not yet used against this guard. Record each verbatim.
4. Nothing else. **No change is required to** id 67's four test files, the `SeedRunner` pairing guard, the population test's derivation, `IsUnderBuildOutputDirectory`, the ledger row, or the `WebApplicationFactory` decision — all four of those were probed this round and hold.

## Final state of the tree

Every mutation restored from a `cp` backup taken this session and confirmed three ways — `cmp` against the backup (`cmp-ok` after each), an empty `git diff --stat` on the tracked file, and a re-read of the changed lines. `src/Billing/Program.cs` lines 15-17 read `configure:`/`configureTelemetry:`/`configureHealth:` pointing at `BillingProgramConfiguration`. `git status --porcelain -- src/` shows **no** `Program.cs`, `SeedRunner.cs` or `ZProbeService` line. `find src -name 'Program.cs'` returns **7**. Both mutated service projects were rebuilt `--no-incremental` from restored source (`src/Billing` and `src/Seed`, each 0 warnings 0 errors) so no armed binary survives, then `tests/Architecture.Tests --no-incremental` → 0 errors, and `dotnet test tests/Architecture.Tests --no-build` → **35/35 green**. `pgrep -fl "dotnet (build|test|format)"` returns nothing. One build or test run was live at a time throughout; nothing was backgrounded.

---

# Re-review (round 4) — id 68 rejected a fourth time; the instrument changed, and three new shapes defeat it

## Verdict

**Id 68 — REJECTED.** The instrument change is right in kind and I endorse it: rounds 1–3's defeats are genuinely retired, and I re-confirmed rows 5, 6 and 7 red myself with verbatim messages. But **three new working exploits** defeat the Roslyn version, all of which **compile**, all of which leave this class **9/9 green** and `Architecture.Tests` **35/35 green** with Billing's wiring broken — and one of them is strictly worse than every previous defeat, because it also kills `configure:`, the *required* argument this file argues at `:104-111` is protected by the type system. A fourth consecutive round ships an absolute safety claim about the mechanism that is false by measurement, now in **two** files rather than one.

The root cause is not another spelling, and it is not the parser. It is that the rewrite **changed the instrument without re-deriving the instrument's own premises**. Roslyn retires "what text shapes can this scanner misread"; it introduces two new premises nobody tested — *the parser's view of which code is live is the compiler's view*, and *the argument the finder locates is the argument the host call receives*. Both are false, and each is one predicate away from being true.

**Id 67 — APPROVED on the code, unchanged.** Its four test files were not touched this round (`git status --porcelain -- tests/ | grep -i DbContextFactory` → four `??` lines, untracked, unmodified); D10 and D11 were met in round 3 and I re-verified nothing regressed. Its only open gate remains the `progress/history.md` entry.

Recommended transitions — **for the leader; I made no `feature_list.json` edit, and none is needed**:

- **id 68: stays `in_progress`** (it already is) for a fifth fix round.
- **id 67: stays `in_review`** (it already is), closing jointly with id 68 — see brief item 5 below, which I accept with one condition.

## What I ran, and what I did not

Baseline `Architecture.Tests` **35/35 green** (`dotnet build tests/Architecture.Tests --no-incremental` → 0 warnings, 0 errors). I ran **ten mutations** of my own — three new attacks, one provenance attack, one compiler-premise probe, and five re-confirmations of the defeat list — each with its own `cp` backup, restore, `cmp`, empty `git diff --stat` on the tracked file, and a re-read of the restored lines. Probes expected to come back **green** were built `--no-incremental` every time, so no defeat I report can be a stale binary; probes expected red used an incremental build, where staleness could only have produced a green I would have re-tested.

I did **not** run `./quality.sh`, did not re-run the 1905-test reconciliation, and never touched `Gateway.IntegrationTests`, so backlog id 85's port race did not arise. I measured `Architecture.Tests` (35) and `Seed.UnitTests` (44) myself; the record's other per-project figures reconcile exactly against rounds 1–3 and I accept them on that basis rather than re-measuring. One build or test process was live at a time; nothing was backgrounded.

**A note on the liveness check itself, because this round produced the trap twice.** `pgrep -fl "dotnet (build|test|format)"` matched my own shell — and so did the bracket-escaped `pgrep -fl "dotnet [b]uild"`, because a shell's command line contains **the commands it ran**, brackets or not. The check that cannot count itself is `pgrep -xl dotnet` plus classification: at close, every `dotnet` process on the machine is `MSBuild.dll /nodemode:1 /nodeReuse:true`, an idle reuse node, which `CLAUDE.md` states is not a build.

## D15 — BLOCKING: the parser and the compiler disagree about which region is live

`_parseOptions` (`:115`) is `new CSharpParseOptions(LanguageVersion.Latest)` — **no preprocessor symbols**. The build defines `DEBUG`. So the region the test parser treats as dead is exactly the region the compiler **compiles**, and the region the parser reads is dead code.

**Measured in two halves.** First, that the compiler defines `DEBUG` — `#if DEBUG` / `#error DEBUG_IS_DEFINED_IN_THIS_BUILD` / `#endif` at the top of `src/Billing/Program.cs`:

```
dotnet build src/Billing --no-incremental
src/Billing/Program.cs(2,8): error CS1029: #error: 'DEBUG_IS_DEFINED_IN_THIS_BUILD'
Build FAILED.
```

The same file parsed by `ParseFile` raises nothing, because to it that region does not exist. Then the exploit — the real call in the branch the compiler takes, the decoy in the branch the parser reads:

```csharp
#if DEBUG
var builder = BillingHost.CreateBuilder(
    args,
    configure: static _ => { });
#else
var builder = BillingHost.CreateBuilder(
    args,
    configure: BillingProgramConfiguration.Configure,
    configureTelemetry: BillingProgramConfiguration.ConfigureTelemetry,
    configureHealth: BillingProgramConfiguration.ConfigureHealth);
#endif
```

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded
dotnet test tests/Architecture.Tests --no-build --filter "FullyQualifiedName~CompositionRootDelegationWiringTests"
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 223 ms
```

**Nine green with Billing's configuration a no-op, its telemetry unwired and its health probes unwired.** This is worse than D12 and D7 in two ways. D12 needed the *live* argument to be absent and a *dead* region to supply matching text; this needs no absence at all — the parser sees a complete, correct, three-argument call. And it defeats `configure:` itself, which `:104-111` reasons is unreachable because "every `configureX` parameter takes a distinct options type" — true, and irrelevant, since nothing here substitutes a type.

**Why `preprocessorSymbols: ["DEBUG", "TRACE"]` is not the fix.** It moves the disagreement rather than removing it: `#if RELEASE`, `#if !DEBUG`, and any `DefineConstants` a future `.csproj` or `Directory.Build.props` adds reopen it immediately, and the test would then be asserting about a configuration the reader cannot see. Two fixes are claim-true. Either **reject directives positively** — Roslyn *enumerates* them, so `root.ContainsDirectives` or `root.DescendantTrivia().Any(t => t.IsDirective)` is a one-line complete precondition with no denylist and no spelling — or **parse with the build's own symbols and assert the two parses agree**. The first is what round 3's `^[ \t]*#` was reaching for by hand; the irony worth recording is that **the rewrite bought the ability to do it exactly and did not do it.**

## D16 — BLOCKING: the finder is not anchored to the invocation it is about

`ExtractNamedArgument` (`:358-372`) collects every `ArgumentSyntax` in the **whole tree** whose `NameColon` matches, and the doc comment at `:343-345` states this deliberately: *"not restricted to any particular invocation."* That is disclosed as an inheritance of round 2's "exactly one match" trade. What was never measured is that it converts the trade into a **false green**: satisfy the count from anywhere, and the host call is unguarded.

`configureHealth:` dropped from the real `BillingHost.CreateBuilder` call and passed to an unrelated local function instead:

```csharp
var builder = BillingHost.CreateBuilder(
    args,
    configure: BillingProgramConfiguration.Configure,
    configureTelemetry: BillingProgramConfiguration.ConfigureTelemetry);

WiringNote(configureHealth: BillingProgramConfiguration.ConfigureHealth);

static void WiringNote(Action<OrderToCash.Billing.Infrastructure.Health.HealthOptions>? configureHealth)
    => Console.WriteLine(configureHealth is null ? "unset" : "set");
```

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 267 ms
```

Exactly one match, correct target, population unchanged, **health probes unwired**. The fix is one predicate: require the matched argument's ancestor `InvocationExpressionSyntax` to be the `*Host.CreateBuilder` / `GatewayHost.Build` call the table is about. That is three lines with a real syntax tree, and it is the thing the parser was adopted to make possible.

## D17 — Advisory: a syntax tree has no symbols, so identical text can mean a no-op

Appending a global-namespace `static class BillingProgramConfiguration` to `src/Billing/Program.cs`, with `Configure`/`ConfigureTelemetry`/`ConfigureHealth` as no-ops, shadows the `using`-imported real type — the enclosing namespace beats a using-directive import. **The call site's text is byte-identical to correct wiring** and every delegate is a no-op.

```
dotnet build tests/Architecture.Tests --no-incremental → Build succeeded
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 274 ms
```

**I judge this advisory, not blocking.** Closing it needs a `CSharpCompilation` and a `SemanticModel`, or the host-driving test this feature deliberately declined, and the mutation is conspicuous in review in a way D15's and D16's are not. But it bounds what the instrument can claim: a syntax-only parse guards **spelling**, never **meaning**, and the doc comment must stop asserting otherwise.

## D18 — Advisory, and it must be ROUTED rather than narrated: the pairing guard checks the name, never the value's provenance

The `SeedRunner` guard asserts the identifier text passed to `OpenDb`. It therefore guards the line the acceptance bullet names (`:26`) and not the line above it. At `src/Seed/Presentation/SeedRunner.cs:21`:

```csharp
var ordersConnectionString = BillingSeedWriter.ConnectionString();
```

The name the guard reads is untouched; the value comes from a sibling.

```
Architecture.Tests   Passed!  - Failed: 0, Passed: 9, Total: 9
Seed.UnitTests       Passed!  - Failed: 0, Passed: 44, Total: 44
```

**Both green, with Orders fixtures written into the Billing database** — id 56's D1 consequence and id 68's own acceptance bullet 2, reached from the line immediately above the line that bullet names. This is `CLAUDE.md`'s provenance rule one level up: the earlier form says a corruption probe only bites on a field whose expected **value** the test supplied; this says the same of a value's **source**. It does not defeat the literal acceptance bullet — A8 below confirms `:26` is red — but it is the same defect class at the adjacent line, so per `CLAUDE.md` it closes in this feature or leaves a **numbered backlog entry**. A sentence in a review is the artefact this repository has already paid for twice.

## D19 — BLOCKING: a fourth consecutive false absolute claim, now in two files

| Site | Claim | Status |
|---|---|---|
| `CompositionRootDelegationWiringTests.cs:87-88` | *"A defeat that relies on any of these shapes cannot exist here, not because this file anticipated it, but because none of them is code."* | **False, measured.** D15 relies on a `#if` region, the first shape in that list. |
| `Directory.Packages.props:26-30` | *"a disabled preprocessor region becomes trivia … so this whole defeat class cannot exist rather than needing another spelling added to a denylist"* | **False, measured**, and now the claim has spread to a second file where no reader of the test would look for it. |
| `CompositionRootDelegationWiringTests.cs:76-79` | *"a disabled `#if false`/`#if false` region — any spelling, any whitespace after `#` — becomes DISABLED TRIVIA"* | **True and beside the point.** The question is never whether a *disabled* region is invisible; it is **which region the parser disabled**. (The doubled `#if false`/`#if false` also reads as a typo for `# if false`.) |
| `impl_…md:1063`, defeat-list row 9 | *"Every 'never'/'always'/'cannot' claim … was re-read against the code it describes"* | The re-reading confirmed the **intent** and never tested the **premise**. One `#error` falsifies it in a single build — so row 9 is mutation-shaped after all, contrary to the record's classification of it as "not a mutation-shaped attack". |

This is D14's finding one level up, and it is the reason the round failed: **hardening the half of the claim that was attacked last round displaced attention from the half that had never been attacked.** `CLAUDE.md` already names this shape — *a rule that hardens one half of a two-part claim does not harden the other, and will quietly borrow attention from it.*

## The ten-row defeat list, re-run by me — rows 5, 6 and 7 confirmed as the brief required

| # | Shape | Mine | Result |
|---|---|---|---|
| 1 | Delete the behaviour | R7a, R7b | **RED.** |
| 2 | Corrupt a supplied field | (record's P10; covered by R5/R6's target comparison on every run) | Accepted from the record. |
| 3 | Substitute a valid sibling identifier | A8 | **RED** — *"OrdersSeedWriter.OpenDb(...) is called with 'billingConnectionString', expected its OWN 'ordersConnectionString' — a sibling's connection string was substituted."* |
| 4 | Shadow the pattern from a comment or literal | R6 | **RED.** |
| 5 | Hide it in a dead region | R5 — `# if false` / `# endif`, D12's exact one-space defeat | **RED** (was green in round 3). Closed. |
| 5′ | **Hide it in a region the parser thinks is dead and the compiler compiles** | **A1** | **DEFEATED — D15.** |
| 6 | Hide it in a raw or verbatim string | R6 — `"""wiring: "configureHealth: …" is wired."""` | **RED** (was green in round 3). Closed. |
| 7 | Drop an optional element entirely | R7a (Projector, `configureHealth`), R7b (Orders, `configureTelemetry`) — **both on files other than Billing**, as the brief required | **RED**, each naming the file and the argument. |
| 7′ | **Drop it from the real call and satisfy the count elsewhere** | **A2** | **DEFEATED — D16.** |
| 8 | Compare a literal to a literal | accepted from the record (P13) | Population is a real disk read; `find src -name 'Program.cs'` → 7, matching the table. |
| 9 | Premise half goes stale | **A0** | **DEFECT — D19.** The premise *"the parser sees what the compiler sees"* is false and takes one `#error` to disprove. |
| 10 | Build-output copy joins the population | accepted from the record (P14); `IsUnderBuildOutputDirectory` byte-identical and re-read | Holds. |
| — | **Identical text, different meaning** | **A3** | **DEFEATED — D17** (advisory). |
| — | **Right name, wrong value provenance** | **A7** | **DEFEATED — D18** (advisory, routable). |

Verbatim red messages, as measured:

```
R5 / R6  src/Billing/Program.cs: the table expects [configure, configureHealth, configureTelemetry], the source on disk has [configure, configureTelemetry] — a delegating argument was added, removed, renamed or duplicated without updating this test's table.
         Could not find a 'configureHealth:' named argument in src/Billing/Program.cs.
         Failed!  - Failed: 2, Passed: 7, Total: 9
R7a      Could not find a 'configureHealth:' named argument in src/Projector/Program.cs.
         Failed!  - Failed: 2, Passed: 7, Total: 9
R7b      Could not find a 'configureTelemetry:' named argument in src/Orders/Program.cs.
         src/Orders/Program.cs: the table expects [configureAcceptance, configureHealth, configureOutbox, configureSaga, configureTelemetry], the source on disk has [configureAcceptance, configureHealth, configureOutbox, configureSaga] — …
         Failed!  - Failed: 2, Passed: 7, Total: 9
A8       OrdersSeedWriter.OpenDb(...) is called with 'billingConnectionString', expected its OWN 'ordersConnectionString' — a sibling's connection string was substituted.
         Failed!  - Failed: 1, Passed: 8, Total: 9
```

## Brief item 4 — the package, judged

**Justified, and 5.0.0 is right.** One `PackageReference`, no version attribute, `PackageVersion` centrally pinned in `Directory.Packages.props:31`, and reachable from exactly one project — `grep -rn "CodeAnalysis" --include='*.csproj' .` returns a single hit, `tests/Architecture.Tests/Architecture.Tests.csproj:21`. No `src/` project sees it, so no production surface changed and no architecture rule is touched. 5.0.0 is the Roslyn band shipping with the .NET 10 SDK on this machine (`sdk/10.0.112`); the package has no `net10.0` lib asset and resolves `net9.0`, which is normal for Roslyn and built clean under `TreatWarningsAsErrors`. Its only non-framework dependency is `Microsoft.CodeAnalysis.Common` pinned `[5.0.0, 5.0.0]`, covered by `CentralPackageTransitivePinningEnabled`. Both 4.14.0 and 5.0.0 were already in the local cache, so no version hunting occurred, per the props file's own standing comment.

Choosing the compiler's parser over a fourth hand-rolled scanner is the correct call and I would not reverse it — the defects above are in **how it is driven**, not in the decision. Two conditions on the package as recorded: the `Directory.Packages.props:26-30` comment must lose its false claim (D19), and the package must appear in the phase commit message per `CLAUDE.md` — the record supplies that line already.

## Brief item 5 — one joint `history.md` entry for ids 67 and 68

**Acceptable, with one condition.** The record states the two effort windows are inseparable; inventing a split would be a fabricated number, which is worse than a joint bound, and `CLAUDE.md` asks for effort records that are honest before they are tidy. The condition: the entry must **name both ids in its heading**, state the figure explicitly **as a bound rather than a total**, and record id 68's shape — four implementation rounds against four reviews, each round defeated by a different class — because that shape, not the wall-clock, is the comparison worth carrying to #9. A joint entry that reads as one feature's cost would understate exactly the thing this repository exists to measure.

One bookkeeping item for the same close, not a review defect: id 67's four test files are **untracked** (`??`), so they must be staged when the human commits or the feature ships with no tests in the repository.

## `R<n>` → test mapping

Unchanged and re-confirmed for a fourth time: neither id is `sdd: true`, neither claims an `R<n>`, `specs/shared/test-matrix.md` is untouched by either, and no `R<n>` appears in either id's test files. Nothing to map.

## `CHECKPOINTS.md` — boxes walked this round

C1, C6 and C7 remain not applicable (no harness change, no `sdd: true` feature, `specs/shared/` untouched).

- [x] **C2** — at most one `in_progress` (id 68); id 67 `in_review`; statuses valid; `feature_list.json` untouched by the implementer this round and not written by me.
- [x] **C3** — no architecture surface touched. The changed files are one test, one test `.csproj` and `Directory.Packages.props`. No shared runtime code, no cross-service DB access, no domain reference, no `src/` project takes the new package.
- [x] **C4** — integration suites untouched; the changed test is a pure source-parsing unit test with no broker; no Jest.
- [ ] **C4** — `./quality.sh` not run by me (per the brief). `Architecture.Tests` **35/35** verified by me at baseline and after the final restore; `Seed.UnitTests` **44/44** verified during D18. Recorded so the reader can tell verification from assumption.
- [x] **C5** — no suspicious untracked files from my probes; no phantom service or build-output copy was created this round; `find src -name 'Program.cs'` returns exactly **7**.
- [ ] **C5** — `progress/history.md` has **no entry** for either id. Blocks closing id 67 and id 68.
- [x] **C5** — `feature_list.json` reflects true state; not written by implementer or reviewer this round.
- [x] **C5** — Claude did not commit. No git command that writes the index or working tree was run; every restore was `cp` from a backup taken this session, confirmed by `cmp`, by an empty `git diff --stat` on the tracked file, and by re-reading the restored lines.

## Defects, with file and line

| # | Severity | Location | Defect | Why it matters |
|---|---|---|---|---|
| D15 | **BLOCKING** | `CompositionRootDelegationWiringTests.cs:115` (`_parseOptions`), consumed at `:423` | Parse options define no preprocessor symbols; the build defines `DEBUG`. The parser reads the branch the compiler discards. Measured: **9/9 green** with `configure:` a no-op and telemetry and health both unwired. | The first defeat to kill a **required** argument, which `:104-111` argues is type-system-protected. Roslyn makes the complete fix a one-line directive check — the rewrite bought that ability and did not use it. |
| D16 | **BLOCKING** | same file, `:358-372`, disclosed at `:343-345` | The argument finder is not anchored to the host invocation. Satisfying the count from any unrelated call passes. Measured: **9/9 green**, health probes unwired. | Converts round 2's disclosed "exactly one match" trade from a false **red** into a false **green** — the family this table exists to catch. One ancestor predicate fixes it. |
| D17 | Advisory | same file, whole class | A syntax tree carries no symbols: a shadowing type makes byte-identical call text mean a no-op. Measured: **9/9 green**. | Bounds what any syntax-only instrument may claim. No fix required; the doc comment must stop denying it. |
| D18 | Advisory, **must be routed** | `src/Seed/Presentation/SeedRunner.cs:21`; guard at `CompositionRootDelegationWiringTests.cs:317-340` | The pairing guard checks the identifier's **name**, never the value's **provenance**. Measured: `Architecture.Tests` 9/9 **and** `Seed.UnitTests` 44/44 green with Orders fixtures going to the Billing database. | Id 68's own acceptance consequence, reached from the line above the line the acceptance names. Per `CLAUDE.md` it closes here or becomes a numbered backlog entry — not a sentence. |
| D19 | **BLOCKING** | `CompositionRootDelegationWiringTests.cs:87-88` and `:76-79`; `Directory.Packages.props:26-30`; `impl_…md:1063` | Fourth consecutive round shipping a false "cannot exist" claim about the mechanism, now propagated to a second file. | #9 inherits the sentence, not the measurement — and the claim now sits in a dependency manifest, where no reader of the test will re-check it. |
| D12, D13 | **Closed** | — | `# if false` (R5) and the raw string / nested-hole family (R6) are both **RED**, re-measured by me with verbatim messages. Rows 1, 3, 4, 7 also red, row 7 on two files other than Billing. | The rewrite is a real improvement and the right instrument. |

## What must change before re-review (id 68)

1. **Fix D15 at the class, not at `DEBUG`.** Either reject any directive trivia in a parsed file (`root.ContainsDirectives` — complete, no denylist, no spelling), or parse with the build's own symbols and assert both parses agree. Arm it with A0's `#error` probe **in both directions**: prove the compiler's symbol set and prove the test now sees the same regions.
2. **Fix D16** by anchoring the argument search to the `*Host.CreateBuilder` / `GatewayHost.Build` invocation, and arm it with A2 verbatim.
3. **Correct every absolute claim (D19)** in `CompositionRootDelegationWiringTests.cs` and in `Directory.Packages.props:26-30`, and state the instrument's real boundary: it guards spelling within the regions it parses, not meaning (D17) and not provenance (D18).
4. **Close D18 or file it as a numbered backlog entry.** Not a sentence in a record.
5. **Arm the class, not these four literals** — the rule that failed three times and failed again this round. Before submitting, ask of each new premise: *what would make this false?* The two that mattered here were "the parser agrees with the compiler" and "the argument I found is the argument that is passed", and both are one probe each.
6. Nothing else. **No change is required to** id 67's four test files, the ledger row, `IsUnderBuildOutputDirectory`, the population's file-set derivation, the `SeedRunner` guard's existing `:26` assertion, the package choice, or the `WebApplicationFactory` decision — all were probed or re-read this round and hold.

## Final state of the tree

Ten mutations across four tracked files (`src/Billing/Program.cs`, `src/Projector/Program.cs`, `src/Orders/Program.cs`, `src/Seed/Presentation/SeedRunner.cs`), every one restored from a `cp` backup taken this session and confirmed three ways — `cmp` against the backup (`cmp-ok` for all four), an empty `git diff --stat` on all four tracked paths, and a re-read of the restored lines, which show Billing's three `configureX:` arguments, Projector's three, Orders' five and `SeedRunner`'s three connection-string assignments all pointing at their own service. `git status --porcelain -- src/` shows no `Program.cs`, no `SeedRunner.cs` and no probe service. `find src -name 'Program.cs'` returns **7**; no build-output copy was created this round. After the last restore, all four files were `touch`ed and `dotnet build tests/Architecture.Tests --no-incremental` ran from restored source (0 warnings, 0 errors), then `dotnet test tests/Architecture.Tests --no-build` → **35/35 green**, so no armed binary survives. Every `dotnet` process on the machine at close is an idle `MSBuild.dll /nodemode:1 /nodeReuse:true` reuse node; no `dotnet build`, `test` or `format` is alive. One build or test run was live at a time throughout; nothing was backgrounded, and no git command that writes the index or working tree was used.

---

# Final review (round 5, capped) — id 68 approved under the cap with one filable residual; id 67 closable on one artefact

## Verdict

**Id 68 — APPROVED UNDER THE CAP, with a disclosed, filable residual.** On the merits alone this is a fifth defeat: a mutation that compiles, leaves `Architecture.Tests` 35/35 and this class 9/9 green, and unwires Billing's health probes, survives the round-5 instrument. I am not calling that a rejection, because the brief caps the loop and routes survivors to the backlog, and because the residual is **guard incompleteness, not a production defect** — the wiring on disk is correct, and every mutation I ran was restored. The four fixes this round are real and I confirmed each: D15 closed structurally, D16 closed *for the shape it was armed against*, D18 closed by a new assertion that genuinely bites, D19's false claim deleted rather than weakened. The honest summary for #9 is in the two sentences the residual finding below makes precise, not in an approval.

**Id 67 — APPROVED on the code, unchanged from rounds 3 and 4.** Its four test files were untouched again this round (`git status --porcelain -- tests/ | grep -i DbContextFactory` → four `??` lines). Its only open gate is still the `progress/history.md` entry.

Recommended transitions — **for the leader; I edited no `feature_list.json` and ran no git command that writes the index or working tree**:

- **id 68 → `done`**, once the joint `history.md` entry exists.
- **id 67 → `done`**, same condition, same entry.
- **One new backlog entry**, filed verbatim from "The residual, in filable form" below.

**Both closures are gated on one artefact that does not yet exist.** `grep -n "^## " progress/history.md | tail -6` ends at id 72; there is **no entry for either id**. C5 requires it and `CLAUDE.md` makes a feature without an effort record non-closeable. That is the leader's write, not mine.

## What I ran, and what I did not

Baseline `dotnet build tests/Architecture.Tests --no-incremental` → 0 warnings, 0 errors; `dotnet test --no-build` → **35/35**. Seven mutations of my own, each with its own `cp` backup, restore, `cmp` against the backup, `git diff --stat` on the tracked path (meaningful here — all four probe targets are **tracked**, and all four were confirmed byte-identical to `HEAD` with `git show HEAD:<path>` before I began, never with a checkout), and a re-read of the restored lines.

**One protocol note that saved four build cycles and is worth recording for #9:** this guard reads `src/` **as text at test time**, so the test binary is unaffected by a `src/` mutation and the stale-binary hazard the arming protocol exists for cannot arise from these probes in the test project. I still forced `--no-incremental` rebuilds of every mutated `src/` project (to prove each mutation compiles) and of `tests/Architecture.Tests` for the one probe I expected to come back **green**, so no green I report can be a stale binary.

I did **not** run `./quality.sh`, did not re-run the 1905-test reconciliation, and never touched `Gateway.IntegrationTests`, so id 85's port race did not arise. I measured `Architecture.Tests` (**35**) and `Seed.UnitTests` (**44**) myself; the record's other per-project figures reconcile exactly against rounds 3 and 4 and I accept them on that basis rather than re-measuring. One build or test process was live at a time; nothing was backgrounded. At close, `ps -eo args | grep -E "dotnet (build|test|format)"` → **0**, and every `dotnet` process on the machine is an idle `MSBuild.dll /nodemode:1` reuse node.

## Brief question 1 — the two surviving absolutes, tested

### `:475` — *"A comment, a raw string, or a quote nested inside an interpolation hole can never produce an `ArgumentSyntax` node here, because none of them is code."* — **FALSE as it governs. It must go.**

Measured (probe P1). Billing's real call drops `configureHealth:`; the argument is supplied instead from **inside an interpolation hole**, in a second invocation of the same host method, with `configure` passed **positionally** so the required argument still matches exactly once:

```csharp
var builder = BillingHost.CreateBuilder(
    args,
    configure: BillingProgramConfiguration.Configure,
    configureTelemetry: BillingProgramConfiguration.ConfigureTelemetry);

var wiringNote = $"{BillingHost.CreateBuilder(args, BillingProgramConfiguration.Configure, configureHealth: BillingProgramConfiguration.ConfigureHealth)}";
ArgumentNullException.ThrowIfNull(wiringNote);
```

```
dotnet build src/Billing --no-incremental              → 0 Warning(s), 0 Error(s)
dotnet build tests/Architecture.Tests --no-incremental → 0 Error(s)
dotnet test  tests/Architecture.Tests --no-build --filter "FullyQualifiedName~CompositionRootDelegationWiringTests"
Passed!  - Failed: 0, Passed: 9, Skipped: 0, Total: 9, Duration: 241 ms
```

**Nine green with Billing's health probes unwired.** The falsification is exact: after the mutation the file's **only** `configureHealth:` `ArgumentSyntax` node is the one inside the interpolation hole, and `ExtractNamedArgument` found it, counted it as its one match, and compared its target. So an interpolation hole produced an `ArgumentSyntax` node **here**, in the precise sense of "here" the sentence uses.

The sentence is true only under its narrowest reading — the *nested quote token* itself is a literal — and its justification clause, *"because none of them is code"*, is **false as written**: an interpolation hole's contents are a genuine expression tree, which the same file says correctly at `:76-79`. And the file is not where the damage settles: `Directory.Packages.props:26-28` states the generalised form outright —

> *"a raw string or an interpolation hole's contents become literal tokens, never reachable as an ArgumentSyntax node, so that half of the defeat class cannot exist"*

— which is **false, measured**, in a dependency manifest where no reader of the test will ever re-check it. This is the fifth consecutive round carrying a false absolute about this mechanism, and the second in which it has propagated to `Directory.Packages.props`. D19's instruction was *prefer deleting the absolute to weakening it*; the deletion reached the `#if` bullet and stopped one paragraph short.

### `:625` — *"A build directory can only ADD an entry — it can never remove a real file or mask a wrong argument in one."* — **TRUE on the half that matters, and it can stand.** One scoping caveat, which does not change its conclusion.

The **mask** half is measured (probe P3), because it is the half that could ever be a false green. Billing's real `Program.cs` drops `configureHealth:` while a **fully correct** copy sits at `src/Billing/bin/Debug/net10.0/publish/Program.cs`:

```
src/Billing/Program.cs: the table expects [configure, configureHealth, configureTelemetry], the source on disk has [configure, configureTelemetry] — a delegating argument was added, removed, renamed or duplicated without updating this test's table.
Could not find a 'configureHealth:' named argument passed to BillingHost.CreateBuilder(...) in src/Billing/Program.cs.
Failed!  - Failed: 2, Passed: 7, Total: 9
```

A build-output copy cannot rescue a wrong real file: the per-service facts resolve a **fixed relative path**, and the population test excludes the copy by path segment before parsing anything. The phantom was removed and its absence confirmed.

The caveat, read from the code rather than measured, on a two-line pure function: `IsUnderBuildOutputDirectory` (`:633-637`) splits the **absolute** path, so its predicate silently ranges over every ancestor directory of the checkout, not only over build directories inside it. Under a checkout whose path contains a `bin` or `obj` segment, *every* `Program.cs` is excluded and `discoveredProgramCsFiles` is empty — which fails the set comparison loudly. So the sentence's operative conclusion — *"a false-red hazard only, never a false-green one"* — **holds**, and only the phrase "a build directory" understates what the predicate tests. **My judgement: leave `:625` standing.** It is the one absolute in this file that has survived being attacked.

## Brief question 2 — the 182 new lines brought three premises the six-row table does not name

The record's enumeration is **present and incomplete**. Pr1–Pr6 each arm the exploit that produced them; none of the three premises below appears, and the first is the round's defeat.

**Un-enumerated premise 1 — the D16 anchor binds a call SHAPE, not THE host call.** `IsArgumentOfInvocation` (`:516-524`) matches *any* invocation rendering as `BillingHost.CreateBuilder`, and `ExtractNamedArgument` (`:490-507`) still counts matches across the whole tree. Round 4's D16 was armed with one shape — an argument passed to an unrelated local function (`WiringNote`) — and the fix closes exactly that shape. A decoy that is *itself* a host call satisfies the anchor, and P1 above is the measurement. This is D14's finding for the **fifth** time: the round armed the reported exploit rather than its class, and the class is *"the argument the finder locates is not the argument the live host call receives"* — of which "passed to a different method" was only one member.

**Un-enumerated premise 2 — the anchor assumes the host type is spelled as a bare identifier.** The pattern requires `Expression: IdentifierNameSyntax`, so a **fully-qualified, entirely correct** host call is a false red (probe P2, wiring untouched and complete, 0 warnings, 0 errors):

```
Could not find a 'configure:' named argument passed to BillingHost.CreateBuilder(...) in src/Billing/Program.cs.
Failed!  - Failed: 1, Passed: 8, Total: 9
```

A `using` alias does the same. The message misdiagnoses correct code as a *missing argument*, which is the failure mode id 68's own acceptance bullet 4 forbids (*"the failure message is checked to name the intended reason rather than an incidental one"*). The irony is local: `:132-137` of this same file celebrates that a fully-qualified **target** is now accepted by `TargetMatches`'s member-boundary suffix rule, while the new anchor rejects a fully-qualified **host type** — the same fix round added both. The record's Pr4 claims this premise is armed "by the full baseline run", but a green baseline tests the **six spellings on disk today**, which is a sample, not the population of legitimate spellings.

**Un-enumerated premise 3 — `ParseFile` rejects ALL directive trivia, not conditional compilation.** `trivia.IsDirective` is true for `#pragma`, `#region`/`#endregion`, `#nullable` and `#line`. Probe P4, wiring correct and complete:

```
src/Billing/Program.cs contains conditional-compilation directive(s) [#pragma warning disable CA1050, #region composition, #endregion] — this parser is not fed the build's preprocessor symbols (D15: …), so a composition root under this guard may not carry conditional compilation of any kind. Remove the directive(s).
Failed!  - Failed: 2, Passed: 7, Total: 9
```

**None of the three items the message names is conditional compilation.** The structural choice is right and I endorse it over symbol-matching; what is un-enumerated is the constraint it imposes — *no composition root in this repository may ever carry any directive, including `#pragma warning disable`* — and the message names a reason that is not the one it fired on. Severity: false red, loud, cheap to fix in the message text.

**Un-enumerated premise 4 (bound, not a defect) — the provenance guard checks the DECLARATION, never the last write.** I armed the new assertion independently, and it works: D18's exact mutation is **RED**, verbatim —

```
'ordersConnectionString' is initialized from 'BillingSeedWriter.ConnectionString()', expected 'OrdersSeedWriter.ConnectionString()' — a sibling writer's connection string was assigned under this writer's own variable name.
Failed!  - Failed: 1, Passed: 8, Total: 9
```

Then the adjacent shape (probe S2): leave the declaration correct and overwrite the value one line later.

```csharp
var ordersConnectionString = OrdersSeedWriter.ConnectionString();
…
ordersConnectionString = BillingSeedWriter.ConnectionString();
```

```
Architecture.Tests  Passed!  - Failed: 0, Passed: 9,  Total: 9
Seed.UnitTests      Passed!  - Failed: 0, Passed: 44, Total: 44
```

Both green, with Orders fixtures written into the Billing database — id 56's D1 consequence for the **third** time, one line below where D18 found it and two lines below where R2-5 found it. This is not a new class; it is the same walk (the read → the name → the declaration → the assignment) and it is the clearest available evidence that a syntax-level provenance check terminates only when it follows assignments, or when the **use** is checked instead of the name.

## Brief question 3 — row 8, checked rather than inherited

**It holds, and the inheritance was legitimate.** Two independent readings:

- The population test is a **genuine disk read**, and P3 proves it by making it fail on a real dropped argument in a real file. The file-set literal is compared against `Directory.GetFiles`, and the per-file expectation against a real Roslyn parse. Nothing compares a literal to a literal.
- The absolute → relative → absolute round trip cannot read a different file than it discovered: `ToRepositoryRelativePath` uses the local `FindRepositoryRoot()` and `ParseFile` re-resolves through `RepositoryPaths.Find`, and I read both — they are the same walk-up for `OrderToCash.sln`, line for line.

**But the leader's instinct was right about there being an interaction, and it is not row 8's.** The population test does **not** use the D16 anchor: its unit is *the multiset of `configure`-prefixed argument names per file*, and P1 preserves that multiset exactly (Billing still has `configure`, `configureTelemetry`, `configureHealth`; the grand total is still 19). So the population test is not, and cannot be, the backstop for an anchored per-service fact. Row 8 is sound; the correct statement is that this round created a **unit mismatch** between the two tests, not a literal-to-literal comparison.

One related note on the new literal: `_hostInvocationsByProgramCsPath` (`:212-220`) is never compared against disk, so a renamed host method would make it stale — but staleness produces a loud red (P2 is exactly that failure mode), so it is a false-red risk only.

## The residual, in filable form — file it verbatim

> **Title.** `composition_root_delegation_guard_anchors_a_call_shape_not_the_live_host_call`
>
> **Where.** `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs:516-524` (`IsArgumentOfInvocation`) and `:490-507` (`ExtractNamedArgument`); false claims at `:474-476` and `Directory.Packages.props:26-28`; secondary false reds at `:517-518` (bare-identifier host type) and `:602-613` (`ParseFile`'s message).
>
> **Mechanism.** `IsArgumentOfInvocation` requires an argument's enclosing invocation to *render as* `<HostType>.<HostMethod>`; it does not require that invocation to be the one live host call the file is about, and `ExtractNamedArgument` still searches the whole tree. So the "satisfy the count elsewhere" family that D16 closed for **unrelated** call sites remains open for a **same-shaped decoy**: drop `configureHealth:` from the real `BillingHost.CreateBuilder(...)` and add a second, discardable `BillingHost.CreateBuilder(args, BillingProgramConfiguration.Configure, configureHealth: BillingProgramConfiguration.ConfigureHealth)` — with `configure` passed **positionally**, so the required argument still matches exactly once. Measured 2026-09-12: compiles with 0 warnings, `Architecture.Tests` **35/35** and this class **9/9 green**, with Billing's health probes unwired. The decoy was placed inside an interpolation hole, which additionally falsifies the claim at `:474-476` and at `Directory.Packages.props:26-28` that an interpolation hole's contents can never be reached as an `ArgumentSyntax` node — after the mutation, the file's only `configureHealth:` node **is** the one in the hole, and the guard read it and passed.
>
> **Why it matters.** It is the same false-green family the table exists to catch — correct-looking wiring with a service's health probes unwired — and it is the fifth consecutive defeat of this guard, each one a variation on the previous round's literal exploit rather than a new idea. The claim in `Directory.Packages.props` is the more expensive half: a false absolute in a dependency manifest, which no reader of the test will re-check, and which #9 inherits as settled.
>
> **What closing it would take.** (1) Anchor to **the** invocation, not to its shape: collect the file's `<HostType>.<HostMethod>` invocations first, require **exactly one**, and read the argument list of that node — which also lets the population be derived from the same node, closing the unit mismatch with `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk`. (2) Match the host expression by rendered-suffix on a member boundary — the rule `TargetMatches` already uses for targets — so a fully-qualified or aliased host call stops being a false red. (3) Delete the two false absolutes rather than weakening them. (4) Reword `ParseFile`'s message to name what it actually rejects (**any** directive trivia, `#pragma` and `#region` included). (5) Arm each against the **class**: for the anchor, at minimum a same-shaped decoy, a positional-argument decoy, and a decoy in an interpolation hole.
>
> **Also record, same entry or its own:** `AssertConnectionStringProvenance` (`:437-461`) checks the **declaration's initializer** and not the variable's last write. Leaving the declaration correct and adding `ordersConnectionString = BillingSeedWriter.ConnectionString();` one line below leaves `Architecture.Tests` **9/9** and `Seed.UnitTests` **44/44** green, with Orders fixtures going to the Billing database — id 56's D1 consequence for the third time, at the third successive line. Closing it means following assignments, or checking the **use** rather than the name.

## The defeat list, this round

| # | Shape | Mine | Result |
|---|---|---|---|
| 1 | Delete the behaviour | P3 (Billing `configureHealth:` dropped) | **RED**, two facts, both naming the file and the argument |
| 3 | Substitute a valid sibling identifier | S1 (D18's exact mutation) | **RED**, verbatim message above — the new guard genuinely bites |
| 5 / 5′ | Dead region; region the parser thinks is dead | not re-armed — the record's Pr1/Pr2 are structural and I re-derived the mechanism (`ParseFile` rejects on trivia, before any argument is read); P4 exercises the same code path and fires | Accepted, with the path independently exercised |
| 7 | Drop an optional element entirely | P3 | **RED** |
| 7′ | Drop from the real call, satisfy the count elsewhere — **same-shaped decoy** | **P1** | **DEFEATED** — the residual above |
| 8 | Compare a literal to a literal | P3 + reading both root-walks | Holds; unit mismatch noted instead |
| 10 | Build-output copy joins the population | P3 (copy present, real file wrong) | **RED** — cannot mask |
| — | Legitimate spelling the instrument cannot read | **P2** (fully-qualified host call) | **FALSE RED**, message misnames the reason |
| — | Legitimate directive the instrument cannot read | **P4** (`#pragma`, `#region`) | **FALSE RED**, message misnames the category |
| — | Identical text, different meaning (D17) | not re-armed — deliberately open, correctly disclosed at `:119-130` as a bound | Accepted |
| — | Right name, wrong value provenance, **one line lower** | **S2** | **GREEN** — bound, filed above |

## `R<n>` → test mapping

Unchanged and re-confirmed for a fifth time: neither id is `sdd: true`, neither claims an `R<n>`, `specs/shared/test-matrix.md` is untouched by either, and no `R<n>` appears in either id's test files. Nothing to map.

## `CHECKPOINTS.md` — boxes walked this round

C1, C6 and C7 remain not applicable (no harness change, no `sdd: true` feature, `specs/shared/` untouched, no `R<n>` claimed).

- [x] **C2** — at most one `in_progress` (id 68); id 67 `in_review`; every status valid; `feature_list.json` untouched by the implementer this round and not written by me.
- [x] **C3** — no architecture surface touched. The round's changed files are one test, one comment in `Directory.Packages.props` and the implementation record. No `src/` change, no shared runtime code, no cross-service DB access, no domain reference, no `src/` project takes the Roslyn package.
- [x] **C4** — integration suites untouched; the changed test is a pure source-parsing unit test with no broker; no Jest.
- [ ] **C4** — `./quality.sh` not run by me, per the brief. What I ran instead: `Architecture.Tests` **35/35** at baseline and again after the final restore, `Seed.UnitTests` **44/44** during S2, and six filtered 9-fact runs. Recorded so a reader can tell verification from assumption.
- [x] **C5** — no suspicious untracked files: the one phantom I created (`src/Billing/bin/Debug/net10.0/publish/Program.cs`) was removed and its absence confirmed; `find src -path '*/bin/*Program.cs' -o -path '*/obj/*Program.cs'` → no hits; `find src -name 'Program.cs'` → **7**; `src/ZProbeService` does not exist.
- [ ] **C5** — `progress/history.md` has **no entry for either id**. This is the only thing blocking both closures.
- [x] **C5** — `feature_list.json` reflects true state; not written by implementer or reviewer this round.
- [x] **C5** — Claude did not commit. No git command that writes the index or working tree was run; every restore was `cp` from a backup taken this session.

## Brief question 4 — id 67, and the joint `history.md` entry

**Id 67 is closable**, on the same single condition as id 68. Nothing regressed: its four test files were untouched for a third consecutive round, and D10's ledger row and D11's correction were verified in round 3 and re-read here.

**The joint entry is acceptable on the conditions I set in round 4, with one correction to the shape the brief proposes.** It must name **both ids** in its heading, state the figure explicitly **as a bound rather than a total**, and record id 68's shape. The record's own D5 supplies the bound honestly and I accept it as written: **1 session; wall-clock floor ≥38 minutes** for the shared id 67 + id 68 implementation slice (13:42:51 → 14:20:12, first new-file mtime to record completion), **plus ≈11–15 minutes** for fix round 2, plus fix rounds 3, 4 and 5 and five reviews, which left no timestamped artefact of their own. The record is right that the two ids are **not separable** within that window and right to refuse to estimate further; an invented split would be the fabricated number `CLAUDE.md` ranks below an honest bound.

**The correction: the shape is not four rounds and four defeats — it is five and five.** Five implementation rounds (the original plus fix rounds 2–5) against five reviews, and **each review found a defeat of a different class in the previous round's own output**: a wiring-note comment; `#if false` and a raw string; `# if false` (one space) and a quote in an interpolation hole; the parser/compiler `#if DEBUG` disagreement plus an unanchored finder; and now a same-shaped decoy host call. Writing it as four-and-four would understate the one measurement this entry exists to carry to #9. The benchmark lesson is not the wall-clock and not the defeat count — it is that **five consecutive rounds armed the exploit that was reported rather than the class it came from**, including the two rounds that changed the instrument and the round whose explicit brief was to arm the class.

One bookkeeping item repeated from round 4, not a review defect: id 67's four test files and this test file are **untracked** (`??`), so they must be staged when the human commits, or both features ship with no tests in the repository.

## Final state of the tree

Seven mutations across two tracked files (`src/Billing/Program.cs` ×4, `src/Seed/Presentation/SeedRunner.cs` ×2, plus one build-output phantom), every one restored from a `cp` backup taken this session and confirmed four ways: `cmp` against the backup (`cmp-ok` every time), `cmp` against `git show HEAD:<path>` (**byte-identical to HEAD** for both files at close), an empty `git status --porcelain -- 'src/*/Program.cs' src/Seed/Presentation/SeedRunner.cs`, and a re-read of the restored lines — Billing's three `configureX:` arguments point at `BillingProgramConfiguration`, and `SeedRunner.cs:21-23` reads each writer's own `ConnectionString()`. After the last restore both files were `touch`ed and rebuilt `--no-incremental` (`src/Billing`, `src/Seed`, `tests/Architecture.Tests`, each 0 warnings 0 errors), then `dotnet test tests/Architecture.Tests --no-build` → **35/35 green**, so no armed binary survives. `find src -name 'Program.cs'` → **7**; the phantom publish copy is gone. No `dotnet build`, `test` or `format` process is alive; every `dotnet` process on the machine is an idle `MSBuild.dll /nodemode:1` reuse node. One build or test run was live at a time throughout, nothing was backgrounded, and no git command that writes the index or working tree was used.
