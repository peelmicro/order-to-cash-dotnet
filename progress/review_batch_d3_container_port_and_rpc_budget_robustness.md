# Review — batch D3, backlog ids 81 and 85

**Verdict: APPROVED (id 81) — APPROVED (id 85).** Both acceptance arrays are met, the fixes are correct, and every guard I attacked failed the way it claims to. **Two defects found, both in the RECORD rather than in the code, both routed with their full replacement text supplied below, and both to be applied before phase 14 is committed:** an arithmetic miscount in id 81's evidence prose (conservative in direction), and two missing ported-idiom ledger rows. Neither falsifies an acceptance bullet; neither is worth a review round to move text this review has already written; **neither may be dropped**, and the ledger rows least of all — that class has a 0-for-3 detection record in this build and this is the check that was supposed to catch it.

The leader dispatches a `test_maintainer`-class text pass over `progress/impl_batch_d3_container_port_and_rpc_budget_robustness.md` and two strings in `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs`. No code, no test logic, no re-open.

Scoped review, per the brief. I did **not** re-run `./quality.sh`. What I ran instead is listed under each item.

## 1. Id 81's diagnosis — does the evidence support the conclusion? **Yes.**

I read the raw diagnostic logs (`…/scratchpad/id81_diag.txt`, `id81_d4…d8_idle.txt`) rather than the record's summary of them, and the four legs of the argument each hold:

- **D1** — `elapsed=4027ms` under a 2 000 ms budget with `BOTH SUCCEEDED`: the barrier wait is provably outside the charged window, so the barrier is ruled out by measurement.
- **`responderObserved=2/2`, `b=ok(3–8ms)`** in every one of the 8 failures: the request reached the responder and the losing call's simultaneous twin, released by the same barrier onto the same connection, completed in single-digit milliseconds. No amount of machine load produces that asymmetry. Contention is ruled out by the data, not by argument.
- **D3 under a 120 000 ms budget losing a reply on an idle machine** kills the "budget too tight" premise outright. A 2-minute stall is not a slow round trip.
- **The controlled comparison**: cold vs. established, one variable, 8 failures vs. 0. `D7` is the decisive row because it isolates *exactly the fix* — `ConnectAsync()` only, no prior request — at 0/200.

The fix follows from the cause: the pair is no longer the connection's first traffic, and the precondition assertion makes the property the test enforces rather than assumes. **Bullet 2 is met** — the cause was identified before the change, with evidence, and each of the entry's four named candidates is ruled out by a measurement.

One nuance worth keeping for #9, not a defect: `responderObserved` proves the responder *received* both requests; it does not prove it successfully *published* both replies. The loss is therefore located at "connect-time, on the reply path" with the exact side unproven. Nothing in the fix depends on which side.

## 2. The disclosed discrepancy — bullet 3's literal recipe. **Legitimate substitution.**

Bullet 3 reads "delay the responder … and show the current budget loses every time and the chosen fix wins every time". That recipe presupposes the fix is a budget change. Given the measured cause it is **unsatisfiable as written**: a delay longer than 2 000 ms makes the current budget lose every time (a tautology about arithmetic, not about the defect), and the chosen fix — `ConnectAsync()` — would *also* lose every time, because it does not touch the budget. Following the recipe literally could only have been made to "pass" by raising the budget, which bullet 5 forbids and which D3 shows would not have fixed anything.

The substitution made instead is on the property the cause is about: cold → `ConnectionState == Closed`, 100 %, by construction; fixed → `Open`, 100 %. The determinism lives in the test. Bullet 3's **spirit** — change of kind, not of probability; determinism in the test — is met, and the causal link between that property and the lost reply rests on a controlled 1 348-round comparison with a single variable, which is *not* "the flakes stopped": it is an experiment with a control arm. The implementer disclosed the divergence explicitly, at the top of its record, with its reasoning. That is the disclosure this harness asks for. **Bullet 3 accepted as met by substitution.**

## 3. Bullet 5's margin — **measured, and the arithmetic checks.**

`D8a[loaded32] p99=5.3ms` and `D8b[loaded32] worst SUCCESSFUL charged call 127ms` are both read off logs on disk. 2000/127 = 15.7 (~15×) and 2000/5.3 = 377 (~380×). The budget is unchanged at 2 000 ms; bullet 5's escape hatch was not used and did not need to be.

## 4. Id 85's change of KIND — **determinism is in the TEST, confirmed by re-arming.**

`ContainerHostPortAssignmentRaceTests.cs:65` holds the squatting listener open **across the whole container start** (`using var squatter = …`), so the retired arm cannot win by being fast — the determinism is a property of the test, not of the mutation. I verified the orientation both ways against a real Docker daemon (29.8.0):

- Unmutated: both cases **pass** (2/2, 6 s).
- **Arm 3 re-armed by me** — squatter released one line before the start, nothing else changed: `repetition 1: host port 42381 — STARTED, no exception`, the case fails naming the port.

Port held → loses; port free → wins. One variable, and the variable is in the test.

## 5. Bullet 3's Kafka caveat — **the broker still advertises a reachable host port.**

All six converted fixtures build `EXTERNAL://localhost:{GetMappedPublicPort(9092)}` inside `WithStartupCallback` and assign `BootstrapServers` from the same mapped port (verified by one `grep` across all six — identical in every one). Empirically: I ran `ProjectorDeadLetterTests` (a **host-side** `ProducerBuilder` producing into the converted fixture's broker, then consumed) together with the race tests — **4 passed, 25 s**. A broker advertising an unreachable port cannot pass that: the host client bootstraps, receives the advertised listener in metadata and reconnects to it. The race was not removed by breaking the advertisement.

## 6. Re-arming — three arms re-run from scratch by me, all naming specifics (id 82's rule)

| Arm | Mutation I made | Named test that failed | Message named |
|---|---|---|---|
| **5** (id 85) | dropped the optional `true` in `tests/Billing.IntegrationTests/KafkaContainerFixture.cs:63` | `EveryWithPortBindingLetsDockerAssignTheHostPort` | `tests/Billing.IntegrationTests/KafkaContainerFixture.cs:63 — WithPortBinding(ExternalContainerPort) binds host port 'ExternalContainerPort', which this fixture chose for itself` — fixture **and** port |
| **3** (id 85) | released the squatter before the container start | `Id85_TheRetiredSelfAssignedShape_FailsEveryTime_…` | `Host port 42381 … Docker started the container anyway` — the port and the outcome |
| **1** (id 81) | deleted `await realConnection.ConnectAsync();` | `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` | `…waits out the whole 2000 ms budget … state … was 'Closed', not 'Open'` — the **budget** and the **state** |

Plus one probe of my own the record did not run — **defeat-list attack 3 against the listener allow-list's own claim**: I reintroduced a `GetFreeTcpPort`-shaped method under an innocent name (`PickAHostPortWithAnInnocentName`) in `tests/Orders.IntegrationTests/KafkaContainerFixture.cs`. `ExactlyOneMethodInTheRepositoryStillConstructsATcpListener` failed naming `…KafkaContainerFixture.cs::PickAHostPortWithAnInnocentName` and printing the three allowed sites. The guard really is keyed by shape, not by name, as its doc comment claims.

Every mutation was backed up with `cp`, restored from the backup, verified `cmp`-identical **and** by re-reading the changed line, `touch`ed and rebuilt `--no-incremental` before the confirming green run. No git command that writes the index or working tree was used. Confirming greens: Architecture guards 5/5, race tests 2/2, `NatsRpcClientIntegrationTests` 7/7.

## 7. Count reconciliation — **exact.**

I summed the 18 per-project figures out of the implementer's own `quality_final.log` myself: 1 508 unit + 509 integration = **2 017**, 0 failed, 0 skipped, 0 warnings. The **+7 attribution** is confirmed independently, not taken on trust:

- `dotnet test --list-tests` discovery: `Architecture.Tests` = **41** (36 + 5 new), `Projector.IntegrationTests` = **65** (63 + 2 new).
- `git status --porcelain -- tests/` shows exactly **9 modified + 2 new** files — the 8 id-85 conversions, `NatsRpcClientIntegrationTests.cs`, and the two new guard files. No test file was deleted, renamed or merged, so no test could have moved the other way.
- `NatsRpcClientIntegrationTests` still has its 7 cases (I ran the class: 7/7).

## DEFECT — id 81's round counts do not reconcile with its own diagnostic table

**Where:** `progress/impl_batch_d3_…md:97` and `:127`; `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs:34` (doc comment, "1 158 concurrent pairs") and `:253` (the assertion message, "8 losses in 608 cold rounds against 0 in 550 established ones").

**What:** summing the record's **own** table rows, and confirmed against the raw logs:

| | Record claims | Table/logs actually give | Missing row |
|---|---|---|---|
| cold rounds | 608 | **648** | `D5[idle][budget=20000]` — 40 rounds, 0 failures |
| established rounds | 550 | **700** | `D8b[loaded32][preconnected]` — 150 rounds, 0 failures |
| total pairs | 1 158 | **1 348** | both of the above |

The 8 failures and the 0 established failures are exact; only the denominators are wrong, and no stated rule excludes the two dropped rows (one is excluded from cold, the other from warm, on no consistent ground).

**Why it matters, and why it does not block:** `CLAUDE.md` is explicit that a number that will not reconcile is the work, not a caveat — and this one is embedded in a **test assertion message and a doc comment**, which is precisely the artefact #9 inherits without re-deriving. It is not approval-blocking because the error is **conservative in every direction that matters**: the true cold rate is 8/648 = 1.23 % against the claimed 1.32 % (both "~1 %"), and the established arm is *stronger* than claimed (0/700, not 0/550). No acceptance bullet turns on it; the cause, the fix and the margin are all unaffected. The correction is four numbers (608 → 648, 550 → 700, 1 158 → 1 348) and is mechanical.

**Routing, not narration:** this is a `test_maintainer`-class edit — an assertion-message and doc-comment correction with the correct values already computed above and the raw logs cited. It must be applied **before phase 14 is committed**; it does not warrant re-opening either entry. The leader owns the dispatch.

## DEFECT 2 — two ported-idiom ledger rows are owed and neither record carries one

`CLAUDE.md` binds the ledger to the **port**, and for an `sdd: false` feature it lives in `progress/impl_<feature>.md`. The record contains no ledger section and no "none owed" line. I checked #7's checkout rather than assuming, and **both entries turn out to sit squarely on a #7 mechanism** — my own first draft of the C6 box said "none owed" on an assumption, and both halves of that assumption were wrong.

### Row A (id 85) — the retired shape IS #7's idiom, hand-rolled and weakened

> **#7 relied on** testcontainers-node's `RandomPortGenerator().generatePort()` to pick the Kafka host port in advance, for exactly the reason #8 did — so `KAFKA_ADVERTISED_LISTENERS` can name it — in all six fixtures (`apps/projector/src/test-support/kafka-test-fixture.ts:35,38,46`, and the same lines in `apps/{orders,billing,fulfillment,notifications}` and `apps/gateway/src/test-support/kafka-test-fixture.ts:41,47`; its own comment at `:14-18` states the reasoning verbatim). That generator is `get-port` (`node_modules/…/testcontainers/build/utils/port-generator.js:4-9`), which does `net.createServer().listen(0)` → read → **`server.close()`** → resolve (`node_modules/…/get-port/index.js:23-32`) — **the identical time-of-check-to-time-of-use window**, plus one thing #8 never had: a process-local `lockedPorts` set that will not re-issue a port for 15 s (`index.js:10-18`).
>
> **In #8 that property was supplied by** eight independent hand-rolled `GetFreeTcpPort` copies with **no shared memory at all**, so two fixtures in one process — or two test assemblies in parallel — could be handed the same port, which #7's 15 s lock prevents. That is the most likely reason this bit #8 twice and is not on #7's record.
>
> **In #8 it is now supplied by** Docker itself: `WithPortBinding(containerPort, true)` holds the port from assignment to bind, with the advertised listener written in `WithStartupCallback` after `GetMappedPublicPort` is known. **This is a deliberate divergence from #7 and is strictly stronger than it** — there is no window left to lose, rather than a smaller one.
>
> **Guard:** `ContainerFixtureHostPortAssignmentTests` (5 cases) and `ContainerHostPortAssignmentRaceTests` (2 cases). Both armed; three arms re-run by this review.

Why the row matters more than usual: **#9 will hit this identically.** `testcontainers-python` offers the same two routes and the same Kafka advertised-listener problem, and without this row #9 has every reason to transliterate #7's `RandomPortGenerator` shape — the one #8 has just spent an entry retiring.

### Row B (id 81) — the cold shape is an artefact of the translation, and the record never says so

> **#7 relied on** `nats.connect()` from `nats.js`, which resolves **only after the connection is established** — its test fixture exposes exactly that (`apps/gateway/src/test-support/open-nats-test-fixture.ts:46`, `connect(): Promise<NatsConnection>`), and the JS client has no lazy-connect mode, so a concurrent pair can never be a connection's first traffic. #7 also has **no OR4 equivalent**: a content search for a concurrent traceparent spec across `apps/gateway/src` returns nothing, so this case is #8-native and its cold shape had no #7 counterpart to inherit.
>
> **In #8 that property is supplied by** nothing by default — `new NatsConnection(opts)` connects lazily on first use — and it is now supplied **explicitly** by `await realConnection.ConnectAsync()` plus the precondition assertion (`tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs:250-255`).
>
> **Guard:** the precondition assertion; armed by deleting `ConnectAsync()`, re-run by this review (fails in 293 ms naming the budget and `'Closed'`).

This row is not ceremony — it **reframes the finding**. As the record stands, the assertion message tells a future reader that "the cold shape loses one of the two replies in ~1 % of runs" as if it were a property of NATS. It is a property of a **lazily-connecting client issuing concurrent first traffic**, a shape #7's client cannot produce and #9's `nats-py` (`await nats.connect()`) cannot produce either. Without the row, #9 inherits a spooky general claim about a broker instead of a precise one about a client idiom.

## Other things named, not filed (phase 14 is frozen at 13 items)

1. **`tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs:111-113`** carries the identical cold-connection shape under a 5 000 ms default. The implementer correctly left it alone and asked for it to be numbered; the leader has already filed it as **id 95, phase 15**. No action here.
2. **`NoConditionalCompilationHidesAPortBindingOrAListener` is deliberately blunt** — it bans *every* preprocessor directive in any file whose raw text mentions `WithPortBinding` or `TcpListener`, including a file that only mentions them in a comment. That is the right trade today (it removes the parser/compiler disagreement structurally rather than by guessing symbols, and Arm 6 proves the disagreement is real), but it will false-red on a future legitimate `#if` in such a file. Documented in the guard's own summary; noted for #9.
3. **"Worst successful charged call = 127 ms across N pairs"** is measured only over the 600 `D8b` pairs — `D4`'s 120 rounds report failures but no per-round charged maximum, so they cannot contribute to a "worst successful" claim whatever N is written. The 127 ms itself is real and is the maximum of the four `D8b` runs.

## Checkpoints walked (C1–C7, applicable boxes only — both entries are `sdd: false`, test-only, no `src/` change)

- [x] **C1 spec/contract** — n/a (`sdd: false`); the contract is the two `acceptance` arrays, read verbatim and walked bullet by bullet above.
- [x] **C2 traceability** — no `R<n>` involved; each acceptance bullet mapped to a named test or a cited measurement (bullets 1–5 for id 81, bullets 1–6 for id 85).
- [x] **C3 tests are real and would fail on regression** — three recorded arms re-run by me from scratch, plus one novel attack; every failure message names the claim, not `Expected: False`.
- [x] **C4 conventions** — no `src/` change, no package added, no domain code touched; `quality.sh` format check clean, 0 build warnings.
- [x] **C5 architecture** — `Architecture.Tests` 41/41 green, including the five new guards; no cross-service or shared-code change.
- [ ] **C6 ported-idiom ledger** — **NOT met. Two rows are owed and neither record carries one, nor a "none owed" statement.** See defect 2. I wrote my own first draft of this box as a "none owed" from an assumption about #7 and then went and looked, which disproved it on both halves — the exact failure mode `CLAUDE.md` records for this box.
- [x] **C7 counts and effort** — reconciled exactly (item 7); effort record appended to `progress/history.md`.
- [ ] **`specs/shared/` root cause** — none in this batch; no `SA-n` owed.

## Status transitions

Ids **81** and **85** set to `"status": "done"` — a single status-line edit each. The `RE-OPENED 2026-09-13 by maintainer ruling` markers and the superseded `ACCEPTED, NOT FIXED` history beneath them are untouched: these two are now `done` because they were **implemented**, not dispositioned.
