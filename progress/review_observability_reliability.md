# Review — `observability_reliability` (id 27, phase 14, `sdd: true`) — round 1

**Verdict: REJECTED.** Status left at `in_review`, as the dispatching brief instructs (the reviewer definition's default is `in_progress`; the brief governs). `feature_list.json` not edited.

**Where this stands.** The five probes the leader asked for all **killed** — the health fixes, the in-flight liveness guard and the round-3 regression fix are real, and failed on the time and message they claim. Three probes of my own, one per mutation family the leader's set did not cover, all **survived on whole-project green suites**. Two of them sit on acceptance criteria this feature closes (`R57`'s continue-on-consume leg, `R58`'s "correlationId in every log line"); the third is a wire field `OR1` names. Every one is the class `CLAUDE.md` says to look for first: a guard that exists, passes, and does not execute the code it is named for. **Recommendation:** one bounded fix round on D1–D4 plus the three record corrections; nothing here needs a gate or an amendment.

## Scope — what I ran, and what I did not

- **Not re-run:** `./quality.sh` in full. The claim "1758 green after the last change" was verified by reading the round-3 log instead: `scratchpad/quality-groupn-round3.log`, written 21:51:59 → 22:03:25, after the last source/test mtime (`SagaFactsConsumer.cs` 21:51:23); 18 `Passed!` lines summing to **1758**, zero `Failed!`, format `[OK]`.
- **Run whole** (each claim was about a whole project): `Architecture.Tests` 21/21 (NetArchTest, C3); under my mutations, `Notifications.UnitTests` 77/77, `Notifications.IntegrationTests` 14/14, `Fulfillment.UnitTests` 130/130, `Fulfillment.IntegrationTests` 61/61, `Projector.UnitTests` 115/115, `Projector.IntegrationTests` 57/57.
- **Run as one named test:** each of the leader's probes.
- `./init.sh` → exit 0, §5d shared-spec parity `[OK]`, only the two standing `[WARN]`s.
- Protocol on every probe: `cp -p` backup → mutate → `dotnet build --no-incremental` → run → record verbatim → restore from backup → `cmp` → `touch` → forced rebuild → confirming run. One build at a time; waits on PIDs with `kill -0`. Final state: all eight mutated files `cmp`-identical to their backups, `grep` for probe markers under `src/`/`tests/` → no hits, no dotnet process alive, `feature_list.json` hunks unchanged (`@@ -406`, `-408`, `-415`, `-468`, `-998`, `-1000`).

## Probes

### The leader's five — all killed

| # | Mutation (current code) | Named test | Verbatim failure | Restore / confirm |
|---|---|---|---|---|
| 1 | Notifications `KafkaHealthCheck`: `_timeout` 2 s → 120 s, `SocketTimeoutMs` line deleted | `Notifications.IntegrationTests.HealthProbesTests.R60_OR6_…` | **[10 s]** `/health/ready took 8003ms while Kafka was paused, exceeding its 6s bound (design.md §8.3: 2 checks x 2s + 2s margin) — the initial in-flight call.` (`HealthProbesTests.cs:205`) | `cmp` identical; rebuilt; 1/1 green |
| 2a | Fulfillment `MsSqlHealthCheck`: `Pooling = false,` deleted | `MsSqlHealthCheckPoolingTests.NeverReusesAPooledPhysicalConnection…` | **[3 s]** `MsSqlHealthCheck.CheckAsync call #1 did not return within 3000ms of MS-SQL being paused (still running at 3002ms) — a pooled connection reused against an unresponsive server can hang far longer than its own stated timeout.` | `cmp` identical; rebuilt; green |
| 2b | Orders `KafkaHealthCheck.CheckAsync`: `Task.Factory.StartNew(…LongRunning…)` → synchronous `GetMetadata` + `Task.FromResult` | `Orders.UnitTests.KafkaHealthCheckLongRunningTests.RunsTheBlockingGetMetadataCall…` | **[2 s]** `CheckAsync itself took 2003ms to RETURN its Task — the synchronous GetMetadata call is blocking the CALLING thread instead of running on its own dedicated (LongRunning) thread.` | `cmp` identical; rebuilt; 1/1 green |
| 2c | **Billing** `MsSqlHealthCheck` only: `Pooling = false,` deleted | `HealthProbeCopyParityTests` (4) | 1 of 4 failed: `src/Billing/Infrastructure/Health/MsSqlHealthCheck.cs diverges from the canonical src/Orders/Infrastructure/Health/MsSqlHealthCheck.cs outside the namespace and own-service using line.` — names Billing | `cmp` identical; rebuilt; 4/4 green |
| 3 | Fulfillment `HealthProbeService`: an `Interlocked` in-flight counter on `/health/ready`; `/health/live` awaits 2 s **only while it is > 0** | `Fulfillment.IntegrationTests.HealthProbesTests.R60_OR6_…` | **[4 s]** `/health/live did not answer 200 within 500ms while a /health/ready call was in flight (elapsed 2005ms, status OK).` — the in-flight liveness assertion | `cmp` identical; rebuilt; 2/2 green (with 2a) |

**Probe 1 settles hand-off item 2's regression**: 10 s and a bound-naming message, not `HttpClient`'s 100 s default.

### Mine — one per missing family, all survived

| # | Family | Mutation | Whole-project result | What it proves |
|---|---|---|---|---|
| P5 | deletion of a host setting | `src/Notifications/NotificationsHost.cs:39` `o.IncludeScopes = true;` → `false` | Notifications unit **77/77**, integration **14/14** — green | D2 |
| P6 | corruption of trace continuity | `src/Fulfillment/Presentation/StockRpcResponder.cs:162` — `StartActivity(…, parentContext: parent)` → `StartActivity(…)` (a fresh root; the header is still extracted) | Fulfillment unit **130/130**, integration **61/61**, `ResponderTraceSubjectCoverageTests` **1/1** — green | D1 |
| P7 | corruption of a wire field | `src/Projector/Infrastructure/Messaging/DeadLetter/KafkaDeadLetterPublisher.cs:46` `x-first-failed-at` → the literal `"corrupted-by-review-probe"` | Projector unit **115/115**, integration **57/57** — green | D3 |

All three restored `cmp`-identical, `touch`ed, force-rebuilt (7 projects), confirming unit runs 77/130/115 green.

## Blocking defects

### D1 — `R57`/`OR4`'s receive side on the RPC transport is guarded by a test that re-implements it

- **Where.** `tests/Orders.IntegrationTests/TraceContextPropagationTests.cs:54-66`, the NATS case cited by `R56`'s, `R57`'s and `OR4`'s traceability. It starts a **stand-in responder** and extracts inside the test, and its own comment says so: *"The SAME mechanism the real responders use … proven here at the wire level since the production responder classes' extraction methods are `private`/service-internal."* That is the feature-19 shape `CLAUDE.md` names: the guard re-implements the conversion instead of reading through it.
- **Enumeration, as a search result.** `grep -n 'ExtractNats('` over `src/` (bin/obj pruned by path) returns six hits:
  - three are the `TraceContext` definitions;
  - three are the production call sites: `OrdersCreateResponder.cs:99`, `StockRpcResponder.cs:160`, `BillingRpcResponder.cs:162`.
- **The same search over `tests/`** returns four hits:
  - `TraceContextCarrierTests.cs:128/137/143`, which test the carrier alone;
  - `TraceContextPropagationTests.cs:60`, the stand-in.
- **The files that construct those responders or their hosts.** 56 test files do so. Every one has zero `traceparent|TraceId|ActivityListener|Exported` matches, except `LogCorrelationTests.cs`, which drives a Kafka fact, not an RPC.
- **No test executes any production responder's extraction.** `ResponderTraceSubjectCoverageTests` reads `SubscribeAsync`/`SubscribeLoopAsync` tokens only (`:58-70`); it never reads `StartActivity`.
- **Armed (P6):** dropping the parent at the production site leaves every suite green.
- **Why it matters.**
  - The `R57` Status cell says `DONE` for *"continued on consume, on both transports"*. The `R56` cell says these tests *"prove every HOP (NATS, …) continues a trace correctly"*. N1's walk says *"Decorative guards found: 0 … not a re-implementation"*, and L24's row names *"every OR4 case"*. For the NATS leg all four statements are false, and the ledger's guard half is hollow in the exact way two phase-13 rejections were.
  - **#7's guard executed production code.** `apps/orders/src/presentation/orders-create.controller.spec.ts:134` constructs the real `OrdersCreateController` with injected headers and asserts the handler observes the caller's trace id. `design.md` §11 row 35 classifies it *"Ported, and generalised to the three responder classes"*; what was delivered generalises a stand-in.
  - #7 later added per-service behavioural guards for the Fulfillment and Billing hops. `apps/fulfillment/src/trace-context-propagation.integration.spec.ts:68,:102` and `apps/billing/src/trace-context-propagation.integration.spec.ts:79,:114` assert that an inbound `traceparent` becomes `outbox.trace_parent`, and that no `traceparent` produces none.
- **What must change.**
  - Guard the extraction at **each of the three production responders** through the production class: a real host or real responder, a real NATS request carrying a caller span's `traceparent`, and an exported server span whose trace id equals the caller's and whose parent span id equals the caller's span id.
  - The Fulfillment and Billing cases should also assert #7's `outbox.trace_parent` outcome, both with and without an inbound header.
  - Arm each responder by P6's mutation.
  - Correct the `R56`/`R57` Status-cell wording and ledger row L24's guard citation to name what now executes production code.

### D2 — `OR7`/`R58`: three host settings in six hosts, guarded in two

- **Where.** All six hosts set `AddJsonConsole` + `IncludeScopes = true` + `ActivityTrackingOptions`: `OrdersHost.cs:53-58`, `FulfillmentHost.cs:37-42`, `BillingHost.cs:37-42`, `NotificationsHost.cs:37-42`, `ProjectorHost.cs:37-42`, `GatewayHost.cs:42-47`.
- **Enumeration.** `grep -nE 'IncludeScopes|AddJsonConsole|ActivityTrackingOptions|JsonConsole'` over `tests/` returns four lines, all doc comments. They are in `Orders.IntegrationTests/LogCorrelationTests.cs:17,21`, `Orders.IntegrationTests/CapturedConsole.cs:10` and `Gateway.IntegrationTests/CapturedConsole.cs:10`. So only the Orders and Gateway hosts have a test that reads real emitted JSON.
- **Armed (P5):** turning `IncludeScopes` off in Notifications leaves both Notifications projects green. Every correlation scope that host pushes (`NotificationFactsConsumer`'s `correlationId`) silently vanishes from its lines.
- **Why it matters.**
  - Ledger row L25 says of exactly this shape *"Any one missing removes the field from every line, with no failing assertion anywhere unless the guard reads real output"*, and `design.md` §10.4 lists L25 among the three rows to attack first.
  - `design.md` §11 rows 50–58 promise *"one log-capture case per service replaces the per-site files"*; two services have one.
  - The acceptance bullet is *"correlationId in every log line"*.
  - #7's final state guards these sites individually (`8a3a3d3`: 65 assertion lines across `notifications/*-log-trace-id.spec.ts`, `projector/*-log-trace-id.spec.ts` and `gateway/…/nats-stream-signal-log-trace-id.spec.ts`; listed below).
- **What must change.**
  - A log-capture case reading real emitted JSON from each of the four unguarded hosts (Fulfillment, Billing, Notifications, Projector), each asserting `correlationId` and `TraceId` on the records of one unit of work.
  - If it is shared, it must enumerate hosts from a **literal** set, not by discovery that a missing host would drop out of.
  - Arm one host per setting.
  - The L25 row's guard citation and `R58`'s Status cell are updated to match.

### D3 — `design.md` §11's ported-guard table was never reconciled against delivery; three of its target classes do not exist

- **Enumeration.** Every `→ \`XxxTests\`` target in §11 checked with `find tests -name "$c.cs"`: 13 distinct names. **Three are missing** — `FactRetryOptionsTests` (rows 4–5), `KafkaDeadLetterPublisherTests` (rows 6–7) and `KafkaDlqDepthTests` (rows 67–69). Rows 79–93 claim #7's per-probe `health-checks.spec` cases are ported. There are no per-probe tests (`MsSqlHealthCheckTests`, `KafkaHealthCheckTests`, NATS/Mongo probe tests: none). Among them is #7's *"NATS probe down-when-closed-without-calling-rtt"*.
- **Did any group check §11?** `grep -n '§11' progress/impl_observability_reliability.md` → **no hits**. N1 walked §10; nobody walked §11.
- **What the gaps contain.**
  - Rows 4–5 were delivered for Orders only. `OrdersProgramConfigurationTests.cs:230-241` is the only test that sets `FACT_RETRY_*`, yet `NotificationsProgramConfiguration.cs:28,31` and `ProjectorProgramConfiguration.cs:30,33` read the same pair. So the substitution family is unguarded in two of three composition roots, by construction: no test sets either variable there.
  - Rows 6–7: the dead-letter publisher's trace injection is covered end to end by `SagaDeadLetterTests › R57_OR4_EveryRetryAttempt…`. Its *no active span → no `traceparent`* half has no test anywhere: `grep traceparent` over `tests/` shows absence asserted only at the carrier (`TraceContextCarrierTests.cs:118`) and at the relay (`TraceContextPropagationTests.cs:273`, `OutboxWireParityTests.cs:196`).
  - `OR1`'s header set is asserted individually only in Orders.
  - `x-first-failed-at` and `x-failed-at` appear in exactly one test file, `SagaDeadLetterTests.cs`.
  - The three `KafkaDeadLetterPublisher.cs` copies are outside every parity family (the implementation record says so at A1i).
  - **Armed (P7):** corrupting `x-first-failed-at` in Projector's copy leaves both Projector projects green. Tasks A1i(iv) says *"the header values are asserted individually"*; in Projector and Notifications five of eight are.
- **What must change.** Walk §11 row by row the way N1 walked §10: for each *"Ported"* row, name the delivered test that carries it, or reclassify it with a reason. Then close the gaps that walk exposes:
  - assert all eight `DeadLetterHeaders` in the Projector and Notifications dead-letter cases, armed by P7's corruption in one copy;
  - add the `FACT_RETRY_*` substitution case to the Notifications and Projector configuration tests;
  - add either the DLQ publisher's no-span case or a stated reason it is unreachable. The consumers always start a `consume` span, so it may be.

### D4 — `tasks.md` Group N is unticked

`specs/observability_reliability/tasks.md:103-108` — N1–N6 are all `[ ]`, while the implementation record and `feature_list.json` present them as done. C6 requires every task ticked on a done `sdd` feature, and standing rule 3 of the same file says *"Tick as you go"*. Tick them, and reword on the box any whose delivered shape differs.

## Required record corrections (in the same round)

- **R1 — the implementation record contradicts itself about round 3.**
  - `:2322` says *"No `.cs` file under `src/`/`tests/` carries a net change from this round's work … N2's own 1758 stands unchanged and is not re-derived here."*
  - `:2316` says six `HealthProbesTests.cs` files and `SagaFactsConsumerTests.cs` changed **permanently**.
  - A round-3 `quality.sh` run exists and does reconcile at 1758 (log cited above), but the record never cites it. Replace the sentence with that run.
  - Also, the table rows at `:2345` (L20) and `:2350` (L25) still cite *"A3 round 3"*, while the correction paragraph at `:2320` says L20's arm is A3b round 1 and L25's latest is coordinator round 2's `ActivityTrackingOptions` arm. Correct the rows, not only the paragraph.
- **R2 — two history-half errors in the ledger.** A wrong history half cannot fail any test, and #9 inherits it.
  - **L26, and `design.md` §8.3** say #7's Gateway NATS probe was *"found"* by #7's reviewer. `order-to-cash-nestjs/progress/review_observability_reliability.md:70-72` says the opposite: the reviewer recorded *"two minor, already-disclosed items"*, the first being that probe, *"Disclosed in `test-matrix.md`'s R60 row and in the implementer's own A8 section"*. The substance (one probe shipped unbounded) is right; the attribution is wrong. `requirements.md` `OR6` gets it right (*"shipped one probe without one and disclosed it"*).
  - **L25** says #7 *"hand-added the fields at each of eleven JSON call sites"*, with no citation. Enumerated with `git grep -nE '\.\.\.\((traceId|[a-zA-Z]+TraceId) \? \{ traceId' <rev> -- apps`, excluding spec files, it gives **8** sites at `95e883a` (problem-json filter, the Orders fact-retry dispatcher, outbox relay, saga dispatcher ×2, sweeper, first-park handler, saga-facts controller) and **21** at HEAD. Neither is eleven. Cite the files and state the revision.
- **R3 — the `R56`/`R57` Status cells** must stop claiming the NATS hop is proven per production hop until D1 lands (see D1).

## Hand-off items — rulings

1. **A2 ledger miss (the operator-cancel envelope).** Every citation was verified in #7's checkout:
   - `saga-command-store.port.ts:40` and `:51` declare `triggeringEventEnvelope: Envelope`, non-nullable;
   - `cancel-order.handler.ts:175` and `:234` build the envelope through `buildTriggeringEnvelope` at `:272`, with `eventType 'orders.cancel.requested'` and the note spread at `:283`;
   - `saga-first-park-dead-letter-handler.ts:44-46` publishes unconditionally;
   - `cancel-order.handler.spec.ts:178` and `:220` guard it.

   #8's side:
   - `CancelOrderCommandHandler.cs:167` and `:194` pass `null`;
   - `SagaFirstParkDeadLetterHandler.cs:96-106` skips the publish;
   - `CancelOrderCommandHandlerTests.cs:146-148` asserts the nulls;
   - `grep orders\.cancel\.requested` over `src/` and `tests/` returns nothing (`xargs` exit 123).

   `design.md:191-193` (§4.2) speaks only of `SagaFactsConsumer`/`SagaFactHandler` threading fact bytes. A grep of all three spec files for `RPC-triggered|operator.cancel|orders\.cancel|no triggering|CancelOrder` returns nothing (exit 1). The leader's claim is confirmed.

   **Ruling: a parity divergence, not a spec violation.** R29 (`specs/shared/requirements.md:227-233`) routes *"the triggering fact"*, and an RPC-triggered command has none; the timeline half is met. **The routing to id 71 is acceptable and does not block feature 27.** The fix shares its envelope with id 71's note, and id 71's acceptance carries it in full, verified in the backlog diff: the envelope on both branches with #7's file:line, the guard flip, a byte-equal `.dlq` integration test armed by restoring the `null`, the §4.2 misattribution corrected, and a ledger row. Its root cause is not `specs/shared/`, so no SA-n is owed.
2. **A4's two production fixes.** Real and now guarded — probes 2a, 2b and 2c all killed.
3. **Design §5.2's factual error** (`NatsStockAvailabilityChecker` built no headers). Correcting it in code without editing the gate-approved text is acceptable. Note the related consequence: that checker is the caller in the NATS test D1 concerns, and it is the only side of the hop that test does exercise in production form.
4. **Stale coverage summary.** Recounted independently, one line per `R1`–`R63`:
   - rows whose cell opens `DONE` are Green; R55's `TODO —` is Not-yet-green;
   - R1 `DOMAIN HALF DONE`, R24 `INTEGRATION HALF DONE`, R61 `DOMAIN UNIT HALF DONE` and R56 `MECHANISM leg DONE, composed-stack leg unproven` are Scoped;
   - R29 `RETRY-CLAUSE ROW DONE … DEAD-LETTER ROW DONE` is Green.

   Result: §1 `10|9|1|0`, §2 `8|8|0|0`, §3 `11|10|1|0`, §4 `8|7|1|0`, §5 `8|8|0|0`, §6 `5|5|0|0`, §7 `6|5|0|1`, §8 `6|5|1|0`, §8.1 `1|1|0|0`, **Total `63|58|4|1`**. This matches `test-matrix.md:72-81` row for row. **Reconciles.**
5. **A2's 3 s settle window** (`SagaCommandDeadLetterTests.cs:179`, inside a 30 s deadline at `:141`). **Accepted as a stated bound.** The claim `UPDATE` commits in the same call stack as the park, and A2's own widened-gap proof (a controlled 1 s injected delay; the guard still failed 2/2 on broken code) shows the guard is not timing-luck. Not blocking.
6. **Comment drift inside the banner-exempt families.** **Accepted by design:** `IsBannerLine` = every leading `//` line, at `FactRetryDispatcherParityTests.cs:174` and `IdempotentConsumerParityTests.cs:259`, per §3.2's cross-reference. Advisory A5 notes a sibling family with no parity guard at all.
7. **`KafkaHealthCheckLongRunningTests`' `IsCompleted` race.** **Accepted.** `192.0.2.1` (TEST-NET-1) is black-holed, so a failure inside the few milliseconds between return and the read needs an immediate socket error that address does not produce. In probe 2b the elapsed-time assertion (`:48`) fired first at 2003 ms, so the `IsCompleted` half was not needed to kill the synchronous shape. Not blocking.
8. **R56's composed-stack leg.** The cell (`test-matrix.md`) names `saga_e2e_verification` (feature 28) and the ratification at `progress/spec_observability_reliability.md` row 4, gate 2026-09-10. Id 28's acceptance carries the R56 bullet. **A ratified Scoped row; confirmed.** (D1 is about the mechanism leg, not this one.)
9. **The flawed `terminus` analogy.** The decision stands for the reason given: the port produces `openapi.yaml`'s exact `HealthResponse`. Accepted.
10. **Projector checks NATS where #7 did not.** An addition justified by PR45, not a dropped guard. Accepted.
11. **Health port names.** The numbers 3002–3006 match #7; environment variable names are not a parity contract (only the Gateway is reached by n8n and the API tests). Accepted.
12. **Package set, counted by element** (`git diff -U0 -- '*.csproj' Directory.Packages.props`; no untracked `.csproj`; lines anchored at `^[+-]\s*<Element `, and a second pass found no element sharing a line with other text):
    - `PackageVersion` **+3 / −0**: `OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Instrumentation.AspNetCore`, all `1.18.0`;
    - `PackageReference` **+19 / −18**;
    - `FrameworkReference` **+5 / −0**, all `Microsoft.AspNetCore.App`.

    **Matches the hand-off exactly.**

**The L10 residual (brief item 8, fourth bullet).** **Accepted.** `SagaFactsConsumerTests.cs:220-227` also asserts a single publication, `Attempts == 1` and `poisonDispatcher.Invocations == 1`. A hung dispatcher and a bypass both fail the named claim, so the imprecise message costs a diagnosis, not a detection.

## Advisories — non-blocking, each with a destination

- **A1 — the §4.2 misattributed comments** at `CancelOrderCommandHandler.cs:167/:194` and the test's `:144-145` stay until id 71 lands. They are in id 71's acceptance, so nothing further is owed here.
- **A2 — two health-probe copy families have no parity guard.** The five `NatsHealthCheck.cs` copies are code-identical: a diff with `//` lines, `namespace` and own-service `using` stripped gives 0 differing lines for Billing, Fulfillment, Gateway and Projector against Orders, but their comments differ. The two `MongoHealthCheck.cs` copies also differ.
  - Pause coverage: NATS is paused in Orders, Billing and Gateway; MS-SQL in Fulfillment; Kafka in Notifications; MongoDB in Projector only.
  - So Fulfillment's and Projector's NATS probes, and the Gateway's Mongo probe, are guarded for stall behaviour only by `HealthProbeTimeoutTests`' naming check.
  - **Leader: add a bullet to id 68**, or fold it into the D-round if cheap.
- **A3 — `FACT_RETRY_*` in the Notifications and Projector roots** is D3's required fix. If the fix round is scoped narrowly, **the leader files it on id 68** (`composition_root_delegation_and_wiring_are_unguarded`) rather than letting it ride in prose.
- **A4 — no finding in this review has its root cause in `specs/shared/`.** Checked:
  - R29's text is met (item 1);
  - `asyncapi.yaml`'s `DeadLetterHeaders` already declares every header `OR1` writes;
  - OR1–OR7 and RI1–RI5 are local.

  No SA-n is proposed. **R1, R24, R55, R61 and the missing standing paragraph under the summary are backlog id 72's**, which exists and is pending. Noted, not blocking.
- **A5 — the banner exemption** (item 6) is a known, spec-endorsed gap. It is recorded here so a future parity family copies `HealthProbeCopyParityTests`' no-exemption shape, not the banner shape.
- **A6 — `init.sh` §5d parity** with #7 `[OK]`. `git status --porcelain --untracked-files=all -- specs/shared infra n8n src/SharedKernel src/Cqrs src/Seed apps/web` prints exactly ` M specs/shared/test-matrix.md`.
- **A7 — #7's degrading notification sender** (`ad90de6`: DLQ after a permanent send failure, plus its `8a3a3d3` log spec) has no #8 counterpart: `grep -ln Degrad` over `src/` returns nothing. It is outside feature 27's mechanisms and not a finding against it. **Leader: confirm whether `notifications_service`'s port owes it**, and file an entry if so.

## `R<n>` → test mapping verified

Each name below was checked as a literal `(void|Task) <name>(` in `tests/` (bin/obj pruned by path). All exist. The mapping's weaknesses are D1–D3.

| Req | Named tests (file) | Verdict |
|---|---|---|
| **R16** | `FactRetryDispatcherTests` › `OR1_RetriesToTheConfiguredMaximumWithExponentialBackoff_ThenPublishesToTheDlqTopicAndReturnsNormally`, `OR1_RetriesThenSucceeds_WithoutEverPublishingToTheDlq`, `OR1_SucceedsOnTheFirstAttempt_WithNoDelayAndNoDlqPublish`, `OR1_ACancelledTokenIsRethrownImmediately_NeitherRetriedNorDeadLettered`, `OR1_XAttemptsIsWrittenFromTheLoopsOwnCounter_NotFromMaxAttemptsReadAtTheEnd`; `SagaFactsConsumerTests` › `OR1_TheDispatchGenuinelyGoesThroughTheInjectedRetryDispatcher_NotAroundIt`, `OR1_TheEnvelopeGuardUnroutedEventTypeAndSO2BranchesAreNotWrapped_TheDispatcherIsNeverInvoked`; `ProjectorFactsConsumerTests` › `OR1_TheUnknownFactTypeIgnoreIsSwallowedInsideProcess_NeverReachingTheRetryPath`; `OR1_R16_APoisonFactIsRetriedThenDeadLetteredWithItsDiagnosticHeaders_…` ×3 (`SagaDeadLetterTests`, `ProjectorDeadLetterTests`, `NotificationDeadLetterTests`) | exercised; header coverage uneven — D3 |
| **R29** DLQ clause | `SagaFirstParkDeadLetterTests` › `OR3_ClaimsTheFirstParkExactlyOnce_AndDoesNothingOnASecondPark`; `SagaCommandDeadLetterTests` › `R29_OR3_OnFirstParkAppendsExactlyOneOrderSagaFailedFact…WhileSO5sRetryScheduleIsUnchanged`; retry clause `SagaCommandRetryTests` › `R29_SO4_SO5_WithNoResponder_ParksAfterExhaustedAttemptsLeavingTheOrderStatusUnchanged` | exercised for fact-triggered rows; the RPC-triggered row goes to id 71 |
| **R56** mechanism | `TraceContextCarrierTests`; `TraceContextPropagationTests` › `R57_OR4_NatsRpc_…`, `R57_OR4_KafkaFacts_…`, `R56_OR4_TheWriteDatabaseHop_SitsBetweenTheRpcSpanAndThePublishSpan_ByParentSpanId` | Kafka and write-db hops exercised; **NATS receive hop re-implemented — D1** |
| **R57** | the above, plus `R57_OR4_AWriteWithNoActiveSpanProducesNoTraceparentHeaderAtAll` and `SagaDeadLetterTests` › `R57_OR4_EveryRetryAttemptAndTheEventualDlqPublishObserveTheSameTraceIdAsTheInboundFact` | publish side exercised; **consume side on NATS — D1** |
| **R58** | `LogCorrelationTests` › `R58_OR7_EveryRecordProducedWhileHandlingAFactCarriesTheSameCorrelationIdAndTheSameTraceId`, `R58_OR7_OmitsTheTraceFieldsEntirelyRatherThanRenderingThemEmpty_WhenNoSpanIsActive`; `ProblemJsonCorrelationTests` › `R58_OR7_TheProblemBodyAndItsOwnLogLineCarryTheSameCorrelationIdAsTheRequestThatFailed` | Orders and Gateway only — **D2** |
| **R59** | `MetricsExposureTests` › `OtcOutboxLagMs_TracksAGenuinelyAgedRealRow_ThenDropsTo0AfterTheRelayDrains`, `OtcDlqDepth_TheRealKafkaBackedGauge_SumsHighMinusLow…`, `OtcDlqDepth_ANonExistentTopic_Reports0AndNeverThrows`; `FactRetryDispatcherTests` › `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer`, `…_RecordedOnTheExhaustedRetryDlqPathToo`; `SagaFactHandlerTests` › `OtcSagaCompletionMs_ARealCompletingTransition_RecordsExactlyOneCompletionTaggedCompleted`; `RequestLatencyMiddlewareTests` › `RecordsOnSuccess_TaggedByTheRequestPath`, `RecordsOnTheErrorPathToo_BeforeRethrowing` | exercised (not re-armed here; A3 round 3's relay-wiring guard is recorded) |
| **R60** | `HealthProbesTests` ×6 › `R60_OR6_ReportsReadyWhileEveryDependencyIsReachable_…`; `HealthProbeTimeoutTests` › `OR6_EveryReadinessProbeInEveryServiceIsBoundedByAnExplicitTimeout`; `MsSqlHealthCheckPoolingTests`; `KafkaHealthCheckLongRunningTests` ×3; `HealthProbeCopyParityTests` ×4; `HealthCheckAggregationTests` ×6 | **exercised and armed by this review** (probes 1, 2a–c, 3) |
| **R62** | `PlaceOrderRequestIdReplayTests` › `RI2_…`, `RI3_ADuplicateKeyOnTheRequestIdIndexResolvesToTheWinnersReReadReply`, `RI3_ADuplicateKeyOnTheOrderReferenceIndexPropagatesUnchanged`, `RI4_OmittingRequestIdPlacesANormalOrder_…`, `RI5_…`; `OrdersCreateIdempotentReplayTests` › `RI1_…`, `RI3_TwoConcurrentFirstTimeOrdersCreateRequests…`, `RI4_TwoOrdersPlacedWithNoRequestIdBothCommit_…` | exercised (Group B's arms recorded; not re-armed here) |

## Ledger history-half sample — 8 rows opened in #7's checkout

| Row | Claim | #7 file:line read | Holds? |
|---|---|---|---|
| L1 | MySQL `UNIQUE` on nullable admits many NULLs; plain `UNIQUE(request_id)` | `apps/orders/src/infrastructure/persistence/schema/orders.schema.ts:33-36` (comment verbatim); `apps/orders/drizzle/0005_sticky_goblin_queen.sql:5` | **yes** |
| L2 | #7's outbox row written after the failing insert; catch inside the tx | `apps/orders/src/infrastructure/persistence/order.repository.ts:127` insert orders, `:168` `outboxRecorder.record`; `place-order.handler.ts:159-163` catch and re-read inside tx | **yes** |
| L3 | `ER_DUP_ENTRY` + substring on `sqlMessage` | `apps/orders/src/application/place-order-request-id.ts:13-14,35-39` | **yes** |
| L8 | `UniqueId.from(requestId)` with try/catch fallback | `place-order.handler.ts:143` → `:205-213` | **yes** |
| L12 | offset commits because the handler returned normally | `apps/orders/src/main.ts:95-104` (`ServerKafka.handleEvent` awaits with no try/catch; a rejection leaves kafkajs uncommitted) — row carries no citation | **yes** (uncited) |
| L27 | dedicated `retries: 0` admin client | `apps/orders/src/infrastructure/health/kafka-health-check.ts:16-24` | **yes** |
| L26 | #7's reviewer *found* the unbounded probe | `progress/review_observability_reliability.md:70-72` — already disclosed by the implementer | **no — R2** |
| L25 | eleven JSON call sites | 8 at `95e883a`, 21 at HEAD, by the command in R2 | **no — R2** |

## `CHECKPOINTS.md`

**C1 — harness.**
- [x] Harness files exist; `./init.sh` exit 0.
- [x] `progress/current.md` and `history.md` exist.
- [x] Agents dir and model declarations unchanged by this feature: `git status` shows no `.claude/` change; not re-read.

**C2 — state.**
- [x] At most one `in_progress` (none; id 27 is `in_review`).
- [x] Statuses valid (init backlog check `[OK]`).
- [x] `current.md` describes the active session.
- [x] No `blocked` features touched.

**C3 — architecture.**
- [x] NetArchTest `Architecture.Tests` **21/21**, run whole.
- [x] No new `ProjectReference` in any `src/*/*.csproj` diff (grep exit 1), so no cross-service DB access added.
- [x] No change under `src/SharedKernel` or `src/Cqrs`, so `SharedKernel` still has zero `PackageReference`s.
- [x] No `decimal` in domain arithmetic. `Order.RecordSagaFailure` performs no arithmetic at all. The only Domain-folder hit is the doc comment `src/Projector/Domain/MoneyFormat.cs:10`, which predates this feature.
- [x] Interactions classified: `.dlq` republication is a Kafka publish of a fact copy per `asyncapi.yaml`; health is HTTP, not inter-service; trace headers ride existing Kafka/NATS messages.
- [x] No stray debug logging or TODO: `grep -nE 'Console\.(Write|Error)|Debug\.Write|\bTODO\b|\bFIXME\b|\bHACK\b'` over the 164 changed or new `src/*.cs` files → no hits (exit 123).

**C4 — verification.**
- [x] `./quality.sh` green at 1758 (round-3 log verified, not re-run).
- [x] Domain tests pure (NetArchTest).
- [x] Integration tests use real containers throughout; all probes above ran against real MS-SQL, Kafka, NATS and Mongo.
- [ ] Coverage thresholds *enforced* — the log carries 18 coverage `[INFO]` lines, but the enforcing gate is feature 34, not yet landed. Pre-existing, not this feature's.
- [x] No Jest.

**C5 — clean close.**
- [x] No suspicious untracked files: probe backups live only in the session scratchpad; the marker grep is clean.
- [ ] `history.md` effort entry — not written; not closeable while rejected.
- [x] `feature_list.json` reflects state (`in_review`, per the brief).
- [ ] The human told what was done — the leader's step after approval.
- [x] Claude did not commit: HEAD is still `909394f`.

**C6 — SDD.**
- [x] All three spec documents exist.
- [x] EARS with ids (OR1–OR7, RI1–RI5).
- [ ] **All tasks ticked — D4.**
- [x] Every `R<n>` has a named, existing test (with the D1–D3 weaknesses).
- [ ] Spec commit precedes implementation commit — nothing committed yet; the human's commit sequence.

**C7 — reuse fidelity.**
- [x] `specs/shared/` byte-identical except `test-matrix.md` (§5d `[OK]`, `git status` above).
- [x] No new amendment, none owed (A4).
- [ ] **Reused ids genuinely satisfied** — `R57` (and `R58` in four services) claim behaviour whose guard does not execute it (D1/D2).
- [x] `n8n/` unchanged.
- [ ] Black-box API script — n/a to this feature.
- [ ] Effort records — pending approval.
- [ ] README benchmark — n/a to this review.

## What must change before re-review

1. **D1** — the RPC receive-side continuation guarded through each of the three production responders; for Fulfillment and Billing, #7's `outbox.trace_parent` with/without assertions; each responder armed by P6's mutation; the L24 guard citation and the `R56`/`R57` cells corrected.
2. **D2** — a real-emitted-JSON log-capture case for Fulfillment, Billing, Notifications and Projector, from a literal host set; armed one host per setting; the L25 guard citation and the `R58` cell corrected.
3. **D3** — a §11 walk in the implementation record, one line per row naming the delivered test or reclassifying it. Plus:
   - all eight `DeadLetterHeaders` asserted in the Projector and Notifications dead-letter cases, armed by P7;
   - the `FACT_RETRY_*` substitution case in the Notifications and Projector configuration tests;
   - the DLQ publisher's no-span case, or its unreachability stated with the consumer-span evidence.
4. **D4** — N1–N6 ticked, and reworded on the box where the delivered shape differs.
5. **R1–R3** — the record's round-3 sentence and table rows, the L25/L26 history halves (and `design.md` §8.3's sentence), and the Status-cell wording.
6. **Reconcile the new total against 1758** by project. Re-arm any existing guard whose test file the round touches, arm-granular, as Group N round 2 established.

**Re-review scope, stated now so it is cheap.**
- **Re-run:** P5, P6 and P7 against the fix, plus one arm per new guard.
- **Read:** the §11 walk.
- **Not repeated:** the five probes above, which do not need repeating unless their files change.

## Effort to date (for the eventual `history.md` entry — not an entry)

- **Clock.** Subagent transcript first/last timestamps, UTC converted to CEST; spans overlap, so they are not additive. The feature is bracketed by `909394f` at 06:04:29.
- **Spec and gate.**
  - spec 06:15 → 06:39;
  - gate approved the same morning.
- **Implementation.**
  - Group B 07:10 → 09:44, with its arming-gap closure 08:22 → 08:34 and one `suite_runner` 08:01 → 08:11;
  - A1 08:35 → 11:01;
  - A2 11:02 → 11:33, interrupted by the VS Code restart, then resumed 11:41 → 13:02;
  - A3, three rounds, 13:07 → 16:23;
  - A4 16:23 → 18:36;
  - A4e fix, two rounds, 18:43 → 20:49;
  - Group N, three rounds, 20:52 → 22:04.
- **Review.** This review started 22:05 CEST.
- **Totals.** 1 spec session, 1 gate, 9 implementer transcripts carrying **14 implementer passes**, 1 suite runner, and this review round (rejected). About 16 h 40 min elapsed at this review's close. #7's counterpart: 1 spec + 1 gate + 6 implementer passes + 1 review, approved first time (`order-to-cash-nestjs/progress/history.md:1057`).

## Appendix — #7's assertions for this feature's mechanisms, enumerated by content

Commands, run in `order-to-cash-nestjs`. Specs are found by path with `node_modules`, `dist` and `.git` **pruned by path** (320 spec files). The unit is the **assertion line**, filtered by content, never by filename. Each hit is then blamed to its commit.

```
RX='dlq|DLQ|deadLetter|DeadLetter|dead_letter|dead-letter|triggeringEvent|traceparent|traceId|spanId|trace_parent|otc_|[Hh]ealth|requestId|[Ff]irstPark|x-failed|x-attempts|x-error|saga_failed|sagaFailed|SagaFailed|[Rr]etry|withTimeout|rtt\(|correlationId'
find . \( -path '*/node_modules' -o -path '*/dist' -o -path './.git' \) -prune -o -name '*.spec.ts' -print | sort      # 320 files
while read f; do grep -nE 'expect\(|assert\.|toThrow|rejects' "$f" | grep -E "$RX" | …; done                         # 322 assertion lines
git blame -l -L $ln,$ln --porcelain -- "$f" | head -1                                                                  # commit per line
```

By commit:
- `8635b66` 99 and `95e883a` 50 — #7 feature 27, both case-classified by `design.md` §11;
- `8a3a3d3` 71;
- `85b7fd8` 16;
- `4f52c8e` 11;
- `8a35d4e` 8, `64a2a77` 8, `32da6e9` 8;
- `8baa22e` 7, `50ae502` 7;
- `a4c14d7` 6, `423643e` 6;
- `c3f8e85` 5;
- `89b41f3` 4;
- `fd445bc` 3, `bf59af9` 3, `ad90de6` 3, `035d8e5` 3;
- `d71baa7` 2, `1eea58f` 2.

Classification totals:

| Count | Classification |
|---:|---|
| 149 | added by #7 feature 27 — case-classified in §11; delivery unreconciled (D3) |
| 65 | not ported — per-site log trace/correlation (D2) |
| 57 | n/a — pre-feature-27 assertions matched on a shared term |
| 16 | not ported — Billing/Fulfillment relay trace linkage (relay half via parity; write half D1) |
| 6 | n/a — R63 rate limit |
| 6 | n/a to feature 27 — #7 degrading sender log spec (A7) |
| 4 | n/a — `billing.credit.release` correlation (feature 41) |
| 4 | n/a — feature 28 composed stack |
| **4** | **dropped and inverted — operator-cancel envelope (item 1, id 71)** |
| 3 | n/a — billing payment causation |
| 3 | n/a to feature 27 — degrading-sender DLQ (A7) |
| 3 | n/a — terminal rejection (id 42) |
| 2 | not ported — D1 |
| **0** | **unclassified** |

Complete output, one classification per hit:

```
bf59af9 billing/src/application/payment-register.handler.spec.ts:277:expect(releaseCausationId).not.toEqual(cmd.requestId);
    -> N/A — billing payment causation edge (earlier feature)
32da6e9 billing/src/credit-release.integration.spec.ts:112:expect(secondOutboxRows).toHaveLength(0); // no fact under the second call's own correlationId
    -> N/A — billing.credit.release responder correlation (#8 feature 41, done)
32da6e9 billing/src/credit-release.integration.spec.ts:162:expect(await harness.outboxRowsFor(correlationId.value)).toHaveLength(0);
    -> N/A — billing.credit.release responder correlation (#8 feature 41, done)
c3f8e85 billing/src/domain/buyer-credit.spec.ts:35:expect(credit.evaluateHold({ orderReference: ORDER, amount: Money.of(10_001, CURRENCY), correlationId: UniqueId.generate() })).toMatchObject({
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8baa22e billing/src/domain/invoice-events.spec.ts:40:expect(event!.correlationId.equals(correlationId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
c3f8e85 billing/src/infrastructure/outbox/outbox-relay.integration.spec.ts:109:expect(rowBeforePublish?.correlationId).toBe(correlationId.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
c3f8e85 billing/src/infrastructure/outbox/outbox-relay.integration.spec.ts:130:expect(envelope.correlationId).toBe(correlationId.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
85b7fd8 billing/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:152:expect(traceparentHeader).toBeDefined();
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 billing/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:157:expect(extractedSpanContext!.traceId).toBe(originTraceId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 billing/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:161:expect(extractedSpanContext!.spanId).not.toBe(writerSpanId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 billing/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:162:expect(extractedSpanContext!.spanId).toMatch(/^[0-9a-f]{16}$/);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 billing/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:169:expect(publishSpan!.spanContext().traceId).toBe(originTraceId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 billing/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:170:expect(publishSpan!.parentSpanContext?.spanId).toBe(writerSpanId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 billing/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:171:expect(publishSpan!.spanContext().spanId).toBe(extractedSpanContext!.spanId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 billing/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:233:expect(receivedHeaders.traceparent).toBeUndefined();
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
8baa22e billing/src/invoice-issue.integration.spec.ts:109:expect(outboxRows[0]).toMatchObject({ eventType: 'invoice.issued.v1', correlationId: correlationId.value, causationId: requestId.value });
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8baa22e billing/src/invoice-issue.integration.spec.ts:143:expect(await harness.outboxRowsFor(correlationId.value)).toHaveLength(0);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8baa22e billing/src/invoice-issue.integration.spec.ts:169:expect(await harness.outboxRowsFor(correlationId.value)).toHaveLength(0);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8baa22e billing/src/invoice-issue.integration.spec.ts:196:expect(await harness.outboxRowsFor(correlationId.value)).toHaveLength(0);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
50ae502 billing/src/payment-register.integration.spec.ts:135:expect(outboxRows[0]).toMatchObject({ correlationId: correlationId.value, causationId: requestId.value });
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
bf59af9 billing/src/payment-register.integration.spec.ts:145:expect(outboxRows[1]!.causationId).not.toBe(requestId.value);
    -> N/A — billing payment causation edge (earlier feature)
bf59af9 billing/src/payment-register.integration.spec.ts:146:expect(outboxRows[1]).toMatchObject({ correlationId: correlationId.value });
    -> N/A — billing payment causation edge (earlier feature)
50ae502 billing/src/payment-register.integration.spec.ts:232:expect(await harness.outboxRowsFor(correlationId.value)).toHaveLength(0);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
50ae502 billing/src/payment-register.integration.spec.ts:251:expect(await harness.outboxRowsFor(correlationId.value)).toHaveLength(0);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
50ae502 billing/src/payment-register.integration.spec.ts:277:expect(await harness.outboxRowsFor(correlationId.value)).toHaveLength(0);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
50ae502 billing/src/payment-register.integration.spec.ts:290:expect(await harness.outboxRowsFor(correlationId.value)).toHaveLength(0);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
c3f8e85 billing/src/presentation/credit.controller.spec.ts:83:expect(dispatchedCommand.correlationId.equals(correlationId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
c3f8e85 billing/src/presentation/credit.controller.spec.ts:84:expect(dispatchedCommand.requestId.equals(requestId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
32da6e9 billing/src/presentation/credit.controller.spec.ts:174:expect(dispatchedCommand.correlationId.equals(correlationId)).toBe(true);
    -> N/A — billing.credit.release responder correlation (#8 feature 41, done)
32da6e9 billing/src/presentation/credit.controller.spec.ts:175:expect(dispatchedCommand.requestId.equals(requestId)).toBe(true);
    -> N/A — billing.credit.release responder correlation (#8 feature 41, done)
8baa22e billing/src/presentation/invoice.controller.spec.ts:103:expect(dispatchedCommand.correlationId.equals(correlationId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8baa22e billing/src/presentation/invoice.controller.spec.ts:104:expect(dispatchedCommand.requestId.equals(requestId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
50ae502 billing/src/presentation/invoice.controller.spec.ts:261:expect(dispatchedCommand.correlationId.equals(correlationId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
50ae502 billing/src/presentation/invoice.controller.spec.ts:262:expect(dispatchedCommand.requestId.equals(requestId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
a4c14d7 billing/src/trace-context-propagation.integration.spec.ts:110:expect(row!.traceParent).toBe(traceparent);
    -> NOT PORTED — D1: inbound traceparent on a PRODUCTION responder becomes outbox.trace_parent; none when absent
035d8e5 fulfillment/src/domain/despatch-advice.spec.ts:47:expect(event.correlationId.equals(correlationId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8a35d4e fulfillment/src/infrastructure/outbox/outbox-relay.integration.spec.ts:128:expect(rowBeforePublish?.correlationId).toBe(correlationId.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8a35d4e fulfillment/src/infrastructure/outbox/outbox-relay.integration.spec.ts:149:expect(envelope.correlationId).toBe(correlationId.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
85b7fd8 fulfillment/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:174:expect(traceparentHeader).toBeDefined();
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 fulfillment/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:179:expect(extractedSpanContext!.traceId).toBe(originTraceId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 fulfillment/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:183:expect(extractedSpanContext!.spanId).not.toBe(writerSpanId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 fulfillment/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:184:expect(extractedSpanContext!.spanId).toMatch(/^[0-9a-f]{16}$/);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 fulfillment/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:191:expect(publishSpan!.spanContext().traceId).toBe(originTraceId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 fulfillment/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:192:expect(publishSpan!.parentSpanContext?.spanId).toBe(writerSpanId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 fulfillment/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:193:expect(publishSpan!.spanContext().spanId).toBe(extractedSpanContext!.spanId);
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
85b7fd8 fulfillment/src/infrastructure/outbox/outbox-relay-trace-linkage.integration.spec.ts:261:expect(receivedHeaders.traceparent).toBeUndefined();
    -> NOT PORTED — Billing/Fulfillment relay trace linkage; relay/writer code parity-guarded to Orders (OutboxRelayParityTests) so the relay half transfers; the inbound-trace-at-write half is D1
035d8e5 fulfillment/src/presentation/despatch.controller.spec.ts:92:expect(dispatchedCommand.correlationId.equals(correlationId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
035d8e5 fulfillment/src/presentation/despatch.controller.spec.ts:93:expect(dispatchedCommand.requestId.equals(requestId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8a35d4e fulfillment/src/presentation/stock.controller.spec.ts:107:expect(dispatchedCommand.correlationId.equals(correlationId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8a35d4e fulfillment/src/presentation/stock.controller.spec.ts:108:expect(dispatchedCommand.requestId.equals(requestId)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
a4c14d7 fulfillment/src/trace-context-propagation.integration.spec.ts:98:expect(row!.traceParent).toBe(traceparent);
    -> NOT PORTED — D1: inbound traceparent on a PRODUCTION responder becomes outbox.trace_parent; none when absent
4f52c8e gateway/src/application/commands/cancel-order.command.spec.ts:18:expect(rpc.calls[0]?.meta.correlationId).toBe('order-1');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
4f52c8e gateway/src/application/commands/place-order.command.spec.ts:60:expect((rpc.calls[0]?.payload as { requestId?: string }).requestId).toBe(idempotencyKey);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
4f52c8e gateway/src/application/commands/place-order.command.spec.ts:96:expect(rpc.calls[0]?.meta.correlationId).not.toBe('order-1');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
4f52c8e gateway/src/application/commands/register-payment.command.spec.ts:90:expect(result.correlationId).toBe('order-1');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
4f52c8e gateway/src/application/commands/register-payment.command.spec.ts:92:expect(paymentCall?.meta.correlationId).toBe('order-1');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
4f52c8e gateway/src/application/commands/register-payment.command.spec.ts:147:expect(result.correlationId).toBe('order-1');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
423643e gateway/src/auth-rate-limit.integration.spec.ts:109:expect(typeof limited!.body.correlationId).toBe('string');
    -> N/A — R63 login rate limit (phase 13)
423643e gateway/src/auth-rate-limit.integration.spec.ts:110:expect(limited!.body.correlationId.length).toBeGreaterThan(0);
    -> N/A — R63 login rate limit (phase 13)
423643e gateway/src/auth-rate-limit.integration.spec.ts:123:expect(retryAfterHeader).toBeDefined();
    -> N/A — R63 login rate limit (phase 13)
423643e gateway/src/auth-rate-limit.integration.spec.ts:124:expect(retryAfterHeader).toMatch(/^d+$/);
    -> N/A — R63 login rate limit (phase 13)
423643e gateway/src/auth-rate-limit.integration.spec.ts:126:expect(Number.isInteger(retryAfterSeconds)).toBe(true);
    -> N/A — R63 login rate limit (phase 13)
423643e gateway/src/auth-rate-limit.integration.spec.ts:127:expect(retryAfterSeconds).toBeGreaterThanOrEqual(0);
    -> N/A — R63 login rate limit (phase 13)
4f52c8e gateway/src/domain/auth/operator-credentials.spec.ts:13:expect(matchesOperator({ username: 'operator', password: 'otc_operator_dev_password' }, configured)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
4f52c8e gateway/src/domain/auth/operator-credentials.spec.ts:21:expect(matchesOperator({ username: 'someone-else', password: 'otc_operator_dev_password' }, configured)).toBe(false);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
4f52c8e gateway/src/domain/auth/operator-credentials.spec.ts:25:expect(matchesOperator({ username: 'Operator', password: 'otc_operator_dev_password' }, configured)).toBe(false);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8635b66 gateway/src/infrastructure/messaging/nats-rpc-client.adapter.spec.ts:144:expect(extractedSpanContext!.traceId).toBe(traceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 gateway/src/infrastructure/messaging/nats-rpc-client.adapter.spec.ts:145:expect(extractedSpanContext!.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8a3a3d3 gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:78:expect(logged.correlationId).toBe('order-42');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:91:expect(rawLine).not.toContain('"correlationId"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:110:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:112:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:141:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:142:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8635b66 gateway/src/infrastructure/observability/http-instrumentation.spec.ts:113:expect(serverSpan!.spanContext().traceId).toBe(clientSpan!.spanContext().traceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 gateway/src/infrastructure/observability/http-instrumentation.spec.ts:114:expect(serverSpan!.parentSpanContext?.spanId).toBe(clientSpan!.spanContext().spanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 gateway/src/infrastructure/observability/http-instrumentation.spec.ts:115:expect(serverSpan!.spanContext().traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 gateway/src/infrastructure/observability/http-instrumentation.spec.ts:116:expect(serverSpan!.spanContext().traceId).not.toBe('0'.repeat(32));
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
4f52c8e gateway/src/orders.integration.spec.ts:109:expect(getResponse.headers['retry-after']).toBeDefined();
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
4f52c8e gateway/src/presentation/problem-json.filter.spec.ts:42:expect(body.correlationId).toBeTruthy();
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8635b66 gateway/src/presentation/problem-json.filter.spec.ts:159:expect(body.correlationId).toBe('11111111-1111-4111-8111-111111111111');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 gateway/src/presentation/problem-json.filter.spec.ts:162:expect(loggedLine.correlationId).toBe('11111111-1111-4111-8111-111111111111');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 gateway/src/presentation/problem-json.filter.spec.ts:163:expect(loggedLine.correlationId).toBe(body.correlationId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a gateway/src/presentation/problem-json.filter.spec.ts:197:expect(logged.correlationId).toBe('22222222-2222-4222-8222-222222222222');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a gateway/src/presentation/problem-json.filter.spec.ts:198:expect(logged.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a gateway/src/presentation/problem-json.filter.spec.ts:199:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a gateway/src/presentation/problem-json.filter.spec.ts:219:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
a4c14d7 gateway/src/saga-e2e-verification.integration.spec.ts:859:expect(dlq.headers['x-failed-consumer']).toBe('orders.saga');
    -> N/A — feature 28 composed-stack suite
a4c14d7 gateway/src/saga-e2e-verification.integration.spec.ts:860:expect(dlq.headers['x-original-topic']).toBe(ORDERS_FACTS_TOPIC);
    -> N/A — feature 28 composed-stack suite
a4c14d7 gateway/src/saga-e2e-verification.integration.spec.ts:861:expect(dlq.headers['x-error']).toBeTruthy();
    -> N/A — feature 28 composed-stack suite
a4c14d7 gateway/src/saga-e2e-verification.integration.spec.ts:862:expect(dlq.value.correlationId).toBe('not-a-uuid-correlation-id');
    -> N/A — feature 28 composed-stack suite
8a3a3d3 notifications/src/application/notification-dispatch-service-log-trace-id.spec.ts:82:expect(logged.correlationId).toBe(ENVELOPE.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/application/notification-dispatch-service-log-trace-id.spec.ts:83:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/application/notification-dispatch-service-log-trace-id.spec.ts:84:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/application/notification-dispatch-service-log-trace-id.spec.ts:114:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/application/notification-dispatch-service-log-trace-id.spec.ts:116:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/application/notification-dispatch-service-log-trace-id.spec.ts:117:expect(logged.correlationId).toBe(ENVELOPE.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/application/notification-dispatch.service.spec.ts:186:expect(meta.correlationId).toBe('order-1');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:69:expect(logged.message).toBe('fact-retry-dispatcher: exhausted attempts, fact dead-lettered');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:70:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:71:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:72:expect(logged.correlationId).toBe(env.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:101:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:103:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:104:expect(logged.correlationId).toBe(env.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:62:expect(logged.correlationId).toBe('order-1');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:63:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:64:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:81:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:82:expect(rawLine).not.toContain('"correlationId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:84:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:85:expect(Object.prototype.hasOwnProperty.call(logged, 'correlationId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:128:expect(logged.correlationId).toBe('order-1');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:129:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/console-notification-sender-log-trace-id.spec.ts:130:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/infrastructure/notification/degrading-notification-sender-log-trace-id.spec.ts:55:expect(logged.correlationId).toBe('order-1');
    -> N/A to feature 27 — #7 degrading sender (ad90de6 mechanism) has no #8 counterpart; see advisory A7
8a3a3d3 notifications/src/infrastructure/notification/degrading-notification-sender-log-trace-id.spec.ts:56:expect(logged.traceId).toBe(originTraceId);
    -> N/A to feature 27 — #7 degrading sender (ad90de6 mechanism) has no #8 counterpart; see advisory A7
8a3a3d3 notifications/src/infrastructure/notification/degrading-notification-sender-log-trace-id.spec.ts:57:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> N/A to feature 27 — #7 degrading sender (ad90de6 mechanism) has no #8 counterpart; see advisory A7
8a3a3d3 notifications/src/infrastructure/notification/degrading-notification-sender-log-trace-id.spec.ts:82:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> N/A to feature 27 — #7 degrading sender (ad90de6 mechanism) has no #8 counterpart; see advisory A7
8a3a3d3 notifications/src/infrastructure/notification/degrading-notification-sender-log-trace-id.spec.ts:84:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> N/A to feature 27 — #7 degrading sender (ad90de6 mechanism) has no #8 counterpart; see advisory A7
8a3a3d3 notifications/src/infrastructure/notification/degrading-notification-sender.spec.ts:121:expect(calls[0]!.meta.correlationId).toBe('order-1');
    -> N/A to feature 27 — #7 degrading sender (ad90de6 mechanism) has no #8 counterpart; see advisory A7
ad90de6 notifications/src/infrastructure/notification/degrading-notification-sender.spec.ts:190:expect(dlqCalls).toHaveLength(1);
    -> N/A to feature 27 — #7 degrading-sender DLQ fix; see advisory A7
ad90de6 notifications/src/infrastructure/notification/degrading-notification-sender.spec.ts:191:expect(dlqCalls[0]!.meta.attempts).toBe(3);
    -> N/A to feature 27 — #7 degrading-sender DLQ fix; see advisory A7
ad90de6 notifications/src/infrastructure/notification/degrading-notification-sender.spec.ts:192:expect((dlqCalls[0]!.meta.error as Error).message).toBe(SOCKET_ERROR.message);
    -> N/A to feature 27 — #7 degrading-sender DLQ fix; see advisory A7
64a2a77 notifications/src/infrastructure/templates/invoice-issued.template.spec.ts:27:expect(message.subject).toContain('correlationId: corr-42');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
64a2a77 notifications/src/infrastructure/templates/order-cancelled.template.spec.ts:25:expect(message.subject).toContain('correlationId: corr-42');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
64a2a77 notifications/src/infrastructure/templates/order-completed.template.spec.ts:23:expect(message.subject).toContain('correlationId: corr-42');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
64a2a77 notifications/src/infrastructure/templates/order-confirmed.template.spec.ts:23:expect(message.subject).toContain('correlationId: corr-42');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
64a2a77 notifications/src/infrastructure/templates/order-despatched.template.spec.ts:23:expect(message.subject).toContain('correlationId: corr-42');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
64a2a77 notifications/src/infrastructure/templates/order-placed.template.spec.ts:28:expect(message.subject).toContain('correlationId: corr-42');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
64a2a77 notifications/src/infrastructure/templates/payment-received.template.spec.ts:26:expect(message.subject).toContain('correlationId: corr-42');
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
64a2a77 notifications/src/notification-consumption.integration.spec.ts:191:expect(sender.sent[0]!.subject).toContain(`correlationId: ${envelope.correlationId}`);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8635b66 notifications/src/notification-dead-letter.integration.spec.ts:256:expect(dlq.headers['x-failed-consumer']).toBe('notifications');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 notifications/src/notification-dead-letter.integration.spec.ts:257:expect(dlq.headers['x-attempts']).toBe('3');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 notifications/src/notification-dead-letter.integration.spec.ts:258:expect(dlq.headers['x-error']).toBeTruthy();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 notifications/src/notification-dead-letter.integration.spec.ts:259:expect(dlq.headers['x-original-topic']).toBe(ORDERS_FACTS_TOPIC);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 notifications/src/notification-dead-letter.integration.spec.ts:262:expect(dlq.value.eventId).toBe(poisonEnvelope.eventId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 notifications/src/notification-dead-letter.integration.spec.ts:263:expect((dlq.value.payload as Record<string, unknown>).retailerCode).toBeUndefined();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8a3a3d3 notifications/src/presentation/notification-facts-controller-log-trace-id.spec.ts:58:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/presentation/notification-facts-controller-log-trace-id.spec.ts:59:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/presentation/notification-facts-controller-log-trace-id.spec.ts:60:expect(Object.prototype.hasOwnProperty.call(logged, 'correlationId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/presentation/notification-facts-controller-log-trace-id.spec.ts:84:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 notifications/src/presentation/notification-facts-controller-log-trace-id.spec.ts:86:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8635b66 notifications/src/presentation/notification-facts.controller.spec.ts:206:expect(trace.getSpanContext(reExtracted)?.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
32da6e9 orders/src/application/cancel-order.handler.spec.ts:172:expect(input.triggeringEventTopic).toBe(ORDERS_FACTS_TOPIC);
    -> DROPPED AND INVERTED — operator-cancel synthetic envelope (hand-off item 1); routed to id 71
32da6e9 orders/src/application/cancel-order.handler.spec.ts:178:expect(input.triggeringEventEnvelope.eventType).toBe('orders.cancel.requested');
    -> DROPPED AND INVERTED — operator-cancel synthetic envelope (hand-off item 1); routed to id 71
32da6e9 orders/src/application/cancel-order.handler.spec.ts:219:expect(input.triggeringEventTopic).toBe(ORDERS_FACTS_TOPIC);
    -> DROPPED AND INVERTED — operator-cancel synthetic envelope (hand-off item 1); routed to id 71
32da6e9 orders/src/application/cancel-order.handler.spec.ts:220:expect(input.triggeringEventEnvelope.eventType).toBe('orders.cancel.requested');
    -> DROPPED AND INVERTED — operator-cancel synthetic envelope (hand-off item 1); routed to id 71
8635b66 orders/src/application/place-order.handler.spec.ts:288:await expect(handler.execute(baseCommand({ requestId: FIXTURE_REQUEST_ID }))).rejects.toBe(orderReferenceDup);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
1eea58f orders/src/domain/order-events.spec.ts:175:expect(event.correlationId.equals(order.id)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
1eea58f orders/src/domain/order-events.spec.ts:192:expect(event.correlationId.equals(order.id)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8635b66 orders/src/domain/order.spec.ts:396:expect(events[0]!.eventType).toBe('order.saga_failed.v1');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/domain/order.spec.ts:415:expect(event!.correlationId.equals(order.id)).toBe(true);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:71:expect(logged.message).toBe('fact-retry-dispatcher: exhausted attempts, fact dead-lettered');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:72:expect(logged.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:73:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:74:expect(logged.correlationId).toBe(env.correlationId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:103:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:105:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:106:expect(logged.correlationId).toBe(env.correlationId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:83:expect(dlqCalls).toHaveLength(1);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:84:expect(dlqCalls[0]!.sourceTopic).toBe('otc.orders.facts.v1');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:85:expect(dlqCalls[0]!.envelope).toBe(env); // the UNMODIFIED original envelope
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:86:expect(dlqCalls[0]!.meta).toMatchObject({
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:92:expect((dlqCalls[0]!.meta.error as Error).message).toBe('boom');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:111:expect(dlqCalls).toHaveLength(0);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:127:expect(dlqCalls).toHaveLength(0);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:133:expect(loadFactRetryPolicy({})).toEqual(DEFAULT_FACT_RETRY_POLICY);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/fact-retry-dispatcher.spec.ts:137:expect(loadFactRetryPolicy({ FACT_RETRY_MAX_ATTEMPTS: '5', FACT_RETRY_BACKOFF_MS: '250' })).toEqual({
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/idempotent-consumer.parity.spec.ts:389:expect(canonicalRetryDispatcherBody, 'fact-retry-dispatcher.ts names a service outside its banner').not.toMatch(
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/kafka-dlq-publisher.spec.ts:75:expect(extractedSpanContext!.traceId).toBe(traceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/kafka-dlq-publisher.spec.ts:95:expect(message.headers.traceparent).toBeUndefined();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8a35d4e orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:120:expect(capturedHeaders!.get('x-correlation-id')).toBe(META.correlationId.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8a35d4e orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:121:expect(capturedHeaders!.get('x-request-id')).toBe(META.requestId.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8635b66 orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:406:expect(extractedSpanContext!.traceId).toBe(traceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:116:expect(extractedSpanContext!.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:117:expect(extractedSpanContext!.spanId).toBe(originSpanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:119:expect(extractedSpanContext!.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:183:expect(extractedSpanContext!.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:187:expect(extractedSpanContext!.spanId).not.toBe(writerSpanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:188:expect(extractedSpanContext!.spanId).toMatch(/^[0-9a-f]{16}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:194:expect(publishSpan!.spanContext().traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:195:expect(publishSpan!.parentSpanContext?.spanId).toBe(writerSpanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:196:expect(publishSpan!.spanContext().spanId).toBe(extractedSpanContext!.spanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/messaging/trace-context-propagation.integration.spec.ts:243:expect(receivedHeaders.traceparent).toBeUndefined();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/kafka-dlq-depth.spec.ts:33:expect(result.get('otc.orders.facts.v1.dlq')).toBe(8 + 5 + 0); // (10-2) + (5-0) + (3-3)
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/kafka-dlq-depth.spec.ts:42:expect(result.get('otc.billing.facts.v1.dlq')).toBe(0);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/kafka-dlq-depth.spec.ts:53:expect(result.get('otc.orders.facts.v1.dlq')).toBe(4);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/kafka-dlq-depth.spec.ts:54:expect(result.get('otc.fulfillment.facts.v1.dlq')).toBe(0);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/log-correlation.integration.spec.ts:129:expect(row?.traceParent).toBe(`00-${originTraceId}-${writerSpan.spanContext().spanId}-01`);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/log-correlation.integration.spec.ts:157:expect(traceIds.every((id) => typeof id === 'string')).toBe(true);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/log-correlation.integration.spec.ts:160:expect(new Set(traceIds).size).toBe(1);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/log-correlation.integration.spec.ts:161:expect(traceIds[0]).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/log-correlation.integration.spec.ts:162:expect(traceIds[0]).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/log-correlation.integration.spec.ts:163:expect(traceIds[0]).not.toBe('00000000000000000000000000000000'.slice(0, 32));
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/log-correlation.integration.spec.ts:172:expect(span.spanContext().traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/observability/log-correlation.integration.spec.ts:237:expect(Object.prototype.hasOwnProperty.call(parsed, 'traceId')).toBe(false);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:65:expect(headers.has('traceparent')).toBe(true);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:73:expect(extractedSpanContext!.traceId).toBe(traceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:74:expect(extractedSpanContext!.spanId).toBe(spanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:77:expect(extractedSpanContext!.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:78:expect(extractedSpanContext!.traceId).not.toBe('00000000000000000000000000000000'.slice(0, 32));
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:105:expect(trace.getSpanContext(asString)?.traceId).toBe(traceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:106:expect(trace.getSpanContext(asString)?.spanId).toBe(spanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:111:expect(trace.getSpanContext(asBuffer)?.traceId).toBe(traceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:112:expect(trace.getSpanContext(asBuffer)?.spanId).toBe(spanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:130:expect(captured).toBe(`00-${traceId}-${spanId}-01`);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:146:expect(trace.getSpanContext(restored)?.traceId).toBe(traceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:147:expect(trace.getSpanContext(restored)?.spanId).toBe(spanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/observability/trace-context.spec.ts:169:expect(publishRecord?.parentSpanContext?.spanId).toBe(writerSpanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
89b41f3 orders/src/infrastructure/outbox/outbox-envelope.integration.spec.ts:70:expect(placedRow?.correlationId).toBe(order.id.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
89b41f3 orders/src/infrastructure/outbox/outbox-envelope.integration.spec.ts:89:expect(confirmedRow?.correlationId).toBe(order.id.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
89b41f3 orders/src/infrastructure/outbox/outbox-envelope.integration.spec.ts:92:expect(row.correlationId).toBe(order.id.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
89b41f3 orders/src/infrastructure/outbox/outbox-envelope.integration.spec.ts:119:expect(envelope.correlationId).toBe(order.id.value);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:125:expect(logged.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:126:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:127:expect(logged.correlationId).toBe(row.orderId.value);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:160:expect(logged.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:161:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:162:expect(logged.correlationId).toBe(row.orderId.value);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:191:expect(sentLine).not.toContain('"traceId":"undefined"');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:192:expect(Object.prototype.hasOwnProperty.call(JSON.parse(sentLine), 'traceId')).toBe(false);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:208:expect(parkedLine).not.toContain('"traceId":"undefined"');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts:209:expect(Object.prototype.hasOwnProperty.call(JSON.parse(parkedLine), 'traceId')).toBe(false);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8a35d4e orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:198:expect(meta.correlationId).toEqual(row.orderId);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8a35d4e orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:199:expect(meta.requestId).toEqual(row.id);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
fd445bc orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:285:expect(releaseStock).toHaveBeenCalledTimes(1); // NOT maxAttempts (3) — no in-line retry at all
    -> N/A — terminal-rejection classification (#8 id 42, done)
fd445bc orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:291:expect(store.parkCalls).toHaveLength(0); // never park()'s retry-eligible path
    -> N/A — terminal-rejection classification (#8 id 42, done)
fd445bc orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:321:expect(releaseStock).toHaveBeenCalledTimes(DEFAULT_SAGA_COMMAND_DISPATCHER_CONFIG.maxAttempts); // full retry budget, unaffected
    -> N/A — terminal-rejection classification (#8 id 42, done)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:346:expect(store.claimDeadLetterCalls).toEqual([row.id]);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:347:expect(firstPark.calls).toHaveLength(1);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:348:expect(firstPark.calls[0]?.row.id).toEqual(row.id);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:349:expect(firstPark.calls[0]?.attempts).toBe(3);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:350:expect(firstPark.calls[0]?.lastError).toContain('no responders');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:370:expect(store.claimDeadLetterCalls).toEqual([row.id]); // still claimed-attempted...
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:371:expect(firstPark.calls).toHaveLength(0); // ...but never called, because the claim failed
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:391:expect(store.claimDeadLetterCalls).toHaveLength(0);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:392:expect(firstPark.calls).toHaveLength(0);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts:136:expect(logged.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts:137:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts:138:expect(logged.correlationId).toBe(claimed[0]!.orderId.value);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts:187:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts:189:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts:190:expect(logged.correlationId).toBe(claimed[0]!.orderId.value);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:107:expect(logged.message).toBe('saga-first-park-dead-letter-handler: no order row for orderId, fact not recorded');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:108:expect(logged.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:109:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:110:expect(logged.correlationId).toBe(row.orderId.value);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:134:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:136:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/infrastructure/saga/saga-first-park-dead-letter-handler-log-trace-id.spec.ts:137:expect(logged.correlationId).toBe(row.orderId.value);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/orders-create-idempotent-replay.integration.spec.ts:133:expect(row?.requestId).toBe(requestId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/orders-create-idempotent-replay.integration.spec.ts:141:expect(await requestIdRowCount(requestId)).toBe(1);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/orders-create-idempotent-replay.integration.spec.ts:163:expect(await requestIdRowCount(requestId)).toBe(1);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/orders-create-idempotent-replay.integration.spec.ts:180:expect(rowA?.requestId).toBeNull();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/orders-create-idempotent-replay.integration.spec.ts:181:expect(rowB?.requestId).toBeNull();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/presentation/saga-facts.controller.spec.ts:37:/** A passthrough fake — calls `process` exactly once and returns/rejects with whatever it does, no retry, no DLQ. `FactRetryDispatcher`'s own retry
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/presentation/saga-facts-log-trace-id.spec.ts:51:expect(headers.traceparent).toBeDefined();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/presentation/saga-facts-log-trace-id.spec.ts:61:expect(logged.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/presentation/saga-facts-log-trace-id.spec.ts:62:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
95e883a orders/src/presentation/saga-facts-log-trace-id.spec.ts:87:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/presentation/saga-facts-trace-continuity.spec.ts:73:expect(headers.traceparent).toBeDefined();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/presentation/saga-facts-trace-continuity.spec.ts:119:expect(dlqCalls).toHaveLength(1);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/presentation/saga-facts-trace-continuity.spec.ts:122:expect(dlqSpanContext).toBeDefined();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/presentation/saga-facts-trace-continuity.spec.ts:123:expect(dlqSpanContext!.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/presentation/saga-facts-trace-continuity.spec.ts:131:expect(consumeSpan!.spanContext().traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/presentation/saga-facts-trace-continuity.spec.ts:132:expect(consumeSpan!.parentSpanContext?.spanId).toBe(upstreamSpan.spanContext().spanId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-command-dead-letter.integration.spec.ts:93:expect(firstParkRow).toMatchObject({ status: 'parked', attempts: 2 });
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-command-dead-letter.integration.spec.ts:94:expect(firstParkRow?.deadLetteredAt).not.toBeNull();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-command-dead-letter.integration.spec.ts:101:expect(matching[0]!.headers['x-failed-consumer']).toBe('orders.saga');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-command-dead-letter.integration.spec.ts:106:expect(sagaFailedRows).toHaveLength(1);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-command-dead-letter.integration.spec.ts:147:expect(await sagaFailedOutboxRows(harness, order.id.value)).toHaveLength(1);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:152:expect(dlq.headers['x-failed-consumer']).toBe('orders.saga');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:153:expect(dlq.headers['x-attempts']).toBe('3');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:154:expect(dlq.headers['x-error']).toBeTruthy();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:155:expect(dlq.headers['x-original-topic']).toBe(ORDERS_FACTS_TOPIC);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:157:expect(dlq.value.eventId).toBe(poisonEnvelope.eventId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:158:expect(dlq.value.correlationId).toBe('not-a-uuid-correlation-id');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:191:expect(dlq.headers['x-failed-consumer']).toBe('orders.saga');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:192:expect(dlq.headers['x-attempts']).toBe('3');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:193:expect(dlq.headers['x-error']).toContain('simulated generic processing failure');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 orders/src/saga-dead-letter.integration.spec.ts:194:expect(dlq.value.eventId).toBe(envelope.eventId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8a3a3d3 projector/src/application/projection-apply-service-log-trace-id.spec.ts:73:expect(logged.correlationId).toBe(envelope.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/application/projection-apply-service-log-trace-id.spec.ts:74:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/application/projection-apply-service-log-trace-id.spec.ts:75:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/application/projection-apply-service-log-trace-id.spec.ts:105:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/application/projection-apply-service-log-trace-id.spec.ts:107:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/application/projection-apply-service-log-trace-id.spec.ts:108:expect(logged.correlationId).toBe(envelope.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/application/projection-apply.service.spec.ts:77:expect(meta).toMatchObject({ orderId: 'order-1', correlationId: 'order-1', error: 'NATS unreachable' });
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:69:expect(logged.message).toBe('fact-retry-dispatcher: exhausted attempts, fact dead-lettered');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:70:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:71:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:72:expect(logged.correlationId).toBe(env.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:101:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:103:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/messaging/fact-retry-dispatcher-log-trace-id.spec.ts:104:expect(logged.correlationId).toBe(env.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/persistence/mongo-read-model-writer-log-trace-id.spec.ts:79:expect(logged.message).toBe('mongo-read-model-writer: placeholder upsert lost the insert race, retrying once');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/persistence/mongo-read-model-writer-log-trace-id.spec.ts:81:expect(logged.correlationId).toBe(orderId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/persistence/mongo-read-model-writer-log-trace-id.spec.ts:82:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/persistence/mongo-read-model-writer-log-trace-id.spec.ts:83:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/persistence/mongo-read-model-writer-log-trace-id.spec.ts:107:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/persistence/mongo-read-model-writer-log-trace-id.spec.ts:109:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/infrastructure/persistence/mongo-read-model-writer-log-trace-id.spec.ts:110:expect(logged.correlationId).toBe(orderId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts-controller-log-trace-id.spec.ts:60:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts-controller-log-trace-id.spec.ts:61:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts-controller-log-trace-id.spec.ts:62:expect(Object.prototype.hasOwnProperty.call(logged, 'correlationId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts-controller-log-trace-id.spec.ts:87:expect(rawLine).not.toContain('"traceId":"undefined"');
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts-controller-log-trace-id.spec.ts:89:expect(Object.prototype.hasOwnProperty.call(logged, 'traceId')).toBe(false);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts-controller-log-trace-id.spec.ts:128:expect(logged.correlationId).toBe(envelope.correlationId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts-controller-log-trace-id.spec.ts:129:expect(logged.traceId).toBe(originTraceId);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts-controller-log-trace-id.spec.ts:130:expect(logged.traceId).toMatch(/^[0-9a-f]{32}$/);
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8a3a3d3 projector/src/presentation/projector-facts.controller.spec.ts:107:expect(logger.error.mock.calls[0]![1]).toMatchObject({ correlationId: envelope.correlationId });
    -> NOT PORTED — D2: per-site log trace/correlation on Notifications/Projector/Gateway-stream; design §11 rows 50–58 promised one log-capture case per service
8635b66 projector/src/presentation/projector-facts.controller.spec.ts:212:expect(trace.getSpanContext(reExtracted)?.traceId).toBe(originTraceId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 projector/src/projector-dead-letter.integration.spec.ts:162:expect(dlq.headers['x-failed-consumer']).toBe('projector');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 projector/src/projector-dead-letter.integration.spec.ts:163:expect(dlq.headers['x-attempts']).toBe('3');
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 projector/src/projector-dead-letter.integration.spec.ts:164:expect(dlq.headers['x-error']).toBeTruthy();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 projector/src/projector-dead-letter.integration.spec.ts:165:expect(dlq.headers['x-original-topic']).toBe(FULFILLMENT_FACTS_TOPIC);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 projector/src/projector-dead-letter.integration.spec.ts:168:expect(dlq.value.eventId).toBe(poisonEnvelope.eventId);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
8635b66 projector/src/projector-dead-letter.integration.spec.ts:169:expect((dlq.value.payload as { shortages: unknown[] }).shortages).toEqual([]);
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
d71baa7 ./packages/contracts/src/index.spec.ts:35:expect(envelope.correlationId).toBe(envelope.aggregateId);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
d71baa7 ./packages/shared-kernel/src/domain/aggregate-root.spec.ts:76:expect(event.correlationId.equals(aggregate.id)).toBe(true);
    -> N/A — pre-feature-27 assertion of an earlier feature (correlation/requestId stamping, templates, auth, retry-after); matched only on a shared term
8635b66 ./packages/shared-kernel/src/domain/event-envelope.spec.ts:55:expect(() => createDomainEvent({ ...validParams(), eventType: 'order.saga_failed.v1' })).not.toThrow();
    -> S11 — added by #7 feature-27 commit; case-classified in design.md §11 (delivery NOT reconciled: see D3)
```

---

## Round 2

**Verdict: REJECTED.** Status left at `in_review`, as the brief directs. `feature_list.json` not edited; `git diff -U0 -- feature_list.json` still shows the same six leader hunks (`@@ -406`, `-408`, `-415`, `-468`, `-998`, `-1000`).

**Where this stands.** D1, D2, D4, R1, R2 and R3 are genuinely closed, and every probe I ran against them killed. Row 6–7 and the `FACT_RETRY_*` substitution are closed, armed by me in copies other than the ones the fix round armed. **D3 is not closed.** Its required deliverable was a §11 walk naming the delivered test per row, but the walk classified **class files**, not cases (`grep -rln "class <Name>"`, record `:2500`). Five row groups it marks "Exists" have no assertion anywhere in the repository, and two of them I armed on whole-project green suites. The same round's closures of the timestamp headers and the Mongo probe pair each satisfy the literal probe they were built against and nothing adjacent to it. **Recommendation:** one more bounded fix round on D5–D7 and R4–R9 — test and record work only, no production change, no gate, no amendment.

### Scope — what I ran, and what I did not

- **Not re-run:** `./quality.sh` or `./init.sh` in full. The 1780 claim is partially reconciled by the whole-project runs below, whose totals match the record's per-project figures: `Orders.UnitTests` **433**, `Projector.UnitTests` **118**, `Projector.IntegrationTests` **58**, `Architecture.Tests` **25**.
- **Run whole** (each claim was "nothing in this project notices"): `Orders.UnitTests`, `Projector.UnitTests`, `Projector.IntegrationTests`, `Architecture.Tests` — each under mutation.
- **Run as named tests:** every closure probe.
- **Protocol on every probe:** `cp -p` backup → mutate with an exact-once replacement (the mutation script asserts the match count is 1) → `dotnet build <project> --no-incremental` → run → record verbatim → `cp` restore → `cmp` → `touch` → forced rebuild → confirming run.
- **One build at a time.** Chained serially in one background script, waited on by PID with `kill -0`, with only read-only work while it ran.
  - My first `pgrep -fl "dotnet (build|test|format)"` matched **my own bash** (PID 921614), because its command line contained that text — the self-counting shape `CLAUDE.md` warns about.
  - The final check used `pgrep -a dotnet | grep -E "dotnet (build|test|format)"`, exit 1.
- **Final state.**
  - Every mutated file is `cmp`-identical to its backup.
  - `grep -n REVIEW-PROBE` over `src/`/`tests/` `*.cs` (bin/obj pruned by path) gives no hits (exit 123).
  - Backups live only in the session scratchpad.
  - HEAD is still `909394f`.

### The #7 checkout at the addendum's path

- **Not the same checkout.** `realpath /media/juanpabloperez/Elements/Slimbook2/Work/Projects/Assessments/order-to-cash-nestjs` returns itself. Its inode (8863232) differs from the canonical checkout's (24280635) on the same device, and `git -C <it> rev-parse HEAD` → `fatal: not a git repository`.
- **It is an unversioned copy**, so a citation into it cannot be pinned to a revision.
- **The cited file does verify.** `apps/orders/src/infrastructure/messaging/kafka-dlq-publisher.spec.ts` is `cmp`-identical between the copy and the canonical checkout (HEAD `bf45af0`, clean). It carries exactly two `it(` cases, at `:45` and `:82`.
- **Ruling:** the substance holds; the citation path is corrected by R7.

### Probes

| # | Closure under test | Mutation | Test run | Verbatim result | Restore / confirm |
|---|---|---|---|---|---|
| 1 | D1 (my P6) | `src/Fulfillment/Presentation/StockRpcResponder.cs:162` — `parentContext: parent` dropped (fresh root) | `StockRpcResponderTraceContinuationTests` (2) | **Both fail.** `D1_StockCheck…`: `Assert.Single() Failure: The collection did not contain any matching items` — exported `rpc fulfillment.stock.check` ×2 + `test caller`, none on the caller's trace. `D1_StockReserve…` (`:118`): `Assert.Equal() Failure: Values differ / Expected: 76447094d713126ab66268ddce0dc39d / Actual: 95c6c7df63e67864ffee3f3056a7c9d7` | `cmp` identical; forced rebuild; **2/2** green |
| 2 | D1, Billing | `src/Billing/Presentation/BillingRpcResponder.cs:162` — `TraceContext.ExtractNats(...)` replaced by `ActivityContext? context = null;` | `BillingRpcResponderTraceContinuationTests` (2) | **Both fail.** `D1_CreditList…`: `Assert.Single() Failure: The collection did not contain any matching items`. `D1_CreditHold…`: `Assert.Equal() Failure: Values differ / Expected: c164f136ba9e407892e6b7e21425f630 / Actual: c98a12d4c87c0fe7147905904e2637b2` | `cmp` identical; forced rebuild; **3/3** green (with #3) |
| 3 | D2 spot-check, `ActivityTrackingOptions` stripped | `src/Billing/BillingHost.cs:42` → `ActivityTrackingOptions.None` | Billing `LogCorrelationTests.R58_OR7_ARealRpcFailureLogLineCarriesTheRequestsCorrelationIdAndATraceId` | **Fails:** `Assert.False() Failure / Expected: False / Actual: True` (the empty-`TraceId` assertion) | as above |
| 4 | L25's claim that *any one* setting missing drops the field | same line **deleted outright** | same test | **Passed 1/1** — see R4 | `cmp` identical; forced rebuild; green |
| 5 | D2 (my P5) | `src/Notifications/NotificationsHost.cs:39` `IncludeScopes = false` | Notifications `LogCorrelationTests.R58_OR7_EveryRecordProducedWhileHandlingAPoisonFactCarriesTheSameCorrelationIdAndTheSameTraceId` | **Fails [1 m 9 s]:** `Expected more than one log record for this poison fact's correlationId; found 0.` | `cmp` identical; forced rebuild; **1/1** green |
| 6 | `FACT_RETRY_*` substitution | `src/Notifications/NotificationsProgramConfiguration.cs:28` — `MaxAttempts` reads `FACT_RETRY_BACKOFF_MS` | `Configure_ReadsFactRetryMaxAttemptsAndBackoffMsIndependently_FromTheirOwnDistinctVariableNames` | **Fails:** `Assert.Equal() Failure: Values differ / Expected: 7 / Actual: 250`. That is the wrong key's value, not the default `3` | `cmp` identical; forced rebuild; **3/3** green (with #7) |
| 7 | Row 6–7 (i), a fabricated id | `src/Notifications/…/KafkaDeadLetterPublisher.cs` — `traceparent` = `00-{random trace}-{random span}-01` whenever a span is active | `KafkaDeadLetterPublisherTests.PublishAsync_WithAnActiveSpan_…` | **Fails:** `Assert.Equal() Failure: Strings differ / ↓ (pos 3) / Expected: "00-6db43e202c9a060adf48f317c157872f-4aaf3"···` | as #6 |
| 8 | Row 6–7 (ii), header always added; plus rows 36–37 | `src/Projector/…/KafkaDeadLetterPublisher.cs` — the `if` removed, header added with a fixed fallback value. **And** `src/Projector/Presentation/ProjectorFactsConsumer.cs:121` — `parentContext: parent` dropped | `Projector.UnitTests` **whole** | **117/118.** The one failure is `PublishAsync_WithNoActiveSpan_PublishesNoTraceparentHeaderAtAll`: `Assert.DoesNotContain() Failure: Filter matched in collection … Header { Key = "traceparent" }`. **The consumer's fresh root failed nothing** | `cmp` identical both; forced rebuild; **34/34** green (`KafkaDeadLetterPublisherTests` + `ProjectorFactsConsumerTests`) |
| 9 | D3 (my P7, the literal) | Projector publisher — `x-first-failed-at` → `"corrupted-by-review-probe"` | `ProjectorDeadLetterTests.OR1_R16_…` | **Fails:** `Assert.True() Failure / Expected: True / Actual: False` (the `TryParse` at `:79`) | `cmp` identical; forced rebuild; **1/1** green |
| 10 | D3, a **valid but wrong** value; plus rows 36–37 | Projector publisher — `x-first-failed-at` renders `publication.FailedAt`. **And** the #8 consumer fresh root | `Projector.IntegrationTests` **whole** | **58/58 green** — D6 and D5 | `cmp` identical both; forced rebuild; confirmed with #8's and #9's runs |
| 11 | §11 rows 65–66 / 74–78 | `src/Orders/Application/Sagas/SagaFactHandler.cs:118` — `var outcomeTag = "completed";` | `Orders.UnitTests` **whole** | **433/433 green** — D5 | `cmp` identical; forced rebuild; `SagaFactHandlerTests` **11/11** green |
| 12 | NATS parity extension | `src/Fulfillment/Infrastructure/Health/NatsHealthCheck.cs` — `_timeout` 2 s → 30 s | `Architecture.Tests` **whole** | **24/25.** The failure is `HoldsEveryNatsHealthCheckCopy…`: `src/Fulfillment/Infrastructure/Health/NatsHealthCheck.cs diverges from the canonical src/Orders/Infrastructure/Health/NatsHealthCheck.cs outside the namespace and own-service using line.` It names the file | `cmp` identical; forced rebuild; **25/25** green |
| 13 | Mongo pair | Same build as #12: `src/Gateway/Infrastructure/Health/MongoHealthCheck.cs` — `cts.CancelAfter(_timeout);` deleted | `Architecture.Tests` **whole** | **Failed nothing** — neither `EveryMongoHealthCheckCopyNamesReadModel_UsesTheSameTwoSecondBoundedTimeout_…` nor `HealthProbeTimeoutTests`. The only failure was #12's — D7 | as #12 |

**Why #8, #10 and #12–#13 share builds.** Each combined mutation targets a different file and a different test, and every failure message names its own test. The surviving mutations are claims about a whole project, which is what those runs measured.

## Closure, finding by finding

### D1 — **CLOSED**

- **Probes 1 and 2 kill** on the trace *value*, not presence.
- **The corruption shape round 1 asked for is asserted.** All three responder tests check `Assert.Equal(callerActivity.SpanId, serverSpan.ParentSpanId)`.
- **The Orders test drives the production class.** `OrdersCreateResponderTraceContinuationTests.BuildHost` calls `AddOrdersAcceptance`, which registers `services.AddHostedService<OrdersCreateResponder>()` (`src/Orders/Infrastructure/OrdersAcceptanceServiceCollectionExtensions.cs:44`). The real request goes over NATS to `StartResponderActivity` (`OrdersCreateResponder.cs:97-99`, `:150`), not to a stand-in.
- **The cells no longer overclaim.**
  - R56 now calls `TraceContextPropagationTests`' NATS case *"a stand-in responder, proving the wire mechanism"* and names the three production-responder files.
  - R57's *"each armed by … a fresh root … and by deleting the extraction call"* was not armed for the two outbox cases in the fix round (record `:2474` says so). Probes 1 and 2 now show it true for Fulfillment and Billing, both cases each.
- **Residual:** the L24 citation — R5.

### D2 — **CLOSED in substance**

- **Probe 5 (my P5) kills.** Probe 3 kills Billing with `ActivityTrackingOptions` stripped to `None`.
- **Enumeration from a literal set holds by construction.** Four per-host test files, no discovery step for a missing host to drop out of.
- **Residuals:**
  - `R58`'s Status cell still cites only Orders and Gateway (`test-matrix.md:187`). The fix round left it out of scope because the leader's brief limited it to the R56/R57 cells (record `:2496`). Round 1's required change 2 named that cell — R6.
  - Probe 4 disproves one clause of L25's *"in #8"* column — R4.

### D3 — **NOT CLOSED** → D5, D6, D7

**The rulings the brief asked for, on the three reclassifications:**

- **Rows 4–5 → `*ProgramConfigurationTests`. Legitimate for the substitution half, partial for the defaults half.**
  - The substitution cases exist in both new roots, and probe 6 kills naming the wrong key's value.
  - #7's rows 4–5 also assert the **defaults** `3`/`500`. `grep -nE "FactRetry\."` over `NotificationsProgramConfigurationTests.cs` and `ProjectorProgramConfigurationTests.cs` returns four lines, all the `7`/`250` asserts (`:153-154`, `:113-114`). The defaults are asserted only in Orders (`OrdersProgramConfigurationTests.cs:220-221`).
  - So a changed fallback in `NotificationsProgramConfiguration.cs:30`/`:33` or its Projector twin goes unnoticed. That is a small, real part of D5.
- **Rows 6–7 → `KafkaDeadLetterPublisherTests` ×3. Legitimate, and genuinely covers #7's two cases.**
  - The active-span case asserts `Assert.Equal(activity!.Id, traceparent)` plus a round-trip to the same `TraceId`, not presence.
  - The no-span case asserts `Assert.Null(Activity.Current)` first, so its premise cannot silently fail.
  - Both are armed by me in copies the fix round's own table did not choose for the other shape (probes 7 and 8).
  - The corrected mechanism at `:2542` is right in substance: `StartActivity` returns null when no listener samples the source. This is renaming the *reasoning*, not the promise.
- **Rows 67–69 → `MetricsExposureTests`. Legitimate; not re-armed by me.**
  - The new case asserts an **exact** sum over two real `.dlq` topics with different partition counts and a third absent (`:156-190`). Its overwrite-not-accumulate arm is recorded.
  - **The pre-existing case is not weakened by its fix.** It still asserts exact equality between the gauge and broker-read watermarks (`:111`, `:119`), and still floors the produced topic at `>= 3` (`:99`).
  - `ReadWatermarkDepthAsync` returning 0 for a missing topic can only lower the expected value. It cannot make a gauge that skips or overwrites a topic match.
  - The cross-topic assumption *"BillingFacts.dlq is never created"* can only turn the case **red** if broken, never green. The collections are separate definitions (`KafkaCollection`, `KafkaContainerFixture.cs:111`; `SagaCollection`, `SagaCollection.cs:13`), and xUnit gives each its own fixture instance.

**The row sample, by literal case name** (method names enumerated with `grep -hoE "public (async Task|void) [A-Za-z0-9_]+"` over each class, bin/obj pruned by path):

| §11 row | #7's claim | Delivered #8 case(s) | Holds? |
|---|---|---|---|
| 1–3 | retry/backoff/DLQ; retry then succeed; first-time success | `FactRetryDispatcherTests` › `OR1_RetriesToTheConfiguredMaximumWithExponentialBackoff_ThenPublishesToTheDlqTopicAndReturnsNormally`, `OR1_RetriesThenSucceeds_WithoutEverPublishingToTheDlq`, `OR1_SucceedsOnTheFirstAttempt_WithNoDelayAndNoDlqPublish` | yes |
| 12–13 | dispatcher copy required from every consumer; byte-identical | `FactRetryDispatcherParityTests` › `RequiresACopyOfTheDispatcherFromEveryServiceThatOwnsAFactConsumer_AndFromNoOther`, `HoldsEveryFactConsumingServicesCopyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine` | yes |
| 14–16 | first-park hook once with attempts; not when already dead-lettered; not when no transition | `SagaCommandDispatcherFirstParkTests` › `OR3_CallsTheFirstParkHookExactlyOnce_WithTheRowsAccumulatedAttemptsAndTheLastError_WhenParkAsyncReportsTheTransition`, `OR3_DoesNotCallTheFirstParkHook_WhenParkAsyncReportsNoTransition`; `SagaFirstParkDeadLetterHandlerTests` › `OR3_AppendsNoFactAndPublishesNoDlqCopy_WhenTheClaimReportsAlreadyDeadLettered` | yes |
| 18–19 | `recordSagaFailure` one event, state unchanged; ids are the order id | `OrderSagaFailureTests` › `OR3_AppendsExactlyOneOrderSagaFailedEvent_AndLeavesStatusLinesTotalsAndUpdatedAtUnchanged`, `OR3_CorrelationIdAndAggregateIdAreBothTheOrderId` | yes |
| 21–28 | carrier inject/extract (7 of 8) | `TraceContextCarrierTests` — 14 cases, `ActiveTraceParent_*` ×2 through `ExtractKafka_*` ×2 | yes |
| 33 | outbound saga command carries the real trace id, two concurrent calls | `NatsSagaCommandsAdapterTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` | yes |
| **34** | the Gateway client injects the real trace id | **none** — see D5 | **no** |
| **36–37** | each consumer continues the inbound Kafka trace | **none** for Projector or Notifications — see D5 | **no** |
| **44** | a real request produces a real server span | **none** — see D5 | **no** |
| **45–46** | problem-json carries the real trace id; omits it when none | **none** — see D5 | **no** |
| 48–49 | malformed-envelope log carries **the real inbound** trace id; omits when no header | `Orders.IntegrationTests/LogCorrelationTests` › `R58_OR7_EveryRecordProducedWhileHandlingAFactCarriesTheSameCorrelationIdAndTheSameTraceId` asserts **one non-empty distinct** `TraceId` (`:97-99`). The poison is produced with no `traceparent` (`:71`), so nothing is compared to an inbound id; `R58_OR7_OmitsTheTraceFieldsEntirely…` (`:133-138`) | **partial** |
| 61–62 | request latency on success and error | `RequestLatencyMiddlewareTests` › `RecordsOnSuccess_TaggedByTheRequestPath`, `RecordsOnTheErrorPathToo_BeforeRethrowing` | yes |
| 63–64 | fact-processing latency, success and exhausted path | `FactRetryDispatcherTests` › `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer`, `…_RecordedOnTheExhaustedRetryDlqPathToo` | yes |
| **65–66** | exact duration and outcome; completed vs cancelled **by attribute** | only `outcome=completed` is ever asserted — see D5 | **no** |
| **74–78** | completing records one; **direct cancel records one**; **compensation cancel records one, not two**; non-closing nothing; ignored nothing | `SagaFactHandlerTests` › `OtcSagaCompletionMs_ARealCompletingTransition_RecordsExactlyOneCompletionTaggedCompleted`, `…_ANonClosingStep_RecordsNothing`, `…_AnIgnoredFact_RecordsNothing` — **3 of 5** | **no** |
| 79–93 | per-probe up/down, including NATS *"down immediately when closed, without calling rtt()"* and Kafka *"always disconnects"* (#7 `apps/orders/src/infrastructure/health/health-checks.spec.ts:26`, `:39`, `:73`) | `HealthProbesTests` ×6 against paused containers, plus `HealthCheckAggregationTests` ×6 | acceptable as "not applicable by shape": #8's `NatsHealthCheck` has no closed-state branch (one bounded `PingAsync`). **The walk must say so** rather than *"confirmed, not re-opened"* |
| 94–108 | `live()` always up; `ready()` all up; `503` naming any single down check | `HealthCheckAggregationTests` ×6 › `Live_Always200Up_IndependentOfEveryCheck`, `Ready_200_WhenAllChecksAreUp`, `Ready_503_NamingOnlyTheFailingCheck_WhenAnySingleOneIsDown` | yes |
| 119–126 | `RI1`–`RI5` | `OrdersCreateIdempotentReplayTests` ×5, `PlaceOrderRequestIdReplayTests` ×7 (names in round 1's `R62` row) | yes |
| 127–128 | outbox-relay parity | `OutboxRelayParityTests` ×3 | yes |

**Of 19 row groups sampled, 13 hold, 1 is partial and 5 do not.** That is too many for the walk to be a reconciliation. Its own row-numbering overlap (`119–128` and `127–128`, `:2536-2537`) is a smaller sign of the same unread shape.

### D4 — **CLOSED**

`tasks.md:103-108` N1–N6 are `[x]`, each pointing to the record section that delivers it. The N6 pointer is true: the transition was a single status line. The pointers are true to the sections they name; what N1's section itself still asserts is R5.

### R1 — **CLOSED**

- `:2322`'s sentence is replaced with the ten temporary arms, the two permanent test changes, and the round-3 `quality.sh` log that settles 1758.
- The L20 row at `:2345` and the L25 row at `:2350` are corrected in the rows themselves, not only in the paragraph.

### R2 — **CLOSED**, each half read in #7's checkout

- **`design.md` §8.3 (`:339`) and L26 (`:412`)** now attribute the discovery to #7's implementer. All three citations verify:
  - `order-to-cash-nestjs/progress/impl_observability_reliability.md:879` is the A8 pass heading;
  - `:912` is the NATS probe *"widened with an explicit `withTimeout(2000ms)` wrapper the Gateway's own copy does not have"*;
  - `:927` is the paused-socket mechanism;
  - `review_observability_reliability.md:70-72` is *"Two minor, already-disclosed items"*, the first being that probe.
- **L25 (`:411`).**
  - `git grep -nE '\.\.\.\((traceId|[a-zA-Z]+TraceId) \? \{ traceId' 95e883a -- apps | grep -v '\.spec\.ts:'` returns exactly the **8** file:lines the row cites (`problem-json.filter.ts:57`, `fact-retry-dispatcher.ts:167`, `outbox-relay.ts:192`, `saga-command-dispatcher.ts:158`/`:187`, `saga-command-sweeper.service.ts:101`, `saga-first-park-dead-letter-handler.ts:67`, `saga-facts.controller.ts:101`).
  - `95e883a` is *"feat(orders): requestId dedup, dead-letter, trace propagation"* (2026-08-27).
  - HEAD returns **21**: Billing 1, Fulfillment 1, Gateway 2, Notifications 5, Orders 8, Projector 4.
  - The "Phase-25" wording matches `8a3a3d3` (*"feat: Phase 25 — every requirement traced…"*).
- **The retired claim, enumerated.** `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "reviewer found"` gives four hits:
  - `tests/Cqrs.UnitTests/DispatcherScopeTests.cs:9` — #8's own dispatcher reviewer, a different claim; left alone, correct;
  - `tests/Orders.IntegrationTests/IdempotentConsumerTests.cs:50` — #7's reviewer on R17, a different claim; left alone, correct;
  - `tests/Billing.IntegrationTests/CreditSimulatorTests.cs:129` — #7's reviewer on R44, a different claim; left alone, correct;
  - `tests/Architecture.Tests/HealthProbeCopyParityTests.cs:52` — **new**: a quotation naming *"the retired 'its reviewer found it' wording"* as retired. Acceptable.
- **None of the four live sites still carries the claim.** A search for `found and disclosed by #7's own` returns all five `NatsHealthCheck.cs` copies at `:13`. `HealthProbeTimeoutTests.cs:8-13` and `Gateway.IntegrationTests/HealthProbesTests.cs:14-19` carry the corrected attribution in their own words.

### R3 — **CLOSED** (see D1)

### Round-1 filing list — closed or routed

- **A2, NATS copies:** closed and armed (probe 12).
- **A2, Mongo pair:** **not closed** — D7. It was routed *"closed in the fix round rather than filed onto id 68"* (`progress/current.md:91`), so it must genuinely close, or be filed.
- **A3, `FACT_RETRY_*`:** substitution closed (probe 6); the defaults half is in D5.
- **A7, degrading sender:** routed as backlog **id 73** (`feature_list.json:1023-1035`). Phase 14, `pending`, `sdd: false`. Its acceptance carries:
  - the decorator on both branches;
  - a MailKit-signal classifier proven against a real SMTP failure;
  - the ledger row;
  - #7's two spec files enumerated by assertion;
  - all three mutation families.

  Routed, not dropped.

### New regressions from the fix round's own test fixes

- **The cross-test `.dlq` collision: not weakened.** The dead-letter tests keep `OrdersFacts` (`ProjectorDeadLetterTests.cs:26`, `NotificationDeadLetterTests.cs:27`), with byte-equality and header asserts intact; probe 9 killed one. The new log cases use `ProjectorFactTopics.FulfillmentFacts` (`LogCorrelationTests.cs:32`) and `NotificationFactTopics.BillingFacts` (`:37`). The positional `ConsumeOneAsync` that caused the collision remains — advisory A8.
- **The `MetricsExposureTests` fix: not weakened** — see the rows 67–69 ruling.

## Blocking defects — round 2

### D5 — the §11 walk marked five row groups "Exists" that have no assertion

Each group below was checked by content, not by filename.

- **Rows 36–37 — consume-side Kafka trace continuation in Projector and Notifications.**
  - **Production sites:** `ProjectorFactsConsumer.cs:119-122` and `NotificationFactsConsumer.cs:149-152`.
  - **#7's guards:** `apps/projector/src/presentation/projector-facts.controller.spec.ts:181` and `apps/notifications/src/presentation/notification-facts.controller.spec.ts:177` — *"extracts the inbound Kafka header's trace context and CONTINUES it (same real traceId)"*.
  - **Search:** `find tests/Projector.UnitTests tests/Projector.IntegrationTests tests/Notifications.UnitTests tests/Notifications.IntegrationTests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -nE "traceparent|\.TraceId|ExtractKafka|callerActivity|originTraceId|inboundTrace"` → **18 lines**:
    - 14 are `KafkaDeadLetterPublisherTests` ×2 (`:21` comment, `:50`, `:51`, `:53`, `:54`, `:55`, `:69`) — the publisher, not a consumer;
    - 2 are comments (`ProjectorDeadLetterTests.cs:72`, `NotificationDeadLetterTests.cs:108`);
    - 2 are `.dlq` presence checks (`:81`, `:114`).
    - **None sends an inbound `traceparent` to a consumer.**
  - **Armed:** probes 8 and 10 — a fresh consumer root fails nothing in either Projector project. Orders' consumer is guarded (`SagaDeadLetterTests` › `R57_OR4_EveryRetryAttempt…`); the other two are not.
  - `R57`'s *"continued on consume"* is therefore proven for one Kafka consumer of three.
- **Row 34 — the Gateway client's trace injection** (`src/Gateway/Infrastructure/Messaging/NatsRpcClient.cs:39`; #7 `nats-rpc-client.adapter.spec.ts:125`).
  - **Search:** over `tests/Gateway.UnitTests` and `tests/Gateway.IntegrationTests`, `traceparent|\.TraceId|TraceId\b` → no output; `TraceContext|OtcActivity|Activity\.Current` → no output (exit 123).
  - Over those two plus `tests/Architecture.Tests`, `Exported|ActivityListener|TracerProvider|InMemoryExporter` → no output.
  - **No Gateway test can observe a trace**, so no mutation was needed to show this one is unguarded.
  - The walk's *"`NatsRpcClientIntegrationTests` … a naming variance, not a gap"* is false: its five cases, listed above, carry no trace assertion.
  - Ledger L21's guard also cites `NatsRpcClientTests`, and `grep -nE "TwoConcurrent|class NatsRpcClient"` finds no such class — R9.
- **Row 44 — a real inbound request produces a server span.**
  - The listener search above is empty.
  - `ActivityKind\.Server|AddAspNetCoreInstrumentation|Microsoft.AspNetCore.Hosting.HttpRequestIn` over `tests/` → 3 hits, all in `Orders.IntegrationTests/TraceContextPropagationTests.cs` stand-ins (`:64`, `:65`, `:300`).
  - `TelemetryWiringTests`' four cases are static — `A3a_…`, `OR4_EveryActivitySourceName…`, `OR5_NoService…Prometheus…`, `OR5_DirectoryPackagesProps…`.
- **Rows 45–46 — problem-json carries, or omits, the trace id.** `ProblemJsonCorrelationTests` has two asserts (`:62`, `:67`), both about `correlationId`, and none about a trace id.
- **Rows 65–66 and 74–78 — `otc_saga_completion_ms` by outcome.**
  - **Search:** `grep -nE "otc_saga_completion_ms|SagaCompletionMs|\"cancelled\""` over `tests/` → 18 lines. The metric appears only in `SagaFactHandlerTests.cs` (`:117` comment, `:122`, `:145`, `:164`). The other 14 are `"cancelled"` status strings in unrelated order, projection and seed tests, none touching the metric.
  - **#7's missing cases:** `saga-fact-handler-saga-completion-metrics.spec.ts:191` (*"a direct cancel … records EXACTLY ONE cancellation"*) and `:208` (*"the compensation-completing cancel … EXACTLY ONE, not two"*).
  - **Armed:** probe 11 — hard-coding the outcome to `completed` leaves all 433 Orders unit tests green.
  - `SagaFactHandlerTests.cs:117`'s doc comment says *"ported cases 74-78"*.
- **Partial — rows 48–49 and the defaults half of rows 4–5**, as ruled above.

**Why this blocks.**
- Round 1's D3 required exactly this walk, and the walk used the method `CLAUDE.md` names as the commonest disguise of a prose sweep: **classifying files when the claim is about assertions**.
- Every row above is this feature's own mechanism (`OR4`, `OR5`, `OR7`), in the ported-guard table its own design wrote.
- This is the *"port its guards"* failure in its original shape: a guard #7 wrote, found in a file named after the mechanism, and not noticed.

**What must change.**
- For each row above, add the case or reclassify it in the record with a reason a reader could check. A zero-hit search is not a reason.
- **Rows 36–37:** a consumer case per service. Inbound `traceparent` → the `consume` span, or a log/DLQ record, carries the **inbound** trace id. Armed by probe 8/10's fresh root.
- **Row 34:** a Gateway client case asserting the real active trace id on the sent headers, armed by deleting `NatsRpcClient.cs:39`.
- **Rows 44–46:** either a Gateway span/log trace case, or an explicit reclassification that the design then carries.
- **Rows 65–66/74–78:** the `cancelled` outcome, and the "one, not two" compensation case, armed by probe 11.
- **Rows 4–5:** default asserts in both roots.
- Then re-walk §11 **row by row by case name**, and correct `SagaFactHandlerTests.cs:117`.

### D6 — `x-first-failed-at` and `x-failed-at` are asserted to parse, never to be right

- **Search:** `grep -nE "FirstFailedAt|FailedAt\b"` over `tests/` → 8 lines.
  - Six are the three `KafkaDeadLetterPublisherTests`' fixed **inputs** (`2026-08-26 10:00:00` / `10:00:01`), never asserted on output.
  - Two are `OrderSagaFailureTests.cs:50`/`:96`, a different `FailedAt` (the `order.saga_failed.v1` payload).
- **The only header checks** are `DateTimeOffset.TryParse` at `SagaDeadLetterTests.cs:94-95`, `NotificationDeadLetterTests.cs:112-113` and `ProjectorDeadLetterTests.cs:79-80`.
- **Armed:** probe 10 — Projector's `x-first-failed-at` rendering `FailedAt`, a valid timestamp in the wrong field, leaves **58/58** green. Probe 9's literal fails only because it does not parse.
- **No parity family covers these publishers.** `grep -ln "KafkaDeadLetterPublisher.cs"` over `tests/` → no hits (exit 123), so each copy's rendering is guarded only by its own service's tests.
- **This is `CLAUDE.md`'s clock rule:** *"for fields the test does not control — clocks — inject the source or bracket the value, or the field is unguarded however many probes you run."*
- **My own round-1 prescription contributed.** *"Armed by P7's corruption"* named a non-parsing literal, and a parse check satisfies it. The prescription was literal-shaped; the rule was not.
- **What must change.** In each `KafkaDeadLetterPublisherTests` copy, assert all seven `x-*` header **values** against the publication the test already builds (`"2026-08-26T10:00:00.0000000+00:00"` and so on). Arm it with probe 10's swap in one copy, and one other header swap in another copy.

### D7 — the Mongo "parity" case cannot fail when the bound is removed

- **Armed:** probe 13 — deleting `cts.CancelAfter(_timeout);` from the Gateway's `MongoHealthCheck.cs` fails nothing in `Architecture.Tests`.
- **Why nothing else catches it:**
  - `EveryMongoHealthCheckCopyNamesReadModel_UsesTheSameTwoSecondBoundedTimeout_…` checks five substrings, and `CancelAfter` is not among them;
  - `HealthProbeTimeoutTests.cs:96-99` accepts **any** `TimeSpan` field;
  - `find tests/Gateway.UnitTests tests/Gateway.IntegrationTests … | xargs grep -nE "PauseAsync|UnpauseAsync"` → only `nats.PauseAsync()`/`UnpauseAsync()` (`HealthProbesTests.cs:151`, `:233`) plus the fixture's own definitions (`NatsContainerFixture.cs:44`, `:46`).
- **So no test executes the Gateway's `readModel` bound**, while a case name asserts it. Projector's copy is covered behaviourally, because Projector pauses Mongo. This is the guard-that-does-not-guard shape, in a closure that was taken *instead of* filing.
- **What must change.**
  - Assert that the bound is applied (`cts.CancelAfter(_timeout)` present in both copies), armed by probe 13.
  - For the stall property on the Gateway, either add a paused-Mongo Gateway case or **file** it explicitly; the round-1 routing depends on one of the two.

## Required record corrections — round 2

- **R4 — L25's *"in #8"* column states an engine claim probed one way.** *"Any one missing removes the field from every line"* holds for `AddJsonConsole` (the fix round's Billing arm) and `IncludeScopes` (probe 5). It holds for `ActivityTrackingOptions` only when the value is set to `None` (probe 3). **Deleting the line leaves `TraceId` on the line** (probe 4, Billing 1/1 green).
  - The likely mechanism is that `Host.CreateApplicationBuilder` configures activity tracking by default. I observed that, and did not verify it in the framework source.
  - The correction must verify the mechanism before writing it, per `CLAUDE.md`'s rule on engine claims. It must also cover `BillingHost.cs:33-35`'s comment and its five siblings, which carry the same *"all three, or a field silently vanishes"* sentence.
- **R5 — L24 and N1.**
  - Record `:2476` says *"ledger row L24's citation corrected"*, but R3 at `:2606` corrects only the cells. `design.md:410` and the record's L24 row (`:2349`) still name only the `OutboxRelay.cs` arm.
  - *"Decorative guards found: 0"* stands uncorrected at `:2318` and `:2355`, though round 1's D1 showed L24's NATS leg was decorative when it was written.
  - Name the three responder tests in L24, and annotate both `0` claims.
- **R6 — `R58`'s Status cell** (`test-matrix.md:187`) must name the four new `LogCorrelationTests`. The leader's fix-round brief scoped this cell out, and round 1 required it; **leader: include it in the next brief's scope explicitly.**
- **R7 — the addendum's #7 path** (`:2657`) must cite the canonical checkout `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` at `bf45af0`, not the unversioned copy.
- **R8 — the §11 walk's row ranges** (`:2536-2537`, `119–128` overlapping `127–128`) must match `design.md`'s `119–121`/`122–126`/`127–128`.
- **R9 — L21's guard citation** names `NatsRpcClientTests`, which does not exist. Correct it to what exists, and fold the missing Gateway case into D5.

## Advisories

- **A8 — the dead-letter consume helpers match by position.** `ConsumeOneAsync` returns the first non-EOF message instead of matching content (record `:2485`). The fix moved the new tests off the shared topic, but any future poison publisher on `OrdersFacts` in the same collection will steal the assertion again. **Destination: leader files a backlog entry** (proposed name `dlq_consume_helpers_match_by_position_not_content`), or it is closed with D6 by matching on `eventId`.
- **A9 — `HealthProbeTimeoutTests`' predicate** (`:96-99`, any `TimeSpan` field) is armed only by deleting the field. An unused field passes. **Destination: a bullet on the entry filed for A8, or its own entry.** The narrow Gateway Mongo instance is D7.
- **A10 — routing check, stated rather than assumed:** no round-2 finding has its root cause in `specs/shared/`.
  - `OR1`'s header set is local to `requirements.md:34` and already matches `asyncapi.yaml`'s `DeadLetterHeaders`.
  - Rows 34–78 are this feature's design table.
  - `R58`/`R59` are status cells, which `init.sh` §5d exempts.

  No SA-n is proposed.
- **A11 — the fix round recorded a stale-armed-binary false red and corrected it** (`:2464`). That is the protocol working. Recorded so the next arming table cites it rather than rediscovering it.

## `R<n>` → test mapping — deltas since round 1

| Req | Change | Verdict |
|---|---|---|
| **R56** mechanism | `OrdersCreateResponderTraceContinuationTests` › `D1_CatalogReferenceList…`; `StockRpcResponderTraceContinuationTests` › `D1_StockCheck…`, `D1_StockReserveStampsOutboxTraceParent…`; `BillingRpcResponderTraceContinuationTests` › `D1_CreditList…`, `D1_CreditHoldStampsOutboxTraceParent…` | NATS receive hop now executes production code — armed (probes 1, 2) |
| **R57** | the above, plus `KafkaDeadLetterPublisherTests` ×3 › `PublishAsync_WithAnActiveSpan_…`, `PublishAsync_WithNoActiveSpan_…` | NATS consume and DLQ publish are exercised; **Kafka consume exercised in Orders only — D5** |
| **R58** | `LogCorrelationTests` in Fulfillment and Billing › `R58_OR7_ARealRpcFailureLogLineCarriesTheRequestsCorrelationIdAndATraceId`; in Notifications and Projector › `R58_OR7_EveryRecordProducedWhileHandlingAPoisonFactCarriesTheSameCorrelationIdAndTheSameTraceId` | all six hosts exercised — armed (probes 3, 5); **cell not updated — R6** |
| **R59** | `MetricsExposureTests` › `OtcDlqDepth_SumsAcrossMultipleTopicsIndependently_AMissingTopicNeitherStopsNorZeroesTheOthers` | DLQ depth sound; **saga completion's `cancelled` outcome unguarded — D5** |
| **R60** | `HealthProbeCopyParityTests` +4 (NATS ×2, Mongo ×2) | NATS armed (probe 12); **Mongo case cannot fail on the bound — D7** |
| **R16/OR1** | the header asserts widened in `ProjectorDeadLetterTests`/`NotificationDeadLetterTests`; `*ProgramConfigurationTests` substitution ×2 | substitution armed (probe 6); **timestamps parse-only — D6** |

## `CHECKPOINTS.md` — round 2

**C1 — harness.**
- [x] Harness unchanged by this round. `./init.sh` exit 0 is the leader's run, not re-run by me.
- [x] `current.md` and `history.md` exist.

**C2 — state.**
- [x] None `in_progress`; id 27 `in_review`.
- [x] `feature_list.json` hunks unchanged (six).
- [x] No `blocked` feature touched.

**C3 — architecture.**
- [x] NetArchTest `Architecture.Tests` **25/25**, run whole (probe 12's confirming run).
- [x] `git diff -U0 -- 'src/*/*.csproj' | grep -c ProjectReference` → **0**.
- [x] `git status --porcelain` shows no change under `src/SharedKernel`, `src/Cqrs` or `src/Seed`.
- [x] The fix round's `src/` edits are comment-only (the five `NatsHealthCheck.cs` banners). No production behaviour changed.
- [x] Interactions unchanged.

**C4 — verification.**
- [ ] `./quality.sh` at 1780 — **not re-run.** Four projects were reconciled by whole-project runs (433 / 118 / 58 / 25).
- [x] Domain tests pure.
- [x] Integration tests hit real containers — every probe above did.
- [ ] Coverage gate *enforced* — feature 34, pre-existing.
- [x] No Jest.

**C5 — clean close.**
- [x] Probe residue: marker grep exit 123, no dotnet build/test/format process alive, backups in the scratchpad only.
- [ ] `history.md` effort entry — not closeable while rejected.
- [x] Claude did not commit: HEAD `909394f`.

**C6 — SDD.**
- [x] Spec documents exist.
- [x] N1–N6 ticked (D4).
- [ ] **Every `R<n>`/§11 row genuinely mapped** — D5, R6.
- [ ] Spec commit before implementation commit — the human's sequence.

**C7 — reuse fidelity.**
- [x] `git status --porcelain --untracked-files=all -- specs/shared infra n8n src/SharedKernel src/Cqrs src/Seed apps/web` → exactly ` M specs/shared/test-matrix.md`.
- [x] No amendment owed (A10).
- [ ] **Reused ids genuinely satisfied** — `R57` Kafka consume in two services, `R59` cancelled outcome (D5).
- [x] `n8n/` unchanged.
- [ ] Effort records — pending approval.

## What must change before re-review round 3

1. **D5** — the §11 rows 34, 36–37, 44, 45–46, 48–49, 65–66, 74–78 and the defaults half of 4–5: each gets a delivered case or a checkable reclassification. Rows 36–37, 65–66/74–78 and 34 are armed as stated. The walk is redone by case name, and `SagaFactHandlerTests.cs:117` is corrected.
2. **D6** — the timestamp and other `x-*` header values asserted in the three `KafkaDeadLetterPublisherTests`, armed by a valid-value swap.
3. **D7** — the Mongo bound asserted as applied, armed by probe 13, and the Gateway stall property either tested or filed.
4. **R4–R9** — record, ledger and cell corrections as listed. R4 must verify its mechanism before stating it.
5. Reconcile the new total against **1780** by project.

**Re-review scope, stated now so it is cheap.**
- **Re-run:** probes 8/10 (consumer fresh root), 10 (timestamp swap), 11 (outcome), 13 (Mongo), plus one arm per new guard.
- **Read:** the redone §11 walk by case name, and R4's mechanism citation.
- **Not repeated:** probes 1–7, 9 and 12, whose files will not change.

## Effort to date — round 2 addition (for the eventual `history.md` entry, not an entry)

- **The fix round and its addendum ran between round 1's close and this dispatch.** Their timestamps are in the leader's transcripts, not in the record.
- **This review:** probe chain first build 00:30:24 → last confirming run 00:41:09 CEST, 2026-09-11; record written by ≈00:50.
- **Totals now:** 1 spec session, 1 gate, the 14 implementer passes counted in round 1, 1 fix round + 1 addendum, 1 suite runner, and **2 review rounds, both rejected**. #7's counterpart was approved on its first review (`order-to-cash-nestjs/progress/history.md:1057`).

## Round 3

**Verdict: REJECTED.** Status left at `in_review`. `feature_list.json` not edited: `git diff -U0 -- feature_list.json | grep '^@@'` still returns the leader's six hunks (`-406`, `-408`, `-415`, `-468`, `-998`, `-1000`), and line 406 still reads `"status": "in_review"`.

**Where this stands.** Every round-2 finding I probed by mutation is closed on value: rows 34, 36–37 and 65–66/74–78, D6's publisher and dispatcher halves, and D7. So are R5–R8, and A8 is acceptably routed. But three guards this feature's own record calls armed do not catch the defect they are named for:
- **The fact-processing latency value is guarded nowhere.** It is recorded in Stopwatch ticks with every suite green, while #7 asserted exact durations.
- **L21's concurrency guards cannot see a hoisted `NatsHeaders`** at either integration site. The implementer's arm put the collision into the mutation, not into the test.
- **Row 44's server span exists because the test's own listener subscribes.** Deleting the host's `AddAspNetCoreInstrumentation()` fails nothing, while the test's comment says that registration produces the span.

R4 is also only half closed: the retired sentence survives in all six `*Host.cs` files. **Recommendation:** one more bounded fix round on D8–D11. It touches one production mechanism (the dispatcher's time source, across three parity copies); the rest is test and comment work. No gate, no amendment.

### Scope — what I ran, and what I did not

- **Not re-run:** `./quality.sh` or `./init.sh`. No claim under test was about the whole suite, so the 1799 figure is the implementer's and the leader's, not mine.
- **Run as named tests only**, under mutation, in six project builds. Each build is one set below.
- **Protocol on every probe.**
  - Setup: `cp -p` backup → exact-once replacement (the script asserts the match count is 1) → `dotnet build <project> --no-incremental` → run → record verbatim.
  - Restore: `cp` restore → `cmp` → `touch` → forced rebuild → confirming run.
  - SET B1's restore was confirmed by SET B2's confirming run: the next forced rebuild includes the restored files, and all three tests passed.
- **One build at a time.** Two background scripts ran strictly in sequence (PIDs 2506642, then 2562934). Each was waited on with `kill -0 <pid>`, doing only read-only work meanwhile. The script's `guard()` ran `pgrep -a dotnet | grep -E "dotnet (build|test|format)"` before every build and test, and never printed.
- **Final state.**
  - `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "REVIEW-PROBE"` → exit 123, no output.
  - All 19 backups in the session scratchpad `cmp` identical to their files, including three round-2 backups.
  - No dotnet build/test/format process alive (`pgrep` exit 1). HEAD is still `909394f`.

### Probes

| # | Closure or claim | Mutation | Test(s) | Verbatim result | Restore / confirm |
|---|---|---|---|---|---|
| A1 | D5 rows 65–66/74–78 (my round-2 probe 11) | `SagaFactHandler.cs:118` → `var outcomeTag = "completed";` | `SagaFactHandlerTests` › `OtcSagaCompletionMs_ADirectCancel_RecordsExactlyOneCancellationTaggedCancelled`, `…_TheCompensationCompletingCancel_RecordsExactlyOneNotTwo` | **Both fail:** `Assert.Equal() Failure: Strings differ / Expected: "cancelled" / Actual: "completed"` (`:220`, `:261`) | `cmp` identical; forced rebuild; **5/5** green (the A set's filter) |
| A2 | D6, dispatcher capture | Orders `FactRetryDispatcher.cs:93` `??=` → `=` | `FactRetryDispatcherTests` › `DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsClockReading_NeverALaterOne` | **Fails:** `Assert.Equal() Failure: Values differ / Expected: 2026-09-11T10:00:00.0000000+00:00 / Actual: 2026-09-11T10:00:02.0000000+00:00` (`:201`), naming the instant | as A1 |
| A3 | D6, publisher value — the **Orders** copy, which the fix round did not arm | Orders `KafkaDeadLetterPublisher.cs:56`: `x-first-failed-at` renders `publication.FailedAt` | `KafkaDeadLetterPublisherTests` › `PublishAsync_RendersEveryDeadLetterHeaderValue_NotJustWhetherItParses` | **Fails:** `Assert.Equal() Failure: Strings differ (pos 18) / Expected: "2026-08-26T10:00:00.0000000+00:00" / Actual: "2026-08-26T10:00:01.0000000+00:00"` (`:100`) | as A1 |
| A4 | Rows 63–64, latency **value**, unit level | Orders `FactRetryDispatcher.cs:80`: `stopwatch.Elapsed.TotalMilliseconds` → `stopwatch.ElapsedTicks` | `FactRetryDispatcherTests` › `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer` | **Passed** — the A set's run was `Failed: 4, Passed: 1`, and this is the one that passed. **D8** | as A1 |
| B1a | Row 72, consumer tag — **substitution** of the real sibling rendering | Orders `FactRetryDispatcher.cs:80`: `consumer.ToString()` → `ConsumerNames.ToToken(consumer)` | `RealInfraMetricsProvenanceTests` › `OtcFactProcessingLatencyMs_RecordedFromARealKafkaDeliveredFact_TaggedByTheRealConsumer`, run **twice** | **Fails both runs:** `Assert.Contains() Failure: Filter not matched in collection / Collection: [Tuple (99.5872, [["consumer"] = "orders.saga"])]` (`:82`) | `cmp` identical; confirmed by B2's confirming run |
| B1b | Row 73, duration — a plausible wrong **start** timestamp | `SagaFactHandler.cs:119`: `clock.UtcNow - order.OrderDate` → `clock.UtcNow - fact.OccurredAt` | `RealInfraMetricsProvenanceTests` › `OtcSagaCompletionMs_RecordedFromRealWallClockTimestamps_ADirectCancelViaStockRejected`, same two runs | **Passed both runs** (`Failed: 1, Passed: 1` ×2) — see A12 | as B1a |
| B2a | Row 72, latency **value**, integration level | as A4 | the row-72 case | **Passed 1/1** — **D8** | `cmp` identical; forced rebuild; **3/3** green (both `RealInfraMetricsProvenanceTests` + the stock-checker case) |
| B2b | L21, stock-checker site — the defect **as named**, with no widening | `NatsStockAvailabilityChecker.cs:33` → `var headers = _reviewProbeSharedHeaders;` plus a `private readonly NatsHeaders _reviewProbeSharedHeaders = new();` field | `NatsStockAvailabilityCheckerTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`, run **three times** | **Passed 3/3** (384 ms, 607 ms, 322 ms) — **D9** | as B2a |
| C1 | D5 row 34 (Gateway) | `NatsRpcClient.cs:39` — `TraceContext.InjectNats(headers);` deleted | `NatsRpcClientIntegrationTests` › `D5_Row34_InjectsTheActiveTraceIdIntoTheOutboundRequestHeaders` | **Fails:** `Assert.False() Failure / Expected: False / Actual: True` (`:154`, empty `traceparent`). With injection present, `:155` also asserts `Assert.Equal(callActivity.Id, traceparent)`, so value is checked too | `cmp` identical; forced rebuild; **3/3** green (the C set) |
| C2 | D7 (my round-2 probe 13) | Gateway `MongoHealthCheck.cs:18` — `cts.CancelAfter(_timeout);` deleted | `HealthProbesTests` › `R60_OR6_ReportsReadModelDownWithinItsBound_WhileMongoIsPaused_AndRecoversWhenItReturns` | **Fails [9 s]:** `/health/ready took 8010ms while Mongo was paused, exceeding its 6s bound (design.md §8.3: 2 checks x 2s + 2s margin).` | as C1 |
| C3 | Row 44, "a real inbound request produces a real server span" | Gateway `Telemetry.cs:68` — `.AddAspNetCoreInstrumentation()` deleted | `LogCorrelationTests` › `Row44To45_ARealHttpRequestProducesARealServerSpan_AndTheFailureLogLineCarriesTheSameTraceId` | **Passed** (the C set: `Failed: 2, Passed: 1`, and this is the one that passed) — **D10** | as C1 |
| D1 | D5 rows 36–37, Projector (my round-2 probes 8/10) | `ProjectorFactsConsumer.cs:121` — `parentContext: parent` dropped (fresh root) | `ProjectorDeadLetterTests` › `OR4_R57_TheConsumeSpanContinuesTheInboundKafkaTrace_TheDlqCopyCarriesTheSameTraceIdAsTheInboundFact` | **Fails [8 s]:** `Assert.Equal() Failure: Values differ / Expected: 919aa3a06c0c7c37cfdaf0d7b1ea3f83 / Actual: 7f671e723d3b1635283f8ae3a124a9ec` (`:177`) — on the trace **value** | `cmp` identical; forced rebuild; **1/1** green |
| E1 | Row 73 — is B1b's survivor caught anywhere? | as B1b, built into `Orders.UnitTests` | `SagaFactHandlerTests` › `OtcSagaCompletionMs_*` (5) | **3 fail on value:** `…_ADirectCancel…` `Expected: 00:11:00 / Actual: 00:06:00`; `…_ARealCompletingTransition…` `Expected: 00:37:00 / Actual: 00:32:00`; `…_TheCompensationCompletingCancel…` `Expected: 00:19:00 / Actual: 00:14:00`. The two "records nothing" cases pass, correctly | `cmp` identical; forced rebuild; **5/5** green |
| F1 | L21, Gateway site — the defect as named, no widening | `NatsRpcClient.cs:31-35` → the same shared `_reviewProbeSharedHeaders` field, keys assigned by indexer | `NatsRpcClientIntegrationTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`, run **three times** | **Passed 3/3** (325 ms, 338 ms, 355 ms) — **D9** | `cmp` identical; forced rebuild; **1/1** green |

**Why A, B1, B2 and C share builds.**
- Each combined mutation targets a different line and a different named test. Each failure message names its own test and asserts that mutation's own value.
- Where one line needed two different mutations (`FactRetryDispatcher.cs:80` in A4 → B1a → B2a), each was applied from the single pristine backup, in a separate build.

## Closure, finding by finding — round 3

### D5 — **CLOSED** for every row I armed

- **Rows 34, 36 and 65–66/74–78 kill on value** (C1, D1, A1).
  - Row 37 (Notifications) is the same shape as D1, and the fix round armed it with a value mismatch (record `:2689`). Not re-armed by me.
  - Rows 48–49 and the rows 4–5 defaults were armed by the fix round (`:2685`, `:2693`) and not re-armed by me.
- **`SagaFactHandlerTests.cs:117` is corrected.** Its comment now names all five methods.
- **Rows 44–46 are not closed:** row 44's server-span half is D10.

### D6 — **CLOSED**

- **Unit value guards on every publisher copy.**
  - Orders: armed by me (A3).
  - Projector and Notifications: armed by the fix round with a timestamp swap and an `x-attempts` swap (`:2712-2713`).
- **The dispatcher's first-failure capture is guarded in the canonical** (A2). The other two copies are covered by `FactRetryDispatcherParityTests`, armed on the Projector copy by the fix round (`:2904-2909`).
- **Ruling on the remaining `DateTimeOffset.TryParse`-only integration asserts** (`SagaDeadLetterTests.cs:94-95`, `NotificationDeadLetterTests.cs:112-113`, `ProjectorDeadLetterTests.cs:80-81`): **acceptable.**
  - Value provenance now has two armed unit layers: the capture, and the rendering in every copy.
  - The only integration-only property left is that each host registers a real `IClock`. A frozen clock would still produce parseable timestamps, but it would also break every other clock-stamped behaviour in the host.
  - Recorded, not required.

### D7 — **CLOSED**

C2 fails naming the bound. The fix round also recorded the Architecture content assertion failing under the same deletion (`:2725`); I did not re-arm it.

**A9 — the 14-row table checked against the files.**
- **Parity canonicals.** `HealthProbeCopyParityTests.cs:37`, `:46` and `:66` make Orders the canonical of all three byte-parity families: MS-SQL ×4 (`:29-35`), Kafka ×3 (`:39-44`) and NATS ×5.
- **Paused containers per service.** `grep -n "PauseAsync"` over the six `HealthProbesTests.cs` files gives:
  - Orders: `nats` (`:175`);
  - Fulfillment: `mssql` (`:168`);
  - Billing: `nats` (`:169`);
  - Notifications: `kafka` (`:169`);
  - Projector: `mongo` (`:168`);
  - Gateway: `nats` (`:151`) and `mongo` (`:269`).
- Each has an elapsed-time assertion against `_pausedReadinessBound`.
- **Every "own" and "byte-parity" cell in record `:2734-2747` matches.** The claim holds.

### R4 — **NOT CLOSED** → D11

**The design cell is honest.**
- L25 (`design.md:411`) says `ActivityTrackingOptions` is on by default: deleting the explicit setting leaves `TraceId` on every line, while `None` removes it.
- It is marked *"measured by the reviewer's deletion probe, not read from framework source."* That matches what my round-2 probes 3–4 measured, and it does not pretend to more. I did not verify the framework mechanism either.
- The only generalisation beyond the measurement is "in the generic host", from one host (Billing). It is labelled as measured, so it is acceptable as written.

**But round 2's R4 also required the six `*Host.cs` comments, and they still carry the retired claim** — see D11.

### R5 — **CLOSED**

- `:2318` and `:2355` read *"Decorative guards found: 1 (L24…)"*.
- The L24 row at `:2349` names the three responder tests by literal case.
- `design.md:410` names them too.

### R6 — **CLOSED**

`test-matrix.md:187` cites `LogCorrelationTests` in all six services by literal case, plus `ProblemJsonCorrelationTests`.

### R7 — **CLOSED**

`grep -n "/media/"` over the record returns two lines. `:2657` now cites the canonical checkout at `bf45af0`, and `:2761` is the correction quoting the retired path.

### R8 — **CLOSED in the live walk**, with one record residue (A14)

The part-2 walk (`:2832-2872`) uses `119–121`, `122–126` and `127–128`, and the leader's `comm` over first cells is empty both ways.

### R9 — **citation CLOSED; the guard it names is hollow** → D9

### A8 — **ACCEPTABLE AS ROUTED**

Backlog **id 74** (`dead_letter_tests_select_their_message_by_content`) is phase 14, `pending`, and joins the 67–70 loop. Its acceptance:
- enumerates the class repository-wide first, as a search result;
- requires content selection by an id the test controls;
- is armed by a decoy on the same topic, a change of kind, per site.

The two new round-2 cases already select by `correlationId` (`ProjectorDeadLetterTests.cs:277-315`).

### Rows 70–73 — the part-2 closure

- **Row 72, tag: sound.** B1a is a substitution of the real sibling rendering (`ToToken` → `orders.saga`), not a literal, and it kills.
- **Row 72, value: not guarded** — D8.
- **Row 73, the "real bound": not tight enough to catch a wrong but plausible value on its own.**
  - B1b measured from the closing fact's own `OccurredAt` and stayed inside `[beforePublish − orderDate, afterObserved − orderDate]` twice.
  - The record's own arm shows why: the window was `[278.8 ms, 6069.6 ms]` (`:2953`), about 5.8 s wide, dominated by the status poll.
  - The value **is** guarded, one layer down. E1 shows the exact FakeClock assertions reject the substitution in all three recording cases, because the unit fixture's fact timestamp (`OrderTestData.Now.AddMinutes(5)`, `SagaFactHandlerTests.cs:361`) differs from `OrderDate` (`Now`).
  - **Ruling: acceptable as a layered guard.** The integration case proves host-clock and persistence provenance, where the plausible failures are hours-scale or zero-scale and the bound catches them. The unit case proves the formula.
  - The integration case's comment overclaims (A12).

### The part-2 concurrency fix — **does not weaken the guard; leaves the class open** → A13

- **Nothing was serialised.**
  - `find tests -name xunit.runner.json …` finds none.
  - `grep -rn "CollectionBehavior\|DisableTestParallelization" tests --include=*.cs` (bin/obj excluded by path) returns only the pre-existing `Gateway.IntegrationTests/AssemblyBehavior.cs:36` and a comment quoting it.
  - `Orders.IntegrationTests` still runs its collections in parallel.
- **Filtering by tag or by bound kept the uniform-corruption kills.** Mine is B1a; the record's re-arms are at `:2952-2953`. The mutation lives in the one shared compiled assembly, so every concurrent recorder carries it.
- **What the fix did not do** is make the capture safe for the concurrency its own comment documents, or look for the same shape elsewhere — A13.

### §11 — a fresh sample of 16 row groups, none in my round-2 sample

Method names enumerated with `grep -n "public (async Task|void)"` over each named file.

| §11 row | #7's claim | Delivered #8 case (literal) | Holds? |
|---|---|---|---|
| 8–9 | Phase-12 incident; generic-throw variant | `SagaDeadLetterTests.cs:30` › `OR1_R16_APoisonFactIsRetriedThenDeadLetteredWithItsDiagnosticHeaders_AndTheCommittedOffsetAdvancesSoTheNextDistinctFactOnTheSamePartitionStillProcesses`; the generic throw through the **real** dispatcher is `SagaFactsConsumerTests.cs:185` › `OR1_TheDispatchGenuinelyGoesThroughTheInjectedRetryDispatcher_NotAroundIt` (a `ThrowingDispatcher`) | yes. The design cell says "both #8 cases", and one integration case plus one real-dispatcher unit case is a fair reading |
| 10 | Projector poison | `ProjectorDeadLetterTests.cs:33` › `OR1_R16_…` (same name) — asserts bytes, five header values, committed offset, next fact processes | yes |
| 11 | Notifications poison | `NotificationDeadLetterTests.cs:35` › `OR1_R16_…` | yes |
| 17 | first park: one `order.saga_failed.v1` + one `.dlq`, neither repeated on a second park, SO5 unchanged | `SagaCommandDeadLetterTests.cs:27` › `R29_OR3_OnFirstParkAppendsExactlyOneOrderSagaFailedFact…WhileSO5sRetryScheduleIsUnchanged`. Asserts outbox count 1 (`:84`) and still 1 after the second park (`:152`), `.dlq` bytes equal (`:98`), no second `.dlq` (`:155`), `dead_lettered_at` unmoved (`:145`) | yes |
| 20 | event-type pattern admits `saga_failed` | not applicable — `FactCatalog.cs:33` `["order.saga_failed.v1"] = typeof(OrderSagaFailedPayload)` | yes (n/a) |
| 29–31 | NATS continuation; Kafka chain; no span → no header | `TraceContextPropagationTests.cs:46` › `R57_OR4_NatsRpc_…`, `:117` › `R57_OR4_KafkaFacts_…`, `:212` › `R57_OR4_AWriteWithNoActiveSpanProducesNoTraceparentHeaderAtAll`. The last asserts `Assert.Null(row.TraceParent)` and `DoesNotContain(… "traceparent")` | yes |
| 32 | retries and DLQ share the inbound trace | `SagaDeadLetterTests.cs:135` › `R57_OR4_EveryRetryAttemptAndTheEventualDlqPublishObserveTheSameTraceIdAsTheInboundFact` | yes |
| 35 | responder continues the trace | `OrdersCreateResponderTraceContinuationTests.cs:40`, `StockRpcResponderTraceContinuationTests.cs:39`/`:80`, `BillingRpcResponderTraceContinuationTests.cs:34`/`:72` | yes (armed in round 2) |
| 38–40 | through the dispatcher; generic failure reaches it; unknown type swallowed inside | `ProjectorFactsConsumerTests.cs:130` › `OR1_TheUnknownFactTypeIgnoreIsSwallowedInsideProcess_NeverReachingTheRetryPath`; 38–39 via row 10's case | yes — the integration route is behavioural (no `.dlq` copy without the dispatcher). The missing Projector unit case is disclosed at `:2850` |
| 41–42 | the same for Notifications | row 11's case | yes, same reasoning |
| 43 | Notifications: no RPC responder, no producer except the DLQ adapter | `FactPublisherConfinementTests.cs:77` › `OnlyTheOutboxAdapterMayReferenceTheFactStreamProducerClient` covers producers. The responder half is **structural**: `grep -n "NATS\|Nats" src/Notifications/*.csproj` exits 1, and `find src/Notifications … | xargs grep -ln "NATS.Client\|INatsConnection"` exits 123. No NATS reference exists to build one | yes |
| 47 | reuses the request-scoped `correlationId` | `ProblemJsonCorrelationTests.cs:22` › `R58_OR7_TheProblemBodyAndItsOwnLogLineCarryTheSameCorrelationIdAsTheRequestThatFailed` | yes (A3g arm) |
| 50–58 | per-site trace logging, omission half ported explicitly | one case per host: `Orders…/LogCorrelationTests.cs:47`, `Fulfillment…:31`, `Billing…:31`, `Notifications…:41`, `Projector…:36`, `Gateway…:45`. Omission: `Orders…:178`, `Gateway…:122`. #7's nine sites are all in `apps/orders` and `apps/gateway`, so omission cases in those two hosts cover the ported set | yes |
| 59–60 | identical trace on two failure lines; no span → none | `Orders…/LogCorrelationTests.cs:47`, `:178` | yes |
| 70–73 | lag; broker-counted depth; latency from a real delivered fact; completion between two real timestamps | `MetricsExposureTests.cs:24`, `:83`; `RealInfraMetricsProvenanceTests.cs:37`, `:106` | **70–71, 73: yes. 72: no on value** (D8) |
| 109–118 | paused real containers ×6 | `R60_OR6_ReportsReadyWhileEveryDependencyIsReachable_ThenNotReadyNamingOnlyTheStoppedDependency_WhileLivenessAnswers200Throughout_AndRecoversWhenItReturns` at Orders `:131`, Fulfillment `:129`, Billing `:129`, Notifications `:130`, Projector `:130`, Gateway `:133` | yes |

**Of 16 row groups sampled, 15 hold and 1 does not (72, on value).**
- **Rows 63–64, from my own round-2 sample, also do not hold on value.** I marked them "yes" in round 2 by case name, without reading the assertion — the same unit error my round-2 D5 found in the walk, one level down.
- This supports the leader's proposed `CLAUDE.md` amendment (`progress/current.md`, *"Proposed `CLAUDE.md` amendment"*): here the unit was the **assertion**, not the case.

## Blocking defects — round 3

### D8 — the fact-processing latency VALUE is guarded nowhere; #7 asserted it exactly

**Evidence.**
- **Probes A4 and B2a.** Recording `stopwatch.ElapsedTicks` at `src/Orders/Infrastructure/Messaging/FactRetryDispatcher.cs:80` left both guards green:
  - the unit case `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer`;
  - the integration case `OtcFactProcessingLatencyMs_RecordedFromARealKafkaDeliveredFact_TaggedByTheRealConsumer`.
- **The only value assertions** are `Assert.True(measurement.Value >= 0)` at `FactRetryDispatcherTests.cs:97` and `:114`, and `m.Value >= 0` at `RealInfraMetricsProvenanceTests.cs:83`.
- **Search.** `find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "MetricCapture.ForInstrument"` returns **14 lines**:
  - `FactRetryDispatcherTests.cs:93`, `:110`;
  - `RealInfraMetricsProvenanceTests.cs:42`, `:122`;
  - `MetricsExposureTests.cs:58`, `:70`, `:115`, `:131`, `:183`;
  - `RequestLatencyMiddlewareTests.cs:21`, `:37`;
  - the three `MetricCapture.cs` factory definitions.

  **`otc_fact_processing_latency_ms` is captured only in the first three**, and none asserts more than `>= 0`.
- **The ported guard that was dropped.**
  - #7 records latency from its injected `Clock`, with the reason written at the site. `apps/orders/src/infrastructure/messaging/fact-retry-dispatcher.ts:130-135` reads: *"Uses the injected `Clock`, not a bare `Date.now()`, so a unit test's fake clock controls the recorded value exactly."*
  - Its unit spec asserts `expect(histogram.sum).toBe(240)` (`fact-retry-dispatcher-metrics.spec.ts:63`) and `.toBe(5750)` (`:89`).
  - Its integration spec bounds the real value `toBeLessThan(30_000)` (`metrics-exposure.integration.spec.ts:197`).
- **#8 replaced the clock with `Stopwatch.StartNew()`** (`FactRetryDispatcher.cs:69`), which no test can drive. There is **no ledger row** for it, although the property *"the latency value is exact"* was supplied by #7's architecture and must now be hand-built.
- **The helper's own documentation contradicts its callers.** `tests/Orders.UnitTests/MetricCapture.cs:7-9` says it exists for *"the exact-value/exact-tag discipline design.md §7 requires ('a greater than zero assertion proves nothing here')"*.

**Why this blocks.** It is the phase-13 shape `CLAUDE.md` names in *"When you port a mechanism, port its guards"*: a guard #7 wrote, in a file named after the mechanism, dropped in translation. It is also a ledger miss on a mechanism whose #7 source states the reason. Nothing found it:
- the §11 walk marked 63–64 and 72 "verified";
- my own round-2 sample marked 63–64 "yes";
- only a corruption probe did. A deletion of the `Record` call would have killed, so a green deletion arm says nothing about the value.

**What must change.**
- **Supply the dispatcher's latency from a source a test can drive**, identically in all three copies so `FactRetryDispatcherParityTests` still holds. Either the injected `IClock` #7 used, or `TimeProvider`.
- **Assert exact durations** in the success and exhausted-path unit cases, as #7's `:63`/`:89` did.
- **Give the row-72 integration case a real upper bound.** At minimum #7's `< 30_000`; a bracket of the test's own observed window is better.
- **Add the ledger row.** History half: *#7 relied on `fact-retry-dispatcher.ts:130-142`'s injected `Clock`*. Name its guard.
- **Arm by the ticks mutation** (A4/B2a), by a seconds-for-milliseconds unit swap, and by deleting the `Record` call.

### D9 — L21's two integration concurrency guards cannot detect a hoisted `NatsHeaders`

**Evidence.**
- **Probes B2b and F1.** The defect L21 names — a `NatsHeaders` hoisted onto a shared field, with no other change — left both named guards green **3/3 each**:
  - `NatsStockAvailabilityCheckerTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`;
  - `NatsRpcClientIntegrationTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`.
- **Why.** Each test starts call A, then `await Task.Delay(50)` before starting call B (`NatsStockAvailabilityCheckerTests.cs:112`, `NatsRpcClientIntegrationTests.cs:200`). A real connection serialises headers when the request is sent, so A's build-to-send window closes long before B builds anything. **The test guarantees the overlap it needs never occurs.**
- **The fix round's arm** (record `:2767`) added `await Task.Delay(200)` **inside the mutation**, between building the shared headers and sending them. That widening makes the defect collide. The test does not.

**Ruling on the `Task.Delay(200ms)` widening: not a legitimate change of kind.**
- `CLAUDE.md:285`'s change of kind is *"delay the dependency by a controlled interval"*: a condition the **test** creates, under which the defective code loses every time and the correct code wins every time.
- A delay added to the mutation is a second defect stacked on the first. It proves only that the assertion can read a collision, never that the test produces one.
- The Orders unit adapter case (`NatsSagaCommandsAdapterTests`, A3c, failed 5/5) is genuine, because its fake connection captures the header reference. The two integration sites have no such seam.

**Also a citation that does not exist.** Both test comments, and record `:2767`, quote `CLAUDE.md` as saying *"make the collision deterministic rather than recording a probability"*.
- `grep -n "rather than recording a probability\|make the collision deterministic" CLAUDE.md` → exit 1, no output.
- Sites quoting it (`find tests src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "rather than recording a probability"`):
  - `NatsStockAvailabilityCheckerTests.cs:87`;
  - `NatsRpcClientIntegrationTests.cs:174`;
  - plus record `:2767`.

**What must change.**
- **Make each test create the overlap itself**, over the real connection. For example, a thin `INatsConnection` decorator that holds call A inside `RequestAsync` until call B has entered it, so an un-widened hoist loses every time.
- **Arm each site with the plain hoist**, B2b/F1's exact shape, with no delay in the mutation.
- **Correct the two comments and `:2767`.** L21's Guard cell (`design.md:407`, *"each armed by hoisting `NatsHeaders` onto a shared field"*) becomes true only once this is done. Until then it is a hollow guard half, the failure `CLAUDE.md`'s *"Both halves of a row are claims"* names.

### D10 — row 44's server span exists because of the test's own listener; the registration it credits is unguarded

**Evidence.**
- **Probe C3.** Deleting `.AddAspNetCoreInstrumentation()` at `src/Gateway/Infrastructure/Observability/Telemetry.cs:68` left `Row44To45_ARealHttpRequestProducesARealServerSpan_AndTheFailureLogLineCarriesTheSameTraceId` green.
- **Why.** The test registers its own `ActivityListener` on `"Microsoft.AspNetCore"` (`LogCorrelationTests.cs:56-62`). ASP.NET Core's hosting layer then starts the request activity for that listener, whatever the host's `TracerProvider` registers.
- **Three places claim otherwise:**
  - the test's comment at `:33-38` (*"`Microsoft.AspNetCore.Hosting`, registered by `AddAspNetCoreInstrumentation()` in `Telemetry.cs`"*);
  - `design.md:465`'s row 44 (*"`TelemetryWiringTests` asserts the ASP.NET Core instrumentation is registered and a real request produces a server span"*);
  - the part-2 walk (`:2853`, *"not a gap"*).
- **Search.** `find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "AddAspNetCoreInstrumentation"` returns **one line**: `LogCorrelationTests.cs:36`, that comment. Nothing asserts the registration.
- **Parity context, from #7's checkout.** #7's `apps/gateway/src/infrastructure/observability/http-instrumentation.spec.ts:30-38` builds its **own** `NodeTracerProvider` and `HttpInstrumentation` in the test, not production `tracing.ts`. So #7 did not guard its registration either. **This is not a lost #7 guard.** It is a false mechanism claim in #8's test comment and design cell, on the property L20 exists for: *"a span created is a span exported"*.

**What must change.** Either option closes it:
- **(a) add a guard that fails under C3** — for example, the Gateway host with an in-memory exporter and **no** test-owned listener, asserting an exported `ActivityKind.Server` span; or a `TelemetryWiringTests` assertion over the Gateway's tracer registration, armed by C3; or
- **(b) correct the comment, the design cell and the walk** to say the registration is unguarded, citing #7's spec lines above for parity.

The name `Row44To45_ARealHttpRequestProducesARealServerSpan…` is true of the framework under either option.

### D11 — R4's retired sentence survives in all six hosts

- **Search.** `find src -name '*Host.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "silently vanishes"` returns **six lines**. All six read *"ActivityTrackingOptions, all three, or a field silently vanishes"*:
  - `src/Notifications/NotificationsHost.cs:34`;
  - `src/Billing/BillingHost.cs:34`;
  - `src/Gateway/GatewayHost.cs:39`;
  - `src/Fulfillment/FulfillmentHost.cs:34`;
  - `src/Projector/ProjectorHost.cs:34`;
  - `src/Orders/OrdersHost.cs:50`.
- **Round 2's R4 named these explicitly**: *"It must also cover `BillingHost.cs:33-35`'s comment and its five siblings"*.
- **The claim is disproved for `ActivityTrackingOptions`** by my round-2 probe 4: line deleted, `TraceId` still present.
- **Why it blocks.** It is the *"enumerate on the wording of the claim being retired"* failure. The design cell was corrected, while the six production files a reader actually opens still assert the old mechanism.
- **What must change:** reword all six in line with L25 (`design.md:411`), and re-run the search above to zero.

## Required record corrections and advisories — round 3

- **A12 — row 73's integration comment overclaims.**
  - `RealInfraMetricsProvenanceTests.cs:147-150` says a corrupted value *"collapses toward 0ms"*, and `:164-168` says the bound is *"never a value this test computed FOR the production code"*.
  - B1b shows a plausible wrong start timestamp passes the bound. E1 shows the exact unit cases are what reject it.
  - **Destination: the fix round (the file is touched for D8 anyway).** Name `SagaFactHandlerTests`' three exact cases as the value guard. Optionally publish the closing fact with an `occurredAt` far in the past, so the substitution falls outside the bound.
- **A13 — the metric-capture concurrency class, fixed at one instance.**
  - **The capture is not thread-safe.** All three `MetricCapture.cs` copies store measurements in a plain `List<>` from `MeterListener` callbacks (Orders.UnitTests `:16-17`, `:28-29`; Orders.IntegrationTests `:14-15`, `:26-27`; Gateway.UnitTests `:14`, `:25`).
  - **The case that now polls while other writers exist** is `RealInfraMetricsProvenanceTests.cs:77` and `:152`: they enumerate `capture.Measurements` while, as their own comment says, hosts in other collections record on the same meter. Enumerating a `List<>` while another thread adds is unsafe.
  - **The exclusivity shape that caused the part-2 failure** remains elsewhere, found by `find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "Assert.Single(.*Measurements"` — **9 lines**, each classified:
    - `Orders.UnitTests/FactRetryDispatcherTests.cs:96`, `:113` — exposed if a parallel class drives a real dispatcher; `SagaFactsConsumerTests` builds one (`BuildRealFactRetryDispatcher`, `:190`).
    - `Orders.IntegrationTests/MetricsExposureTests.cs:65`, `:77` (`otc_outbox_lag_ms`) — exposed to any concurrently running Orders host's relay loop.
    - `MetricsExposureTests.cs:118`, `:138`, `:186` (`otc_dlq_depth`) — exposed to a concurrent host's depth gauge.
    - `Gateway.UnitTests/RequestLatencyMiddlewareTests.cs:24`, `:40` — exposed only if a parallel Gateway unit class drives the real middleware. **Unverified.**
  - None has flaked in the recorded runs; all nine are latent.
  - **Destination.**
    - A thread-safe capture (a lock or a concurrent collection) goes in the fix round, since D8 touches these tests.
    - The nine-site exclusivity class goes to the **leader, as a bullet on backlog id 74**: same loop, same *"a positional or exclusive read of a shared stream"* shape, and it needs its own enumeration first.
- **A14 — the superseded §11 walk is not marked superseded.**
  - Record `:2498-2557` (the round-1-fix walk) still classifies D5's rows as "Exists" and carries the overlapping `119–128` at `:2536`.
  - `grep -n -i "supersed"` over the record after line 2440 returns nothing.
  - **Destination: the fix round.** One line at `:2498` pointing to `:2820`.
- **A15 — routing check, stated rather than assumed.**
  - No round-3 finding has its root cause in `specs/shared/`. D8 and D10 concern this feature's own design rows (§7, §11, L20), D9 is L21, and D11 is production comments.
  - No SA-n and no shared-spec backlog entry is owed.

## `R<n>` → test mapping — deltas since round 2

| Req | Change | Verdict |
|---|---|---|
| **R57** (Kafka consume) | `ProjectorDeadLetterTests` › `OR4_R57_TheConsumeSpanContinuesTheInboundKafkaTrace_TheDlqCopyCarriesTheSameTraceIdAsTheInboundFact`; `NotificationDeadLetterTests` › same name | continued on consume in all three consumers — Projector armed by me (D1), Notifications by the fix round |
| **R57/OR4** (outbound NATS) | `NatsRpcClientIntegrationTests` › `D5_Row34_InjectsTheActiveTraceIdIntoTheOutboundRequestHeaders` | injection armed (C1); **concurrency guards at two sites hollow — D9** |
| **R58** | `test-matrix.md:187` names all six hosts' cases | closed (R6) |
| **R59** | `SagaFactHandlerTests` › `OtcSagaCompletionMs_ADirectCancel…`, `…_TheCompensationCompletingCancel_RecordsExactlyOneNotTwo`; `RealInfraMetricsProvenanceTests` ×2 | completion outcome and value armed (A1, E1); latency tag armed (B1a); **latency value unguarded — D8** |
| **R60** | `HealthProbesTests` › `R60_OR6_ReportsReadModelDownWithinItsBound_WhileMongoIsPaused_AndRecoversWhenItReturns` | Gateway Mongo bound armed (C2); A9 table verified |
| **R16/OR1** | `KafkaDeadLetterPublisherTests` ×3 › `PublishAsync_RendersEveryDeadLetterHeaderValue_NotJustWhetherItParses`; `FactRetryDispatcherTests` › `DeadLetterPublication_FirstFailedAt_…` | header values and first-failure capture armed (A2, A3) |
| **R56** (first hop) | `Gateway.IntegrationTests/LogCorrelationTests` › `Row44To45_…` | proves the framework's span, **not the host's registration — D10** |

## `CHECKPOINTS.md` — round 3

**C1 — harness.**
- [x] Harness files unchanged by this round.
- [ ] `./init.sh` exit 0 — the leader's run; not re-run by me.

**C2 — state.**
- [x] None `in_progress`; id 27 `in_review`.
- [x] `feature_list.json` hunks unchanged (six).
- [x] No `blocked` feature touched.

**C3 — architecture.**
- [ ] NetArchTest — not re-run this round. No `src/` file carries a change from this round: every mutation is restored, with a `cmp` against its backup.
- [x] No new `ProjectReference`, and no shared-runtime change: both fix rounds edited tests, records, and one `test-matrix.md` cell.
- [x] Interactions unchanged.

**C4 — verification.**
- [ ] `./quality.sh` at 1799 — not re-run (no whole-suite claim was under test).
- [x] Domain tests pure.
- [x] Integration tests hit real containers — B, C, D and F did.
- [ ] Coverage gate enforced — feature 34, pre-existing.
- [x] No Jest.

**C5 — clean close.**
- [x] Probe residue: marker grep exit 123; 19 backups `cmp` identical; no dotnet build/test/format alive.
- [ ] `history.md` effort entry — not closeable while rejected.
- [x] Claude did not commit: HEAD `909394f`.

**C6 — SDD.**
- [x] Spec documents exist.
- [x] N1–N6 ticked.
- [ ] **Every `R<n>` and §11 row genuinely mapped** — rows 63–64/72 value (D8), row 44 (D10), L21 (D9).
- [ ] Spec commit before implementation commit — the human's sequence.

**C7 — reuse fidelity.**
- [x] Only `specs/shared/test-matrix.md` modified under `specs/shared/` (round 2's check; unchanged by part 2 per its record `:2988`).
- [x] No amendment owed (A15).
- [ ] **Reused ids genuinely satisfied** — `R59`'s latency is not asserted as a value (D8).
- [x] `n8n/` unchanged.
- [ ] Effort records — pending approval.

## What must change before re-review round 4

1. **D8** — latency from a drivable time source in all three dispatcher copies. Exact unit asserts, a real upper bound on row 72, the ledger row, and arms by ticks, seconds-for-ms, and deletion.
2. **D9** — both integration concurrency cases create the overlap themselves, armed by the plain hoist with no delay. Correct the two comments, record `:2767` and, once true, L21's Guard cell.
3. **D10** — a registration guard that fails under C3, or an honest correction of the comment, `design.md:465` and the walk.
4. **D11** — the six host comments reworded; the enumeration re-run to zero.
5. **A12–A14** in the same round (thread-safe capture; row 73 comment; superseded marker). **A13's nine-site exclusivity class** to the leader, as a bullet on id 74.
6. **Reconcile the new total against 1799** by project.

**Re-review scope, stated now so it is cheap.**
- **Re-run:** A4 and B2a (ticks), B2b and F1 (plain hoist, both sites), C3 (if option (a) is chosen), the D11 search, plus one arm per new guard.
- **Not repeated:** A1–A3, B1a, C1–C2, D1 and E1, whose files the fix should not change. If `FactRetryDispatcher.cs` changes for D8, A2 is re-run too.

## Effort to date — round 3 addition (for the eventual `history.md` entry, not an entry)

- **Fix round 2 and its part 2 ran between round 2's close and this dispatch.** Their timestamps are in the leader's transcripts.
- **This review.** First probe chain 03:35:29 → 03:40:22 CEST, 2026-09-11; follow-up chain 03:40:57 → 03:41:52; record written by ≈03:55.
- **Totals now:** 1 spec session, 1 gate, the 14 implementer passes counted in round 1, 1 fix round + 1 addendum, fix round 2 + part 2, 1 suite runner, and **3 review rounds, all rejected**. #7's counterpart was approved on its first review (`order-to-cash-nestjs/progress/history.md:1057`).

## Round 4

**Verdict: APPROVED.** Id 27 set to `done` by a single-line edit of `feature_list.json:406`, guarded by asserting lines 401, 402 and 406 before `sed -i '406s/…'`. `git diff -U0 -- feature_list.json | grep '^@@'` still returns the same six hunks (`-406`, `-408`, `-415`, `-468`, `-998`, `-1000`), and the `-406` hunk now reads `-      "status": "pending",` / `+      "status": "done",`. The `progress/history.md` entry, with its effort record, is appended.

**Where this stands.** Every round-3 blocking defect is closed, each by my own mutation on the defect as named, with no widening:
- **D8:** the latency value kills on the exact figure at unit level (`240` vs `2400000`) and at row 72's integration bound.
- **D9:** the plain `NatsHeaders` hoist fails 3/3 at both integration sites, and the overlap is created by the test's own `RequestOverlapBarrierConnection`.
- **D10:** row 44 fails the moment the host's own `.AddAspNetCoreInstrumentation()` is deleted.
- **D11:** its search returns nothing.

A12–A14 are closed by reading. Both mechanisms of the red run survive spot-checks. The green re-run on identical code is acceptable, because the red run has an external cause that **the red log itself names**. **The record's attribution of that cause is wrong and must be corrected (RC1).** No new code defect was found. Four record/comment corrections and three advisories are open at approval, each with a named owner. None is a guard that cannot fail, and none has its root cause in `specs/shared/` beyond id 75, which is already routed.

### Scope — what I ran, and what I did not

- **Not re-run:** `./quality.sh` or any whole project; no claim under test was about the full suite. The 1799 figure is the leader's run, `scratchpad/quality_final2.log` (06:34). I re-summed its 18 `Passed!`/`Failed!` lines: **1799**. `grep -c "^Failed!"` gives **0**, and the file ends `[OK] quality.sh finished`.
- **Run:** 12 named-test executions under mutation in two sets, O (Orders) and G (Gateway), plus the confirming runs.
- **Protocol on every probe:**
  - `cp -p` backup to the session scratchpad's `r4/`;
  - scripted replacement asserting exactly one match;
  - `dotnet build <test project> --no-incremental`;
  - the named test, output recorded verbatim;
  - `cp` restore, `cmp`, re-read of the mutated lines;
  - `touch`, forced rebuild, confirming run.
- **One build at a time.** Set O (PID 4080301) and then set G (PID 4104405), strictly in sequence, each waited on with `while kill -0 <pid>`, doing only read-only work meanwhile. The scripts ran `pgrep -a dotnet | grep -E "dotnet (build|test|format)"` before every build and test; it never fired.
- **Final state:**
  - `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "REVIEW-PROBE"` → exit 123, no output;
  - all 4 backups `cmp` identical to their files;
  - `pgrep` exit 1; HEAD `909394f`.
- **Runtime note:** these probes ran on runtime 10.0.12 / SDK 10.0.112 (after the unattended apt run below). Rounds 1–3 ran on 10.0.11 / 10.0.111.
- **`./init.sh`** was run after the backlog edit: **exit 1**, see C1. The single `[FAIL]` is §4 lockstep: *"progress/current.md claims a feature while none is active"*. It is the expected consequence of `in_review → done`, and `progress/current.md` is the leader's file, which this brief forbids me to write. Every other check is `[OK]`, including the backlog tripwire (*"no feature lost, no done reverted"*), SDD coherence and §5d shared-spec parity. Run once before the edit, it exited 0.

### Probes

| # | Closure | Mutation | Named test | Verbatim result | Restore / confirm |
|---|---|---|---|---|---|
| O1 | D8, unit | Orders `FactRetryDispatcher.cs:84` `(clock.UtcNow - enteredAt).TotalMilliseconds` → `.Ticks` | `FactRetryDispatcherTests` › `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer` | **Fails:** `Assert.Equal() Failure: Values differ / Expected: 240 / Actual: 2400000` (`:112`) | `cmp` identical; forced rebuild; `FullyQualifiedName~FactRetryDispatcher` **11/11** green (8 dispatcher + 3 parity) |
| O2 | A2 re-run, because the file changed | same file `:97` `firstFailedAt ??= clock.UtcNow;` → `=` | `FactRetryDispatcherTests` › `DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsClockReading_NeverALaterOne` | **Fails:** `Assert.Equal() Failure: Values differ / Expected: 2026-09-11T10:00:00.0000000+00:00 / Actual: 2026-09-11T10:00:02.0000000+00:00` (`:237`) | as O1 |
| O3 | D8, row 72's integration bound | as O1 | `RealInfraMetricsProvenanceTests` › `OtcFactProcessingLatencyMs_RecordedFromARealKafkaDeliveredFact_TaggedByTheRealConsumer` | **Fails [10 s]:** `Assert.Contains() Failure: Filter not matched in collection / Collection: [Tuple (1103778, [["consumer"] = "OrdersSaga"])]` (`:102`). The tag matched; only the upper bound rejected the value | `cmp` identical; forced rebuild; this case + O4's case **2/2** green |
| O4 | D9, stock checker — the plain hoist, **no delay anywhere in the mutation** | `NatsStockAvailabilityChecker.cs:33` `var headers = new NatsHeaders();` → `var headers = _reviewProbeSharedHeaders;`, plus a `private readonly NatsHeaders _reviewProbeSharedHeaders = new();` field | `NatsStockAvailabilityCheckerTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`, **3 runs** | **Fails 3/3** (561 ms, 344 ms, 357 ms): `Assert.NotEqual() Failure: Strings are equal / Expected: Not "00-f572cc9d543579b4a4a838bd9501eb30-7a3073986d8f47"··· / Actual: "00-f572cc9d543579b4a4a838bd9501eb30-7a3073986d8f47"···` (`:141`). Runs 2–3: `00-f9f69c50946e79e747509c26e751f7ec-…`, `00-af3940eba4a7e34fb8954a608bb81821-…` | as O3 |
| G1 | D10 | `src/Gateway/Infrastructure/Observability/Telemetry.cs:68` `.AddAspNetCoreInstrumentation()` deleted | `LogCorrelationTests` › `Row44To45_ARealHttpRequestProducesARealServerSpan_AndTheFailureLogLineCarriesTheSameTraceId` | **Fails [1 s]:** `Assert.NotEmpty() Failure: Collection was empty` (`:113`, the exported server-span assertion) | `cmp` identical; forced rebuild; this case + G2's case **2/2** green |
| G2 | D9, Gateway — the plain hoist, no delay | `NatsRpcClient.cs:31-35` object initializer → a shared `_reviewProbeSharedHeaders` field, keys assigned by indexer | `NatsRpcClientIntegrationTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`, **3 runs** | **Fails 3/3** (332 ms, 270 ms, 266 ms): `Assert.NotEqual() Failure: Strings are equal / Expected: Not "00-cc7fbc4ccf29a4b6bf67fe473b548dc4-0a2339037c21e9"···` (`:228`). Runs 2–3: `00-d7d268b87ae7bf3c80d35566571f6cb2-…`, `00-63301e97139e5ef5d9f912c934c270a8-…` | as G1 |

**Why each set shares a build.**
- Each mutation sits on a different line and is read by a different named test, and each failure message asserts that mutation's own value.
- In set G, row 44's case replaces `IRpcClient` with a `RecordingRpcClient`, and the concurrency case builds `NatsRpcClient` directly with no host. Neither executes the other mutation's code.
- In set O, the success-path latency case never enters the `catch` that O2 mutates.

## Closure, finding by finding — round 4

### D8 — **CLOSED**

- **Production.** All three copies read `clock.UtcNow` at entry and afresh at both record sites (Orders `:73`, `:84`, `:136`).
  - `find src -name 'FactRetryDispatcher.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "Stopwatch"` returns 3 lines, all the comment at `:68` (Orders, Notifications, Projector): *"from the injected IClock, not a bare Stopwatch"*. No `Stopwatch` remains in code.
- **Unit exact values.** The success path is armed by me (O1). The exhausted path (`5750`) was armed by the fix round (record `:3008-3010`); I did not re-arm it.
- **Integration bound.** O3 shows it rejects an over-scaled value. It **cannot** reject an under-scaled one, by arithmetic: seconds-for-milliseconds records `real_ms / 1000`, which lies inside `[0, publish-to-observation window]` whenever the latency does. That family is caught only by the unit exact case, which the fix round armed with that exact swap (`:3009`). **Ruling: acceptable as a layered guard,** the same ruling round 3 gave row 73.
- **Ledger row L29** is present, and both halves verify (see *The leader's design edits*).

### D9 — **CLOSED**

- **The overlap is created by the test.**
  - `RequestOverlapBarrierConnection` (Orders `:26`, Gateway `:30`) wraps a real `NatsConnection`. Its `RequestAsync<TRequest,TReply>` blocks on a `Barrier(2)` via `SignalAndWait(TimeSpan.FromSeconds(10), …)` before delegating (Orders `:67`, Gateway `:71`). Every other member passes through.
  - Both tests start the two calls with `Task.Run` and no `Task.Delay` between them (`NatsStockAvailabilityCheckerTests.cs:134-136`, `NatsRpcClientIntegrationTests.cs` in the same shape).
  - Under the hoist, both calls write the shared headers before either sends, so the collision is by construction, not by timing.
  - If a regression stops one call from reaching the barrier, the test fails with a named `TimeoutException` after 10 s rather than hanging.
- **Arming** was done with the plain hoist and no delay in the mutation (O4, G2): 3/3 at each site.
- **Citation corrected.** `find tests src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "rather than recording a probability\|make the collision deterministic"` returns 2 lines:
  - `tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs:90` — the correction sentence quoting the old wording: *"attributed a widened-delay 'make the collision deterministic' instruction to `CLAUDE.md`; that sentence does not appear there (it was the leader's own brief)"*;
  - `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs:177` — the same correction sentence.

  No line attributes the sentence to `CLAUDE.md` as a rule.
- **L21's Guard cell** (`design.md:407`, *"each armed by hoisting `NatsHeaders` onto a shared field"*) is now true at all three sites.
- **What the concurrency cases do not assert** is each call's value against its own activity: they assert only that the two differ. The value is guarded elsewhere:
  - Gateway: `D5_Row34_InjectsTheActiveTraceIdIntoTheOutboundRequestHeaders` (round 3, C1);
  - stock checker: `TraceContextPropagationTests.cs:70`'s construction inside the row-29 `R57_OR4_NatsRpc_…` case (sampled in round 3, not armed by me).

  See A17.

### D10 — **CLOSED**

- **G1 kills.** The test appends a `RecordingActivityExporter` to the host's own tracer provider via `services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new SimpleActivityExportProcessor(exporter)))` (`LogCorrelationTests.cs`, inside `overrideServices`), and asserts a `ActivityKind.Server` span was exported.
- **No test-owned listener on ASP.NET Core.** `find tests/Gateway.IntegrationTests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "ActivityListener\|ShouldListenTo\|Microsoft.AspNetCore\""` returns 9 lines, each classified:
  - `LogCorrelationTests.cs:40`, `:41`, `:44` — doc comments describing the listener that was removed;
  - `NatsRpcClientIntegrationTests.cs:132`, `:134`, `:137` — a listener with `ShouldListenTo = source => source.Name == OtcActivity.SourceName`;
  - `NatsRpcClientIntegrationTests.cs:190`, `:192`, `:195` — the same, in the concurrency case.

  None listens to `Microsoft.AspNetCore`.

### D11 — **CLOSED**

- `find src -name '*Host.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "silently vanish"` → exit 123, no output.
- **Enumerated on the retired wording too:** `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "all three, or"` → exit 123, no output.
- **The new wording** (`BillingHost.cs:33-41`, identical in the five siblings at `:35-37`, and at Gateway `:40-42` and Orders `:51-53`) says:
  - the explicit `ActivityTrackingOptions` setting is not what turns `TraceId` on;
  - the generic host enables it by default, so deleting the line leaves `TraceId` present;
  - setting it to `None` removes it;
  - *"measured by a deletion probe against this runtime (round 2, probes 3-4), never read from framework source"*.
- **Judged against my round-2 finding:** this is exactly what probes 3–4 measured, it claims no more, and it matches L25 (`design.md:411`). Closed.

### A12 — **CLOSED** (by reading)

- Row 73's doc comment now says the bound alone is not tight enough, names B1b, and names `SagaFactHandlerTests`' three `OtcSagaCompletionMs_*` recording cases as the exact value guard.
- The closing fact now carries `DateTimeOffset.UtcNow.AddMinutes(-5)`. By arithmetic, B1b's substitution would record ≈300,000 ms against an upper bound of `afterObserved − orderDate`, which is seconds, because the order is placed moments before. Not re-armed by me.
- The inline comment keeps *"collapses toward 0ms"* only as a quoted retraction.
- **A new over-/under-claim sits in the row-72 case of the same file — RC3.**

### A13 — **CLOSED**

All three `MetricCapture.cs` copies (`tests/Orders.UnitTests`, `tests/Orders.IntegrationTests`, `tests/Gateway.UnitTests`):
- hold a `private readonly Lock _gate`;
- in each measurement callback, copy the tag span to an array outside the lock, then `Add` inside `lock (_gate)`;
- return `ToArray()` snapshots from `Measurements` (and, in the two Orders copies, `LongMeasurements`) under the same lock.

A concurrent add can no longer be observed mid-enumeration. The nine-site exclusivity class is routed to id 74 (below).

### A14 — **CLOSED**

Record `:2500`: *"**A14 (review round 3) — SUPERSEDED by the complete 41-row-group walk at `:2822`**"*. It names the overlapping `119–128` row against the later walk's `119–121`/`122–126`.

## The red run's two mechanisms

### Mechanism 2 — the shared production consumer group

**Spot-checks of skipped sites: 4 Orders site groups and 1 Projector group, all holding.**
- **Orders, no saga wiring.** `SagaConsumptionTests.cs:72`'s `firstHost` is built at `:44-53` from `Host.CreateApplicationBuilder()` with `AddOrdersOutbox`, `AddOrdersAcceptance` and `AddDispatcher` only. There is no `AddOrdersSaga`, so it never registers the subscriber. Holds.
- **Orders, an unreachable broker.** `OrdersCreateAcceptanceTests.cs` `BuildHost` (`:680-697`) sets `options.Kafka.BootstrapServers = "127.0.0.1:1"` inside the test's own configure delegate.
  - The class is `[Collection(NatsCollection.Name)]` (`:30`).
  - `NatsCollection` (`NatsContainerFixture.cs:61`) is `ICollectionFixture<NatsContainerFixture>, ICollectionFixture<MsSqlContainerFixture>`, with no Kafka fixture.
  - Holds for its 10 sites.
  - The same shape holds at `CatalogReferenceListAcceptanceTests.cs:368-382` (5 sites), `OrdersCreateIdempotentReplayTests.cs:451-465` (2) and `OrdersCreateResponderTraceContinuationTests.cs:86-100` (1).
- **Projector, a private broker.** `OffsetContractTests.cs:138`/`:171` (the record's `:131`/`:157` before comments were added): the class has no `[Collection]`, owns `private readonly KafkaContainerFixture _kafka = new();`, and initialises it in `InitializeAsync`, once per test instance. No later test inherits that broker. Holds.

**Re-count, path-excluded** (`find tests/<P>.IntegrationTests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -nE "\.StopAsync\("`):
- **Orders: 21 lines** — the 19 skipped sites plus 2 lines in the helper's own file (`SagaIntegrationTestSupport.cs:186`, a doc comment; `:205`, the helper body). `StopHostAndWaitForGroupToClearAsync` appears 26 times: the definition plus 25 sites.
- **Projector: 4 lines** — 2 skipped sites plus `ProjectorTestHost.cs:47`/`:63`. The helper appears 5 times: the definition plus 4 sites.
- **Notifications:** 1 `.StopAsync(` line (the helper body) and 12 helper mentions.
- All three reconcile with the record: 25 + 19 = 44, and 4 + 2 = 6.

**The population beyond the three projects the record enumerated.**
- **Production consumer groups:** `find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "GroupId ="` returns 4 lines:
  - Notifications `:115` `"notifications"`;
  - Orders `:130` `"orders.saga"`;
  - `KafkaDlqDepthGauge.cs:53` — a per-instance `Guid` group, which no test can inherit;
  - Projector `:111` `"projector"`.

  Fulfillment and Billing own no group.
- **Hosts from any other test project:** `find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -lE "(OrdersHost|ProjectorHost|NotificationsHost)\.CreateBuilder|AddOrdersSaga\(|StartHostAsync\(|ProjectorTestHost\.StartAsync\("` returns 58 files:
  - Billing IT 15 and Fulfillment IT 12, which match their own fixtures' `StartHostAsync(` and join no group;
  - Notifications IT 5, Orders IT 13 and Projector IT 6 — the enumerated projects;
  - unit projects: Orders 2, Projector 2, Notifications 1, which build no broker connection;
  - **Gateway IT: 2.**
- **The Gateway two:**
  - `OperatorNoteReachesTimelineEndToEndTests.cs` starts `OrdersHost` (`:82`) and `ProjectorHost` (`:189`) and tears both down with a bare `StopAsync` (`:269`, `:271`);
  - `StreamProjectorEndToEndTests.cs` starts `ProjectorHost` (`:69`), torn down at `:173`.
  - Each is the **only** class in its collection (`OperatorNoteEndToEndCollection` at `:297`; `StreamProjectorEndToEndCollection` at `:182`), and each has exactly **1** `[Fact]` (`grep -cE '\[(Fact|Theory)'`). No later test inherits either broker, so mechanism 2 cannot cross a test boundary. **Safe, but unclassified by the record,** whose class enumeration (`:3176`) selected files by `.dlq` content — RC4(b).

**Ruling: the clearance wait closes the enumerated population and narrows the class. It does not close the class.**
1. **It is a teardown-side check on this test's own host.** It turns a stale member into a loud, named failure before the next test starts. **Nothing guards the population itself.**
   - A future test in `SagaCollection`, `ProjectorInfraCollection` or `NotificationsCollection` that tears down with a bare `StopAsync()` reintroduces the hazard with every suite green.
   - Today's correctness rests on this round's enumeration, not on construction.
2. **It throws from `finally`.** If a test body has already failed and the group does not clear within 150 s, the `TimeoutException` replaces the body's assertion in the report. That is the burial #7 recorded for its feature 26 (history, F5) — under exactly the contention in which the real message matters most.
3. **Routed as A16**, below.

### Mechanism 4 — warm-up/fact partition-key mismatch — **the Orders/Projector claim holds**

- **Offset reset.** `find src tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "AutoOffsetReset\."` gives 29 lines. Production usages are exactly three:
  - `src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs:132` `Earliest`;
  - `src/Projector/…/KafkaFactStreamSubscriber.cs:113` `Earliest`;
  - `src/Notifications/…/KafkaFactStreamSubscriber.cs:117` `Latest`.

  Every other hit is a doc comment, a config-test assertion, or a test-owned raw consumer set to `Earliest`. There is no `AutoOffsetReset.Latest` usage under `tests/Orders.IntegrationTests` or `tests/Projector.IntegrationTests`.
- **Keys, with a line-start-safe pattern** (the record's single-line `new Message<…> { … Key =` regex would miss a multi-line initializer). `find tests/<P>.IntegrationTests … | xargs -0 grep -nE "^\s*Key\s*=|[{,]\s*Key\s*="` gives **Orders 9, Projector 2** — the same 11 as the record.
  - Orders' 9 key by `correlationId`, `secondCorrelationId` or `Guid.NewGuid()`.
  - Projector `:165` keys by `orderId`.
  - Projector `:218` keys by the `key` parameter of `PublishRawAsync`, whose callers pass `orderId.ToString()` (`:55`) and `secondOrderId.ToString()` (`:92`).
  - The record's *"every one keys by correlationId, orderId, or Guid"* is true once `:218` is traced to its callers.
- **Ruling.** `Earliest` makes the skipped-partition race structurally impossible for these two consumers, whatever the keys. Verified.

## The environment event — ruling

**The timeline, verified by me, which the coordinator's corrected brief also gives.**
- **`/var/log/dpkg.log` (CEST):**
  - `06:20:28 upgrade dotnet-host-10.0 10.0.11 → 10.0.12`;
  - `06:20:29 upgrade dotnet-hostfxr-10.0`, `dotnet-runtime-10.0` and `aspnetcore-runtime-10.0`;
  - `06:20:30` apphost and targeting packs;
  - `06:20:31 upgrade dotnet-sdk-10.0 10.0.111 → 10.0.112`;
  - `06:20:34` every one of them `status installed`;
  - a separate libc6 run from `06:20:42`.
- **The red log, `scratchpad/quality_final.log`:**
  - mtime **06:20:32.918**;
  - its last test project to report, `Gateway.IntegrationTests`, ran `Duration: 4 s`, so it started ≈06:20:28–29, **while dpkg was unpacking the host and runtime**.
- **The inner exception, which neither the record nor the brief cites.** Every `DockerUnavailableException : Failed to connect to Docker endpoint at 'unix:///var/run/docker.sock'` (**78** lines) is paired with `System.IO.FileNotFoundException : Could not load file or assembly 'System.Net.Requests, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'. The system cannot find the file specified.` (**78** lines). There are **28** distinct `[FAIL]` lines, all at container-fixture start, before any test body.
  - `System.Net.Requests` is a shared-framework assembly, loaded lazily by Testcontainers' Docker client, and it went missing while dpkg replaced `Microsoft.NETCore.App` under the running testhost. **The "Docker unavailable" message is the wrapper; the failure is an assembly load.**
- **The Docker daemon:** `systemctl show docker -p ActiveEnterTimestamp -p NRestarts` → `NRestarts=0`, `ActiveEnterTimestamp=Thu 2026-09-10 05:16:08 CEST`. `journalctl -u docker --since 06:19:30 --until 06:21:30` gives 3 lines, all endpoint and task events, none an error.

**Rulings.**
1. **The green re-run on identical code is acceptable.** This is not a re-roll of a probability. The red run's cause is a change of kind: a shared framework being replaced while in use. It is evidenced independently by the package log's timestamps and by the red log's own failing assembly load, and every failure precedes any test body. That is categorically different from *"the isolated re-run passed"*.
2. **The record's attribution must be corrected — RC1.** Record `:3326-3331` attributes the red run to a *"transient daemon-socket stall"* / *"brief daemon-socket unavailability"*, coincident with an SDK update. The daemon half is unsupported (no restart, continuously active) and contradicted by the exception itself (an assembly load, not a socket error). The SDK half has the order wrong: the **host, hostfxr and runtime** were replaced first (06:20:28–30) and are the direct mechanism; the SDK followed (06:20:31–34).
   - **Yes to the coordinator's wording**, *"runtime and SDK upgraded mid-run by unattended apt"*, and the correction must also cite the `System.Net.Requests` `FileNotFoundException` as the observed mechanism.
   - The record's *"Docker was reachable again immediately"* is true and is no evidence of a stall.
   - `progress/current.md`'s *"the evidenced cause is the SDK being replaced"* is superseded the same way. That file is the leader's.

## The leader's design edits

- **L29 (`design.md:415`) — both halves verified.**
  - **History half**, read with `nl -ba` at #7 HEAD `bf45af0`, `apps/orders/src/infrastructure/messaging/fact-retry-dispatcher.ts`:
    - `:129-134` is the comment (*"Uses the injected `Clock`, not a bare `Date.now()`, so a unit test's fake clock controls the recorded value exactly"*);
    - `:135` is `const enteredAt = this.clock.now();`;
    - `:142` is the success-path `factProcessingLatencyHistogram().record(this.clock.now().getTime() - enteredAt.getTime(), { consumer })`;
    - `:171` is the same call after the dead-letter publish.
  - **#8 half:** Orders `FactRetryDispatcher.cs:73`, `:84`, `:136` read.
  - **Guard half:** it names both unit cases by literal name with `240`/`5750`, plus row 72's bound. **The guard executes the code the row is about**, and I armed the success case and the bound (O1, O3).
  - **Residue in the record, not the design:** `:3003` transcribes #7's record sites as *"(`:141`, `:171`)"*, but `:141` is `await process(envelope);`. See RC4(c).
- **Row 44 (`design.md:466`; the brief's `:465`, shifted one line since).** The cell's claims match the test: an exporter appended through `ConfigureOpenTelemetryTracerProvider`, no test-owned `ActivityListener` on `Microsoft.AspNetCore`, and a failure when `.AddAspNetCoreInstrumentation()` is deleted (G1). Holds.
- **§10's count (`:369`).** `grep -cE "^\| \*\*L[0-9]+\*\*" specs/observability_reliability/design.md` → **29**, contiguous `L1 … L29`. *"29 since review round 3"* holds.

## Backlog routing

### Id 75 (`dead_letter_first_failed_at_semantics`) — **ACCEPTED AS ROUTED**

**Verified:**
- **`specs/shared/asyncapi.yaml:2240-2243`** declares `x-first-failed-at:` and `x-failed-at:` each as a bare `$ref: '#/components/schemas/Instant'`. The four headers above them (`x-failed-consumer`, `x-attempts`, `x-error`, `x-original-topic`, `:2225-2239`) each carry a `description:`.
- **#7:**
  - `fact-retry-dispatcher.ts:136` `const firstFailedAt = enteredAt;`;
  - rendered at `kafka-dlq-publisher.ts:49` `'x-first-failed-at': meta.firstFailedAt.toISOString()`;
  - asserted at `fact-retry-dispatcher.spec.ts:89` `firstFailedAt: new Date('2026-08-26T09:00:00.000Z')`.
- **#8:** `firstFailedAt ??= clock.UtcNow` inside the `catch` (`:97`), guarded by O2.

**Ruling.**
- **Root cause.** The contract declares the header and never its meaning, so the root cause is `specs/shared/`. The *"does `specs/shared/` actually prescribe the thing you are about to amend it for?"* check passes: it prescribes the header's existence and type, and the amendment would add only what is missing.
- **The entry satisfies the routing rule.** It is numbered, human-gated, and makes a single recommendation (SA-3 wording). It requires a test whose clock separates dispatch entry from first failure, and arming in both repositories.
- **Not a feature-27 blocker.** Acceptance bullet *"failed processing lands on `<topic>.dlq` after N attempts"* is met, and no `R<n>` defines the header's instant.
- **The entry's own observation is right:** O2 distinguishes the first failure from a later one, never from dispatch entry.

### Id 74, the A13 bullet — **ACCEPTED**

`feature_list.json` id 74 (at `:1039`), acceptance bullet 5, meets the bar:
- it cites this review from `:1580`;
- it requires enumeration first;
- it requires each site to scope its capture to what the test produced, or to prove exclusivity;
- it arms by a concurrent writer emitting a matching-shaped measurement.

**One further bullet is requested for the same entry — A16.**

## §11 — a fresh sample outside round 2's and round 3's

**What remains unsampled.** Round 2 sampled 1–3, 12–13, 14–16, 18–19, 21–28, 33, 34, 36–37, 44, 45–46, 48–49, 61–62, 63–64, 65–66, 74–78, 79–93, 94–108, 119–126 and 127–128. Round 3 sampled 8–9, 10, 11, 17, 20, 29–31, 32, 35, 38–40, 41–42, 43, 47, 50–58, 59–60, 70–73 and 109–118. Against design §11's 41 groups (`grep -n "^| [0-9]" design.md`, `:445-485`), **the only groups neither sample touched are 4–5, 6–7, 67–69, 129–133 and 134–145 — 24 rows.** All five are sampled here.

**Method.** Method names enumerated with `grep -nE "public (async Task|void) [A-Za-z0-9_]+"`, then **the assertions read**, not only the names. This is round 3's lesson about rows 63–64.

| §11 row | #7's claim | Delivered #8 case (literal) and what it asserts | Holds? |
|---|---|---|---|
| 4–5 | env defaults `3`/`500`; reads both from the environment | `OrdersProgramConfigurationTests.cs:212` › `ConfigureSaga_DefaultsFactRetryPolicyToThreeAttemptsAndFiveHundredMs_WhenNoEnvVarsAreSet` (`Assert.Equal(3, …MaxAttempts)`, `Assert.Equal(500, …BackoffMs)`); `:237` › `ConfigureSaga_ReadsFactRetryMaxAttemptsAndBackoffMsIndependently_FromTheirOwnDistinctVariableNames` (sets `7`/`250`, asserts both); `Configure_…` with the same names at `NotificationsProgramConfigurationTests.cs:140`/`:169` and `ProjectorProgramConfigurationTests.cs:100`/`:129` | **yes on behaviour.** The design cell names `FactRetryOptionsTests`, which does not exist — RC2 |
| 6–7 | injects the real trace id, extractable to the same id; no active span → no `traceparent` at all | `KafkaDeadLetterPublisherTests.cs:27` › `PublishAsync_WithAnActiveSpan_PublishesATraceparentHeaderThatExtractsToTheSameRealTraceId` (`Assert.Equal(activity!.Id, traceparent)`, then `Assert.Equal(activity.TraceId, extracted.TraceId)`); `:58` › `PublishAsync_WithNoActiveSpan_PublishesNoTraceparentHeaderAtAll` (`Assert.Null(Activity.Current)`, `Assert.DoesNotContain(… h.Key == "traceparent")`); the same names at `:28`/`:59` in the Projector and Notifications copies | **yes** |
| 67–69 | sums `(high − low)` across partitions; reports `0` for a missing topic without throwing; queries each topic independently | `MetricsExposureTests.cs:83` › `OtcDlqDepth_TheRealKafkaBackedGauge_SumsHighMinusLowAcrossEveryPartitionOfEveryDlqTopic_AgainstTheBrokersOwnReportedCount` (`Assert.Equal(expectedDepth, measurement.Value)` against broker watermarks); `:123` › `OtcDlqDepth_ANonExistentTopic_Reports0AndNeverThrows` (`Assert.Null(exception)`, `Assert.Single(capture.LongMeasurements)` — **no value assertion**); `:156` › `OtcDlqDepth_SumsAcrossMultipleTopicsIndependently_AMissingTopicNeitherStopsNorZeroesTheOthers` (`Assert.Equal(expectedOrdersDepth + expectedFulfillmentDepth, measurement.Value)` with Billing's `.dlq` absent) | **yes.** 67 and 69 on value. 68's *"reports 0"* is guarded by `:156`'s exact sum — a missing topic contributing anything but 0 breaks it, and the gauge returns 0 at `KafkaDlqDepthGauge.cs:81`/`:95` — **not by the case named for it** (A17). The design cell names `KafkaDlqDepthTests`, which does not exist — RC2 |
| 129–133 | fourteen facts: handler table, unknown type, rank table, summaries | not applicable. `grep -c "typeof(" src/Contracts/Facts/FactCatalog.cs` → `14` | **yes (n/a)** |
| 134–145 | `saga-e2e-verification.integration.spec`, 12 cases | not applicable. `feature_list.json` `"name": "saga_e2e_verification"` (`:420`) `"status": "pending"` (`:424`) | **yes (n/a)** |

**The non-existence claim, as a search.**
- `find tests -name 'FactRetryOptionsTests.cs' -o -name 'KafkaDlqDepthTests.cs'` (bin/obj irrelevant for a filename) → no output.
- By content: `find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -nE "class (FactRetryOptions|KafkaDlqDepth|DlqDepth)\w*"` → exit 123, no output.

**Of 5 row groups (24 rows) sampled, all 5 hold on behaviour.** Two design cells cite classes that do not exist, and one case name claims a value it does not assert. The part-2 walk (record `:2837`) noted the 4–5 reclassification; its 67–69 line (`:2866`) says *"verified"* with no note of the name mismatch.

## Findings open at approval — each with an owner, none blocking

- **RC1 — the record's environment attribution** (`progress/impl_observability_reliability.md:3326-3331`).
  - **Owner:** the leader; `progress/` is outside `src/`/`tests/`. Append the correction in place, not as a rewrite.
  - **Content:** the host, hostfxr, runtime and ASP.NET Core runtime (10.0.11 → 10.0.12, unpacked 06:20:28–30), then the SDK (10.0.111 → 10.0.112, 06:20:31–34), were upgraded mid-run by unattended apt.
  - **Mechanism:** `FileNotFoundException` for `System.Net.Requests` (78×, one per `DockerUnavailableException`).
  - **Strike** *"daemon-socket stall"*.
  - **Why it must be fixed:** a red run attributed to Docker invites the next reader to add Docker retries. The real lesson — check `/var/log/dpkg.log` before diagnosing a red full run — is already in `progress/current.md` and belongs in the record that #9 reads.
- **RC2 — `design.md` §11 cells naming classes that do not exist.**
  - Row 4–5 (`:446`) `FactRetryOptionsTests` → the three `*ProgramConfigurationTests` cases above.
  - Row 67–69 (`:475`) `KafkaDlqDepthTests` → the three `MetricsExposureTests` cases above.
  - **Owner:** the leader, marked in place like row 44.
  - **Same class as round 1's R9** (L21 citing a `NatsRpcClientTests` that never existed).
- **RC3 — `tests/Orders.IntegrationTests/RealInfraMetricsProvenanceTests.cs:39-42`.**
  - The row-72 doc comment says of ticks-for-milliseconds and seconds-for-milliseconds: *"a real-infra bound this wide could never catch either"*.
  - O3 shows it catches ticks: `1103778` rejected. The same test's inline comment at `:94-99` says so.
  - **Correct to:** the bound rejects over-scaled values and cannot reject under-scaled ones, which only the unit exact cases catch.
  - **Owner:** `test_maintainer`, via the leader. The change is comment-only and changes no assertion, so it does not reopen the feature.
- **RC4 — record residue (the leader):**
  - **(a) Forbidden filter form.** The class-closure commands at `:3199-3200`, `:3260`, `:3266-3267` and `:3273` use `| grep -v '/bin/\|/obj/'`, the post-filter form `CLAUDE.md` forbids. My path-excluded re-runs give the same populations (above), so no count is wrong; replace the commands.
  - **(b) Unclassified hosts.** The class enumeration at `:3175-3192` selects by `.dlq` content and so omits Gateway IT's two production-group hosts. Add their two classification lines (single-test collections, safe).
  - **(c) Wrong line.** `:3003` cites #7's success record site as `:141`; it is `:142`.
  - **(d) Parity claim not true.** `:3016`, and the Gateway copy's own doc comment, call the two `RequestOverlapBarrierConnection.cs` files *"byte-parity siblings"*. After normalising the namespace, `diff` differs at `:18-24` vs `:18-28` (doc-comment wording). No parity test covers them. Say *"same logic"*, or make them byte-identical.
- **A16 — the clearance wait narrows the class.**
  - **Owner:** the leader, as **one more bullet on id 74** — same loop, same *"a test that assumes exclusive use of a shared broker resource"* shape.
  - **Content, part 1:** make clearance structural, so that a new test in a shared-broker collection cannot tear down without it. For example, `StartHostAsync`/`ProjectorTestHost.StartAsync` return an `IAsyncDisposable` whose dispose waits; or a sweep over `.StopAsync(` sites in those collections with the **expected skipped set as a literal**.
  - **Content, part 2:** preserve the body's exception when teardown also throws.
  - **Arming:** a bare `StopAsync()` teardown in `SagaCollection`, which must fail the sweep.
- **A17 — names claiming more than their assertions** (record only; renaming would ripple into `test-matrix.md` and design citations):
  - `OtcDlqDepth_ANonExistentTopic_Reports0AndNeverThrows` asserts no value (guarded by `:156`);
  - both `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` cases assert distinctness only (value guarded by `D5_Row34_…` and the row-29 `R57_OR4_NatsRpc_…` case).
- **A18 — routing check, stated.**
  - The only finding in this round rooted in `specs/shared/` is id 75, already a numbered entry with an SA-3 recommendation.
  - RC1–RC4 concern this feature's own record, design and one test comment.
  - A16 and A17 concern test shape.
  - No SA-n is owed by this review.

## `R<n>` → test mapping — deltas since round 3

| Req | Test(s) | Verdict |
|---|---|---|
| **R59** (fact-processing latency value) | `FactRetryDispatcherTests` › `OtcFactProcessingLatencyMs_RecordedOnSuccess_TaggedByConsumer` (`240`), › `OtcFactProcessingLatencyMs_RecordedOnTheExhaustedRetryDlqPathToo` (`5750`); `RealInfraMetricsProvenanceTests` › `OtcFactProcessingLatencyMs_RecordedFromARealKafkaDeliveredFact_TaggedByTheRealConsumer` | **closed.** Armed by me at unit (O1) and integration (O3) level; the exhausted path by the fix round |
| **R57/OR4** (outbound NATS, concurrency) | `NatsStockAvailabilityCheckerTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`; `NatsRpcClientIntegrationTests` › `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` | **closed** — O4 and G2, 3/3 each, no delay in the mutation |
| **R56** (first hop, the host's registration) | `Gateway.IntegrationTests/LogCorrelationTests` › `Row44To45_ARealHttpRequestProducesARealServerSpan_AndTheFailureLogLineCarriesTheSameTraceId` | **closed** (G1) |
| **R16/OR1** (first-failure capture) | `FactRetryDispatcherTests` › `DeadLetterPublication_FirstFailedAt_IsTheFirstAttemptsClockReading_NeverALaterOne` | holds after the dispatcher change (O2) |
| **R58** | unchanged; D11 was comment-only | — |

Every other `R<n>` row stands as verified in rounds 1–3.

## `CHECKPOINTS.md` — round 4

**C1 — the harness is complete.**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh`, `progress/current.md`, `progress/history.md` exist (`ls`).
- [x] `.claude/agents/` holds implementer, leader, reviewer, spec_author, suite_runner, test_maintainer.
- [ ] Every agent definition declares its model — not re-checked this round; unchanged by the feature.
- [ ] `./init.sh` exits 0 — **exit 1 after the `done` edit, solely §4 lockstep:** `progress/current.md` still names id 27 while none is active. That is the leader's session transition, and this brief forbids me to write that file. Exit 0 before the edit. Every other check is `[OK]`, including backlog tripwire, SDD coherence (*"8 sdd feature(s) past pending have their triple-doc"*) and §5d parity (*"shared spec byte-identical to #7 across 6 file(s)"*).

**C2 — state is coherent.**
- [x] No feature `in_progress` (`init.sh`: *"no feature in_progress"*).
- [x] Every status valid; tripwire *"no feature lost, no done reverted"*.
- [x] Id 27, now `done`, has passing tests: the leader's run gives 1799 / 0 failed, re-summed by me.
- [ ] `progress/current.md` describes the active session — it must be reset by the leader now that id 27 is closed; see C1.
- [x] No `blocked` feature touched.

**C3 — architecture is respected.**
- [x] NetArchTest: `Architecture.Tests` **25/25** in `quality_final2.log` (the leader's run, read by me, not run by me). No `src/` change is left by this round: all 4 backups are `cmp` identical and the marker grep exits 123.
- [x] No cross-service DB access introduced. `git diff -- '*.csproj' | grep -E "^[+-].*(ProjectReference|PackageReference|FrameworkReference)"` shows no `ProjectReference` change: only OpenTelemetry `PackageReference`s and a `FrameworkReference Microsoft.AspNetCore.App`.
- [x] No shared runtime beyond `SharedKernel`/`Contracts`/`Cqrs`; `git status --porcelain -- src/SharedKernel` is empty.
- [x] No `Domain/` reference to `OrderToCash.Cqrs`, and no `decimal` in the domain — the architecture tests are in the 25/25 above.
- [x] Interactions unchanged by rounds 2–4.
- [x] No stray probe residue (marker grep exit 123).

**C4 — verification is real.**
- [x] `./quality.sh` passes: the leader's run at 06:34 (`[OK] quality.sh finished`), summed by me.
- [x] Domain tests pure.
- [x] Integration tests hit real containers — O3, O4, G1 and G2 all ran against Testcontainers.
- [ ] Coverage thresholds — the log carries per-report line figures only (e.g. `53.1%`, `6.6%`, `97.2%`, `90.2%`); I read no aggregate domain-layer figure. The gate itself is feature 34's, as in round 3.
- [x] No Jest.

**C5 — the session closed cleanly.**
- [x] No stray files: `git status --porcelain --untracked-files=all | grep -E '\.(tmp|bak|orig)$|/bin/|/obj/'` → no output.
- [x] `progress/history.md` entry with effort record — appended this round.
- [x] `feature_list.json` reflects id 27 `done`; the leader's other hunks are untouched.
- [ ] The human told what was done and how to test it — the leader's.
- [x] Claude did not commit: HEAD `909394f`.

**C6 — Spec-Driven Development.**
- [x] `specs/observability_reliability/{requirements,design,tasks}.md` exist.
- [x] EARS with `R<n>` ids (round 1).
- [x] `tasks.md` fully ticked: `grep -c '^\s*- \[ \]'` → **0**, `grep -c '^\s*- \[x\]'` → **50**.
- [x] Every `R<n>` mapped to a named test; deltas above.
- [ ] Spec commit precedes implementation commit — the human's commit sequence; nothing is committed yet.

**C7 — spec-reuse fidelity.**
- [x] `specs/shared/` byte-identical to #7 except `test-matrix.md`: `git status --porcelain specs/shared` → ` M specs/shared/test-matrix.md` only; `init.sh` §5d `[OK]`.
- [x] No silent fork; the one semantic gap found (id 75) is routed as a human-gated SA-3 recommendation.
- [x] Reused ids genuinely satisfied — R59's latency is now asserted as a value.
- [x] `n8n/` unchanged: `git status --porcelain n8n | wc -l` → 0.
- [ ] Black-box API script parity — feature 28's, not this feature's.
- [x] Effort records complete and honest — the `history.md` entry records this as a slow feature, with the reasons.
- [ ] README benchmark section — the wrap-up's.

## Effort — round 4 addition (the entry itself is in `progress/history.md`)

- **Transcript first/last timestamps**, UTC converted to CEST, from this session's `subagents/*.jsonl`:
  - fix round 1: 22:36 → 00:23;
  - review round 2: 00:24 → 00:48;
  - fix round 2: 00:51 → 02:27;
  - fix round 2, part 2: 02:29 → 03:24;
  - review round 3: 03:26 → 03:47;
  - fix round 4: 03:48 → 04:36;
  - red-run diagnosis and class closure (one transcript, resumed once): 04:39 → 06:35;
  - **this review: 06:38 → ≈07:05**, with probe set O 06:43:26 → 06:45:27 and set G 06:45:44 → 06:46:28.
- **Totals:**
  - 1 spec session and 1 gate;
  - 14 implementer passes before the first review, plus 5 implementer transcripts after it (7 passes, counting fix round 1's addendum and the diagnosis round's class-closure resume);
  - 1 suite runner;
  - **4 review rounds: 3 rejected, 1 approved.**
- **Wall-clock:** ≈24 h 10 min of near-continuous agent time, 06:15 on 2026-09-10 → ≈07:05 on 2026-09-11, excluding the 06:39 → 07:10 gate gap. Against #7's ≈7 h 45 min (`order-to-cash-nestjs/progress/history.md:1057`), that is **≈3.1×**.
