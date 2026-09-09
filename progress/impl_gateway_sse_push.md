# `gateway_sse_push` — implementation report

Feature id 26, phase 13, `sdd: false`. Last endpoint of the Gateway, and of
phase 13: `GET /orders/stream` — a server-sent-event stream of saga
progress, fed by the projector's own NATS update signal, with a bounded
replay buffer for `Last-Event-ID` reconnection and a heartbeat that keeps
the connection alive.

## What was built

**`GET /orders/stream`** (openapi.yaml `stream` tag, R55), mapped as its
own literal route, registered after `GET /orders/{id}` (the ported-idiom
ledger's row 6, from feature `gateway_rest_auth`, already licenses this
order — see the ledger below).

```
src/Gateway/
  Domain/Sse/            CursorGenerator (pure, epoch-ms + sequence cursor),
                         ReplayBuffer<T> (pure, bounded, oldest-evicted-first),
                         ReplayResult<T>
  Application/Stream/    StreamFrame, StreamHub (the live hub: replay buffer +
                         per-subscriber Channel<StreamFrame> fan-out)
  Infrastructure/
    Messaging/           GatewaySseOptions (BufferCapacity/PingIntervalMs,
                         FromEnvironment()), NatsStreamSignalSubscriber
                         (BackgroundService, subscribes readmodel.order.updated.*
                         / readmodel.timeline.appended.*)
  Presentation/
    Endpoints/            StreamEndpoints.cs (writes the raw id:/event:/data:
                         frames directly — no ASP.NET Core SSE helper exists)
    Dto/                  StreamReadyResponseDto, StreamPingResponseDto (added
                         to the existing ResponseDtos.cs)
  GatewayHost.cs          MapStreamEndpoints() added, after MapOrdersEndpoints()
  Infrastructure/
    GatewayOptions.cs             Sse property added
    GatewayServiceCollectionExtensions.cs   StreamHub + NatsStreamSignalSubscriber
                                            + GatewaySseOptions registered
  Program.cs              options.Sse = GatewaySseOptions.FromEnvironment()
```

`.env.example`: `GATEWAY_SSE_BUFFER_CAPACITY=500`, `GATEWAY_SSE_PING_INTERVAL_MS=15000`
(same names, same defaults as #7's own `sse.config.ts`).

## Packages

**Zero new NuGet packages.** `System.Threading.Channels.Channel<T>` (used
for `StreamHub`'s per-subscriber fan-out) is part of the shared framework
since .NET Core 3.0 — the same type `ChannelSagaCommandSignal`
(`src/Orders/Infrastructure/Saga/`) already uses in this repository for its
own single-consumer case; `StreamHub` is the multi-consumer fan-out case.
`PeriodicTimer` (the ping cadence, `StreamEndpoints.cs`) is likewise BCL.
Everything else this feature touches (`NATS.Client.Core`,
`Confluent.Kafka`, `Microsoft.AspNetCore.*`) was already referenced by
`src/Gateway`/`tests/Gateway.IntegrationTests` before this feature.

One new **project reference**, test-only: `tests/Gateway.IntegrationTests`
now also references `src/Projector/Projector.csproj`, to boot the REAL
`ProjectorHost` for `StreamProjectorEndToEndTests.cs` (below) — the same
precedent `src/Fulfillment/Fulfillment.csproj`'s existing test-only
reference (feature `gateway_rest_auth`) already established.
`src/Gateway` itself never references `src/Projector`.

## The ported-idiom ledger

| # | #7 relied on | In #8 that property is supplied by | Guard |
|---|---|---|---|
| 1 | An RxJS `Subject` (`apps/gateway/src/application/stream-hub.ts`) for multi-subscriber live fan-out — every open SSE connection subscribes to the SAME `Observable`. | There is no BCL equivalent of a multicast `Subject`. `StreamHub` (`src/Gateway/Application/Stream/StreamHub.cs`) keeps one bounded `System.Threading.Channels.Channel<StreamFrame>` PER live connection instead, broadcasting a published frame by writing it into every subscriber's own channel (`ConcurrentDictionary<Guid, Channel<StreamFrame>>`). Bounded + `DropOldest` (not `ChannelSagaCommandSignal`'s own `DropWrite`): a stalled reader should catch up on the MOST RECENT frames once it resumes, not replay ones already stale. | `StreamHubTests.Publish_EmitsAFrameToLiveSubscribers_WithAFreshCursor`, `Publish_NeverDeliversAFrame_ToADroppedSubscription`, `Unsubscribe_RemovesTheSubscriber_FromSubscriberCount` — armed, see below. |
| 2 | A single-threaded Node event loop, which makes `CursorGenerator`'s plain `this.sequence += 1` (`apps/gateway/src/domain/sse/cursor.ts`) race-free by construction — two calls to `next()` can never genuinely overlap there. | This hub is fed by TWO concurrent NATS subscription loops (`NatsStreamSignalSubscriber`'s `order.updated`/`timeline.appended` loops, run via `Task.WhenAll`) on the .NET thread pool — two calls to `CursorGenerator.Next()` CAN happen at the same instant on different threads. `Interlocked.Increment(ref long)` replaces the plain increment. | `CursorGeneratorTests.Next_NeverProducesADuplicateCursor_UnderGenuineConcurrentCalls` — 8 threads × 2,000 calls each, asserts 16,000 distinct cursors. Armed by SUBSTITUTION (there is no "delete" for a concurrency primitive) — `Interlocked.Increment` replaced with a plain `++_sequence`, the SAME test observed to fail with duplicate cursors under load, three times, then restored — arming table row 8. |
| 3 | The same single-threaded assumption, for `ReplayBuffer`'s own `push`/`replayAfter` (`apps/gateway/src/domain/sse/replay-buffer.ts`) — not thread-safe by #7's own design, safely so on that runtime. | `ReplayBuffer<T>.Push` is invoked from the same two concurrent NATS loops; `ReplayAfter` is invoked from every SSE connection's own Kestrel request thread — genuinely concurrent here. Every member is guarded by one `lock (_entries)` — the SAME `lock (field)` shape `IssuedOrderWindow` (feature `gateway_rest_auth`) already establishes in this service. | Not independently armed for concurrency (the lock is a standard mutual-exclusion primitive, not a business rule) — correctness under single-threaded access is proved by `ReplayBufferTests`' 7 ported cases, all armed (below). |
| 4 | `JSONCodec<{ orderId: string } & Record<string, unknown>>` (`apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.ts`) — decodes each frame's `orderId` out of the JSON PAYLOAD, then re-serialises the decoded object with `JSON.stringify` when the SSE frame is later written. | `NatsStreamSignalSubscriber` reads `orderId` off the NATS **subject** itself — the trailing token of `readmodel.order.updated.<orderId>`/`readmodel.timeline.appended.<orderId>`, which `NatsUpdateSignalPublisher` constructs from `document.OrderId.ToString("D")` — and forwards the payload BYTES completely untouched into `StreamHub.Publish`. Stronger, not weaker: the SSE `data:` line is then guaranteed byte-identical to what the projector actually published (never a structural equivalent from a second serialiser round trip), and orderId extraction no longer depends on the payload happening to carry an `orderId` field of its own. | `NatsStreamSignalSubscriberTests` (5 cases, the pure subject-parsing step) + `StreamHttpTests.OrderIdFilter_AClientSubscribedToOneOrder_NeverReceivesAnotherOrdersFrames` (real broker, proves the filtering value is genuinely subject-derived) — armed, see below. |
| 5 | `console.error(JSON.stringify(...))` with an `x-correlation-id` fallback read from NATS headers (`nats-stream-signal.adapter.ts`'s decode-failure branch, and its own dedicated spec `nats-stream-signal-log-trace-id.spec.ts`) — needed BECAUSE #7 decodes `orderId` from the payload, which is exactly what fails to parse on a malformed frame, so headers are the only trustworthy identifying context left. | **Not applicable, by construction, not by omission.** Since this subscriber reads `orderId` from the SUBJECT (row 4), a malformed-PAYLOAD frame still carries full identifying context (the subject itself, logged verbatim: `logger.LogWarning(ex, "... subject={Subject}", message.Subject)`) without needing a headers-based fallback at all. The `traceId`/OTel half of #7's own spec is genuinely out of scope here — feature 27 (observability). | `StreamHttpTests.AMalformedSignalFrame_IsSkipped_WithoutBreakingConsumptionOfTheNextWellFormedFrame` (real broker: a syntactically-invalid frame is published, then a well-formed one on the SAME subject, and only the well-formed one arrives) — armed indirectly by row 4's arming (which mutates the exact code path this test exercises). |
| 6 | ASP.NET Core Minimal API route precedence — already recorded as row 6 of feature `gateway_rest_auth`'s own ledger (`progress/impl_gateway_rest_auth.md`): "a literal route segment always outranks a route-parameter segment at the same position, regardless of `Map*` call order", probed ONLY over a throwaway `/probe/{id}` vs `/probe/literal` pair (`RoutePrecedenceTests.cs`), with that row's own note: "feature 26 will hit the identical question and should not have to re-derive this." | Confirmed, not re-derived: `StreamEndpoints.MapStreamEndpoints` is registered AFTER `OrdersEndpoints.MapOrdersEndpoints` (which maps `GET /orders/{id}`) — the identical registration order the probe already covers. | `StreamHttpTests.GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute` — proves the REAL production route, not only the probe route: if the literal ever lost, `RequestParsing.ParseId("id", "stream")` would reject "stream" as not a GUID and answer 400. Armed, see below. |
| 7 | #7's own `stream.controller.ts` — an SSE controller with no framework helper, writing raw `id:`/`event:`/`data:` lines directly, because NestJS's `@Sse()` decorator sends a longer `Cache-Control` than the contract's own `const`. | ASP.NET Core Minimal APIs have no SSE helper at all — `StreamEndpoints.cs` writes `context.Response.Body` directly for the identical reason (exact control of the two `const` headers and the exact frame shape). Not a divergence; the same translation shape every ASP.NET Core SSE implementation in this ecosystem uses. | Header assertions in `StreamHttpTests.Connect_SendsStreamReady_ThenALiveFramePublishedOnReadmodelOrderUpdated` (200, `text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`, real Kestrel round trip). |
| 8 | #7's own `gateway_sse_push` rejection, F1: a real-projector end-to-end spec (`stream-projector-e2e.integration.spec.ts`) spawned the projector via `tsx` behind a header comment that FALSELY claimed the setup was equivalent to the real dev command. | `StreamProjectorEndToEndTests.cs` boots the real, unmodified `ProjectorHost.CreateBuilder` graph IN-PROCESS (never a spawned child process) — the SAME shape `FulfillmentStockEndToEndTests.cs` already establishes and this repository has already reviewed and accepted. The class's own remarks state the two genuine differences from the `docker-compose` deployment explicitly, rather than asserting equivalence: (1) one .NET process hosts both the projector's `IHost` and the test runner, never two OS processes; (2) configuration is an `Action<ProjectorOptions>` delegate, never environment variables. Every line of PROJECTOR code that runs is the real, unmodified production code. | `StreamProjectorEndToEndTests.AFactPublishedOnTheRealOrdersFactsTopic_ConsumedByTheRealProjector_ArrivesAtAConnectedSseClient` — real Kafka, real MongoDB, real NATS, a `order.placed.v1` fact produced on the real topic, consumed by the real `ProjectorFactsConsumer`, projected, published on the real `NatsUpdateSignalPublisher`'s subjects, and received as `order.updated`+`timeline.appended` SSE frames. Green, first run (see "Final verification run"). |
| 9 | *(engine claim, probed one direction — see the SSE-specific instruction in this feature's own brief)* — `NetworkStream.ReadTimeout`, a genuine per-read idle timeout, as the mechanism `Heartbeat_KeepsTheConnectionAlive_...` uses to prove the heartbeat's necessity. | Probed in the direction that matters for THIS claim: with the heartbeat present, the connection survives ≥6 consecutive read windows (the FORWARD direction — the baseline, unmutated test, always green); with the ping-write deleted (the arming mutation, below), the very first read blocks past the window and `NetworkStream.Read` throws `IOException`/`SocketException` within ~1 second (the REVERSE direction). Both directions genuinely exercised by the SAME test file — this is not a symmetric two-party engine claim (no "which order were two things created in" question), so a single test proves both the presence and the absence case by construction (baseline vs. arming mutation), and both are recorded in the arming table below. | `StreamHeartbeatHttpTests.Heartbeat_KeepsTheConnectionAlive_AcrossAnIdleReadTimeoutThatWouldOtherwiseFireWithoutIt`. |

## `ping` deliberately carries no `id:` line — the rule this feature must not "tidy"

`StreamEndpoints.WriteFrameAsync` takes `id: string?`; `stream.ready` and
`ping` are always called with `id: null`. Guarded three ways:

1. **Domain**: `StreamHub.MintCursor()` never calls `ReplayBuffer.Push` —
   `StreamHubTests.MintCursor_NeverAddsToTheReplayBuffer`.
2. **Wire, unit-adjacent**: none — the frame-writing decision lives in
   `StreamEndpoints`, Presentation-only, no ASP.NET-independent unit seam.
3. **Wire, real HTTP**: `StreamHeartbeatHttpTests.PingFrames_CarryNoIdLine_AndReconnectingWithARealContentFramesIdStillResumes`
   — captures a real content frame's id, observes a `ping` arriving
   STRICTLY AFTER it with `Id: null`, reconnects with the content frame's
   id, and confirms the stream still resumes. Armed (below): giving `ping`
   an id makes this test fail with `Assert.Null() Failure: Value is not
   null`.

## Enumeration — #7's SSE-related Gateway test files, classified

Command, run against `order-to-cash-nestjs` (sibling checkout):

```
$ find apps/gateway/src -iname "*.spec.ts" | grep -iE "sse|stream"
apps/gateway/src/application/stream-hub.spec.ts
apps/gateway/src/domain/sse/cursor.spec.ts
apps/gateway/src/domain/sse/replay-buffer.spec.ts
apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.spec.ts
apps/gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts
apps/gateway/src/stream.integration.spec.ts
apps/gateway/src/stream-projector-e2e.integration.spec.ts
```

**7 files.** One line each:

| # | #7 file | Classification |
|---|---|---|
| 1 | `domain/sse/cursor.spec.ts` | **PORTED** — `CursorGeneratorTests.cs` (3 ported cases, +1 new: concurrent-calls uniqueness — ledger row 2) |
| 2 | `domain/sse/replay-buffer.spec.ts` | **PORTED, 1:1** — `ReplayBufferTests.cs` (7 cases, one per #7 case) |
| 3 | `application/stream-hub.spec.ts` | **PORTED, +3 cases** — `StreamHubTests.cs` (the first 3 cases are ported; `MintCursor_NeverAddsToTheReplayBuffer`, `Publish_NeverDeliversAFrame_ToADroppedSubscription`, `Unsubscribe_RemovesTheSubscriber_FromSubscriberCount` are new — #7's own suite never directly guards these, though `stream.controller.ts`'s comments claim the first two) |
| 4 | `infrastructure/messaging/nats-stream-signal.adapter.spec.ts` | **PORTED, at a stronger level** — #7 mocks the NATS connection (`vi.fn()`); this repository's own convention is real infrastructure, so the equivalent guard is `StreamHttpTests.AMalformedSignalFrame_IsSkipped_WithoutBreakingConsumptionOfTheNextWellFormedFrame` + `Connect_SendsStreamReady_ThenALiveFramePublishedOnReadmodelOrderUpdated` (real broker) plus `NatsStreamSignalSubscriberTests.cs` (5 cases, the pure subject-parsing step #7's own file has no equivalent of, since #7 parses the PAYLOAD, not the subject — ledger row 4) |
| 5 | `infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts` | **NOT PORTED — not applicable, by construction** (ledger row 5). The `correlationId`-from-headers fallback #7 needs (because it decodes `orderId` from a payload that can fail to parse) has no equivalent need here (this subscriber reads `orderId` from the subject, which survives a payload decode failure intact). The `traceId`/OTel half is feature 27's scope, unbuilt by design in this phase. |
| 6 | `stream.integration.spec.ts` | **PORTED, 2 describe blocks split into 2 files** — the main describe (`stream.integration.spec.ts`'s own top block) → `StreamHttpTests.cs` (7 cases: connect+stream.ready+live frame, orderId filter, reconnect-replays-missed [acceptance bullet 1, forward], reconnect-unknown-cursor [acceptance bullet 1, reverse], reconnect-without-duplicates [acceptance bullet 1, boundary], route-precedence-for-the-real-route [ledger row 6], malformed-frame-skip [ledger row 5]); the "ping heartbeat" describe block → `StreamHeartbeatHttpTests.cs` (3 cases: ping arrives on interval, ping carries no id + reconnect still resumes, heartbeat keeps the connection alive [acceptance bullet 2, the STRONG form — ledger row 9]) |
| 7 | `stream-projector-e2e.integration.spec.ts` | **PORTED, corrected** — `StreamProjectorEndToEndTests.cs` (1 case), fixing #7's own F1 rejection (a `tsx`-spawned child process falsely claimed equivalent to the real dev command) by booting the real `ProjectorHost` graph in-process instead — ledger row 8 |

All 7 accounted, one line each. Nothing grouped.

## Acceptance bullets — how each is proved

**1. "client reconnect resumes without duplicates."** Read literally
against openapi.yaml's own admission that delivery is AT-LEAST-ONCE (a
frame may repeat after a reconnect; clients deduplicate on `eventId`) — the
transport-level guarantee this bullet is actually about is "resumes AFTER
the given cursor, never AT-OR-BEFORE it, and never drops a frame it
should have replayed." Proved in **both directions**, per this feature's
own instruction:
- **A valid `Last-Event-ID` replays exactly the missed frames** —
  `StreamHttpTests.Reconnect_AKnownLastEventId_ReplaysEveryFrameMissedSince_ResumedTrue`
  (not one more), and
  `StreamHttpTests.Reconnect_WithACursorOfAnAlreadyReceivedFrame_NeverRedeliversThatFrame`
  (not one fewer, AND never the cursor's own frame again — asserts
  `Assert.Single`, not merely `Assert.NotEmpty`).
- **An unknown/aged-out cursor answers `resumed:false`** —
  `StreamHttpTests.Reconnect_AnUnknownOrAgedOutLastEventId_AnswersStreamReadyResumedFalse_NeverAnError`.
- The boundary itself (exclusive, not inclusive) is proved at the pure
  `ReplayBuffer<T>` level too — `ReplayBufferTests.ReplayAfter_ReturnsAnEmptyMissedList_WhenTheClientIsAlreadyCaughtUpToTheNewestCursor`.

Armed twice, both directions, at the `ReplayBuffer` level (arming table).

**2. "heartbeat keeps the connection alive."** Two tests, deliberately at
different strengths: `Ping_ArrivesOnTheConfiguredInterval_WithAWellFormedStreamPingBody`
(the weak form — a ping arrives, matching #7's own level) and
`Heartbeat_KeepsTheConnectionAlive_AcrossAnIdleReadTimeoutThatWouldOtherwiseFireWithoutIt`
(the strong form this feature's brief asks for) — a raw
`TcpClient`/`NetworkStream` with `ReadTimeout` set to 4× the ping interval,
surviving ≥6 consecutive read windows only because SOMETHING keeps writing
bytes within each window. Armed: deleting the ping write makes the FIRST
read past `stream.ready` block for the full window and throw
`IOException`/`SocketException` — see the arming table.

## Arming table

Every mutation below was: (1) applied with `Edit`, (2) built with `dotnet
build --no-incremental` (defeating MSBuild's incremental up-to-date check),
(3) the named test run and its FAIL captured verbatim, (4) restored from a
`cp` backup taken before mutation (never `git checkout --`), (5) verified
byte-identical with `cmp` against the backup, (6) rebuilt with
`--no-incremental`, (7) the named test re-run and confirmed green.

| # | File mutated | Mutation | Named test | Verbatim failure (abridged) | Restored + green |
|---|---|---|---|---|---|
| 1 | `StreamEndpoints.cs` | `ping` given `id: pingCursor` instead of `id: null` | `StreamHeartbeatHttpTests.PingFrames_CarryNoIdLine_AndReconnectingWithARealContentFramesIdStillResumes` | `Assert.Null() Failure: Value is not null` `Expected: null` `Actual: "1788936515004-3"` | Yes — `cmp` identical, `--no-incremental` rebuild, 3/3 green |
| 2 | `ReplayBuffer.cs` | Off-by-one: the found cursor's OWN frame is re-added to `missed` | `ReplayBufferTests.ReplayAfter_ReturnsEverythingAfterAKnownCursor_ResumedTrue`, `ReplayAfter_ReturnsAnEmptyMissedList_WhenTheClientIsAlreadyCaughtUpToTheNewestCursor`, `ReplayAfter_ACursorOlderThanTheBufferHolds_ResolvesResumedFalse` | `Assert.Equal() Failure: Collections differ … Expected: <generated> ["b", "c"] Actual: List<string> ["a", "b", "c"]` (three named tests failed simultaneously) | Yes — `cmp` identical, `--no-incremental` rebuild, 7/7 green |
| 3 | `ReplayBuffer.cs` | `ReplayAfter` always returns `Resumed: true`, even for an unknown/aged-out cursor | `ReplayBufferTests.ReplayAfter_AnUnknownCursorNeverIssued_IsTreatedTheSameAsOneThatAgedOut_ResumedFalse`, `ReplayAfter_ACursorOlderThanTheBufferHolds_ResolvesResumedFalse` | `Assert.False() Failure Expected: False Actual: True` (both) | Yes — `cmp` identical, `--no-incremental` rebuild, 7/7 green |
| 4 | `StreamEndpoints.cs` | The `ping`-writing branch deleted (timer still re-armed, nothing written) | `StreamHeartbeatHttpTests.Heartbeat_KeepsTheConnectionAlive_AcrossAnIdleReadTimeoutThatWouldOtherwiseFireWithoutIt` AND `Ping_ArrivesOnTheConfiguredInterval_WithAWellFormedStreamPingBody` | `System.IO.IOException: Unable to read data from the transport connection: Connection timed out.` (idle-read test, failed in ~1s — the FIRST read past `stream.ready` blocked for the full window with nothing else on the wire) / `System.TimeoutException: collectUntil: timed out after 00:00:05` (ping-arrival test) | Yes — `cmp` identical, `--no-incremental` rebuild, 3/3 green |
| 5 | `NatsStreamSignalSubscriber.cs` | `hub.Publish(...)` call deleted (deliberately not called; parameters read via `_ = (...)` so the build still compiles) | `StreamHttpTests.Connect_SendsStreamReady_ThenALiveFramePublishedOnReadmodelOrderUpdated` | `System.TimeoutException: collectUntil: timed out after 00:00:05, collected so far: 1 frame(s).` (only `stream.ready` ever arrived — the live fact never did) | Yes — `cmp` identical, `--no-incremental` rebuild, 1/1 (targeted) green, then full suite re-confirmed 7/7 |
| 6 | `NatsStreamSignalSubscriber.cs` | Every frame forwarded under a fixed `Guid.Empty` `orderId`, regardless of the real subject | `StreamHttpTests.OrderIdFilter_AClientSubscribedToOneOrder_NeverReceivesAnotherOrdersFrames` | `System.TimeoutException: collectUntil: timed out after 00:00:05` (the watched-order filter never matched `Guid.Empty`, so no frame ever arrived) | Yes — `cmp` identical, `--no-incremental` rebuild, 1/1 green |
| 7 | `GatewayHost.cs` | `app.MapStreamEndpoints();` deleted | `StreamHttpTests.GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute` | `Assert.Equal() Failure Expected: OK Actual: BadRequest` — exactly what the test's own docstring predicted: with the literal route gone, `/orders/stream` fell to `GET /orders/{id}`, which rejected `"stream"` as not a valid GUID | Yes — `cmp` identical, `--no-incremental` rebuild, 1/1 green |
| 8 | `CursorGenerator.cs` | `Interlocked.Increment(ref _sequence)` → plain `++_sequence` (a concurrency primitive, so the mutation is a SUBSTITUTION, not a deletion — there is no "delete" for a lock/atomic-increment) | `CursorGeneratorTests.Next_NeverProducesADuplicateCursor_UnderGenuineConcurrentCalls` | `Assert.Equal() Failure: Values differ Expected: 16000 Actual: 9465` — run 3 times to confirm the race is genuinely reproducible, not a one-off: `9465`, `10282`, `12421` distinct cursors out of 16,000 across three separate runs, all FAILED | Yes — `cmp` identical, `--no-incremental` rebuild, re-ran 3 times, all green |

## R55 — `specs/shared/test-matrix.md`

Row updated from TODO (SSE half unproven, gateway half done, projector
half done) to: SSE half **DONE**, citing every test above; web half
remains **TODO**, owed to `apps/web` (features 29/30, both `pending`) —
not built by this feature. The row's overall status stays TODO (the web
half keeps it there), unchanged from before this feature — see the row
itself for the full citation.

## A pre-existing test-infra fragility, found and fixed while verifying

`dotnet test OrderToCash.sln` (the FULL solution — six heavy
`*.IntegrationTests` projects, each booting real Testcontainers, running
as separate processes concurrently) reproduced 8 failures in
`Gateway.IntegrationTests`, **twice in a row**, on a freshly-quiesced
system (`docker ps` showed only the persistent long-running dev stack
between the two runs, no leftover ephemeral containers). Every failure was
the SAME exception — `Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerImpl.BindAsync`
throwing `TaskCanceledException` — within the first ~2.5 seconds of the
run, and **never once** a business-logic assertion. Enumerated directly
(the search result, not a prose sweep):

```
$ grep -n "\[FAIL\]" quality_run.log
DocsAndAnonymousRouteHttpTests.EveryRegisteredRouteExceptTheTwoDocumentedPublicOnes_RejectsAnAnonymousRequestWith401
AuthAndRateLimitHttpTests.Login_ExceedingTheRateLimit_AnswersTheSame429_RegardlessOfWhetherTheCredentialsWereValid
AuthAndRateLimitHttpTests.Login_WithTheWrongPassword_Returns401ProblemJson
AuthAndRateLimitHttpTests.Me_WithoutABearerToken_Returns401
OrdersHttpTests.PlaceOrder_WithNoLines_Returns400ValidationFailed_BeforeAnyRpcCall
AuthAndRateLimitHttpTests.Me_WithAValidBearerToken_ReturnsTheOperatorsIdentity
AuthAndRateLimitHttpTests.ARefusedClient_IsStillServed_OnEveryOtherEndpoint
OrdersHttpTests.PlaceOrder_WhenTheRpcTransportIsUnreachable_Returns503UpstreamUnavailable
```

8 hits, one classification line each: **all 8 are pre-existing test
classes this feature never touched** (`AuthAndRateLimitHttpTests`,
`DocsAndAnonymousRouteHttpTests`, `OrdersHttpTests` — all from feature
`gateway_rest_auth`); **none** of the 43 tests this feature added
(`StreamHttpTests`, `StreamHeartbeatHttpTests`,
`StreamProjectorEndToEndTests`, or any `*.UnitTests` file) ever appeared
in either failure list. All five of those pre-existing classes carry no
`[Collection]` attribute, so each is its own implicit, unnamed xUnit
collection — and DIFFERENT collections run in parallel by default. Every
`[Fact]` in them calls `GatewayTestHost.StartAsync`, binding a real Kestrel
listener; under `dotnet test $SLN`, many such binds raced each other
WITHIN this one assembly, compounded by five OTHER `*.IntegrationTests`
projects' own Testcontainers starting at the same instant.

Two fixes, both within this feature's own file scope
(`tests/Gateway.IntegrationTests/`), both cited honestly rather than
silently:

1. **`AssemblyBehavior.cs`** — `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
   Serialises every collection in THIS assembly (including the five
   implicit per-class ones), closing the WITHIN-assembly instance of the
   race. Reduced the failure count from 8/47 to 2/47 on the next run — a
   genuine, measured improvement, not a full fix.
2. **`GatewayTestHost.cs`** — `StartAsync` now retries up to 5 times,
   PACED explicitly (200ms × attempt, never a tight loop — the exact
   discipline this feature's own brief names, "pace any readiness retry
   loop explicitly"), catching ONLY `TaskCanceledException` (a genuine DI
   configuration failure throws a different exception type and is never
   masked). This closes the residual CROSS-project instance of the race,
   which no change inside this one assembly alone could remove.

**Both changes are cited as a claim, not asserted in prose**: a THIRD full
`./quality.sh` run, after both fixes, produced **`Gateway.IntegrationTests:
47/47, 0 failed`** and `quality.sh exit code: 0` — see "Final verification
run" below. This is a pre-existing fragility in `gateway_rest_auth`'s own
test classes, made newly VISIBLE by this feature's own additional
Kestrel-booting test classes adding to the concurrent-startup load, not a
defect this feature's own SSE code introduced — but it is this feature's
own fix, made and verified in this feature's own scope, because leaving a
known-red `quality.sh` for the next feature to trip over would be worse
than fixing three lines' worth of test-collection behaviour.

## Final verification run

Three full `./quality.sh` passes were run in sequence, each after the
Testcontainers from the previous run had been reaped (`docker ps`
confirmed only the persistent dev stack remained between runs — no
leftover ephemeral containers muddying the next run's own resource
picture):

1. **First pass** — 8/47 failed in `Gateway.IntegrationTests`, all
   pre-existing classes, all `KestrelServerImpl.BindAsync`
   `TaskCanceledException` (documented above). `quality.sh exit code: 1`.
2. **Second pass**, after `AssemblyBehavior.cs` alone — 2/47 failed, same
   exception, same class of pre-existing test. `quality.sh exit code: 1`.
3. **Third pass**, after `GatewayTestHost.cs`'s paced retry too:

```
$ ./quality.sh
── 1. Format check ── dotnet format --verify-no-changes: clean
── 2. Build ── dotnet build: succeeded, 0 warnings, 0 errors
── 3. Test + coverage ── dotnet test: all tests passed
── 4. Coverage summary ── per-project line coverage 0.0%–97.2% (18 reports)
quality.sh finished
quality.sh exit code: 0
```

Per-project totals, counted directly off that third run's own log (never
asserted from memory):

```
$ grep -oE "Failed: *[0-9]+, Passed: *[0-9]+, Skipped: *[0-9]+, Total: *[0-9]+" quality_run4.log \
    | awk -F'Total: *' '{sum+=$2; count++} END {print "projects:", count, "total tests:", sum}'
projects: 18 total tests: 1524

$ grep -oE "Failed: *[0-9]+" quality_run4.log | awk -F': *' '{sum+=$2} END {print "total failed:", sum}'
total failed: 0
```

**18 projects, 1524 tests, 0 failed, 0 skipped.** `Gateway.UnitTests:
193/193` (up from the pre-feature 161 — a net +32: 4 `CursorGeneratorTests`,
7 `ReplayBufferTests`, 6 `StreamHubTests`, 5 `NatsStreamSignalSubscriberTests`,
10 `GatewaySseOptionsTests`). `Gateway.IntegrationTests: 47/47` (up from
the pre-feature 36 — a net +11: 7 `StreamHttpTests`, 3
`StreamHeartbeatHttpTests`, 1 `StreamProjectorEndToEndTests`) — **43 tests
added this feature.** Every `Gateway.IntegrationTests` test ran against
REAL infrastructure: a real `nats:2.14.5-alpine` (`StreamHttpTests`,
`StreamHeartbeatHttpTests`), and real `apache/kafka:4.3.1` +
`mongo:8.3.8` + `nats:2.14.5-alpine` all together, driving the actual
`ProjectorHost` production code (`StreamProjectorEndToEndTests`) — never a
mock.

`dotnet format OrderToCash.sln --verify-no-changes` — clean.

`./init.sh` — exits 0 (`echo $?` confirmed directly): 64 features parsed,
1 `in_progress` at the start of this run (`gateway_sse_push`), SDD
coherence holds, backlog tripwire clean.

## What was NOT done, and why

- **The web half of R55** — `apps/web` (features 29/30, `web_app`/
  `web_component_tests`) are both still `pending`; the read-model/SSE
  chain this feature builds is what they will consume. Not a gap in this
  feature's own scope.
- **A real Billing-responder end-to-end test** and **structured logging in
  `LoginThrottleOptions.FromEnvironment`** — both recorded as deliberate,
  unchanged carry-overs from feature `gateway_rest_auth`'s own report;
  neither is this feature's concern.
- **OTel/`traceId` threading in the malformed-signal-frame log line** —
  feature 27's scope (observability_reliability), and genuinely not needed
  here for the reason ledger row 5 states: this subscriber's diagnostic
  context (the subject) survives a payload decode failure by construction,
  unlike #7's own payload-derived `orderId`.

## Surprises

- **A pre-existing test-infra fragility in `gateway_rest_auth`'s own test
  classes, invisible until this feature's own tests added enough
  concurrent Kestrel-boot load to make it reproduce twice in a row** — see
  "A pre-existing test-infra fragility" above. Found, fixed, and verified
  within this feature's own scope, not silently left for the next one.
- **Subscribing to `StreamHub` BEFORE computing the replay snapshot**, not
  after (the order `StreamEndpoints.StreamAsync` uses), closes a narrow
  race #7's own `stream.controller.ts` does not: reading the buffer first
  and subscribing only afterward leaves a window where a frame published
  in between is neither in the snapshot nor delivered live — an outright
  drop. Subscribing first means the worst case is a harmless DUPLICATE
  (already tolerated by the spec's own at-least-once delivery), never a
  silent miss.
- **`NetworkStream.ReadTimeout` (not `HttpClient.Timeout`, which is an
  ABSOLUTE deadline, not an idle one)** turned out to be the right, simple
  mechanism for proving the heartbeat's necessity — `HttpClient`'s own
  response stream does not support `CanTimeout`, which is exactly why the
  strong heartbeat test uses a raw `TcpClient` instead of the `HttpClient`
  every other test in this feature uses.
- **Reading `orderId` off the NATS subject rather than the JSON payload**
  (ledger row 4) turned out to remove an entire class of #7's own
  diagnostic-context problem (ledger row 5) as a side effect, not by
  design up front — the translation was chosen for wire-fidelity, and the
  removed dependency on payload-headers-as-fallback was noticed only while
  writing the ledger.

---

Files touched, for reference (absolute paths under
`/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-dotnet/`):

**New**: `src/Gateway/Domain/Sse/{CursorGenerator,ReplayBuffer,ReplayResult}.cs`,
`src/Gateway/Application/Stream/{StreamFrame,StreamHub}.cs`,
`src/Gateway/Infrastructure/Messaging/{GatewaySseOptions,NatsStreamSignalSubscriber}.cs`,
`src/Gateway/Presentation/Endpoints/StreamEndpoints.cs`,
`tests/Gateway.UnitTests/{CursorGeneratorTests,ReplayBufferTests,StreamHubTests,NatsStreamSignalSubscriberTests,GatewaySseOptionsTests}.cs`,
`tests/Gateway.IntegrationTests/{SseFrameReader,StreamHttpTests,StreamHeartbeatHttpTests,StreamProjectorEndToEndTests,AssemblyBehavior}.cs`.

**Edited**: `src/Gateway/Presentation/Dto/ResponseDtos.cs`,
`src/Gateway/Infrastructure/GatewayOptions.cs`,
`src/Gateway/Infrastructure/GatewayServiceCollectionExtensions.cs`,
`src/Gateway/GatewayHost.cs`, `src/Gateway/Program.cs`,
`src/Gateway/Presentation/Endpoints/OrdersEndpoints.cs` (docstring only),
`tests/Gateway.UnitTests/OpenApiContractTests.cs`,
`tests/Gateway.IntegrationTests/MongoContainerFixture.cs`,
`tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj`,
`tests/Gateway.IntegrationTests/GatewayTestHost.cs`, `.env.example`,
`specs/shared/test-matrix.md`, `feature_list.json` (single line, id 26
status only).

---

## Fix round — response to `progress/review_gateway_sse_push.md`

Four items from "What must change before re-review": D1 (blocking, a ported idiom with no row and no guard), D2 (blocking, the #7 enumeration filtered by the property under test and dropped two hits), D3 (add file+line citations to the ledger), D4 (a doc comment names a test that does not exist). Per the review's own finding, nothing in `src/Gateway`'s runtime *behaviour* changes in this round — every item below is a guard, a citation or a comment. This section is appended; nothing above it is rewritten.

### D1 — the teardown idiom: a new ledger row, and a guard that reaches it through the endpoint

**Ledger row 10** (extending the table above, not editing it):

| # | #7 relied on | In #8 that property is supplied by | Guard |
|---|---|---|---|
| 10 | Express/Nest's engine-supplied `res.on('close', ...)` teardown hook — `apps/gateway/src/presentation/stream.controller.ts:80-83`: `res.on('close', () => { subscription.unsubscribe(); clearInterval(pingInterval); })`. The lifecycle event is the ENGINE's; #7 never hand-writes the "client went away" detection itself. | There is no ASP.NET Core equivalent event to subscribe to. The property is HAND-BUILT here: `context.RequestAborted` (a `CancellationToken` the framework cancels when the client disconnects) is threaded through every `await` in the read/ping loop (`StreamEndpoints.cs:47-51`, the `cancellationToken` parameter), a `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` swallows the resulting cancellation as the expected "client went away" outcome (`StreamEndpoints.cs:143-153`), and a `finally { hub.Unsubscribe(subscriptionId); }` (`StreamEndpoints.cs:154-157`) is the hand-written teardown itself — the direct translation of #7's `subscription.unsubscribe()`. (`clearInterval(pingInterval)` has no #8 equivalent to leak: `using var pingTimer` at `StreamEndpoints.cs:101` disposes the `PeriodicTimer` by scope exit, a `using`, not a `finally` line — the one part of #7's hook genuinely supplied by the language here rather than hand-built.) | `StreamHttpTests.Disconnect_UnsubscribesFromTheHub_ThroughTheEndpointsOwnTeardown` — real Kestrel, a real client disconnect (`response.Dispose()`), reached through the endpoint (never `StreamHub.Unsubscribe` called directly). Armed below. |

**Why this row was missing.** Every other row in the original table records a translation the implementer noticed while writing the code. This one was not: the `finally` block reads as an obvious, uncontroversial line, and "obvious" is exactly the condition under which a ported idiom goes unrecorded — the review's own framing ("a guard whose assertion cannot detect the defect it names," sixth exhibit) names the risk correctly. The fix is not cleverness, it is the ledger's own discipline applied a tenth time.

**The guard, added to `tests/Gateway.IntegrationTests/StreamHttpTests.cs`** (new `using`s: `Microsoft.Extensions.DependencyInjection`, `OrderToCash.Gateway.Application.Stream`):

```csharp
[Fact]
public async Task Disconnect_UnsubscribesFromTheHub_ThroughTheEndpointsOwnTeardown()
{
    await using var gateway = await StartAsync();
    var token = await LoginAsync(gateway);
    var hub = gateway.Services.GetRequiredService<StreamHub>();
    Assert.Equal(0, hub.SubscriberCount);

    var (response, reader) = await OpenSseConnectionAsync(gateway.Client, token);
    await reader.CollectUntilAsync(f => f.Any(x => x.Event == "stream.ready"), TimeSpan.FromSeconds(5));

    // By the time stream.ready's bytes have reached the client, the
    // endpoint has already called hub.Subscribe() — it happens strictly
    // before the first WriteFrameAsync call — so this is a direct read,
    // never a poll.
    Assert.Equal(1, hub.SubscriberCount);

    response.Dispose();

    var deadline = DateTime.UtcNow.AddSeconds(10);
    while (hub.SubscriberCount != 0 && DateTime.UtcNow < deadline)
    {
        await Task.Delay(50);
    }

    Assert.Equal(0, hub.SubscriberCount);
}
```

This reaches the teardown through the real endpoint, not through `StreamHub` directly — exactly the distinction the review drew against `StreamHubTests.Unsubscribe_RemovesTheSubscriber_FromSubscriberCount`, which calls `Unsubscribe` itself and so proves nothing about whether `StreamEndpoints`'s own `finally` still calls it. `GatewayTestHost` gained one new member for this, `public IServiceProvider Services => _app!.Services;` — the same DI-container-access seam the class's own doc comment already pointed at (`FulfillmentDispatcherRegistrationTests`'s seam, generalised from build-time substitution to run-time resolution), three lines, no other change to that file.

**Arming record** (mutate → `dotnet build --no-incremental` → run named test → capture verbatim FAIL → restore from `cp` backup → `cmp`-verify → `dotnet build --no-incremental` → confirm green):

| Step | Result |
|---|---|
| Baseline (unmutated) | `dotnet test --filter "FullyQualifiedName~Disconnect_UnsubscribesFromTheHub_ThroughTheEndpointsOwnTeardown"` → **Passed! Failed: 0, Passed: 1** |
| Mutation | `StreamEndpoints.cs`'s `finally { hub.Unsubscribe(subscriptionId); }` body replaced with an empty block (comment only) |
| Rebuild | `dotnet build tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj --no-incremental` → 0 warnings, 0 errors |
| Named test, mutated | **FAILED** — `Assert.Equal() Failure: Values differ` / `Expected: 0` / `Actual: 1` (`StreamHttpTests.cs:358`) |
| Restore | `cp` backup restored over the mutated file |
| `cmp` | `cmp /tmp/.../StreamEndpoints.cs.bak src/Gateway/Presentation/Endpoints/StreamEndpoints.cs` → no output, byte-identical |
| Rebuild | `dotnet build --no-incremental` → 0 warnings, 0 errors |
| Named test, restored | **Passed! Failed: 0, Passed: 1** |

The comment marker left in the file during the mutated run (`// ARMED MUTATION — D1 guard, deliberately deleted for the fix round's arming proof. Restored immediately after.`) is not present in the restored file — the `cmp` line above is the proof, not the comment's absence alone.

### D2 — redone #7 enumeration, content-based (not filtered by the property under test), classified per `it(` case

**The corrected command** — union of the original name-based command and a content-based one, run against the sibling `order-to-cash-nestjs` checkout:

```
$ find apps/gateway/src -iname "*.spec.ts" | grep -iE "sse|stream"
apps/gateway/src/application/stream-hub.spec.ts
apps/gateway/src/domain/sse/cursor.spec.ts
apps/gateway/src/domain/sse/replay-buffer.spec.ts
apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.spec.ts
apps/gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts
apps/gateway/src/stream.integration.spec.ts
apps/gateway/src/stream-projector-e2e.integration.spec.ts

$ grep -rln --include=*.spec.ts -e "orders/stream" -e "text/event-stream" -e "StreamHub" -e "stream-hub" -e "sse-test-client" -e "EventSource" apps/gateway/src
apps/gateway/src/application/stream-hub.spec.ts
apps/gateway/src/contract-drift.integration.spec.ts
apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.spec.ts
apps/gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts
apps/gateway/src/stream.integration.spec.ts
apps/gateway/src/stream-projector-e2e.integration.spec.ts
```

Union: **8 files** (the content-based command misses `cursor.spec.ts`/`replay-buffer.spec.ts` — pure-domain files with no textual hit on any of those six strings — and the name-based command misses `contract-drift.integration.spec.ts`, whose filename carries no SSE/stream token at all). This confirms the review's own finding exactly: the two commands disagree on two files, and only their union sees all eight.

**Every `it(`/`it.each(` case in the eight files, one line each — 32 cases, none grouped:**

| # | File:line | #7's own case title (abridged) | Classification | #8 counterpart |
|---|---|---|---|---|
| 1 | `application/stream-hub.spec.ts:5` | "R55 — publishing emits a frame to live subscribers with a fresh cursor" | PORTED | `StreamHubTests.Publish_EmitsAFrameToLiveSubscribers_WithAFreshCursor` |
| 2 | `application/stream-hub.spec.ts:17` | "replayAfter resumes from the buffer, resumed:true, everything after the given cursor" | PORTED | `StreamHubTests.ReplayAfter_ResumesFromTheBuffer_ResumedTrue_EverythingAfterTheGivenCursor` |
| 3 | `application/stream-hub.spec.ts:28` | "replayAfter(undefined) — a fresh connection with no Last-Event-ID — is resumed:false" | PORTED | `StreamHubTests.ReplayAfterNull_AFreshConnectionWithNoLastEventId_IsResumedFalse` |
| 4 | `contract-drift.integration.spec.ts:55` | "every openapi.yaml path+method … is registered as a Nest route, and every Nest route is in openapi.yaml" | NOT APPLICABLE — general whole-API contract-drift check; matched the content grep only because the file also contains an SSE-specific case (#7), no SSE content of its own | `OpenApiContractTests.cs` (general contract drift, out of R55's own scope; this feature only touched its docstring) |
| 5 | `contract-drift.integration.spec.ts:73` | "GET /docs answers 200 text/html — served by middleware, not a Nest route" | NOT APPLICABLE — same reason as #4, no SSE content | `OpenApiContractTests.cs` |
| 6 | `contract-drift.integration.spec.ts:81` | "openapi.yaml declares exactly 17 top-level paths and 18 operations" | NOT APPLICABLE — same reason as #4, no SSE content | `OpenApiContractTests.cs` |
| 7 | `contract-drift.integration.spec.ts:87` | "F9 — GET /orders/stream is registered BEFORE GET /orders/:id, so the literal 'stream' path segment is never swallowed by the :id wildcard" | PORTED — the case the review's D2 finding named as dropped by the original per-file classification | Ledger row 6 (`progress/impl_gateway_rest_auth.md:106`) + `StreamHttpTests.GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute` (the real production route, not only `RoutePrecedenceTests`'s probe route) |
| 8 | `domain/sse/cursor.spec.ts:5` | "produces the openapi.yaml frame-format shape '<epochMs>-<seq>'" | PORTED | `CursorGeneratorTests.Next_ProducesTheOpenApiYamlFrameFormatShape_EpochMsDashSeq` |
| 9 | `domain/sse/cursor.spec.ts:10` | "never repeats a cursor for two frames emitted within the same millisecond" | PORTED | `CursorGeneratorTests.Next_NeverRepeatsACursor_ForTwoFramesMintedWithinTheSameMillisecond` |
| 10 | `domain/sse/cursor.spec.ts:19` | "advances the epoch-millisecond prefix when the clock advances" | PORTED | `CursorGeneratorTests.Next_AdvancesTheEpochMillisecondPrefix_WhenTheClockAdvances` |
| 11 | `domain/sse/replay-buffer.spec.ts:5` | "returns everything after a known cursor, resumed:true" | PORTED | `ReplayBufferTests.ReplayAfter_ReturnsEverythingAfterAKnownCursor_ResumedTrue` |
| 12 | `domain/sse/replay-buffer.spec.ts:14` | "returns an empty missed list when the client is already caught up to the newest cursor" | PORTED | `ReplayBufferTests.ReplayAfter_ReturnsAnEmptyMissedList_WhenTheClientIsAlreadyCaughtUpToTheNewestCursor` |
| 13 | `domain/sse/replay-buffer.spec.ts:21` | "resumed:false, nothing missed, when no Last-Event-ID was sent at all (fresh connect)" | PORTED | `ReplayBufferTests.ReplayAfter_ResumedFalseNothingMissed_WhenNoLastEventIdWasSentAtAll` |
| 14 | `domain/sse/replay-buffer.spec.ts:28` | "the buffer is bounded — a cursor older than the buffer holds resolves resumed:false" | PORTED | `ReplayBufferTests.ReplayAfter_ACursorOlderThanTheBufferHolds_ResolvesResumedFalse` |
| 15 | `domain/sse/replay-buffer.spec.ts:38` | "an unknown cursor (never issued) is treated the same as one that aged out — resumed:false" | PORTED | `ReplayBufferTests.ReplayAfter_AnUnknownCursorNeverIssued_IsTreatedTheSameAsOneThatAgedOut_ResumedFalse` |
| 16 | `domain/sse/replay-buffer.spec.ts:45` | "never grows past its configured capacity" | PORTED | `ReplayBufferTests.Size_NeverGrowsPastItsConfiguredCapacity` |
| 17 | `domain/sse/replay-buffer.spec.ts:54` | "rejects a non-positive capacity" | PORTED | `ReplayBufferTests.Constructor_RejectsANonPositiveCapacity` |
| 18 | `infrastructure/messaging/nats-stream-signal.adapter.spec.ts:24` | "R55 — decodes readmodel.order.updated frames and publishes them onto the StreamHub" | PORTED, at a stronger level (ledger row 4 — real broker, not a mocked connection) | `NatsStreamSignalSubscriberTests.cs` (subject-parsing) + `StreamHttpTests.Connect_SendsStreamReady_ThenALiveFramePublishedOnReadmodelOrderUpdated` |
| 19 | `infrastructure/messaging/nats-stream-signal.adapter.spec.ts:47` | "a malformed frame is logged … and skipped, without breaking consumption of the next well-formed frame" | PORTED | `StreamHttpTests.AMalformedSignalFrame_IsSkipped_WithoutBreakingConsumptionOfTheNextWellFormedFrame` |
| 20 | `infrastructure/messaging/nats-stream-signal.adapter.spec.ts:73` | "stop() unsubscribes both subscriptions" | NOT PORTED as its own case — the case the review's D2 finding named as dropped by per-file classification. See new ledger row 11 below: not applicable by construction, not by omission | `NatsStreamSignalSubscriber.ExecuteAsync`'s shared `stoppingToken` (`NatsStreamSignalSubscriber.cs:59-68`) |
| 21 | `infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:67` | "reads x-correlation-id from the NATS HEADERS … never from the body it failed to decode" | NOT PORTED — not applicable, by construction (ledger row 5): #8 reads `orderId` from the subject, so a malformed-payload frame keeps full identifying context without a headers fallback | n/a — see ledger row 5 |
| 22 | `infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:82` | "omits correlationId entirely … when the frame carries no x-correlation-id header at all" | NOT PORTED — not applicable, same reason as #21 | n/a — see ledger row 5 |
| 23 | `infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:95` | "omits traceId when the adapter is started with NO span active" | NOT PORTED — OTel/`traceId` threading is feature 27's scope (`observability_reliability`), genuinely out of scope here | n/a — feature 27 |
| 24 | `infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts:121` | "threads the REAL active span's traceId when one genuinely IS active" | NOT PORTED — same reason as #23 | n/a — feature 27 |
| 25 | `stream.integration.spec.ts:44` | "sends stream.ready on connect, then a live frame published on readmodel.order.updated.<orderId>" | PORTED | `StreamHttpTests.Connect_SendsStreamReady_ThenALiveFramePublishedOnReadmodelOrderUpdated` |
| 26 | `stream.integration.spec.ts:68` | "orderId filter — a client subscribed to one order never receives another order's frames" | PORTED | `StreamHttpTests.OrderIdFilter_AClientSubscribedToOneOrder_NeverReceivesAnotherOrdersFrames` |
| 27 | `stream.integration.spec.ts:86` | "R55 reconnection — a known Last-Event-ID replays every frame missed since, resumed:true" | PORTED | `StreamHttpTests.Reconnect_AKnownLastEventId_ReplaysEveryFrameMissedSince_ResumedTrue` |
| 28 | `stream.integration.spec.ts:119` | "R55 reconnection — an unknown/aged-out Last-Event-ID answers stream.ready with resumed:false, never an error" | PORTED | `StreamHttpTests.Reconnect_AnUnknownOrAgedOutLastEventId_AnswersStreamReadyResumedFalse_NeverAnError` |
| 29 | `stream.integration.spec.ts:140` | "R55 'without duplicates' — reconnecting with the cursor of an already-received frame never redelivers that frame" | PORTED | `StreamHttpTests.Reconnect_WithACursorOfAnAlreadyReceivedFrame_NeverRedeliversThatFrame` |
| 30 | `stream.integration.spec.ts:245` | "sends a ping frame on the configured interval, with a well-formed StreamPing body" | PORTED | `StreamHeartbeatHttpTests.Ping_ArrivesOnTheConfiguredInterval_WithAWellFormedStreamPingBody` |
| 31 | `stream.integration.spec.ts:271` | "F: ping frames carry no id, and reconnecting with a real content frame's id still resumes" | PORTED | `StreamHeartbeatHttpTests.PingFrames_CarryNoIdLine_AndReconnectingWithARealContentFramesIdStillResumes` |
| 32 | `stream-projector-e2e.integration.spec.ts:136` | "a fact published on the real orders facts topic, consumed by the REAL projector, arrives at a connected SSE client as order.updated and timeline.appended" | PORTED, corrected (ledger row 8 — #7's own F1 rejection) | `StreamProjectorEndToEndTests.AFactPublishedOnTheRealOrdersFactsTopic_ConsumedByTheRealProjector_ArrivesAtAConnectedSseClient` |

32 cases, one line each, nothing grouped. This supersedes the original 7-file/7-line table, which is left above unedited as the historical record of what the first pass actually enumerated.

**New ledger row 11**, for case #20 above (`nats-stream-signal.adapter.spec.ts:73`, "stop() unsubscribes both subscriptions") — the second gap the review's D2 finding named, same teardown family as D1 but a genuinely different mechanism:

| # | #7 relied on | In #8 that property is supplied by | Guard |
|---|---|---|---|
| 11 | `NatsStreamSignalAdapter.stop()` (`infrastructure/messaging/nats-stream-signal.adapter.ts`) calling `this.orderUpdatedSub.unsubscribe()`/`this.timelineAppendedSub.unsubscribe()` explicitly — a hand-called teardown method, exercised by `nats-stream-signal.adapter.spec.ts:73`. | **Not hand-built here — genuinely not applicable, by construction, not by omission.** `NatsStreamSignalSubscriber.ExecuteAsync` (`NatsStreamSignalSubscriber.cs:59-68`) passes ONE shared `stoppingToken` — the token `BackgroundService.StopAsync` cancels on host shutdown, a **.NET host lifecycle mechanism** — into both `connection.SubscribeAsync<byte[]>(wildcardSubject, cancellationToken: stoppingToken)` calls (`NatsStreamSignalSubscriber.cs:72`), and NATS.Client.Core's `SubscribeAsync` is itself a cancellation-aware `IAsyncEnumerable` — a **library mechanism**. Cancelling the one token ends both `await foreach` loops and, per the NATS client's own contract, unsubscribes both underlying subscriptions. Neither mechanism is hand-written in #8, unlike D1's `finally` block — this is the SAME shape as #7's own engine-supplied `res.on('close')`, just supplied by a different engine (the .NET host + the NATS client library, rather than Express/Nest). | Per the ledger's own rule ("a guard test is required … where the property was supplied by #7's engine … and must be hand-built here"), no dedicated guard is mandated — this property is not hand-built in #8. Indirect evidence only: every `GatewayTestHost.DisposeAsync` call (after every test in this suite) calls `_app.StopAsync()`, and a hung subscription loop would hang every test's teardown rather than only this feature's own tests — none has, across 8 `StreamHttpTests` cases, 3 `StreamHeartbeatHttpTests` cases and the full `quality.sh` run below. |

This is the honest answer, not a convenient one: the review asked whether this row's answer was "ledger answer, unwritten" and it is now written, with the mechanism named precisely enough to distinguish it from D1's (which genuinely does require a guard, because it genuinely is hand-built).

### D3 — file and line citations added to every ledger row's "#7 relied on" half

The reviewer verified every citation against the #7 checkout and confirmed all were factually accurate; only the line numbers were missing. Addendum, keyed to the original table's row numbers (rows 1-9 above, unedited):

| Row | File:line |
|---|---|
| 1 | `apps/gateway/src/application/stream-hub.ts:37` (`private readonly subject = new Subject<StreamFrame>();`) and `:47` (`this.subject.next(frame);`) |
| 2 | `apps/gateway/src/domain/sse/cursor.ts:18` (`this.sequence += 1;`) |
| 3 | `apps/gateway/src/domain/sse/replay-buffer.ts:32` (`push(cursor: string, value: T): void {`) and `:44` (`replayAfter(cursor: string | undefined): ReplayResult<T> {`) — neither method takes a lock |
| 4 | `apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.ts:31` (`const orderUpdateCodec = JSONCodec<{ orderId: string } & Record<string, unknown>>();`) through `:43` (`this.hub.publish('timeline.appended', decoded.orderId, decoded);`) |
| 5 | `apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.ts:83` (`const correlationId = message.headers?.get('x-correlation-id');`) through `:90` (`console.error(JSON.stringify({...`) |
| 6 | `progress/impl_gateway_rest_auth.md:106` (the ledger row itself — `#7 relied on` half cites #7's `contract-drift.integration.spec.ts` finding F9, not a #7 source file, since this row's own subject is route-registration ORDER, not a line of production code) |
| 7 | `apps/gateway/src/presentation/stream.controller.ts:1-8` (the header comment giving the `@Sse()`/`Cache-Control` reason) |
| 8 | `progress/history.md:1041` (#7's own `gateway_sse_push` effort record: `"round 1 review … verdict REJECTED, 1 blocking (F1: the E2E booted the real, unmodified apps/projector under tsx, the compiler this project abandoned everywhere for silently mis-resolving bare-typed DI, behind a header comment that falsely claimed equivalence to pnpm dev:projector) + F3/F4/F5 minor"`) |
| 9 | n/a — an engine claim probed by #8 itself (`NetworkStream.ReadTimeout`), not a citation of #7's own code; row 9's text already says so |
| 10 | `apps/gateway/src/presentation/stream.controller.ts:80-83` (`res.on('close', () => { subscription.unsubscribe(); clearInterval(pingInterval); });`) — see D1 above |
| 11 | `apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.spec.ts:73` (the case title `'stop() unsubscribes both subscriptions'`) — see D2 above |

### D4 — the stale test-class reference

`StreamEndpoints.cs:34`'s doc comment named `OrdersStreamRouteTests`, which does not exist. Fixed to name the real guard, `StreamHttpTests.GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute`. Re-verified clear:

```
$ grep -rn "OrdersStreamRouteTests" --include=*.cs .
$ echo "exit: $?"
exit: 1
```

(`grep`'s own exit code 1 means zero matches — the string is gone from the tree, source and tests both.)

### Fix-round verification

```
$ dotnet build tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj --no-incremental
Build succeeded. 0 Warning(s), 0 Error(s).

$ dotnet test tests/Gateway.IntegrationTests --filter "FullyQualifiedName~Disconnect_UnsubscribesFromTheHub_ThroughTheEndpointsOwnTeardown"
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

`Gateway.UnitTests`: unaffected by this round (no unit-level change), left at 193/193 from the original pass. `Gateway.IntegrationTests`: 47 → **48** (the one new `StreamHttpTests` case). `./quality.sh` was re-run in full after all four items landed:

```
$ ./quality.sh
── 1. Format check ── dotnet format --verify-no-changes: clean
── 2. Build ── dotnet build: succeeded, 0 warnings, 0 errors
── 3. Test + coverage ── dotnet test: all tests passed
── 4. Coverage summary ── per-project line coverage 0.0%-97.2% (18 reports)
quality.sh finished
quality.sh exit code: 0
```

Per-project totals, counted directly off that run's own log:

```
$ grep -oE "Failed: *[0-9]+, Passed: *[0-9]+, Skipped: *[0-9]+, Total: *[0-9]+" quality_fixround.log \
    | awk -F'Total: *' '{sum+=$2; count++} END {print "projects:", count, "total tests:", sum}'
projects: 18 total tests: 1525

$ grep -oE "Failed: *[0-9]+" quality_fixround.log | awk -F': *' '{sum+=$2} END {print "total failed:", sum}'
total failed: 0
```

**18 projects, 1525 tests (1524 + the one D1 guard), 0 failed, 0 skipped.** `./init.sh` re-run, exit 0.

### `feature_list.json`

Single-line edit: id 26's `status` field `in_progress` → `in_review`. `git diff` on that file, checked before finishing, shows exactly that one changed line for id 26 plus the seven pre-existing hunks already in the working tree (ids 25, 40, 41 `done`, an id-56 acceptance edit, entries 61-65) — all left untouched. No `git checkout --` was run on this file, at any point in this round.

### What this round did not touch

No file under `src/Gateway/` changed behaviourally: `StreamEndpoints.cs`'s only net change is the D4 comment fix (a doc comment, restored to its pre-mutation body for D1's arming in between). `GatewayTestHost.cs` gained one property (`Services`). `StreamHttpTests.cs` gained one test and two `using`s. `progress/impl_gateway_sse_push.md` (this file) and `specs/shared/test-matrix.md` gained the citations above. No other service, no new NuGet package.

---

## Leader addendum — ledger row 11's evidence, corrected (review finding R2-1)

> Written by the leader at the approval of id 26, applying the round-2 review's own correction verbatim. **Nothing above is rewritten**; this section supersedes row 11's *evidence* clause only. Its conclusion — that no guard is owed — stands and was independently confirmed.

Row 11 argues that #7's `stop() unsubscribes both subscriptions` case needs no #8 counterpart because the property is supplied by `BackgroundService`'s `stoppingToken` and the NATS client's cancellation-aware `IAsyncEnumerable`, rather than by anything hand-built. **That conclusion is right and the reviewer verified the mechanism.** The *evidence* offered for it could not fire, which is the same defect one level up: a claim resting on something nobody had made fail.

The review probed it — one consume loop given `CancellationToken.None` — and measured **1 m 35.5 s mutated against 1 m 35.4 s restored** across three host start/stop cycles. Nothing hangs, nothing slows, nothing turns red.

**The corrected clause, and it is strictly stronger than what it replaces:**

> *"…and this property is not independently observable in #8: a consume loop that ignores the stopping token still costs nothing measurable — verified by probe, one loop given `CancellationToken.None`, no test slowed by so much as a second across three host start/stop cycles. That unobservability, not an absence of effort, is why no guard is written."*

**Why this matters beyond one row.** *"No guard is owed here"* is a claim like any other, and the honest justification for it is not *"the framework handles it"* — it is **"here is what I did to try to make it fail, and here is why nothing could."** A row that says the first without the second is indistinguishable, to any later reader and to assessment #9, from a row where nobody tried.

This is the **second** time in this one feature that a claim about a teardown path rested on something nobody had made fail — D1 was the first, and there the answer was a real guard. Together they set the question the guard-hardening loop's instrument must ask of **every** ledger row: **"if this row's stated evidence were false, what would turn red, and has anyone seen it?"**

**Also carried, non-blocking and not fixed here** (review findings R2-2 and the two citation imprecisions, recorded so they are not lost): `GatewayTestHost.cs:70-79` disposes the partially built `WebApplication` on a `TaskCanceledException` but not on the propagating path — one line. Ledger row 5 attaches a snippet to `:90` that lives at `:85-86` inside an otherwise correct range, and row 10's **#8-side** line numbers are one stale because the D4 fix inserted a line above them.
