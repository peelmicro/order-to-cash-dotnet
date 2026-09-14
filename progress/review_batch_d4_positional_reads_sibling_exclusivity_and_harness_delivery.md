# Review — batch D4, backlog id 74 (positional reads, sibling exclusivity, structural group clearance, `test.runsettings` delivery)

**Verdict: APPROVED.** All seven acceptance bullets are met. One advisory, recorded below, about the *evidence* for one arm — not about the guard, which I armed myself from scratch and which fails with exactly the message the record quotes.

**Scope.** A probing review, not a re-run of the world. I did not re-run `./quality.sh`; I re-derived three populations with my own path-excluded content searches, ran **five armings from scratch** (four from the record's table plus one of my own the record did not run), verified the count attribution against the implementer's own `quality_74.log` with my own summing command, checked two ledger rows against #7's checkout, and read the two new guards in full. Every figure below I measured in this session unless marked otherwise.

---

## 1. The population re-derived — the class does end where the implementer says

My own enumeration, unit = **a Kafka message-read call site**, `bin`/`obj` excluded **by path**:

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "\.Consume(" | sort
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "ConsumerBuilder<" | sort
```

**18 message-read sites, identical to the record's §2 table line for line.** I read the five it calls FIXED and four it calls "already correct" (`FactPartitioningTests:68`, `OutboxRelayTests:94`, `OutboxWireParityTests:86,168`): every one now selects by a value the test itself produced (`eventId`, `correlationId`, or a key that is this test's own order id). **No positional read survives**, and the two positional `ConsumeOneAsync(topic, timeout)` overloads are gone from the tree (`grep ConsumeOneAsync` returns only Orders' correlation-matching one plus four comments describing the retirement).

The entry's claim that the class does not end at the three dead-letter tests it names is **confirmed**: Billing `BC16` and Fulfillment `FS16` really did take the first message on a shared topic and assert only the key. I proved the consequence rather than reading it — see arm P1 below, where the pre-fix shape holds the decoy and the key assertion passes.

**Bullet 5's population, re-derived independently** (membership decided by the capture mechanism, never by the property under test):

```
… | xargs -0 grep -n "Assert.Single(.*\(Measurements\|Exported\|LongMeasurements\|Captured\|Lines\|Records\)"
… | xargs -0 grep -n  "Exported\.\|Measurements\.\|LongMeasurements\."
… | xargs -0 grep -ln "MeterListener\|RecordingActivityExporter\|CapturedConsole"
```

All **8** `Assert.Single(exporter.Exported, …)` sites in the tree now carry `&& a.TraceId == <this test's own activity>.TraceId`; **no** `Assert.Single(capture.Measurements)` / `LongMeasurements` site remains (only the three `MetricCapture` doc comments quoting the retired shape). The remaining `Assert.Single(… .Lines)` / `.Records` hits are over objects the test owns — out of class, correctly. Nothing is left unclassified.

---

## 2. Armings re-run from scratch — 5 of 41, across four classes plus one probe of my own

Protocol on each: `cp -p` backup → mutate → `dotnet build --no-incremental` → the ONE named test → restore from backup → `cmp` → `touch` + forced rebuild → confirming green. Never two builds at once. No git command that writes the tree was used anywhere in this review.

| # | Class | Mutation | Named test | Result |
|---|---|---|---|---|
| **P1** | positional read | `ConsumeMatchingEventId(…)` → `consumer.Consume(20s)!` | `BillingOutboxRelayTests.BC16_…` | **RED**, against real MSSQL + Kafka containers: *"the read from 'otc.billing.facts.v1' returned the envelope with eventId a97d21c5-…, not the fact this test's own relay cycle published (f3a6e743-…). A decoy … was deliberately published to that topic first, under the SAME Kafka key, so a read that selects by POSITION returns the decoy — and the message-key assertion that follows cannot detect it, because the decoy shares the key."* Restored `cmp`-identical, rebuilt, **1/1 green**. |
| **X1** | sibling exclusivity | `capture.SingleOwnMeasurement()` → `Assert.Single(capture.Measurements)`, site 1 only | `RequestLatencyMiddlewareTests.RecordsOnSuccess_TaggedByTheRequestPath` | **RED**: *"Assert.Single() Failure: The collection contained 2 items — [Tuple (4242, …/orders), Tuple (0.0877, …/orders)]"*. Restored, **237/237 green** (the record's Gateway baseline, to the test). |
| **G2** | consumer-group clearance | `KafkaGroupTestHost.StopAsync` → `_inner.StopAsync(ct)` | `KafkaGroupTeardownGuardTests.ABareStopAsync_OnAHostTheHelperHandsOut_StillRunsTheGroupClearance` | **RED**: *"a bare host.StopAsync() ran the consumer-group clearance 0 time(s); it must run it exactly once…"*. Restored, **3/3 green in 69 ms** (the record's performance claim holds). |
| **R1** | `test.runsettings` delivery | `<RunSettingsFilePath>` added to `Seed.UnitTests.csproj` | `TestRunSettingsDeliveryTests.NoTestProject_OverridesRunSettingsFilePath` | **RED**, naming the project and the value it declared. Restored `cmp`-identical, **49/49 green**. |
| **C1** | census (bullet 6's structural guard) | one real site unwrapped (`HealthProbesTests:160`) | `KafkaGroupHostWrappingTests.EveryTestHostBuiltFrom…` | **RED**: *"1 of 16 test host(s) … are not wrapped in a KafkaGroupTestHost: Orders.IntegrationTests/HealthProbesTests.cs::R60_OR6_…"*. Restored, **49/49 green**. |
| **+1 mine** | not in the record | provenance filter defeated **inside** `MetricCapture.OwnMeasurements` | both `RequestLatencyMiddlewareTests` cases | **RED with the guard's OWN message**: *"expected EXACTLY ONE 'otc_request_latency_ms' measurement recorded by this test's own flow; saw 2 (…)"* — so the id-82 naming requirement is satisfied by the guard itself, not only by the pre-fix shape it replaces. Restored, 237/237. |

Every one reproduced the record's wording. **The re-runs after the `IDE1006` rename are real, not carried forward**: `scratchpad/rearm_all.sh` (23:11) drives four per-class scripts and `rearm_all.log` (23:17, 40 KB) contains 35 test invocations with full verbatim output — and my own independent R1 and C1 runs reproduce its messages exactly, including *"1 of 16"* and the same offender method name.

---

## 3. Bullet 3 — the determinism is in the TEST, not in the mutation

Confirmed by reading and by running. Each read site publishes a **decoy envelope with a different `eventId`, under the SAME Kafka key, before the real record exists** (`PublishDecoyAsync`, in the test). Same key ⇒ same partition ⇒ strictly earlier offset ⇒ the decoy is first on **every** run by a Kafka ordering guarantee, not by timing. The mutation is **only** the selection (`ConsumeMatchingEventId` → `consumer.Consume(20s)`); it contains no delay, no sleep and no repetition. P1 went red first try. The same structure holds for bullet 5: `RecordFromAConcurrentWriter` uses `ExecutionContext.SuppressFlow()` + a fresh `Thread` that is **joined** before the assertion, so the unscoped read sees exactly two deterministically — and the four exporter sites start `StartAForeignSpan(displayName)` as a root activity (`parentContext: default`) in the test. **The streak is kept.**

---

## 4. Bullets 6 and 7 — structural, not per-site

**Bullet 6.** The clearance lives in `KafkaGroupTestHost` (three copies), whose `StopAsync`, `Dispose` and `DisposeAsync` all route through one idempotent clearance — so a bare teardown cannot escape it, and the helper's **declared return type** carries the property to all 51 existing call sites by the compiler. The question the brief asked — *would a NEW per-site omission fail something?* — is answered **yes, by measurement**: arm C1 unwrapped one real site and the census failed naming it. The census derives its population from **content** (invocations of the three composition roots that unconditionally register their `KafkaFactStreamSubscriber`), is a **Roslyn** instrument parsed with `DEBUG` so parser and compiler agree on live regions, scopes the wrapper search to the **enclosing method**, carries a **literal** exemption set with a written reason, fails on a **stale** exemption, and fails if it finds **nothing**. The non-masking half is real: the clearance never throws, records the leak, and enforces on the **setup** path — `ATestThatFailsAndWhoseTeardownAlsoFails_StillReportsItsOwnFailure` asserts the original message survives *and* carries a control proving the teardown genuinely failed.

**Bullet 7.** `TestRunSettingsDeliveryTests` uses a **literal** 18-project list and derives the rest **by subtraction** — `TheLiteralProjectList_MatchesTheTreeInBothDirections` reads the tree (path-excluded `bin`/`obj`/`publish`) and fails in both directions, so a nineteenth project cannot silently escape; it fails the guard until named. Project files are read as **parsed XML**, closing the comment and conditioned-`PropertyGroup` shapes structurally. Verified by arm R1 and by my own arm of escape shape 3 (below).

---

## 5. Ported-idiom ledger — two rows spot-checked against #7's checkout, including the corrected one

- **L74-2 (outbox relay)** — **correct.** `apps/billing/src/infrastructure/outbox/outbox-relay.integration.spec.ts:49-73` is `consumeForKey(key, groupId)` with `if (messageKey !== key) { return; }`. #7 filtered; it never took the first message. The row's characterisation of what #8 dropped in translation is accurate.
- **L74-5 (the corrected "none owed")** — **correct, and the correction was the right call.** `find … \( -name '*.runsettings' -o -name 'vitest.config*' \)` in #7 returns 10 `vitest.config*` + 7 `vitest.integration.config.mts`; every `apps/*/vitest.config.mts` and `packages/*/vitest.config.mts` imports **only** `defineConfig` — no shared base, so "#7 has no inheritance at all" is verified. `grep -rn reloadConfigOnChange` returns nothing outside `node_modules`. And `apps/seed/src/domain-threshold-guard.spec.ts` really does carry a hand-maintained literal `NESTJS_SERVICES` list with a literal `EXEMPT_VACUOUS_DOMAIN` exemption set, for the reason the row quotes. A row **was** owed; the drafted "none owed" was wrong for the reason the rule predicts — an unchecked premise about #7.
- Incidentally verified while there: **L74-1** (`notification-dead-letter.integration.spec.ts` and `projector-dead-letter.integration.spec.ts` both `find((m) => m.value.eventId === poisonEnvelope.eventId)`) and **L74-4** (`kafka-test-fixture.ts:88-126`, `waitForConsumerGroupReady` — a **setup-path** readiness check, no teardown clearance anywhere). Both history halves accurate.

*One wording nit, not a defect:* L74-5 says "ten `vitest.config.mts` files"; nine are `.mts` and `apps/web/vitest.config.ts` is `.ts`. The substance — no shared base — holds for all of them.

---

## 6. Count reconciliation — attribution checked, not taken on trust

`quality_74.log` (23:37): **18 projects, 2 034 passed, 0 failed, 0 skipped, `quality.sh finished`.** I summed the 18 per-project figures myself with my own command: **2 034**; `grep -c '^Failed!'` → **0**.

Attribution **+17 = 2 017 + 17**, confirmed independently:
- `Architecture.Tests` **41 → 49 (+8)**. The 41 baseline is D3's review's own independently-discovered figure; I measured **49/49** in this session, three times. `grep -c '\[Fact\]'` on the two new files gives **7 + 1 = 8**.
- `Orders/Notifications/Projector.IntegrationTests` **+3 each**: `grep -c '\[Fact\]'` on each `KafkaGroupTeardownGuardTests.cs` gives **3, 3, 3**; I ran the Notifications copy and saw **3/3**. `Projector.IntegrationTests` 65 → 68 matches D3's independently-discovered 65.
- The four named unit baselines come back unchanged; I measured **Gateway.UnitTests 237/237** myself.
- Two helper *methods* were deleted, no `[Fact]`s. **8 + 9 = 17. Exact.**

---

## 7. `CHECKPOINTS.md` walk

- **C1** — [x] harness complete; `./init.sh` exits 0 (`scratchpad/init_74.log`, 23:38; re-verified by the leader).
- **C2** — [x] exactly one `in_progress` (id 74) before this close, now `done`; all statuses valid; `progress/current.md` is this session's.
- **C3** — [x] **no change under `src/`** (`git diff --stat -- src/` empty); `Architecture.Tests` **49/49 green in this session**, which is the NetArchTest suite run, not eyeballed. No new shared runtime code; nothing crosses a service boundary.
- **C4** — [x] `./quality.sh` clean end to end (format + build with zero warnings + 18 suites + coverage); domain tests untouched and still pure; the integration work is against **real containers** — I ran `BillingOutboxRelayTests.BC16` against live Testcontainers MSSQL + Kafka in this review. No Jest.
- **C5** — [x] untracked files are this batch's test sources and the two `progress/` records — nothing suspicious, no build output; history entry with effort record appended below; `feature_list.json` updated by me, one line; the human has not committed and neither have I.
- **C6** — n/a, `sdd: false`.
- **C7** — [x] `specs/shared/` untouched (`git diff --stat -- specs/` empty); no `SA-n` owed; no acceptance criterion here is blocked by the shared spec, so nothing needs routing to a backlog entry.

---

## 8. Advisories — named here, not filed (phase 14 is frozen at 13 items)

1. **The R3 arm's recorded evidence does not exist in any log, and the post-rename re-run of it did not arm anything.** `scratchpad/b7/arm.sh` (mtime 22:15) still writes the **empty** `<Project></Project>` shadow that the record itself discloses "did not arm anything" (`NETSDK1013`, the build fails before the guard runs) — and `rearm_all.log`'s ARM C at 23:11–23:17 shows exactly that failure again, with no test executed. The record says the rerun "used a shadow that carries the minimum to build"; no such script or log is on disk, and `grep -rn "NoDirectoryBuildPropsShadows" scratchpad/` finds only the script's `run` line. So the claim *"R1–R7 … were re-run"* is **false for R3**.
   **Why this is an advisory and not a rejection: I armed R3 myself**, with a buildable `tests/Directory.Build.props` carrying `TargetFramework`/`ImplicitUsings`/`Nullable`/`LangVersion` and forgetting `RunSettingsFilePath`. Build succeeded, and `NoDirectoryBuildPropsShadowsTheRootOne_UnderTests` failed with **the exact message the record quotes**: *"1 Directory.Build.* file(s) exist under tests/ and shadow the root one MSBuild would otherwise reach: [tests/Directory.Build.props]. …"*. Restored (file removed), **49/49 green**. The guard is real and armed; what was missing was the evidence, and this review supplies it. Note also that R3's mutation touches no compiled source, so the `IDE1006` rename could not have invalidated it — the gap is in the bookkeeping, not in the coverage.
2. **Two numbers in the record's §11 re-arm paragraph do not reconcile.** *"31 of the 35 arms were re-run"* — the denominator is **41**, not 35 (31 listed + P3–P6 = 35 re-run, of 41). And *"The four not re-run (P1, P2, X10–X13)"* names **six** arms, not four. The **sets** are complete and mutually consistent (35 + 6 = 41); only the two counts are wrong. In a repository whose commit hook exists because of exactly this, worth fixing in the record before the phase is committed.
3. **`NoDirectoryBuildPropsShadowsTheRootOne_UnderTests` fails on ANY `Directory.Build.*` under `tests/`, including one that imports the root.** That is stricter than the escape it names. Deliberate-looking and harmless today (there are none), and the failure message explains the condition — recording it only so a future legitimate shadow is not mistaken for a defect.
4. **A census false-negative shape, for whoever touches it next:** an unwrapped `OrdersHost.CreateBuilder` host in a method that *also* constructs a `KafkaGroupTestHost` for a different host would pass. Narrow, no instance exists, and closing it would need per-variable flow analysis.

---

## Effort record

See `progress/history.md`.
