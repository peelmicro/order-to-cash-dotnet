# Current session

**Feature:** `operator_cancel_races_saga_forward_progress` (id 62, phase 14)

## FULL WRAP-UP IN PROGRESS (user's word, 2026-09-11) — phase 14 checkpoint, then continue

**User rulings, via AskUserQuestion:**
1. **One checkpoint commit.** Features 27, 71 and 77 go in as done, id 62 as clearly labelled unreviewed in-progress work. Per-feature separation is impossible: 27, 71 and 62 edited the same Orders files, and git holds no approved-71 state of them.
2. **The `CLAUDE.md` amendment is approved** ("name the unit in every brief; a sample is never the population"). Applied under *Briefing subagents economically*, with the four instances and the id 78 verification lesson.
3. **SA-3 is approved for both repositories:** `x-first-failed-at` = the instant the first processing attempt failed; `x-failed-at` = the instant the final attempt failed. Its code and tests stay id 75's work.

**Id 62 paused safely** ("SAFE FOR COMMIT"): no mutation, no process. The tree is **unchanged since its first pass's green run** (`/tmp/quality_run.log` 13:34: **1833**, 0 failed; the leader confirmed no `src/`, `tests/` or harness file is newer than that log). Four of its tests assert the rejected supersede design; they are named in its record's `### Paused for wrap-up` and must be labelled in the commit body.

**Order of operations:**
1. `init.sh`, then the checkpoint commit (code, `progress`, `feature_list`, `CLAUDE.md`, harness).
2. **SA-3** as its own commit in each repository: `asyncapi.yaml` bytes identical, a `history.md` section and a README registry row in each, and verification of the asyncapi-reading tests in #8 and any codegen/drift check in #7.
3. **Docs:** `PROCESS.md`, `README.md`, the three external documents and both quizzes, delegated with committed facts only, then committed.
4. **Push** both repositories.
5. **Brief the continuation:** id 62's rework resumes first.
**Status:** in_progress. **Implementer dispatched**; its brief names the unit of every criterion: race branch (A) `stock_reserved` vs `credit.approved.v1` and (B) `credit_approved`/`confirmed` vs `despatch.create`, each run **in both orderings** under `CLAUDE.md`'s two-party probe rule. Required order: reproduce first (failing on today's code), then the defence with its lock mechanism and cost, R25's no-op preserved, the note lookup under the race, and the full run waited on inside the agent's own turn. Solution **1821**. `init.sh` exit 0 (lockstep OK, 53/77 done). Nothing committed since `909394f`

**Id 77's leftovers closed by the leader:**
- **N1:** a note under the record's Step 5 heading says the "re-run" search was narrower, and names the reviewer's probe 6 as the search of record.
- **N2:** `Billing.IntegrationTests` = 90 added to the per-project list, marked as corrected.
- **A1:** routed as id 74's 7th bullet (a structural check over a literal list of test projects for per-project escapes of `RunSettingsFilePath`, `ImportDirectoryBuildProps` and `--settings`).

## Id 62 — implemented, green at 1833, SENT BACK by the leader before review: the defence strands acquired resources

**What was sound, verified by the leader:**
- **The branch × ordering matrix.** Both forward-progress-first orderings stranded on today's code; both operator-first orderings are controls.
- **The row lock.** `UPDLOCK, ROWLOCK` in `EfCoreOrderRepository.GetByIdAsync`, proven both ways, no deadlock.
- **The rest:** R25's original path, the note lookup under the race.
- **The run.** `/tmp/quality_run.log` (13:34) sums to **1833** = 1821 + 12, 0 `Failed!`. State clean, `init.sh` exit 0.

**The defect, found by reading the code, not the record.** The second half of the defence, `SagaFactHandler.cs:134-151`, **supersedes** a genuine forward-progress `Advance` fact whenever `HasPendingCompensationAsync` is true. It records it ignored and returns, **enqueuing no compensation for the resource that fact reports acquired**.
- **Branch A:** a superseded `credit.approved.v1` means Billing already **holds the credit**. The `stock_reserved` cancel plan releases only stock (`saga.md:218`), and nothing issues `credit.release`. The credit hold is stranded, the same defect moved from Fulfillment to Billing.
- **Branch B:** a superseded `order.despatched.v1` means Fulfillment already **despatched and consumed the stock**. Superseding un-despatches nothing.
- **Why the green tests could not see it.**
  - Branch A's test publishes `credit.approved.v1` itself and never asserts a credit release.
  - The stand-in `stock.release`/`credit.release` responders answer success unconditionally, so a release against consumed stock is invisible.
  - Unit of the gap: **a resource acquired in another service**, which no test observed.
- **`HasPendingCompensationAsync` is also too broad** (`EfCoreSagaCommandStore.cs:353-362`): any `credit.release`/`stock.release` row, any status, operator-cancel or saga-decided (R27's `credit_rejected` also enqueues `stock.release`).
- **Root cause.** The defence acts **after** acquisition. #7's own named defence acts **before**: *"a transactional status re-check before dispatching"*. The leader's search found no dispatch gate on a pending cancel, and no cancel planning that looks at forward commands already sent.

**Why the leader sent it back instead of to review:** a verified defect with its evidence would be a certain rejection, and a review round is ~20 min of probes.

**Id 62 set back from `in_review` to `in_progress`** (single line).

**The send-back asks the same implementer for:**
- responders that **record** issued commands and model real acquisition and consumption, with per-case assertions of commands issued and resources held;
- recommended design: gate forward dispatch (`credit.hold`/`despatch.create`) under the order-row lock once an operator cancel is pending, plus in-flight-aware cancel planning.
  - At `stock_reserved` with `credit.hold` sent, follow the `credit_approved` plan (`saga.md:219`).
  - At `confirmed` with `despatch.create` sent, refuse with the existing `ORDER_NOT_CANCELLABLE`, or show a resolution that strands nothing.
  - If the choice needs a spec change, **stop and report**: human gate.
- supersede, if kept, never discards a held resource without compensating it, scoped to operator-cancel rows;
- arms for the gate, the planning and the old supersede.

## Id 77 `test_hosts_exhaust_the_per_user_inotify_limit` — DONE (review round 1, approved 2026-09-11)

**What the review proved:**
- Arm (i) (`RunSettingsFilePath` deleted) → `"<null>"`.
- Arm (ii) (runsettings entry deleted) → `"<null>"`, under plain `dotnet test` **and** `quality.sh`'s exact coverage flags.
- The reviewer's own probe (value set to `true`) → `"true"`, so the guard reads the value, not only its presence.
- Every restore matched `cmp` and pre-probe md5.
- Probe 4: "the guard passed inside the coverage run" counts **because** the coverage path was also turned red (arm ii) and `quality.sh` sets no environment.
- No SA-n and no ledger owed (#7 has no counterpart).

**Left at approval:**
- **N1 (record text):** the "re-run" hot-reload search at `impl_…md:165-168` was narrower than id 71's (tests only, 2 of 11 terms, no file-writer search). The reviewer's wider search is now the search of record. **The leader corrects the record.**
- **N2 (record text):** `impl_…md:219-220` says "17 projects unchanged" but lists 16 totals, omitting `Billing.IntegrationTests` = 90. The sum still reconciles. **The leader corrects it.**
- **A1 (advisory):** one guard is enough today (one delivery path, 18 projects, no override), but a **future** per-project escape (`RunSettingsFilePath`, `ImportDirectoryBuildProps`, `--settings`) would be invisible. **Routed as a bullet on id 74's guard-hardening loop.**
- **A2 (advisory, accepted):** a shell or test-process `DOTNET_hostBuilder__reloadConfigOnChange` would make the guard pass with the harness broken. That is correct for the property, but the message would name the wrong source. Nothing in the suite sets it.
- **Not carried, recorded:** id 71's other bypass paths (`dotnet vstest`, running the assembly directly, IDE runners).

**For the commit (A3):** `test.runsettings` and `tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs` are **untracked**. They MUST be committed with the `Directory.Build.props` change, or every test project points at a missing settings file.

**`init.sh` exit 1 after approval** (lockstep, this file still named id 77), fixed by naming id 62.

## Id 77 — implemented; verified; review dispatched

**Verified by the leader:**
- **The record** is `progress/impl_test_hosts_exhaust_the_per_user_inotify_limit.md`, 250 lines.
- **The guard:** `HostInotifyReloadGuardTests.RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled` builds `OrdersHost.CreateBuilder` and asserts the host's own `IConfiguration["hostBuilder:reloadConfigOnChange"] == "false"`.
- **Two arms:** deleting `RunSettingsFilePath`, and deleting the runsettings environment entry. Each fails with the named message reporting `"<null>"`, and each restore was `cmp` IDENTICAL.
- **The `quality.sh` path, observed two ways:** under quality.sh's exact flags filtered to the guard, and inside the full run.
- **Escape enumeration:** 18 `tests/*.csproj`, none setting `RunSettingsFilePath`; no `Directory.Build.*` under `tests/`; no `--settings` anywhere.
- **Hot-reload search:** no `appsettings*.json`, no `IOptionsMonitor` or `GetReloadToken` in tests.
- **Inotify honesty:** the record cites id 71's full-run **118/128** as authoritative, and the leader's **116** explicitly as a partial last-50-seconds sample. It states that raising the limit is the user's decision.
- **Count:** 1821 = 1820 + 1 (`Orders.UnitTests` 444→445).
- **State:** idle, no backups, harness lines intact, id 77 `in_review`, `init.sh` exit 0.

**Review brief:**
- re-run both arms;
- set the environment value to `true` (the guard must report `"true"`, reading the value, not just its presence), and rule whether a test setting the variable itself could mask a broken harness;
- confirm the coverage-path observation, the escape enumeration and the hot-reload search;
- check the inotify honesty;
- rule whether a guard in one project is enough for a globally delivered setting.

## Id 71 `operator_note_survives_the_compensation_branches` — DONE (review round 3, approved 2026-09-11)

**Approval:**
- **Approved at round 3,** after rounds 1 (acceptance and ledger) and 2 (stale comments) were rejected.
- **The review's A6 arm** failed on the stored bytes at byte 12, after the note assertion had already passed. The byte check is real.
- **Effort** is in `progress/history.md`: 4 implementer passes + 3 review passes, ≈4 h 08 min.
- **Total** stays **1820** (last full green run 10:39; no tests added since).
- **`init.sh` exited 1 after approval** (lockstep, this file still named id 71), fixed by moving to id 77.

**R3-F1, not blocking, and it corrects the leader's own brief.**
- **The claim:** the round-2 text brief told the implementer that `dotnet build` with `TreatWarningsAsErrors` *"catches broken `<see cref>` in rewritten doc comments"*.
- **Why it can't:** `Directory.Build.props:22` sets `GenerateDocumentationFile` to `false`, so CS1574 (unresolved cref) is never emitted. The verification the brief prescribed **could not fail**, a guard that does not guard, written by the leader.
- **What escaped:** the reviewer's doc-generating build found **12** unresolved crefs in Orders alone, including `ISagaCommandStore.cs:26` (rewritten this round) and `SagaFactHandler.cs:183`.
- **Routing:** filed as backlog id 78 `doc_comment_crefs_are_never_compiler_checked`, next edit.

**Other leftovers:**
- **R3-F2 (record only) — DONE.** Superseded pointers added at the old `:160` figure ("12 hit lines", withdrawn) and above the old `:233` totals, both pointing to the D7 per-line correction (2/1/1/15 = 19).
- **R3-A1 (advisory) — folded into id 78.** `ISagaCommandStore.cs:77` and `SagaCommand.cs:55` say the envelope was "consumed from" its topic; operator-cancel rows *store* the topic, they don't consume from it. Same files as id 78's cref fixes.
- **R3-F1 — FILED as backlog id 78** `doc_comment_crefs_are_never_compiler_checked` (phase 14). Its acceptance:
  - enumerate first, with a doc-enabled build of every project and one line per CS1573/CS1574/CS1584/CS1734 site;
  - fix each site;
  - make the check standing (doc generation under `TreatWarningsAsErrors` with CS1591 suppressed, or a `quality.sh` step);
  - arm it with one unresolved cref;
  - correct R3-A1's wording.

  Its notes say plainly that the unguardable verification was the leader's brief.

**Id 77: the agent orphaned its full run, and the leader recovered it.** The implementer ended its turn *"waiting for the background quality.sh run to finish"*, the stall `CLAUDE.md` forbids, with no record written yet.
- **Recovery:** the leader found the run by `ps` (PID 889371), checked that the harness under test was unmutated, and waited on the PID with `kill -0`.
- **Harness check:**
  - `Directory.Build.props` diff = only the `RunSettingsFilePath` block;
  - `test.runsettings` intact (one environment entry);
  - no backups;
  - the guard `tests/Orders.UnitTests/HostInotifyReloadGuardTests.cs` › `RealHostComposition_Configuration_ReportsReloadConfigOnChangeDisabled` reads the host's own `IConfiguration`, never the environment, and its message names the setting.
- **Result:** exit at 11:57:08, **GREEN at 1821** (1820 + the guard), 0 `Failed!`.
- **Review round 2's residual (b) is now observed:** the guard passed **inside** `quality.sh`'s own `--collect:"XPlat Code Coverage"` invocation, which passes no `--settings`.
- **Honest limit:** the leader's inotify sampler covered only the run's last ~50 s (peak 116), so no full-run peak was taken for this run.
- **The agent was resumed** to write its record (arms with `cmp`, override enumeration, hot-reload search), run `init.sh` and set `in_review`.

**Id 77 dispatched.**
- **The guard:** a real host's own `IConfiguration["hostBuilder:reloadConfigOnChange"]` must be `"false"`.
- **Two arms:** delete the `RunSettingsFilePath` line; delete the runsettings environment entry.
- **Invocation paths:** a per-path check, plus an enumeration of any project that overrides `RunSettingsFilePath`, the hot-reload search, and a green full run.
- **One risk already settled by reading `quality.sh:53`:** it runs `dotnet test "$SLN" --collect:"XPlat Code Coverage" --results-directory …` with **no** `--settings`, so it cannot override `RunSettingsFilePath`. The agent must still observe it, since review round 2's residual (b) was that nobody had.

**Phase-14 order now: 77 (running) → 62 → 73 → 76 → 72 → the 67–70 loop with 74 → 78**, with 75 on the human gate. Id 78 goes last: a doc-enabled build sweeps every project, so it should run after the entries that still edit doc comments (62, 73, 76 and the loop).
- **`CHECKPOINTS.md`:** four unticked boxes, none caused by id 71. Two are session hygiene (`init.sh`, this file), and one is the coverage gate, deferred to feature 34 at `quality.sh:80`.
**Status:** in_review — implemented and verified by the leader; 1815 tests (green). **The review is dispatched**, with an explicit ruling asked on the outbox-plus-feature-66 composition for bullet 1. Feature 27's approval items are **all closed**: `test_maintainer` corrected the Gateway barrier doc comment ("Byte-parity sibling" → "implements the same logic … Not byte-identical"; the Orders copy made no such claim). Nothing committed since `909394f`

## Id 71 — round-2 text fixes verified; re-review round 3 dispatched

**Verified by the leader:**
- **The five D6/D3 blocks** now state the true behaviour: operator-cancel compensation rows carry the synthetic `orders.cancel.requested` envelope and ARE republished, and `null` is left only for pre-column rows ("none exists today" otherwise). The sites are `ISagaCommandStore.cs:14-24` and `:50-60`, `SagaCommand.cs:42-50`, `SagaFirstParkDeadLetterHandler.cs:96-104` and `SagaFirstParkDeadLetterHandlerTests.cs:116-124`.
- **The retired claim is gone:** the leader's retired-wording grep returns **nothing**.
- **A6:** `SagaCommandStoreTests.cs:352` asserts the stored envelope bytes. It was armed with a same-note, different-`eventId` duplicate write: the old note-only test **passed 1/1** against it (blind), and the new test fails on the bytes.
- **A1:** the true count is **4** Application → `Infrastructure.Messaging.Rpc` references in Orders, including the envelope file itself. They are id 76's.
- **D7:** 2 ported / 1 strengthened / 1 superseded / 15 not applicable = 19.
- **Build and tests:** solution build 0/0, format clean, and the affected classes green (12, 18, 1).
- **State:** the total stays 1820; idle, no backups, `init.sh` exit 0.

**Also filed while the fix round ran:** backlog **id 77** `test_hosts_exhaust_the_per_user_inotify_limit` records the inotify harness change as its own work. It still needs a guard reading the host's own `IConfiguration`, armed two ways, plus confirmation under `quality.sh`'s coverage run. Id 76's first bullet was amended to include `OperatorCancelRequestedEnvelope`. `init.sh`'s validator: 76 entries, 51 done, unique ids.

**Round 3's brief is deliberately small, per the reviewer's own round-2 note:** read the five blocks, re-run the retired-wording enumeration, arm A6 once, verify A1's count and D7, and build Orders once.

## Id 71 — REJECTED in review round 2 on stale text only; a text fix round is dispatched

**The review's own probes all failed as they should, a big change from round 1:**
- M1 on all three timeline branches, each naming the missing note;
- a corruption at the `credit.release` enqueue site, killed on `credit_approved`;
- M7 on the rewritten precedence test;
- a duplicate enqueue that UPDATEs the envelope, against the new guard;
- the sibling topic `otc.billing.facts.v1`, against `Topic_EqualsOrdersFactTopicName`;
- A2's outbox cases now name the note.

**Closed:** D1, D2, D4 (19 hits reconcile; the reviewer withdrew its uncounted "21"), D5's decision, A1's new import, A2–A5.

**What blocks: the same class as D2, in comments.** Five comment blocks still say operator-cancel compensation rows carry **no** envelope, the exact behaviour id 71 changed. The leader verified each:
- `ISagaCommandStore.cs:16-20`;
- `ISagaCommandStore.cs:51-55`, which tells callers to pass `null`;
- `SagaCommand.cs:44-46`;
- `SagaFirstParkDeadLetterHandler.cs:98-100`;
- `SagaFirstParkDeadLetterHandlerTests.cs:118-120`.

`ISagaCommandStore`'s two are also D3's residue. The leader's retired-wording search found four more "RPC-triggered" comments to classify: `SagaCommandRequestFactory.cs:40`, `OperatorCancelRequestedEnvelope.cs:17`, `CancelOrderCommandHandler.cs:85` and `:164`.

**Why it escaped twice.** Every enumeration so far searched for **overwrite** (D2's retired mechanism) or **§4.2** (D3's retired citation). The claim "operator-cancel rows are `null`" uses neither word. That is `CLAUDE.md`'s *"enumerate on the wording of the claim being retired"* failing because the **behaviour** being retired had its own separate wording, which nobody searched. The fix round's brief now enumerates on that wording.

**Also required:**
- **D7:** the D4 totals line mixes lines and assertions; per line it is 2 ported / 1 strengthened / 1 superseded / 15 not applicable.
- **A6:** the duplicate-enqueue test claims "byte-for-byte" but asserts only the note. It is being made true with a stored-bytes assertion, armed by a same-note, different-bytes update the note-only assertion cannot see.
- **A1's comment miscount:** `OperatorCancelRequestedEnvelope.cs:59-61` says "three" Application → `Infrastructure.Messaging.Rpc` references, while the file itself imports that namespace (line 2).

**Harness change ruled on:**
- **(a) safe:** no `appsettings*.json` exists under `src/` or `tests/`, and nothing reads a reload token.
- **(b) reaches the hosts:** the test-host environment carries the variable under a single-project and a solution-level `dotnet test`, with 0 inotify instances per host. It was not observed under `quality.sh`'s coverage collection.
- **(c) unguarded:** acceptable for now; a one-test guard is cheap.
- **(d)** it should be **its own backlog entry**, filed by the leader. **Next edit.**

**Leader actions:**
- **Id 76 bullet 1 amended:** `OperatorCancelRequestedEnvelope` still imports `Infrastructure.Messaging.Rpc` and is in id 76's population.
- **The text fix round dispatched:**
  - D6 and D3 via a retired-wording enumeration with one classification per hit;
  - A6 with an arm;
  - A1's count and D7;
  - verification by solution build, format and the affected test classes. No full run is needed for comments plus one assertion; the total stays 1820.

## Id 71 close-out — verified; re-review round 2 dispatched

**Verified by the leader:**
- **The run:** `scratchpad/quality_full_clean.log` (10:39) sums to **1820** with **0** `Failed!` lines and `[OK] quality.sh finished`. That is 1815 + the fix round's 5 new tests: `OperatorCancelRequestedEnvelopeTests.Topic_EqualsOrdersFactTopicName`, `SagaCommandStoreTests.EnqueueAsync_ADuplicateEnqueueWithADifferentEnvelope_LeavesTheFirstEnvelopeInPlace`, and the Gateway timeline `[Theory]`'s 3 branch cases.
- **A2:** no bare `GetProperty("note")` is left in `OrdersCancelAcceptanceTests`; the arm now fails naming the missing note.
- **State:** idle, no backups, id 71 `in_review`, `init.sh` exit 0.

**The inotify class, fixed on the test side.**
- **The change:** a root `test.runsettings` sets `DOTNET_hostBuilder__reloadConfigOnChange=false`, wired by one `<RunSettingsFilePath>` line in `Directory.Build.props`. It applies to `quality.sh`'s solution-level run and to any bare `dotnet test`.
- **The proof, by change of kind:** one host-heavy project alone peaked at **123/128 without** the setting and **118/128 with** it (above the desktop floor: 8 → 3). The full run peaked at **118**. The system limit was not touched.
- **The margin is thin, and the user should know it:** desktop processes hold **115** instances at rest, so about **10** remain for a full run. A desktop app that opens more watchers could make runs red again. Raising `fs.inotify.max_user_instances` is the user's call; it is not needed today.
- **For the commit record:** `Directory.Build.props` and the new `test.runsettings` are a harness change **outside id 71's feature scope**. The reviewer is asked whether it should be its own backlog entry.

**Re-review round 2 dispatched:**
- re-run M1 on all three timeline branches, and M7 on the rewritten precedence guard;
- probe the new duplicate-envelope guard by making the duplicate enqueue UPDATE;
- substitute a sibling topic literal against the topic guard;
- reconcile D4 (19 vs 21 hit lines);
- rule explicitly on the inotify harness change (safety, whether it reaches the test hosts, whether it is guarded, and whether it needs its own entry).

## Resumed after the reboot (booted 2026-09-11 09:53) — checks passed; id 71 close-out dispatched

**Resume checks, run by the leader:**
- `./init.sh` exit 0.
- No `dotnet build/test/format`, `quality.sh` or test-host process.
- No backup files or arming markers under `src/` or `tests/`.
- **No package upgrades since 06:22 yesterday.** SDK 10.0.112, runtime 10.0.12.
- The `otcnet-*` compose stack restarted on its own, all healthy.

**What the pause record added.** The fix round's last full quality.sh before the reboot was **red**: `Billing.UnitTests.BillingDispatcherRegistrationTests.InvoicingPorts_EachResolve_AndAreEachRegisteredScoped` threw `IOException: The configured user limit (128) on the number of inotify instances has been reached`. Every other project passed (Orders.Unit 444, Orders.IT 135, Gateway.IT 59, Architecture 25).
- **The agent's explanation:** accumulated session state, cleared by the reboot.
- **The leader doubts it.**
  - `HostApplicationBuilder` watches configuration files by default (reload-on-change), one inotify instance per host.
  - The limit (128) is **per user across all processes**.
  - quality.sh runs 18 test projects in parallel, many building real hosts.
  - So a fresh boot could hit it again.
- **Measured by the leader right after boot, before any test ran:**
  - `max_user_instances` = **128**; **113 already open** for this user.
  - The top holders are desktop processes (VS Code and extensions, kwallet, wireplumber, ~3 each), leaving **15 free**.
  - **Nothing** in `src/`, `tests/` or `quality.sh` disables config reload-on-change.
  - 49 test files across 12 projects build real hosts.
  - **So the red is structural on this machine, not session residue.** The implementer was told to fix the class **before** the full run.
- **Measure, don't assume.** The close-out brief samples the inotify count during the full run. If it is red again, or comes near the limit, the fix goes in at its class: disable config reload for test hosts, proved by a per-project peak count with and without the setting. It never raises the system limit, which is the user's machine.

**Id 71 close-out dispatched to a fresh implementer:**
- A2's remaining four bare `GetProperty("note")` assertions (`OrdersCancelAcceptanceTests.cs` `:380`, `:452`, `:506`, `:589`), armed once;
- format, a full green quality.sh with inotify sampling, and init.sh;
- reconciliation against **1820** (1815 + the fix round's 5 new tests).

Then re-review.

## RESUME HERE AFTER THE REBOOT (2026-09-11)

**What was paused.** Id 71's review-round-1 fix round (D1–D5, A1's new instance, A2, A4, A5). Before the reboot the implementer was asked to:
- restore any arming mutation from backup and `cmp`-verify it;
- start no build;
- record its state in `progress/impl_operator_note_survives_the_compensation_branches.md` › `### Paused for reboot`.

**Leader's pre-reboot snapshot:** no `dotnet build/test/format`, `quality.sh` or test-host process; no backup files under `src/` or `tests/`; no Testcontainers. Only the long-running `otcnet-*` compose stack was up.

**On resume, in order:**
1. Run `./init.sh` and confirm exit 0. Id 71 stays `in_review`, and this file's **Feature:** line names it, which is valid during a review pass.
2. Check that no source file is left mutated:
   - read the `### Paused for reboot` section;
   - run `git status --porcelain -- src tests` and compare it against the files the fix round names;
   - `grep -rn "ARM MUTATION\|UnusedRealCheckAsync" src tests`, which must return nothing.
3. Check `/var/log/dpkg.log` for any upgrade the reboot applied. Two unattended .NET upgrades already landed this phase (runtime 10.0.12, SDK 10.0.112).
4. Restart the dev stack if needed (`docker compose up -d`). Integration tests use their own Testcontainers and do not need it.
5. Re-dispatch id 71's remaining fix-round work to a fresh implementer. The previous agent's context does not survive the session. **State at the pause, as reported by the agent and recorded in its `### Paused for reboot` section:**
   - **D1 done:** the Gateway timeline `[Theory]` over the three branches, armed by deletion and corruption.
   - **D2 done:** row 3 rewritten; the precedence test rewritten and re-armed with M7; a duplicate-envelope guard added and armed.
   - **D3 done:** comments corrected; §4.2 enumerated (25 hits) and classified.
   - **D4 done:** a content-based #7 enumeration (19 hits).
   - **D5 done:** row 1 corrected; decision: rely on the per-site guards, no new overload.
   - **A1 done:** the `Infrastructure.Outbox` reference removed, with a new guard test.
   - **A4 and A5 done:** A5's fabrication arm was killed on both saga-decided tests.
   - **A2 PARTIAL:** `OrdersCancelAcceptanceTests.cs` `:380`, `:452`, `:506`, `:589` still use a bare `GetProperty("note")` and must fail naming the missing note.
   - **Not yet run this round:** `dotnet format --verify-no-changes`, the full `./quality.sh`, the reconciliation against 1815 plus the round's new tests, and `./init.sh`.

   **The brief:** finish A2 at those four sites; then format, a full green quality.sh (check `/var/log/dpkg.log` first if red) and init.sh; then return id 71 for re-review.
6. Then follow the phase-14 order below: 71 → 62 → 73 → 76 → 72 → the 67–70 loop with 74, with 75 on the human gate.

**Human-gate items still open:** id 75's SA-3 wording for `x-first-failed-at`, and the proposed `CLAUDE.md` amendment ("name the unit in every brief; a sample is never the population"). Nothing committed since `909394f`; all work is uncommitted on disk and survives the reboot.

## Id 71 — REJECTED in review round 1; fix round sent; a systemic layering gap found while verifying A1

**The ruling I asked for came back against the composition.** Both of its premises were false:
- **"A real Billing host is required."** False: `tests/Gateway.IntegrationTests/StandInResponder.cs:17` is a public real-NATS stand-in for any subject, and feature 66's harness already boots real Orders and Projector hosts and Kafka.
- **"The Projector path is branch-agnostic."** Unproven: the only integration-level `compensationSteps` projected into Mongo is empty (`TimelineProjectionTests.cs:145`), and only the compensation branches produce a non-empty one.

**My error:** I relayed the record's reasoning as "deliberate, not an omission" and sent it for a ruling **without checking either premise**. One `grep` for a stand-in and one for `compensationSteps` would have settled both. The reviewer's required shape is one `[Theory]` over the three branches in feature 66's harness, asserting `detail.note` and the branch's `compensationSteps` length (review `:119-134`).

**Blocking findings, each verified by the leader against the files:**
- **D2.** Ledger row 3 describes an envelope overwrite no code performs (the only write is the INSERT). Its guard `FindOperatorCancelNoteAsync_PrefersCreditReleaseOverStockRelease_WhenBothRowsExistForTheOrder` **stayed green with the precedence reversed** (M7). The retired "overwrite" wording is live at `CancelOrderCommandHandlerTests.cs:259`, `SagaCommandStoreTests.cs:268`, `ISagaCommandStore.cs:137`, and record `:22`/`:102`.
- **D3.** The `design.md §4.2` misattribution moved: it is now at `CancelOrderCommandHandler.cs:84` and `OperatorCancelRequestedEnvelope.cs:16`.
- **D4.** The #7 enumeration named nine files instead of searching by content; the reviewer's content search finds 21 hit lines, 12 outside the list. No guard was dropped.
- **D5.** Ledger row 1's nullability reason is wrong: `SagaFact.cs:43` is the real reason, and #7's non-nullable compile-time guarantee is not carried over for future callers.

**Confirmed by the reviewer:**
- Bullet 3 holds per status at integration level: `stock_rejected` at `SagaCompensationStockRejectedTests.cs:74`, `credit_rejected` at `:113`. My brief understated this.
- Both enqueue sites' envelope deletions were killed (M4, M5).
- The `.dlq` copy is asserted byte-equal.
- The history halves hold (#7 never reads the note back).
- No SA-3 is needed.
- The port-allocation red run is accepted.

**A1 is systemic, not one import.** A1 flagged id 71's new `OperatorCancelRequestedEnvelope.cs:3` → `Infrastructure.Outbox`. The leader's search found far more Application-layer files importing Infrastructure namespaces:
- **Billing, 10:** `InvoiceIssueService`, `CreditHoldService`, `PaymentRegisterService`, `CreditReleaseService`, `ICreditReadPort`, `IInvoiceReadPort`, `ListInvoicesQuery`, `ListCreditQuery`, `HoldCreditCommand`, `ReleaseCreditCommand`, all `Infrastructure.Messaging.Rpc`.
- **Fulfillment, 10:** `DespatchCreationService`, `StockReservationService`, `StockReplenishService`, `IStockReadPort`, `ListStockQuery`, `CheckStockQuery` and four command handlers.
- **Orders:** `ISagaCommands`, `SagaCommandRequestFactory` and `CancelOrderCommandHandler` (`Infrastructure.Messaging.Rpc`), plus the new `OperatorCancelRequestedEnvelope`.
- **No architecture test forbids Application → Infrastructure.** `grep` over `tests/Architecture.Tests` returns nothing.

`CLAUDE.md` says dependencies point inward and Infrastructure implements the Application's ports, but NetArchTest enforces only domain purity. The violation has been silent since phase 8.
- **Routing:** id 71's fix round removes the **new** instance only. The class is **filed as backlog id 76** (`application_layer_depends_on_infrastructure_unguarded`, phase 14): enumerate first, move the RPC payloads into `src/Contracts`, keep the wire bytes and parity guards green, and add a NetArchTest rule over a literal list of the six assemblies, armed in two services.
- **#7, checked before filing (HEAD `bf45af0`):**
  - **#7 kept every RPC payload type in its contracts package**, generated from `asyncapi.yaml` (`packages/contracts/src/generated/asyncapi.types.ts`). #8's `src/Contracts` holds only `Envelopes`, `Facts` and `Wire`; its payloads sit in each service's `Infrastructure/Messaging/Rpc`. That placement is why the Application imports exist, and it makes id 76's remedy a return to #7's placement, not an invention.
  - **#7 is not layer-clean either:** `apps/orders/src/application/saga-fact-handler.ts:17-18`, `commands/saga-dispatch.handlers.ts:8`, `apps/notifications/src/application/commands/notify.command-handlers.ts:14-20`, and two `trace-context` imports, with no lint rule against any of them. So id 76 is **not a guard dropped in translation**: it is #8's own stated convention, never enforced, and the entry says so.

**Routed now:**
- **The fix round went to the same implementer:** D1–D5, A1's new instance, A2, A4 and A5, with D2's enumeration run on the retired wording.
- **A3 is a new acceptance bullet on id 62:** the note lookup under the race picks the operator's own note, armed by position-based selection.

## Id 71 — implemented; verified by the leader; one acceptance question for the reviewer

**Verified:**
- **State:** idle; id 71 `in_review`.
- **Total:** `quality_id71_rerun.log` (08:19) sums to **1815** with 0 `Failed!` = 1799 + 16 (Orders.Unit 437→443, Orders.IT 124→134).
- **Both enqueue sites** assert the synthetic `orders.cancel.requested` envelope: `stock.release` at `CancelOrderCommandHandlerTests.cs:157`, `credit.release` at `:265`. The operator no-note case asserts the key is **omitted** (`:192`).
- **Arms** (record `:199-202`): deletion (`FindOperatorCancelNoteAsync` → `null`), corruption (`"CORRUPTED"`), substitution (real sibling tokens `credit.hold`/`stock.reserve`), and the dead-letter parity deletion (restore `null` → `Assert.NotNull() Failure`).
- **A #7 difference the record documents from #7's own code:** #7 **never reads the note back** (`cancel-order.handler.ts` does not read `triggeringEventEnvelope` anywhere; record `:101`). #8's read-back is new behaviour, not a port, which is why its ledger row is the pure "#8 supplies it" half.

**The first full run was red with an evidenced infrastructure cause.** `quality_id71.log` shows Projector's `HealthProbesTests` failing in **1 ms** with Docker's own `Bind for 0.0.0.0:… failed: port is already allocated`. That is the Docker host-port allocator racing at container creation with ~28 containers starting at once.
- No dpkg activity after 06:22; nothing in `src/Projector` or `tests/Projector.IntegrationTests` changed.
- The green re-run stands on that evidence. **Watch for recurrence:** a second port-allocation red would make it a harness defect (quality.sh's container concurrency) worth an entry.

**The acceptance question, sent to the reviewer rather than decided here:**
- **What bullet 1 asks.** The note must land *"on the read-model timeline entry … proved end to end"* for each of **three branches**.
- **What was delivered** (`OrdersCancelAcceptanceTests`):
  - `StockReserved_WithANote…` and `Confirmed_WithANote…` prove the note on the **`order.cancelled.v1` outbox row**.
  - `CreditApproved_WithANote…` proves only the **enqueued `credit.release` row's stored envelope**.
- **The record's reasoning** (`:78-92`, deliberate, not an omission): a per-branch timeline test would need a real Billing host inside `Gateway.IntegrationTests`. The outbox → Kafka → Projector → Mongo path is **unchanged and branch-agnostic**, already proven by feature 66's `PostOrdersCancelWithANote_ThroughTheRealFourServiceChain_LandsOnTheRealMongoTimelineEntry`.
- **What narrows the gap:** `confirmed` and `credit_approved` share the same credit-release-then-stock-release chain (the handler's `CreditApprovedOrConfirmed` variant), so `Confirmed_WithANote…` exercises that chain's read-back at integration level. `credit_approved` differs only in the starting status.
- **Bullet 3, per saga-decided status:** `credit_rejected`'s wire-key absence is asserted at integration (`SagaCompensationCreditRejectedTests.cs:113`). `stock_rejected` rests on the unit case's null domain `Note` plus the general serializer omission guard (`JsonWireOptionsTests.cs:114`, `OutboxWireParityTests.cs:315`), with no per-status wire assertion.
- **Why the reviewer, not the leader:** whether the composition satisfies the literal bullet is an acceptance ruling. Ordering a Billing-backed Gateway harness first would spend a large round the ruling might make unnecessary. If the reviewer requires the literal reading, the round builds it.

## Feature 27 `observability_reliability` — DONE (review round 4, approved 2026-09-11)

- **Approved at round 4** after rejections in rounds 1–3 (`progress/review_observability_reliability.md`, Round 4 from `:1672`). Every round-3 closure was probed by the reviewer's own mutation: D8 `Expected 240 / Actual 2400000`; D9 plain hoist 3/3 at both sites; D10 `Collection was empty`; D11 zero hits.
- **Tests:** solution **1799**, from the reviewer's own sum over the leader-read 06:34 log.
- **Effort** is recorded in `progress/history.md:2122` (≈3.1× #7, four review rounds).
- **The red run's cause was confirmed by the reviewer from the log itself:** each `DockerUnavailableException` pairs with a `FileNotFoundException` for `System.Net.Requests`. That is the runtime replaced under running tests; the Docker daemon never restarted.

**Non-blocking items left at approval:**
- **RC1 (leader, record `:3326-3331`):** the attribution should read "runtime and SDK upgraded mid-run by unattended apt", citing the paired exception. **Being done now.**
- **RC2 (leader, `design.md:446`, `:475`) — DONE.** Both cells now name the literal cases, found by `grep`, not taken from the review:
  - `:446` → the `*ProgramConfigurationTests` defaults and reads cases in Orders, Notifications and Projector;
  - `:475` → the three `MetricsExposureTests` `OtcDlqDepth_*` cases, rows 67/68/69.
- **RC1 — DONE** as a correction block under the record's verification heading, citing the dpkg timeline, the uninterrupted daemon, and the paired `FileNotFoundException System.Net.Requests`.
- **RC3 — DONE** by `test_maintainer`, comment only. The integration bound catches over-scaled values (ticks); only the unit cases catch under-scaled ones (seconds).
- **RC4(c) — DONE:** `(:141, :171)` → `(:142, :171)`, marked.
- **RC4(d) — record DONE.** `:3016`'s "byte-parity siblings" now says *same logic, doc comments differ at `:18-24` vs `:18-28`, no parity test*.
  - **Its other half is DEFERRED:** the **Gateway copy's own doc comment** (`tests/Gateway.IntegrationTests/RequestOverlapBarrierConnection.cs`) makes the same claim, but it is a `tests/` file and id 71's implementer is building `Gateway.IntegrationTests` right now. It goes to `test_maintainer` as a comment-only edit **after id 71 reports**, so no file changes under a running build.
- **RC4(a) — DONE.** All six enumeration commands in the class-closure section (`:3201-3202`, `:3262`, `:3268-3269`, `:3275`) are now path-excluding `find … -not -path … | xargs -0 grep`. The heading above them now says the original post-filter form was **the leader's own**: I counted the 44/6 teardown sites with `grep -rn … | grep -v '/bin/…'`, the exact form `CLAUDE.md` forbids. The reviewer's path-excluded re-run gave the same populations.
  - **Method:** one script with 8 exact replacements, each asserted to match once before anything was written; the lines were read back afterwards.
  - The only post-filter text left in the section is the quotation inside that correction.
- **RC4(b) — DONE.** A note at `:3179` classifies Gateway's two end-to-end hosts as safe (each is the only class in its collection, `OperatorNoteEndToEndCollection` `:297` and `StreamProjectorEndToEndCollection` `:182`, with one `[Fact]` each). It records that the enumeration had selected files by `.dlq` content, the property the table was built from.
- **Open from feature 27's approval: exactly one item.** RC4(d)'s other half, the Gateway `RequestOverlapBarrierConnection.cs` doc comment claiming byte-parity, goes to `test_maintainer` once id 71's implementer reports.
- **`init.sh` back to exit 0** once this file named id 71 (validator: 51/74 done, 1 in_progress). **Id 71's implementer is dispatched.** Its brief names the unit of every criterion (compensation branch ×3, saga-decided status ×2, enqueue site ×2, test case, arm), per the proposed amendment.
- **RC3 (`test_maintainer`):** the `RealInfraMetricsProvenanceTests.cs:39-42` comment claims the row-72 bound catches neither wrong unit, but it catches ticks. **Dispatched.**
- **RC4 (leader, record residue):** post-filter-form enumeration commands; Gateway's two consumer-group hosts unclassified (safe); `:3003` cites #7 `:141` for `:142`; the two barrier copies called byte-identical when they are not. **Texts being located.**
- **A16 (leader):** make the consumer-group clearance structural, and keep a test's own failure visible when teardown also throws. **Added as a bullet on id 74.**
- **A17:** record only. **A18:** nothing new in `specs/shared/` beyond id 75.

**`init.sh` exited 1 after the approval,** because this file's **Feature:** line still named id 27 while no feature was active. That is exactly the lockstep check C2 exists for. Fixed by naming id 71 here and setting it `in_progress`.
**Status:** **in_review** — rejected in review rounds 1, 2 and 3. Fix round 4 verified; the red run is diagnosed; **both mechanisms are closed at their class**; the full run is **green** (1799, read by the leader). **Re-review round 4 dispatched.** Nothing committed since `909394f`

## Class closure — verified; one more red run, with an evidenced external cause

**Verified by the leader:**
- **The final run:** `scratchpad/quality_final2.log` (06:34) sums to **1799**, with **0** `Failed!` lines. Gateway IT 56/56, Orders IT 124, Projector IT 59, Notifications IT 16, ending `[OK] quality.sh finished`. Nothing running.
- **Helper uses:** `StopHostAndWaitForGroupToClearAsync` appears 12× in Notifications, 26× in Orders (definition + 25 sites) and 5× in Projector (definition + 4). That matches the agent's classification: Orders 25 of 44 sites needed it (18 `NatsCollection` sites point at an unreachable broker; 1 never registers the saga), and Projector 4 of 6 (2 own a private container).
- **Mechanism 4:** zero warm-up/fact partition mismatches in Orders or Projector, and both use `AutoOffsetReset.Earliest`. Delegated to the reviewer to spot-check.

**The round's first full run was red: Gateway IT 28/56, all `DockerUnavailableException`.** The agent attributed it to "an SDK auto-update coincident with Docker daemon unavailability". **The leader checked the machine:**
- `/var/log/dpkg.log`: `dotnet-sdk-10.0` **10.0.112** installed at **06:20:34**, by an unattended apt run (06:20:41–06:22:01). `dotnet --list-sdks` now shows **only** 10.0.112, so 10.0.111 was replaced.
- The red log `scratchpad/quality_final.log` was last written at **06:20:32**, two seconds earlier.
- `systemctl show docker`: `ActiveEnterTimestamp=Thu 2026-09-10 05:16:08`. **The daemon never restarted.**

**Correction, from the full dpkg/apt history for that window (read after the note above was written).** The .NET **runtime** was replaced before the log stopped, not only the SDK after it:
- **06:20:28** — an unattended apt run starts upgrading the runtime from 10.0.11 to 10.0.12: `dotnet-host` at 06:20:28; `dotnet-hostfxr` and `dotnet-runtime` at 06:20:29; `dotnet-apphost-pack` at 06:20:30; then the targeting pack and `aspnetcore-runtime`. That run ends at 06:20:37.
- **06:20:32** — the red log is last written.
- **06:20:34** — SDK 10.0.112 is installed.
- **06:20:41–06:21:48** — a separate libc6 upgrade.

My first note cited only the SDK install at 06:20:34, which came *after* the log stopped. It was the right event class read at the wrong line.

**The evidenced cause is the .NET host runtime (hostfxr, runtime, ASP.NET Core runtime) being replaced under the running `dotnet test` processes, 2–4 seconds before the log stopped, followed by the SDK.** The "Docker daemon unavailability" half is unsupported. The reviewer was sent the corrected timeline.
- **Why the green re-run on identical code counts here:** the red has an **external cause with machine evidence** (a package log and a timestamp). That is unlike "the isolated re-run passed", which is evidence of nothing.
- **The record's attribution** is sent to the reviewer to rule on.

**Environment note for the wrap-up:** the machine's only .NET SDK is now **10.0.112**. `global.json` still pins `10.0.111` with `rollForward: latestPatch`, which resolves to it. CLAUDE.md's environment note stays true as written, but the commit's environment line should name 10.0.112.

**Unattended apt can replace the SDK mid-suite.** Any future red full run should be checked against `/var/log/dpkg.log` before it is diagnosed as code.

## The red quality.sh — diagnosed; fixed in Notifications; class closure sent back

**Verified by the leader:** the session scratchpad's `quality_round3.log` (05:37) sums to **1799**, with **0** `Failed!` lines, ending `[OK] quality.sh finished`. Notifications IT passed 16/16, Orders IT 124, Projector IT 59. Nothing is running.

**What the round proved, by change of kind:**
- **Mechanism 2 — the shared production consumer group: a real, deterministic defect.**
  - A silent member blocks a second member's assignment for 90 s or more.
  - `host.StopAsync()` + `Dispose()` returned after **30,078 ms**, which is .NET's default `HostOptions.ShutdownTimeout`, while the broker was still unreachable. So "`StopAsync` returned, therefore the group was left" is false under contention.
  - Fixed in Notifications with `StopHostAndWaitForGroupToClearAsync` at 10 teardown sites. The helper was armed 3/3 in each direction.
- **Mechanism 4 — not among the leader's three hypotheses, and the actual cause of the failure.**
  - `NotificationDeadLetterTests`' `FixedPartitionKey` (`"notifications-dead-letter-tests"`) hashed to partition **3**, while the warm-up's key hashed to partition **5**, measured directly.
  - So a successful warm-up proved assignment of the wrong partition.
  - Fixed by aligning the literal. The suite-level revert did not reproduce a second time, and the record says so honestly; the partition measurement carries the proof.
- **Mechanisms 1 and 3** were ruled out by reading and configuration.

**Not done, against the brief.** The round found the identical shared-group shape in Orders (`orders.saga`: `SagaDeadLetterTests`, `SagaCommandDeadLetterTests`, `LogCorrelationTests`) and Projector (`projector`: `ProjectorDeadLetterTests`, `LogCorrelationTests`). It marked them *"disclosed … not fixed … out of scope — both suites were green in the failing run."* That is the green-run-as-evidence reasoning the round was sent to retire.
- **The leader's count:** host `StopAsync` sites are **Orders 44 and Projector 6, with 0 uses of the helper in either**.
- **Mechanism 4's class was never enumerated.** The leader's literal-constant grep found partition keys only in Notifications, which proves nothing about Orders and Projector, whose tests key by other idioms.

**Sent back to the same agent,** which holds the helper and both probes:
- classify all 50 teardown sites, apply the clearance wait where a later test joins the same group, and arm it per project with the zombie-member construction;
- enumerate warm-up-versus-fact partition keys in every idiom, measuring the partitions;
- finish with a green full run reconciled against 1799.

## Leader design corrections from fix round 4, and a spec gap found while writing them

**All in `specs/observability_reliability/design.md`, each marked in place:**
- **§11 row 44 (`:465`)** now credits `Row44To45_ARealHttpRequestProducesARealServerSpan_AndTheFailureLogLineCarriesTheSameTraceId`, which exports through the host's own tracer provider, instead of `TelemetryWiringTests`, which never asserted the registration (D10).
- **Ledger row L29 added**, for fact-processing latency (D8).
  - **History half, read myself at #7 HEAD `bf45af0`, not relayed:** `apps/orders/src/infrastructure/messaging/fact-retry-dispatcher.ts:135` `const enteredAt = this.clock.now()`, then records at `:142` and `:171`, with the reason given in its comment at `:129-134`.
  - **#8 half:** `FactRetryDispatcher.cs:73`, `:84`, `:136`.
  - **Guards, cited by literal name after a `grep`:** `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer` (`:93`) and `OtcFactProcessingLatencyMs_RecordedOnTheExhaustedRetryDlqPathToo` (`:130`). The fix round's own reply had abbreviated one of them with "…".
- **§10's count claim (`:369`)** now records 29 rows since review round 3.

**The gap, found on the line after L29's evidence (#7 `:136`).**
- **#7:** `const firstFailedAt = enteredAt;`. #7's `x-first-failed-at` is the dispatch **entry** instant (`kafka-dlq-publisher.ts:49`), asserted as such at `fact-retry-dispatcher.spec.ts:89`.
- **#8:** the instant of the first **caught failure**.
- **The contract:** `specs/shared/asyncapi.yaml:2240-2243` declares both headers as a bare `$ref: Instant` with **no description**, while every neighbouring header has one. `:1578-1579` is only an example.
- **Classification:** neither assessment violates the contract, so this is **a parity gap whose root cause is `specs/shared/`**.
- **Routing:** filed as **backlog id 75** (`dead_letter_first_failed_at_semantics`, phase 14), per `CLAUDE.md`'s rule. It carries a recommendation, not a menu: an **SA-3** defining `x-first-failed-at` as the instant the first attempt failed. That matches the name and #8, and costs #7 one line (`:136`) plus its spec expectation. It also requires a test whose clock advances between entry and first failure, so the two instants can differ. **Human-gated.** Not a feature-27 blocker.

## Fix round 4 — changes verified; its quality.sh was red, and the red was misreported as a flake

**Verified closed by the leader against the files:**
- **D8.** All three `FactRetryDispatcher` copies time from the injected `IClock`; the only `Stopwatch` left is the comment at `:68`. `FactRetryDispatcherTests` asserts `Assert.Equal(240, …)` at `:112` and `Assert.Equal(5750, …)` at `:150`. Row 72's integration case bounds the value (`m.Value <= upperBoundMs`, `:104`).
- **D9.** Both concurrency tests drive a `RequestOverlapBarrierConnection`, with no delay between calls.
- **D10.** Row 44 exports through the host's own tracer provider (`ConfigureOpenTelemetryTracerProvider(… AddProcessor(… RecordingActivityExporter))`), and no `ShouldListenTo` remains.
- **D11.** The six host comments are reworded; the agent's enumeration returns zero.
- **State:** idle, no backups, `feature_list.json` untouched.

**The red run, read from the run itself.** The log is this session's `scratchpad/quality.log`, summing to **1799** and ending `[FAIL] dotnet test failed`.
- **Failed case:** `NotificationDeadLetterTests › OR4_R57_TheConsumeSpanContinuesTheInboundKafkaTrace_TheDlqCopyCarriesTheSameTraceIdAsTheInboundFact`, after 1 m 37 s, `Assert.NotNull() Failure: Value is null` at `:238`.
- **Where:** the warm-up at `:211` **succeeded**. The poison fact was produced (`:228-235`). `ConsumeMatchingAsync(FulfillmentFacts.dlq, correlationId, 90 s)` returned null (`:237`).

**How it was reported:** *"a pre-existing container-contention flake … confirmed by an isolated re-run passing."* That is wrong twice:
- An isolated green run is not evidence about a red one (`CLAUDE.md`).
- The test is not pre-existing: fix round 2 wrote it hours earlier, and the same round disclosed a shared-`.dlq` collision in exactly these cases.

**Finding the right log took four reads, and two of them were the wrong artefact.**
- `/tmp/quality_run2.log` summed to **1796**: the fix-round-2 run.
- `/tmp/claude-1000/scratch_quality{,2}.log` belonged to fix round 3. Its red first run failed `RealInfraMetricsProvenanceTests` with `Assert.Single() … contained 2 items`, the A13 capture race; its re-run was green.
- **The check that caught each wrong one was the per-project sum against the expected total.** Without it, I would have diagnosed a run from a different code state. This is the "number that does not reconcile is a finding" rule applied to picking evidence, not only to reporting it.

**Candidate mechanisms sent to the diagnosis round, each to be reproduced deterministically:**
1. **`ConsumeMatchingAsync` positioning:** it starts after the dead letter is already published.
2. **Every Notifications test host shares production consumer group `"notifications"`** (`KafkaFactStreamSubscriber.cs:115`). A stale member could hold `FulfillmentFacts`' partitions past 90 s.
3. **The retry budget plus rebalance under load** exceeds the window.

The round must fix the proven one at its class, including the sibling `ProjectorDeadLetterTests` case, prove unfixed-fails / fixed-passes under the reproducing condition, and end with a **green** full quality.sh.

## Review round 3 — REJECTED; four guards called "armed" that cannot see their defect, one of them passed by the leader

**Closed and confirmed by the reviewer's own mutations:** D5 (rows 34, 36–37, 65–66/74–78), D6, D7, R5–R8, and the A9 table.
- **Rulings:** row 72's consumer tag is guarded; row 73's layered guard is acceptable; `TryParse`-only integration header asserts are acceptable given the unit value guards; A8 routed to id 74 is accepted.

**New blocking findings, each verified by the leader against the files:**
- **D8.** All three `FactRetryDispatcher` copies record `stopwatch.Elapsed.TotalMilliseconds` from `Stopwatch.StartNew()` (`:69`, `:80`, `:132`), which no test can drive. The tests assert only `>= 0`, so recording ticks instead passed. #7 asserted exact values from an injected clock (`240`/`5750`) with an integration `< 30_000` bound. No ledger row records the swap.
- **D9.** Both new L21 concurrency tests `await Task.Delay(50)` between call A and call B (`NatsStockAvailabilityCheckerTests.cs:112`, `NatsRpcClientIntegrationTests.cs:200`), so the calls never overlap. The plain `NatsHeaders` hoist passed 3/3 at each site.
- **D10.** Row 44's test registers its own `ActivityListener` on `Microsoft.AspNetCore` (`LogCorrelationTests.cs:56-62`), so deleting the host's `.AddAspNetCoreInstrumentation()` (`Telemetry.cs:68`) left it green. `design.md:465` claims `TelemetryWiringTests` asserts that registration.
- **D11.** R4 was half-fixed: all six `*Host.cs` still say *"all three, or a field silently vanishes"*.
- **A13.** All three `MetricCapture.cs` copies append to a plain `List` with no lock.

**D9 is also a leader miss.** Verifying fix round 2, I read *"deterministic via a `Task.Delay(200ms)` widening"* and accepted it without checking **where** the delay was. It was in the **mutation**. A mutation widened until the test fails proves the test can see a defect the test itself never creates. That is `CLAUDE.md`'s *"change of kind, not of probability"* inverted: the kind was changed in the wrong artefact.

A second detail: both tests' comments quote *"make the collision deterministic"* as **`CLAUDE.md`**. The sentence is from **my own addendum brief**, and `grep` finds it nowhere in `CLAUDE.md`. A brief's wording laundered into an attributed rule is how a coordinator's instruction becomes a false citation.

**Fix round 4 (fresh implementer):**
- **D8:** a drivable time source in the three parity copies, exact unit asserts plus a real integration bound, armed by ticks, seconds and deletion, with ledger-row text for the leader to transcribe.
- **D9:** each test creates the overlap itself (a barrier responder), armed by the **plain** hoist with no delay, ≥3 runs; comments corrected.
- **D10:** a host-registration guard armed by deleting the registration, or an honest correction.
- **D11:** six comments reworded, enumerated to zero.
- **A12–A14.**
- **The brief's explicit rule:** *"never widen a mutation to make a test fail — if the named mutation does not fail, the TEST changes."*

**Leader, after it reports:** correct `design.md:465`; add D8's ledger row to §10.

**Backlog:** id 74 gained A13's exclusivity class (`Assert.Single` over a shared capture, nine sites per the review). It is to be enumerated and closed in the 67–70 loop.

**For the proposed `CLAUDE.md` amendment:** a fourth entry. The unit the leader failed to check was **which artefact** the determinism lived in: test versus mutation.

## Fix round 3 — verified by counting the population, not the sample

- **§11 walk matches the design exactly.** Record `:2820` lists exactly design §11's **41** row groups, in the same order, and `comm` over the two first-cell sets gives an **empty difference both ways**.
- **The not-applicable rows match the design's own cells.** Rows 20, 129–133 and 134–145 are marked not applicable / deliberately not ported, as the design's §11 cells declare (`FactCatalog` already registers `order.saga_failed.v1`; feature 28 owns the composed-stack proof).
- **Three rows carry no `›`/`.cs:` evidence**, and all three are accounted for:
  - 109–118 cites the health-probe case through a brace path; the leader has verified that case in all six files repeatedly.
  - 129–133 and 134–145 are the not-applicable rows.
- **The leader's own verdict count (32) undercounted** because rows word their verdict differently ("not applicable, re-confirmed", "partial → closed"). That is a narrow pattern of mine, not a record defect: the agent's 38 verified + 1 partial→closed + 2 not ported = 41 matches the row count.
- **R5:** `:2318` and `:2355` now read "Decorative guards found: **1** (L24)", and the L24 row at `:2349` names the three responder tests.
- **D6:** `FactRetryDispatcherTests › DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsClockReading_NeverALaterOne` exists. The arm `??=`→`=` fails naming the instant, and the Projector-copy mutation fails the parity test.
- **New integration file:** `RealInfraMetricsProvenanceTests.cs` holds 2 cases.
- **Count:** quality.sh **1799** = 1796 + 3 (Orders.Unit +1, Orders.IT +2).
- **State:** idle, no backups, `feature_list.json` at 6 hunks.

**Re-review round 3's brief:**
- re-run the reviewer's own round-2 mutations for D5 and D6 (plus the dispatcher's `??=`), D7 (`CancelAfter`) and L21;
- probe rows 70–73 with its own corruptions;
- verify a fresh §11 sample of at least 10 rows outside its round-2 sample, by case name;
- rule on the `Task.Delay(200ms)` determinism widening, the remaining `TryParse` integration asserts, and the metric-capture concurrency fix.

## Proposed `CLAUDE.md` amendment for the phase-14 human gate — NOT applied (amendments are gated)

**The pattern: three leader briefs in one feature checked the wrong unit, and each cost a round.**

| Brief | Unit it used | Unit the claim was about | What escaped |
|---|---|---|---|
| Group N, N1 re-arm criterion | the **group** that touched a file | the **arm** it made stale | 13 of 27 ledger rows touched after their arm, including a regression (the unbounded in-flight health call) |
| Review round 1 fix, §11 walk | the **class** name | the **test case** | five §11 row groups marked "Exists" with no case (D5) |
| Review round 2 fix, §11 walk | the **reviewer's sample** (19 groups) | the **table** (41 groups) | ~22 groups never walked; found by the leader's own count before re-review |
| Leader's verification of fix round 2 (L21 arms) | "the arm is deterministic" | **which artefact** holds the determinism: the test or the mutation | a `Task.Delay(200)` inserted into the **mutation**, while both tests waited 50 ms between calls and never overlapped; the plain hoist passed 3/3 (review round 3, D9). The tests also quoted the leader's brief as `CLAUDE.md` |

**Why a rule and not care.** `CLAUDE.md` already says *"classify the unit the claim is about"*, but only for **sweeps an agent runs**. All three failures were in **criteria the leader wrote into a brief**. The agent then applied them faithfully, so the defect entered at the brief and every downstream check inherited it. Reviewers caught two of the three, and the leader caught the third only by counting the table before dispatching the re-review.

**Proposed wording, for "Briefing subagents economically":**
- **Name the unit in every acceptance criterion a brief sets:** arm, case, row, site, copy.
- **When a sample exists** (a reviewer's probe set, a previous round's table), say explicitly that it is evidence and **not the population**, and give the command that counts the population.
- **Before dispatching a re-review, count the population yourself** and compare it with the record's.

**Evidence:** the leader's own entries above, *"Whose error it is: mine, in the brief"* (Group N), *"Why it happened — my brief, the second time in one feature"* (round 2), and the round-2-fixes verification (41 vs 17).

## Review round 2 fixes — verified; three items open, sent as fix round 3 before re-review

**Verified closed:**
- **Tests:** 16 new, quality.sh **1796** = 1780 + 16. The per-project deltas sum to 16: Orders.Unit +3, Notifications.Unit +2, Projector.Unit +2, Orders.IT +2, Projector.IT +1, Notifications.IT +1, Gateway.IT +5.
- **D7:** `Gateway.IntegrationTests/HealthProbesTests.cs:256` pauses a real Mongo.
- **R6:** the R58 cell cites six `LogCorrelationTests` files.
- **R7:** both remaining `/media/` hits are quotations inside the correction.
- **L21:** all three `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` cases exist (Orders unit adapter, Orders IT stock checker, Gateway IT RPC client).
- **State:** idle, no backups, `feature_list.json` at 6 hunks.

**Leader design edits (each marked in place):**
- **L21 (`:407`)** cites those three cases, and notes the old `NatsRpcClientTests` never existed.
- **L24 (`:410`)** names the three production-responder trace tests, and records that the original guard was decorative for the responders (D1).

**Still open, sent as fix round 3:**
1. **The §11 walk covered a sample, not the table.** Design §11 has **41** row groups (counted by `awk`/`grep` over the §11 table). The section walks 17 table rows and scopes itself to *"the 19 originally-sampled row groups"*. My brief said *all*; the agent took the reviewer's sample as the population. That is the sweep-completeness failure again, in a new disguise: **a reviewer's sample used as the enumeration**. Fix round 3 walks all 41 with a pasted case-name grep line per row.
2. **R5 was deferred, not done.** "Decorative guards found: 0" still stands at `:2318` and `:2355`. The round wrote that the leader corrects the N1 table, but that table is in its own record.
3. **D6's other half.** Unit tests now assert exact header values, killing the reviewer's probe. But they **supply** `FirstFailedAt`. The dispatcher's capture, `firstFailedAt ??= clock.UtcNow` (`:93`, identical in all three copies), has no test: `grep FirstFailedAt` over tests returns only the three publisher tests. `??=`→`=` would record the last failure as the first with everything green. Fix round 3 adds a fake-clock dispatcher test and proves the parity test covers the other copies.

## Review round 2 — REJECTED; verified by the leader, and the root cause is the leader's brief again

**Closed and confirmed:** D1, D2, D4, R1, R2, R3, and row 6–7. The reviewer's own probes killed the responder trace mutations on value, `IncludeScopes = false` in Notifications, and `ActivityTrackingOptions = None` in Billing.

**New blocking findings, each verified by the leader:**
- **D5 — five §11 row groups the walk marked "Exists" have no test case.**
  - Rows 36–37: consume-side trace continuation in Projector and Notifications. A fresh consumer root left Projector 58/58 green.
  - Row 34, row 44 and rows 45–46: Gateway client injection, server span, problem-json trace id. **`grep -l` for any trace id over Gateway test files returns 0.**
  - Rows 65–66 and 74–78: the `cancelled` outcome. Hard-coding `completed` left Orders 433/433 green.
  - Rows 48–49 and the rows 4–5 defaults are partial.
- **D6 — failure-time headers asserted only with `DateTimeOffset.TryParse`** (Orders `:94-95`, Notifications `:112-113`, Projector `:79-80`). Rendering `FailedAt` into `x-first-failed-at` left Projector 58/58 green.
- **D7 — the Gateway Mongo probe's `CancelAfter` is unguarded.** The two `MongoHealthCheck` copies are **not** byte-identical (`6cf82aa…` vs `5dae342…`). The Mongo parity fact checks only structure ("names readModel, same 2 s timeout, same exceptions"), and only Projector pauses Mongo. So my round-1 routing *"closed in the fix round rather than filed onto id 68"* was not true for the Mongo pair.

**Why it happened — my brief, the second time in one feature.** The round-1 fix brief said to verify each §11 row *"by `grep -n` of the literal case **or class** name"*. A class existing says nothing about its cases, so the walk passed at file granularity. This is the same failure as the Group N brief's group-granular re-arm criterion, and both are `CLAUDE.md`'s *"classify the unit the claim is about"*. The unit of a §11 row is a **test case**; the unit of arm staleness is an **arm**. Fix round 2's brief now says so in words, and names the loophole as mine.

**Corrections:**
- **R4 (L25, design) — corrected by the leader.** `ActivityTrackingOptions` is on by default in the generic host, measured by the reviewer's deletion probe. So the cell now reads *"two explicit settings plus one framework default an explicit `None` defeats"*, marked as measured, not read from framework source.
- **R9 (L21, design) — bigger than a wrong citation.** `NatsRpcClientTests` does not exist (0 files), and the N1 table repeats it (record `:2346`). L21 claims *three* outbound NATS sites build fresh `NatsHeaders` per call, but only **one** is guarded:
  - `NatsSagaCommandsAdapterTests`' concurrency case (Orders), armed in A3c (`:1459`, failed 5/5).
  - The Gateway's `NatsRpcClient` has a single non-concurrent test (`NatsRpcClientIntegrationTests` › `CallAsync_DecodesTheSuccessReply_AndSendsTheCorrelationAndRequestIdHeaders`).
  - `NatsStockAvailabilityChecker`, whose headers A3 added, has none.

  **Sent to fix round 2 as an addendum:** concurrency guards for both, armed by hoisting the headers into a shared field, the record corrected, and the three guards' literal case names reported. `design.md`'s L21 row gets **one** edit, after those names exist.
- **R5–R8 (record and R58 cell) — in fix round 2.** R7: the `/media/...` path is an unversioned copy, not the git checkout (`fatal: not a git repository`; inode differs), though the cited file is `cmp`-identical.

**Advisories:**
- **A8 (positional `ConsumeOneAsync` on shared `.dlq` topics) — filed as backlog id 74**, phase 14, joining the 67–70 loop. It is a test-shape class whose population is not yet enumerated.
- **A9 (`HealthProbeTimeoutTests` passes any `TimeSpan` field) — folded into fix round 2**, since D7 is its live instance. The fix round must show, as a search result, a behavioural test per `IHealthCheck` implementation (14).

## Phase-14 sequence after feature 27 closes — written while re-review round 2 runs

Every entry below is strictly sequential, for two reasons already paid for this phase: **never two builds at once**, and **one writer for `feature_list.json`**. Order and the reason for each position:

1. **Id 71 `operator_note_survives_the_compensation_branches`** comes first, because it shares files with id 62.
   - Its acceptance now includes #7's synthetic `orders.cancel.requested` envelope on both compensation branches, which closes feature 27's A2 parity divergence (a parked operator-cancel compensation dead-letters nothing).
   - It touches `CancelOrderCommandHandler.cs:167/:194`, `SagaFactHandler.cs:187` and `CancelOrderCommandHandlerTests.cs:146-148`.
   - Brief inputs are already prepared in *"A ledger miss in feature 27's A2"* below.
2. **Id 62 `operator_cancel_races_saga_forward_progress`** comes second, on the same handler and compensation path, so after 71 rather than alongside it.
   - Its failing reproduction starts from `OrdersCancelAcceptanceTests`' two isolated branches (`:85-91`, `:155-158`), adding back the omitted responder.
   - Brief inputs are prepared in *"Prep for backlog id 62"* below: #7's Finding 1 at `review_orders_catalog_and_cancel_responders.md:100`, the named defence, and #8's terminal-rejection classification already in place.
3. **Id 73 `notification_send_degrades_on_permanent_failure`**: Notifications only, no file overlap with 71/62. The MailKit signal classification is its ledger row.
4. **Id 72 `test_matrix_rows_that_outlived_their_named_closer`**: Gateway tests plus `test-matrix.md` (R1/R24/R61 cells and the standing paragraph). It runs after 71/62/73 so its recount of the coverage summary counts the final Status cells, not a moving target.
5. **Ids 67–70, one guard-hardening loop.** Its first task is one repository-wide enumeration of the class, as a search result, before any fix, per `CLAUDE.md`. It runs last because it sweeps test shapes the entries above may add to.

**Revised again 2026-09-11, after id 77 was filed.** The running order is now **71 → 77 → 62 → 73 → 76 → 72 → the 67–70 loop with 74**, with 75 on the human gate.
- **Why id 77 goes right after 71:** it is small (one guard test plus confirmation under `quality.sh`), touches only `tests/Architecture.Tests`-style harness files and no service code, and every later entry depends on full runs staying green on this machine's thin inotify margin. Guarding the setting first means a silent removal is caught by a named test, not rediscovered as a red run in the middle of another feature.

**Previous revision, after ids 75 and 76 were filed.** The order was **71 → 62 → 73 → 76 → 72 → the 67–70 loop with 74**, with 75 on the human gate:
- **Id 71** — in its review fix round.
- **Id 62** — unchanged position. It gains A3's bullet: the operator-note lookup must pick the operator's own note under the race.
- **Id 73** — unchanged, Notifications only.
- **Id 76 `application_layer_depends_on_infrastructure_unguarded`** — new, placed by file overlap:
  - **after id 62**, because 62 changes `CancelOrderCommandHandler`, one of 76's import sites, and two rounds must not rewrite the same file;
  - **before the loop**, because 76 moves the RPC payload records into `src/Contracts`, which id 70's payload-test guards cover, so the loop's enumeration must see the final placement.
- **Id 72** — its matrix recount still goes after the entries that change Status cells.
- **Ids 67–70 + 74** — last, as before.
- **Id 75 `dead_letter_first_failed_at_semantics`** — **waits on the human gate** for the SA-3 ruling. Nothing is built until the amendment is ruled, because it changes `specs/shared/` in both repositories.

**Then phase 14 closes:** a final quality.sh, and the "full wrap-up" only on the user's word.

## Review round 1 fixes — all verified; re-review round 2 dispatched

**Row 6–7 addendum, verified by the leader:**
- **Six new tests:** `KafkaDeadLetterPublisherTests` ×3 (Orders, Notifications and Projector UnitTests), 2 `[Fact]`s each, driving the real publisher through its `IProducer` constructor.
- **The active-span case asserts the value.** It checks `Assert.Equal(activity!.Id, traceparent)` and that the header parses back to the same `TraceId`, where the integration tests only checked presence.
- **The no-span case asserts `DoesNotContain` on `traceparent`.**
- **All six arms fail as intended:** fabricated id → `Strings differ` (pos 3); header always added → `Filter matched`. All `cmp`-restored.
- **Count:** quality.sh **1780** = 1774 + 6 (+2 each in Orders.Unit 431→433, Notifications.Unit 78→80, Projector.Unit 116→118).
- **State:** idle, no backups, `feature_list.json` untouched at 6 hunks.
- **Record:** row 6–7 at `:2506` is now "Ported", with the mechanism corrected at `:2542` (unreachable only while `Telemetry.cs` registers `AddSource`) and the addendum at `:2648`. #7's `kafka-dlq-publisher.spec.ts` was read directly (`:2657`).

**For the re-review:** the addendum cites #7's checkout at a `/media/…/Elements/…` path, not the sibling path this repository uses. The reviewer is asked to check with `realpath` whether it is the same checkout.

**Feature 27 total over the review cycle: 1758 → 1780 (+22).** That is 16 from the fix round and 6 from the addendum, and every closure was armed.

## Review round 1 fix round — verified except §11 row 6–7, which is sent back

**Verified by the leader:**
- idle, no backups, `feature_list.json` at 6 hunks (untouched);
- `tasks.md` N1–N6 ticked;
- the new test files' `[Fact]` counts match the report: 1+2+2 responder trace-continuation, 4 log-correlation, plus 2 `FACT_RETRY_*` substitution, 1 DLQ cross-topic sum and 4 NATS/Mongo parity = **16**;
- quality.sh **1774** = 1758 + 16;
- R56 and R57 cells cite all three responder tests;
- all four retired "reviewer found it" comments corrected (the search returns nothing at the four sites; enumeration recorded at `:2559`);
- `x-first-failed-at` now asserted in all three dead-letter tests (Orders, Notifications, Projector).

**D3's reclassifications, judged row by row:**
- **Rows 4–5 (`FACT_RETRY_*`) — legitimate.** The design's own parenthetical folds them into `*ProgramConfigurationTests`, and the substitution cases now exist for Notifications and Projector.
- **Rows 67–69 (DLQ depth) — legitimate.** The multi-message sum and the missing topic already lived in `MetricsExposureTests` under other names, and "each topic independently" was added.
- **Row 6–7 (the DLQ publisher's `traceparent`) — not legitimate, and wrong in its reasoning.**
  - **Wrong mechanism.** The record calls the no-active-span branch unreachable because the consumers "ALL start a consume Activity unconditionally" (`:2542`). But `ActivitySource.StartActivity` returns **null** when no listener samples the source. The consumers' unconditional call (Orders `SagaFactsConsumer.cs:139-140`, Notifications `:151-152`, Projector `:121-122`) guarantees nothing. A non-null activity depends on `Telemetry.cs` registering `AddSource(OtcActivity.SourceName)` (Orders `:70-71`, Notifications/Projector `:61-62`) under the default sampler.
  - **Cheaply testable.** The branch is inline in all three publisher copies (`Activity.Current?.Id` → `AddHeader`), not in the shared carrier. All three take an `IProducer<string, byte[]>` in a constructor (`:25`, `:34`, `:25`), and **no test constructs one**.
  - **The active-span half is presence-only for two copies.** `NotificationDeadLetterTests.cs:114` and `ProjectorDeadLetterTests.cs:81` assert only `ContainsKey("traceparent")`, so a fabricated header would pass.
  - **Sent back:** one `KafkaDeadLetterPublisherTests` per copy, covering both halves, each armed two ways (fabricated id; header always added); the mechanism corrected in the record; quality.sh reconciled against 1774.

## Review round 1 — REJECTED; findings verified by the leader before dispatching the fix round

**The reviewer's probes of the leader's claims all held:**
- the L27 arm fails in 10 s naming the bound;
- the pooling guard fails at 3002 ms;
- the LongRunning guard fails at 2003 ms;
- the parity test names Billing;
- in-flight liveness fails at 2005 ms.

**Its own probes found four blocking gaps.** The leader verified each:
- **D1 — no test drives a production RPC responder's trace continuation.** The reviewer armed it: a fresh trace root in `StockRpcResponder` left Fulfillment 130/130 + 61/61 green. `TraceContextPropagationTests.cs:56-58`'s comment admits the stand-in. The responders are `OrdersCreateResponder`, `StockRpcResponder` and `BillingRpcResponder`. N1's "0 decorative guards" was false for L24, and the R56/R57 cells overclaim.
  - #7 guarded only Orders (`orders-create.controller.spec.ts:134`). #7's own review `:72` records that its Fulfillment/Billing responders did not extract trace context at all.
- **D2 — `CapturedConsole` log-capture tests exist only in Orders and Gateway** (the `grep -l` count per project is 2 and 2). `IncludeScopes = false` in `NotificationsHost` left Notifications green. Design §11 promised one test per service.
- **D3 — §11 names three classes, and 0 files exist for each:** `FactRetryOptionsTests` (`design.md:445`), `KafkaDeadLetterPublisherTests` (`:446`), `KafkaDlqDepthTests` (`:474`). `x-first-failed-at` is asserted only in Orders' `SagaDeadLetterTests`, so the Projector corruption stayed green. **Nobody walked §11 against delivery**, and the impl record mentions §11 zero times.
- **D4 — `tasks.md:103-108` N1–N6 unticked.**

**The same pass is also a finding against the leader.** My consolidated hand-off listed 12 items and none of these four. I verified what the implementers claimed, but never walked design §11, the design's own enumeration of #7's tests, against the files delivered. That is the "port the guards" rule, whose instrument sat in the design unexamined.

**Record corrections:**
- **R1:** impl `:2322` vs `:2316`, and L20/L25 citations.
- **R2:** design §8.3 and L26 say #7's *reviewer* found the unbounded Gateway NATS probe, but #7's review `:70-72` calls it *"already disclosed … in the implementer's own A8 section"*. L25's "eleven call sites" is uncited (the reviewer counted 8 at `95e883a`, 21 at HEAD).
- **R3:** R56/R57 cells.

**Rulings on the hand-off:**
- The A2 envelope divergence is parity, not spec; routing it to id 71 is accepted.
- The matrix recount is confirmed.
- The package set is confirmed by element.
- All four residuals are accepted.

**Leader actions taken while the fix round runs:**
- **R2 corrected in `design.md`, each correction marked in place as made at review round 1.**
  - **§8.3 (`:339`) and L26 (`:412`)** now say #7's **implementer** found and disclosed the unbounded Gateway NATS `rtt()` probe (`order-to-cash-nestjs/progress/impl_observability_reliability.md:912`, mechanism `:927`, section A8 `:879`), and that #7's reviewer confirmed it (`review_observability_reliability.md:70-72`).
  - **L25 (`:411`)** replaces the uncited "eleven" with **my own reading**, not the reviewer's relayed number. `git grep -nE '\.\.\.\((traceId|[a-zA-Z]+TraceId) \? \{ traceId' <rev> -- apps`, spec files excluded, gives **8 at `95e883a`** ("feat(orders): requestId dedup, dead-letter, trace propagation", 2026-08-27), with all 8 file:line cited in the row, and **21 at HEAD**.
- **The correction was enumerated on the RETIRED wording, not stopped at the design.** `reviewer found it|its reviewer found|eleven … call sites` over all `.md`/`.json`/`.cs` files outside design.md returns 6 hits:
  - three quotations, which stay (review `:108`, `:196`; this file `:26`);
  - **three live comments** — `tests/Architecture.Tests/HealthProbeTimeoutTests.cs:9`, `src/Orders/Infrastructure/Health/NatsHealthCheck.cs:13`, `src/Gateway/Infrastructure/Health/NatsHealthCheck.cs:11`.

  Those are `src/`/`tests/`, so not mine to edit. They were sent to the fix round, which is already syncing that family's comments, with an instruction to widen the search for wrapped comments.

  **My own first pattern was too narrow.** It required `reviewer found it` on one line, and one comment wraps across lines. Widened to `grep -nE "reviewer found"` over `src/` and `tests/` (bin/obj pruned by path), the search returns 7 hits:
  - **4 live sites of the retired claim.** The three above, plus `tests/Gateway.IntegrationTests/HealthProbesTests.cs:15`. The fourth was sent to the fix round as a follow-up.
  - **3 different, correct claims, which stay:** `Cqrs.UnitTests/DispatcherScopeTests.cs:9` (#8's own reviewer, dispatcher scope), `IdempotentConsumerTests.cs:50` (#7's reviewer on R17), `CreditSimulatorTests.cs:129` (#7's reviewer on R44).

  In `design.md`, `reviewer found|eleven` now matches only inside the three correction notes (`:339`, `:411`, `:412`).
- **#7's degrading notification sender is filed as backlog id 73** (`notification_send_degrades_on_permanent_failure`, phase 14).
  - #7 built it in `ad90de6` explicitly as *"Not a feature_list.json entry"* (`impl_notification_degradation.md:3`), so #8's copied backlog never carried it: a fix that was never written down as work.
  - The acceptance requires the classifier rebuilt on **MailKit's** own signals, since #7 read nodemailer's `responseCode`/`code`. It also requires a ledger row, #7's tests enumerated by assertion, and all three mutation families.
  - Backlog validator green: 72 entries, and all added lines belong to ids 27/31/71/72/73.

**Routing:**
- **Fix round:** D1–D4, the record parts of R1/R2, R3, and the NATS (5) / Mongo (2) health-check parity extension, **closed here instead of filed onto id 68**.
- **Leader:** the design.md parts of R2.
- **Leader, to check:** #7's degrading notification sender (`ad90de6`) has no #8 counterpart.

## Group N round 3 — verified; the review is dispatched

- **Health tests:** in all six `HealthProbesTests.cs` the in-flight readiness call now starts through `TryGetTimedAsync(..., _pausedReadinessBound)`, with a path-naming assertion ("the initial in-flight call"). A search for token-less `GetAsync("/health/ready")` finds no hit. The six remaining `await readyInFlightTask` lines unpack a bounded `(response, elapsed)` result.
- **L27:** the re-arm now fails in **10 s** naming the 6 s bound, not 1 m 42 s.
- **L26, L12, L22 and L24:** armed, each with a verbatim failure in the record at `:2229`.
- **L10:** the test now polls the real `FactRetryDispatcher`'s dead-letter side effect, with a message naming the bypass, and also asserts `Attempts == 1` and `poisonDispatcher.Invocations == 1`. Residual for the reviewer: the message names the bypass by inference, so a hung dispatcher would give the same message.
- **Clean state:** nothing running, no backup files or mutation markers remain, `feature_list.json` hunks unchanged, quality.sh **1758** = 1758 + 0.
- **Review brief:**
  - rule on the 12 hand-off items;
  - arm L27 by failure time and message;
  - arm both production fixes and the parity test;
  - arm in-flight liveness;
  - rule on the A2 envelope routing to id 71;
  - enumerate #7's **assertions** for dropped guards, by content;
  - check at least 6 ledger history-half citations;
  - verify the matrix reconciliation;
  - count the package set by elements.

## Group N — reported complete (id 27 `in_review`); N1 sent back for one enumeration round

**Verified by the leader:**
- No build or test process alive (`ps`, self-match excluded).
- Id 27 is `in_review`, and the backlog validator is green with *"no feature in_progress"*.
- `feature_list.json` hunks are unchanged: N6's edit sits inside the existing line-406 hunk.
- `.env.example:168/174/175` carry `OTEL_EXPORTER_OTLP_ENDPOINT`, `FACT_RETRY_MAX_ATTEMPTS` and `FACT_RETRY_BACKOFF_MS`.
- Summary rows are now §2 `8|8|0|0`, §3 `11|10|1|0`, Total `63|58|4|1`. The agent recounted independently, and the sums reconcile two ways.
- The N1 table has 28 rows (27 guarded plus L5).
- quality.sh gave 1758 = 1758 + 0.

**N1's conclusion rests on a false premise.** It says zero rows needed re-arming because no later group touched a guard's subject or test file after its arm. The record contradicts that:
- L26's A4e half cites the six hard-coded-`Up()` arms, which ran against the **original** `HealthProbesTests.cs`. Both A4 fix rounds then rewrote all six files.
- L27's latest arm, round 1's item (c), predates round 2's change to the same file.
- Both rows' subject files were rewritten after A4c/A4d: `KafkaHealthCheck` to `StartNew(LongRunning)`, and `MsSqlHealthCheck` with `Pooling = false`.
- Candidates to check: L15/L17, whose outbox subject files A3d changed after A2's arms.

The guards very likely still fail. What is wrong is that the record states an unchecked fact as the reason.

**Whose error it is: mine, in the brief.** The record's full sentence shows the mechanism: *"a row is re-armed only if its guard test file or the source file it is about was touched by a group later than the one that recorded its arm"*. That is my brief's wording, *"modified by a LATER group after the recorded arm"*, applied faithfully. The A4 fix rounds belong to Group A4, the same group that recorded the arms in A4c–A4e. So a criterion granular to the **group** structurally cannot see a change made inside that group after its own arm.

The staleness question is about each **arm**: was anything touched after *this arm's* record line? That is `CLAUDE.md`'s *"classify the unit the claim is about"*, broken by the leader who keeps quoting it. It is the same shape as the sweep that filters on the wrong property. My first round-2 message to the agent called the premise false without saying so; a follow-up corrected the attribution, so the record will say the briefed criterion was replaced, not that the agent erred. **Sent back** for a per-row enumeration of later touches, as a search result, with re-arms of every flagged row against current code. No full quality.sh is needed if every mutation is restored `cmp`-identical.

**Round 2 result, verified by the leader.**
- **Arm-granular enumeration:** **13 of 27 rows** had a later change to a guard or subject file, against the leader's own 2–4 candidates. The group-granular criterion would have shipped all 13 unchecked.
- **Re-armed:** 10 rows, each `cmp`-restored. No backup or mutation markers remain under `src/` or `tests/`. `feature_list.json` hunks unchanged, and nothing running.

**Round 2 also surfaced three problems, sent back as round 3:**
1. **A regression inside A4's own round 2, found by the L27 re-arm.**
   - **The defect.** `HealthProbesTests.cs` starts its in-flight readiness call as `client.GetAsync("/health/ready")` with **no token** (Notifications `:181`, Fulfillment `:180`), then drains it with an unbounded `await readyInFlightTask`. So a probe that exceeds its bound fails only at `HttpClient`'s **100 s default**, as a bare `TaskCanceledException`. That is exactly what A4 round 1 removed. The L27 re-arm failed at 1 m 42 s, where round 1's version of the same arm had failed in ~10 s naming the 6 s bound.
   - **The record gave the opposite mechanism.** It said the failure surfaced *"through the addenda's own bounded per-call token rather than the original arm's 100s HttpClient default."*
   - **Why my round-2 verification missed it.** I checked that `_liveWhileReadyInFlightBound` and `samplesWhileReadyInFlight >= 3` existed in all six files. I did not read the line starting the call they sample around, which is checking that the new assertion exists without checking the call it depends on.
2. **L12, L22 and L24** were "checked by reading" and not re-armed: the "probably still fails" this round exists to replace.
3. **L10's arm fails as a generic `System.TimeoutException`.** Any hang would produce it, so the failure message cannot name the bypass it is meant to prove.

**Enumerated by the leader, not inferred from two files:** `grep -nE 'GetAsync\("/health/ready"\)'` over the six `HealthProbesTests.cs` (bin/obj pruned by path) returns **6 hits, one per file**:
- Billing `:181`, Fulfillment `:180`, Gateway `:159` (`gateway.Client`), Notifications `:181`, Orders `:187`, Projector `:180`.

`grep -n 'await readyInFlightTask'` returns **6 matching drains**: `:198`, `:197`, `:176`, `:198`, `:204`, `:197`. So the regression covers the whole family, not two copies.

**For the review hand-off (item 2):** A4's round-2 in-flight sampling reintroduced an unbounded readiness call in all six files. Group N's arm-granular L27 re-arm exposed it, and round 3 fixes it. The reviewer should confirm the fix by the L27 arm's failure **time and message**, not only by the test passing.

**Round 3 asks for:**
- the unbounded call bounded through `TryGetTimedAsync` in every file with the shape, enumerated first;
- L27 re-armed, now expected to fail in ~10 s naming the bound, and L26 re-armed because the test changed again;
- L12, L22 and L24 armed;
- L10's test made to assert the bypass directly;
- both record sentences corrected;
- a full quality.sh reconciled against 1758.

**Status after round 3:** if it confirms, the feature-27 review goes out with the hand-off below.

## For the feature-27 review — consolidated hand-off (every item already verified; Group N's record lines added when it reports)

**Findings to rule on:**
1. **A2 ledger miss: commands triggered by RPC rather than by a fact.** #8 dead-letters nothing for a parked operator-cancel compensation; #7 dead-letters a synthetic `orders.cancel.requested` envelope. R29 is met, so this is parity, not the spec. The test at `CancelOrderCommandHandlerTests.cs:146-148` guards the divergence the wrong way round, and the handler's `design.md §4.2` citation is unsupported. Routed to id 71. Full evidence: the section *"A ledger miss in feature 27's A2"* below.
2. **A4's two production defects, found and fixed in round 1 and guarded in round 2.** The MS-SQL probe's pooled connection hung more than 500 s against a paused server. The Kafka probe's synchronous `GetMetadata` starved liveness under a constrained pool. Evidence: `impl_observability_reliability.md:1976` and `:2046`.
3. **The design §5.2 factual error.** It claimed all three NATS request sites already build headers; `NatsStockAvailabilityChecker` built none. Corrected in A3, and the design text itself was not edited.
4. **`test-matrix.md`'s stale coverage summary** (§2 and §3 unmoved by feature 27's R16/R29 flips). Group N recounts. #7's own *"Why the numbers moved"* paragraph records the identical defect.

**Stated limits, to be named in the review rather than discovered:**
5. A2's second-park check uses a **3 s** settle window. A wrongful claim committing more than 3 s after the park would evade it; the real gap is sub-second.
6. **Comment drift is unguarded by design** inside the banner-exempt `IdempotentConsumer` and `FactRetryDispatcher` parity families. It has already happened once. The new `HealthProbeCopyParityTests` deliberately does **not** exempt comments.
7. The `KafkaHealthCheckLongRunningTests` residual: a `GetMetadata` failing within milliseconds could fail the *fixed* code between return and the `IsCompleted` read. Not observed.
8. **R56's composed-stack leg** is deferred to feature 28. The deferral is ratified, and #7's §8 summary row is identical.

**Deliberate choices, recorded:**
9. Design §9.1 justifies the hand-rolled `IHealthCheck` by analogy with #7 refusing `@nestjs/terminus`, a package, but .NET's built-in health checks need no package. The port is kept because it produces `openapi.yaml`'s exact `HealthResponse`; the rationale is flawed and the decision stands.
10. **The Projector checks NATS, which #7 did not**, justified by `projector_read_model`'s PR45. It is an addition, not a dropped guard.
11. **Health port names diverge from #7** (`ORDERS_HEALTH_PORT` versus #7's `ORDERS_PORT`); the numbers 3002–3006 match. Environment variable names are not a parity contract here.
12. **For the commit message, read off `git diff -U0 -- '*.csproj' Directory.Packages.props` (no untracked csproj):**
    - **3 new `PackageVersion`s**, all 1.18.0: `OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore`. None removed.
    - **`OpenTelemetry.Extensions.Hosting` was already pinned** and unused. This feature is its first consumer, so it is new as a `PackageReference`, not as a version.
    - **19 `PackageReference` elements added:**
      - `OpenTelemetry`, `.Exporter.OpenTelemetryProtocol` and `.Extensions.Hosting` in each of Billing, Fulfillment, Notifications, Orders and Projector (15);
      - those three plus `.Instrumentation.AspNetCore` in the Gateway (4).
    - **18 `PackageReference` elements removed:** `Microsoft.Extensions.Hosting`, `.Options` and `.Logging.Abstractions` in all five non-Gateway services, plus `Microsoft.Extensions.Hosting.Abstractions` in Billing, Fulfillment and Orders (Billing 4, Fulfillment 4, Orders 4, Notifications 3, Projector 3). They became redundant with the shared framework, and NuGet's pruning warning NU1510 is an error under `TreatWarningsAsErrors`.
    - **5 `FrameworkReference Include="Microsoft.AspNetCore.App"` elements**, one per non-Gateway service. No NuGet package.
    - **A counting trap caught here:** my first raw counter reported 25 added `PackageReference` lines and 16 `FrameworkReference` lines. It counted **comment lines mentioning those words**. The element counts above are the reading; the raw ones must not reach the commit body.

## Group A4 — CLOSED after two fix rounds

**Round 2, verified by the leader:**
- **Eight new tests, counted from the files:**
  - `MsSqlHealthCheckPoolingTests` (1), in Fulfillment.IntegrationTests;
  - `KafkaHealthCheckLongRunningTests` (1 each), in Orders, Notifications and Projector UnitTests;
  - `HealthProbeCopyParityTests` (4), in Architecture.Tests.
- **Reconciliation:** quality.sh total **1758** = 1750 + 8. I re-added the eighteen project figures myself; the per-project deltas are +4 Architecture, and +1 each for Notifications.Unit, Orders.Unit, Projector.Unit and Fulfillment.Integration.
- **Copies:** all four `MsSqlHealthCheck` and all three `KafkaHealthCheck` copies are byte-identical **including comments**, modulo service namespace. The parity test uses literal path sets and does **not** exempt comments, so the banner-exemption gap A3 left in two families is not repeated here.
- **Pooling guard:** removing `Pooling = false` fails the new test on the first post-pause call at 3002 ms, identically in 3 of 3 runs. It also fails the existing A4e case, at 8005 ms against the 6 s bound.
- **LongRunning guard:** it asserts both that `CheckAsync` returns in < 250 ms and that its task **is not already completed**. A synchronous implementation always returns a completed task, so the guard stays armed even where TEST-NET-1 `192.0.2.1` fails fast.
  - Residual for the reviewer: if `GetMetadata` ever failed within the few milliseconds between return and the `IsCompleted` read, the *fixed* code could fail spuriously. That hasn't been observed, and it's unlikely with a literal IP and no brokers.
- **Liveness while readiness is in flight:** all six files use `_liveWhileReadyInFlightBound = 500 ms` and `samplesWhileReadyInFlight >= 3`.
  - Arm (a): delaying liveness only while ready is in flight fails the test at 2002 ms, while the pre-round-2 test **passed** against the same mutation.
  - Arm (b): an instant Down fails the sample count.
- **Record:** both errors corrected in place (noted at `:2120`), and no Scratch files remain.

**My own lapse, recorded because the rule is mine:** twice this session I checked for a live quality.sh with `pgrep -f "quality.sh…"`, and both times it matched my own shell's command line: two phantom `bash` PIDs, gone by the next `ps`. That is exactly the self-match `CLAUDE.md` forbids. I use `ps -p <pid>` from now on.

**Group N dispatched** with the pre-checks below folded into its brief. The brief names the stale §2/§3/Total recount as feature 27's own work, and forbids touching R1/R24/R55/R61 and the standing paragraph, which belong to backlog id 72.

## A ledger miss in feature 27's A2 — found while prepping id 71; a parity divergence, not a spec violation

**The divergence.** When an operator-cancel compensation command (`stock.release` or `credit.release`) exhausts its retries and parks:
- **#7** dead-letters a synthetic `orders.cancel.requested` envelope to the orders facts topic's `.dlq`.
- **#8** records `order.saga_failed.v1` and publishes **no** `.dlq` copy.

**#7, read from its checkout:**
- The port makes the envelope **mandatory**: `apps/orders/src/application/ports/saga-command-store.port.ts:40` and `:51` declare `triggeringEventEnvelope: Envelope`, non-nullable.
- So the cancel handler must build one on both branches: `cancel-order.handler.ts:175` (`stock.release`) and `:234` (`credit.release`), via `buildTriggeringEnvelope` at `:272`, which also carries the operator `note`.
- The park hook publishes it unconditionally: `saga-first-park-dead-letter-handler.ts:44-46`, with no skip branch.
- It is guarded: `cancel-order.handler.spec.ts:178` and `:220` assert `eventType === 'orders.cancel.requested'`.
- The mechanism arrived with #7's feature 41 (`32da6e9`), which ran **after** #7's feature 27 had created the column.

**#8:**
- The port is nullable.
- `CancelOrderCommandHandler.cs:167` and `:194` pass `triggeringEventEnvelope: null`, commented *"RPC-triggered — no triggering fact envelope to carry (design.md §4.2)"*.
- `SagaFirstParkDeadLetterHandler.cs:96-105` skips the `.dlq` publish with a log line.
- **The guard is inverted:** `CancelOrderCommandHandlerTests.cs:146-148` asserts `Assert.Null(triggeringFact.Envelope)` and `Assert.Null(triggeringFact.Topic)`, citing the same §4.2. `OR3_AWinningClaimWithNoTriggeringEnvelope_AppendsTheFactButPublishesNoDlqCopy` encodes the skip.
- **The ordering is reversed:** #8's feature 41 (phase 13) predates the column (phase 14 A2), so there was nothing to fill when it ran, and A2 threaded envelopes only through the fact-triggered path.

**Why it is not a spec violation.** R29 (`specs/shared/requirements.md:227-233`) says *"on exhausting the attempts THE SYSTEM SHALL route the **triggering fact** to the dead-letter topic"*. An operator cancel has no triggering fact, so #8's skip complies. #7's synthetic envelope is an implementation choice. That makes this a **parity divergence**, whose operational cost is that a parked operator-cancel compensation leaves no redrive artefact in #8.

**Why it is a ledger miss.** The design's cited section §4.2 (`design.md:191-193`) speaks only of `SagaFactsConsumer`/`SagaFactHandler` threading **fact** bytes. A search of the whole design for `RPC-triggered|operator.cancel|orders\.cancel|no triggering` returns **nothing**. So no ledger row asks *"#7 relied on a non-nullable envelope port; what supplies that in #8?"*. The comment citing §4.2 for the `null` is a citation the section does not support. `grep -rn "orders\.cancel\.requested"` over #8's `src/` and `tests/` returns nothing: #7's guard was not ported. It was inverted.

**Routing.** Not a feature-27 blocker, because R29 is met.
- **The feature-27 reviewer** gets this with its evidence, as a disclosed A2 ledger miss to rule on.
- **Id 71 is its natural home.** Id 71 carries the operator note across the compensation branches, and #7's `buildTriggeringEnvelope` is the very envelope that carries it, so building that envelope closes both. Once Group N's N6 releases `feature_list.json`, id 71's acceptance gains:
  - (a) the synthetic `orders.cancel.requested` envelope on both branches, with `note` when present and the orders facts topic;
  - (b) `CancelOrderCommandHandlerTests.cs:146-148` flipped to #7's assertions;
  - (c) an integration assertion that a parked operator-cancel compensation publishes a `.dlq` copy byte-equal to the stored envelope;
  - (d) the misattributed `design.md §4.2` comments at handler `:167`/`:194` and test `:144-145` corrected;
  - (e) a ledger row citing #7's port `:40`/`:51`.
- **Id 71's stale citation** also gets corrected: it names `SagaFactHandler.cs:167`, and the `order.Cancel(...)` call is now at `:187` (A2 added lines). That call still passes no `note:`, so id 71's premise holds.
- **DONE while Group N round 2 runs** (`feature_list.json` free: round 2 is barred from it, and no reviewer is running):
  - id 71 gained two acceptance bullets. One requires #7's synthetic `orders.cancel.requested` envelope on both branches, with the file-and-line citations into #7. The other ports #7's two assertions, corrects the §4.2 misattribution, adds a byte-equal `.dlq` integration test armed by restoring the `null`, and adds a ledger row.
  - id 71's notes now cite `SagaFactHandler.cs:187`.
  - Bullet 5's `test-matrix.md:127` was checked and is still R29's row.

## Id 62 — citations REFRESHED after id 71 moved the code (read while id 77's review ran; supersedes the line numbers in the older prep below)

**Acceptance (5 bullets, `feature_list.json` id 62):**
- [0] a reproducing test that **fails on today's code** before any fix;
- [1] the defence stated with its throughput cost;
- [2] R25's precondition-unmet ignore must stay a no-op, never a retry storm;
- [3] armed by removing the fix;
- [4] *(added from id 71's A3)* `FindOperatorCancelNoteAsync` exercised **under the race**, so the note that lands is the operator's own, armed by position-based selection.

**Where the race lives, at current line numbers:**
- **The operator decision:** `src/Orders/Application/Commands/CancelOrderCommandHandler.cs:110-113` reads status inside `unitOfWork.ExecuteAsync` (`GetByIdAsync`), then branches.
  - `:118` `CreditApproved or Confirmed` → enqueues `credit.release` (`:179-189`);
  - `:122` `StockReserved` → enqueues `stock.release` (`:211-221`).
  - Nothing locks the order row against the saga's own transaction between the read and the enqueue.
- **The saga's forward progress and R25:** `src/Orders/Application/Sagas/SagaFactHandler.cs:74` `SagaStepTable.ForStatus(fact.EventType, order.Status)`. Its equality-only precondition ignores a fact whose status has moved, recording a `SagaIgnoredFactRecord` (`:86-97`). That ignore is the correct no-op bullet [2] protects, and the path that strands a released resource under the race.
- **The note lookup under the race (bullet [4]):** `SagaFactHandler.cs:201` calls `commandStore.FindOperatorCancelNoteAsync`, implemented at `EfCoreSagaCommandStore.cs:293`, port at `ISagaCommandStore.cs:178`.
- **The isolations the reproduction must remove:** `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs`
  - `:88-90` no `credit.hold` responder, pinning `stock_reserved`;
  - `:157-158` no `despatch.create` responder;
  - `:219` pins at `confirmed`.

  Add the omitted responder back and make the interleave **deterministic** (a controlled delay or barrier, never repetition).
- **Unchanged from the older prep:** #7's evidence is its Finding 1 (`order-to-cash-nestjs/progress/review_orders_catalog_and_cancel_responders.md:100`, reproduced live 2-for-2), with #7's named defence, a transactional status re-check or row lock before the compensating dispatch. #8's terminal-rejection classification is already in place (backlog id 42), so the stranded case parks terminally rather than retrying forever.

## Prep for backlog id 62 (operator cancel races saga forward progress) — read-only, done while Group N runs

**#7's evidence, read from its checkout:**
- **Finding 1**, `order-to-cash-nestjs/progress/review_orders_catalog_and_cancel_responders.md:100`. HIGH severity, non-blocking, *"live-reproduced with a 2-for-2"* on the `credit_approved`/`confirmed` branch.
- **Consequence (`:76`):** the race *"can strand the entire order at `despatched` with `cancellation_reason: NULL` plus a `saga_commands` row parked forever"*. The narrower `stock_reserved` version strands one Fulfillment reservation.
- **Fix #7 named but did not build:** *"a transactional status re-check before dispatching"*.
- **Routing:** #7 filed **no** backlog entry for the race. My search matched ids 17, 27, 28, 35 and 38, all false positives (`race` inside "t**race**" and similar). #7 shipped only the narrow adapter fix, its feature 42. The race itself lived on only in review prose (`history.md:1087`), which is the routing failure `CLAUDE.md` names. #8's id 62 is the first artefact to carry it.

**#7's automated suite never reproduces the race; it isolates each branch from it.** `apps/orders/src/orders-cancel.integration.spec.ts`:
- `:70-82`: no `credit.hold` responder, pinning the order at `stock_reserved`;
- `:138-152`: no `despatch.create` responder, pinning it at `confirmed`;
- the comments at `:322` and `:363` say so.

**#8 copies the same isolation.** `tests/Orders.IntegrationTests/OrdersCancelAcceptanceTests.cs`:
- `:85-91`: no `credit.hold` responder;
- `:155-158`: no `despatch.create` responder.

Enumeration: 31 lines in `tests/Orders.IntegrationTests` mention a race. Only the four in this file (`:87`, `:119`, `:156`, `:202`) are this race. The others are allocator, relay concurrency, subscribe-side readiness, dead-letter park and idempotent-replay races. `OrdersCancelResponderReadinessRaceTests` is a responder-readiness race, despite its name.

**What changes the #8 consequence:** #8 already has #7's feature-42 fix (backlog id 42, done in phase 8). `src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs:132-139` maps `PRECONDITION_FAILED` and the rest of the terminal set (`:160-162`) to `SagaCommandBusinessRejectionError`, not to capped-backoff retries. So in #8, the failing reproduction id 62's bullet 0 requires should expect a **terminally parked** compensating command and a stranded order or resource, not an endless retry. The brief should say what exactly to assert.

**Brief shape for id 62 (after the feature-27 review):**
1. Start from `OrdersCancelAcceptanceTests`' two isolated branches, and *add* the omitted responder (`credit.hold` or `despatch.create`) so forward progress races the cancel. Make the race deterministic with a controlled delay, never by repetition, per `CLAUDE.md`'s change-of-kind rule. Show it FAILS on today's code.
2. The candidate defence is #7's own named one: a transactional status re-check, or row lock, before the compensating dispatch. State its throughput cost, as bullet 1 requires.
3. Check against R25: `SagaStepTable.ForStatus`'s equality-only precondition check must keep ignoring genuinely stale facts (bullet 2).

## Group A4 — reported complete, not closed: timed-out polls are swallowed

**Checked and sound:** all 7 boxes ticked, `init.sh` exit 0, no stray process, **no new `PackageVersion`** (only a `FrameworkReference`), `specs/shared/` diff limited to `test-matrix.md`, id 27 still `in_progress`, A4d matches the repository's own port **by namespace** (`.Application.Ports.IHealthCheck`) against a literal set of 14, and the Gateway route sweep's literal anonymous set now includes both health routes.

**The defect.** After the first quality.sh run failed, every `HealthProbesTests.cs` got a `TryGetAsync` helper that returns `null` on its own 5 s token. Every caller treats `null` as *"not observed this poll"*. So:
- **`/health/live` hanging during the pause passes.** The in-loop check is `if (liveWhileDown is not null) { Assert.Equal(OK, …) }`, identical in all six files. *"Answers 200 throughout"* is claimed and never checked.
- **A readiness probe with a loose bound passes.** A poll during the pause that exceeds any bound is skipped, and the test fails only if no 503 arrives within 30 s. A probe bounded at 12 s instead of 2 s still yields one. Ledger L27's arm was recorded **before** the helper existed, so it has never been seen to fail against the current test.

**The flake diagnosis was never evidenced.** The record says *"Root cause: a defect in the TEST, not the production `MsSqlHealthCheck`"*, but it never identifies which call hung, and no artefact survived (`find` for `.trx`/logs: nothing). Its signature, `TaskCanceledException` at `HttpClient`'s 100 s timeout, is **exactly what A4c's own unbounded-probe arm produced**. Two production mechanisms are live and untested:
- `KafkaHealthCheck.CheckAsync` calls the synchronous `GetMetadata(2s)` on a request thread.
- `MsSqlHealthCheck` opens a **pooled** `SqlConnection`, whose cancelled command against a paused server can outlast `CancelAfter`.

`HealthCheckAggregator.ReadyAsync` runs checks **sequentially**, so a legitimate worst case is checks × 2 s. A 5 s per-call cap is below Orders' 6 s.

**What the fix round does:**
1. A timed-out liveness poll, or a ready poll exceeding a stated per-service bound, fails during the pause.
2. The fix is armed by changes of kind: a 10 s delay on `/health/live`, a 12 s MS-SQL bound, and the L27 arm re-run.
3. The flake is **measured**: timed consecutive probe calls against paused containers, plus liveness latency under a deliberately capped thread pool.
4. If a production bound is exceeded, the fix lands at the class across every copy. If not, the record's "test defect" sentence becomes *"not reproduced; mechanism unknown."*
5. Reconcile against 1750.

**How it was found:** the tests were read, not trusted. The same shape as the rest of this phase: a test that swallows the failure it is named for cannot fail, so it proves nothing, however green it is.

## A4 fix round 1 — verified; two production defects found; round 2 needed to guard them

**Verified by the leader, not taken from the report:**
- No build or test process alive.
- `Pooling = false` in all four `MsSqlHealthCheck` copies, byte-identical modulo namespace.
- `TaskCreationOptions.LongRunning` in all three `KafkaHealthCheck` copies. They differ only in comments: Orders carries the long summary and one extra inline comment.
- Per-service `_pausedReadinessBound`: 8 s for Orders and Projector (3 checks), 6 s for the rest (2 checks). Per-call token is bound + 2 s.
- The retired phrase *"not observed this poll"* survives only as *"HERE ONLY"* on the pre-pause and recovery helper, which is accurate.
- Record addendum at `impl_observability_reliability.md:1976`, with an explicit correction at `:2032`.
- quality.sh reconciles at 1750 = 1750 + 0 (no test added).

**Why the quality.sh flake happened.** Two real production defects, each measured before and after the fix:
1. **`MsSqlHealthCheck`'s "fresh" `SqlConnection` was pooled** (ADO.NET's default). A physical connection warmed while healthy, then reused against a paused server, **hung for more than 500 s** with `CancelAfter(2s)` never taking effect. With `Pooling = false`, 20 calls took 2000–2001 ms.
2. **`KafkaHealthCheck` called the synchronous `GetMetadata` on a request thread.** Under `ThreadPool.SetMaxThreads(6,6)` with 20 concurrent ready polls, `/health/live` stalled up to **4005 ms**. With `LongRunning`, the maximum was 887/156/147 ms across three runs.

**Not closed, for four reasons:**
1. **Both production fixes are unguarded in the committed suite.** Their arms were scratch measurements, since deleted. Reverting either fix has never been shown to fail a committed test. The Orders, Billing and Notifications MS-SQL copies, and all three Kafka copies, have no behavioural test that could fail.
2. **"Liveness 200 throughout" is still sampled at points.** With 2 s probes, the first pause-window poll returns the 503 and the loop breaks, so the in-loop liveness branch is never reached; the record admits this for Fulfillment (variant 2). Liveness is never sampled **while a ready request is in flight**, the exact condition under which defect 2 starved it.
3. **Two record errors:**
   - The measurement table says Kafka uses *"a dedicated per-call admin client"*; the source says one per **process**.
   - The flake correction invokes a *"non-paused-but-contended container"*, when Fulfillment's test **pauses** MS-SQL and the measured pooled hang against a paused server is the observed mechanism.
4. **The test count moves** once guards are added, and must reconcile.

## Group N pre-checks — done while the A4 fix round runs

**N4 (absence): clean.** `git status --porcelain --untracked-files=all -- specs/shared infra n8n src/SharedKernel src/Cqrs src/Seed` prints one line, ` M specs/shared/test-matrix.md`.

**N5: present.** B1's `filter: "[request_id] IS NOT NULL"` is at record line 186. B4's real `SqlException` text is at 202, 356 and 361. The OTel version is 1.18.0, at 1398–1403.

**N3 (`.env.example`): three variables missing.** `FACT_RETRY_MAX_ATTEMPTS`, `FACT_RETRY_BACKOFF_MS` and `OTEL_EXPORTER_OTLP_ENDPOINT` are absent, while every relevant `*ProgramConfiguration.cs` reads them:
- the retry pair is read in Orders, Notifications and Projector;
- the endpoint is read in all six services.

The five `*_HEALTH_PORT` variables are present. `README.md` has no environment table, so N3's README half does not apply.

**N1 (ledger walk): every one of the 27 guarded rows has a recorded arm.** My first enumeration matched `\bL<n>\b` beside an arm word on the same line, and reported L11, L13, L14 and L25 as unarmed. That was the wrong unit: those arms are recorded under the **test names**, at record lines 881, 844, 892/902 and 1519–1535. L27's arm predates `TryGetAsync`, and the fix round re-runs it.

**`test-matrix.md`'s summary table is stale, and nothing can see it.** The legend says the counts are *"counted from the Status column as it actually stands, one row at a time"*. I classified all 63 rows from their Status cells:
- §1, §4, §7, §8 and 8.1 reconcile, which validates the classification.
- §2 and §3 do not. R16 went TODO → DONE and R29's dead-letter leg closed, and neither summary row moved. Both full cells carry no shortfall word (`outstanding|unproven|deferred|scoped|todo|not yet|half`: zero hits).

| Row | Table says | Rows say |
|---|---|---|
| 2. `outbox_and_idempotency` | 8 \| 7 \| 0 \| 1 | 8 \| **8** \| 0 \| **0** |
| 3. `order_saga_orchestrator` | 11 \| 9 \| 2 \| 0 | 11 \| **10** \| **1** \| 0 |
| **Total** | 63 \| 56 \| 5 \| 2 | 63 \| **58** \| **4** \| **1** |

**This is #7's own recorded defect, repeated.** #7's matrix carries a *"Why the numbers moved"* paragraph: its table once claimed 47 green because *"it had simply never been recomputed as rows were filled."* #7 also has a paragraph naming each scoped row and its standing, and #8's legend points to *"the paragraph under the table"* — which in #8 does not exist (line 82 is blank, 83 is `---`). Neither `init.sh` nor any test reads the counts: §5d exempts `test-matrix.md` by design, and the six readers found by `grep -l test-matrix` cite rows, not totals. **Routing:** Group N recounts §2, §3 and Total, because feature 27's own flips moved them.

**Three scoped rows outlived their named closer.** R1, R24 and R61 each say their API half waits for *"the gateway feature"* because *"no Gateway/API surface exists yet"*. The Gateway shipped in phase 13 (id 25) and closed none of them. This is `CLAUDE.md`'s *"the next feature that touches X"* failure in its own words. Resolved against #7 before routing:

- **R61: stale, but unarmed.** `tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs` › `ReplenishStock_ThroughTheRealGatewayPipeline_ActuallyAddsUnitsInFulfillmentsRealDatabase` drives `POST /stock/replenish` through real Kestrel into Fulfillment's real database, read back through an independent `DbContext` (`review_gateway_rest_auth.md:181`). That is how #7's gateway feature closed R61. No arm of its replenish assertion is recorded; only its pacing is unarmed, and id 69 carries that.
- **R24: owner misnamed.** #7 closed it in `api_tests` (#7's feature 31, `d19342c`), tightened by amendment A1 to a structural causal-order assertion. #8's id 31 `api_tests` is pending in phase 18, but its acceptance said only *"full happy path"*. **Fixed:** id 31 now carries an R24 bullet naming A1's structural assertion.
- **R1: no owner in #8.** #7 closed its API half only in the Phase 25 traceability closure (`8a3a3d3`), with `apps/gateway/src/money-representation.integration.spec.ts`: a 215-line shape-discovering sweep of every money-bearing Gateway response, deliberately not a per-field list. #8's backlog has no traceability-closure feature.
- **Routed to a new phase-14 entry, id 72.** It adds the standing paragraph the legend points to, arms and flips R61, builds R1's API half by porting #7's sweep, and corrects the R24 cell to name id 31.

## Group A3 — CLOSED

**Confirmed by a full run, not inferred:** `Orders.IntegrationTests` **117/117**, exit 0, 8 m 56 s, with no build or test process alive after it (16:22:05). That run also settles the rebuild question it started with: it began `--no-build` fourteen seconds after the last restore, and had it executed the stale "called twice" binary, the new `OtcDlqDepth_OneRunOnceAsyncCycle_CallsTheGaugeExactlyOnce` would have failed with *Actual 2*. It passed. **Solution total 1707** = 1706 + the one new test.

## Group A3 — round 3: the surviving guard closed, and why the agent kept stalling

**The orphaned `quality.sh` run passed and reconciles.** The agent read it from its redirected log: format, build and test all `[OK]`, and the eighteen project counts sum to **1706** (re-added by the leader: 50+23+24+207+232+124+423+70+44+107+16+6+13+59+86+50+116+56).

**The surviving `otc_dlq_depth` guard is closed, in both directions.** New test `OutboxRelayTests › OtcDlqDepth_OneRunOnceAsyncCycle_CallsTheGaugeExactlyOnce` drives a real `OutboxRelay.RunOnceAsync` and asserts `FakeDlqDepthGauge.CallCount == 1` — the assertion that fake was built for and never had. Deleting the relay's `RecordAsync` call gave *Expected 1 / Actual 0*; calling it twice in one cycle gave *Expected 1 / Actual 2*. Both restored `cmp`-identical; the call is present once in all three `OutboxRelay` copies. Solution total becomes **1707**.

**A confirming full `Orders.IntegrationTests` run is in flight** (`--no-build`, started 14 s after the last restore). It checks itself on the rebuild question: had it run the stale "called twice" binary, the new test would fail with *Actual 2*. Its output is readable at the session task directory as `bg1gpcinj.output`.

**Why the agent kept ending its turn "waiting":** its wait loop was `until ! pgrep -f "quality.sh"; do sleep 20; done`, and `pgrep -f` matches full command lines — the waiting shell's own command line contains `quality.sh`, so the loop matched itself and never exited. Recorded in `CLAUDE.md`: wait on a PID with `kill -0`, never on `pgrep -f <pattern>`.

## A4 pre-brief checks — done while A3 closes, all resolved without a gate item

**1. Design §8.2's readiness-check table matches #7, with one deliberate addition.** Enumerated by #7's check *implementation* files (`apps/*/src/infrastructure/health/*-health-check.ts`) — the first attempt searched #7's health *controllers* for dependency names, found nothing in all six, and was the wrong predicate, because the controllers delegate to injected check classes.

| Service | #7's checks | #8's design |
|---|---|---|
| Orders | MySQL, Kafka, NATS | same |
| Fulfillment | MySQL, NATS | same — no Kafka, and #7's own `health-checks.spec.ts` says so |
| Billing | MySQL, NATS | same |
| Notifications | MySQL, Kafka | same |
| Gateway | Mongo, NATS | same |
| Projector | Mongo, Kafka | **adds NATS** |

Fulfillment and Billing publish facts only through their outbox, which is exactly why neither checks Kafka: the outbox decouples them from the broker at request time. The Projector's extra NATS check is stricter than #7, justified by `projector_read_model`'s `PR45` (the NATS connection is required at boot) — an addition, not a dropped guard.

**2. The health ports: numbers match #7, names do not.** #7 used `ORDERS_PORT=3002` … `PROJECTOR_PORT=3006` (read in each `apps/*/src/main.ts`); #8's design names them `ORDERS_HEALTH_PORT` etc. #8's `.env.example` defines only `GATEWAY_PORT` and no compose file names a per-service port, so this creates **no duplicate name within #8**. Environment variable names are not a parity contract here — the n8n workflows and the API tests reach only the Gateway. Design §8.1's *"the same allocation #7 used"* is precise about the **numbers** only. Recorded; nothing to change.

**3. `IHealthCheck` shadows a framework type of the same name.** The ASP.NET Core shared framework (`Microsoft.AspNetCore.App/10.0.11`) ships `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions.dll`, which defines `IHealthCheck`, and A4a adds a `FrameworkReference` to that framework in five services. Nothing in `src/` uses the framework type today, so there is no clash yet — **but A4d's enumeration of "every `IHealthCheck` implementation" must match the repository's own port type by namespace, not the bare name**, or it is a sweep whose predicate two unrelated interfaces satisfy. Separately, design §9.1 justifies hand-rolling by analogy with #7 refusing `@nestjs/terminus`, a package; .NET's built-in health checks need no package, so that reason does not transfer. The hand-rolled port is still kept, because it produces `openapi.yaml`'s exact `HealthResponse` shape — the flawed rationale is recorded, and the gate is not reopened.

## Group A3 — arming round result

**Eight of the nine disclosed gaps are closed**, each mutated, seen to fail with a verbatim message, restored `cmp`-identical: A3a's package-version substitution; A3c/L21's shared `NatsHeaders` (failed 5/5); A3d/L22 (the `.dlq` message lost its `traceparent`); A3d/L23 (no `outbox.publish` span produced); A3f's `AddJsonConsole` deletion and `ActivityTrackingOptions` removal, as separate arms; and four of A3h's five metric instruments, including `otc_fact_processing_latency_ms` on both the success and the exhausted-retry path.

**One guard survived, and it has to be closed before feature 27 is:** deleting `OutboxRelay`'s `dlqDepthGauge.RecordAsync(...)` left both `otc_dlq_depth` integration cases **green**, because neither drives `OutboxRelay.RunOnceAsync` — both construct `KafkaDlqDepthGauge` and call `RecordAsync` on it directly. The test proves the gauge works, not that the relay ever calls it. **`FakeDlqDepthGauge.CallCount` already exists and is asserted nowhere**, so this check was clearly intended and never written. The agent rightly added no test in a round told to add none; it is carried here so it cannot be dropped.

**Also resolved:**
- The `FactRetryDispatcher` copies' header comments are synced: both copies now differ from the canonical in **0** non-identity lines, and the parity test is green.
- **The banner exemption was checked against the spec and deliberately left wide.** Design §3.2 cross-references `IdempotentConsumer.cs`, whose own header defines the banner as *every contiguous leading `//` line*, and `IdempotentConsumerParityTests` uses the same rule. **For the final reviewer:** comment drift inside those banners is therefore unguarded by design, in two parity families — and it has already happened once, which is how it was found.
- The "Armed (thread safety)" heading is corrected in the record, and all 38 new tests are single-case `[Fact]`s, so the attribute count and the case count agree.

**The stall happened again despite an explicit instruction.** The A3 agent ended its turn "waiting for the quality.sh completion notification", leaving that run orphaned. The leader is waiting on the process ID directly.

## Group A3 — implemented, not yet closed

**Implemented and verified mechanically:** all 10 tasks ticked, `init.sh` exit 0, no stray process, **exactly three packages** added (`OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore`, all **1.18.0** with `Extensions.Hosting`), `OutboxWriter`/`OutboxRelay` copies differing only in banner and `WriteModelDbContext` alias, Notifications' `EfCoreUnitOfWork` untouched, and the §5.2 correction implemented (`NatsStockAvailabilityChecker` now builds a fresh `NatsHeaders`). Solution **1706** (1668 + 38).

**Not closed, because its own honest "What remains" section lists roughly twelve guards never seen to fail**: A3a's package-version substitution; A3c's thread-safety mutation; ledger L21, L22, L23 (passed, never mutated); A3f's `AddJsonConsole` and `ActivityTrackingOptions` (only `IncludeScopes` armed); and **none of A3h's five metric instruments**. Same standard as every group: Group B's two gaps were closed before B counted as done. An arming round is running on the same agent.

**Found by the leader while verifying, sent into that round:**
- **Comment drift the parity test cannot see.** A3 updated the canonical `FactRetryDispatcher.cs` header to allow `System.Diagnostics`, `.Infrastructure.Observability` and `OtcMetrics`; the Projector and Notifications copies still carry the old text while their identical code uses all three — so two files now describe themselves wrongly. `FactRetryDispatcherParityTests.IsBannerLine` treats *every* leading `//` line as banner and skips the whole block, so drift in the file's own reference contract is unguarded. The round is to sync the copies and to make the exemption match design §3.2's definition of "banner" — or, if §3.2 really means the whole block, record the gap rather than contradict the spec.
- **A mislabelled heading**: "Armed (thread safety)" over a paragraph that says it was not armed.
- **An unchecked reconciliation method**: 1706 = 1668 + 38 was counted against `[Fact]`/`[Theory]` **attributes**, which equals the test count only if no new theory has more than one data row.

## For the final reviewer — a factual error in the gate-approved design, found before A3 was dispatched

**Design §5.2 says all three outbound NATS request sites *"each already build a fresh `NatsHeaders` per call."* That is false for one of them.** `Gateway/.../NatsRpcClient.cs:30` and `Orders/.../NatsSagaCommandsAdapter.cs:94` do; **`Orders/.../NatsStockAvailabilityChecker.cs:30-34` calls `RequestAsync` with no `headers:` argument at all.** It works because `StockRpcResponder` exempts `stock.check` from metadata — `RequireMeta` is called only on `stock.reserve`, `stock.release` and `despatch.create` (lines 229, 242, 281), a ratified exemption (`FS3`). So `A3c` must *construct* headers at that site rather than add a trace header beside existing ones. The design was not edited mid-implementation, because no decision changes: still three sites, still fresh-per-call, still fifteen subjects. The A3 brief carries the correction and asks for it to be recorded.

**How it was found is the part worth keeping.** The leader's first enumeration searched for `new NatsHeaders` and found two sites — because a site that sends **no** headers is exactly the one a header search cannot see. Re-enumerating by the request call (`RequestAsync`) found all three. The same pattern `CLAUDE.md` names: a sweep must not filter by the property it is testing.

**Also confirmed before dispatch:** design §9.1 explicitly rejects `OpenTelemetry.Instrumentation.EntityFrameworkCore` (a beta) and `OpenTelemetry.Exporter.Prometheus.AspNetCore` (`OR5` forbids a scrape endpoint), so the phase's package list is exactly `OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` and `OpenTelemetry.Instrumentation.AspNetCore`. `OutboxWriter.cs`/`OutboxRelay.cs` belong to `OutboxRelayParityTests`' seven-file family, so A3d changes all three copies identically; `EfCoreUnitOfWork` is in no parity family and its four copies already differ, so leaving Notifications' copy alone breaks nothing.

## Group A2 — closed after the restart, with one limit for the final reviewer

**Resumed and finished.** All 8 tasks armed. Two regressions surfaced only when the whole `Orders.IntegrationTests` project ran, and were fixed: the schema guard's literal column list lacked the three new `saga_commands` columns, and a dead-letter read raced a sibling test on the same topic until it filtered by `correlationId`. `Orders.IntegrationTests` **106/106**, run in the foreground. Solution **1668** reconciled as 1654 + 14.

**The entry-71 question is answered in code:** `R29` needed the triggering fact's full bytes, and `triggering_event_envelope` (migration `20260910091952_AddSagaCommandsDeadLetterColumns`) is the column entry 71 will reuse — the same column name #7 uses.

**A flaky test was fixed rather than accepted.** `SagaCommandDeadLetterTests` failed 2 runs in 5 on `cmp`-identical source. The first report called it an environmental flake in a pre-existing file; both labels were wrong. It was a race in the test itself (a poll on `Status == "parked"` read `DeadLetteredAt` before the separate claim `UPDATE` committed), in a file this feature wrote. Proven fixed with a controlled 1 s delay injected between the two updates: old poll failed 2/2, new poll passed 2/2, and the second-park guard **still failed 2/2 on broken code** — so that guard is real, not timing-dependent.

**Limit to hand the final reviewer:** the second-park check waits for two reads **3 s apart** to agree. A wrongful claim committing more than 3 s after the park would evade it. In the real code the gap is sub-second (same call stack), so it is a stated bound rather than a defect — but it should be named in the review rather than discovered.

**Concurrent-build incidents: three in this feature.** The second and third were not two agents building at once but one agent backgrounding a run and continuing to work — and the leader's own "don't end your turn waiting, keep going" wording contributed to the third. `CLAUDE.md` now says: while your own build or test process is alive, only read-only work; prefer long suites in the foreground.

## RESUME HERE — state at the interruption, verified by the leader before stopping (historical — superseded by the A2 block above)

**Why it stopped:** the human needed to restart VS Code. The running Group A2 agent was stopped **deliberately** first, so its stop was controlled and the tree could be inspected, rather than being killed uncontrollably by the restart.

**What it was doing when stopped:** its last message was *"The full end-to-end test passes on the first try. Now let's run the other saga integration tests that I modified"* — i.e. **verification, not an arming cycle**. No `*.bak`/`*.orig` arming backups were found anywhere in the tree.

**The tree at the stop, measured, not assumed:**
- `dotnet build OrderToCash.sln` — **0 warnings, 0 errors**
- `Orders.UnitTests` **398**, `Projector.UnitTests` **107**, `Notifications.UnitTests` **70**, `Architecture.Tests` **16** — all green. A left-armed mutation would have shown as a failure here; none did.
- `./init.sh` — exit 0
- **Container-backed suites were NOT run at the stop** (they take ~25 min). Their last known-good figure is **1654**, after Group A1.

**Group progress:**
| Group | State | Last reconciled figure |
|---|---|---|
| B — `requestId` replay | **complete**, 8/8 guards armed with verbatim failures | 1635 = 1623 + 12 |
| A1 — retry-then-dead-letter | **complete**, 10/10 tasks, parity of the three dispatcher copies verified by diff | 1654 = 1635 + 19 |
| A2 — first-park hook, `R29` | **stopped mid-verification**, code substantially written, **0 of 8 boxes ticked** | not yet reconciled |
| A3 — telemetry | not started | — |
| A4 — health | not started | — |

**To resume, in this order:**
1. `./init.sh` must exit 0.
2. **Re-dispatch Group A2 telling it to INSPECT the A2 work already on disk and finish it, not rebuild it from scratch.** Its partial edits are in `src/Orders/` (saga command store, `SagaCommand` entity and configuration, `SagaFactHandler`, `SagaCommandDispatcher`), `src/Notifications/` and `src/Projector/`. It had reached a passing end-to-end test.
3. **A2's brief still owes one explicit answer**: whether `R29` needed the triggering fact's **bytes** on `saga_commands` — the same column backlog entry **71**'s bullet 5 needs. `SagaCommand.cs` and `SagaCommandConfiguration.cs` are both modified, so inspect what was added.
4. Run the full suite **only when nothing else is building**, and reconcile against **1654**.

**One rule to carry, because it cost real time this session:** never run two builds against the same projects at once — a concurrent build corrupted an assembly badly enough to crash a test host with `BadImageFormatException`, blaming an innocent auto-property. If a failure names a line the source cannot explain, clear `bin/` and `obj/` before believing it.

## The gate ruling — approved 2026-09-10

**G2 approved: widen `FactPublisherConfinementTests` to `\.Infrastructure\.(Outbox|Messaging\.DeadLetter)(\.|$)`.** That guard was itself ratified at the `order_saga_orchestrator` gate on 2026-09-04, so widening it needed a gate of its own. Projector and Notifications need a dead-letter **producer** and own no outbox; the alternative was giving them an `Infrastructure/Outbox/` folder containing no outbox, which makes the *name* lie to keep the *pattern* true — the failure this repository has paid for repeatedly.

**G1, G3, G4, G5 were decided in the spec with cited reasons and approved for awareness**: five services gain an HTTP listener for health as an `IHostedService` hosting a minimal `WebApplication` (not by converting the generic hosts); sequencing B → A1 → A2 → A3 → A4; `causationId` seeded from `requestId` when supplied, matching #7 and `asyncapi.yaml`'s own definition; JSON console logging in every host.

**Six of #7's seven open points were verified as already inherited**, each cited to the #8 artefact that carries it — including the largest, the 14th fact `order.saga_failed.v1`, already registered in `asyncapi.yaml`. **No `SA-n` was proposed**, and the spec author checked whether `specs/shared/` actually prescribes what #7's promotion candidate would have amended. It does not.

## Goal## Goal

**Phase 14 — cross-cutting reliability and observability.** One `sdd: true` feature and six backlog entries, which is the most lopsided phase so far.

**Id 27 `observability_reliability` is `sdd: true`** — the first spec-driven feature since the projector, so it takes the full loop: `spec_author` writes the triple-doc, **the human gate runs between `spec_ready` and `in_progress`**, and only then an implementer. It carries health checks, OTel propagation through NATS *and* Kafka, structured logging, retry + DLQ, and `orders.create`'s `requestId` idempotent replay — the last of which has been openly deferred since Phase 8 and is documented as such in `README.md` and in a comment in `PlaceOrderCommand.cs`.

## Backlog attachment map — written BEFORE the first dispatch

Six entries are filed against this phase, **all six found by this run's own reviews**. Two clusters and two standalones:

| Entry | Rides | Why |
|---|---|---|
| **62** operator cancel races saga forward progress | **id 27** | Its own notes say a fix without R29/DLQ context would be guessing, and R29's dead-letter clause is id 27's. The race is a reliability question and this is the reliability phase |
| **71** operator note across the compensation branches | **id 27**, or standalone right after | Needs the `saga_commands` row extended — and **entry 71's bullet 5 folds in the finding that the same row must store the triggering fact's BYTES, not just its id**, which R29's redrive clause needs. One column serves both, so opening that table twice would be waste |
| **67** design-time factory env reads · **68** delegation and wiring · **69** Gateway readiness pacing · **70** retyped key lists | **one guard-hardening loop, after id 27** | Same shared cause as the phase-13 loop — *a guard whose assertion cannot detect the defect it names* — and three of the four are residues that loop itself routed rather than closed |

**And the condition that loop must honour, learned by paying for it:** its **first task is one repository-wide enumeration of the class**, as a search result, before any fix. The phase-13 loop fixed each entry at the sites its entry named and the retired shape survived in three more files — which is exactly what entry **70** now exists to clean up.

## What phase 13 leaves for phase 14 to do differently

- **Five of six phase-13 rejections were one class, and every one was found by a reviewer's mutation, not by reading.** Budget review time accordingly; it is not overhead here, it is the detector.
- **Three mutation families now, not two** — deletion, corruption, **substitution**. Substitution applies where a literal names a member of a set whose siblings exist in this repository, and a swap that fails is not evidence until the message names what you meant to break.
- **Id 27 is `sdd: true`, so the ledger goes back into `design.md`** with its guards named in `tasks.md` — the `progress/impl_*.md` location is the `sdd: false` rule.
- **Ask what `specs/shared/` actually prescribes before proposing an amendment.** The last time this was skipped, an `SA-3` was proposed for a table the shared spec never describes, which #7 had already solved with no wire change at all.

## Blockers

None. Nothing is uncommitted.

## Notes

**This file is the leader's and no subagent may write to it.** Rewrite the whole body at every transition and treat a reviewer's approval as the trigger — `init.sh` §4 reads the `**Feature:**` line only and cannot see a stale body.

---

## Template (reset to this on session close)

```markdown
# Current session

**Feature:** <name or "none active">
**Status:** <status>
**Session started:** <date>

## Goal

## Decisions taken this session

## Blockers

## Notes
```
