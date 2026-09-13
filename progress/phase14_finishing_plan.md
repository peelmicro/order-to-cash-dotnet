# Phase 14 — the finishing plan

**Written 2026-09-13, after the maintainer rejected dispositioning and asked for the phase to be finished and correct.** The first version of this plan was premise-checked and came back **DO NOT ACT** with two false claims, one substantively wrong batch description and one under-scoped entry. This is the corrected version. Every factual claim below names the command that produced it.

## The frozen list — 13 items

Phase 14 is these and nothing else. Anything found from here goes to a phase-15 list and does not touch phase 14, regardless of how good the reason is.

```
python3 -c "import json;d=json.load(open('feature_list.json'));print(sorted(f['id'] for f in d['features'] if f.get('phase')==14 and (f['status']!='done' or 'ACCEPTED, NOT FIXED' in f.get('notes',''))))"
```

→ `[69, 70, 74, 78, 81, 82, 84, 85, 86, 88, 89, 90, 92]` — the 11 dispositioned entries plus the two still open.

Completion is checkable without trusting anyone's report:

```
python3 -c "import json;d=json.load(open('feature_list.json'));print([f['id'] for f in d['features'] if f.get('phase')==14 and f['status']!='done'])"
```

An empty list, with no entry carrying an `ACCEPTED, NOT FIXED` note, means the phase is finished.

## What the 11 dispositioned entries are

None changes shipped behaviour — each entry's own note carries the maintainer's ruling to that effect, and the categories below match each entry's own title and disposition reason.

| Kind | Ids | What it is |
|---|---|---|
| Test-harness quality | 74, 81, 85 | positional `.dlq` reads, a fixed 2 000 ms RPC budget, a TOCTOU port race |
| Guard or evidence quality | 69, 70, 82, 89 | the code is correct; the test meant to catch a regression cannot fail |
| Documentation | 78 | `<see cref>` targets nothing checks, because `GenerateDocumentationFile=false` |
| Maintenance duplication | 84 | a third copy of the RPC payload records |
| Acknowledged design residual | 88, 90 | id 80 bounds head-of-line blocking rather than eliminating it; a clamp unreachable until someone binds it to an env var |

## The batches

Phase 14 was expensive because each entry got its own dispatch and its own review round. These are small and homogeneous; they do not need that.

**Batch A — guard and evidence quality (69, 70, 82, 89).** **Dispatch to `implementer`, NOT to `test_maintainer`.** The first version of this plan called it "mechanical test work requiring no source changes, suitable for `test_maintainer`". Both halves were wrong, and the premise check caught the second:

- **All four entries require arming** — `id 69` bullet 4, `id 70` bullet 2, `id 82` bullets 1/3/4/5, `id 89` bullet 3. Arming means mutate, run the named test, record the verbatim failure, restore. **`test_maintainer` has tools `Read, Write, Edit, Glob, Grep` (`.claude/agents/test_maintainer.md:5`) — no `Bash`, so it cannot run a test at all**, and therefore cannot arm anything. Routing armed work to it would have produced an arming table nobody could have executed.
- **Id 70's arming mutates production source.** Its bullet 2 requires adding an undeclared property to `OrdersCancelReplyPayload` and to `DespatchCreateReplyPayload` — records under `src/Contracts/Rpc/` and `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs`. So "no `src/` path appears" was false, and the work is not test-only.
- **Id 70 overlaps id 84 in Batch C.** Id 70 bullet 2 names `OrdersCancelReplyPayload` and `CatalogReferenceListReplyPayload` as having **two definitions each** — the Gateway copy plus the owning service's own — which is exactly the duplication id 84 exists to remove. **Sequence 84 before 70, or brief them together**, or id 70's arming will target a definition id 84 then deletes.

- **Id 69 is wider than one line suggests, and the widening is the leader's own.** Bullets 1–4 concern the two Gateway readiness loops (`tests/Gateway.IntegrationTests/StandInResponder.cs:157`, `FulfillmentStockEndToEndTests.cs:89`), which are already paced and correct — only the guard is missing. **Bullet 5, added at the SA-3 commit, widens it to four committed-offset read helpers that catch every `KafkaException`** (e.g. `tests/Orders.IntegrationTests/SagaIntegrationTestSupport.cs:425-436`). Brief it as five bullets, not two loops.
- Id 70: three payload test files hand-type the expected key set (`OrdersCancelPayloadTests.cs:64-65`, `CatalogReferenceListPayloadTests.cs:96-100`, `StockRpcPayloadTests.cs:151-156`) where `CreditRpcPayloadTests.cs:32` derives it by reflection. Adopt the reflection idiom.
- Id 82: arming failure messages that name nothing. Bullet 5 touches one `CLAUDE.md` sentence — documentation, not source.
- Id 89: strengthen the assertion on the parallel dispatch path.

**Batch B — test-harness robustness (74, 81, 85).**

- **Id 85 is NOT "one shared fix to a helper", and the first version of this plan said so wrongly.** There is no shared helper: `GetFreeTcpPort` has **eight independent copies** (six `KafkaContainerFixture` plus Notifications' two Mailpit fixtures). Its six bullets require: enumerate the class first as a search result; move every self-assigned binding to `WithPortBinding(containerPort, true)` + `GetMappedPublicPort`, the idiom five sibling fixtures already use; **do not fix the Kafka sites naively**, because `KAFKA_ADVERTISED_LISTENERS` must carry the host-visible address, which is why a port was chosen in advance there; prove it by a change of kind, not of probability, by binding the port in the window between check and container start; arm it by re-introducing `GetFreeTcpPort` at one converted site; and delete the eight copies rather than leave them dead.
- Id 74: replace positional `.dlq` reads with correlation-filtered ones.
- Id 81: derive the Gateway OR4 RPC budget rather than fixing it at 2 000 ms.

**Batch C — source hygiene (78, 84, 90).** These touch `src/`.

- Id 78: `Directory.Build.props:22` sets `GenerateDocumentationFile=false`; the entry names specific `cref` targets in `src/Orders` and `src/Billing/Domain/Invoice.cs`.
- Id 84: `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs` duplicates records that live canonically in `src/Contracts/Rpc/`.
- Id 90: guard the `Math.Max(1, ...)` clamp in `SagaCommandDispatchWorker.ExecuteAsync`, and decide clamp-silently vs fail-fast at startup.

**Then the two open entries.** Id 86 (a delegation guard anchored to a call shape rather than to the live host call) and id 92 (#7's missing end-to-end late-approval coverage, which needs the three-container saga harness).

## The one item that is a decision, not a fix

**Id 88.** Id 80 moved the worst case from *one stuck responder stalls every order* to *N stuck responders stall every order*. Eliminating head-of-line blocking entirely is a design change, not a correction. Options go to the maintainer when the batch reaches it; the leader does not decide it.

## Known, and deliberately NOT added to phase 14

The premise check corrected a false claim in this plan's first version: the other open backlog entries are **not** all planned future features.

- **Id 52 is phase 10** — earlier than 14, not later — a retroactive ported-idiom ledger recommended by feature 46's review. An audit finding.
- **Id 47 is phase 21** and is also an audit finding, from feature 45's review: `EfCoreOrderNumberAllocator`'s atomic seed scans `dbo.orders` on every allocation rather than only when seeding. That one concerns production behaviour and is worth the maintainer knowing about, but it is **not** phase 14 and is not being added to it.
- Ids 28–38 (phases 15–25) are genuinely planned future features.

- **Found by batch D1, 2026-09-13, and deliberately NOT filed as a phase-14 entry.** Id 84 unified 17 Gateway records onto `src/Contracts/Rpc` and kept 10 as Gateway-only because the `orders.*` and `catalog.*` payloads have no Contracts counterpart. Those Orders payloads remain genuinely duplicated between `src/Orders/Presentation/Rpc/` and the Gateway. The implementer recommended filing it as its own entry. **The leader declined**, because the maintainer froze phase 14 at 13 items and "finish an item, discover a consequence, grow the list" is the exact pattern the freeze exists to stop. It is recorded here so it is not lost; whether it ever becomes a backlog entry is the maintainer's call, not the leader's.

- **A leader reasoning error worth keeping, from the same batch.** D1 sequenced id 84 before id 70 on the stated ground that 84 would delete a duplicate `OrdersCancelReplyPayload` that 70 arms against. **It did not** — 84 classified both `OrdersCancelReplyPayload` definitions as Gateway-only and kept them, so the hazard never existed. The premise check verified that two definitions existed, which was true; neither it nor the leader checked the step that actually mattered, namely whether id 84 would *remove* one. **A verified premise is not a verified inference drawn from it.** The sequencing was harmless here and the reasoning was wrong.

## Process for this phase, and why it is lighter

These are test-quality fixes, not saga semantics. They do not get spec phases or ledgers. One review pass per batch, not per entry. And every brief is premise-checked before dispatch — the mechanism added on 2026-09-13 after the maintainer asked what the apparatus is for if the coordinator's own recommendations are wrong; on its first run it found four defects in this plan's first version, at about a third the cost of the implementation cycle a wrong brief would have burned.

## The deliberate `done` → `pending` reversion, 2026-09-13

The 11 dispositioned entries were re-opened because **the maintainer rejected the dispositioning and directed that phase 14 be finished and correct.** Each entry's `notes` now opens with a `RE-OPENED 2026-09-13 by maintainer ruling` marker and retains its superseded `ACCEPTED, NOT FIXED` text as history.

**`init.sh`'s backlog tripwire fired on all eleven**, exactly as designed — it cannot distinguish a deliberate reversion from the accidental corruption it exists to catch, and that is the correct trade:

```
[FAIL] backlog tripwire: feature id 69 reverted from done to pending
... (69, 70, 74, 78, 81, 82, 84, 85, 88, 89, 90 — eleven, counted)
```

The `.backlog-snapshot` was then cleared deliberately so the next clean run re-baselines. It is untracked and within-session by design (`init.sh:216-218`). This is recorded here rather than done silently, because a guard that is routinely worked around stops being a guard.

**What made this necessary was a caught error, not a plan.** The first version of brief D1 was premise-checked and returned `DO NOT ACT` on nine findings, the critical one being that the brief presented ids 84 and 70 as open work while the on-disk record said `done — ACCEPTED, NOT FIXED`, dated the same day. The backlog would have said one thing while the work said another. The record is now consistent with the ruling, and only then is a brief dispatched against it.
