# Review — feature 26 `gateway_sse_push`

**Verdict: REJECTED.** Feature set `in_review` → `in_progress` in `feature_list.json` (id 26, line 394 — single-line edit; `git diff` on that file shows exactly one changed line belonging to id 26, and `git checkout --` was never run on it).

This is a strong build. The replay buffer, the cursor generator, the hub, the frame writer and the real-projector end-to-end test are all better than #7's, and three of my four "does this guard actually bite" probes killed cleanly on the first attempt — including the two the brief singled out (`ping` carries no `id`, and the heartbeat's *strong* form). The rejection rests on one defect and one method failure, both in the same place: **the stream's teardown path — #7's `res.on('close')` idiom — has no ledger row and no test at all, and I proved it by deleting the unsubscribe and watching 193/193 and 10/10 stay green.**

## What I ran (independent verification, not a re-run of the world)

I did **not** re-run the 18-project / 1524-test solution. That claim is the implementer's and is not what is under test here; the claims under test are the two acceptance bullets, the `ping` absence rule, the E2E's honesty, the scope expansion, the ledger and the #7 enumeration.

| Command | Result |
|---|---|
| `dotnet build OrderToCash.sln --no-incremental` | 0 warnings, 0 errors |
| `dotnet test tests/Gateway.UnitTests` | **193 passed**, 0 failed |
| `dotnet test tests/Gateway.IntegrationTests` (whole assembly, real NATS / Mongo / Kafka / MS-SQL containers, includes the real-projector E2E) | **47 passed**, 0 failed, 5 m 13 s |
| `dotnet test tests/Architecture.Tests` (NetArchTest — run, not eyeballed) | **16 passed**, 0 failed |
| `./init.sh` | exit **0** |
| `diff -rq specs/shared <#7>/specs/shared` | one line of output: `test-matrix.md` differs. Nothing else. |
| 5 mutation probes, each `dotnet build --no-incremental` then the named suites | **4 killed, 1 survived** |

Every mutated file was restored from a `cp` backup taken before mutation and verified byte-identical with `cmp`; each restore was followed by a forced `--no-incremental` rebuild before the confirming green run. The three files I touched (`StreamEndpoints.cs`, `ReplayBuffer.cs`, `NatsStreamSignalSubscriber.cs`) are all `cmp`-identical to their backups now, and the endpoint's `finally` block was re-read on disk after the last restore.

## Mutation probes

| # | Mutation | Family | Suites run | Outcome |
|---|---|---|---|---|
| P1 | `StreamEndpoints.cs:134` — `ping` given `id: pingCursor` instead of `id: null` | absence | `StreamHeartbeatHttpTests` | **KILLED** — `PingFrames_CarryNoIdLine_AndReconnectingWithARealContentFramesIdStillResumes` failed: `Assert.Null() Failure: Value is not null / Expected: null / Actual: "1788941319535-3"` |
| P2 | `StreamEndpoints.cs` — heartbeat **stops after the first two pings** (not deleted; the first two still arrive) | emission, discriminating | `StreamHeartbeatHttpTests` (all 3) | **KILLED, and it discriminates**: the weak test `Ping_ArrivesOnTheConfiguredInterval_…` still **passed**, while `Heartbeat_KeepsTheConnectionAlive_AcrossAnIdleReadTimeoutThatWouldOtherwiseFireWithoutIt` failed in 1 s with `System.IO.IOException : Unable to read data from the transport connection: Connection timed out` (inner `SocketException`). The strong form is genuinely stronger, and it is the one carrying bullet 2 |
| P3 | `ReplayBuffer.ReplayAfter` — returns **one fewer** frame (drops the newest missed entry) — the direction the implementer's own table did *not* probe | off-by-one, reverse | `Gateway.UnitTests`, then `StreamHttpTests` `Reconnect_*` | **KILLED twice over** — unit: `ReplayBufferTests.ReplayAfter_ReturnsEverythingAfterAKnownCursor_ResumedTrue`, `…ACursorOlderThanTheBufferHolds_ResolvesResumedFalse`, `StreamHubTests.ReplayAfter_ResumesFromTheBuffer_…` (3 of 193). HTTP: `Reconnect_AKnownLastEventId_ReplaysEveryFrameMissedSince_ResumedTrue` **and** `Reconnect_WithACursorOfAnAlreadyReceivedFrame_NeverRedeliversThatFrame` both failed (`collectUntil: timed out … collected so far: 1 frame(s)`), while `Reconnect_AnUnknownOrAgedOutLastEventId_…` correctly stayed green |
| P4 | `NatsStreamSignalSubscriber.cs:84` — the `eventId` value **corrupted on the wire** (regex-replaced with `00000000-…`), everything else intact | **payload**, not deletion | `StreamHttpTests` (all 7) | **KILLED** — `Reconnect_WithACursorOfAnAlreadyReceivedFrame_NeverRedeliversThatFrame`: `Assert.Equal() Failure: Values differ / Expected: e27ffa92-590d-4898-bb49-e6568cf18596 / Actual: 00000000-0000-0000-0000-000000000000`. One of seven catches a wrong payload field; the other six count and filter frames without opening them |
| P5 | `StreamEndpoints.cs:154-157` — the `finally`'s `hub.Unsubscribe(subscriptionId)` **deleted** (a disconnected SSE client is never unsubscribed) | teardown | `Gateway.UnitTests` **and** `StreamHttpTests` + `StreamHeartbeatHttpTests` | **SURVIVED — 193/193 and 10/10 green.** Defect D1 |

## Acceptance bullets

**Bullet 1 — "client reconnect resumes without duplicates" — MET, and countable in both directions.** A valid `Last-Event-ID` replays exactly the missed frames: not one more (`Reconnect_WithACursorOfAnAlreadyReceivedFrame_NeverRedeliversThatFrame` asserts `Assert.Single`, plus `DoesNotContain` on both the cursor and the already-received `eventId`), and not one fewer (P3 above, killed at unit and HTTP level). An unknown cursor answers `resumed: false` and never an error (`Reconnect_AnUnknownOrAgedOutLastEventId_AnswersStreamReadyResumedFalse_NeverAnError`, and the aged-out case at `ReplayBufferTests.ReplayAfter_ACursorOlderThanTheBufferHolds_ResolvesResumedFalse`). The "resumes after, never at-or-before" boundary is exclusive by construction and armed in both directions.

**Bullet 2 — "heartbeat keeps the connection alive" — MET, in the strong form.** The suite carries both strengths, and P2 shows the difference is real rather than rhetorical: with the heartbeat degraded rather than deleted, the ping-arrival test still passes and the survival test dies. `NetworkStream.ReadTimeout` is a genuine per-read idle timeout and the synchronous `Read` is the member that honours it — the test's own comment says so and is correct.

**The `ping` absence rule — GUARDED.** P1 killed it. `openapi.yaml:388-408` states the rule and its reasoning; `StreamHub.MintCursor` never pushes to the buffer (`StreamHubTests.MintCursor_NeverAddsToTheReplayBuffer`), `WriteFrameAsync` omits the `id:` line when `id is null`, and the wire-level test observes a real `ping` arriving *after* a real content frame with `Id == null` and then proves the content frame's id still resumes. This is the one rule the brief called out and it is the best-guarded thing in the feature.

## R55 → test mapping

| Half of R55 | Named tests | Verified |
|---|---|---|
| SSE frame mechanics (cursor, replay buffer, hub) | `CursorGeneratorTests` (4), `ReplayBufferTests` (7), `StreamHubTests` (6) | Yes — all run in my 193; P3 proves the replay assertions bite in both directions |
| `GET /orders/stream` over the wire | `StreamHttpTests` (7) against real `nats:2.14.5-alpine` and real Kestrel | Yes — all 7 ran green in my 47; P1/P3/P4 killed four of them under mutation |
| Heartbeat | `StreamHeartbeatHttpTests` (3) | Yes — P2 |
| `Kafka fact → real projector → NATS → SSE client` | `StreamProjectorEndToEndTests` (1) | Yes — ran green inside my whole-assembly run against real Kafka + Mongo + NATS |
| Web half | none — `apps/web` features 29/30 are `pending` | Correctly left TODO; the row's overall status stays TODO, which is honest |

`specs/shared/test-matrix.md`'s R55 row was updated to name every test above and to say plainly which half remains unproven. The R54/R63 rows and the summary table in the same uncommitted diff belong to feature 25, not to this feature.

## Defects

### D1 — BLOCKING. The teardown idiom: no ledger row, and no test anywhere. Proved by mutation.

- **#7:** `apps/gateway/src/presentation/stream.controller.ts:80-83` — `res.on('close', () => { subscription.unsubscribe(); clearInterval(pingInterval); })`. The lifecycle hook is **Express's**, i.e. the engine's.
- **#8:** `src/Gateway/Presentation/Endpoints/StreamEndpoints.cs:143-157` — the property is hand-supplied by the injected `CancellationToken` (`HttpContext.RequestAborted`) unwinding the `await` loop, plus `try/catch (OperationCanceledException)/finally { hub.Unsubscribe(subscriptionId); }` and `using var pingTimer`.
- **This is the ledger's own shape** — *"#7 relied on X (an engine-supplied close event); in #8 that property is supplied by Y (a hand-written `finally` plus a cancellation token that must actually be the request's)"* — and there is **no row for it** among the nine, so no guard was ever named for it.
- **Nothing tests it.** P5: replacing the `finally` body with a no-op left `Gateway.UnitTests` at **193/193** and `StreamHttpTests` + `StreamHeartbeatHttpTests` at **10/10**. `StreamHub.SubscriberCount` exists and is documented as a "test/diagnostic seam", and the only test that reads it (`StreamHubTests.Unsubscribe_RemovesTheSubscriber_FromSubscriberCount`) calls `Unsubscribe` directly — **never through the endpoint**, which is exactly the "the guard does not execute the code the row is about" failure `CLAUDE.md` names as the ledger's current residual risk.
- **Why it matters.** If the cleanup path is ever wrong, every disconnected SSE client leaks a 256-slot `Channel<StreamFrame>` and a `ConcurrentDictionary` entry for the life of the process, and `StreamHub.Publish` iterates every one of them on every fact. The clients of this endpoint are browser `EventSource`s, whose defining behaviour is to disconnect and reconnect — this feature *implements* that reconnection. The code is correct today; that is precisely the case `CLAUDE.md` says must still be guarded ("#7 learned this twice, on two different features, both correct code with no guard").
- **Remedy.** One ledger row, and one named guard that goes through the endpoint: open an SSE connection over real Kestrel, assert the hub has one subscriber, dispose the response, then poll `SubscriberCount` back to 0 against a deadline. `GatewayTestHost` currently exposes only `Client`, so it needs to expose the host's `IServiceProvider` (or the `StreamHub`) — a three-line change to test infrastructure this feature already edits. Arm it by deleting the `Unsubscribe` and record the verbatim failure.

### D2 — BLOCKING. The #7-test enumeration filtered by the property it was testing, and it dropped hits.

The command in the report is `find apps/gateway/src -iname "*.spec.ts" | grep -iE "sse|stream"` — the candidate set is selected by **whether the filename mentions the thing under test**, which is the shape `CLAUDE.md` binds against ("a sweep must not filter by the property it is testing … ask what a violation would do to the candidate list"). A #7 guard for SSE behaviour living in a file not named for SSE is invisible to it, and one is:

```
$ grep -rln --include=*.spec.ts -e "orders/stream" -e "text/event-stream" -e "StreamHub" -e "stream-hub" -e "sse-test-client" -e "EventSource" apps/gateway/src | sort
apps/gateway/src/application/stream-hub.spec.ts
apps/gateway/src/contract-drift.integration.spec.ts
apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.spec.ts
apps/gateway/src/infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts
apps/gateway/src/stream.integration.spec.ts
apps/gateway/src/stream-projector-e2e.integration.spec.ts
```

One classification line each: five are already in the report's list; **`contract-drift.integration.spec.ts:87` is not** — #7's F9 case, *"GET /orders/stream is registered BEFORE GET /orders/:id, so the literal 'stream' path segment is never swallowed by the :id wildcard"*. (The two pure-domain files `cursor.spec.ts`/`replay-buffer.spec.ts` do not match on content and are in the report's list already; the union of the two commands is 8 files, the report enumerated 7.) **Nothing was lost behaviourally** — `StreamHttpTests.GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute` covers it and is armed (implementer's row 7). The finding is that the method could not have told you that.

The second half is the same failure one level down: the classification is **per file** where the rule now asks per assertion ("enumerate #7's test files for the mechanism and classify each assertion"). At file granularity one #7 case disappears without a line of its own — `apps/gateway/src/infrastructure/messaging/nats-stream-signal.adapter.spec.ts:73`, **`it('stop() unsubscribes both subscriptions')`**. #8 has no counterpart and no "not ported, because…" line, and it is the *same teardown family as D1*: the property (both NATS loops end when the host stops) is now supplied by `BackgroundService`'s `stoppingToken` threaded into `SubscribeAsync`, which is a legitimate answer — but it is a ledger answer, and it is unwritten.

**Remedy.** Re-run the enumeration with a command that does not filter on the property (content-based, as above, unioned with the name-based one), and classify **each `it(` case** of the eight files, one line per case: ported / deliberately not ported with the reason / not applicable with the mechanism that supplies it here.

### D3 — Minor, must be fixed before re-review. Ledger rows cite #7 files without lines.

`CLAUDE.md` on disk (the clause added in phase 13, after ids 40/41) requires the *"#7 relied on X"* half to be *"read out of #7's checkout, with a file and line … Cite the file and line in the row."* Rows 1-5 and 7-8 cite files and identifiers but no lines. I checked every one against the #7 checkout and **all are factually accurate** — `cursor.ts:18` `this.sequence += 1`, `replay-buffer.ts:32-44` (unsynchronised `push`/`replayAfter`), `stream-hub.ts:37,47` (`new Subject<StreamFrame>()` / `this.subject.next(frame)`), `nats-stream-signal.adapter.ts:31-43` (`JSONCodec<{orderId: string} & …>`, `decoded.orderId`) and `:83-90` (`headers?.get('x-correlation-id')`, `console.error(JSON.stringify(...))`), `stream.controller.ts:1-8` (the `@Sse()` `Cache-Control` reason), and row 8's account of #7's F1 rejection matches #7's own `progress/history.md:1041` verbatim. So this is a formal shortfall, not a factual one — add the line numbers, which is the cheapest item on this list.

### D4 — Minor. A doc comment names a test class that does not exist.

`src/Gateway/Presentation/Endpoints/StreamEndpoints.cs:34` says *"`OrdersStreamRouteTests` proves this holds for THIS real production route"*.

```
$ grep -rn "OrdersStreamRouteTests" --include=*.cs .
src/Gateway/Presentation/Endpoints/StreamEndpoints.cs:34:/// <c>Map*</c> call order." <c>OrdersStreamRouteTests</c> proves this holds
```

One hit — the comment itself. The real guard is `StreamHttpTests.GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute`. A comment naming a guard is a claim like any other, and this one is false; a reader who greps for it concludes the guard was deleted.

## The scope expansion — ruled: in scope, sound, and it weakens nothing

The `gateway_rest_auth` test-infra fix (`tests/Gateway.IntegrationTests/AssemblyBehavior.cs`, new; `GatewayTestHost.StartAsync`'s paced bind retry) was in scope and is correctly made.

- **In scope**: both files are inside `tests/Gateway.IntegrationTests/`, an assembly this feature already extends with three new Kestrel-booting classes — which is what made the latent race reproduce. The alternative was leaving a red `quality.sh` for the next feature, and the report discloses the change rather than burying it, with three full runs (8/47 → 2/47 → 0/47) as the evidence.
- **It changes no assertion.** `DisableTestParallelization` reorders execution; the retry catches **only** `TaskCanceledException` and only four times before propagating. No test in the assembly asserts that host startup fails — `grep -rn "Assert.Throws\|ThrowsAsync" tests/Gateway.IntegrationTests/*.cs` returns five hits, all in `NatsRpcClientIntegrationTests` and all about RPC errors, none about `StartAsync` — so nothing a retry could mask is under test.
- **Feature 25's armed guards still bite.** The whole assembly ran 47/47 for me including `DocsAndAnonymousRouteHttpTests.EveryRegisteredRouteExceptTheTwoDocumentedPublicOnes_…`, whose D2 rewrite derives the protected set by **subtraction** from a literal public pair — so this feature's brand-new `/orders/stream` route is auth-covered automatically, and I confirmed the mechanism by reading it rather than assuming it.
- One nit, not blocking: in `GatewayTestHost.StartAsync`, a **non**-`TaskCanceledException` from `app.StartAsync()` leaves the partially built `WebApplication` undisposed. Wrapping the failure path is a one-line improvement.

## The end-to-end test and #7's F1 — claim verified true

#7 was rejected because a header comment claimed a `tsx`-spawned child projector was equivalent to the real dev command. I checked #8's claim against the code rather than accepting it:

- `StreamProjectorEndToEndTests.StartProjectorAsync` calls **`ProjectorHost.CreateBuilder(args: [], configure: …)`** — the same static entry `src/Projector/Program.cs` itself calls. There is no child process, no alternative compiler and no test-only fork of the projector graph; `AddProjector`, `KafkaFactStreamSubscriber`, `ProjectionApplyService` and `NatsUpdateSignalPublisher` are the production types.
- The class remarks **name the two differences instead of asserting equivalence**: one process rather than two, and configuration by delegate rather than environment variables. Both are true — `Program.cs` differs from the test in exactly those two respects and in nothing else.
- The test asserts payload content (`eventId`, `status`, `eventType`, `orderId`), not merely that two frames arrived, and it ran green in my whole-assembly run against real Kafka, Mongo and NATS.

That is the transferable lesson honoured: the harness comment is a claim, and this one holds.

## Other observations (not defects)

- **Subscribe-before-snapshot** (`StreamEndpoints.cs:76`, ahead of `hub.ReplayAfter`) is a deliberate divergence from #7, and the right one: it trades a spec-tolerated duplicate (`openapi.yaml` documents at-least-once and `eventId` dedup) for eliminating a silent drop. It is documented in the code and in the report's "Surprises"; it deserves a line in the ledger as a *divergence* row when the ledger is revised.
- **Ledger row 9** states which direction its engine claim was probed and why the claim is not two-party. That is the right form, and P2 independently confirms both directions.
- Reading `orderId` off the NATS **subject** rather than the payload (row 4) is genuinely stronger, and it is what makes row 5's "not applicable" honest rather than convenient — the subject survives a payload that will not parse, which `AMalformedSignalFrame_IsSkipped_…` proves against a real broker.
- `.env.example` uses #7's own two variable names and defaults (`GATEWAY_SSE_BUFFER_CAPACITY=500`, `GATEWAY_SSE_PING_INTERVAL_MS=15000`, matching #7's `.env.example:352-355`). Zero new NuGet packages, confirmed: `src/Gateway/Gateway.csproj` and `Directory.Packages.props` carry no addition for this feature; `Channel<T>` and `PeriodicTimer` are BCL.

## CHECKPOINTS walk

**C1 — the harness is complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds the five agents
- [x] Every agent definition declares its model
- [x] `./init.sh` exits 0 — run by me, exit 0

**C2 — state is coherent**
- [x] At most one feature `in_progress` (one, id 26, after this review's transition)
- [x] Every status is in `rules.valid_status`
- [x] Every `done` feature has passing tests associated with it
- [x] `progress/current.md` describes the active session
- [x] No `blocked` feature

**C3 — architecture is respected**
- [x] No framework reference inside any `Domain/` folder — `tests/Architecture.Tests` **run**, 16/16, and its `DomainAssemblies` list includes `OrderToCash.Gateway`. The new `Domain/Sse` types use only `Interlocked`, `LinkedList<T>` and `Func<DateTimeOffset>`
- [x] No cross-service database access — `src/Gateway` references no EF Core or SqlClient; the `src/Projector` project reference is **test-only**, in `tests/Gateway.IntegrationTests.csproj:64`, with a comment stating so, matching the existing `src/Fulfillment` precedent
- [x] No shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs` — `Gateway.csproj:25-33`
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` (architecture test)
- [x] `src/SharedKernel` still has zero `PackageReference` (architecture test)
- [x] No `decimal` in domain arithmetic — none introduced; this feature carries no money
- [x] Every interaction classifiable — Kafka carries the facts into the projector; the Gateway's inbound `readmodel.*` subscription is the projector's ephemeral **update signal**, NATS core pub/sub, ratified at the gate that approved `specs/projector_read_model/design.md` §7; the Gateway's own RPC remains request/reply. No Kafka-as-request-bus, no RPC-for-facts
- [x] No stray debug logging, no context-free TODOs — one structured `LogWarning` carrying the subject; `grep` for `TODO|FIXME|HACK` across every file this feature added returns nothing

**C4 — verification is real**
- [ ] **`./quality.sh` passes** — not re-run by me (the implementer reports exit 0, 18 projects / 1524 tests / 0 failed). My own runs cover 193 + 47 + 16 with 0 failed. The box is left open only because D1 means a branch ships unguarded, not because a run is known bad
- [x] Domain tests are pure — `CursorGeneratorTests`, `ReplayBufferTests` reference only the domain types
- [x] Integration tests use Testcontainers against real MsSql / Kafka / NATS / MongoDB — verified by watching the containers come up during my runs
- [x] Coverage thresholds — coverlet gate passed in the implementer's `quality.sh`; not re-run
- [x] No Jest anywhere
- [ ] **Every branch that must be guarded is guarded** — D1: the teardown branch is not

**C5 — the session closed cleanly**
- [x] No suspicious untracked files
- [ ] `progress/history.md` entry with effort record — **not appended; the feature is not closed**
- [x] `feature_list.json` reflects the true state (id 26 → `in_progress`)
- [ ] The human has been told what was done and how to test it — owed after the fix round
- [x] Claude did not commit

**C6 — SDD** — not applicable, `sdd: false`. The one applicable box:
- [x] R55 is covered by concrete named tests recorded in `specs/shared/test-matrix.md`

**C7 — spec-reuse fidelity**
- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — verified by `diff -rq` against the #7 checkout, one line of output
- [x] No silent fork — the `openapi.yaml` `ping`-has-no-id rule was *inherited* from #7's copy (last touched by `b6c6506`, the verbatim copy commit), not written here
- [x] The `R<n>` ids are #7's, and R55's SSE half genuinely holds here
- [ ] n8n workflows fired against the .NET Gateway — still unexercised, carried from feature 25, not this feature's bullet
- [ ] Black-box API script — same
- [ ] `progress/history.md` effort record — owed at approval
- [ ] README benchmark section — owed at phase close

## What must change before re-review

1. **D1** — add the teardown ledger row (*#7's `res.on('close')` at `stream.controller.ts:80-83`; here, `HttpContext.RequestAborted` + `finally`*) **and** a named guard that reaches it through the endpoint, not through `StreamHub` directly. Arm it by deleting the `Unsubscribe` and record the verbatim failure. This is the whole reason for the rejection; the other three are cheap.
2. **D2** — redo the #7 enumeration with a command that does not filter on the property under test, and classify **each `it(` case**, not each file. Give `contract-drift.integration.spec.ts:87` and `nats-stream-signal.adapter.spec.ts:73` their own lines.
3. **D3** — add file **and line** citations to every ledger row's *"#7 relied on"* half. All the claims are correct; only the citations are missing.
4. **D4** — fix `StreamEndpoints.cs:34` to name the test that exists.

Nothing in `src/Gateway`'s behaviour needs to change. Every one of these is a guard, a citation or a comment.

## Phase 13 is **not** closed by this review

Ids **56, 60, 61, 63, 64 and 65** remain `pending` in phase 13, so **no phase-13 closing assessment is due** and none is written here.

**On the leader's grouping — I agree with both halves, and the second one more strongly than the first.** Id 56 (every env read in every `Program.cs` deletable with the suite green) genuinely is its own feature: its cause is that no test project compiles a composition root, its blast radius is six `Program.cs` files, and this feature adds a seventh env-read pair (`GATEWAY_SSE_BUFFER_CAPACITY`, `GATEWAY_SSE_PING_INTERVAL_MS`) whose `FromEnvironment()` is unit-tested while its *call site* in `Program.cs:16` is not — exactly id 56's shape, arriving one more time. And grouping 60/61/63/64/65 as one guard-hardening loop is right for the reason the map already gives: they share a single cause, *a guard whose assertion cannot detect the defect it names*. **D1 above is a sixth instance of that same cause**, so when the fix round lands, consider whether the teardown guard belongs in that loop's instrument rather than as a one-off — the question "does this test execute the code the row is about?" is the loop's real subject, and it now has six exhibits rather than five.

**For the effort record when this closes** (not written to `progress/history.md` yet — the feature is not done): #7's counterpart was **2 implementation passes + 2 completed review passes, rejected once on F1, traceable total ≈1 h 22 min**, plus an interrupted review attempt that produced no artefact. #8's round-1 implementation ran ≈08:20 → 09:51 by artefact mtimes (`progress/current.md` 08:20:11 to `progress/impl_gateway_sse_push.md` 09:51:20, ≈1 h 31 min, including three full `quality.sh` passes and the `gateway_rest_auth` test-infra fix), and this review ≈09:55 → 10:33, ≈38 min, most of it container time — five mutation probes and a 47-test real-infrastructure assembly at 5 m 13 s a run. **#8's two traceable passes already total ≈2 h 09 min against #7's ≈1 h 22 min for the whole feature, and a fix round and a re-review are still owed**, and the honest confound is that #8's round 1 built more (a real in-process projector E2E instead of #7's rejected spawned one, a strong-form heartbeat test #7 never had, a concurrency guard on the cursor) and fixed a neighbouring feature's flaky harness on the way. The comparison to record at approval is passes and cause, not the ratio alone: **#7 was rejected here for a false equivalence claim in a harness comment; #8 is rejected here for a ported idiom with no row and no guard** — different defect, same family as the two rejections that preceded it this phase.

---

# Review round 2 — feature 26 `gateway_sse_push`

**Verdict: APPROVED.** Feature set `in_review` → `done` in `feature_list.json` (id 26, single-line edit on the `status` field; `git diff` on that file read afterwards and it shows exactly that one changed line for id 26 alongside the pre-existing hunks for ids 25/40/41/56 and entries 61-65, all untouched. `git checkout --` was not run on that file at any point in this round).

Round 1 above is closed and unamended. This section records only round 2.

All four required items are closed, and the round's centre — **D1 — is closed at a higher standard than the remedy asked for**: the new guard dies under three independent mutations, one per mechanism the ledger row claims. The two new findings below are non-blocking and both are corrections to *prose in the ledger*, not to code or to a guard; neither leaves a behaviour unproven, and one of them is the more interesting artefact this round produced.

## What I ran

I did not re-run the 18-project solution; the implementer's `quality.sh` claim (18 projects / 1525 tests / 0 failed) is not what is under test here. The claims under test were the four remedies, and I ran the specific suites that carry them plus four mutation probes of my own.

| Command | Result |
|---|---|
| `dotnet build tests/Gateway.IntegrationTests --no-incremental` (before every probe run and after every restore) | 0 warnings, 0 errors, each time |
| `dotnet test tests/Gateway.IntegrationTests --filter "FullyQualifiedName~StreamHttpTests"` (baseline, unmutated) | **8 passed**, 0 failed, 3 m 33 s — 7 from round 1 plus the new D1 guard |
| `dotnet test tests/Gateway.IntegrationTests` (whole assembly, real NATS / Kafka / Mongo / MS-SQL containers, final confirming run on restored source) | **48 passed**, 0 failed, 5 m 14 s |
| 4 mutation probes, each preceded by `dotnet build --no-incremental` | **4 killed / 0 survived on D1's guard; 1 survived silently on ledger row 11's stated evidence** (finding R2-1) |
| `./init.sh` | exit **0**, 64 features parsed, coherence holds |
| `diff -rq specs/shared <#7>/specs/shared` | one line of output: `test-matrix.md` differs. Nothing else — unchanged from round 1 |
| Independent re-run of both #7 enumeration commands, plus a wider unfiltered sweep of all 44 `apps/gateway` spec files | union is 8 files, exactly as reported; no ninth file exists (below) |

Every mutated file was restored from a `cp` backup taken before mutation, `touch`ed to defeat MSBuild's incremental check, `cmp`-verified byte-identical against that backup, rebuilt `--no-incremental`, and re-read on disk. `grep -n "REVIEW PROBE"` across both mutated files returns nothing (exit 1). Both files are also byte-identical to the backups I took in **round 1**, which is independent evidence that round 1's restores held too:

```
$ cmp round1bak/ReplayBuffer.cs src/Gateway/Domain/Sse/ReplayBuffer.cs                 # identical
$ cmp round1bak/NatsStreamSignalSubscriber.cs src/Gateway/Infrastructure/Messaging/... # identical
$ diff round1bak/StreamEndpoints.cs src/Gateway/Presentation/Endpoints/StreamEndpoints.cs
34,36c34,37   # the D4 doc-comment fix, and nothing else
```

That last diff is the independent check the brief asked for on "anything the round disturbed": **`StreamEndpoints.cs`'s only net change since round 1 is the D4 comment**. Not asserted from the report — computed against my own round-1 copy of the file.

## Mutation probes

| # | Mutation | What it tests | Outcome |
|---|---|---|---|
| A | `StreamEndpoints.cs` — `hub.Unsubscribe(subscriptionId)` deleted from the `finally` (the exact mutation that left 193/193 and 10/10 green in round 1) | the teardown call itself | **KILLED** — `Disconnect_UnsubscribesFromTheHub_ThroughTheEndpointsOwnTeardown` failed in 10 s: `Assert.Equal() Failure: Values differ / Expected: 0 / Actual: 1` at `StreamHttpTests.cs:358`. Verbatim match to the implementer's own arming record |
| B | `StreamEndpoints.cs` — the request's own token **not threaded** into the read/ping loop (parameter renamed to `requestAborted`, `var cancellationToken = CancellationToken.None;`) | whether the guard is about `HttpContext.RequestAborted` or merely about the `finally` existing | **KILLED** — same assertion, but the test now took **41 s** instead of 10 s before failing: with the token gone the loop only unwinds when the 15 s ping write hits a dead socket, past the guard's 10 s deadline. The guard is genuinely about the cancellation path, not just the `finally` |
| C | `StreamEndpoints.cs` — the endpoint subscribes on a **freshly constructed `StreamHub`** instead of the DI singleton | whether the new `GatewayTestHost.Services` seam observes the hub the endpoint actually uses | **KILLED at the mid-test assertion** — `Expected: 1 / Actual: 0` at `StreamHttpTests.cs:348`. The seam is not a parallel view; it resolves the same singleton the running endpoint resolves |
| D | `NatsStreamSignalSubscriber.cs:64` — one of the two consume loops given `CancellationToken.None` instead of the shared `stoppingToken` | **ledger row 11's** claim that a broken teardown here would be observable | **SURVIVED, and provably invisible** — see finding R2-1 |

Probes A, B and C together answer the brief's real question about D1: *does the guard execute the code the ledger row is about?* Row 10 names three mechanisms — the threaded `RequestAborted`, the `catch (OperationCanceledException)`, and the hand-written `finally`. B kills the first, A kills the third, and C proves the observation point is the endpoint's own hub rather than a test-side artefact. This is no longer a guard that could pass for an unrelated reason.

## The four items

### D1 — CLOSED, and armed harder than the remedy required

- **Ledger row 10 exists**, and its account is accurate against the code I read: `context.RequestAborted` threaded through every `await` (`StreamEndpoints.cs:48-52`), `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` (`:144-154`), `finally { hub.Unsubscribe(subscriptionId); }` (`:155-158`), and `using var pingTimer` (`:102`) as the one part the language supplies rather than the hand.
- **The guard reaches the teardown through the endpoint.** `Disconnect_UnsubscribesFromTheHub_ThroughTheEndpointsOwnTeardown` opens a real SSE connection over real Kestrel, reads `stream.ready` off the socket, asserts `SubscriberCount == 1`, disposes the response (a real client disconnect), and polls back to 0 against a 10 s deadline. It never calls `StreamHub.Unsubscribe`.
- **The new seam does not widen anything.** `GatewayTestHost.Services => _app!.Services` is a read-only accessor on the real running host. `StreamHub` is registered `AddSingleton` (`GatewayServiceCollectionExtensions.cs:49-53`), so the test resolves the identical instance the endpoint's DI parameter resolves — probe C proves that empirically rather than by reading the registration. `SubscriberCount` is `_subscribers.Count` on the same `ConcurrentDictionary` that `Subscribe`/`Unsubscribe` mutate (`StreamHub.cs:80-103`), not a separately maintained counter that could drift.
- The mid-test `Assert.Equal(1, hub.SubscriberCount)` is a **direct** read justified by ordering (`hub.Subscribe()` precedes the first `WriteFrameAsync`), and probe C confirms it is load-bearing rather than decorative.

### D2 — CLOSED, and the enumeration verified independently rather than accepted

I re-ran both commands against the #7 checkout myself. The name-based command returns 7 files, the content-based one returns 6, and their union is the 8 the report tables — the two commands disagree on exactly `contract-drift.integration.spec.ts` and the two pure-domain files, as stated.

I then asked the question the corrected command still cannot answer for itself — *could a ninth file exist that neither command sees?* — by sweeping the **whole** candidate set with no property filter:

```
$ find apps/gateway/src -iname "*.spec.ts" | wc -l
44
$ grep -ric --include=*.spec.ts -E "sse|stream|last-event-id|lasteventid" apps/gateway/src | awk -F: '$2>0'
[26 files]
```

26 files carry a textual hit; 18 of them are outside the union of 8, and I classified every one: **all 18 are substring noise** — `sse` inside *assertion*, `stream` inside *upstream*/*downstream*, `RpcBusinessError`. I opened the six highest-count offenders (`black-box-api` 13, `saga-e2e-verification` 17, `money-representation` 6, `problem-json.filter` 5, `nats-rpc-client.adapter` 4, `contract-drift` 7) and read the matching lines. No SSE case lives outside the eight files. **The candidate set is complete.**

The per-case classification is real. I enumerated every `it(`/`test(` in the eight files:

```
$ for f in <the 8>; do grep -cE "^\s*(it|test)(\.each\(|\.skip|\.only)?\s*[\(<]" "$f"; done
3 4 3 7 3 4 7 1   → TOTAL 32
```

**32, matching the report exactly**, and every line number in its table (5/17/28, 55/73/81/87, 5/10/19, 5/14/21/28/38/45/54, 24/47/73, 67/82/95/121, 44/68/86/119/140/245/271, 136) resolves to the `it(` whose title the table quotes — I printed all 32 and compared. Spot-checking the classifications: `contract-drift.integration.spec.ts:87` (#7's F9) and `nats-stream-signal.adapter.spec.ts:73` (`stop() unsubscribes both subscriptions`) each have their own line, as required. And every #8 counterpart named in the table exists — I grepped all 23 named C# test methods across `tests/`, zero missing.

### D3 — CLOSED. Every citation resolves; two are imprecise inside a correct range

I resolved all eleven rows against the two checkouts. Rows 1, 2, 3, 4, 6, 7, 8, 10 and 11 land **exactly** on the text they quote — `stream-hub.ts:37`/`:47`, `cursor.ts:18`, `replay-buffer.ts:32`/`:44`, `nats-stream-signal.adapter.ts:31`/`:43`, `impl_gateway_rest_auth.md:106`, `stream.controller.ts:1-8`, #7's `progress/history.md:1041` (the F1 sentence is on that line, verbatim), `stream.controller.ts:80-83` (`res.on('close', …)`), and `nats-stream-signal.adapter.spec.ts:73`. Row 9 correctly says "n/a — an engine claim probed by #8 itself".

Two imprecisions, neither material and neither blocking:

- **Row 5** cites the range `:83` through `:90` and attaches the snippet `console.error(JSON.stringify({...` to `:90`. The range is right and covers `logDecodeFailure` entirely; the quoted snippet is at `:85-86`, and `:90` is `...(correlationId ? { correlationId } : {}),`.
- **Row 10's #8-side citations are one line stale** — `:47-51`, `:143-153`, `:154-157`, `:101` are now `:48-52`, `:144-154`, `:155-158`, `:102`, and the doc comment on the new test (`StreamHttpTests.cs:323`) says `StreamEndpoints.cs:143-157` for the same reason. Cause is benign and provable: the D4 comment fix added one line above them, in the same round. Worth a one-character-per-number correction the next time that file is opened, not a round.

### D4 — CLOSED

`StreamEndpoints.cs:35-37` now names `StreamHttpTests.GetOrdersStream_IsNeverCapturedByTheOrderByIdRoute`, which exists and which round 1 already confirmed is the real guard. My own sweep:

```
$ grep -rn "OrdersStreamRouteTests" --include=*.cs .
$ echo $?
1
```

## New findings this round (both non-blocking)

### R2-1 — Ledger row 11's stated evidence cannot fire, and I proved it. Fix the sentence, keep the conclusion.

This is the round's most useful artefact, and it is exactly the shape the brief predicted.

Row 11's **core claim is true and I verified it in code**: `NatsStreamSignalSubscriber.ExecuteAsync` passes one shared `stoppingToken` into both `ConsumeAsync` calls (`:63-64`), each of which hands it to `connection.SubscribeAsync<byte[]>(…, cancellationToken: stoppingToken)` (`:72`). Both mechanisms named — `BackgroundService`'s stopping token and NATS.Client.Core's cancellation-aware `IAsyncEnumerable` — are genuinely framework/library supplied, unlike D1's hand-written `finally`. So the row's **conclusion that no dedicated guard is mandated is correct**, and correct for the right reason: the ledger rule binds where a property #7 got free must be **hand-built** here, and here the inheritance runs the other way — #7 hand-wrote `stop()` and guarded it; #8 gets the same property from its host and its client library.

What is false is the row's supporting sentence: *"a hung subscription loop would hang every test's teardown rather than only this feature's own tests — none has."*

Probe D broke exactly that plumbing — one of the two loops given `CancellationToken.None`, so it can never end on host stop — and measured the consequence against the unmutated baseline, on the same machine, back to back:

| Run | Mutated (probe D) | Restored |
|---|---|---|
| `Connect_SendsStreamReady…` (1 host start/stop) | Passed, 30 s (`real 0m34.7s`) | Passed, 30 s (`real 0m34.9s`) |
| `StreamHttpTests.Reconnect_*` (3 host start/stops) | Passed, 1 m 31 s (`real 1m35.5s`) | Passed, 1 m 31 s (`real 1m35.4s`) |

**Zero difference, to the second, across three host start/stop cycles.** Nothing hangs, nothing goes red, nothing even gets slower. The row's own evidence is evidence that cannot fire — the guard-that-does-not-guard, appearing this time inside a ledger row's justification rather than inside a test.

Why this is a finding and not a rejection: the behaviour is correct, the mechanism claim is verified, no rule of `CLAUDE.md` mandates a guard here, and the failure mode being argued about (one NATS subscription lingering in a process that is exiting) has no observable consequence in this system — which is *itself* the honest reason no guard can be written. The correction is one sentence, and it is strictly stronger than what is there now:

> *"…and this property is not independently observable in #8: a consume loop that ignores the stopping token still costs nothing measurable — verified by probe, one loop given `CancellationToken.None`, no test slowed by so much as a second across three host start/stop cycles. That unobservability, not an absence of effort, is why no guard is written."*

This is the second time in this feature that a claim about a teardown path turned out to rest on something nobody had made fail. That pattern is the answer to the leader's question below.

### R2-2 — Carried from round 1, still open, still not blocking

`GatewayTestHost.StartAsync`: a **non**-`TaskCanceledException` from `app.StartAsync()` still leaves the partially built `WebApplication` undisposed (`GatewayTestHost.cs:70-79` — the `catch` disposes, the propagating path does not). One line. Recorded again so it is not lost, not held against the feature.

## Acceptance bullets and R55 — unchanged from round 1, re-confirmed on a green run

Both bullets were verified in round 1 by probes P1-P4 and nothing in this round touched the code they cover — proved, not assumed, by the `diff` against my round-1 copy of `StreamEndpoints.cs` showing only the D4 comment. The R55 row in `specs/shared/test-matrix.md` now also cites the D1 guard by name, with the ledger row it belongs to; I read the row. The web half is still honestly marked TODO against features 29/30, both `pending`.

| Half of R55 | Named tests | Verified this round |
|---|---|---|
| SSE frame mechanics | `CursorGeneratorTests` (4), `ReplayBufferTests` (7), `StreamHubTests` (6) | round 1 (193/193, P3 killed both directions); unchanged since |
| `GET /orders/stream` over the wire | `StreamHttpTests` (**8**, was 7) | **8/8 green** in my baseline run and inside the 48/48 assembly run |
| Endpoint teardown on client disconnect | `StreamHttpTests.Disconnect_UnsubscribesFromTheHub_ThroughTheEndpointsOwnTeardown` (**new**) | probes A, B, C — all killed |
| Heartbeat | `StreamHeartbeatHttpTests` (3) | round 1 (P2, strong form discriminates); green in the 48/48 |
| `Kafka fact → real projector → NATS → SSE client` | `StreamProjectorEndToEndTests` (1) | green in the 48/48 |
| Web half | none — features 29/30 `pending` | correctly TODO |

## CHECKPOINTS walk — round 2

**C1 — the harness is complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds the five agents
- [x] Every agent definition declares its model
- [x] `./init.sh` exits 0 — run by me this round, exit 0

**C2 — state is coherent**
- [x] At most one feature `in_progress` — **zero** after this review; id 26 goes to `done`
- [x] Every status is in `rules.valid_status` — 64 features: 43 `done`, 21 `pending`, 0 otherwise, after this transition
- [x] Every `done` feature has passing tests associated with it — id 26's are the 8 + 3 + 1 integration cases and 32 unit cases enumerated above
- [x] `progress/current.md` describes the active session
- [x] No `blocked` feature

**C3 — architecture is respected**
- [x] No framework reference inside any `Domain/` folder — `tests/Architecture.Tests` **run** in round 1, 16/16, `DomainAssemblies` includes `OrderToCash.Gateway`; no `Domain/` file changed this round (`cmp` against my round-1 copies)
- [x] No cross-service database access — unchanged; the `src/Projector` reference remains test-only in `tests/Gateway.IntegrationTests.csproj`
- [x] No shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs`
- [x] No `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `src/SharedKernel` still has zero `PackageReference`
- [x] No `decimal` in domain arithmetic — this feature carries no money
- [x] Every interaction classifiable — unchanged from round 1's ruling
- [x] No stray debug logging, no context-free TODOs — and no probe markers left behind (`grep -n "REVIEW PROBE"` returns nothing in both files I mutated)

**C4 — verification is real**
- [x] **`./quality.sh` passes** — not re-run by me; the implementer reports exit 0 with 18 projects / 1525 tests / 0 failed, and the only source delta since round 1's green solution run is one doc comment, one test method and one test-host property. My own runs this round: 48/48 integration (whole assembly, real containers) plus 8/8 and 3/3 targeted. Round 1's 193 unit + 16 architecture stand
- [x] Domain tests are pure
- [x] Integration tests use Testcontainers against real MsSql / Kafka / NATS / MongoDB — watched the containers come up
- [x] Coverage thresholds — coverlet gate green in the implementer's run; not re-run
- [x] No Jest anywhere
- [x] **Every branch that must be guarded is guarded** — the box round 1 left open. The teardown branch now has a guard that dies three ways

**C5 — the session closed cleanly**
- [x] No suspicious untracked files
- [x] `progress/history.md` entry with effort record — appended at this approval
- [x] `feature_list.json` reflects the true state (id 26 → `done`)
- [ ] The human has been told what was done and how to test it — owed by the leader at hand-off, not by this review
- [x] Claude did not commit

**C6 — SDD** — not applicable, `sdd: false`. The one applicable box:
- [x] R55 is covered by concrete named tests recorded in `specs/shared/test-matrix.md`

**C7 — spec-reuse fidelity**
- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — `diff -rq` re-run this round, one line of output
- [x] No silent fork
- [x] The `R<n>` ids are #7's, and R55's SSE half genuinely holds here
- [ ] n8n workflows fired against the .NET Gateway — still unexercised, carried, not this feature's bullet
- [ ] Black-box API script — same
- [x] `progress/history.md` effort record — appended at this approval
- [ ] README benchmark section — owed at phase close

## Effort record — the framing, and what it should not be read as

Written in full to `progress/history.md`. The comparison in one line: **the pass counts are identical and the wall-clock is not.**

- **#7**: 2 implementation passes + 2 completed review passes, rejected once (F1 — a `tsx`-spawned child projector behind a header comment falsely claiming equivalence to the real dev command), **traceable ≈1 h 22 min**, plus an interrupted review attempt that produced no artefact. Read out of #7's `progress/history.md:1041`, not from memory.
- **#8**: 2 implementation passes + 2 completed review passes, rejected once, **traceable ≈3 h 04 min** — round-1 build ≈1 h 31 min, round-1 review ≈38 min, round-2 fix ≈23 min, this review ≈32 min, off artefact mtimes.

**Same shape, 2.2× the clock**, and the honest confound is that #8 built more: a real in-process projector end-to-end test instead of #7's rejected spawned one, a strong-form heartbeat test #7 never had, a cursor concurrency guard #7's runtime made unnecessary, a teardown guard #7 had at unit level and #8 now has through a real disconnect, and a neighbouring feature's flaky Kestrel-bind harness fixed on the way. Most of #8's extra clock is container time: a single 48-test assembly run is 5 m 14 s here, and this review alone spent four of them.

**The rejection causes differ, and that is the part worth carrying.** #7 was rejected for **a false equivalence claim in a harness comment**. #8 was rejected for **a ported idiom with no ledger row and no guard** — the same family as this phase's two earlier rejections, and the third time in phase 13 that the defect was invisible to traceability (the requirement was met) and invisible to arming (the behaviour was correct on every path a test took).

## The guard-hardening loop — my answer to the leader's question

**Fold the *question* in; leave the *defect* closed here.** D1 itself is finished — it has a row and a guard I killed three ways, and re-opening it inside ids 60-65 would buy nothing.

But it belongs in that loop's **instrument**, as a distinct variant, and this round produced a second exhibit that makes the case rather than merely restating it. Ids 60-65 all share the shape *a guard whose assertion cannot detect the defect it names*. D1 is one step upstream of that: **there was no assertion at all, and what hid it was a missing ledger row**, not a weak one. R2-1 is one step to the side: **the row exists, names no guard — correctly — and then offers evidence that cannot fire.** Same cause, three positions on the same axis.

So the instrument that loop builds should not stop at *"for each test, if the behaviour it names were deleted, does it go red?"* It should also ask, of every row in every ported-idiom ledger: **"if this row's stated evidence were false, what exactly would turn red — and has anyone seen it?"** That question costs one line per row at spec time, and it would have caught R2-1 in seconds; nothing else in this harness would have caught it at all, because the row satisfies the requirement, satisfies traceability, and describes correct code. Seven exhibits now, and two of them are in this feature.

## Phase 13 is **not** closed by this approval

Ids **56, 60, 61, 63, 64 and 65** remain `pending` in phase 13 — six, enumerated rather than counted from the brief:

```
$ python3 -c "... for f in features: if f['status']=='pending' and f['phase']==13: print(f['id'], f['name'])"
56 composition_root_env_reads_are_unguarded
60 envelope_fixture_collisions_defeat_provenance_assertions
61 catalog_ordering_claim_guarded_for_products_only
63 readiness_retry_loops_are_paced_by_a_timeout_that_never_elapses
64 orders_wire_key_theory_compares_a_hand_typed_list_to_itself
65 gateway_order_detail_totals_transposition_survives
```

Id 62 `operator_cancel_races_saga_forward_progress` is **phase 14**, not 13 — I checked, having first assumed from the contiguous id range that it sat in this band. **No phase-13 closing assessment is due, and none is written here.**

## Post-transition state — one expected `init.sh` failure, and it is the leader's to clear

After setting id 26 `done`, `./init.sh` exits **1** on exactly one check, and only that one:

```
── 4. Session file in lockstep
[FAIL]  progress/current.md claims a feature while none is active: "**Feature:** `gateway_sse_push` (id 26, phase 13)"
```

Everything else is green — 64 features parsed, every status valid, **no feature `in_progress`**, ids unique, SDD coherence over 7 sdd features, backlog tripwire clean (no feature lost, no `done` reverted), commit-msg hook matching its tracked copy.

I did **not** clear it: `progress/current.md:75` says *"This file is the leader's and no subagent may write to it."* And `:77` says this file has gone stale at four consecutive features, *"always at the moment a reviewer performed a transition the leader was not present for"* — which is precisely this moment. It is the leader's rewrite, and the approval is its trigger.

`progress/history.md` has this feature's entry with its effort record. Nothing was committed.
