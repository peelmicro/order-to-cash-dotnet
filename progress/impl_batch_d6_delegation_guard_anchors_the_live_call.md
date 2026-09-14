# impl — batch D6, backlog id 86: the delegation guard anchors THE live host call

**Status: PASS.** All six acceptance bullets implemented and armed against the class rather than the reported literal. `Architecture.Tests` **50/50** (unchanged count: this entry hardens existing tests and adds no new `[Fact]`). Full `./quality.sh` reconciliation is in §10.

**Entry:** id 86, `composition_root_delegation_guard_anchors_a_call_shape_not_the_live_host_call`, phase 14, `sdd: false`. The last implementation entry of phase 14.

**Files touched (three, plus this record):**

| File | Why |
|---|---|
| `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` | bullets 1, 2, 3 (first copy), 4, 6 |
| `Directory.Packages.props` | bullet 3 (second copy — the manifest half) |
| `progress/impl_batch_d6_delegation_guard_anchors_the_live_call.md` | this record (bullet-free: the brief's Report section) |

No source file under `src/` was changed. The ten mutations below touched `src/Billing/Program.cs` and `src/Seed/Presentation/SeedRunner.cs` temporarily and every one was restored and `cmp`-verified (§5).

---

## 1. Where the brief and the contract differ — the bullets win, and here is where they diverged

The brief told me to read id 86's six `acceptance` bullets verbatim and treat them as the contract, and to report any place the brief and a bullet appear to differ. Three differences, none of which changed what I built:

| # | The brief says | `feature_list.json` says | What I did |
|---|---|---|---|
| D1 | "Id 86 is `pending`" | id 86's `status` is **`in_progress`** (already transitioned before I started) | Nothing — the brief forbids touching `feature_list.json` and the coordinator owns its transitions. Reported here only so the record is not read as a claim that I moved it. |
| D2 | "`AssertConnectionStringProvenance` is **declared at `:437`**, not near `:366`" | bullet 6 cites `:437-461` | The brief and the bullet agree; the brief's correction of *its own* earlier draft is accurate. `:437` was the declaration, `:366` a `<see cref>` in a doc comment 71 lines above it. |
| D3 | bullet 3 names **two** false absolutes | bullet 3 names **two** — and a **third**, textually identical claim exists at the same file's `:75-84` | I deleted all three (§4, bullet 3). Naming two and leaving the third would repeat the exact defect the bullet's last sentence names ("the FIFTH consecutive round in which a false absolute about this mechanism shipped"). `CLAUDE.md`'s rule — *a list of places a correction must reach is the same prose sweep wearing the clothes of a fix* — makes the enumeration obligatory, not optional; the enumeration is in §8. |

The brief's warning about `:474-476` was right and worth the words: the line looks like ordinary doc-comment prose about comments and raw strings, and it **is** one of the two claims to delete. I read what the bullet pointed at, not what sat at the line.

---

## 2. Bullet by bullet

### Bullet 1 — anchor to THE invocation, not to its shape

> *"collect the file's `<HostType>.<HostMethod>` invocations first, require EXACTLY ONE, and read the argument list of that node. Deriving the population from the same node also closes the unit mismatch with `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk`, whose unit is the per-file multiset of `configure*` names and which the decoy preserves exactly"*

New `FindTheHostInvocation(root, relativePath, hostType, hostMethod)`:

1. collects every `InvocationExpressionSyntax` in the file whose **callee renders as** `<HostType>.<HostMethod>` (bullet 2's rule);
2. asserts `matches.Length > 0` — the composition root still calls its host at all;
3. asserts `matches.Length == 1`, naming the **line numbers** of every match;
4. returns that node.

`AssertDelegatingArguments` resolves the node once per file and `ExtractNamedArgument` now reads `hostInvocation.ArgumentList.Arguments` — it no longer *searches* for an argument, so there is no candidate set for a decoy to join. The old per-argument shape filter `IsArgumentOfInvocation` (D16's fix) is **deleted**, not kept alongside: a shape test is exactly what a same-shaped decoy satisfies.

**The unit mismatch, closed as the bullet describes.** `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk` now derives each file's discovered argument names from the **same node**, so the decoy that preserves the per-file multiset (`configure` positional on the decoy, `configureHealth:` moved onto it) fails it. Measured both ways in §5: arm **A2** is that exact decoy — **2/2 green** on the pre-id-86 guard, **2/2 red** on this one.

**One addition beyond the bullet's letter, inside its intent — the stray check.** The population test also asserts that every `configure*:`-named argument **anywhere in the file** is one of the host call's own. Without it, `src/Seed/Program.cs` (no host invocation, expected zero arguments) would be checked by an empty list matching an empty table — a comparison that cannot fail — and the D16 family (an argument bound to an unrelated local function while the real call is intact) would be reported by nothing. Armed as **A8**, which fails the population test while the per-service test stays green, proving the two checks are independent.

### Bullet 2 — match the host expression by rendered-suffix on a member boundary

> *"the rule `TargetMatches` already uses for targets — so a fully-qualified or aliased host call stops being a FALSE RED (measured: a fully-qualified host call today fails, and the message misdiagnoses it as a missing argument)"*

`FindTheHostInvocation` compares `NormalizeExpressionText(invocation.Expression)` against `hostType + "." + hostMethod` through the **existing** `TargetMatches` — equal, or ends with `"." + expected` on a member boundary. One rule now governs both halves of the call (the callee and the delegating argument's target).

**The bullet's parenthetical is a measurement, so I re-measured it rather than citing it** (arm **A9-control**, §5): `OrderToCash.Billing.BillingHost.CreateBuilder(...)` — correct wiring — on the pre-id-86 guard fails with

```
Could not find a 'configure:' named argument passed to BillingHost.CreateBuilder(...) in src/Billing/Program.cs.
```

which is the misdiagnosis the bullet describes, verbatim: the host call was there and fully wired; only the *qualification* differed. On this guard the same file is **2/2 green** (arm **A9**).

**The boundary still has teeth, and that is a separate measurement** (arm **A10**): a decoy called through a `using` **alias** of the same type, `MyBillingHost.CreateBuilder(...)`, is **not** admitted — `"MyBillingHost.CreateBuilder"` does not end with `".BillingHost.CreateBuilder"`. The discriminator is in the message: the guard reported *"Could not find a `configureHealth:` named argument on the `BillingHost.CreateBuilder(...)` call at `src/Billing/Program.cs:14`"* (one host invocation found — the real one), **not** *"Found 2 … invocations"*. A bare-substring rule would have produced the latter. This is attack 3 (substitute a valid sibling identifier) applied to the callee.

### Bullet 3 — delete the two false absolutes rather than weakening them

Deleted, not softened, in **three** places (§1 D3, §8 for the enumeration):

| Location | The retired claim | What replaced it |
|---|---|---|
| `CompositionRootDelegationWiringTests.cs:474-476` (pre-edit numbering), in `ExtractNamedArgument`'s `<summary>` | *"A comment, a raw string, or a quote nested inside an interpolation hole can never produce an `ArgumentSyntax` node here, because none of them is code."* | Sentence removed. The rewritten summary describes what the method reads (the host node's own argument list) and points at the stray check for the "somewhere else" half. |
| `CompositionRootDelegationWiringTests.cs:75-84` (pre-edit numbering), the class summary's `<list>` | *"a raw string …, a verbatim string … and a quote nested inside an interpolation hole are all LITERAL TOKENS … never argument syntax"* + *"A defeat that relies on either of these two shapes cannot pass this guard, because neither is code."* | The list item that lumped the hole in with literal tokens is gone. A new item names the interpolation hole as **the exception**, says its contents are a genuine expression tree whose `ArgumentSyntax` nodes are as real as any other, cites the measurement, and says what actually rules the decoy out: the anchor, *not* the construct being unreadable. |
| `Directory.Packages.props:26-28` | *"a raw string or an interpolation hole's contents become literal tokens, never reachable as an ArgumentSyntax node, so that half of the defeat class cannot exist"* | Rewritten into what the parser **does** retire (comments; raw and verbatim string tokens) and what it **does not** (the interpolation hole, measured), with the fix named. The manifest comment no longer makes a claim a reader of #9 would inherit as settled. |

Arm **A3** (a decoy host call inside `$"…{ … }"`) is the positive proof that the retired claim was false: on the pre-id-86 guard it is **2/2 green** with Billing's health probes unwired; on this guard it fails with *"Found 2 'BillingHost.CreateBuilder(...)' invocations … at line(s) [13, 18]"* — line 18 **is** the interpolation hole. The guard reads the hole's contents as code, which is precisely what the deleted sentences denied.

### Bullet 4 — reword `ParseFile`'s message to name what it actually rejects

> *"ANY directive trivia, `#pragma` and `#region` included — rather than calling them conditional-compilation directives (measured false red with a misnamed category)"*

Behaviour unchanged (`trivia.IsDirective`, which is true of every preprocessor directive). The message now reads *"contains N preprocessor directive(s) […] — a composition root under this guard may carry NO directive of ANY kind: not only the conditional-compilation family (#if/#elif/#else/#endif), but #region/#endregion, #pragma, #nullable, #line, #warning/#error and #define/#undef too, all of which this check rejects"*, keeps the D15 reason for the `#if` family, and gives the reason the rest are rejected with it. The `<remarks>` and the class summary's D15 paragraph were corrected the same way (both said "conditional-compilation").

Armed as **A7** with a `#region`/`#endregion` pair — the category the old message misnamed. Verbatim message in §5.

### Bullet 5 — arm each against the CLASS, not the reported literal

Ten mutations plus four control runs, §5. For the anchor specifically the bullet's three minimums are **A1** (same-shaped decoy), **A2** (positional-argument decoy — the filed exploit), **A3** (decoy inside an interpolation hole), with **A8** (argument bound to an unrelated call) and **A10** (alias-qualified decoy) added because the class is "an argument that looks like it belongs to the host call", not "a second `BillingHost.CreateBuilder`".

### Bullet 6 — `AssertConnectionStringProvenance` checks the DECLARATION'S INITIALIZER and never the variable's last write

> *"Close it by following assignments, or by checking the USE rather than the name."*

Closed by following assignments, which makes the two existing halves compose into the property rather than sit adjacent to each other:

1. **exactly one declarator** of that name (was `SingleOrDefault`, which threw an unnamed `InvalidOperationException` on two; now an assertion that names the count and the lines);
2. the declarator's initializer is literally `<Service>SeedWriter.ConnectionString()` (unchanged);
3. **no later write of any kind** — no `AssignmentExpressionSyntax` whose left-hand **subtree** contains that identifier (so `=`, `+=`, `??=` and a tuple deconstruction `(x, _) = …` are all caught), and no `ref`/`out` argument passing it.

With (1)–(3) holding, the declaration's initializer **is** the value `AssertOpenDbPairing` sees handed to `OpenDb`. The property the row asserts — *the value that reaches `<Service>SeedWriter.OpenDb` is that same service's `ConnectionString()`* — is now what the test proves, instead of two facts about two lines.

Armed as **A11** (plain assignment — the bullet's exact mutation), **A12** (tuple deconstruction — the same defect by a different syntax) and **A13** (the D18 initializer mutation, proving the older half still fires). The **control** is the decisive one: with A11 on disk, the pre-id-86 guard is **1/1 green** and `Seed.UnitTests` is **44/44 green** — reproducing the bullet's filed measurement exactly, Orders fixtures headed into the Billing database with both suites clean.

---

## 3. Instrument-premise enumeration

I did not replace the instrument (Roslyn stays), but I changed **how matching works**, and `CLAUDE.md` requires the new premises to be written down and armed in the same pass. Six premises, each with the arm that can falsify it:

| # | New premise | Falsifiable by | Result |
|---|---|---|---|
| P1 | Every guarded `Program.cs` contains **exactly one** invocation whose callee renders as its host call | the whole suite — if any of the six had two, the per-service test and the population test would both fail | 50/50 green; a second one is A1/A2/A3 and it fails by name |
| P2 | Rendered-suffix matching **accepts** legitimate qualification (`global::`, namespace, extern alias) | A9 (fully-qualified call must be GREEN) | green; the same input is RED on the old instrument |
| P3 | Rendered-suffix matching **rejects** a non-boundary near-match (`MyBillingHost.CreateBuilder`) | A10 — and the discriminator is *which* message appears | "Could not find … on the BillingHost.CreateBuilder(...) call", not "Found 2" |
| P4 | `hostArguments.Contains(argument)` identifies nodes by **reference**, so the host call's own arguments are never reported as strays | the green run itself: if reference equality did not hold, all 19 would be strays and every file would fail | 50/50 green, and A8 shows a real stray IS reported |
| P5 | `SyntaxTrivia.IsDirective` is true of far more than `#if` — so the message must say so | A7 (`#region`/`#endregion`) | RED, message names `#region` and the whole family |
| P6 | Walking an assignment's **left subtree** catches deconstruction as well as plain assignment | A11 (plain) and A12 (tuple) | both RED, each message quoting the offending line |

**Two bounds I did not close, stated as bounds rather than as absolutes** (the file says so too):

- a `using` alias that **renames** the type (`using H = …BillingHost; H.CreateBuilder(…)`) and a generic `CreateBuilder<T>` both render as something the suffix rule does not accept, so correct code written that way would be a **false RED** — the safe direction, and the same symbol-binding boundary D17 already states. A10 exercises exactly this rendering and confirms it.
- a composition root that passed `configure` **positionally on the real call** would also be a false red. That is unchanged behaviour from rounds 1–5 (the table's unit has always been the *named* argument) and is not something id 86 asks to change; recording it because it is the one remaining shape where correct code fails this guard.

---

## 4. What changed in the guard, as a diff summary

- `FindTheHostInvocation` — **new**; the anchor.
- `ExtractNamedArgument` — signature changed from `(SyntaxNode root, …)` to `(InvocationExpressionSyntax hostInvocation, …)`; reads, does not search.
- `IsArgumentOfInvocation` — **deleted** (superseded; a shape filter is the defect).
- `IsConfigureNamedArgument`, `DescribeArgument`, `LineOf` — **new** helpers (the unit, and line numbers in every message).
- `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk` — derives from the host node; adds the stray check; `Assert.Equal(19, grandTotal)` replaced with an `Assert.True` whose message names the claim and the per-service breakdown (id 82's rule: a bare `Assert.Equal` prints two numbers and names nothing).
- `AssertConnectionStringProvenance` — declarator count assertion; later-write and `ref`/`out` checks.
- `AssignsToVariable` — **new**.
- `ParseFile` — message reworded; behaviour unchanged.
- Doc comments — three false absolutes deleted, D15/D16 paragraphs corrected, a "Fix round 6 — id 86" paragraph added stating what the anchor is and why the question's *order* is the fix.

---

## 5. Arming table — the class, not the literal

Protocol per arm: `cp` backup → write the variant → `dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental` (which rebuilds all nine referenced `src/` projects) → run the named test(s) → record the message verbatim → restore from the backup → `cmp` against the backup. No `git checkout`, no `git stash`, no git command that writes the tree. Every build below reported **0 Warning(s), 0 Error(s)** — each mutation is a *compilable* defeat, which is what makes it a defect rather than a typo. Restores: `cmp` clean on all ten (`ARM <id> RESTORE OK` printed by the runner each time), plus a final re-read of the mutated lines and a forced `--no-incremental` rebuild before the confirming green run.

Named tests: `B` = `BillingProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToBillingProgramConfiguration`, `P` = `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk`, `S` = `SeedRunner_OpensEachWritersDbContext_WithThatWritersOwnConnectionString_NeverASiblings`.

| Arm | Class member (defeat-list #) | Mutation | Tests | Result |
|---|---|---|---|---|
| A1 | same-shaped decoy | real call drops `configureHealth:`; a second, discarded `BillingHost.CreateBuilder(args, configure: …, configureHealth: …)` with every argument named | B, P | **RED 2/2** |
| A2 | **positional-argument decoy — the filed exploit** (#3-adjacent) | as A1 but the decoy passes `configure` **positionally**, so the file's `configure*` name multiset is unchanged | B, P | **RED 2/2** |
| A3 | decoy inside an **interpolation hole** (#6) | as A2 but the decoy lives inside `$"[billing] wiring { … }"` | B, P | **RED 2/2** |
| A4 | drop an OPTIONAL element entirely (#7) | real call drops `configureHealth:`, no decoy | B, P | **RED 2/2** |
| A5 | shadow from a **comment** (#4) | A4 + `// configureHealth: BillingProgramConfiguration.ConfigureHealth` | B, P | **RED 2/2** |
| A6 | shadow from a **raw string** (#6) | A4 + a `"""…"""` raw string containing the argument text, whose `.Length` is printed | B, P | **RED 2/2** |
| A7 | **directive** category (#5) + bullet 4's message | `#region composition` / `#endregion` around the real call | B, P | **RED 2/2** |
| A8 | argument bound to an unrelated call (D16 family) — real call **intact** | `DescribeHealthWiring(configureHealth: …)` local function | B, P | **P RED, B GREEN** (independent checks) |
| A9 | bullet 2's false red — must be **GREEN** | host call written fully qualified, wiring otherwise correct | B, P | **GREEN 2/2** |
| A10 | substitute a valid sibling identifier (#3) on the **callee** | decoy through `using MyBillingHost = …BillingHost;` | B, P | **RED 2/2**, and by the *"Could not find"* branch — proving the member boundary held |
| A11 | bullet 6 — the **last write** | `ordersConnectionString = BillingSeedWriter.ConnectionString();` one line below a correct declaration | S | **RED** |
| A12 | bullet 6 — the same defect by deconstruction | `(ordersConnectionString, _) = (BillingSeedWriter.ConnectionString(), 0);` | S | **RED** |
| A13 | bullet 6 — the older half still fires (D18) | declaration's initializer repointed to the sibling | S | **RED** |

### Verbatim failure messages

**A1 / A2 / A3** (identical message; A3's second line number is the interpolation hole):

```
Found 2 'BillingHost.CreateBuilder(...)' invocations in src/Billing/Program.cs at line(s) [13, 18] — expected exactly ONE live host call. A second invocation of the same host method is the id 86 decoy shape: it can carry the delegating arguments the real call no longer passes while every name still appears in the file. If a composition root ever legitimately needs two host calls, this guard must be re-designed, not relaxed.
```

**A4 / A5 / A6** (both tests, two distinct messages):

```
Could not find a 'configureHealth:' named argument on the BillingHost.CreateBuilder(...) call at src/Billing/Program.cs:13 — the argument was dropped from the host call, or moved to another call.
```
```
src/Billing/Program.cs: the table expects [configure, configureHealth, configureTelemetry] on the host call, the host call on disk passes [configure, configureTelemetry] — a delegating argument was added, removed, renamed, duplicated or MOVED OFF the host call without updating this test's table.
```

**A7**:

```
src/Billing/Program.cs contains 2 preprocessor directive(s) [#region composition, #endregion] — a composition root under this guard may carry NO directive of ANY kind: not only the conditional-compilation family (#if/#elif/#else/#endif), but #region/#endregion, #pragma, #nullable, #line, #warning/#error and #define/#undef too, all of which this check rejects. The #if family is why the rule exists (D15: this parser is fed no preprocessor symbols while the build defines DEBUG, and passing them would only relocate the disagreement to a symbol nobody passed); the rest are rejected with it because a twenty-line composition root needs none of them and admitting any would put this guard back in the business of deciding which text is live. Remove the directive(s).
```

**A8** (population test only; the Billing test passed, as designed):

```
src/Billing/Program.cs: 1 'configure*:' named argument(s) are bound to something OTHER than this file's one host call — ['configureHealth:' at line 19]. A composition root passes its configuration delegates to its host and to nothing else; an argument bound elsewhere (a decoy invocation, a local function, an expression inside an interpolation hole) is the id 86 defeat shape, not legitimate wiring.
```

**A10** — the message that proves the boundary (note `:14`, the single real call; a substring rule would have said *"Found 2"*):

```
Could not find a 'configureHealth:' named argument on the BillingHost.CreateBuilder(...) call at src/Billing/Program.cs:14 — the argument was dropped from the host call, or moved to another call.
```

**A11**:

```
'ordersConnectionString' is written again after its declaration — [line 22: 'ordersConnectionString = BillingSeedWriter.ConnectionString()']. Its declaration says 'OrdersSeedWriter.ConnectionString()', but the value that reaches OrdersSeedWriter.OpenDb(...) is whatever was written LAST, so a sibling writer's connection string can be assigned over a correctly-declared local (id 86, bullet 6). A seed connection-string local is assigned exactly once, at its declaration.
```

**A12**:

```
'ordersConnectionString' is written again after its declaration — [line 22: '(ordersConnectionString, _) = (BillingSeedWriter.ConnectionString(), 0)']. Its declaration says 'OrdersSeedWriter.ConnectionString()', but the value that reaches OrdersSeedWriter.OpenDb(...) is whatever was written LAST, so a sibling writer's connection string can be assigned over a correctly-declared local (id 86, bullet 6). A seed connection-string local is assigned exactly once, at its declaration.
```

**A13**:

```
'ordersConnectionString' is initialized from 'BillingSeedWriter.ConnectionString()', expected 'OrdersSeedWriter.ConnectionString()' — a sibling writer's connection string was assigned under this writer's own variable name.
```

### The controls — a change of KIND, measured, not a citation

`CLAUDE.md` asks that a fix be proved by a change of kind rather than of probability. Each control put the **pre-id-86 guard** (`git show HEAD:tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs`, read-only) on disk with the same mutated input, so the only variable is the guard:

| Input | Pre-id-86 guard | This guard |
|---|---|---|
| A2 — positional-argument decoy | **PASSED 2/2** (the false green id 86 was filed for) | **FAILED 2/2** |
| A3 — decoy in an interpolation hole | **PASSED 2/2** (falsifies bullet 3's two absolutes directly: the hole produced the `ArgumentSyntax` node the guard read) | **FAILED 2/2** |
| A9 — fully-qualified host call, correct wiring | **FAILED**: `Could not find a 'configure:' named argument passed to BillingHost.CreateBuilder(...) in src/Billing/Program.cs.` (bullet 2's misdiagnosed false red) | **PASSED 2/2** |
| A11 — sibling assigned over a correct declaration | **PASSED 1/1**, and `Seed.UnitTests` **44/44** | **FAILED** |

**One honest note about how the control was built.** HEAD's copy of the guard does not compile today: batch D5 (id 78, `GenerateDocumentationFile=true`, landed uncommitted earlier today) fixed two doc-comment defects in this very file — an unclosed `<b>` (CS1570 ×2) and an ambiguous `<see cref="CSharpSyntaxTree.ParseText"/>` (CS0419) — and those fixes are in the working tree, not in HEAD. My first control attempt therefore failed to build, and I briefly mis-suspected a pre-existing broken build. It was not: `progress/impl_batch_d5_doc_comment_targets_and_dispatch_clamp.md:112-113` records both fixes against this file. I applied **those two doc-comment fixes and nothing else** to the HEAD copy before using it as the control, so the control differs from the current file only in the id 86 logic. Recording the false start because the first reading of the evidence looked like a finding and was not one.

---

## 6. Defeat list — all ten, stated

`CLAUDE.md`'s ten attacks, run against the guard as it now stands:

| # | Attack | Ran? | Evidence / why it cannot bite |
|---|---|---|---|
| 1 | Delete the behaviour | **Yes** | A4 — the argument removed outright, RED on both tests |
| 2 | Corrupt a payload field the test supplied | **Yes, in the form this guard has** | A13 (the initializer's *value* repointed to a sibling) and, for the delegating argument, the pre-existing `TargetMatches` assertion; there is no wire payload here — the "field" is the rendered target of each argument |
| 3 | Substitute a valid sibling identifier | **Yes, twice** | A10 substitutes the **callee** (`MyBillingHost` for `BillingHost`); A11/A12/A13 substitute a **sibling writer** (`BillingSeedWriter` for `OrdersSeedWriter`). Both sibling families are real (`*Host`, `*SeedWriter`), and both failures name what was intended to break — not a default-value failure |
| 4 | Shadow the pattern from a comment or string literal | **Yes** | A5 (comment) — RED, i.e. the comment did not satisfy the guard |
| 5 | Hide the real thing in a dead region | **Yes** | A7 — `#region` rejected with the (now correctly named) directive message. `#if false` is rejected by the same `IsDirective` check, armed at id 68 round 5 and unchanged here |
| 6 | Hide it in a raw or verbatim string | **Yes** | A6 (raw string) — RED. A3 covers the interpolation hole, which is **not** in this category and is now documented as the exception |
| 7 | Drop an OPTIONAL element entirely | **Yes** | A4 — `configureHealth` is an optional parameter; absence is caught by reading the host node's own argument list |
| 8 | Compare a literal to a literal | **Yes** | the population test's file set and argument names are compared against a **literal table** while the discovered side is re-derived from disk through a real parse; A1–A8 all move the disk side and all fail. The expected set is the literal; the rest is derived by subtraction |
| 9 | Satisfy the closer half and leave the premise half stale | **Yes** | the three deleted absolutes ARE the stale premise halves of this guard; §8 enumerates on the wording of the **retired** claim, not the new one |
| 10 | Let a build-output copy join the population | **Not re-run** | `IsUnderBuildOutputDirectory` and the file-set assertion are byte-identical to the version armed at id 68 round 3 (D9) and re-measured at its round-2 re-review with a *correct* copy under `bin/…/publish/` while the real file was wrong (still RED). Nothing in this change touches file discovery — only what is read **inside** a discovered file |

---

## 7. Ported-idiom ledger

`sdd: false`, so the ledger lives here. **Not "none owed"** — I looked first, and #7 has both mechanisms.

### Row 1 — "the one live call in the composition root is the one the guard checks"

| | |
|---|---|
| **#7 relied on** | Whole-file **regex over `main.ts` text**, with the call count and the option asserted **separately**. `apps/projector/src/projector-consumes-only.spec.ts:85-88`: `const connectMicroserviceCalls = mainTs!.content.match(/\.connectMicroservice[<(]/g) ?? []; expect(…).toHaveLength(1); expect(mainTs!.content).toMatch(/transport:\s*Transport\.KAFKA/);`. The same shape in `apps/billing/src/billing-consumes-no-facts.spec.ts:75-78` and `apps/notifications/src/notifications-consumes-only.spec.ts:97-100`. `apps/projector/src/main-kafka-options.spec.ts:3-4` states the convention outright: *"Pure text scan over main.ts, same discipline every structural guard in this codebase uses."* |
| **So the property was supplied by** | **Nothing.** #7 asserts "exactly one call exists" and "the right option text exists **somewhere in the file**" as two unrelated facts. A second `connectMicroservice` would break the count (a true red, by luck), but the option assertion is never tied to the call — the exact id 86 defect, live in #7, unguarded, in three services. A comment carrying `transport: Transport.KAFKA` satisfies it. |
| **In #8 supplied by** | `FindTheHostInvocation` — exactly one invocation **node**, arguments read from that node — plus the population test's stray check. Not text, and not two separate facts. |
| **Guard (named)** | `BillingProgramCs_DelegatesConfigureConfigureTelemetryAndConfigureHealth_ToBillingProgramConfiguration` and `ThePopulationTableMatchesEveryDelegatingArgumentFoundOnDisk`, armed A1/A2/A3/A8/A10 above, with the pre-id-86 control showing the same inputs green. |
| **For #9** | This is a **strengthening**, not a port: #9 (FastAPI) inherits the temptation to scan `main.py` as text. If it does, it inherits both halves of the defect — an unanchored assertion and an instrument that cannot tell code from a comment. Python's `ast` module is the equivalent of the Roslyn move, and the anchor (find *the* call node, read *its* keywords) is the part that matters more than the parser. |

### Row 2 — "each seed writer opens its own database"

| | |
|---|---|
| **#7 relied on** | **Encapsulation** — the connection string never becomes a value in the orchestrator's scope. `apps/seed/src/index.ts:34-36` is `const ordersHandle = await openOrdersDb();` / `openFulfillmentDb()` / `openBillingDb()`, each zero-argument; `apps/seed/src/writers/orders-db.writer.ts:34-39` is `export async function openOrdersDb(config: OrdersDbConfig = loadOrdersDbConfig())`, i.e. the config is a **default parameter** resolved inside the writer's own module. |
| **So the property was supplied by** | The **shape of the call**, not by a test: there is no local to mis-assign and no argument to swap, so #7's "Orders fixtures into the Billing database" defect is not constructible without editing the writer module itself. Guarded by nothing — correctly, because nothing could break. Enumerated: `find … -name '*.spec.ts' -print0 \| xargs -0 grep -n "openOrdersDb\|openBillingDb\|openFulfillmentDb\|loadOrdersDbConfig\|loadBillingDbConfig"` returns **zero hits** — no #7 test guards this pairing, because #7 has no pairing to guard. |
| **In #8 supplied by** | Nothing structural — `SeedRunner.RunAsync` **hoists** all three connection strings into locals and passes them to `OpenDb(...)`. The translation *created* the substitution surface. It must therefore be supplied by a guard, and this is the third consecutive line at which that guard was incomplete (id 56's D1 → the env key; id 68's D18 → the declaration's initializer; id 86 → the last write). |
| **Guard (named)** | `SeedRunner_OpensEachWritersDbContext_WithThatWritersOwnConnectionString_NeverASiblings`, now asserting one declarator + its initializer's provenance + **no later write of any kind**; armed A11/A12/A13, control A11 showing pre-fix green on both suites. |
| **For #9** | Either keep #7's shape (each writer resolves its own config internally — no surface, no guard needed) **or** hoist and guard all three properties. The cheap correct answer is the first; #8 took the second and has now paid for it three times. That is the row's whole message. |

### Guard enumeration — #7's tests for the ported mechanism

Rule: *when you port a mechanism, port its guards — enumerate #7's tests, not only its source.* Content-based (not filename-based), classified per **assertion**:

```
$ cd order-to-cash-nestjs && find . -path ./node_modules -prune -o -type f -name '*.spec.ts' -print0 \
    | xargs -0 grep -n "MAIN_TS\|mainTs\|relativePath === 'main.ts'"
```

| #7 assertion | Classification |
|---|---|
| `main-kafka-options.spec.ts:15-16` `toMatch(/fromBeginning:\s*true/)`, `not.toMatch(/…false/)` | **N/A** — a Kafka option, not a delegation argument; #8 guards `fromBeginning`-equivalents in the Projector consumer tests |
| `main-kafka-options.spec.ts:22-30` ordering of three calls by `indexOf` | **N/A** — an ordering property; #8's composition roots have no such ordering |
| `main-kafka-options.spec.ts:35` `toMatch(/groupId:\s*kafkaConfig\.groupId/)` | **Ported in strengthened form** — "the argument is bound to the right value" is `TargetMatches` on each delegating argument, but anchored to the call rather than whole-file |
| `projector-consumes-only.spec.ts:85-86` count of `connectMicroservice` calls `toHaveLength(1)` | **Ported in strengthened form** — "exactly one live call", now counted as syntax nodes (`FindTheHostInvocation`) instead of regex matches over text |
| `projector-consumes-only.spec.ts:87` `toMatch(/transport:\s*Transport\.KAFKA/)` | **Deliberately NOT ported as written** — this is the unanchored whole-file assertion that id 86 exists to retire. #8 asserts the equivalent **on the call node** |
| `projector-consumes-only.spec.ts:88` `not.toContain('Transport.NATS')` | **N/A** — #8's type system makes the wrong-service substitution a compile error (class doc, "What is unchanged") |
| `billing-consumes-no-facts.spec.ts:75-78, 94` | Same three shapes as above, same classifications |
| `notifications-consumes-only.spec.ts:97-100` | Same three shapes as above, same classifications |

No #7 guard was dropped in translation: the two that carry over (call count, argument value) are present and stronger; the one not carried over is carried over *differently*, deliberately, and that difference is this entry.

---

## 8. Negative claims, as search results

### The false-absolute sweep — enumerated on the wording of the claim being RETIRED

`CLAUDE.md`: *enumerate on the wording of the claim being retired, not the claim being written.* The retired claim's distinctive term is "interpolation hole":

```
$ find . -type f \( -name '*.cs' -o -name '*.props' -o -name '*.md' -o -name '*.json' -o -name '*.csproj' \) \
    -not -path '*/bin/*' -not -path '*/obj/*' -not -path './.git/*' -not -path '*/node_modules/*' -print0 \
  | xargs -0 grep -nI -i -e 'interpolation hole' -e 'interpolated hole'
```

**31 hits before my edits, 32 after** — excluding this record itself, whose own mentions would otherwise make the population move every time I edited the classification (`-not -name 'impl_batch_d6_…md'`). Counted, not estimated:

```
$ … -not -name 'impl_batch_d6_delegation_guard_anchors_the_live_call.md' … | wc -l          → 32   (after)
$ 26 unchanged-file hits + (HEAD Directory.Packages.props: 2) + (HEAD guard file: 3)        → 31   (before)
```

The +1 is `Directory.Packages.props` going from two mentions to three: the rewritten comment names the construct once more, to say the old claim about it was false. Every hit classified, by file, with the count per file so a missed one shows as an unclassified line rather than as an absent sentence:

| File (hits after / before) | Lines | Classification |
|---|---|---|
| `tests/Architecture.Tests/CompositionRootDelegationWiringTests.cs` (3 / 3) | now `:54`, `:82`, `:358` | `:54` is the *history* of the defeat — **true as written**, left. `:82` is the **new** list item naming the hole as the exception. `:358` is the stray check's failure message. The two pre-edit hits at `:77-78` were the `<list>` item's **FALSE ABSOLUTE — deleted** (bullet 3's third copy, §1 D3) |
| `Directory.Packages.props` (3 / 2) | now `:26`, `:34`, `:40` | pre-edit `:26-27` was the **FALSE ABSOLUTE — deleted** (bullet 3's second copy). The three current hits are the rewritten comment: the defeat history, "what it does NOT retire, measured", and the sentence recording that the old wording was false |
| the `ExtractNamedArgument` summary (pre-edit `:474-476`) | — | **FALSE ABSOLUTE — deleted** (bullet 3's first copy). **Not matched by this grep at all**: it says "interpolation" and "hole" on different lines, and a line-oriented grep for a two-word phrase cannot see it wrapped. Found by the bullet's citation. Recorded because it is the sweep's one structural blind spot, and the bullet — not the sweep — is what caught it |
| `feature_list.json` (3 / 3) | `:1271`, `:1273`, `:1276` | the entry itself — not mine to edit |
| `CLAUDE.md` (1 / 1) | `:339` | conventions — correct as written, out of scope |
| `progress/review_composition_root_delegation_and_design_time_factories.md` (10 / 10) | `:462, 551, 808, 810, 829, 831, 833, 914, 918, 961` | the review that filed this entry — an archived record of what was measured; **left unchanged by convention** |
| `progress/impl_composition_root_delegation_and_design_time_factories.md` (2 / 2) | `:942`, `:1033` | id 68's implementation record — archived, left |
| `progress/history.md` (3 / 3) | `:2491`, `:2504`, `:2508` | archived phase history — left; `:2508` already records the residue correctly |
| `progress/current.md` (2 / 2) | `:1623`, `:1627` | `:1623` is history — true. **`:1627` is a LIVE copy of the retired claim**: *"raw strings, verbatim strings and interpolation holes are literal tokens, never arguments, so D13 and round 2's defeats cannot exist"*. `current.md` is the phase's working document, read by the next phase, not an archived review. **Out of my file scope** (the brief bounds me to what the bullets require plus this record), so I did not edit it — flagged for the coordinator, §11 |
| `specs/fulfillment_stock/design.md` (1 / 1), `specs/billing_credit/design.md` (1 / 1), `src/{Billing,Fulfillment,Orders}/Infrastructure/Outbox/OutboxRelay.cs` (3 / 3) | `:421`, `:600`, `:99-100` | a different subject entirely (`FromSqlInterpolated` parameterisation) — unrelated, correct, left. `specs/` is read-only regardless |
| `progress/impl_batch_d6_delegation_guard_anchors_the_live_call.md` | this record | **excluded from the population** — self-reference; see the command above |

### Test-matrix rows

```
$ grep -n "CompositionRootDelegation\|SeedRunner_Opens" specs/shared/test-matrix.md
(no output)
```

No `R<n>` row references these tests: id 86 is a guard-hardening backlog entry with no EARS requirement behind it, so no matrix row is owed and none was changed. (`specs/shared/` is read-only in any case.)

---

## 9. Defeat-list attacks I did NOT run, and why they cannot bite

Stated explicitly per `CLAUDE.md`, so the reviewer can check it in seconds rather than find the hole in a round: only #10 was skipped, with the reason in §6's table (file discovery is byte-identical to the previously-armed version; this change reads *inside* discovered files only). Attacks #1–#9 were all run, above.

---

## 10. Suite counts, reconciled

Baseline handed to me (after batch D5): `./quality.sh` clean at 18 projects, **2 042 passed, 0 failed, 0 skipped**; `Architecture.Tests` **50**.

- `Architecture.Tests` after this change: **50 passed, 0 failed, 0 skipped** — identical. This entry adds **no new `[Fact]`**: it hardens three existing tests, so the arithmetic is 50 − 0 + 0 = 50 and the solution total is expected to be **2 042 exactly**.
- Full `./quality.sh` result: see §12 (run at the end of the session, after every mutation was restored and a forced `--no-incremental` rebuild).

A count that does not reconcile is a finding, not a footnote — if the figure in §12 is not 2 042, the discrepancy is explained there or the number is withdrawn.

---

## 11. Disclosures — things I could not do, or did not do

1. **`progress/current.md:1627` still asserts the retired absolute.** *"raw strings, verbatim strings and interpolation holes are literal tokens, never arguments, so D13 and round 2's defeats cannot exist."* It is false in the same way as the three deleted copies, and `current.md` is a **living** document rather than an archived review. Bullet 3 names two locations; the brief bounds me to the bullets plus this record. **Recommended to the coordinator:** correct that sentence in the same pass that closes id 86 — one line, and it is the copy the next phase reads first.
2. **Id 86's `status` was already `in_progress`** when I read `feature_list.json`, not `pending` as the brief says. I made no change to the file.
3. **Two remaining false-red shapes**, both stated as bounds in the guard itself (§3): a type-renaming `using` alias, and a generic `CreateBuilder<T>`. Both need symbol binding, which is D17's disclosed boundary; both fail in the safe direction.
4. **A positional `configure` on the *real* call** would also be a false red. Unchanged from rounds 1–5 and not in scope for id 86, but it is the one shape where correct code fails this guard, so it is written down rather than left to be rediscovered.
5. **D17 is untouched**, as the entry requires: a shadowing type that makes byte-identical call text mean a no-op remains a stated bound of any syntax-only instrument.
6. **A `git status` discrepancy I could not explain, recorded rather than glossed.** My session's opening snapshot listed `src/Billing/Program.cs` as modified (` M`). When I took my arming backup of it — **before** any mutation — it was **byte-identical to HEAD**, and it is byte-identical to HEAD now; the same is true of `src/Seed/Presentation/SeedRunner.cs`. Verified with `diff <(git show HEAD:<path>) <backup>` (read-only) for both. So no uncommitted change of anyone's was lost by this arming: whatever the snapshot saw was already gone before I read the file. The likeliest explanation is that the snapshot predates the end of an earlier batch in the same working tree; I could not confirm it, so it is here as an open observation rather than a conclusion. **Net change under `src/`: none** — `git diff --stat src/Billing/Program.cs src/Seed/Presentation/SeedRunner.cs` is empty.

## 12. Final verification

- `./init.sh` — exits 0 (run before starting; re-run at the end, §12 below).
- Forced rebuild after the last restore: `dotnet build tests/Architecture.Tests/Architecture.Tests.csproj --no-incremental` → **0 Warning(s), 0 Error(s)**; confirming run `dotnet test --no-build` → **50/50 passed**.
- `./quality.sh` (format + build + all 18 projects + coverage), run after every restore:

```
[OK]    dotnet format --verify-no-changes: clean
[OK]    dotnet build: succeeded
[OK]    dotnet test: all tests passed
[OK]    quality.sh finished
```

**2 042 passed, 0 failed, 0 skipped, across 18 test projects** — reconciles **exactly** with the D5 baseline, as predicted in §10 (this entry adds no test and removes none). Counted, not remembered:

```
$ grep -oE "Passed: +[0-9]+" quality.txt | awk '{s+=$2} END {print s}'   → 2042
$ grep -oE "Failed: +[0-9]+" quality.txt | awk '{s+=$2} END {print s}'   → 0
$ grep -oE "Skipped: +[0-9]+" quality.txt | awk '{s+=$2} END {print s}'  → 0
$ grep -cE "^Passed!" quality.txt                                        → 18
```

Per-project, for the four the brief named: Gateway.UnitTests **237**, Billing.UnitTests **262**, Orders.UnitTests **498**, Fulfillment.UnitTests **146**, `Architecture.Tests` **50** — all unchanged.

- `./init.sh` after all work: **exit 0**, "environment and state are coherent".
