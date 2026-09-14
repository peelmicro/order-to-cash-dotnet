# id 94 — `SagaConsumptionTests.SO9`'s recorded F6 arm no longer kills its own test

**Status: PASS.** Strengthened (not renamed-alone). Both F6 mutations now
fail the test with a message naming the broken claim, restored cleanly,
green on a forced rebuild.

File touched: `tests/Orders.IntegrationTests/SagaConsumptionTests.cs` only.
`src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs`
was mutated three times for arming and is `cmp`-identical to a pre-mutation
backup after every restore; `git status --porcelain src/` is empty.

---

## 1. Reproduce (acceptance bullet 1)

Applied the exact F6 row-1 mutation `impl_order_saga_orchestrator.md`
records as killing the test: `KafkaFactStreamSubscriber.cs:134`,
`EnableAutoOffsetStore = false` → `true`.

```
dotnet build tests/Orders.IntegrationTests/Orders.IntegrationTests.csproj --no-incremental
dotnet test tests/Orders.IntegrationTests/Orders.IntegrationTests.csproj --no-build \
  --filter "FullyQualifiedName~SagaConsumptionTests.SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered"
```

Result, verbatim:

```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 11 s
```

The recorded arm is dead — confirmed, not assumed. Restored from a `cp`
backup, `cmp`-identical, forced `--no-incremental` rebuild, re-ran the
(then-unmodified) test: **Passed! 1/1, 13 s**.

## 2. The mechanism, proved rather than asserted (acceptance bullet 2)

Read, in order: `src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs`,
`src/Orders/Presentation/SagaFactsConsumer.cs`,
`src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs`.

**Link 1 — `FactRetryDispatcher.DispatchAsync` catches the handler's
exception inside its own loop and never rethrows it (except
`OperationCanceledException`).** `FactRetryDispatcher.cs:79-115`: a `for`
loop up to `MaxAttempts` (default 3, `FactRetryOptions.cs:16`), `catch
(Exception ex)` at line 94 swallows every non-cancellation exception, logs,
delays (`backoffMs << (attempt-1)`) and loops. On exhaustion (line
117-146) it publishes to the DLQ and **returns normally** — the file's own
banner states this is deliberate: "never rethrows on exhaustion — so the
caller's own Kafka subscriber loop reaches `StoreOffset` and the partition
is not blocked". **HOLDS.**

**Link 2 — `SagaFactsConsumer.HandleMessageAsync` wraps the whole
processing path in `factRetryDispatcher.DispatchAsync`.**
`SagaFactsConsumer.cs:146-154`. Nothing between the envelope
deserialisation (which catches its own exceptions and returns, lines
76-95) and the dispatcher call can throw past this wrapper for an ordinary
processing failure. **HOLDS.**

**Link 3 — `KafkaFactStreamSubscriber.ConsumeAsync`'s `await
handler(...)` (line 94) therefore never throws for an ordinary handler
exception, so `consumer.StoreOffset(consumeResult)` (line 96) runs on the
SAME `Consume()` iteration, and `consumer.Close()` in the `finally` (line
106) is reached only on graceful shutdown/cancellation — never on this
failure path.** Direct consequence of links 1-2, confirmed by reading the
`finally` block: the only paths back into it are normal loop exit
(cancellation) or an exception escaping `await handler(...)`, which link 2
shows cannot happen here. **HOLDS.**

**Link 4 — `gate.Attempts >= 2` is satisfied by `FactRetryDispatcher`'s
own in-process retry loop, on the SAME `Consume()` call, never by a fresh
Kafka delivery to a rebuilt/rebalanced consumer.** Direct consequence of
links 1-3: there is exactly one `consumer.Consume()` call for this
message in the whole scenario; the second `ThrowOnceGate.BeforeEnqueueAsync`
invocation happens because `DispatchAsync`'s loop calls `process(ct)`
again, in-process, after its own backoff delay — not because a new
`ConsumeResult` was returned by a fresh `Consume()` call. **HOLDS.**

All four links hold. The test's own name (`…AndTheFactIsRedelivered`)
claims a mechanism (Kafka-level redelivery to a rebuilt consumer) that is
no longer reachable via an ordinary handler exception once OR1 is wired
in — this is by design, not a regression: OR1's whole point is that one
poison message must not block a partition via Kafka-level
redelivery-forever, so a handler exception is contained and retried
in-process instead.

**Why the single, immediate committed-offset read was also blind (a
detail the brief's diagnosis pointed at but did not fully explain).**
`KafkaFactStreamSubscriber`'s config keeps `EnableAutoCommit = true`
regardless of `EnableAutoOffsetStore`. The library's own default behaviour
under `EnableAutoOffsetStore = true` (F6 row 1) is documented in the
class's own remarks, quoted from the pinned Confluent.Kafka 2.15.0 XML
docs: a message's offset is auto-stored the moment `Consume()` **returns**
it — i.e. immediately, well before the in-process retry loop even starts —
but the periodic background committer only turns a stored offset into a
**committed** one every `auto.commit.interval.ms` (documented default
5 000 ms). The old test read the committed offset within roughly 500-1500
ms of order placement (the time for `gate.Attempts` to reach 2, given a
500 ms backoff). That window is far short of one full auto-commit tick, so
even the premature auto-store from F6 row 1 had no way to be visible yet —
independent of the `consumer.Close()` mechanism the test's own comment
(now corrected) attributed it to. This is why the reproduction in §1 shows
the **whole test**, including its broker-read halves, passing clean under
F6 — not just a `gate.Attempts`-only assertion.

## 3. The decision: strengthen, and rename because strengthening the
"genuine redelivery" claim specifically is infeasible (acceptance bullet 3)

**Strengthened.** The "not before" half no longer takes a single read
immediately after `gate.Attempts` reaches 2. It now polls the broker
through a window (`_autoCommitIntervalBracket` = 7 s, comfortably past the
5 s default `auto.commit.interval.ms`) **while the retry remains
deterministically blocked on `ThrowOnceGate`** (never released until this
whole polling window has elapsed and passed), asserting on every read that
the committed offset still equals the baseline and failing the instant it
does not, with a message naming the observed offset and both F6
mutations. This directly closes the gap in §2: any offset stored before
the handler genuinely completes — whether via F6 row 1's auto-store or
F6 row 2's early explicit `StoreOffset` — now has a full commit cycle to
reach the broker before the assertion window closes, and the assertion is
re-evaluated on every poll rather than once.

**Verified against both F6 mutations (see §5) — both now fail.**

**Renamed** from
`SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered`
to
`SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedUntilItSucceeds`,
**in addition to strengthening, not instead of it** — because one specific
part of the old name's claim cannot be strengthened into truth under the
current, *correct* architecture: "genuine Kafka redelivery" of a handler
exception is not merely hard to observe, it is **architecturally
unreachable** now (§2, links 1-4) — `FactRetryDispatcher` never lets an
ordinary handler exception escape to the point where Kafka-level
redelivery could occur, by design (OR1's whole purpose). An assertion that
"only passes on genuine Kafka redelivery" would have to FAIL on today's
correct code, which is not a test anyone wants. The alternative — forcing
a real redelivery by killing the whole host process mid-retry, or
bypassing `FactRetryDispatcher` from the test — was rejected: the former
is a materially different (and heavier) kind of test than an in-process
xUnit integration fixture is suited to, the latter would test a path
production code does not take, and both are out of this entry's declared
scope (test-only, `SagaConsumptionTests.cs`). What **is** still true, is
still this test's whole point, and is what the requirement's own SHALL
clause (`specs/order_saga_orchestrator/requirements.md` §SO9: "advance the
committed offset … only after the handler … has returned successfully")
actually binds, is the offset-timing property — and that is exactly what
the strengthened assertion now proves.

**Spec traceability note (not fixed — out of this entry's file scope).**
`specs/order_saga_orchestrator/requirements.md:88`'s test-matrix row still
names the OLD method name. This entry's brief restricts touched files to
`SagaConsumptionTests.cs` (and support fixtures) plus this record, so the
row was not edited. Flagged here as a disclosure for the coordinator to
route — it is a one-line mechanical update to a feature-owned spec file
(not `specs/shared/`), not a design change.

## 4. Class enumeration (acceptance bullet 4)

Command (path-excluded by `find -not -path`, never by post-filtering
`grep`'s content output):

```
find progress -maxdepth 1 -name '*.md' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -nE '^\s*\|.*(EnableAutoOffsetStore|AutoOffsetReset|StoreOffset|EnableAutoCommit)'
```

Complete output (17 lines) — kept verbatim in
`/tmp/…/scratchpad/id94_class_enum_raw.txt` during the session; reproduced
here in full:

```
progress/review_notifications_service.md:34:| P11 | **Live `AutoOffsetReset.Latest` observation** — 10 of 18 partitions held **no** committed offset for the `notifications` group and carried ~30 historical facts | Mailpit stayed at 7 through assignment — `Latest` genuinely suppressed the backlog |
progress/review_notifications_service.md:228:| Ledger entry — `AutoOffsetReset.Latest` | none | ❌ **D3** |
progress/review_notifications_service.md:277:| R2-P5 | **D3** — `AutoOffsetReset.Latest` → `Earliest` on `KafkaFactStreamSubscriber.cs:99` | **FAILS by name**, verbatim below |
progress/review_notifications_service.md:440:| Ledger entry — `AutoOffsetReset.Latest` | `KafkaFactStreamSubscriberConfigTests` (3 cases) | ❌ **D3** | ✅ **flipped by me, dies by name** |
progress/impl_projector_read_model.md:58:| **K8** | Changed `AutoOffsetReset.Earliest` to `Latest` **at the assignment site** in `KafkaFactStreamSubscriber.cs` | `KafkaFactStreamSubscriberConfigTests.PR38_BuildConsumerConfig_SetsEarliestNotLatest_BecauseTheReadModelIsRebuiltByReplay` | `Assert.Equal() Failure … Expected: Earliest / Actual: Latest`. Mutation-applied proof: SHA-256 before `52a8eb7c…7fe284`, after `d341607…418b79` — genuinely different |
progress/impl_projector_read_model.md:79:| **B** | Doc-comment-only edit: `Earliest`→`Latest` inside `KafkaFactStreamSubscriber`'s class-remarks `<see cref="AutoOffsetReset.Earliest"/>` | `SURVIVED` | `SURVIVED` ✓ (sentinel proving the sweep can report a survivor) |
progress/spec_projector_read_model.md:31:| 11 | **`AutoOffsetReset.Earliest`, the opposite of the service next door** | … Guarded by a reflection read of the private `BuildConsumerConfig`, by an integration case producing before the group first subscribes, and by `OffsetContractTests` reading the committed offset **from the broker** | No |
progress/spec_projector_read_model.md:58:| 38 | **The mutation sweep, and its own machinery** | … | No |
progress/impl_batch_d2_pacing_arming_message_and_dispatch_guards.md:248:| 82-19 | `KafkaFactStreamSubscriber.cs` (Orders) — `EnableAutoOffsetStore = false` → `true` | `SagaConsumptionTests.SO9_AHandlerThatThrows_…` | **SURVIVED — Passed 1/1.** See §5, Disclosure D1. |
progress/review_order_saga_orchestrator.md:21:| **`EnableAutoCommit = true` ⇒ `false`** in `KafkaFactStreamSubscriber` | **`SagaConsumptionTests` stays GREEN, 2/2** — see D1 |
progress/review_order_saga_orchestrator.md:203:| P1 | `EnableAutoCommit = true` ⇒ `false` (`KafkaFactStreamSubscriber.cs:115`), run `SagaConsumptionTests` | **FAILS at `SagaConsumptionTests.cs:219`** … |
progress/review_order_saga_orchestrator.md:204:| P2 | `consumer.StoreOffset(consumeResult)` moved **before** `await handler(...)` (§12 row 3's mutation), run `SagaConsumptionTests` | **FAILS at `SagaConsumptionTests.cs:190`** … |
progress/impl_order_saga_orchestrator.md:306:| 1 | `EnableAutoCommit = false` (the reviewer's own probe, `KafkaFactStreamSubscriber.cs:115`) | **FAILS** | … |
progress/impl_order_saga_orchestrator.md:307:| 2 | F6 row 1 — `EnableAutoOffsetStore = true` (`KafkaFactStreamSubscriber.cs:116`) | **FAILS** | … |
progress/impl_order_saga_orchestrator.md:308:| 3 | F6 row 2 — `StoreOffset` moved to **before** `await handler(...)` (`KafkaFactStreamSubscriber.cs:93-95`) | **FAILS** | … |
progress/spec_order_saga_orchestrator.md:24:| 6 | Offset-commit-after-handler semantics. … | armed twice (`tasks.md` F6 — flip the flag back to the default; move `StoreOffset` above the `await`). `design.md` §3.3. |
progress/impl_observability_reliability.md:795:| **L12** | All three `OR1_R16_...` integration tests' committed-offset assertion, `consumer.Committed(...)` | **Yes**, and directly armed for Orders: mutating `FactRetryDispatcher` to rethrow AFTER publishing the dead letter … made the SAME test's offset-advance assertion time out … |
```

Classification, one line per **distinct recorded arm** (several rows above
are the same arm quoted from multiple documents):

| Arm | Target test | Mutation | In THIS class (could OR1's in-process retry satisfy its gate the same way SO9's did)? |
|---|---|---|---|
| Orders 82-19 / F6 row 1 | `SagaConsumptionTests.SO9_…` | `EnableAutoOffsetStore = false → true` | **YES — this is the named instance.** Fixed by this entry (§5: now fails). |
| Orders F6 row 2 | `SagaConsumptionTests.SO9_…` | `StoreOffset` moved before `await handler` | **YES — same instance, same test.** Fixed by this entry (§5: now fails). |
| Orders historical P1/P2 (`review_order_saga_orchestrator.md`, pre-OR1) | `SagaConsumptionTests` (same test, an earlier phase) | Both F6 mutations | **HISTORICAL** — these are the SAME test's own arming history from before `observability_reliability` added `FactRetryDispatcher`; they killed the test then because the exception genuinely propagated to `Close()` at that time. Superseded by the 82-19 finding and this entry; not a second, separate instance. |
| Orders `EnableAutoCommit = true ⇒ false` | `SagaConsumptionTests.SO9_…` | Disables auto-commit entirely (the library's *wrong alternative* fix, not F6) | **NO** — different failure mode: the offset is stored but never committed at all, so the "does advance" half's `WaitForCommittedOffsetToExceedAsync` genuinely times out. Unrelated to the in-process-retry swallowing defect; still correctly caught today (unchanged by this fix). |
| `observability_reliability` L12 (Orders/Notifications/Projector `OR1_R16_...` DLQ tests) | `SagaDeadLetterTests`/`NotificationDeadLetterTests`/`ProjectorDeadLetterTests` | `FactRetryDispatcher` mutated to rethrow AFTER publishing the DLQ record (a `src/`-level mutation of the dispatcher itself, not a consumer-config mutation) | **NO** — proves a different, still-live property (the offset only advances after `DispatchAsync` genuinely returns, whether via success or DLQ-exhaustion), not the "not before" premature-store property F6 targets. Unaffected by this class. |
| Notifications D3 / `AutoOffsetReset.Latest → Earliest` | `KafkaFactStreamSubscriberConfigTests` (reflection on the real private `BuildConsumerConfig`) | Config-value mutation | **NO** — reads the built `ConsumerConfig` object directly by reflection; there is no behavioural inference through `FactRetryDispatcher` to defeat. Structurally immune to this class. |
| Projector K8 / `AutoOffsetReset.Earliest → Latest` | `KafkaFactStreamSubscriberConfigTests.PR38_BuildConsumerConfig_SetsEarliestNotLatest_…` | Config-value mutation, same shape as Notifications' D3 | **NO** — same reason: reflection on the config object, not attempts-counting. |

**A new instance found by this enumeration, not previously armed against
this specific mutation, and NOT fixed here (out of this entry's declared
scope — `SagaConsumptionTests.cs` only):**

`Projector.IntegrationTests/OffsetContractTests.cs` ›
`PR38_AThrowingHandlerLeavesTheCommittedOffsetUnchanged_ReadFromTheBroker`
has the **identical vulnerable shape** SO9 had before this fix: a
`ThrowOnceGate`-style in-process counter, a single immediate
`Assert.True(baseline == afterFailedDelivery, …)` read taken right after
`gate.Attempts >= 2`, and `src/Projector/Presentation/ProjectorFactsConsumer.cs:128`
wraps its own dispatch in the SAME canonical `FactRetryDispatcher`
(`src/Projector/Infrastructure/Messaging/FactRetryDispatcher.cs` — the
copy, confirmed present) with the identical
`EnableAutoOffsetStore = false` config
(`src/Projector/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs:114-115`).
Every recorded arm against `PR38` (K8, above) mutates `AutoOffsetReset`,
never `EnableAutoOffsetStore`/`StoreOffset`-ordering — so the specific F6
class was never tried against it. This is **plausible by code-reading,
not empirically re-armed** (mutating Projector's `src/` is outside this
Orders-only entry's declared touch scope), so it is reported as a finding
rather than a measured fact, per the same convention that produced id 94
itself (a disclosure found while working other work, routed rather than
fixed inline). Recommended: a new backlog entry, "Projector's PR38 may
share id 94's defeated arm — verify by applying the two F6-equivalent
mutations to `src/Projector/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs`
and re-running `PR38`; if it survives, apply the SAME bracket-window
strengthening this entry applied to SO9."

**A second, unarmed observation: Orders is the only one of the three
fact-consuming services with no reflection-based
`KafkaFactStreamSubscriberConfigTests`-style unit guard on
`EnableAutoOffsetStore`/`EnableAutoCommit`.** Both Notifications
(`tests/Notifications.UnitTests/KafkaFactStreamSubscriberConfigTests.cs`)
and Projector (`tests/Projector.UnitTests/KafkaFactStreamSubscriberConfigTests.cs`)
carry one; `grep -rln "BuildConsumerConfig" tests/Orders.UnitTests/*.cs
tests/Orders.IntegrationTests/*.cs` returns only this file
(`SagaConsumptionTests.cs`, the integration-level test this entry
strengthens). A defense-in-depth unit test at Orders would be structurally
immune to this whole class (as Notifications' and Projector's already are)
— not added here, since it is a NEW test file rather than the file this
entry's brief names, but recommended as a small, low-risk follow-up.

## 5. Arming table (acceptance bullet 5)

All mutations applied to
`src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs`,
backed up with `cp` beforehand, restored with `cp` afterward, `cmp`-verified
byte-identical to the backup after every restore, then force-rebuilt
(`dotnet build … --no-incremental`) before the next run. `pgrep -a -x
dotnet` / idle-MSBuild-node check run before every build; no two
builds/tests overlapped.

| # | Mutation | Command | Result (verbatim) |
|---|---|---|---|
| 1 (reproduce, §1) | `EnableAutoOffsetStore = false → true`, against the **old** (unrenamed) test | `dotnet test … --filter "FullyQualifiedName~…LeavesTheCommittedOffsetUnchangedAndTheFactIsRedelivered"` | `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 11 s` |
| 2 (arm, F6 row 1) | Same mutation, against the **new**, strengthened, renamed test | `dotnet test … --filter "FullyQualifiedName~…LeavesTheCommittedOffsetUnchangedUntilItSucceeds"` | `Failed OrderToCash.Orders.IntegrationTests.SagaConsumptionTests.SO9_AHandlerThatThrows_LeavesTheCommittedOffsetUnchangedUntilItSucceeds [13 s]` — `Error Message: the committed offset advanced to [p0=unset, p1=unset, p2=unset, p3=1, p4=unset, p5=unset] (baseline was [p0=unset, p1=unset, p2=unset, p3=unset, p4=unset, p5=unset]) WHILE the retry was still deterministically blocked on the gate — a StoreOffset call reached the broker before the handler completed (F6: EnableAutoOffsetStore = true, or StoreOffset moved before the handler's await).` |
| 3 (restore + rebuild) | Restored from backup | `cmp` → identical; `dotnet build … --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)` | — |
| 4 (arm, F6 row 2) | `StoreOffset` moved before `await handler(...)` | `dotnet test … --filter "FullyQualifiedName~…LeavesTheCommittedOffsetUnchangedUntilItSucceeds"` | `Failed … [13 s]` — `Error Message: the committed offset advanced to [p0=unset, p1=unset, p2=unset, p3=unset, p4=unset, p5=1] (baseline was […unset…]) WHILE the retry was still deterministically blocked on the gate — a StoreOffset call reached the broker before the handler completed (F6: EnableAutoOffsetStore = true, or StoreOffset moved before the handler's await).` |
| 5 (restore + rebuild) | Restored from backup | `cmp` → identical; `sed -n '90-97p'` re-read confirms `await handler(...)` precedes `consumer.StoreOffset(consumeResult)` again; `dotnet build … --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)` | — |
| 6 (confirm green, both tests in the file) | Fully restored tree | `dotnet test … --filter "FullyQualifiedName~SagaConsumptionTests"` | `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 32 s` (`SO1_...` and `SO9_...LeavesTheCommittedOffsetUnchangedUntilItSucceeds`) |

`git status --porcelain src/` and `git diff --stat` on
`KafkaFactStreamSubscriber.cs` are both empty after the final restore.

## 6. Files touched

- `tests/Orders.IntegrationTests/SagaConsumptionTests.cs` — the only
  permanent change: renamed method, new
  `_autoCommitIntervalBracket` constant, strengthened "not before"
  polling loop, updated comments (Close()-based mechanism description
  removed as stale; "redelivery" reworded to "retry" where the claim
  is now about the in-process mechanism).
- `src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs`
  — mutated three times for arming/reproduction, restored and
  `cmp`-verified byte-identical every time. **No net change.**
- `progress/impl_id94_so9_dead_arm.md` (this file).

## 7. What I could not do, and why

- Did not empirically re-arm Projector's `PR38` against the F6-equivalent
  mutations — out of this entry's declared file scope (`src/Projector`
  is untouched by design; see §4's disclosure).
- Did not add an Orders-side `KafkaFactStreamSubscriberConfigTests`
  reflection guard (§4's second observation) — a new test file, not the
  file this entry's brief names.
- Did not update `specs/order_saga_orchestrator/requirements.md:88`'s
  stale test-matrix row naming the old method name — outside the
  brief's declared touch scope (`specs/` was not listed); flagged in §3
  for the coordinator to route.
- Did not touch `feature_list.json`, per the brief.

## 8. What surprised me

The single committed-offset read in the OLD test was not merely
insufficiently strengthened against F6 — it could **never** have caught
F6 once OR1 landed, regardless of when exactly `gate.Attempts` happened to
reach 2, because the mechanism it was implicitly relying on
(`consumer.Close()`'s immediate commit on a dying consumer) had been
replaced by a periodic background committer with a fixed ~5 s cadence, and
the test's own timing budget for reaching `gate.Attempts >= 2` (~500-1500
ms, governed by `FactRetryOptions.BackoffMs`) is structurally shorter than
that cadence. The old arming record's own "SURVIVED" finding (82-19) is
therefore not a flake or a near-miss — it was permanently, deterministically
dead the moment OR1 shipped, on every run, which is why the reproduction
in §1 needed no luck to demonstrate.
