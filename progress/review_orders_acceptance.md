# Review — `orders_acceptance` (feature 15, phase 8)

**Verdict: REJECTED.** One blocking defect (D1), one required harness fix (D2), one significant host defect (D3), six advisories. Feature returned to `in_progress`.

`sdd: false`, so no `specs/orders_acceptance/` is expected and C6 does not apply to this feature. Reviewed against `progress/impl_orders_acceptance.md`, `specs/orders_aggregate/design.md` §8–§10, `specs/shared/asyncapi.yaml` (`ordersCreate` / `stockCheck` channels and their four payload schemas), `specs/shared/requirements.md`, `specs/shared/test-matrix.md`, and **`CLAUDE.md` as it stands on disk** (grepped, not the injected copy — the disk copy names three shared projects including `src/Cqrs`, and the arming protocol's forced-rebuild clause, both of which this review applies).

The blocking defect is not a style point and it is not the feature's design. It is the one branch pair in this feature that the implementer **personally found broken and personally fixed**, shipped with no test, and which survives its own re-breaking on a fully green solution.

---

## 1. What I did not re-run, and why

Per the leader's brief and `CLAUDE.md`'s *"probe the claims, do not re-run the world"*, the following six claims were established by the leader with `scripts/arm-probe.sh`, rebuilds forced either side, restored from their own backups, zero residue. **I did not re-prove any of them** and I cite the leader's table for each:

| Claim | Cited from the leader's table |
|---|---|
| #7's blocking money-mapping defect avoided in substance | Fixtures 2500 / 50 / 2450 with `Assert.NotEqual` guards, no zero-discount fixtures; swapping `InitialDiscount` / `TotalAmount` at `OrdersCreateResponder.cs:100` fails `Orders.IntegrationTests` |
| Responder scopes per message | `IServiceScopeFactory` injected, `CreateScope()` inside the handler, `IDispatcher` resolved from the scope |
| Deleted error code gone | `UnknownOrderStatusError` / `status_unknown` across `src/` and `tests/` → 0 |
| `CancellationReason` moved | Assigned inside `TransitionTo`, after the legality check and `Status = to` |
| `Rehydrate` O1 guarded | `Count == 0` → `Count == -1` fails `Orders.UnitTests` |
| `Rehydrate` O2 guarded | `EnsureLineCurrencyMatches(...)` → no-op fails `Orders.UnitTests` |

I also did **not** re-run the implementer's own seven-row arming table row by row, and I did not re-run the eleven-project suite twice. I ran it once, in full, because `quality.sh` green is itself a claim I had to check — and because D1 is a claim *about* the full suite (that it stays green while armed), which is one of the cases where a full run is the point.

Everything below this line I ran myself.

---

## 2. My own arming table — items 1 and 2 of the brief

Protocol: back up first, mutate, forced `--no-incremental` rebuild, run, restore from the backup (never `git checkout --`), re-read the restored line, forced rebuild again.

| # | Item | What was armed | Result |
|---|---|---|---|
| P1 | **1** | `NatsStockAvailabilityChecker.cs:40` — the `catch (NatsNoRespondersException)` body changed from `throw new StockCheckTransportError(...)` to `throw new StockCheckTimeoutError(RpcSubjects.StockCheck, options.Value.StockCheckTimeoutMs)`, collapsing the outage/timeout distinction the implementer's §2 says it fixed | **`Orders.UnitTests` 56/56 PASSED. `Orders.IntegrationTests` 43/43 PASSED (2 m 38 s, real NATS + real MS-SQL).** *** THE GUARD DOES NOT GUARD *** — restored, line re-read as `throw new StockCheckTransportError(RpcSubjects.StockCheck, "no responder is subscribed to fulfillment.stock.check.");`, rebuilt |
| P2 | **2** | `PlaceOrderCommandHandler.cs:29` — `: ICommandHandler<PlaceOrderCommand, PlaceOrderResult>` removed, then **the real host executed**: `dotnet run --project src/Orders/Orders.csproj --no-build` | **FIRED, in the real host.** `Unhandled exception. OrderToCash.Cqrs.DispatcherValidationException: No command handler is registered for OrderToCash.Cqrs.ICommandHandler`2[...PlaceOrderCommand,...PlaceOrderResult]. Exactly one is required.` at `DispatcherServiceCollectionExtensions.cs:35` ← `Program.cs:40`. Process exit 134, **before `builder.Build()` and before `RunAsync()`**. Restored, line re-read, rebuilt |
| P3 | **2** | `OrdersAcceptanceServiceCollectionExtensions.cs:42` — `services.AddScoped<IStockAvailabilityChecker, NatsStockAvailabilityChecker>();` commented out, then the same real host executed | **DID NOT FIRE.** `info: Microsoft.Hosting.Lifetime[0] Application started. Press Ctrl+C to shut down.` / `Hosting environment: Production`. The only failure was the absent NATS broker, from the responder's own subscribe. A missing port is **not** a boot failure. Restored, line re-read, rebuilt |

No residue: `find src tests progress -type f \( -size 0 -o -name '*.tmp' -o -name '*.orig' -o -name '*.bak' \) -not -path '*/obj/*' -not -path '*/bin/*'` returns nothing; all three probe target lines re-read correct; `git status --short` is 34 entries, unchanged from the start of the review.

---

## 3. Item 1 — the stand-in Fulfillment responder

**The stand-in itself is honest, and I confirm it.** `tests/Orders.IntegrationTests/StandInFulfillmentStockCheckResponder.cs` opens its **own real `NatsConnection`** against the **same real broker** (`NatsContainerFixture`, Testcontainers, `nats:2.14.5-alpine` — the tag `docker-compose.infra.yml` pins) and subscribes to the real `fulfillment.stock.check` subject. `grep -rn "INatsConnection" tests/` shows nothing mocked anywhere in this feature; the production `NatsStockAvailabilityChecker` makes a real `RequestAsync` and gets a real reply. That is a stand-in *process*, not a stand-in *transport*, and it is the right call.

**But the real Fulfillment outage is never exercised — it is not even simulated, and this is D1.**

`grep -rn "NatsStockAvailabilityChecker" tests/ --include=*.cs` returns **exactly one hit**, and it is an XML `<see cref>` inside a doc comment. **No test constructs, resolves or exercises the production stock-check client at all.** Every acceptance test starts a stand-in that answers; none omits one, and none makes one slow. The three tests that mention the transport error types construct them by hand (`OrdersCreateErrorMapperTests.cs:38,50`) or inject one through a fake port (`PlaceOrderCommandHandlerTests.cs:89`) — both of which prove the *mapper* and the *handler*, and neither of which proves that NATS throws the type the mapper is keyed on.

P1 turns that from a reading into a measurement: with the two branches collapsed into one, **99 tests across both Orders projects stay green.**

**The leak fix is also incomplete** — the second half of the brief's item 1. `DisposeAsync` (lines 80–102) drains the loop as:

```
await _cts.CancelAsync();
try { await _loop; } catch (OperationCanceledException) { }
await _connection.PingAsync();
await _connection.DisposeAsync();
_cts.Dispose();
```

If `_loop` faults with anything other than `OperationCanceledException` — a `NatsException` on a dropped connection, a `RpcJson.Deserialize` throw on a malformed message, an `InvalidOperationException` from `ReplyAsync` — `DisposeAsync` propagates it and `PingAsync`, `_connection.DisposeAsync()` and `_cts.Dispose()` **never run**. That leaks precisely the live, subscribed connection the fix exists to prevent, on the exception path the fix was written for. Separately, `StartAsync`'s `catch { await responder.DisposeAsync(); throw; }` (lines 49–53) will **replace the original probe failure** with whatever `DisposeAsync` throws, which on a broken connection is likely `PingAsync`. The implementer's §2 claims the leak is fixed; it is fixed for the probe-failure path only.

---

## 4. Item 2 — does boot validation run in the real host?

**For a missing or duplicated handler: yes, proven, in the real host (P2).** `src/Orders/Program.cs:40` calls `AddDispatcher(Assembly.GetExecutingAssembly())`, and `AddDispatcherFromTypes` (`src/Cqrs/DispatcherServiceCollectionExtensions.cs:131`) validates **synchronously, at composition time**, so the process dies at line 40 with a non-zero exit before `Build()` is ever reached. I ran the real executable, not a test service collection, and the stack trace's bottom frame is `Program.<Main>$(String[] args) in .../src/Orders/Program.cs:line 40`.

One note in the implementer's favour that I checked rather than assumed: `OrdersDispatcherRegistrationTests` uses a bare `ServiceCollection`, which the brief was right to be suspicious of — but `AddDispatcherFromTypes` derives its expected-handler universe **purely from the scanned type list** and never consults the container's other registrations, so the bare collection is a faithful proxy for this specific check, not a weaker one. The unit test is legitimate. P2 was still worth running, because that equivalence is a property of the current implementation rather than a guarantee.

**For a missing port: no — and that is D3.** `Host.CreateApplicationBuilder` enables `ValidateOnBuild` and `ValidateScopes` only when the environment is Development, and `Program.cs` never calls `UseDefaultServiceProvider`. P3 shows the real host printing `Application started` with `IStockAvailabilityChecker` unregistered. The failure would first appear inside `OrdersCreateResponder.HandleAsync` (`src/Orders/Presentation/OrdersCreateResponder.cs:58–79`), whose catch-all hands it to `OrdersCreateErrorMapper`, whose `_ =>` arm (`OrdersCreateErrorMapper.cs:66`) turns it into an `INTERNAL_ERROR` RPC reply logged at `LogWarning`. A container misconfiguration is thereby disguised as a per-request business error. `CLAUDE.md`: *"every port is registered explicitly in `Program.cs`, and the startup validation pass is what turns 'a handler is missing' from a runtime surprise into a boot failure. The lesson #7 paid for is that DI failures must be loud at boot; keep it that way."*

---

## 5. Item 3 — ruling on the non-fireable arming row (row 4)

**Ruling: the un-armability is legitimate, and the row is honestly written.** I verified the mechanism rather than the prose.

`Cancel` (`src/Orders/Domain/Order.cs:225–255`) builds its `OrderCancelled` through a closure that captures the **local** `reason` parameter, never the `CancellationReason` property. `TransitionTo` (`Order.cs:389–408`) assigns `Status`, then `UpdatedAt`, then `CancellationReason`, then calls `Raise(buildEvent())`, and `Raise` only appends to the uncommitted-events list — nothing dispatches synchronously. So on a single thread there is no observer, inside or outside the aggregate, that can distinguish "assigned inside `TransitionTo` before `Raise`" from "assigned in `Cancel` after `TransitionTo` returned". The change is a conformance correction to `design.md` §6.1's invariant wording, provable by reading only.

Row 4 states *"Survived — 36/36 still green. Genuine, honestly reported"*, names the reason, and claims no guard. That is the correct form for a non-fireable row and it is the opposite of the failure the arming protocol exists to catch: it would have been trivially easy to omit the row entirely, and it was not omitted. **Approved as written.** One improvement for next time, not a defect: a non-fireable row is stronger when it names what *would* make it fireable — here, any future edit that adds a statement to `TransitionTo`'s tail which reads `CancellationReason` — so the deferral carries a trigger rather than only a justification.

---

## 6. Item 4 — the traceability position

**Ruling: `16 green / 1 scoped / 46` is correct. No requirement was left unflipped.** Checked against the sources, not against the implementer's account:

- `specs/shared/requirements.md:56` assigns **R30–R36 and R61** to `fulfillment_stock` — feature 17, phase 9. R31's own wording is the **answering** side: *"THE SYSTEM SHALL answer per line whether the requested units are currently available, SHALL mutate no stock item and SHALL emit no fact."* `test-matrix.md:134` names its stack-neutral test `fulfillment/integration/stock-check.spec`, a Fulfillment file. Feature 15 builds the **caller**. Flipping R31 here would be a claim about a system that does not exist, which is exactly what matrix rule 3 forbids.
- **R13** (`test-matrix.md:106`) is already `DONE` against `OutboxAtomicityTests` with no stated shortfall. This feature's `AcceptanceItem3` proves the same atomicity through a longer path; there is nothing to flip.
- **R62** (`test-matrix.md:190`) sits under `observability_reliability` and stays `TODO`. `requestId` is read off the wire and read nowhere else — `PlaceOrderCommandHandler` never inspects `command.RequestId` and `causationId` is a fresh `UniqueId.New()`. Correct posture; a flip would be false.
- Feature 15 is `sdd: false` and owns **no** matrix row, so rule 3's gate has no rows to gate.
- `git status --short specs/` is **empty**: `specs/shared/` is untouched by this feature, no amendment, no silent fork. C7's first box holds.

The implementer's §6 said it went in expecting to flip a row and found it could not. I reached the same place from the sources independently. **The scope brief did not leave a requirement unflipped.**

---

## 7. CHECKPOINTS walk

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer
- [x] every agent definition declares its model or documents its deliberate inheritance — `init.sh` §2 all OK
- [x] `./init.sh` exits **0**

### C2 — state is coherent
- [x] at most one feature `in_progress` — `init.sh`: *"no feature in_progress"* (15 was `in_review` when checked; set to `in_progress` by this verdict)
- [x] every status is in `rules.valid_status`
- [x] every `done` feature has passing tests associated with it — 294 tests green across eleven projects
- [ ] **`progress/current.md` describes the active session** — it reads `**Feature:** none — outbox_and_idempotency closed, awaiting feature 15` / `**Status:** idle` while feature 15 has been implemented and reviewed. Leftovers from the previous session. **A6**
- [x] every `blocked` feature records why — none blocked

### C3 — architecture is respected
- [x] no EF Core / Kafka / NATS / MongoDB / ASP.NET Core inside any `Domain/` — **`Architecture.Tests` 15/15 green**, run, not eyeballed
- [x] no cross-service DB access — Orders touches only `otc_orders`; the reference catalogue join (`products` → `currencies`) is inside the Orders context, as `design.md` §8.3 rules
- [x] no shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — `Orders.csproj` adds only a `ProjectReference` to `Cqrs.csproj`
- [x] no `Domain/` namespace references `OrderToCash.Cqrs` — `grep -rn "OrderToCash.Cqrs" src/*/Domain` returns nothing; `CqrsDomainPurityTests` green
- [x] `src/SharedKernel` still has zero `PackageReference` — the single grep hit is prose in a comment; `SharedKernelHasNoPackagesTests` green
- [x] no `decimal` in domain arithmetic — `grep -rn decimal src/*/Domain` returns nothing; money is `long` minor units end to end, `Money.MinorUnits` → `bigint` → wire `long`
- [x] every interaction classifiable — `orders.create` and `fulfillment.stock.check` are both RPC in `asyncapi.yaml`'s `rpcTransport`; `order.placed.v1` goes to Kafka through the outbox. No Kafka-as-request-bus, no RPC-for-facts
- [x] no stray debug logging, no context-free TODOs

### C4 — verification is real
- [x] `./quality.sh` passes — **exit 0, 4 m 22 s**, format check + build + test + coverage
- [x] domain tests are pure — `Orders.UnitTests` domain cases reference no framework; the new Application-layer tests use hand-rolled fakes in `PlaceOrderTestDoubles.cs`, no mocking library referenced
- [x] integration tests use Testcontainers against real infrastructure — `NatsContainerFixture` drives a real `nats:2.14.5-alpine` via the generic `ContainerBuilder` (the precedent `KafkaContainerFixture` already set and this review accepts), alongside real MS-SQL. **No mocked broker anywhere in this feature**
- [x] coverage thresholds — Orders domain 88.7% by the implementer's filtered recount, `quality.sh` reports 90.2% / 97.2% / 95.8% on the domain-bearing reports; the ≥60% overall gate remains deliberately unenforced until feature 34 per `quality.sh`'s own header
- [x] no Jest anywhere — xUnit only

### C5 — the session closed cleanly
- [x] no suspicious untracked files, no build output outside `.gitignore`, no zero-byte strays under `src/` or `tests/` outside `obj/` — checked explicitly after my own probes
- [ ] **`progress/history.md` has an entry for the feature, including its effort record** — not appended: the feature is rejected and not closeable. Blocked by the verdict, not by the implementer
- [x] `feature_list.json` reflects the true state — set to `in_progress` by this verdict
- [ ] the human has been told what was done and how to test it manually — pending the re-submission
- [x] **Claude did not commit** — no `git commit`, no `git push` in this review

### C6 — spec-driven development
**N/A for this feature** (`sdd: false`, no `specs/orders_acceptance/` expected). Repository-wide the section still holds: `init.sh` reports *"SDD coherence: 2 sdd feature(s) past pending have their triple-doc"*.

### C7 — spec-reuse fidelity (the boxes this feature can move)
- [x] `specs/shared/` unchanged — `git status --short specs/` empty; no amendment raised, none needed
- [x] the `R<n>` ids are #7's, and none was claimed here — see §6
- [ ] `progress/history.md` effort records complete — pending, as C5

---

## 8. Defects

### D1 — BLOCKING. The Fulfillment outage/timeout classification is unguarded, and it is the branch this feature fixed

**File:** `src/Orders/Infrastructure/Messaging/NatsStockAvailabilityChecker.cs`, lines **38–53** (the `catch (NatsNoRespondersException)` and `catch (NatsNoReplyException)` blocks).

**Evidence:** probe P1 — collapsing line 40 into a `StockCheckTimeoutError` leaves `Orders.UnitTests` 56/56 and `Orders.IntegrationTests` 43/43 green. Corroborated statically: `NatsStockAvailabilityChecker` appears in `tests/` exactly once, in a doc comment.

**Why it matters, in three ways.**

1. It is **the code this feature discovered was wrong**. The implementer's own §2 and *"What surprised me"* record that `RequestAsync` throws `NatsNoReplyException` rather than returning a `NatsMsg<T>` with a null `Data`, that the fix was needed *"in both the production `NatsStockAvailabilityChecker` and the test-only stand-in"*, and that it was found *"by running the integration suite for real rather than trusting the XML doc"*. A production bug was found live, corrected, and shipped with **no regression test**. Re-introducing it is green. That is the exact shape `CLAUDE.md`'s arming protocol was written to stop, and it applies *"with double force where the branch has no live caller yet, because integration harnesses cannot reach it"* — Fulfillment is feature 17, so neither branch has a live caller.
2. It breaks a **binding** line of the spec this feature was told to implement. `specs/orders_aggregate/design.md` §9.2 fixes *"stock check unavailable / timed out → `UNAVAILABLE` / `TIMEOUT` with the subject in `details`"* as two distinct replies, and says why: *"The distinction the mapping preserves matters to feature 42 (terminal-rejection classification): a `VALIDATION_FAILED` or an `ORDER_NOT_CANCELLABLE` reply is a business outcome and must not be retried at capped backoff forever, whereas a `TIMEOUT` is transport and must be."* Feature 42 will branch on a distinction that nothing in this repository proves. `OrdersCreateErrorMapperTests` proves the mapper's table given the right exception type; nothing proves the right exception type is produced.
3. It is the **one claim the stand-in exists to make**. The brief's whole argument for a stand-in over a mock is *"the transport is what is under test"*. Four tests all start a responder that answers. The transport's two failure modes — nobody home, and home but silent — are the half of the transport a mock could not have proven either, and they are untested.

**What must change.** Two cases in `tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs`, both over the fixture's existing real broker, each armed before submission:

- start **no** stand-in, send a real `orders.create`, assert the reply is an `RpcError` with `code == "UNAVAILABLE"` and `details.subject == "fulfillment.stock.check"`, and assert zero order rows and zero outbox rows;
- start a stand-in that deliberately never replies (or shrink `OrdersAcceptanceOptions.Nats.StockCheckTimeoutMs` for that host), assert `code == "TIMEOUT"` with `details.timeoutMs`, and the same two zero-row assertions.

The arming that closes this row is **swapping the two `catch` bodies** and recording that both new tests fail, with the verbatim messages. A test that only asserts "some error came back" does not close D1 — the defect is precisely that the two errors are interchangeable today.

### D2 — REQUIRED. The stand-in's leak fix does not cover the general exception path

**File:** `tests/Orders.IntegrationTests/StandInFulfillmentStockCheckResponder.cs`, `DisposeAsync` lines **80–102**, and `StartAsync` lines **49–53**.

`await _loop` catches only `OperationCanceledException`. Any other fault propagates out of `DisposeAsync` **before** `PingAsync()`, `_connection.DisposeAsync()` and `_cts.Dispose()`, leaking a live subscribed connection — the same leftover-responder-answers-a-later-test failure the implementer already paid a `quality.sh` cycle to diagnose, reached by a different door. And `StartAsync`'s cleanup can mask the original probe exception with a `DisposeAsync` throw.

**Why it matters:** the implementer's account states the leak is fixed. It is fixed on one path. This is test-harness code, so it cannot corrupt production behaviour — but it can and did produce a false red under load, and a leak that only manifests under full-solution contention is the hardest class of flake to attribute.

**What must change:** put the connection and CTS disposal in a `finally`, so the drain's outcome cannot skip them; and in `StartAsync`, suppress or aggregate a disposal failure so the probe's own exception survives.

### D3 — SIGNIFICANT. The real host does not validate the container at boot, and the responder disguises the consequence

**Files:** `src/Orders/Program.cs` (no `UseDefaultServiceProvider`, environment defaults to Production); `src/Orders/Presentation/OrdersCreateResponder.cs:58–79`; `src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs:66`.

**Evidence:** probe P3 — the real host prints `Application started` with a port unregistered.

**Why it matters:** `CLAUDE.md` names this as the lesson #7 paid for, in the same bullet that mandates the dispatcher's validation pass. The handler half is genuinely solved (P2); the port half is not, and the responder's catch-all actively converts it into an `INTERNAL_ERROR` reply at `LogWarning` severity, which is the *worst* place for it to surface — indistinguishable, to the caller and to a log scan, from a genuine transient. **This is the first `Program.cs` in the repository and it sets the precedent for the other five services**, which is why it is worth fixing now rather than at feature 27.

**What must change:** one call in `Program.cs` — `builder.Services`' provider configured with `ValidateOnBuild = true` and `ValidateScopes = true` unconditionally, not only in Development — plus a test that arms it: unregister a port, assert the host **fails to build**. `OrdersDispatcherRegistrationTests` is the natural home.

### Advisories (not blocking)

- **A1** — `OrdersCreateAcceptanceTests.AcceptanceItems1And2_...` asserts the order row but never asserts that `order.placed.v1` reached the `outbox` table through the `orders.create` path. Its negative twin asserts `OutboxMessages.CountAsync() == 0`, which is trivially satisfied because no order exists at all. The pairing is asymmetric; one `Assert.Equal(1, await assertDb.OutboxMessages.CountAsync())` in the happy-path test would make both halves mean something. R13/R14 are covered elsewhere, which is why this is an advisory and not a defect.
- **A2** — `OrdersCreateResponder` performs no validation of the inbound payload. `asyncapi.yaml`'s `OrdersCreateRequestPayload` marks `retailerCode`, `companyCode`, `currency` and `lines` required and `lines` `minItems: 1`; a request omitting `lines` deserialises `Lines` to null and `ToCommand` (`OrdersCreateResponder.cs:82`) throws `NullReferenceException` → `INTERNAL_ERROR`, where the schema calls for a client-caused refusal. `CLAUDE.md` places "DTOs, validation" in `Presentation/`.
- **A3** — `AcceptanceItem1_OrdersCreate_CallsFulfillmentStockCheckWithTheRequestsOwnCompanyAndLines`: `observed` is also written by `WaitUntilSubscribedAsync`'s `PROBE` requests, so the assertion is about the **last** request the stand-in saw, not the only one. It still fails correctly if the real request never arrives, so the guard holds — noted because the case name reads stronger than the assertion.
- **A4** — `src/Orders/Presentation/README_PLACEHOLDER.cs` and `src/Orders/Domain/README_PLACEHOLDER.cs` both say *"Later phases replace this with real Presentation types"*. Both namespaces now hold real types. Dead files; delete them with this feature.
- **A5** — the Application-layer error codes (`STOCK_UNAVAILABLE`, `REFERENCE_DATA_NOT_FOUND`, `ORDER_DISCOUNT_NOT_SUPPORTED`) are SCREAMING_CASE while domain error codes follow `<subject>.<snake_case_reason>`. Deliberate and defensible — these are not `DomainError`s and they mirror #7's separate hierarchy — but the two conventions now coexist in one service with nothing recording that they are different on purpose. Worth a sentence in `design.md` §9 at the next spec touch.
- **A6** — `progress/current.md` still reads `**Feature:** none ... **Status:** idle`. C2's fourth box. This is the third recurrence in this repository's lineage (#7 recorded it three times; feature 14's review recorded it as its own D5). Its persistence across three reviews is itself the finding: a checkpoint that is re-opened every feature and closed by hand every feature is a candidate for an `init.sh` check, not for another advisory.
- **A7** (harness, no action on the implementer) — the invocation brief asked me to confirm *"fourteen architecture rules green"*. There are **fifteen**: 7 classes, 15 `[Fact]`s, `Architecture.Tests` 15/15 green. `CqrsDomainPurityTests` landed with feature 43 and the brief's count predates it. Recorded so the number is not carried forward wrong — and as one more instance of `CLAUDE.md`'s *"the injected copy of this file is a cache — check the disk"*, which is why I grepped the disk copy rather than quoting the injected one.

---

## 9. Confirming runs

| Check | Result |
|---|---|
| `./quality.sh` | **exit 0**, `real 4m22.239s` — format + build + test + coverage |
| Full suite | **294 passed, 0 failed** across eleven projects: SharedKernel 47, Cqrs 23, Contracts 21, Orders.UnitTests 56, Seed.UnitTests 34, Notifications.Integration 7, Seed.Integration 6, **Architecture 15**, Fulfillment.Integration 19, Billing.Integration 23, **Orders.Integration 43** (2 m 53 s) |
| Architecture rules | **15/15 green** (not fourteen — see A7) |
| `./init.sh` | **exit 0** — backlog coherent, valid statuses, SDD coherence, no superseded rule text outside `progress/`, repo-local identity OK |
| `dotnet format --verify-no-changes` | **clean, exit 0** |
| `dotnet build --no-incremental` | 0 warnings, 0 errors, 18.3 s |
| Zero-byte / `*.tmp` / `*.orig` / `*.bak` strays under `src/`, `tests/`, `progress/` (outside `obj/`, `bin/`) | **none** |
| Probe residue | none — all three armed lines re-read correct after restore, forced rebuild each time |

**Wall-clock.** Estimated from file mtimes and the bounding commit, not a stopwatch:

- lower bound `f8281d3` at **2026-09-03 06:50**;
- **implementation ≈ 1.1 h** — first feature-15 file `src/Orders/Domain/OrderStatus.cs` at **14:53**, last implementer-touched file `StandInFulfillmentStockCheckResponder.cs` at **15:51** (the post-`quality.sh` teardown-race fix), `progress/impl_orders_acceptance.md` written **15:59**;
- **leader verification pass ≈ 1.0 h** — `arm-probe.sh` restores stamped `OrdersCreateResponder.cs` 16:40, `Order.cs` 16:51, `PlaceOrderCommandHandler.cs` 17:35, `NatsStockAvailabilityChecker.cs` 17:40; plus four review attempts lost to server-side 529s before anything was written;
- **this review ≈ 0.9 h** — one full `--no-incremental` build, two real-host arming runs, one full armed `Orders.IntegrationTests` run (2 m 49 s), one `quality.sh` (4 m 22 s), `init.sh`, `dotnet format`, and the traceability walk;
- **feature total so far ≈ 3.0 h, 1 implementation session, 1 review pass (this one, rejecting).**

**#7 baseline: 2 implementation sessions, 2 review passes, its first rejection.**

---

## 10. The benchmark question

*"#8 was handed #7's blocking defect in advance and avoided it — did that show as a saving, and is the honest reading 'the warning worked' or 'a different defect took its place'?"*

**Both, and the second is the finding. The warning worked exactly as far as it was specific, and no further.**

**It showed as a real saving.** #7 needed a second implementation session and a second review pass to find its money-mapping defect. #8 never had it: the leader's table establishes it was avoided **in substance** — three distinct non-zero amounts (2500 / 50 / 2450) with explicit `Assert.NotEqual` guards, no zero-discount fixture anywhere, and swapping `InitialDiscount` for `TotalAmount` at `OrdersCreateResponder.cs:100` failing a real integration test. That is the defect made *unrepresentable* by the fixture, not merely absent from this run. #8's implementation was one session and ~1.1 h against #7's two sessions. On sessions, #8 is genuinely ahead.

**And a different defect took its place, of the same class one layer out.** #7's defect was a *wrong* mapping that its fixtures could not distinguish. #8's D1 is a *right* mapping — arrived at by finding and fixing a live production bug mid-feature — that no fixture distinguishes either. The transferred warning was a rule about **money fixtures**. The rule underneath it, which did not transfer, is: *the branch you just corrected is the branch most likely to be unguarded, because you corrected it by observation rather than by test.* #8 armed seven rows, including the money mapping it had been warned about, and did not arm the one thing it had personally just fixed.

So the honest reading is not "the warning worked" and not "a different defect took its place" — it is: **inheriting a specific defect buys you that specific defect. It does not buy you the class.** The strongest evidence for that is where D1 sits: not in code #8 wrote carelessly, but in code #8 wrote *because it had already found the bug there*. Foreknowledge sharpened attention on the named target and left the periphery exactly as thin as #7's was.

**Two caveats against over-reading the saving.**

1. The session count is not yet comparable, because the feature is not closed. #7's baseline is 2 implementation sessions **and 2 review passes including a rejection**; #8 has spent 1 implementation session and is now at 1 review pass, also a rejection. If the fixes land in one round, #8 closes at 2 + 2 — level on passes, ahead on implementation sessions and on wall-clock. That is the number to record when it closes, and it should be recorded as *level on rework, ahead on first-pass throughput*, not as a clean win.
2. The reuse's real contribution on this feature was upstream of the defect entirely: `asyncapi.yaml`'s four payload schemas, `design.md` §9.2's error table and §10's handler shape were **read, not designed**. `OrdersCreateErrorMapper` is a transcription of a settled table; `PlaceOrderCommandHandler`'s ordering (reference data → stock check → open the unit of work) was decided before a line was written. That is where the hour went that #7 had to spend, and it is a more durable finding than the defect story.

**Note for #9.** When you are handed a predecessor's defect, write down the *class* alongside the instance, and put the class in your arming checklist rather than the instance. The instance here was "assert three distinct money values"; the class is "arm every branch you corrected during this feature, especially one you corrected because a run surprised you". #8 satisfied the first and missed the second, in the same file where the surprise happened.

---

## 11. What must change before re-review

1. **D1** — two integration cases over the real broker proving `UNAVAILABLE` (no responder) and `TIMEOUT` (responder present, silent), each with the zero-order/zero-outbox assertions, and an arming record showing that **swapping the two `catch` bodies in `NatsStockAvailabilityChecker.cs` fails both**, verbatim messages included.
2. **D2** — `DisposeAsync` disposes the connection and CTS in a `finally`; `StartAsync`'s cleanup does not mask the probe's own exception.
3. **D3** — `Program.cs` validates the container on build unconditionally, with a test that fails when a port is unregistered.
4. **A6** — `progress/current.md` brought into lockstep with `feature_list.json`.
5. **A4** — delete the two dead `README_PLACEHOLDER.cs` files.
6. Advisories A1, A2, A3, A5 — address or record a reason; none blocks.

Re-run `./quality.sh` and `./init.sh`, and update `progress/impl_orders_acceptance.md`'s arming table with the new rows. Everything else in this feature — the stand-in's honesty, the real-host boot validation for handlers, the wire shapes against `asyncapi.yaml`, the money handling, the domain follow-ups, the traceability position, `specs/shared/` left untouched — I checked and it stands.

---
---

# Re-review — round 2

**Verdict: REJECTED (second pass).** The round-1 verdict and every defect above stand as written and are **not** amended.

**D1 — the blocking defect — is genuinely closed. D2, A1 and A4 are closed. D3's production fix is correct.** What blocks a second time is a single, repeating shape: **three of the four fixes written to close round 1's findings are themselves unguarded**, plus a harness guard of the coordinator's that fires on correct state. None of it is wrong behaviour. All of it is behaviour that nothing would notice losing.

Cost to close is low and mechanical — three small tests in files that already hold their siblings, and one `init.sh` condition. This should be a short round.

---

## R2.1 — What I did not re-prove

Established by the coordinator, cited not repeated:

| Claim | Cited from the coordinator's message |
|---|---|
| D1's fix is real at the branch | Collapsing the outage branch into `StockCheckTimeoutError` now **fails** `Orders.IntegrationTests`; restore green, zero residue; armed with `scripts/arm-probe.sh` under forced rebuilds |
| D3's wiring is present | `Program.cs` sets `ValidateOnBuild = true` and `ValidateScopes = true` unconditionally |

I did **not** re-run the implementer's round-2 arming rows, and I did not re-run the two `dotnet run` real-executable verifications of D3 — I ran those myself in round 1 and the coordinator has re-run them since. I **did** re-run `./quality.sh` in full, because "the suite is green" is a claim about the full suite and because my four new probes are claims about what that suite fails to catch.

---

## R2.2 — My round-2 arming table

Each via `scripts/arm-probe.sh` (backup first, mutate, forced `--no-incremental` rebuild, run, restore from the backup, forced rebuild, re-run). All four targets re-read correct afterwards; `find` for zero-byte/`.tmp`/`.orig`/`.bak` strays returns nothing.

| # | Target | Mutation | Suite | Result |
|---|---|---|---|---|
| Q1 | `src/Orders/Program.cs:42` — D3's own fix | `ValidateOnBuild = true,` → `ValidateOnBuild = false,` | `Orders.UnitTests` | *** suite still green — **GUARD DOES NOT GUARD** *** |
| Q2 | `src/Orders/Presentation/Rpc/OrdersCreateRequestValidator.cs:43` — A2's `companyCode` check | `missing.Add("companyCode");` → `_ = missing;` | `Orders.UnitTests` | *** suite still green — **GUARD DOES NOT GUARD** *** |
| Q3 | `OrdersCreateRequestValidator.cs:48` — A2's `currency` check | `missing.Add("currency");` → `_ = missing;` | `Orders.UnitTests` | *** suite still green — **GUARD DOES NOT GUARD** *** |
| Q4 | `src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs:27` — A2's whole point | `"VALIDATION_FAILED"` → `"INTERNAL_ERROR"` on the `InvalidOrdersCreateRequestError` arm | `Orders.UnitTests` | *** suite still green — **GUARD DOES NOT GUARD** *** |

Q4 is the sharpest of the four: it re-creates, verbatim, the symptom A2 was raised to fix — *"a client-caused refusal disguised as a server fault"* — and 304 tests do not notice.

---

## R2.3 — The seven items, answered

### 1. D1's two new tests, on their own terms — **PASS, fully**

Read line by line, not taken on report. `tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs`:

- `AcceptanceItem_OrdersCreate_MapsNoStockCheckResponderToUnavailableAndPersistsNoOrder` (line 240) — **no stand-in started at all**, so the production client observes a real `NatsNoRespondersException` off a real broker. Asserts `Assert.Equal("UNAVAILABLE", error.Code)`, `Assert.Equal(RpcSubjects.StockCheck, ((JsonElement)error.Details!["subject"]!).GetString())`, then `Assert.Equal(0, await assertDb.Orders.CountAsync())` and `Assert.Equal(0, await assertDb.OutboxMessages.CountAsync())`. **All four things you asked for.**
- `AcceptanceItem_OrdersCreate_MapsASilentStockCheckResponderToTimeoutAndPersistsNoOrder` (line 299) — asserts `"TIMEOUT"`, `details.subject`, **`Assert.Equal(500, ((JsonElement)error.Details!["timeoutMs"]!).GetInt32())`**, and the same two zero-row assertions. **All five.**

Neither assertion can be vacuous by construction: `error.Details!` on a null dictionary throws, and an absent key throws `KeyNotFoundException` — so a mapper that stopped populating `details` fails these tests rather than passing them silently. That is the property `"some error came back"` lacked.

The TIMEOUT case's design deserves the credit it is due. `StartSilentAsync` answers only `request.CompanyCode == "PROBE"` — the harness's own subscribe probe — and returns `null` for everything else. So the stand-in **is** demonstrably subscribed (which is what stops `NatsNoRespondersException` firing and makes this a genuine `NatsNoReplyException` case) while being silent to the request under test. That is the difference between testing the timeout and testing the outage twice, and it is got right.

### 2. The `orders.create` readiness probe — **SOUND, and A3 did not widen**

`WaitUntilOrdersCreateReachableAsync` (line 408) is not papering over an ordering problem; it closes a real and correctly-diagnosed one. `IHost.StartAsync()` returns once `BackgroundService.ExecuteAsync` has been **scheduled**, and `OrdersCreateResponder.ExecuteAsync` yields at its `SubscribeAsync` before the server has registered the SUB — so `StartAsync` returning has never meant "the responder is reachable". Three things make the probe honest rather than a disguised sleep:

- it is a **real round trip**, not a delay, so it terminates on the actual condition;
- it is **bounded** (100 × 200 ms) and ends in `throw new TimeoutException("orders.create responder never became reachable.")` — a loud, attributable failure, never a silent continue;
- it is the same idiom `NATS.Client.Core`'s own docs recommend for the subscribe-side race, already used by the stand-in and already reviewed here.

Applying it to all six tests rather than only the outage test is the right call, precisely because the other five were passing on *incidental* warm-up supplied by stand-in construction — timing no one chose and no one could see.

**Side-effect freedom, checked rather than assumed.** The probe sends `RetailerCode: "NATS-PROBE-CONNECTIVITY"`. `PlaceOrderCommandHandler` resolves the retailer **first**, before `CurrencyExistsAsync`, before `FindProductsAsync`, before `stockAvailability.CheckAsync`, and before the unit of work is opened — so the probe throws `ReferenceDataNotFoundError` → `NOT_FOUND` having touched nothing. There is a neat incidental proof of this in the suite already: `AcceptanceItems1And2_...` asserts `Assert.Equal("ORD-000001", reply.OrderReference)`, which could not hold if a probe had consumed a sequence value from `EfCoreOrderNumberAllocator`.

**A3's scope did not grow, and I checked rather than accepting the claim.** The probe never reaches `stockAvailability.CheckAsync`, so it never sends a `fulfillment.stock.check` request and never writes `observed` in `AcceptanceItem1_...`. `observed` remains last-write-wins across the stand-in's own stock-check probes and the real request only, exactly as A3 described in round 1. A3 stands unchanged, still an advisory.

### 3. D3's regression test — **the port half is real; the host half is not guarded (D6)**

`OrdersDispatcherRegistrationTests.RealHostComposition_BuildServiceProvider_SucceedsWhenEveryPortIsRegisteredAndFailsWhenOneIsRemoved` is a genuinely two-sided test in one method — it builds the real `AddLogging` + `AddOrdersOutbox` + `AddOrdersAcceptance` + `AddDispatcher` composition, asserts a clean build, then removes the `IStockAvailabilityChecker` descriptor and asserts the throw naming that type. That is a real guard on a real composition, and it is better than what round 1 asked for.

**But it does not guard the thing D3 actually was.** The test calls `BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true })` — it supplies those options **itself**. It therefore proves *the container refuses a broken graph when asked to validate*; it proves nothing about whether **the host asks**. Q1 measures the consequence: flipping `Program.cs:42` to `ValidateOnBuild = false` leaves the suite green. D3's entire content was that `Host.CreateApplicationBuilder`'s Development-only default made the check silent in Production — and that one line is now held in place by nothing but the two manual `dotnet run` observations, which no CI run repeats.

The implementer's live verification was correct and I do not doubt it. A verification that cannot run again is not a guard.

### 4. A2's validation — **behaviour correct; half the required set and the whole mapper case unguarded (D4, D5)**

The behaviour is right. `OrdersCreateRequestValidator.Validate` is called in `OrdersCreateResponder.HandleAsync` immediately after `RpcJson.Deserialize` and **before** `ToCommand`, so a malformed request can no longer NRE its way into the catch-all; it checks `retailerCode`, `companyCode`, `currency`, `lines` (null **or** `Count == 0`, matching `asyncapi.yaml`'s `required` set plus `minItems: 1`) and each `lines[i].productCode`; it lives in `Presentation/Rpc/`, which is where `CLAUDE.md` puts validation; and `OrdersCreateErrorMapper` matches `InvalidOrdersCreateRequestError` **first**, so it structurally cannot fall through to `INTERNAL_ERROR`.

The coverage does not match the behaviour:

- `OrdersCreateRequestValidatorTests` has five cases — well-formed, `lines` null, `lines` empty, `retailerCode` missing/blank (a `[Theory]`), `lines[i].productCode` blank. **`companyCode` and `currency` — two of the four fields `asyncapi.yaml` marks required — have no case at all**, and Q2/Q3 confirm each survives deletion on a green suite.
- **`InvalidOrdersCreateRequestError` appears in no mapper test.** `grep -rn InvalidOrdersCreateRequestError tests/` returns four hits, all inside `OrdersCreateRequestValidatorTests`. `OrdersCreateErrorMapperTests` is the file whose whole premise is *"7 cases, one per row of design.md §9.2's table"*; the new first arm was not given an eighth. Q4 confirms it reverts to `INTERNAL_ERROR` unnoticed.
- No integration test sends a malformed `orders.create` **over the wire**, so A2's end-to-end claim — the caller receives `VALIDATION_FAILED`, not `INTERNAL_ERROR` — is asserted nowhere at the level A2 was about. (**A9**, below.)

### 5. **There is no new architecture rule** — the premise of this item is false (A8)

`git status --short tests/Architecture.Tests/` is **empty**. The directory holds the same seven test classes as in round 1 and the same **15** `[Fact]`s. Nothing was added, so nothing needed arming.

"Fifteen — one more than before" repeats the stale *fourteen* that round 1's **A7** already corrected: the count has been 15 since feature 43 added `CqrsDomainPurityTests`. This is the second time in two rounds that a stale count has been quoted forward, which is exactly what `CLAUDE.md`'s *"the injected copy of this file is a cache — check the disk"* exists to prevent, applied to a number rather than a rule. Recorded so it stops here.

### 6. A4 — **the decline is correct, and my advisory was partly wrong**

I checked the justification rather than accepting it, and it holds completely:

- `specs/orders_aggregate/design.md:484` states it outright: *"`src/Orders/Domain/README_PLACEHOLDER.cs` **must not be deleted**: `tests/Architecture.Tests/DomainAssemblies.cs` resolves the Orders assembly through `typeof(OrderToCash.Orders.Domain.OrdersDomainPlaceholder)`"* — and line 46 of the same document carries `README_PLACEHOLDER.cs  KEEP — see §10.2`.
- Still true on disk: `DomainAssemblies.cs:34` and `OrdersDomainContractsTests.cs:37` both resolve through that exact type.
- `tests/Architecture.Tests/**` was outside this feature's touch scope, so the dependency could not have been removed first.

**A4 was my error to write as one advisory covering both files.** The Presentation placeholder was genuinely dead and is correctly deleted; the Domain one is a load-bearing assembly anchor. The implementer flagged the conflict with a citation instead of either silently overriding the advisory or silently breaking fifteen rules, which is the behaviour `CLAUDE.md` asks for on a spec conflict. Credit where due.

**What should eventually happen, so it does not become permanent by default (A10).** Re-anchor both call sites on a type that exists *because the domain exists* — `typeof(OrderToCash.Orders.Domain.Order)` — then delete the placeholder and amend `design.md` §10.2 and its line-46 `KEEP` note. That is two lines in `tests/Architecture.Tests/` plus a spec touch, so it must be **scheduled**, not taken opportunistically: give it to a feature that already has `tests/Architecture.Tests/` in scope, or to a `test_maintainer` task, and record it now so the placeholder does not outlive its reason by twenty phases. Until then, keeping it is correct.

### 7. Gates — **`quality.sh` green; `./init.sh` exits 1 (D7)**

`./quality.sh` — **exit 0, `real 4m34.773s`**, **304 passed / 0 failed** across eleven projects (`Orders.UnitTests` **64**, `Orders.IntegrationTests` **45**, `Architecture.Tests` **15/15**). `dotnet format --verify-no-changes` — clean, exit 0.

`./init.sh` — **exit 1, right now, on this tree:**

```
── 4. Session file in lockstep
[FAIL]  progress/current.md claims a feature while none is in_progress: "**Feature:** `orders_acceptance` (id 15, phase 8) — `sdd: false`"
══ init.sh: FAILURES above — do not advance the session ══
```

The new section-4 check branches on `inProgress.length`: exactly one → `current.md` must name it; **zero → `current.md` must match `/none|idle|awaiting/i`**. It has no branch for `in_review`, which is in `rules.valid_status` and is the status a feature holds for the whole of every review pass. So during any review the session file is *correct* — it names the feature under review — and the check fails it.

The check is a good idea and it did catch the real thing it was written for. But as written it is **a guard firing on state that is not wrong**, which `CLAUDE.md` names explicitly: *"A reviewer that rejects work against a superseded rule is a guard firing on something no longer true, which is the guard-that-does-not-guard inverted and just as expensive."* Same shape, different file. The fix is one condition: treat `in_progress` **or** `in_review` as "a feature is active, and `current.md` must name it", and require the idle wording only when neither exists. This is the coordinator's file, not the implementer's — recorded here because C1's last box is `./init.sh` exits 0, and it does not.

---

## R2.4 — CHECKPOINTS, round 2 (changes from round 1 only)

- C1 — [ ] **`./init.sh` exits 0** → now **fails** (D7). Every other C1 box unchanged and green.
- C2 — [x] `progress/current.md` describes the active session → **closed**; it now names `orders_acceptance` and the coordinator's new `init.sh` section 4 exists to keep it closed. (The check's own bug is D7, not a C2 regression.)
- C3 — unchanged, all green. `Architecture.Tests` 15/15, run not eyeballed; `tests/Architecture.Tests/` untouched this round.
- C4 — [x] `./quality.sh` passes (exit 0, 304 tests); [x] integration against real Testcontainers infrastructure — **strengthened this round**: the outage and timeout paths now run against the real broker; [x] coverage unchanged (no `Domain/` file touched); [x] no Jest.
- C5 — [x] no strays, verified after my four probes; [ ] `progress/history.md` effort record — still not appended, blocked by the verdict; [ ] human told what was done and how to test — pending re-submission; [x] Claude did not commit.
- C6 — N/A (`sdd: false`).
- C7 — [x] `specs/shared/` untouched (`git status --short specs/` empty); [x] no `R<n>` claimed; [ ] effort record pending.

---

## R2.5 — Traceability: confirming what I meant

**Yes — the matrix should stay unchanged, and that is exactly what round-1 §6 concluded.** Re-checked against round 2's additions rather than assumed:

- the two new D1 tests prove the **caller-side** transport classification, which is `design.md` §9.2's mapping table — a design obligation, not an EARS requirement. R31 remains the **responder's** requirement, mapped to `fulfillment/integration/stock-check.spec` (`test-matrix.md:134`), owned by feature 17;
- the validator proves `asyncapi.yaml`'s `required` set — a schema-conformance claim. **No `R<n>` covers `orders.create` request validation**: the only `orders.create` row in the matrix is R62 (idempotent replay, `test-matrix.md:190`), still correctly `TODO`;
- A1's outbox assertion strengthens this feature's own pairing; R13 (`test-matrix.md:106`) was already `DONE` with no stated shortfall, so there is nothing to flip.

`16 green / 1 scoped / 46 not-yet-green` remains correct, and `specs/shared/` is byte-untouched.

---

## R2.6 — Defects, round 2

Round 1's D1, D2, D3 and A1, A2, A4 are **closed**. A3 and A5 remain open advisories, unchanged. New:

### D4 — BLOCKING. A2's fix reverts to `INTERNAL_ERROR` on a green suite

**File:** `src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs:27`. **Evidence:** Q4.

A2 named one symptom: a client-caused refusal reaching the caller as a server fault. The fix is the `InvalidOrdersCreateRequestError` arm returning `VALIDATION_FAILED`. Changing that string back to `"INTERNAL_ERROR"` leaves 304 tests green. `OrdersCreateErrorMapperTests` exists specifically as one case per row of `design.md` §9.2's table and has seven; the new arm is the only one without a sibling.

**Fix:** an eighth case in `OrdersCreateErrorMapperTests` asserting `Map(new InvalidOrdersCreateRequestError("..."), clock)` yields `Code == "VALIDATION_FAILED"`. Arm it with the Q4 mutation and record the verbatim failure.

### D5 — BLOCKING. Half of `asyncapi.yaml`'s required set is checked by code and by no test

**File:** `src/Orders/Presentation/Rpc/OrdersCreateRequestValidator.cs:43` (`companyCode`) and `:48` (`currency`). **Evidence:** Q2, Q3.

`asyncapi.yaml`'s `OrdersCreateRequestPayload.required` is `[retailerCode, companyCode, currency, lines]`. Two of the four are guarded, two are not. This matters more than an ordinary coverage gap because the validator's *only* reason to exist is enforcing that list — a validator half of whose list is unenforceable-by-test is the same "looks complete, proves half" shape the round-1 D1 had.

**Fix:** extend the existing `[Theory]` (or add two `[Fact]`s) for `companyCode` and `currency`, null/blank/whitespace, in `OrdersCreateRequestValidatorTests`. Arm both.

### D6 — REQUIRED. `Program.cs`'s `ValidateOnBuild` is held in place by nothing repeatable

**File:** `src/Orders/Program.cs:42`. **Evidence:** Q1.

`RealHostComposition_...` supplies `ValidateOnBuild = true` itself, so it cannot notice the host ceasing to. D3's whole content was the Development-only default; that line is now unguarded and the only evidence it is set is two manual `dotnet run` observations.

**Fix, either shape — pick one and say why.** (a) Factor the host composition into a static, testable method (e.g. `OrdersHost.CreateBuilder(...)`) that `Program.cs` calls and the test drives, so the test observes the host's *own* options rather than its own. (b) A text-level guard over `src/Orders/Program.cs` asserting both flags are set unconditionally — the idiom this repository already uses (`RpcSubjectsTests` reads `asyncapi.yaml` as text; `progress/history.md` names *"a pure-text parity guard over committed artefacts"* as one of the two instruments #7 said would transfer). (a) is stronger; (b) is cheaper and honest about what it proves. Either must be armed with the Q1 mutation.

### D7 — BLOCKING (harness, the coordinator's file). `./init.sh` exits 1

**File:** `init.sh`, section 4, the `inProgress.length === 0` branch. **Evidence:** run above; `rules.valid_status` includes `in_review`; feature 15 is `in_review`.

The check has no branch for `in_review` and so demands the session file claim idleness during every review pass, when naming the feature is the correct content. It fires on correct state.

**Fix:** treat `in_progress` **or** `in_review` as "a feature is active — `current.md` must name it"; require the idle wording only when neither status is present. Then re-run and confirm exit 0 while 15 is `in_review`, which is the case that is broken today.

### Advisories

- **A3** *(carried, confirmed not widened)* — see item 2 above.
- **A5** *(carried, now three families)* — Application `SCREAMING_CASE` codes, domain `<subject>.<snake_case_reason>` codes, and now `InvalidOrdersCreateRequestError` with **no `Code` property at all** (mapped by exception type). Three deliberate conventions, none recorded as deliberate. `design.md` §9 owes a sentence; correctly out of this feature's touch scope, so schedule it.
- **A8** *(new, harness)* — no new architecture rule exists; the count has been 15 since feature 43. See item 5.
- **A9** *(new)* — no integration test sends a malformed `orders.create` over the wire. One seventh case in `OrdersCreateAcceptanceTests` (omit `lines`, assert the reply's `code == "VALIDATION_FAILED"`, assert zero order rows) would prove A2 end to end **and** close D4 at the level A2 was actually about. Recommended as the single test that does the most work.
- **A10** *(new)* — the scheduled disposition of `src/Orders/Domain/README_PLACEHOLDER.cs`. See item 6.

---

## R2.7 — Effort, and the benchmark question with the full arc

**Effort to date** (file mtimes and bounding commits, an estimate recorded as one):

| Phase | Wall-clock | Evidence |
|---|---:|---|
| Implementation, round 1 | ~1.1 h | `OrderStatus.cs` 14:53 → `StandInFulfillmentStockCheckResponder.cs` 15:51; report 15:59 |
| Coordinator verification, round 1 | ~1.0 h | `arm-probe.sh` restores 16:40 → 17:40; plus four launches lost to 529s |
| Review, round 1 | ~0.9 h | build, 2 real-host probes, 1 armed integration run, 1 `quality.sh` |
| Implementation, round 2 | ~0.6 h | `StandInFulfillmentStockCheckResponder.cs` 17:52 → `NatsStockAvailabilityChecker.cs` 18:28; report 18:23 |
| Review, round 2 | ~0.6 h | 4 `arm-probe.sh` runs, 1 full `quality.sh` (4 m 35 s), `init.sh`, `dotnet format` |
| **Total so far** | **~4.2 h** | **2 implementation sessions, 2 review passes, heading to a third of each** |

**#7 baseline: 2 implementation sessions, 2 review passes, its first rejection.** #8 has now **met** that count and is about to exceed it. The wall-clock is probably still lower; the session-count advantage is gone.

### The answer: neither offered reading — the evidence supports a third, and it is sharper

Not *"the warning worked"*. Not *"a different defect took its place"*. **Both of those are true and both are too small. What the two rounds actually measured is #8's failure signature, and it is remarkably uniform: #8 does not ship wrong code. It ships correct code with nothing holding it in place.**

Look at all seven defects across both rounds:

- **Round 1** — D1: a *correct* exception classification, unguarded. D2: a *correct* leak fix, incomplete on one path. D3: a *correct* dispatcher validation, not extended to ports.
- **Round 2** — D4: a *correct* mapper arm, unguarded. D5: two *correct* validator checks, unguarded. D6: a *correct* host setting, unguarded.

**Seven defects, zero wrong behaviours.** Every one is "the code does the right thing, and nothing would notice if it stopped." Compare #7's baseline defect: a **wrong mapping** — code that was actually incorrect, shipped, and found by a second review pass. Those are different failure modes, and the difference is the finding.

So the honest reading is: **the warning eliminated the class it named — wrong behaviour — and did not touch the class underneath it.** #8's three acceptance items were right on the first pass and never regressed across two rounds; the inherited money defect was made *unrepresentable*, not merely absent. That is a real transfer and it is worth recording as one. What did not transfer is the habit the warning was an instance of.

**And the recursive finding is the one worth carrying to #9, because it happened twice inside a single feature.** Round 1's D1 was the branch the implementer had *just fixed* after a live discovery. Round 2's D4, D5 and D6 are three of the four fixes written to close round 1. **Twice, the freshly-corrected code was the least-guarded code in the feature.** The mechanism is legible and it is not carelessness: you correct code *because you observed it was wrong*, and having observed it, it feels proven. It is not. Observation is not a regression test, and the moment of highest confidence is the moment of lowest coverage.

Three caveats so the row is not over-read:

1. **The saving was real and has now been spent.** #8 implemented in one session what took #7 two, and never had #7's blocking defect. It has since spent two review rounds adding guards. Net: comparable hours, different purchase — #8 will hand feature 42 a *proven* `UNAVAILABLE`/`TIMEOUT` distinction, which `design.md` §9.2 says that feature depends on and which #7 never proved because it never noticed the distinction was load-bearing.
2. **The rejections are not evidence the reuse failed.** They are evidence the gate works on a stack where the arming protocol is being applied for the first time to RPC transport code. Round 1's D1 would have shipped in #7 and been discovered by feature 42.
3. **One defect this round is the harness's, not the implementer's** (D7), and one item in the brief (a new architecture rule) did not exist (A8). Both are stale-cache errors of the same family as round 1's A7. When the coordinator writes the brief and also verifies part of the work, the parts it verifies are exactly the parts its own stale assumptions are invisible in — which is the same argument for an independent reviewer, applied one level up.

**Note for #9, superseding round 1's.** Keep an explicit rule: *every line you change in response to a review finding gets a test before the round closes, and you arm it.* Not the class of bug — the class of **moment**. The most dangerous code in any feature is the code you fixed last, and neither #7's inherited warning nor a coverage percentage will find it. Only reverting your own fix will.

---

## R2.8 — What must change before re-review

1. **D4** — an eighth case in `OrdersCreateErrorMapperTests` for `InvalidOrdersCreateRequestError` → `VALIDATION_FAILED`, armed with the Q4 mutation.
2. **D5** — `companyCode` and `currency` cases in `OrdersCreateRequestValidatorTests`, armed.
3. **D6** — guard `Program.cs`'s `ValidateOnBuild`/`ValidateScopes`, by testable composition root or by text guard; say which and why; armed with the Q1 mutation.
4. **D7** *(coordinator's, not the implementer's)* — `init.sh` section 4 must treat `in_review` as an active status. Re-run and confirm exit 0 **while feature 15 is `in_review`**.
5. **A9** *(recommended, not required)* — one wire-level malformed-request case in `OrdersCreateAcceptanceTests`; it closes D4 end to end as well.
6. **A5, A10** — schedule, do not do opportunistically; both need files outside this feature's touch scope.

Re-run `./quality.sh` and `./init.sh`, and append the new arming rows to `progress/impl_orders_acceptance.md`.

Everything else stands and I want it on the record as standing: D1's two real-transport tests, D2's `finally`, D3's production wiring and its port-half regression test, A1's outbox assertion, the readiness probe, the `StartSilentAsync` design, the A4 decline, and the traceability position. The behaviour of this feature is right. What is missing is what would tell you if it stopped being.

---
---

# Re-review — round 3

**Verdict: APPROVED.** Both prior verdicts and every defect above stand as written and are **not** amended.

D4, D5 and D6 are closed. D7 (the coordinator's) is closed and verified in the exact state that broke it. A9 was taken up and is the strongest single test in the feature. **Seven independent mutations of mine this round: seven fired, zero survivors.** The pattern that produced two rejections — *a correct change that nothing would notice being reverted* — has stopped, and I looked for it by shape rather than by instance, including in places nobody named.

Four advisories remain open; none blocks, each has a named owner and a next slot.

---

## R3.1 — What I did not re-prove

| Claim | Cited from the coordinator's message |
|---|---|
| D4 armed | `"VALIDATION_FAILED"` → `"INTERNAL_ERROR"` in `OrdersCreateErrorMapper.cs` — suite fails |
| D5 armed (`companyCode`) | neutering the required check at `OrdersCreateRequestValidator.cs:41` — suite fails |
| D6 armed | `ValidateOnBuild = true` → `false` in `OrdersHost.cs:48` — suite fails |
| D7 armed both ways | `in_review` + named → 0; `in_progress` + idle → 1; restored → 0 |

I re-ran `./quality.sh` in full (a claim about the full suite) and `./init.sh` **in the `in_review` state**, because that state is precisely what D7 was about and a fix verified in a different state proves nothing. Everything else below I ran myself.

---

## R3.2 — My round-3 arming table

All via `scripts/arm-probe.sh` — backup first, mutate, forced `--no-incremental` rebuild, run, restore from the backup, forced rebuild, re-run — except **P4**, which is a real-executable probe. Every target re-read correct afterwards; `find` for zero-byte/`.tmp`/`.orig`/`.bak` returns nothing.

| # | Target | Mutation | Suite | Result |
|---|---|---|---|---|
| Q5 | `OrdersCreateRequestValidator.cs:48` — `currency` | `missing.Add("currency");` → `_ = missing;` | `Orders.UnitTests` | **suite FAILED — the guard fires** |
| Q6 | `:38` — `retailerCode` (guarded before this round) | → `_ = missing;` | `Orders.UnitTests` | **suite FAILED — the guard fires** |
| Q7 | `:53` — `lines` (guarded before this round) | → `_ = missing;` | `Orders.UnitTests` | **suite FAILED — the guard fires** |
| Q8 | `:61` — `lines[i].productCode` | → `_ = missing;` | `Orders.UnitTests` | **suite FAILED — the guard fires** |
| Q9 | `OrdersCreateResponder.cs:65` — **the validator's call site**, not the validator | `OrdersCreateRequestValidator.Validate(request);` → a comment | `Orders.IntegrationTests` | **suite FAILED — the guard fires** |
| Q10 | `OrdersHost.cs:60` — the composition root's dispatcher registration | `builder.Services.AddDispatcher(...)` removed | `Orders.UnitTests` | **suite FAILED — the guard fires** |
| P4 | the **shipped binary**, with `IStockAvailabilityChecker` unregistered | `dotnet run --project src/Orders/Orders.csproj --no-build` | real executable | **dies at `Build()`** with `Unable to resolve service for type '...IStockAvailabilityChecker' while attempting to activate 'PlaceOrderCommandHandler'` — **before `Application started` ever prints** |

**Seven for seven.** For the record, round 1 was 3 probes / 1 survivor (D1), round 2 was 4 probes / 4 survivors (D4, D5, D6 and the mapper case). Round 3 is 7 / 0.

---

## R3.3 — The seven items

### 1. Has the pattern stopped, or moved again? — **stopped, and I checked for the shape, not the instances**

I enumerated round 3's whole diff — eight files — and asked of each *"what reverts green?"*:

| Changed file | Guarded by | Verified |
|---|---|---|
| `OrdersHost.cs` (new) — the two flags | `RealHostComposition_Build_...` | coordinator's arming; and **Q10** for the rest of the composition |
| `OrdersHost.cs` — the three `Add*` calls | same test | **Q10** — removing `AddDispatcher` fails it |
| `OrdersCreateErrorMapper.cs` | `Map_AnInvalidOrdersCreateRequestError_...` | coordinator's arming |
| `OrdersCreateRequestValidator.cs` | 7 cases in `OrdersCreateRequestValidatorTests` | **Q5–Q8** + coordinator's `companyCode` |
| `OrdersCreateResponder.cs` — the **call site** | the A9 integration test | **Q9** |
| `Program.cs` (thinned) | — | see the terminated regress below |
| the three test files | n/a | they *are* the guards |

**The one probe nobody asked for is the one worth reporting.** D4 and D5 could both be satisfied while the validator was never actually *called* — the unit tests invoke `Validate` directly, so deleting `OrdersCreateRequestValidator.Validate(request);` from `OrdersCreateResponder.HandleAsync` would leave every validator test and every mapper test green. That is exactly the shape that produced two rejections, one level out again. **Q9 shows it fires** — and the reason it fires is a deliberate design choice the implementer made and explained: A9 starts **no** stand-in Fulfillment responder, so a request that slips past validation reaches the stock check and comes back `UNAVAILABLE` instead of `VALIDATION_FAILED`. Had A9 been written with a stand-in running — the obvious, comfortable way — the empty-`lines` request would have reached `Order.Place`, raised `OrderMustHaveAtLeastOneLineError`, mapped to `VALIDATION_FAILED`, and **passed while proving nothing about the validator being wired in at all.**

That is the first time in this feature that a test was designed so that the *obvious* version of it would have been vacuous, and the non-obvious version was chosen with the reason written down. It is the single best piece of work in the three rounds.

I also checked the mapper arm's *ordering* claim (`InvalidOrdersCreateRequestError` matched first) and the composition's registration *order* — neither is behaviourally load-bearing (the error type is neither a `PlaceOrderError` nor a `DomainError`, so no arm shadows it; and `ValidateOnBuild` runs at `Build()`, after all registrations, so their order cannot change the outcome). No hidden ordering guard is missing.

**The one regress that terminates, and why that is legitimate rather than a fourth round.** Nothing asserts that `Program.cs` *calls* `OrdersHost.CreateBuilder`. I would have raised that had `Program.cs` retained any composition of its own — it does not: it contains **no** `builder.Services.*` call and **no** `ConfigureContainer`, only environment reading and `CreateBuilder → Build → RunAsync`. Reverting to an unvalidated host is therefore not a *reversion* of this round's change but a rewrite of the file, and **P4 closes it empirically anyway**: the shipped binary, run with a port removed, now dies at `Build()`. That is the exact inverse of round 1's P3, which printed `Application started` on the same mutation. The chain Program → OrdersHost → flags → refusal is proven end to end in the artefact that actually ships. A regress has to stop somewhere; it stops here on evidence, not on assertion.

### 2. D5's completeness — **all four required fields, plus the per-line one**

`asyncapi.yaml`'s `OrdersCreateRequestPayload.required` is `[retailerCode, companyCode, currency, lines]`, with `lines` additionally `minItems: 1`. Every one is now enforceable-by-test, and I armed the three the coordinator did not:

- `currency` — **Q5 fires** (the field you asked me to confirm);
- `retailerCode` — **Q6 fires** (guarded before this round, confirmed still guarded — a regression check, not a formality);
- `lines` — **Q7 fires** (same);
- `lines[i].productCode` — **Q8 fires** (beyond the required set; it prevents a null product code reaching `PlaceOrderRequestLine`);
- `companyCode` — the coordinator's arming, cited.

Five checks, five guards, no survivors. `OrdersCreateRequestValidatorTests` now holds seven cases: one well-formed, `lines` null, `lines` empty, and four `[Theory]`-driven null/blank/whitespace sets. D5 is closed completely rather than at the one point that was named.

### 3. D6's factoring — **nothing dropped, and no architecture rule was needed**

I diffed the move rather than trusting it. `OrdersHost.CreateBuilder` contains, in order: `Host.CreateApplicationBuilder(args)`; the `ConfigureContainer` with both flags (comment carried verbatim); `AddOrdersOutbox`; `AddOrdersAcceptance`; `AddDispatcher(Assembly.GetExecutingAssembly())`. That is exactly what `Program.cs` held before, in the same order, with nothing added and nothing lost. `Program.cs` retains only the environment reading, which is correctly *not* part of the DI-graph question.

One subtlety that could have broken silently and did not: `Assembly.GetExecutingAssembly()` moved from `Program.cs` into `OrdersHost`. Both live in the **Orders assembly**, so it still resolves to the same assembly and the scan is unchanged — and had it not, `AddDispatcher` would have thrown `DispatcherValidationException` at boot rather than failing quietly, so the failure mode was safe either way. Worth naming because "move a `GetExecutingAssembly()` call" is precisely the kind of refactor that is silently wrong in a different project layout.

**No fifteenth or sixteenth architecture rule was needed, and none was quietly skipped.** `tests/Architecture.Tests/` is byte-untouched this round (`git status --short` on it is empty) and still holds 7 classes / **15** `[Fact]`s, all green. The existing rules are about `Domain/` purity, `SharedKernel` package-freedom, `Cqrs`-in-domain, decimal-in-domain and fact-publisher confinement; a composition root is by definition the one place permitted to know every layer, so no existing rule is violated and no new rule is implied. `OrdersHost.cs` sits at `src/Orders/` root beside `Program.cs`, which is the precedent already set — not inside a layer folder, so it does not muddy the four-folder convention either. If a rule is ever worth having here it is *"a composition root contains registration and nothing else"*, and at n = 1 that would be inventing a rule from a single instance; the honest slot is when the second and third services acquire theirs (features 17, 19), where the shape can be compared rather than assumed.

### 4. A3's scope, now the probe runs everywhere — **not materially misleading; downgraded to a named, scheduled cleanup**

The readiness probe now runs in all seven tests in the file. It still cannot pollute `observed`, and I verified the mechanism rather than accepting the claim: its payload carries `RetailerCode: "NATS-PROBE-CONNECTIVITY"`, and `PlaceOrderCommandHandler` resolves the retailer **first** — before the currency check, before product resolution, before `stockAvailability.CheckAsync`, and before the unit of work opens — so the probe is refused at reference-data resolution and never emits a `fulfillment.stock.check` request at all. `observed` is written only by the stand-in's own stock-check startup probes and by the real request.

There is a second, incidental proof of the probe's side-effect freedom already sitting in the suite: `AcceptanceItems1And2_...` asserts `reply.OrderReference == "ORD-000001"`, which could not hold if any probe had consumed a value from `EfCoreOrderNumberAllocator`.

So A3 reads stronger than it asserts and is not misleading — the assertion still fails if the real request never arrives, and the probe's `CompanyCode` is the literal `"PROBE"`, which can never equal `OrderPersistenceTestSupport.CompanyCode`. **But it can now be retired outright for two lines**, which was not true when I raised it: `StartSilentAsync` introduced a `request.CompanyCode == "PROBE"` discriminator, and applying the same discriminator inside `AcceptanceItem1_...`'s answer callback (`if (request.CompanyCode == "PROBE") return …; observed = request;`) makes `observed` unambiguous. Downgraded from a standing advisory to **a two-line cleanup with a named mechanism**, owed whenever this file is next opened — not owed now.

### 5. The Domain placeholder — what should happen, so it does not become permanent by default

The decision to keep it remains correct: `specs/orders_aggregate/design.md:484` mandates it, and `DomainAssemblies.cs:34` and `OrdersDomainContractsTests.cs:37` both resolve the Orders assembly through `typeof(OrderToCash.Orders.Domain.OrdersDomainPlaceholder)`. Deleting it today breaks all fifteen rules for every service.

**What should eventually happen, concretely (A10):** re-anchor both call sites on a type that exists *because the domain exists* — `typeof(OrderToCash.Orders.Domain.Order)` — then delete `src/Orders/Domain/README_PLACEHOLDER.cs` and amend `design.md` §10.2 together with its line-46 `README_PLACEHOLDER.cs  KEEP — see §10.2` entry. Two lines in `tests/Architecture.Tests/`, one deletion, one spec touch.

**Why it needs a slot rather than good intentions.** It is a spec amendment, so it cannot be taken opportunistically by whoever notices; and the anchor is load-bearing for *every* service, so it must be changed when someone is already running the architecture suite. The natural slot is **feature 17 (`fulfillment_stock`)**, the next feature that adds a service whose `Domain/` namespace needs an anchor of its own — at which point the choice is made once for two services instead of retrofitted for one. Recorded here, and it belongs in feature 17's brief; if it is not taken there, it should be taken by a `test_maintainer` task before feature 20, because five more services will otherwise each inherit a placeholder whose reason nobody remembers.

### 6. Gates

| Check | Result |
|---|---|
| `./quality.sh` | **exit 0**, `real 4m3.051s` |
| Full suite | **312 passed / 0 failed**, eleven projects — `Orders.UnitTests` **71**, `Orders.IntegrationTests` **46**, Architecture **15/15**, plus SharedKernel 47, Cqrs 23, Contracts 21, Seed.Unit 34, Notifications 7, Seed.Integration 6, Fulfillment 19, Billing 23 |
| Architecture rules | **15/15 green**, `tests/Architecture.Tests/` untouched |
| `./init.sh` | **exit 0 — run with feature 15 at `in_review`**, the exact state D7 broke on. Sections renumbered as described: lockstep is 4, superseded-rules is 5 |
| `dotnet format --verify-no-changes` | **clean, exit 0** |
| Zero-byte / `*.tmp` / `*.orig` / `*.bak` strays under `src/`, `tests/`, `progress/` | **none**, checked after all seven of my probes |
| Probe residue | none — all seven targets re-read correct |

**D7's fix inspected, not just observed passing.** `init.sh` section 4 now filters `f.status === "in_progress" || f.status === "in_review"` into an `active` set and requires `current.md` to name one of them, falling back to the idle-wording check only when nothing is active. The comment names the finding and its cause. That is the right shape: it fixes the branch that was missing rather than loosening the check.

---

## R3.4 — CHECKPOINTS, round 3

- **C1** — [x] harness files present; [x] `progress/current.md` and `history.md` exist; [x] agent definitions declare their models; [x] **`./init.sh` exits 0** (round 2's open box, now closed, and closed in the failing state).
- **C2** — [x] one feature active; [x] statuses valid; [x] every `done` feature has passing tests (312 green); [x] `current.md` names the active feature and `init.sh` now enforces it mechanically; [x] no `blocked` features.
- **C3** — [x] domain purity by NetArchTest 15/15, run not eyeballed; [x] no cross-service DB access; [x] no shared runtime code beyond `SharedKernel`/`Contracts`/`Cqrs`; [x] no `Domain/` reference to `OrderToCash.Cqrs`; [x] `SharedKernel` zero `PackageReference`; [x] no `decimal` in domain arithmetic; [x] every interaction Kafka-fact or NATS-RPC per `asyncapi.yaml`; [x] no stray debug logging, no context-free TODOs.
- **C4** — [x] `./quality.sh` passes; [x] domain tests pure, hand-rolled fakes, no mocking library; [x] integration against real Testcontainers MS-SQL and NATS — **strengthened again this round**, the malformed-request path now runs over the real broker; [x] coverage thresholds (domain 88.7%, unchanged — no `Domain/` file touched); [x] no Jest.
- **C5** — [x] no suspicious untracked files or strays; [x] **`progress/history.md` entry with its effort record — appended with this verdict**; [x] `feature_list.json` reflects true state — feature 15 set `done`; [x] the human is owed the "what was done / how to test manually" report, which the leader gives at close; [x] **Claude did not commit** — no `git commit`, no `git push` in any of the three passes.
- **C6** — N/A (`sdd: false`; repository-wide, `init.sh` reports SDD coherence green).
- **C7** — [x] `specs/shared/` byte-untouched across all three rounds (`git status --short specs/` empty); [x] no `R<n>` claimed here, ids untouched; [x] effort record complete and honest, **including that this feature was not faster** — see below.

---

## R3.5 — Traceability, final

Unchanged and correct: **`16 green / 1 scoped / 46 not-yet-green`**. Round 3 added a mapper case, four validator cases and one integration case; none of them closes an `R<n>`. `asyncapi.yaml` schema conformance is a design obligation, not an EARS requirement — the only `orders.create` row in the matrix is **R62** (idempotent replay, `test-matrix.md:190`), correctly still `TODO` and correctly out of scope. **R31** remains Fulfillment's (`requirements.md:56`, `test-matrix.md:134`), and feature 15 owns no matrix row, so rule 3's gate has nothing to gate.

---

## R3.6 — Defects and advisories at approval

**Closed:** D1, D2, D3 (round 1); D4, D5, D6, D7 (round 2); A1, A2, A4, A6, A9.

**Open at approval — four, none blocking:**

- **A3** — `observed` in `AcceptanceItem1_...` is last-write-wins. Not misleading (the assertion fails if the real request never arrives), and now retirable in two lines with the `CompanyCode == "PROBE"` discriminator `StartSilentAsync` introduced. Owed when the file is next opened.
- **A5** — three error-code conventions now coexist in Orders: Application `SCREAMING_CASE`, domain `<subject>.<snake_case_reason>`, and `InvalidOrdersCreateRequestError` with no `Code` at all (mapped by type). All three deliberate, none recorded as deliberate. One sentence in `design.md` §9. Out of this feature's touch scope; schedule it.
- **A10** — the Domain placeholder's disposition. See item 5. Slot it in feature 17.
- **A11 (new, and mine to own)** — **`EfCoreOrderNumberAllocator` has no direct test.** Its `WITH (UPDLOCK, ROWLOCK)` concurrency claim and its self-seeding branch (`ISNULL(MAX(CAST(SUBSTRING(order_reference, …) AS int)), 0) + 1` over a **non-empty** `orders` table) are exercised by nothing: every integration test starts from a fresh database, so the seed always evaluates over zero rows and the `MAX`/`CAST` never runs on real data. I verified the SQL statically and it is correct — `OrderNumber.Prefix` is `"ORD-"` (4 chars) and the substring offsets are derived from `OrderNumber.Prefix.Length` rather than hardcoded, so a prefix change cannot desynchronise them, and `'ORD-000009' → '000009' → 9 → 10` is right. I did **not** run a live concurrency probe.

  Two things must be said plainly about A11. First, **#7's assurance for the identical code came from its reviewer's own probes, not from tests the implementer owed** — 24 allocators racing in 24 real MySQL transactions producing a gap-free `1..24`, and a virgin counter continuing correctly from a table already holding `ORD-900000` (`progress/history.md`, #7 feature 15). #8 has not reproduced either. Second, **I could have raised this in round 1, had the file in front of me, and did not.** Raising a previously-unraised production-code gap as a fourth blocking round would be the reviewer moving the goalposts, which is a worse failure than the gap. It is recorded as an advisory with a named next slot: **feature 16**, which owns the saga's transaction and directly inherits the "gap-free references cost serialised placement" ceiling this allocator creates.

- **A12 (minor)** — `progress/current.md`'s **Status** line still reads `in_progress — REJECTED on review, fix pass in flight` while the backlog says `in_review`. The lockstep check matches the `**Feature:**` line only, so this passes; the leader resets the file at close. Noted for completeness, owed by nobody.

**Also recorded, harness-side:** **A7** (round 1) and **A8** (round 2) — the architecture-rule count was quoted as "fourteen", then as "fifteen, one more than before", when it has been **15** since feature 43 and `tests/Architecture.Tests/` has not changed across any of the three rounds. Twice a stale number was carried forward into a brief. `CLAUDE.md`'s *"the injected copy of this file is a cache — check the disk"* applies to counts as much as to rules; a one-line `init.sh` assertion on the `[Fact]` count would close it mechanically, and that is cheaper than a third recurrence.

---

## R3.7 — Effort record

Estimated from file mtimes and the bounding commit `f8281d3` (2026-09-03 06:50). An estimate, recorded as one.

| Phase | Wall-clock | Evidence |
|---|---:|---|
| Implementation, round 1 | ~1.1 h | `OrderStatus.cs` 14:53 → `StandInFulfillmentStockCheckResponder.cs` 15:51; report 15:59 |
| Coordinator verification, round 1 | ~1.0 h | `arm-probe.sh` restores 16:40 → 17:40; plus four launches lost to server-side 529s |
| **Review pass 1 (REJECTED)** | ~0.9 h | 2 real-host probes, 1 armed integration run, 1 `quality.sh` |
| Implementation, round 2 | ~0.6 h | 17:52 → 18:28; report 18:23 |
| **Review pass 2 (REJECTED)** | ~0.6 h | 4 `arm-probe.sh` runs, 1 full `quality.sh`, `init.sh`, format |
| Implementation, round 3 | ~0.6 h | 18:49 → 19:26; report 19:23 |
| **Review pass 3 (APPROVED)** | ~0.7 h | 6 `arm-probe.sh` runs (one against the integration suite), 1 real-executable probe, 1 full `quality.sh`, `init.sh` in the `in_review` state, format, and a read of #7's own record for this feature |
| **Total** | **≈5.5 h** | **3 implementation sessions, 3 review passes** |

**#7's baseline for the same feature: 2 implementation sessions, 2 review passes, ~4 h** — its first rejection, 1 blocking defect + 7 non-blocking on pass 1, approved on pass 2 with **6 non-blocking defects left open**.

**#8 is slower on this feature: ~1.4× on wall-clock, +1 implementation session, +1 review pass.** That is the honest number and it goes in the benchmark table as a loss, not as a footnote.

---

## R3.8 — The benchmark question, with the full arc

*"#8 was handed #7's blocking defect in advance and avoided it, then was rejected three times for defects #7 never had. Is the honest reading 'the warning worked but the review found harder things', '#8's review is stricter than #7's was', or something the evidence supports better?"*

I read #7's own record for this feature before answering, because the question is about what the two **reviews** looked for, and that is checkable rather than arguable.

### The evidence: #7 found the same defect class and shipped it

#7's `orders_acceptance` was approved on pass 2 **with six non-blocking defects open**. Two of them are, in substance, #8's blocking defects:

> **#7's N2:** *"the DI guard proves the two compilers differ but nothing guards `emitDecoratorMetadata` or the six `dev` scripts themselves, so reverting either would leave the whole suite green."*

That is **#8's D6, almost word for word** — a correct fix, with nothing that would notice it being reverted. #7 classified it `non-blocking` and approved. #8 classified it blocking and rejected.

> **#7's N3:** *"the ESLint selector matches only `TSParameterProperty`, so a manually-assigned bare-typed constructor parameter evades it (demonstrated)."*

A guard that guards half of what it claims — **#8's D5**, which was two of four required fields. Non-blocking in #7. Blocking in #8.

And #7's carried **D4**: *"`progress/current.md` out of lockstep with `feature_list.json` for the third time."* #8 hit the same thing (A6), and turned it into a mechanical `init.sh` check that then had its own bug (D7) and got fixed. #7 recorded it three times and automated nothing.

### So: neither offered reading. The evidence supports a third

**Both assessments produced the same defect class. They drew the blocking line in different places — and #8 drew it where #7's own hand-off notes said it should be drawn.**

Not *"the review found harder things"*: the things found were the same things. Not *"#8's review is stricter"* as a bare claim, because that reads as a reviewer's preference — it is a **recorded policy difference with a traceable origin**. #7's own "for #8 and #9" section for this very feature says the portable artefact is *"a happy-path fixture with a zero discount makes money assertions look complete while proving nothing"*. #8 inherited that sentence, generalised it correctly to *"a guard that survives its own reversion proves nothing"*, and then enforced the generalisation as blocking. Three rounds is the price of that enforcement, and it bought seven armed guards where #7 shipped two unarmed ones.

### And the honest counterweight: #8's review was narrower where #7's was broader

This matters and I will not leave it out. #7's reviewer produced independent evidence #8's has not:

- 24 order-number allocators racing in 24 separate real-MySQL transactions → gap-free, duplicate-free `1..24`;
- a virgin counter over a table already holding `ORD-900000` continuing at `ORD-900001`;
- an RPC timeout measured at **808 ms** against an 800 ms budget with a subscriber that accepts and never replies;
- a real `kill -TERM` proving the shutdown drain on the real `AppModule`;
- reading `kafkajs`'s upstream source to root-cause a negative timestamp.

#8's reviewer ran mutation probes, traceability walks and real-executable boots — and left the allocator's concurrency and re-seeding unprobed (**A11**). **#7's review was broader on live-system interrogation; #8's was sharper on mutation.** Those are different instruments, and the trilogy is better off with both recorded than with either claimed as superior.

### What the arc actually measured

Across three rounds, **#8 shipped no incorrect line that any pass found.** Every one of the seven defects was *the behaviour is right and nothing holds it in place*. #7's blocking defect was a **wrong mapping** — code that was actually incorrect, on the wire, feeding a credit-simulator predicate. Those are different failure modes, and the difference is the transfer working: the warning eliminated the class it named.

**The recursive finding is the transferable one, and it held in both directions.** Round 1's D1 was the branch the implementer had *just fixed* after a live discovery. Round 2's D4/D5/D6 were three of the four fixes written to close round 1. Round 3's fixes were guarded — including at the call site nobody named — because the question changed from *"does my fix work?"* to *"what fails if my fix is reverted?"*. **That one substitution is what ended the loop**, and it is worth more to #9 than any number in this table.

**Note for #9, superseding rounds 1 and 2.** Two rules, in this order. (1) *Every line you change in response to a review finding gets a test before the round closes, and you arm it.* The most dangerous code in a feature is the code you fixed last, because you fixed it by observation and observation feels like proof. (2) *When you inherit a predecessor's non-blocking defect list, decide explicitly which of those you will treat as blocking* — and write the decision down before the first review, not after the third. #8 made that decision implicitly, three times, and paid a round each time. The list is short and it is already published: #7's N2 and N3 for this feature are the whole of it.

---

## R3.9 — What the human should test manually

The Orders host is now runnable. With `docker compose -f docker-compose.infra.yml up -d` and `.env` exported:

```
export $(grep -E '^MSSQL_(APP_PASSWORD|APP_USER|DB_ORDERS|HOST_PORT)=' .env | xargs)
dotnet run --project src/Orders/Orders.csproj
```

It should print `Application started` and subscribe to `orders.create`. Two things are worth seeing by hand, because they are the two findings this review cost three rounds to secure:

1. **Comment out `services.AddScoped<IStockAvailabilityChecker, …>` in `OrdersAcceptanceServiceCollectionExtensions.cs` and run it again.** It must die at `Build()` — *before* `Application started` — naming `IStockAvailabilityChecker`. That is D3/D6.
2. **With the host running and no Fulfillment process anywhere**, send an `orders.create` request over NATS. The reply must be `UNAVAILABLE` with `details.subject: "fulfillment.stock.check"`, **not** `TIMEOUT` and not `INTERNAL_ERROR` — and no order row must appear. That is D1.

Then restore both and re-run `./quality.sh`.

---

### R3.10 — Session-close note (added after the verdict)

Setting feature 15 `done` made `./init.sh` exit 1 on its own new section-4 check: with no feature active, `progress/current.md` may not name one. **That is the guard working, not D7 recurring** — and it is the first time in this repository that the lockstep check has caught the transition it was written for, rather than a stale leftover.

`progress/current.md` was therefore reset to the idle state `CHECKPOINTS.md` C2 prescribes (`**Feature:** none — orders_acceptance (id 15) closed, awaiting feature 16`, `**Status:** idle`, template retained). `./init.sh` re-run: **exit 0**, `progress: 17/43 features done`. Recorded here rather than done silently, because a reviewer editing a progress file is worth stating: it is the direct consequence of the status change this verdict makes, and leaving the repository with a red gate behind an APPROVED verdict would contradict C1's own box. No file under `src/`, `tests/` or `apps/web/` was touched by this review in any of its three passes.

This also closes **A12** (the stale `Status:` line noted at approval) as a side effect.
