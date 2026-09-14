# Batch D3 — backlog ids 81 and 85

Implementer record. Two backlog entries, one dispatch, no shared files. Neither touches `src/`.

## Status

| Entry | Verdict |
|---|---|
| **81** — Gateway OR4 concurrent-trace test times out on a fixed 2 000 ms RPC budget | **PASS** — cause identified with evidence BEFORE any change, and it is **not** the budget. Fixed at the cause; budget left at 2 000 ms with its margin measured and stated. |
| **85** — eight fixtures self-assign host ports and race | **PASS** — 12 self-assigned bindings in 8 files converted, 8 copies of `GetFreeTcpPort` deleted, Kafka handled by the documented startup-callback route rather than naively, change of KIND proved against a real Docker daemon, 5 new guards armed. |

## Discrepancies between the brief and the entries

The brief instructed me to read the `acceptance` arrays verbatim and to report where the two differ. Three differences, all minor, none affecting the work:

1. **Brief §Status says "Both are now `pending`".** In `feature_list.json` as I read it, **id 81's status is `in_progress`** and id 85's is `pending`. I have not touched `feature_list.json`; the coordinator owns its transitions.
2. **Id 85 bullet 4 specifies Docker's error as `port is already allocated`.** Docker **29.8.0** on this machine says `failed to bind host port 0.0.0.0:NNNNN/tcp: address already in use` instead. Both wordings name the port, so the change-of-kind test asserts on **the port number**, not on either wording; both are recorded in the test's own doc comment.
3. **Id 81 bullet 3 prescribes the proof as "delay the responder … and show the current budget loses every time and the chosen fix wins every time".** That recipe presumes the budget is the binding constraint. The diagnosis (§Id 81 / Cause) disproved that premise, so the change-of-kind proof is made on the property the cause *is* about — whether the connection is established before the concurrent pair is issued — which is 100 %/0 %, deterministic, and lives in the test. Bullet 3's *spirit* (change of kind, determinism in the test not the mutation) is met; its literal recipe is not, because following it would have proved something that is not true.

---

# Id 81 — the OR4 concurrent-trace timeout

## Enumeration (bullet 1) — every fixed RPC or wait budget in `tests/Gateway.IntegrationTests`, plus `NatsRpcClient`'s own default

Taken **before** any change, so the line numbers and the `= 2000` on line 29 are the pre-change state.

```
find tests/Gateway.IntegrationTests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
 | xargs -0 grep -nE '(Timeout[[:space:]]*=|timeoutMs|Task\.Delay\(|CancellationTokenSource\(|TimeSpan\.From|Flush\(|PollTimeoutMs|ReadTimeout|Attempts[[:space:]]*=|SignalAndWait\(|connectTimeoutMS)' \
 | grep -vE '^[^:]+:[0-9]+:[[:space:]]*(///|//|\*)' | sort
```

103 hits, **0 unclassified**. The second `grep` drops **comment lines only**, anchored as `path:lineno:` followed by whitespace then `///`, `//` or `*` — deliberately anchored rather than the naive `grep -v '//'`, which would have silently eaten all 16 `mongodb://…` lines (`b:` immediately followed by `//` matches an unanchored `:\s*//`). That is the content-filter trap `CLAUDE.md` names, and avoiding it is why the anchor is there.

The complete classified output is in `progress/impl_batch_d3_id81_budget_enumeration.txt` (one line per hit: `path:line — CLASS — source`). Class totals and what each class means:

| Class | Hits | Classification |
|---|---|---|
| `POLL-DEADLINE` | 36 | Upper bound on a *polling* wait (`CollectUntilAsync`, `PollUntilStatusAsync`, an explicit `deadline`, a `CancellationTokenSource(ts)`). Generous by construction — 3 s to 90 s — and each one fails loudly with its own message. Not an RPC budget; not implicated. |
| `MONGO-FAIL-FAST` | 16 | `mongodb://127.0.0.1:1/?connectTimeoutMS=1` — a deliberately unreachable Mongo so a test that must not depend on the read model fails in 1 ms instead of waiting. The opposite of a too-tight budget: tightness is the point. Not implicated. |
| `POLL-INTERVAL` | 16 | `Task.Delay` *between* poll attempts (50–500 ms). Pacing, not a budget. Not implicated. |
| `READINESS-PACE` | 7 | `ReadinessAttempts = 100` / `ReadinessPacingInterval = 50 ms` and their uses — backlog ids 63 and 69's class, already fixed and already guarded by `GatewayReadinessPacingRaceTests`. Distinct mechanism; not implicated. |
| `RPC-BUDGET` | 5 | **The class this entry is about**: `BuildClient`'s default (2 000 ms, used by all four success-path cases including OR4) and the three deliberately short budgets — 500 ms for the no-responder case and 300 ms twice for the silent-responder cases, whose whole purpose is to make a timeout happen quickly. The three short ones must stay short. |
| `READINESS-PROBE-RPC` | 4 | `NatsSubOpts { Timeout = 200 ms }` inside the three readiness probes — one real round trip per attempt, paced. Not implicated. |
| `XUNIT-CASE-TIMEOUT` | 3 | `[Fact(Timeout = 120_000)]` / `[Theory(Timeout = 180_000)]` — per-case ceilings on the three heaviest end-to-end cases. Not implicated. |
| `KAFKA-POLL` | 3 | `Kafka.PollTimeoutMs = 200` — consumer poll interval, not a deadline. Not implicated. |
| `ASSERTED-BOUND` | 3 | Budgets that are the **subject** of an assertion (`_pausedReadinessBound`, `_liveWhileReadyInFlightBound`, and the 500 ms bound in the pacing-race test). Changing them changes what is proved; not implicated. |
| `KAFKA-FLUSH` | 2 | `producer.Flush(5 s / 10 s)`. Not implicated. |
| `SSE-SOCKET-READ` | 2 | `NetworkStream.ReadTimeout = PingIntervalMs * 4` — derived from the heartbeat interval, and the thing the heartbeat test exists to beat. Not implicated. |
| `HOST-BIND-RETRY` | 2 | `MaxBindAttempts = 5` with a `200 ms * attempt` backoff in `GatewayTestHost`. Paced. Not implicated. |
| `CONTROLLED-DELAY` | 2 | `_subscriberDelay = 300 ms` — a delay the pacing-race test *injects* to make its own proof deterministic. Not a budget. |
| `PACING-RACE-PROBE-RPC` | 1 | `NatsSubOpts { Timeout = 2 s }` in the pacing-race test's own probe. Not implicated. |
| `BARRIER` | 1 | `RequestOverlapBarrierConnection`'s `SignalAndWait(10 s)`. **Measured, not assumed, to sit OUTSIDE the RPC budget** — see diagnostic D1 below. |

**`NatsRpcClient`'s own default timeout** (`src/Gateway/Infrastructure/Messaging/NatsOptions.cs:8`): `DefaultTimeoutMs = 5_000`. Production is therefore 2.5x more generous than the test that went red, and is unchanged by this work — no `src/` file was touched.

## Cause, identified BEFORE any change (bullet 2)

The leader's sighting is real and I re-read it rather than trusting the summary: `/tmp/claude-1000/quality_feature73_1.log:132-142`, `RpcTimeoutError : RPC call to "gateway.it.trace-concurrent" timed out after 2000ms`, case duration `[2 s]`, inside a full run in which **six containerised assemblies overlapped** (`Orders 10 m 50 s`, `Gateway 6 m 9 s`, `Billing 4 m 41 s`, `Fulfillment 3 m 34 s`, `Notifications 2 m 52 s`, `Projector 1 m`).

I then measured, with a throwaway diagnostic (`tests/Gateway.IntegrationTests/ZzId81Diagnostic.cs`, added, run and **deleted** before any production-side change; it is not in the final tree).

**D1 — is the barrier wait charged against the budget?** Started call A, delayed call B by 4 000 ms so A sat at the barrier for 4 s under a 2 000 ms budget.

```
D1 barrier-held-4000ms, budget 2000ms, elapsed=4027ms, outcome=BOTH SUCCEEDED (pre-request wait NOT charged)
```

So the budget covers only what happens after `RequestOverlapBarrierConnection` delegates to the real connection. The barrier is not the cause.

**D2/D8a — how big is the charged round trip?**

```
D8a[idle]      n=200 min=0.3 p50=0.5 p95=0.9 p99=1.4 max=1.5 (ms)
D8a[loaded32]  n=200 min=0.6 p50=2.0 p95=4.0 p99=5.3 max=5.5 (ms)      # 32 busy loops on 16 cores
```

**D3 — the OR4 shape under a budget that cannot plausibly fire.** Ten rounds, budget **120 000 ms**. Rounds 0-6 charged 2.1-3.7 ms each. Round 7 hung and the case failed after **2 m 2 s** with `timed out after 120000ms` — on an **idle** machine. That one observation kills the contention hypothesis outright: a 120-second stall is not a slow round trip.

**D5/D6/D7/D8b — what separates a lost reply from a normal one.** Each round builds a fresh responder and a fresh `NatsConnection`, releases two calls through the barrier, catches rather than throws, and logs.

| Diagnostic | Machine | Variant | Rounds failing |
|---|---|---|---|
| D3 | idle | cold, budget 120 000 ms | **1 / 8** |
| D5 | idle | cold, budget 2 000 ms | **2 / 40** |
| D5 | idle | cold, budget 20 000 ms | 0 / 40 |
| D4 | idle | cold, budget 5 000 ms (2 subject variants) | 0 / 60 |
| D6 | idle | cold, budget 2 000 ms | **2 / 200** |
| D8b | idle | cold, budget 2 000 ms | **3 / 150** |
| D8b | 32 busy loops | cold, budget 2 000 ms | 0 / 150 |
| D6 | idle | **warm** — one ordinary prior request | 0 / 200 |
| D7 | idle | **`ConnectAsync()` only**, no prior request | 0 / 200 |
| D8b | idle | **pre-connected** | 0 / 150 |
| D8b | 32 busy loops | **pre-connected** | 0 / 150 |

**Totals: 8 failures in 648 cold rounds (1.23 %); 0 failures in 700 rounds where the connection was established first** (500 by an explicit `ConnectAsync()` — D7's 200 plus D8b's two pre-connected rows of 150 — and 200 by D6's one ordinary prior request). *Corrected by the leader 2026-09-13: this line originally read 608 and 550, dropping `D5[idle][budget=20000]` (40 rounds) from the cold arm and `D8b[loaded32][preconnected]` (150 rounds) from the established arm, on no consistent ground. Both arms now sum from the table above: 8+40+40+60+200+150+150 = 648 cold, and 200+200+150+150 = 700 established. The 8 and the 0 were always exact; only the denominators were wrong, and both errors were conservative — the true cold rate is lower than claimed and the established arm is stronger. See the Count correction section at the end.*

Every single failure had the same signature, printed by the diagnostic:

```
D5[idle][budget=2000] round=10 FAILED a=RpcTimeoutError(2004ms) b=ok(6ms) responderObserved=2/2 poolBusy=2 poolPending=0 poolThreads=7
D8b[idle][cold] round=16 FAILED a=RpcTimeoutError(2003ms) b=ok(5ms) responderObserved=2
```

**`responderObserved=2/2`** — the stand-in responder received *both* requests and replied to both. **`b=ok(5ms)`** — the losing call's simultaneous twin completed in 2-8 ms.

**The cause, stated:** when the two concurrent calls are the **first traffic over a lazily-established `NatsConnection`**, one of the two replies is not delivered to its caller, about 1 % of the time, and that call then waits out whatever budget it has. It is a connect-time race in the NATS client, not slowness.

**Each of the entry's named candidates, ruled out by the measurements above, not by argument:**

- *"the responder's own start-up inside the 2 000 ms window"* — **no.** `StandInResponder.StartAsync` completes its paced readiness probe before the calls are built, and in all 8 failures the responder had received both requests.
- *"contention with a full suite's containers"* — **no.** Reproduced 5 separate times on an **idle** machine; the charged round trip is p99 1.4 ms idle and 5.3 ms at 2x CPU oversubscription. Decisively: in every failure the losing call's **simultaneous twin took 2-8 ms**. No amount of machine load makes one call take 2 000 ms while its partner, released by the same barrier onto the same connection, takes 4 ms.
- *"the single-reader dispatch path filed as id 80"* — **no.** That is Orders' saga dispatch; this reproduces in `Gateway.IntegrationTests` with only NATS running and no Kafka, no MS-SQL and no Orders host in the process.
- *"or none of these"* — **this.**

## What changed, and why the budget was NOT raised (bullet 5)

One file: `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs`.

1. `await realConnection.ConnectAsync();` before the barrier decorator is built in `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`. The overlap the case needs is supplied by `RequestOverlapBarrierConnection`, so issuing the pair over an unestablished connection added nothing to what the case proves and added a 1 %-per-run lost reply.
2. An `Assert.True` immediately after it, asserting `ConnectionState == Open` with a message that names the budget and prints the offending state. This is the guard (bullet 4).
3. `2000` extracted to `private const int SuccessPathBudgetMs = 2000`, so the guard's message names the budget rather than transcribing it, with the margin measurement in its doc comment.

**The budget is unchanged at 2 000 ms, and bullet 5's escape hatch is not used, because the measurement says the budget is not tight.** The number and its margin, as bullet 5 requires them stated:

- **2 000 ms**, against a **worst successful charged call of 127 ms** observed across 1 348 concurrent pairs (idle and loaded), and a **p99 of 5.3 ms under 32 busy loops on 16 cores**.
- **How the margin was measured**: `D8a` sampled 200 sequential charged round trips through the real broker with a 120 000 ms budget (so the budget could not truncate the sample); `D8b` recorded the worst *successful* charged call across 600 barrier-released pairs. Both were run twice, once idle and once with 32 CPU-busy processes started before the run and killed after it (`uptime` moved from 1.77 to 16.41). Margin: **~15x the worst observation, ~380x p99.**
- Raising it would have been treating a symptom that is not present: D3 lost a reply under a **120 000 ms** budget and waited out all two minutes of it, so any larger number merely makes the red run slower.

## Change of KIND (bullet 3)

The failure mode itself is probabilistic (~1 %), so a "the flake stopped" proof is impossible in principle and forbidden in any case. The proof is made on the **property the cause is about**, where the change is 100 %/0 % and lives entirely in the test:

- **Without the fix**: a freshly constructed `NatsConnection` has `ConnectionState == Closed` at the moment the pair is issued — **every time, by construction**. Measured: the armed run below failed in **188 ms**, before any call was made.
- **With the fix**: `Open` — every time. Confirming run green.

The supporting change-of-kind evidence for *why that property matters* is the 8/648 vs 0/700 table above, which is a difference in the cause's presence, not in luck: every one of the three "established first" variants (prior request, `ConnectAsync()` only, pre-connected under load) returned exactly zero.

## Arming — id 81

**Arm 1.** Delete `await realConnection.ConnectAsync();` from `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId`.

- Backup: `cp` to scratch before mutating. Rebuild: `dotnet build … --no-incremental` (9.12 s).
- Named test run: `OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId` — **FAILED in 188 ms**, verbatim:

```
backlog id 81: this case must issue its two concurrent calls over an ALREADY ESTABLISHED connection, never as the
connection's first traffic — the cold shape loses one of the two replies in ~1 % of runs (8 losses in 648 cold rounds
against 0 in 700 established ones) and the loser then waits out the whole 2000 ms budget, which is the RpcTimeoutError
this test reported on 2026-09-12. The connection state at the moment the pair was about to be issued was 'Closed', not 'Open'.
```

- Restored from the backup, `cmp` identical, `touch`ed, rebuilt `--no-incremental`, re-read line 250 to confirm the line is back. Confirming run: **1/1 passed, 363 ms**.

The message names the claim, the budget (`2000 ms`) and the offending value (`'Closed'`) — not backlog id 82's `Expected: False / Actual: True` shape.

## Finding NOT fixed — the identical shape in `tests/Orders.IntegrationTests` (routing, not a footnote)

`CLAUDE.md` requires a defect class to be enumerated repository-wide before any instance is fixed:

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -ln 'RequestOverlapBarrierConnection'
tests/Orders.IntegrationTests/RequestOverlapBarrierConnection.cs      — the decorator itself (Orders copy)
tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs    — OR4_TwoConcurrentCalls_EachCarriesItsOwnActiveTraceId, SAME cold shape
tests/Gateway.IntegrationTests/RequestOverlapBarrierConnection.cs     — the decorator itself (Gateway copy)
tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs       — the case id 81 names; FIXED here
```

`tests/Orders.IntegrationTests/NatsStockAvailabilityCheckerTests.cs:111-113` constructs `new NatsConnection(...)`, wraps it in the barrier and releases two concurrent calls as that connection's first traffic — the identical shape, under `new NatsOptions()`'s 5 000 ms default. It carries the same ~1 %-per-run lost reply.

**I did not change it**, because id 81's bullet 1 scopes this entry to `tests/Gateway.IntegrationTests` and the brief says not to touch an unrelated service. The fix is one line and is exactly the Gateway one:

```csharp
await using var realConnection = new NatsConnection(new NatsOpts { Url = nats.Url });
await realConnection.ConnectAsync();            // <- this line, plus the same precondition assertion
var connection = new RequestOverlapBarrierConnection(realConnection);
```

**Recommendation: this needs its own numbered backlog entry.** `CLAUDE.md` is explicit that a finding recorded only as a sentence in a record names nobody and is therefore nobody's; the class was found by id 81's own diagnosis and ends outside id 81's scope.

---

# Id 85 — fixtures that self-assign host ports

## Enumeration (bullet 1), as search results

**(a) Every `WithPortBinding` call, bin/obj excluded BY PATH:**

```
find tests -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n 'WithPortBinding' | sort
```

17 hits in 13 files (complete output; one classification line each):

| # | Hit | Classification |
|---|---|---|
| 1 | `tests/Billing.IntegrationTests/KafkaContainerFixture.cs:33: .WithPortBinding(_hostExternalPort, ExternalContainerPort)` | SELF-ASSIGNED → converted (Kafka route) |
| 2 | `tests/Billing.IntegrationTests/NatsContainerFixture.cs:23: .WithPortBinding(ClientPort, true)` | Already the target idiom — in-tree control, untouched |
| 3 | `tests/Fulfillment.IntegrationTests/KafkaContainerFixture.cs:33: .WithPortBinding(_hostExternalPort, ExternalContainerPort)` | SELF-ASSIGNED → converted (Kafka route) |
| 4 | `tests/Fulfillment.IntegrationTests/NatsContainerFixture.cs:23: .WithPortBinding(ClientPort, true)` | Already the target idiom — untouched |
| 5 | `tests/Gateway.IntegrationTests/KafkaContainerFixture.cs:28: .WithPortBinding(_hostExternalPort, ExternalContainerPort)` | SELF-ASSIGNED → converted (Kafka route) |
| 6 | `tests/Gateway.IntegrationTests/NatsContainerFixture.cs:20: .WithPortBinding(ClientPort, true)` | Already the target idiom — untouched |
| 7 | `tests/Notifications.IntegrationTests/KafkaContainerFixture.cs:34: .WithPortBinding(_hostExternalPort, ExternalContainerPort)` | SELF-ASSIGNED → converted (Kafka route) |
| 8 | `tests/Notifications.IntegrationTests/MailpitAuthContainerFixture.cs:59: .WithPortBinding(_authRequiredSmtpPort, SmtpContainerPort)` | SELF-ASSIGNED → converted (plain route) |
| 9 | `tests/Notifications.IntegrationTests/MailpitAuthContainerFixture.cs:60: .WithPortBinding(_authRequiredHttpPort, HttpContainerPort)` | SELF-ASSIGNED → converted (plain route) |
| 10 | `tests/Notifications.IntegrationTests/MailpitAuthContainerFixture.cs:67: .WithPortBinding(_authAdvertisedOnlySmtpPort, SmtpContainerPort)` | SELF-ASSIGNED → converted (plain route) |
| 11 | `tests/Notifications.IntegrationTests/MailpitAuthContainerFixture.cs:68: .WithPortBinding(_authAdvertisedOnlyHttpPort, HttpContainerPort)` | SELF-ASSIGNED → converted (plain route) |
| 12 | `tests/Notifications.IntegrationTests/MailpitContainerFixture.cs:45: .WithPortBinding(_smtpHostPort, SmtpContainerPort)` | SELF-ASSIGNED → converted (plain route) |
| 13 | `tests/Notifications.IntegrationTests/MailpitContainerFixture.cs:46: .WithPortBinding(_httpHostPort, HttpContainerPort)` | SELF-ASSIGNED → converted (plain route) |
| 14 | `tests/Orders.IntegrationTests/KafkaContainerFixture.cs:46: .WithPortBinding(_hostExternalPort, ExternalContainerPort)` | SELF-ASSIGNED → converted (Kafka route) |
| 15 | `tests/Orders.IntegrationTests/NatsContainerFixture.cs:29: .WithPortBinding(ClientPort, true)` | Already the target idiom — untouched |
| 16 | `tests/Projector.IntegrationTests/TestSupport/KafkaContainerFixture.cs:32: .WithPortBinding(_hostExternalPort, ExternalContainerPort)` | SELF-ASSIGNED → converted (Kafka route) |
| 17 | `tests/Projector.IntegrationTests/TestSupport/NatsContainerFixture.cs:23: .WithPortBinding(ClientPort, true)` | Already the target idiom — untouched |

**12 self-assigned bindings in 8 files, 5 already-correct bindings in 5 files — exactly the population the entry states.** (`MsSqlContainerFixture` and `MongoContainerFixture` never appear: they use the `Testcontainers.MsSql` / `Testcontainers.MongoDb` builders, which take a Docker-assigned port by default. That is an absence I checked rather than assumed — they are not in the `WithPortBinding` hit list at all.)

**(b) Every copy of `GetFreeTcpPort`, and every `TcpListener`, repository-wide:**

```
find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n 'GetFreeTcpPort\|TcpListener' | sort
```

29 hits, all under `tests/`, classified:

- **8 `private static int GetFreeTcpPort()` declarations** — Billing:67, Fulfillment:67, Gateway:62, Notifications:80, Orders:90, Projector:68 (Kafka fixtures), MailpitContainerFixture:97, MailpitAuthContainerFixture:90. **All 8 deleted.**
- **8 `new TcpListener(IPAddress.Loopback, 0)` lines**, one inside each of those declarations. Deleted with them.
- **12 field initialisers** calling `GetFreeTcpPort()` — 6 Kafka (one each), Mailpit ×2, MailpitAuth ×4. All removed; the ports are now properties read from Docker after start.
- **1 hit that is NOT in the class**: `tests/Notifications.IntegrationTests/SendFailureClassifierRealSmtpTests.cs:147`, inside `GetUnboundPort`. Bullet 1 requires this to be **classified, not assumed**: it is called at line 72 by `ARealRefusedConnection_RaisesASocketException_AndClassifiesAsTransient`, which wants a port with **nothing listening on it** so that a real `SmtpClient.ConnectAsync` produces a genuine `SocketException`. It hands its port to no container, so it has no time-of-check-to-time-of-use window with Docker at all — the opposite requirement from the class. **Kept**, and it is bullet 6's "exactly one copy survives, in one home, with its callers stating why": the caller's own doc comment states why, and the new architecture guard names it in a literal allow-list.

## What changed

**The six Kafka fixtures — NOT naively (bullet 3).** `KAFKA_ADVERTISED_LISTENERS` must carry the host-visible address, which is precisely why a port was chosen in advance; passing `true` and leaving the advertised listener stale would have produced a broker advertising a port nothing can reach — the race gone and the broker broken. Each fixture now uses Testcontainers' documented Kafka approach:

- `.WithPortBinding(ExternalContainerPort, true)` — Docker assigns **and holds** the host port.
- `.WithCommand("/bin/sh", "-c", "while [ ! -f /testcontainers_kafka_start.sh ]; do sleep 0.1; done; exec /bin/sh /testcontainers_kafka_start.sh")` — the image's own `ENTRYPOINT` (`/__cacert_entrypoint.sh`, which `exec "$@"`) is left intact; only the command is replaced, so the container idles until its configuration is known.
- `.WithStartupCallback(...)` — documented as running *"after the container start, but before the wait strategies"*, i.e. the one moment at which `GetMappedPublicPort` already knows Docker's choice and the broker has not yet read its configuration. It copies in a `0755` script exporting the real `KAFKA_ADVERTISED_LISTENERS` and `exec`ing the image's own `/etc/kafka/docker/run`.
- `BootstrapServers` became `{ get; private set; }`, assigned from `GetMappedPublicPort` after `StartAsync`.

The route was probed directly against `apache/kafka:4.3.1` before a line of C# was written — container started with `-p 9092` (dynamic), script copied in afterwards, and the broker logged `Kafka Server started` with `advertised.listeners=PLAINTEXT://localhost:29092,EXTERNAL://localhost:34232` read back out of `/opt/kafka/config/server.properties` inside the container, the port matching `docker port`. It was then verified through real traffic: `StreamProjectorEndToEndTests` (a real producer on the host → real broker → real Projector → SSE) passed in 38 s.

**The two Mailpit fixtures — the plain route.** Mailpit advertises nothing about its own host port, so `WithPortBinding(containerPort, true)` + `GetMappedPublicPort` is the whole fix. `MailpitAuthContainerFixture`'s two containers now bind the *same* container-side ports, which is not a clash precisely because Docker gives each its own host port — the property the retired shape had to arrange by hand.

**New guards.** `tests/Architecture.Tests/ContainerFixtureHostPortAssignmentTests.cs` (5 cases) and `tests/Projector.IntegrationTests/ContainerHostPortAssignmentRaceTests.cs` (2 cases).

## Change of KIND against a real Docker daemon (bullet 4)

`ContainerHostPortAssignmentRaceTests` — the same experiment twice with one variable changed, three repetitions each, and the determinism in the TEST: the squatting listener is **held open across the whole container start** rather than closed first, so the retired shape cannot win by being fast.

- `Id85_TheRetiredSelfAssignedShape_FailsEveryTime_WhenThePortIsTakenInsideItsOwnCheckToBindWindow` — chooses a port the retired way (listen on 0, read, **close**), takes it with another socket, starts a container bound to it: **fails all 3 repetitions**, and the assertion additionally requires Docker's error to name that exact port, so a failure for any other reason is reported as a different failure.
- `Id85_TheAdoptedDockerAssignedShape_StartsEveryTime_EvenWhileEveryPortTheRetiredShapeWouldHaveChosenIsHeld` — holds **16** ports chosen the retired way, then starts a container with `WithPortBinding(4222, true)` three times: **starts all 3**, and the port Docker reports back is asserted not to be one of the 16 held.

Both pass, together, in **4 s**.

**The control that makes the red meaningful (Arm 3).** Releasing the squatter one line before the container start — changing nothing else — makes the retired arm **succeed**:

```
Backlog id 85 — the retired self-assigned shape was expected to LOSE the race every time and did not. Host port 35599
was chosen by a TcpListener that was then closed, another socket took it before the container started, and Docker
started the container anyway. Outcomes so far: repetition 1: host port 35599 — STARTED, no exception
```

Port held → loses every time. Port free → wins. One variable. That is the change of kind.

## Arming — id 85

Protocol for each arm: `cp` backup → mutate → `dotnet build OrderToCash.sln --no-incremental` → run the named test(s) → record verbatim → restore from the backup → `cmp` against the backup → `touch` → `--no-incremental` rebuild → confirming green run. No `git checkout`, no `git stash`, no git command that writes the index or working tree was used at any point; `git show HEAD:<path>` was used **once, read-only**, to obtain the committed retired shape for Arm 2.

| Arm | Mutation | Named test that failed | Verbatim message (abridged where noted) |
|---|---|---|---|
| **2** (bullet 5) | Re-introduce `GetFreeTcpPort` at one converted site — `tests/Gateway.IntegrationTests/KafkaContainerFixture.cs` restored to its committed retired form | `ExactlyOneMethodInTheRepositoryStillConstructsATcpListener` **and** `EveryWithPortBindingLetsDockerAssignTheHostPort` | *"…Unexpected listener-constructing method(s): **tests/Gateway.IntegrationTests/KafkaContainerFixture.cs::GetFreeTcpPort**. Allowed: …"* and *"Offending call site(s): **tests/Gateway.IntegrationTests/KafkaContainerFixture.cs:27 — WithPortBinding(_hostExternalPort, ExternalContainerPort) binds host port '_hostExternalPort', which this fixture chose for itself**"* — names the fixture and the port, as bullet 5 requires |
| **5** | Drop the optional `true`: `WithPortBinding(ExternalContainerPort)` in Billing's fixture (defeat-list attack 7 — the C# overload defaults it to `false`, so this compiles and binds host port 9092 fixed) | `EveryWithPortBindingLetsDockerAssignTheHostPort` | *"Offending call site(s): **tests/Billing.IntegrationTests/KafkaContainerFixture.cs:63 — WithPortBinding(ExternalContainerPort) binds host port 'ExternalContainerPort', which this fixture chose for itself**"* |
| **6** | A real self-assigned binding inside `#if DEBUG` with a compliant decoy in `#else` (defeat-list attack 5) | `NoConditionalCompilationHidesAPortBindingOrAListener` | *"…Offender(s): **tests/Fulfillment.IntegrationTests/KafkaContainerFixture.cs carries [#if DEBUG, #else, #endif]**"* — **and `EveryWithPortBindingLetsDockerAssignTheHostPort` PASSED under this mutation**, which is the point: the parser really is blind to the branch the compiler takes, so the premise is removed structurally rather than by guessing symbols |
| **8** | Rename the one exempted call site (`BuildWithASelfAssignedHostPort` → `BuildTheRetiredArmContainer`) | `TheOneAllowedSelfAssignedBindingStillExists` (and, as expected, `EveryWithPortBindingLetsDockerAssignTheHostPort`, since the renamed site is no longer exempt) | *"…is exempted … because it is the retired arm of the change-of-kind proof, and it no longer exists. Either restore it or delete the exemption; leaving the exemption in place leaves a named hole with nothing behind it."* — followed by all **19** `WithPortBinding` call sites found |
| **4** | Point the sweep at the wrong tree (`RepositoryPaths.Find(".")` → `Find("src")`) | `EveryFixtureExpectedToBindAContainerPortWasActuallyRead`, `ExactlyOneMethodInTheRepositoryStillConstructsATcpListener`, `TheOneAllowedSelfAssignedBindingStillExists` — **3 of 5** | *"This guard's own population went empty — the allowed listener-constructing method(s) … were not found at all, so the sweep is reading the wrong tree and would report a clean run over nothing. Found: (nothing)"* |
| **3** | Release the squatter before the container start in the change-of-kind test | `Id85_TheRetiredSelfAssignedShape_FailsEveryTime_…` | quoted in full above |

Arm 4 is the one worth reading twice: with an empty population the two *rule* tests pass vacuously, and the three *population* tests are what catch it. That asymmetry is deliberate — violations are found per invocation (so a new offender shows up rather than filtering itself out), and emptiness is found by a literal expected set.

Every restore was confirmed by `cmp` against the backup **and** by re-reading the changed line; `git diff` was never offered as evidence of a restore, since two of the mutated files are untracked and `git diff` on them cannot fail.

---

## Defeat list — which of `CLAUDE.md`'s ten attacks were run against these guards

| # | Attack | Against the id-85 source guards | Against the id-81 precondition guard and the race proof |
|---|---|---|---|
| 1 | Delete the behaviour | **Run** — Arm 2 (re-introduce the retired shape), Arm 8 (delete the exempted site) | **Run** — Arm 1 deletes `ConnectAsync()`; Arm 3 deletes the squatter's hold |
| 2 | Corrupt a payload field the test supplied | **Run** — Arm 5 corrupts the argument list itself (`true` removed); the guard reads the argument from the tree, not from anything the test supplies | **Not applicable** — the id-81 guard asserts a value the test does not supply (`ConnectionState` comes from the NATS client); the race proof asserts on a port Docker chose and on Docker's own error text |
| 3 | Substitute a valid sibling identifier | **Run and deliberately defeated-by-design**: the listener allow-list is keyed by **shape** (a method constructing a `TcpListener`), not by the name `GetFreeTcpPort`, so renaming it does not escape — Arm 2's mutation would have been caught under any name. The `WithPortBinding` rule likewise keys on the invoked member, not on a fixture name. The live sibling families here are the container-port constants (`4222`, `9092`, `1025`, `8025`) and the fixture class names; swapping either leaves the binding shape intact and is therefore **not** a defect this guard exists to catch — it is a compile-time or wait-strategy failure instead. Stated rather than skipped. | **Run** for the race proof: the two arms use the **same image, command and wait strategy**, so substituting either would break both arms identically rather than hiding one |
| 4 | Shadow the pattern from a comment or string literal | **Run, and live in the tree**: six converted fixtures now contain the text `WithPortBinding(hostPort, containerPort)` and `TcpListener` **inside doc comments**, and all five guards are green. A text scanner would false-red on every one of them. Roslyn sees invocations and object creations only | **Not applicable** — the guard executes code |
| 5 | Hide the real thing in a dead region | **Run** — Arm 6, and it is the reason `NoConditionalCompilationHidesAPortBindingOrAListener` exists | **Not applicable** — executed code, and the whole file would have to compile either way |
| 6 | Hide it in a raw or verbatim string | **Considered, cannot bite**: a raw or verbatim string is a `LiteralExpressionSyntax`, never an `InvocationExpressionSyntax` or `ObjectCreationExpressionSyntax`, so text inside one is invisible to both rules for the same structural reason as attack 4. The inverse — hiding a *real* call inside a string — is not possible; a string does not execute | **Not applicable** |
| 7 | Drop an OPTIONAL element entirely | **Run** — Arm 5. This one is not hypothetical here: `WithPortBinding(int port, bool assignRandomHostPort = false)` has a C# default, so `WithPortBinding(9092)` compiles and means a *fixed* host port. The rule therefore demands the literal `true` be present rather than comparing presence | **Not applicable** — `ConnectAsync()` has no optional element to drop; dropping the whole call is attack 1 |
| 8 | Compare a literal to a literal | **Considered, cannot bite**: every assertion is over nodes read out of parsed files on disk; the literals in the class are the *expected* sets, and the actual sets are derived from the tree. Arm 4 proves the derived side can be made empty and that this is caught | **Not applicable** |
| 9 | Satisfy the closer half and leave the premise half stale | **Run** — this is exactly Arm 4 (the rule half green, the population half stale) and Arm 8 (the exemption half naming something that no longer exists). Both are guarded by their own case | **Run** — Arm 1's message states the premise (the 8/608-vs-0/550 measurement) and the closer (`ConnectionState`); the premise half is re-derivable from the diagnostic table in this record, and the numbers in the message were read off the runs logged above, not estimated |
| 10 | Let a build-output copy join the population | **Run in design, verified by construction**: `IsUnderBuildOutputDirectory` splits the absolute path and compares **segments**, so a `bin/`/`obj/` copy of any fixture is excluded by path, never by matching the text of a result line. Every shell enumeration in this record uses `find … -not -path '*/bin/*' -not -path '*/obj/*'` for the same reason | **Not applicable** |

## New premises this instrument carries (the instrument-change rule)

The id-85 guard is a Roslyn reader, and `CLAUDE.md` requires the premises a new instrument introduces to be enumerated and armed in the same round. Three, each armed:

1. **The parser and the compiler may disagree about which branch is live.** `CSharpParseOptions` here defines no symbols; the build defines `DEBUG`. Armed (Arm 6) and removed structurally rather than by symbol-matching, because passing `DEBUG` would only move the disagreement to `#if RELEASE` or a future `DefineConstants`.
2. **It is looking at the thing the claim is about.** The rule is applied to every `WithPortBinding` *invocation node*, not to somewhere the text also matches; and the line it reports is the span of the **method name**, not of the whole fluent chain (the first version reported line 27 for a call on line 28 — corrected before the arming table was finalised, and Arm 5 shows the corrected line 63 exactly).
3. **A sweep can read nothing and report clean.** Armed (Arm 4) by three population assertions with literal expected sets.

---

## Suite counts, reconciled

Baseline stated in the brief, measured today before this work: **18 projects, 2010 passed, 0 failed, 0 skipped, 0 build warnings.**

New tests added by this batch: **7** — 5 in `Architecture.Tests` (`ContainerFixtureHostPortAssignmentTests`) and 2 in `Projector.IntegrationTests` (`ContainerHostPortAssignmentRaceTests`). No test was deleted, renamed or merged; `Gateway.IntegrationTests`' `NatsRpcClientIntegrationTests` still has its 7 cases (verified directly: `Failed: 0, Passed: 7`).

Expected total: **2010 + 7 = 2017.**

**Final `./quality.sh`, clean, exit 0**: format check clean, build succeeded with **0 Warning(s)**, **18 projects, 2 017 passed, 0 failed, 0 skipped**. Per project, read off the run:

| Project | Passed | | Project | Passed |
|---|---|---|---|---|
| SharedKernel.UnitTests | 50 | | Seed.IntegrationTests | 6 |
| Cqrs.UnitTests | 23 | | **Architecture.Tests** | **41** (36 + 5 new) |
| Contracts.UnitTests | 24 | | **Projector.IntegrationTests** | **65** (63 + 2 new) |
| Notifications.UnitTests | 111 | | Notifications.IntegrationTests | 26 |
| Gateway.UnitTests | 237 | | Fulfillment.IntegrationTests | 64 |
| Fulfillment.UnitTests | 146 | | Billing.IntegrationTests | 90 |
| Billing.UnitTests | 262 | | Gateway.IntegrationTests | 65 |
| Orders.UnitTests | 491 | | Orders.IntegrationTests | 152 |
| Seed.UnitTests | 44 | | | |
| Projector.UnitTests | 120 | | **Total** | **2 017** |

**Reconciliation: 2 010 (baseline) + 7 (new) = 2 017. Exact; nothing moved the wrong way.** The four unit-suite figures the brief pins are unchanged — Gateway 237, Billing 262, Orders 491, Fulfillment 146 — which is what a batch touching only integration fixtures and architecture tests should do.

That run is also the real proof that the Kafka conversion is correct rather than merely green in one project: all six converted Kafka fixtures started real brokers, and every integration suite that depends on one (Orders 152, Billing 90, Gateway 65, Fulfillment 64, Notifications 26, Projector 65) passed in full. `init.sh` exits 0.

## Files touched

| File | Entry | What |
|---|---|---|
| `tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs` | 81 | `ConnectAsync()` + precondition guard + `SuccessPathBudgetMs` constant + the measurement recorded in doc comments |
| `tests/{Orders,Notifications,Gateway,Fulfillment,Billing}.IntegrationTests/KafkaContainerFixture.cs`, `tests/Projector.IntegrationTests/TestSupport/KafkaContainerFixture.cs` | 85 | Docker-assigned port + startup-callback advertised listener; `GetFreeTcpPort` deleted |
| `tests/Notifications.IntegrationTests/MailpitContainerFixture.cs` | 85 | Docker-assigned ports (2); `GetFreeTcpPort` deleted |
| `tests/Notifications.IntegrationTests/MailpitAuthContainerFixture.cs` | 85 | Docker-assigned ports (4); `GetFreeTcpPort` deleted |
| `tests/Architecture.Tests/ContainerFixtureHostPortAssignmentTests.cs` | 85 | **new** — 5 guards |
| `tests/Projector.IntegrationTests/ContainerHostPortAssignmentRaceTests.cs` | 85 | **new** — the change-of-kind proof, 2 cases |
| `progress/impl_batch_d3_container_port_and_rpc_budget_robustness.md` | both | this record |
| `progress/impl_batch_d3_id81_budget_enumeration.txt` | 81 | the 103-hit classified enumeration, in full |

Not touched: anything under `src/`, `specs/shared/`, `feature_list.json`, `CLAUDE.md`, `init.sh`, `quality.sh`, the #7 checkout. **Neither entry's status was transitioned to `in_review`** — the brief reserves `feature_list.json` to the coordinator, and `CLAUDE.md`'s single-writer rule makes that the safe reading; both entries are ready for that transition. `tests/Gateway.IntegrationTests/ZzId81Diagnostic.cs` existed only while the cause was being measured and was deleted before any fix was written; it is not in the tree.

## What I could not do, and what surprised me

- **Bullet 3 of id 81 could not be satisfied as literally written** — see the discrepancy list at the top. Its recipe presumes a slowness cause that the measurements disprove.
- **The `NatsStockAvailabilityCheckerTests` sibling is left unfixed**, deliberately and in scope; it needs its own entry.
- **The surprise was D3.** I set a 120 000 ms budget purely so it could not interfere with a latency measurement, and it fired — on an idle machine, after seven rounds that each took three milliseconds. Everything after that was a different investigation from the one the entry expected, and it is the only reason the answer is not "the budget was too tight".
- **A smaller surprise, recorded because it is the sort of thing that gets mis-explained later**: the diagnostic rounds ran *faster* under 32 CPU hogs (≈20 ms/round) than idle (≈240 ms/round). The explanation is `StandInResponder`'s paced readiness loop: idle, the client reaches its first probe before the server has processed the responder's `SUB`, gets `NatsNoRespondersException` and pays one 50 ms pacing delay; loaded, it arrives later and the first probe already succeeds. It has nothing to do with the race, but a reader comparing the run times would otherwise reasonably suspect the measurement.


---

## Ported-idiom ledger — ADDED BY THE LEADER 2026-09-13, after the review found it owed and missing

`CLAUDE.md` binds the ledger to the **port**, and for an `sdd: false` feature it lives here. This record carried neither a ledger section nor a "none owed" line. The reviewer drafted "none owed" from an assumption, then checked #7's checkout and **disproved both halves** — which is the rule *"'none owed' is a claim about #7 exactly like any other, needing the same citation"* doing its job on the person applying it. Full text and citations: `progress/review_batch_d3_container_port_and_rpc_budget_robustness.md` §"DEFECT 2".

### Row A (id 85) — the retired shape IS #7's idiom, hand-rolled and weakened

| | |
|---|---|
| **#7 relied on** | testcontainers-node's `RandomPortGenerator().generatePort()` to pick the Kafka host port in advance, for exactly the reason #8 did — so `KAFKA_ADVERTISED_LISTENERS` can name it — in all six fixtures (`apps/projector/src/test-support/kafka-test-fixture.ts:35,38,46` and the same lines in `apps/{orders,billing,fulfillment,notifications}`; `apps/gateway/src/test-support/kafka-test-fixture.ts:41,47`, its comment at `:14-18` stating the reasoning verbatim). That generator is `get-port`, which does `net.createServer().listen(0)` → read → **`server.close()`** → resolve — **the identical TOCTOU window** — plus a process-local `lockedPorts` set that will not re-issue a port for 15 s. |
| **In #8 that property was supplied by** | eight independent hand-rolled `GetFreeTcpPort` copies with **no shared memory at all**, so two fixtures in one process, or two test assemblies in parallel, could be handed the same port. #7's 15 s lock prevents exactly that, and its absence is the most likely reason this bit #8 twice and is not on #7's record. |
| **In #8 it is now supplied by** | Docker itself — `WithPortBinding(containerPort, true)` holds the port from assignment to bind, with the advertised listener written in `WithStartupCallback` once `GetMappedPublicPort` is known. **A deliberate divergence from #7 and strictly stronger than it**: no window is left to lose, rather than a smaller one. |
| **Guard** | `ContainerFixtureHostPortAssignmentTests` (5 cases) and `ContainerHostPortAssignmentRaceTests` (2 cases). Both armed; three arms re-run independently by the review. |

**Why this row matters for #9:** `testcontainers-python` offers the same two routes and the same advertised-listener problem. Without this row, #9 has every reason to transliterate #7's `RandomPortGenerator` shape — the one #8 has just spent an entry retiring.

### Row B (id 81) — the cold shape is an artefact of the translation, and this record never said so

| | |
|---|---|
| **#7 relied on** | `nats.connect()` from `nats.js`, which resolves **only after the connection is established** (`apps/gateway/src/test-support/open-nats-test-fixture.ts:46`). The JS client has no lazy-connect mode, so a concurrent pair can never be a connection's first traffic. #7 also has **no OR4 equivalent** — a content search for a concurrent-traceparent spec across `apps/gateway/src` returns nothing — so this case is #8-native and its cold shape had no #7 counterpart to inherit. |
| **In #8 that property is supplied by** | nothing by default: `new NatsConnection(opts)` connects lazily on first use. It is now supplied **explicitly** by `await realConnection.ConnectAsync()` plus the precondition assertion (`tests/Gateway.IntegrationTests/NatsRpcClientIntegrationTests.cs:250-255`). |
| **Guard** | the precondition assertion; armed by deleting `ConnectAsync()`, re-run by the review (fails in 293 ms naming the budget and `'Closed'`). |

**This row reframes the finding rather than decorating it.** Without it, a future reader takes the assertion message to mean that "the cold shape loses one of the two replies in ~1 % of runs" is a property of **NATS**. It is a property of a **lazily-connecting client issuing concurrent first traffic** — a shape neither #7's `nats.js` nor #9's `nats-py` (`await nats.connect()`) can produce. Without the row, #9 inherits a spooky general claim about a broker instead of a precise one about a client idiom.

## Count correction — ADDED BY THE LEADER 2026-09-13

The round denominators recorded above do **not** reconcile with this record's own diagnostic table, confirmed by the review against the raw logs. Two table rows were dropped on no consistent ground — `D5[idle][budget=20000]` (40 rounds, 0 failures) from the cold arm, and `D8b[loaded32][preconnected]` (150 rounds, 0 failures) from the established arm.

| | Recorded | **Correct** |
|---|---|---|
| cold rounds | 608 | **648** |
| established rounds | 550 | **700** |
| total pairs | 1 158 | **1 348** |

The **8** cold failures and the **0** established failures are exact; only the denominators were wrong. The error is conservative in every direction that matters — the true cold rate is 8/648 = 1.23 % against the claimed 1.32 % (both "~1 %"), and the established arm is **stronger** than claimed (0/700, not 0/550). The cause, the fix and the margin are unaffected, and no acceptance bullet turns on it.

It still had to be corrected rather than footnoted, because `CLAUDE.md` is explicit that a number which will not reconcile is the work and not a caveat — and because these figures are embedded in a **test assertion message and a doc comment**, which is precisely the artefact #9 inherits without re-deriving. The two `tests/` occurrences (`NatsRpcClientIntegrationTests.cs:34` and `:253`) are corrected under a separate mechanical dispatch; the leader does not edit `tests/`.
