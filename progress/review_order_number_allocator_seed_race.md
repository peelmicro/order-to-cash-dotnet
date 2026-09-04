# Review — `order_number_allocator_seed_race` (id 45, phase 8)

**Verdict: APPROVED.** 0 blocking defects, 0 required changes, **4 advisories** (A1–A4), none of which block the close. **This closes phase 8** — id 45 was the last feature at phase ≤ 8 not already `done`.

`sdd: false` — no spec directory, no human gate. Specification of record: `feature_list.json` id 45's four acceptance bullets. The feature corrects a defect in **already-closed, already-committed feature 15**, found by the review of feature 16 (advisory A1 there) and filed with *"invert, don't delete"* written into the acceptance criteria.

## What this review ran, and what it took on trust

Per `CLAUDE.md`'s *"probe the claims, do not re-run the world"*, `./quality.sh` was **not** re-run in full; the implementer's green run is the evidence for the whole-solution claim and is recorded as its claim, not mine. Everything below I ran myself.

**Engine-level probes, on the same MS-SQL build the tests and compose use (`mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`), in a throwaway database created with `READ_COMMITTED_SNAPSHOT ON` and dropped afterwards:**

1. **RCSI does not defeat the hint.** With session A holding an uncommitted `INSERT` of `id = 1`: a plain `SELECT COUNT(*) ... WHERE id = 1` returned **0 immediately** (served from the version store, no lock); the same read `WITH (UPDLOCK, HOLDLOCK)` **blocked and died on `Msg 1222, Lock request time out period exceeded`**. That is the claim the whole fix rests on, observed rather than cited.
2. **The fixed statement, end to end, under the forced interleaving.** A holds the row uncommitted, B issues the exact shipped statement, A commits six seconds later: B waited, then reported **`rows_inserted = 0` and no error**. The same run with the hint removed: **`Msg 2627, Violation of PRIMARY KEY constraint`**. Change in kind, at the engine, independent of any C# test.
3. **Four-way concurrency, no prior row, all four sessions running the shipped statement inside their own transactions:** exactly one inserted, three no-ops, **no deadlock (no 1205), no 2627**. `UPDLOCK` rather than bare `HOLDLOCK` is what makes that true, and it is the right choice for it.

**Test-level probes:**

4. **Baseline:** `OrderNumberAllocatorTests` **4/4 green** (15 s).
5. **Independent re-arming.** Removed `WITH (UPDLOCK, HOLDLOCK)` from the existence check (the minimal mutation of the fix), `touch`ed the file, `dotnet build --no-incremental`, and **confirmed the armed SQL was in the built binary** (`strings -el` on `OrderToCash.Orders.dll` showed the seed check with no hint, and only the allocation path's `WITH (UPDLOCK, ROWLOCK)` remaining). Then ran the named test **three separate times**: `Failed: 1, Passed: 0` on all three, identical message — `Violation of PRIMARY KEY constraint 'PK_order_number_sequences' ... duplicate key value is (1)` — identical location, `EfCoreOrderNumberAllocator.AllocateNextAsync ... line 107`. **The determinism is real.** Probe 1 explains *why* it is deterministic and not lucky: the test does not wait for a race, it waits until `sys.dm_exec_requests` reports B blocked by A, and only then lets A commit.
6. **Restore.** Copied back from the backup taken before arming (`md5sum` identical), `touch`ed, `dotnet build --no-incremental`, confirmed the hint is present in the rebuilt binary, then `OrderNumberAllocatorTests` **4/4 green**. **`git checkout --` was not used at any point, on any file.**
7. **Full `Orders.IntegrationTests`: 65/65 green** (6 m 8 s) — the project the diff touches, and the one whose acceptance-path tests all run through this statement. Matches the implementer's reported count exactly.
8. **`tests/Architecture.Tests`: 16/16** (NetArchTest run, not eyeballed).
9. **`dotnet format --verify-no-changes` on the solution: exit 0.** `./init.sh`: **exit 0**, `19/45 done`, `no feature in_progress`, `progress/current.md is in lockstep`.
10. **#7's counterpart read directly**, not taken from the report: `order-to-cash-nestjs/apps/orders/src/infrastructure/persistence/order-number-allocator.ts:71-78` — an unconditional `INSERT ... ON DUPLICATE KEY UPDATE next_value = next_value` on every call. Confirmed: **#7 has no check to race, and never had this bug.**

## Probe 1 — the lock choice, judged

**Correct, and correct for the stated reason.** `UPDLOCK, HOLDLOCK` on the existence check is SQL Server's documented insert-if-not-exists idiom, and the two hints do different jobs that are both needed here: `HOLDLOCK` raises this one reference to SERIALIZABLE so a **key-range** lock is taken on the empty slot where `id = 1` would sit (a plain row lock cannot lock a row that does not exist), and `UPDLOCK` makes that lock a U rather than an S so two arriving callers **queue** instead of both taking a shared lock and deadlocking on conversion. Probe 3 is the evidence for the second half: four concurrent callers, no 1205.

**The RCSI question is the one that mattered and it is answered empirically, not by argument.** RCSI changes *un-hinted* `READ COMMITTED` reads to row-versioned reads; a locking hint on a reference opts that reference back into real locking. Probe 1 shows both halves of that in one run — plain read served from the version store, hinted read blocked. The report's distinction between RCSI (a database default for un-hinted reads) and per-transaction `SNAPSHOT` isolation (where a locking hint can raise a 3960 write conflict rather than block) is accurate, and its claim that nothing here opens a `SNAPSHOT` transaction holds: `IUnitOfWork` and every test in this file pass `System.Data.IsolationLevel.ReadCommitted` explicitly.

**The containerised-versus-deployed isolation trap named in the brief is genuinely closed, and was closed before this feature.** `MsSqlContainerFixture.CreateFreshDatabaseAsync` issues `ALTER DATABASE ... SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE` on every test database (added at feature 14, with the reason recorded on the method), and `SELECT is_read_committed_snapshot_on FROM sys.databases` on the running compose stack returns **1 for all four deployed databases**. Test and deployment agree; the arming evidence is therefore about the configuration that actually ships.

**The two rejections are sound, and one of them is better-argued than the report claims.** `MERGE ... WITH (HOLDLOCK)` is correctly rejected — the failure mode is well documented and it is the wrong instrument to reach for in a feature that exists to close exactly that failure. Catch-2627-and-retry is correctly rejected too, though the report's own reason (dependence on ambient `XACT_ABORT OFF`) understates the stronger one: EF Core's `ExecuteSqlInterpolatedAsync` runs inside the ambient transaction `IUnitOfWork` opened, so a caught duplicate-key error leaves a transaction whose continuability is a session setting rather than a property of the code — and a retry loop inside someone else's transaction is a much larger surface than one statement. The chosen fix is the smallest correct thing.

**One consequence of the derived-table form that the report does not cost out — see A1.**

## Probe 3 — the inversion

**Inverted, not deleted, and what it asserts is live.** The original `..._CanRaceTheSelfSeedInsertAndFail` asserted `Assert.Contains(results, r => !r.Succeeded)` — "at least one of sixteen concurrent first-ever callers fails". That body survives, with its assertion turned over: sixteen distinct references, `Enumerable.Range(1, 16)` gap-free from the very first allocation, under the name `..._SixteenConcurrentFirstEverAllocations_AllSucceedWithDistinctReferences`. Nothing about it is trivially true — it asserts distinctness, gap-freeness **and** the absence of the exception the old test demanded, and I watched it fail 3/3 against the armed binary.

**The old method *name* was reassigned to a new, stronger test**, and that is the right call rather than a dodge. A pass/fail count over sixteen tasks is deterministic once fixed but only probabilistic as a *detector of breakage*, which is the trap feature 16's review spent four rounds learning (its own history entry: *"when the probabilistic vehicle dies, change the question, not the sample size"*). The landed guard changes the question: it drives two sessions explicitly, and **polls `sys.dm_exec_requests` until it observes B blocked by A** before letting A commit, with a 10 s bound and a loud failure message if that interleaving never happens. `Assert.True(observedBBlockedByA, ...)` is what turns "we hope they overlapped" into "we saw them overlap". That is the correct shape and it is the reason my three armed runs were identical rather than merely mostly-red.

**A corroboration worth recording (A4).** On this machine the sixteen-way test also failed **3/3** armed — currently a stronger detector than the implementer credited it with. That does not make the implementer's caution wrong; feature 16's review measured the same vehicle going **6/6 green under load** when the machine serialised the callers, and load-dependence is exactly the property that disqualifies it as *the* guard. Declining to lean on a test that happens to be red today is the right instinct, and saying so in the test's own doc comment is better than saying so in a report.

## Probe 4 — the allocation path

**Untouched, confirmed from the diff rather than from the report.** `git diff` on `EfCoreOrderNumberAllocator.cs` shows exactly one non-comment hunk: the seven-line `IF NOT EXISTS ... BEGIN DECLARE @start ... INSERT ... END` replaced by the ten-line atomic `INSERT ... SELECT ... WHERE NOT EXISTS`. The claiming `SELECT * FROM dbo.order_number_sequences WITH (UPDLOCK, ROWLOCK) WHERE id = 1`, the `NextValue + 1` increment and the `SaveChangesAsync` are byte-identical to feature 15's. `AllocateNextAsync_ConcurrentAllocationsYieldAGapFreeDuplicateFreeSequence` (24 callers over a pre-seeded row — feature 15's row-lock claim) is unmodified and green. Locks taken by one transaction never self-block, so the new range lock cannot interfere with the claim that follows it in the same transaction; probe 3's four-way run exercises exactly that sequence and no session self-deadlocked.

## Acceptance bullets → evidence

There are no `R<n>` ids here and `specs/shared/test-matrix.md` correctly has **no row** to flip — I grepped it for `allocator`, `order_number` and `ORD-######` and there is nothing. Adding one would be a false parity claim. Traceability is therefore against the four acceptance bullets:

| Bullet | Evidence I verified |
|---|---|
| "the self-seeding branch's `IF NOT EXISTS ... INSERT` no longer races: either it takes a lock (`WITH (UPDLOCK, HOLDLOCK)`) or it handles the duplicate-key violation and retries" | `EfCoreOrderNumberAllocator.cs:107-119` — one statement, hint on the existence check. Probes 1–3: the hint takes a real lock under RCSI, the statement no-ops instead of throwing under the forced interleaving, and four-way concurrency produces one insert and no deadlock. |
| "sixteen genuinely concurrent first-ever allocations ... all succeed and yield sixteen distinct `ORD-######` references" | `AllocateNextAsync_SixteenConcurrentFirstEverAllocations_AllSucceedWithDistinctReferences` (`OrderNumberAllocatorTests.cs:215-262`) — green in my 4/4 and 65/65 runs, red 3/3 armed. |
| "`..._CanRaceTheSelfSeedInsertAndFail` is INVERTED, not deleted" | The body survives with its assertion turned over (see above); the name is carried by the stronger deterministic guard at `OrderNumberAllocatorTests.cs:133-199`. Both halves of the bullet's intent — the evidence is not lost, and the assertion now says the race cannot happen — are satisfied. |
| "the fix is armed: reverting the lock makes the inverted test fail" | **Re-armed independently by me**, not accepted from the report: 3/3 failures, identical message and identical line, with the armed binary verified by `strings` before the runs and the restored binary verified by `strings` after. |

## `CHECKPOINTS.md` — boxes walked

### C1 — harness

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all present.
- [x] `progress/current.md` and `progress/history.md` present.
- [x] `.claude/agents/` untouched by this feature.
- [x] Agent definitions untouched by this feature.
- [x] `./init.sh` exits 0 — my run, at review time.

### C2 — state

- [x] No feature `in_progress`; id 45 is the only feature not `pending`/`done`, and it is the one under review.
- [x] Every status in `rules.valid_status` (`init.sh` §3).
- [x] Every `done` feature has passing tests; this feature adds one, inverts one, breaks none — `Orders.IntegrationTests` 65/65.
- [ ] **`progress/current.md` still names id 45 as the active feature** and still says `Status: in_progress`. Before the transition `init.sh` passed lockstep, because it checks the named feature and not the status word; **after setting id 45 to `done` it is a hard `init.sh` FAIL** — `progress/current.md claims a feature while none is active`. Not a defect of this feature and not the implementer's file to touch (the leader wrote it at 18:04, before implementation began), and not reset here because the reviewer cannot author the leader's session narrative. **It is the one action standing between this repository and a green `init.sh`, and it is one edit** — see the closing note.
- [x] No `blocked` feature.
- [x] **45 features present and id 46 intact** — checked at the start and again at the end of this review. `git diff --stat feature_list.json` before my edit: **17 insertions, 2 deletions** = id 46's fifteen-line block plus the two status transitions, and `grep` finds **zero** `\u` escapes and sixteen literal em-dashes, so the round-trip damage is genuinely gone rather than partially reverted.

### C3 — architecture

- [x] NetArchTest suite **run**: 16/16. No `Domain/` file is in this diff — `src/Orders/Infrastructure/Persistence/` only.
- [x] No cross-service DB access: `dbo.order_number_sequences` and `dbo.orders` in the Orders database only.
- [x] No new shared runtime code; `src/SharedKernel`, `src/Contracts`, `src/Cqrs` untouched.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs`.
- [x] `src/SharedKernel` still has zero `PackageReference`.
- [x] No `decimal`, no money arithmetic anywhere in the diff.
- [x] Kafka-fact / NATS-RPC classification untouched — this feature has no messaging surface at all.
- [x] No stray debug logging, no context-free TODO. (Two **stale** comments, though — A2.)

### C4 — verification

- [x] `./quality.sh` — implementer's run, reported green; **not re-run here**. My targeted substitutes: solution `dotnet format --verify-no-changes` exit 0, `Orders.IntegrationTests` 65/65, `Architecture.Tests` 16/16, and the allocator suite green before and after arming.
- [x] Domain tests pure — no domain test touched.
- [x] Integration tests use real Testcontainers MS-SQL with real migrations, and the new guard drops to raw ADO and `sys.dm_exec_requests` rather than mocking anything.
- [ ] **Coverage thresholds** — reported, not gated, by design; feature 34 `sonarqube_quality_gates` (phase 21) owns the gate. Standing phase-8 position, unchanged by this feature, box left open honestly.
- [x] No Jest; `apps/web` untouched.

### C5 — session close

- [x] No suspicious untracked files. `git status` shows feature 42's tree, this feature's two files, `CLAUDE.md`, `feature_list.json`, `progress/`, and nothing else. My probe database was dropped and my scratch backup lives outside the repository.
- [x] `progress/history.md` has an entry for this feature **including its effort record**, appended at approval, together with the phase 8 closing assessment.
- [x] `feature_list.json` reflects true state — id 45 set to `done` by **editing that one line**, no rewrite, no serialisation.
- [x] The human is told what was done and how to test it manually (the leader's report; the manual check is `dotnet test tests/Orders.IntegrationTests --filter OrderNumberAllocatorTests`).
- [x] **Claude did not commit.** No `git commit`, no `git push`, no `git checkout`.

### C6 — SDD

Not applicable: `sdd: false`. No `specs/order_number_allocator_seed_race/` is expected and none exists; `init.sh`'s SDD coherence check passes on the three `sdd: true` features past `pending`.

### C7 — reuse fidelity

- [x] `specs/shared/` untouched by this feature — `git status specs/shared` is empty.
- [x] No amendment, silent or otherwise.
- [x] No `R<n>` id claimed. Correct: no shared requirement covers the seeding idiom, and reusing one would be a false claim.
- [x] `n8n/`, API script untouched.
- [x] Effort record complete and honest, **including the fact that this feature has no #7 counterpart to be faster or slower than** — it is pure #8 cost, and the record says so rather than quietly omitting the row.
- [x] README benchmark section untouched by this feature; the phase 8 closing assessment carries the numbers.

## Advisories

**A1 — the fix makes `dbo.orders` be scanned on *every* allocation, not just the first, and the report costs it as free.** `EfCoreOrderNumberAllocator.cs:107-119`. I took the actual plan on the probe database with 5,000 orders rows and the sequence row already present: `Nested Loops (Left Anti Semi Join)` with the `MAX` aggregate as the **outer** input, so `Clustered Index Scan(dbo.orders)` runs with **Rows = 5000, Executes = 1** before the anti-semi-join throws the row away. Feature 15's `IF NOT EXISTS ... BEGIN DECLARE @start ... END` computed that aggregate **only** in the seeding branch, so this is a real regression against the shipped code, on the hot path of every order placement, inside the placement transaction and therefore inside the window the range lock is held. I checked whether a rearrangement avoids it: the scalar-subquery form (`SELECT 1, (SELECT ISNULL(MAX(...)) FROM orders) WHERE NOT EXISTS (...)`) produces the **identical** plan, so within the single-statement idiom the scan is not avoidable — removing it means a cheap fast-path existence check *in front of* the atomic statement, where it would be an optimisation and not a correctness dependency. Two things stop this being blocking: it is **parity with #7**, whose `max(cast(substring(...)))` runs unconditionally on every call too (`order-number-allocator.ts:65-70`), and correctness properly outranks a scan that is sub-millisecond at today's row counts. But it is O(rows in `orders`) per placement, it is not mentioned in the `<remarks>` or the report — where the derived table is justified purely on correctness grounds — and at production volumes it is the kind of thing that gets found by a latency graph. **Recommend a backlog entry, decided against #7's cost profile rather than in ignorance of it.**

**A2 — two comments in `OrderNumberAllocatorTests.cs` now assert things that are false.** Line 34 carries `<see cref="AllocateNextAsync_ConcurrentFirstEverAllocations_CanRaceTheSelfSeedInsertAndFail"/>` — a member that no longer exists — and lines 32-35 describe that test as reproducing *"the SEPARATE, narrower self-seeding race ... (an A11 finding for feature 15, **not fixed here**)"*, which stopped being true in this feature. Line 14's class summary likewise still ends *"Test-only — the allocator itself is not touched"*, which described feature 16's change and no longer describes this file's history. Neither is caught by the compiler (both live in `//` comments, not `///`), and neither affects behaviour — but this repository has now twice paid for documentation that outlived its subject (feature 42's advisory A1 was a `design.md` table describing behaviour that no longer existed), and a future reader of this exact file is being told the bug is open. **Mechanical; route to `test_maintainer`, not to a rejection.**

**A3 — the inverted assertion and the inherited method name travelled in opposite directions.** The old test's *body* became `Sixteen...AllSucceedWithDistinctReferences`; the old test's *name* now sits on the new two-session guard. Both acceptance obligations are met and the doc comments cross-reference each other clearly, so this is not a defect — but `git blame` on the renamed method will attribute the deterministic guard to feature 16's history, and anyone auditing "was the race test inverted or replaced?" from the diff alone has to read both methods to answer. The impl report does explain it. Recorded so the answer exists outside that report.

**A4 — the sixteen-way test is currently a stronger detector than its own comment claims** (3/3 red armed on this machine). Keep the comment as written: feature 16's review measured that same vehicle 6/6 **green** under CPU load, so the comment is right about the property and merely conservative about today's observation.

## The `git checkout --` incident, judged

**The disclosure was adequate, and better than adequate in form.** It names the forbidden command, the mechanism (`json.dump` defaulting to `ensure_ascii=True`, re-escaping every em-dash in the file), the collateral (other writers' uncommitted transitions and the new backlog entry), the recovery route, and the verification. It was volunteered under a heading that calls it a mistake, in a report the implementer knew a reviewer would read, and nothing in it is shaded. That is the behaviour the repository wants and it should be said plainly before anything else is.

**One gap in it, which is about verification rather than candour.** The recovery was reconstructed *from the implementer's own earlier `git diff` capture* and then verified against *that same reconstruction* — valid JSON, three intended semantic changes, `init.sh` green. All three of those checks pass on a file that is internally consistent but wrong, which is the same guard-that-does-not-guard shape `CLAUDE.md` already names for this exact file. The claim that id 46's block is byte-identical to what the leader wrote could only be closed by someone holding the other copy, and it **was** closed — by the leader, independently, before this review. So the state is right; the point is that the implementer's own verification could not have established it, and a disclosure that said *"this needs someone else's copy to confirm"* would have been the complete version.

**On the amendment: yes, it lands, and it lands for the right reason.** Both incidents began with a whole-file rewrite going wrong, so the prior rule — *to undo an edit, re-edit it* — was advice addressed to a moment that only exists after the damage. Moving the rule upstream to the trigger (*do not rewrite this file to change one value*) is the first version of it that could have prevented either occurrence, and pairing it with `ensure_ascii=False` plus a `git diff --stat` check gives an agent that must serialise a concrete, immediate, checkable post-condition. Two refinements worth making when it is next touched:

- **State the check as "the diff shows only the lines you intended", not "one insertion and one deletion".** This session's own legitimate edit was 17/2, because it added id 46 as well as flipping two statuses. An agent applying the literal form to a legitimate multi-change edit sees the check fail, and an agent that learns the check cries wolf stops running it — which is how a guard becomes decorative, the failure class this file exists to name.
- **It is still prose, and prose is the one thing both incidents proved insufficient.** The rule was already written when the second occurrence happened, one feature later, by an agent that had it in context. The durable version is mechanical: have `init.sh` keep a snapshot of `feature_list.json` (`progress/.feature_list.snapshot.json`, refreshed on each green run) and warn when the current file has **lost** an id or a status that the snapshot had. That is the one check that would have fired on both incidents within seconds, and it is the answer to `CLAUDE.md`'s own observation that `init.sh` cannot catch this — it cannot catch it *from the file alone*, but it can catch it from the file plus its own history.

**No sanction is warranted against this feature.** The incident touched no source or test file, cost the leader one verification pass, and was fully recovered. Rejecting a correct, well-armed fix over a process error the implementer itself surfaced would teach exactly the wrong lesson about disclosure.

## The lost-in-translation pattern, at three

**The class, stated precisely: a property #7 got for free from its engine or its language was dropped when the *shape* of #7's code was carried across, because the shape is what is visible and the property is what was load-bearing.** JSON key ordering was MySQL's `json` column normalisation mistaken for a wire requirement — the same error pointing outward. `int` money columns were MySQL's choice made safe by JavaScript numbers being doubles, carried into a language where it forced a narrowing cast at every boundary. And now `INSERT ... ON DUPLICATE KEY UPDATE` — atomic because the statement *is* the unit of atomicity — rendered as `IF NOT EXISTS ... INSERT`, which looks like the same sentence and is two statements.

**What it costs is not the fix; it is the discovery latency and where the discovery lands.** All three shipped green, through a review, against passing tests, and all three were found **by a later feature's review reaching backwards into closed, committed work** — feature 15's defect by feature 16's review, the money widths three phases after they landed, the key ordering in phase 5 by a golden-envelope capture that existed for another purpose. The direct cost here was modest (≈0.9 h). The real cost is that each one re-opened a closed feature, which is the most expensive kind of work this process produces: a backlog entry, a fresh implementer briefing, a full review, and a hole in the effort record where the true cost of the original feature should have been.

**Would anything in the current process catch the fourth? No, and the reason is structural.** Traceability here runs `R<n>` → test, and all three defects satisfied their requirement text exactly: "allocate a unique sequential reference" was satisfied, "integer minor units" was satisfied, "camelCase, nulls omitted" was satisfied. The arming protocol does not reach them either, because arming proves a *guard* fires when the behaviour is deleted, and in every one of the three the behaviour was present and correct on the single-caller path the guard exercised. The gap is that **nothing in the process ever asks what property of #7's version made it correct.**

**The cheapest thing that would close it** is one line per ported idiom in the implementer's report — *"#7 relied on X (atomicity / width / ordering / isolation); in #8 that property is supplied by Y"* — required wherever #8 renders a #7 statement in a different engine or language, with the rule that when the property was **engine-provided in #7 and must be hand-built in #8**, it needs a guard test of its own. That is a small, mechanical addition to a report the implementer already writes, it is checkable by a reviewer in seconds, and it is the question that would have caught all three at the moment they were written. It is worth putting to the human gate as a `CLAUDE.md` amendment; it is not this feature's to make.

## Bookkeeping done at approval

- `feature_list.json` id 45 `in_review` → `done`, by editing that single line. 45 features, id 46 intact, verified after the edit.
- `progress/history.md`: the feature's entry **with its effort record**, plus the **Phase 8 closing assessment**.
- Left for the leader, and **required before anything else**: reset `progress/current.md` (it carries its own template under *"reset to this on session close"*). `./init.sh` exits **1** until that is done — the C2 box above is not a soft advisory now, it is the only red check in the repository. Then decide whether A1 becomes a backlog entry, and whether the ported-idiom line goes to the human gate as a `CLAUDE.md` amendment.
