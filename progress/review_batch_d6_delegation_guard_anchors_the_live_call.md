# review — batch D6, backlog id 86: the delegation guard anchors THE live host call

**Verdict: APPROVED.** Scoped review: the full suite was **not** re-run (batch D5's `./quality.sh` baseline of 2 042 was re-used as the leader directed). What I ran myself is below — seven mutations, four of them as two-directional controls against the pre-id-86 guard rebuilt on disk.

## What I ran (independent, not the implementer's logs)

Protocol per probe: `cp` backup → write variant → `dotnet build` (compile-validity) → run the named test(s) `--no-build` → restore from backup → `cmp` → forced `--no-incremental` rebuild → confirming green. No git command that writes the tree was used at any point.

| Probe | Input | **Old guard** (HEAD + only D5's two doc fixes) | **New guard** |
|---|---|---|---|
| M1 | decoy host call inside an **interpolation hole**, real call loses `configureHealth:` | **PASSED 9/9** | **FAILED 2/2** — *"Found 2 'BillingHost.CreateBuilder(...)' invocations in src/Billing/Program.cs at line(s) [4, 9]…"* (line 9 **is** the hole) |
| M2 | **positional-argument decoy** (the filed exploit) | **PASSED 9/9** | **FAILED 2/2**, same message |
| M3 | host call written **fully qualified**, wiring correct | **FAILED 1/9**: *"Could not find a 'configure:' named argument passed to BillingHost.CreateBuilder(...) in src/Billing/Program.cs."* — bullet 2's misdiagnosed false red, reproduced verbatim | **PASSED 9/9** |
| M4 | `ordersConnectionString = BillingSeedWriter.ConnectionString();` one line below a correct declaration | **PASSED 9/9**, and **`Seed.UnitTests` 44/44 PASSED** with Orders fixtures headed for the Billing DB | **FAILED**, message quotes the offending line and names id 86 bullet 6 |
| M6 | the same defect via `TryOverride(out ordersConnectionString);` — a claim the record makes and did **not** arm | not run | **FAILED** — *"…written again after its declaration — [line 30: 'out ordersConnectionString']"* |
| M7 | `#region composition` / `#endregion` around the real call | not run | **FAILED 2/2**, message names `#region/#endregion` and the whole directive family (bullet 4) |

All four controls therefore run **both directions**, and each is a change of kind, not of probability. Restores verified by `cmp` against my backups **and** by `diff` against `git show HEAD:<path>` for both source files; forced `--no-incremental` rebuild then `Architecture.Tests` **50/50**, `Seed.UnitTests` **44/44**.

Two incidental corroborations: the old guard does not compile today for exactly the two reasons the record's "honest note" gives — `CS1570` (an unclosed `<b>` at the *Fix round 5* paragraph, not the one at *What is unchanged*) then `CS0419` on `<see cref="CSharpSyntaxTree.ParseText"/>` — so that disclosure is accurate; and the arming table's thirteen variants and logs exist on disk with coherent timestamps (01:22–01:30), `out/quality.txt` at **01:45**, i.e. after the last restore at 01:30:58.

## Acceptance bullets

| Bullet | Verdict | Evidence |
|---|---|---|
| 1 — anchor to THE invocation | **Met.** `FindTheHostInvocation` (`:647-672`) collects `<HostType>.<HostMethod>` invocations, asserts `>0` then `==1` naming every line, returns the node; `ExtractNamedArgument` (`:689`) now takes that node and **reads** `ArgumentList.Arguments`. A second matching invocation fails by name (M1/M2) — neither tolerated nor silently preferred. The population test derives from the same node (`:319-324`), closing the unit mismatch. | M1, M2 |
| 2 — rendered-suffix on a member boundary | **Met.** `TargetMatches` now governs the callee too; the false red is gone in the direction that matters and the old misdiagnosis reproduced exactly on the old guard. | M3 both ways |
| 3 — delete the two false absolutes | **Met, and the third copy was real.** HEAD carried the identical claim in the class summary at `:76-84` (*"never argument syntax … cannot pass this guard, because neither is code"*) — I read it out of `git show HEAD:` — and it is deleted, replaced by a list item naming the hole as **the exception**. `Directory.Packages.props:34-44` now says what the parser does and does **not** retire. | see enumeration below |
| 4 — `ParseFile`'s message | **Met.** M7's verbatim message names `#region/#endregion`, `#pragma`, `#nullable`, `#line`, `#warning/#error`, `#define/#undef`. | M7 |
| 5 — arm the CLASS | **Met.** Three distinct anchor arms exist as separate variants and logs: `A1.cs` (same-shaped, all-named decoy), `A2.cs` (positional), `A3.cs` (inside `$"…{ … }"`), plus `A8` (stray bound to a local function) and `A10` (alias-qualified callee). I reproduced the A2 and A3 classes myself. | M1, M2 + variants on disk |
| 6 — last write, not the declaration | **Met, and over-delivered.** Declarator-count assertion + initializer provenance + no later write of any kind (`AssignsToVariable` walks the LHS **subtree**, so `=`, `+=`, `??=` and tuple deconstruction are caught) + no `ref`/`out` pass. The `out` half was an unarmed claim; I armed it (M6) and it fires. The filed control is real: old guard 9/9 **and** `Seed.UnitTests` 44/44 green on the mutation. | M4, M6 |

## Bullet 3's enumeration, re-derived

Path-excluding, content-based, on the **retired** wording rather than the new claim:

```
find . -type f \( -name '*.cs' -o -name '*.props' -o -name '*.md' -o -name '*.json' -o -name '*.csproj' \) \
  -not -path '*/bin/*' -not -path '*/obj/*' -not -path './.git/*' -not -path '*/node_modules/*' -print0 \
| xargs -0 grep -nI -i -e 'never argument syntax' -e 'never produce an .ArgumentSyntax' \
    -e 'never reachable as an ArgumentSyntax' -e 'cannot exist' -e 'cannot pass this guard' -e 'literal tokens'
```

Every hit classified; no fourth **live** copy survives. `CompositionRootDelegationWiringTests.cs:77` (raw/verbatim strings are literal tokens) is **true as written** and is explicitly excepted at `:82-91`; `:113`/`:158` are retractions. `Directory.Packages.props:40` quotes the old wording *in order to record that it was false*. `progress/current.md:1627` carries the leader's in-place `[RETRACTED …]` annotation. Everything else is an archived review/impl record of id 68, `feature_list.json`'s own entry, `CLAUDE.md`, or the unrelated `FromSqlInterpolated` subject. One residue worth naming and not filing (phase 14 is frozen at 13): `progress/impl_composition_root_delegation_and_design_time_factories.md:984` still states the retired claim inside id 68's archived record — archived-by-convention, and the D6 record's own classification (which greps the phrase *"interpolation hole"*) does not list it because that line spells it differently.

## Ported-idiom ledger — both history halves checked against #7's checkout

- **Row 1.** `apps/projector/src/projector-consumes-only.spec.ts:85-88` is exactly as quoted: a regex count of `.connectMicroservice[<(]` over whole-file **text**, then `expect(mainTs!.content).toMatch(/transport:\s*Transport\.KAFKA/)` — two unrelated whole-file facts, the option never tied to the call. `apps/billing/src/billing-consumes-no-facts.spec.ts:75-78` and `apps/notifications/src/notifications-consumes-only.spec.ts:97-100` are the same shape, and their counts use the *weaker* `/connectMicroservice/g` (a mention in a comment counts). `apps/projector/src/main-kafka-options.spec.ts:3-4` states the convention verbatim. So the row's conclusion — the id 86 defect is live and unguarded in #7, in three services — is **accurate**.
- **Row 2.** `apps/seed/src/index.ts:34-36` is three zero-argument `openOrdersDb()`/`openFulfillmentDb()`/`openBillingDb()` calls; `apps/seed/src/writers/orders-db.writer.ts:34-39` resolves the config as a **default parameter** inside the writer's own module. The "guarded by nothing, because nothing could break" half reproduces: the record's enumeration over `*.spec.ts` returns zero hits, which I re-ran. Accurate, and the *"#8's translation created the substitution surface"* reading is the right one for #9.

Guard enumeration of #7's tests for the ported mechanism is present and classified per assertion; no #7 guard was dropped in translation.

## CHECKPOINTS.md

- **C1** — [x] harness files present; [x] `./init.sh` exits 0 (leader-verified, and the implementer's `out/init2.txt` at 01:53).
- **C2** — [x] exactly one `in_progress` before this close (id 86), none after; [x] all statuses valid; [x] `progress/current.md` is this phase's working document.
- **C3** — [x] domain purity, `SharedKernel` package-freedom, `Cqrs` confinement and the decimal rule all verified by **running** `Architecture.Tests` (50/50), twice, not by eye; [x] no cross-service DB access introduced — no `src/` file has any net change (`diff` against HEAD on both mutated files); [x] no shared runtime code added; [x] no stray debug logging.
- **C4** — [x] `./quality.sh` clean, 18 projects, **2 042 / 0 / 0** (`out/quality.txt`, 01:45, after the last restore); [ ] not re-run by me — scoped review, and this entry adds no test: `[Fact]` count in the guard file is **9 at HEAD and 9 now**, and no other test file was touched by D6; [x] no Jest.
- **C5** — [x] history entry with effort record appended; [x] `feature_list.json` set to `done` by this review; [x] no commit by Claude.
- **C6** — N/A, `sdd: false`. No `R<n>` row is owed: `grep -n "CompositionRootDelegation\|SeedRunner_Opens" specs/shared/test-matrix.md` returns nothing, and `specs/` is unchanged.
- **C7** — [x] `specs/shared/` untouched by this entry.

## Defects

None blocking. Two notes, neither filed (phase 14 frozen at 13):

1. `progress/impl_composition_root_delegation_and_design_time_factories.md:984` still asserts the retired absolute in id 68's archived record (see above). Archived-by-convention; worth a one-line strike-through if the leader touches that file.
2. `out/ctrl.A11.test.txt` does not exist — only its build log — so the record's A11 **control** figure ("pre-id-86 guard 1/1 green") has no log behind it. I reproduced that control myself in both halves (old guard 9/9 green with the mutation, `Seed.UnitTests` 44/44 green), so the claim is true; the bookkeeping is what is missing.

## Effort record

**1 implementer session, 0 rejections, 1 review session.** Implementer wall-clock ≈ **51 min** (brief 01:05:38 → record finalised 01:56:32; thirteen arms 01:22–01:30, four controls 01:28–01:31, closing `./quality.sh` finishing 01:45:48, `./init.sh` 01:53:54). Review ≈ **35 min**, of which nearly all was the two-directional control work: rebuilding the pre-id-86 guard on disk cost three failed builds before it compiled, and each direction then cost one `--no-incremental` build plus seven cheap `--no-build` runs. The datum worth keeping: **an old-guard control is the only evidence that separates "the new guard is red" from "the new guard was always red"**, and it is affordable — one extra build, because the tests read source from disk rather than from the assembly under test.

## One item left for the leader, by design

With id 86 set `done`, **no feature is `in_progress`**, and `./init.sh` now exits **1** on a single check: *"`progress/current.md` claims a feature while none is active"* — its header still reads *id 86 … `in_progress`*. That is the expected post-close transition and `progress/current.md` is the leader's phase document; I have deliberately not edited it, because the leader wrote to that same file minutes ago (the `:1627` retraction) and two writers on one file is the race `CLAUDE.md` names. Every other `init.sh` check passes. Updating the header clears it.
