# History — append-only log

> One entry per completed feature. **The effort record is mandatory**: this file is
> assessment #8's measurement against the #7 baseline, and the trilogy's first
> empirical answer to how much a mature spec plus a mature harness accelerates a
> full re-implementation. Without honest effort numbers here, the benchmark the
> SDD adoption plan asked for does not exist.
>
> **Record what was NOT faster with the same care as what was.** An all-green
> benchmark is not a result, it is a lack of measurement. The #7 baseline lives in
> `peelmicro/order-to-cash-nestjs`, `progress/history.md`, feature by feature.

Entry format:

```markdown
## <feature_name> (id <n>, phase <n>) — <date>

**Effort:** <n> session(s), ~<n>h wall-clock
**#7 baseline:** <n> session(s), ~<n>h | no counterpart (new in #8)
**Spec:** specs/<name>/ | n/a (sdd: false) | reused from specs/shared/ (R<n>…)
**Tests:** <what was added, and the R<n> requirements they prove>

**What was built:**

**Deviations from the spec/plan:**

**What the reuse saved — and what it did not:**

**Notes for #9:**
```

---

## repo_bootstrap (id 1, phase 1) — 2026-08-31

**Effort:** 1 session, ~0.5h wall-clock
**#7 baseline:** 1 session, ~0.5h
**Spec:** n/a (sdd: false)
**Tests:** n/a — toolchain phase, no application code yet

**What was built:**

.NET 10 toolchain verified by scaffolding, building and running the three templates the build actually needs (`webapi`, `worker`, `xunit`), not by reading a version string; all eight key NuGet packages resolved and build-verified together. Repo-local `peelmicro` identity and account-explicit remote set. `global.json` (SDK 10.0.111, `rollForward: latestPatch`), `.nvmrc`, `.gitignore`, `.editorconfig` and a minimal honest README created.

**Deviations from the spec/plan:**

- `dotnet-ef` alignment deferred from Phase 1 to Phase 6, the phase that first uses it. Left unticked in the plan rather than quietly dropped.
- The cloned repository was completely empty (not even a first commit), so this phase's commit is the root commit and `git branch -M main` was needed.

**What the reuse saved — and what it did not:**

**Saved:** the GitHub two-account 403 that cost #7 a debugging session did not recur — it was written down in #7's plan, so it cost one `git remote set-url` before the first push, which then succeeded first time. Likewise the `/data/` gitignore trap that silently untracked 11 source files in #7: anchored from the start here, and verified in both directions with `git check-ignore`.

**Did not save:** everything stack-specific was new work — the `.editorconfig` C# section, `global.json`, the .NET template and package probes. Effort came out identical to #7's despite the reuse, because what #7 saved on was offset by .NET having more toolchain surface to pin than Node did.

**Notes for #9:**

Verify a toolchain by *using* it, not by asking its version. The probe that scaffolds, builds and tests a throwaway project is cheap and has caught real problems in both assessments. Also: pin the SDK/interpreter in a file whose failure mode is loud — `global.json` makes `dotnet` fail outright when unsatisfiable, which is exactly what a session check wants.

## harness_layer (id 2, phase 2) — 2026-08-31

**Effort:** 1 session, ~1h wall-clock
**#7 baseline:** 1 session, ~1.5h
**Spec:** n/a (sdd: false)
**Tests:** `init.sh` verified green and verified to exit 1 on all eight break cases

**What was built:**

`AGENTS.md`, `CHECKPOINTS.md`, `init.sh` and the six agent definitions copied from #7 and re-pointed to .NET, with model pinning left untouched. `CLAUDE.md` adapted: three rules translated (explicit DI registration plus a startup validation pass; one `BackgroundService` per transport; xUnit backend / Vitest web) and one added with no #7 ancestor — the JSON wire shape must match #7 byte for byte. `CHECKPOINTS.md` C7 inverted from "is this reusable by #8?" to "did it actually reuse it, and is the benchmark honest?". `feature_list.json` reset to 42 pending features keeping #7's ids, names and phases for row-to-row comparison, plus `cqrs_dispatcher` (id 43), flagged as having no #7 counterpart. Fresh `progress/`. `docs/PROCESS.md` re-pointed, §11 reset so #7's findings stay #7's.

**Deviations from the spec/plan:**

- The plan's option of porting `init.sh`'s Node backlog validator to Python was declined at the human gate. Node is present for `apps/web` anyway, the script is proven, and rewriting it would add effort to the benchmark while changing nothing. The oddity is documented in `CLAUDE.md` instead of engineered away.
- I had earlier proposed dropping several #7 "firefighting" features from the backlog. On inspection they were never backlog features at all — they were `impl_*.md` reports for work done inside existing features. Nothing was dropped; 41 + 1 = 42.

**What the reuse saved — and what it did not:**

Measured rather than asserted, by diffing each file against #7's pristine original:

| Artifact | Lines | Changed | % |
|---|---|---|---|
| `.claude/agents/leader.md` | 75 | 2 | 3% |
| `AGENTS.md` | 79 | 10 | 13% |
| `.claude/agents/suite_runner.md` | 45 | 9 | 20% |
| `init.sh` | 178 | 41 | 23% |
| `.claude/agents/reviewer.md` | 63 | 16 | 25% |
| `.claude/agents/spec_author.md` | 94 | 29 | 31% |
| `.claude/agents/implementer.md` | 67 | 26 | 39% |
| `.claude/agents/test_maintainer.md` | 39 | 16 | 41% |
| `CHECKPOINTS.md` | 69 | 29 | 42% |
| `docs/PROCESS.md` | 288 | 189 | 66% |
| `CLAUDE.md` | 144 | 98 | 68% |

**Pure orchestration ports almost free.** `leader.md` needed two lines, because "decompose, gate, never implement" contains nothing stack-specific. **Anything encoding conventions or accumulated evidence barely ports at all** — `CLAUDE.md` and `PROCESS.md` are effectively rewrites wearing a copy's clothes.

The honest summary is that the *harness* transferred and the *conventions* did not, and only the first is what #7 claimed was reusable. A one-third saving on wall-clock (1h vs 1.5h) is real but far smaller than "we copied it" suggests.

**Notes for #9:**

Expect the same split, and budget for it: the agent definitions and `AGENTS.md` are nearly free, `CLAUDE.md` and `PROCESS.md` are near-total rewrites every time. Also worth carrying: re-point by targeted patch against a pristine copy rather than by rewriting, so the diff is auditable afterwards — that is what made this measurement possible at all.
