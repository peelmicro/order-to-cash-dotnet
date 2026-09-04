# Review — `orders_saga_terminal_rejection_classification` (id 42, phase 8)

**Verdict: APPROVED.** 0 blocking defects, 0 required changes, **5 advisories** (A1–A5), all recorded below and none of which block the close.

`sdd: false` — no spec directory, no human gate. Specification of record: `feature_list.json` id 42's three acceptance bullets plus the seam feature 16 left at `specs/order_saga_orchestrator/design.md` §6.1, §6.3 and §12.

## What this review actually ran, and what it took on trust

Per `CLAUDE.md`'s *"probe the claims, do not re-run the world"*, the full suite was **not** re-run here. It was run by the coordinator immediately before this review — `./quality.sh` **exit 0**, `dotnet format` clean, build clean, **508 passed / 0 failed** across eleven projects, `./init.sh` exit 0 — and that run, not the implementer's, is the evidence for the whole-solution claim. The implementer did **not** run `./quality.sh` and said so plainly rather than implying otherwise; given a diff confined to `src/Orders` plus its own test projects, and given that it ran the solution build, the format check, `Orders.UnitTests` in full, `Orders.IntegrationTests` in full against real Testcontainers and `Architecture.Tests`, **the budget call was reasonable** — the one thing it left uncovered (the four other services' integration suites) cannot be reached by this diff. It is nonetheless a claim someone else had to close, and it is recorded here as such.

Run independently for this review:

- Three mutation probes, each with `dotnet build --no-incremental` before the confirming run, each restored from a backup copy taken first — never `git checkout --` (see the arming table below).
- `tests/Architecture.Tests` — **16/16 passed** (NetArchTest, run rather than eyeballed, per C3).
- `NatsSagaCommandsAdapterTests` + `SagaCommandDispatcherTests` — 29/29 green post-restore; `SagaCommandStoreTests` — 4/4 green post-restore against a real MS-SQL container.
- `diff -rq specs/shared` against the #7 checkout — only `test-matrix.md` differs, exactly as C7 permits.
- The nine terminal codes read out of `specs/shared/asyncapi.yaml` (lines 2839–2855) and, separately, out of #7's `apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.ts:79-100` — not taken from the implementer's report.
- #7's `saga-command-dispatcher.ts:171-194` read directly to adjudicate the disclosed divergence.

## CHECKPOINTS.md — boxes walked

### C1 — harness

- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all present.
- [x] `progress/current.md` and `progress/history.md` present.
- [x] `.claude/agents/` unchanged by this feature.
- [x] Agent definitions unchanged by this feature.
- [x] `./init.sh` exits 0 (coordinator's run, immediately pre-review).

### C2 — state

- [x] Exactly one feature not `done`/`pending` in flight: id 42.
- [x] Every status in `rules.valid_status`.
- [x] Every `done` feature has passing tests; this feature adds 16 and breaks none.
- [x] `progress/current.md` describes this session, not a leftover.
- [x] No `blocked` feature.
- [x] **44 features present, id 45 (`order_number_allocator_seed_race`) intact** — checked explicitly at the start and again at the end of this review. No `git checkout --` was run on `feature_list.json` at any point.

### C3 — architecture

- [x] NetArchTest suite **run**: 16/16. No `Domain/` file is touched by this diff (`src/Orders/Application/Ports/*` and `src/Orders/Infrastructure/**` only).
- [x] No cross-service DB access: the change touches `saga_commands` in the Orders database only.
- [x] No new shared runtime code; `src/SharedKernel`, `src/Contracts`, `src/Cqrs` untouched.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs`.
- [x] `src/SharedKernel` still has zero `PackageReference`.
- [x] No `decimal` anywhere in the diff; no money arithmetic in it at all.
- [x] Classification unchanged and correct: this is a **NATS-RPC reply** being interpreted, never a fact. No Kafka surface is touched. The `rejected` status is deliberately *not* a fact — the responder's own outbox owns the fact, per SO6.
- [x] No stray debug logging; the one new log line is a structured `LogError` carrying `Command`, `OrderId`, `RpcErrorCode`, `Attempt`, `Message`. No context-free TODO in the diff.

### C4 — verification

- [x] `./quality.sh` passes — **coordinator's run**, exit 0, 508/508. Not re-run here.
- [x] Domain tests pure — no domain test touched.
- [x] The one new integration test uses Testcontainers MS-SQL, real migrations, real SQL; no mocked broker or DB anywhere in this diff.
- [ ] **Coverage thresholds** — reported, not gated, by design. `quality.sh`'s own header says it reports a number and does not enforce one; feature 34 `sonarqube_quality_gates` (phase 21) owns the threshold. Box left open honestly rather than ticked against a gate that does not yet exist. This is the standing position for phase 8, not a finding against this feature.
- [x] No Jest. xUnit throughout; `apps/web` untouched.

### C5 — session close

- [x] No suspicious untracked files: `git status` shows exactly the twelve touched source/test files, `feature_list.json`, `progress/current.md`, and the new impl report.
- [x] `progress/history.md` entry with effort record appended on this approval (below).
- [x] `feature_list.json` set to `done` by this review, and by nothing else.
- [ ] The human has been told what was done and how to test it manually — **the leader's next step**, not this reviewer's.
- [x] No commit, no push, by me.

### C6 — SDD

Not applicable: `sdd: false`, no `specs/<name>/` is owed. The one SDD-adjacent obligation is `specs/shared/test-matrix.md`, and it is correctly untouched — see the traceability section.

### C7 — reuse fidelity

- [x] `specs/shared/` byte-identical to #7's apart from `test-matrix.md` — verified by real `diff -rq` against the #7 checkout during this review, not from memory.
- [x] No amendment, silent or otherwise, in this diff.
- [x] `R<n>` ids: none claimed by this feature, correctly (below).
- [x] `n8n/workflows/*.json` untouched.
- [x] The API script is untouched and unaffected.
- [x] Effort record appended, and it records a **loss** (below) rather than rounding it into a win.
- [ ] README benchmark section — the leader's wrap-up, not this feature's.

## Acceptance bullets → tests, verified

There is **no `R<n>` for this feature and none should be invented.** `specs/shared/test-matrix.md`'s saga block is R19–R29; R29 covers the *timeout → backoff → park → DLQ* clause and nothing in it names the terminal-vs-transient split. The `R42` appearing at `test-matrix.md:219` is the shared requirement R42 (compensation), an unrelated numeric coincidence with this feature's backlog id — confirmed by reading the row. #7 recorded the identical finding for its counterpart. **Leaving the matrix untouched is correct**; adding a row would have been a false claim of shared-requirement coverage.

| Acceptance bullet | Named test(s) | Verified how |
|---|---|---|
| 1 — a terminal-business `RpcError` is classified terminal, not retried forever | `NatsSagaCommandsAdapterTests.R42_ATerminalRpcErrorCode_MapsToSagaCommandBusinessRejectionErrorNotTransportError` (`[Theory]`, all 9 codes); `...R42_ReleaseStockAgainstAnAlreadyConsumedReservation_PreconditionFailedIsTerminalNotTransport`; `SagaCommandDispatcherTests.R42_ATerminalBusinessRejectionCallsThePortExactlyOnceDelaysZeroTimesAndRejectsRatherThanParking` | Probe 1 and probe 2 below — both killed |
| 2 — a genuinely retryable transport failure is unaffected | `NatsSagaCommandsAdapterTests.R42_ATransientRpcErrorCode_StillMapsToSagaCommandTransportErrorUnchanged` (`[Theory]`, `TIMEOUT`/`UNAVAILABLE`/`INTERNAL_ERROR`); `...AnRpcErrorReplyBody_WithATransientCode_MapsToSagaCommandTransportError`; `SagaCommandDispatcherTests.R42_ATransientRpcErrorIsUnaffectedByTheTerminalClassification_StillRetriedToExhaustionAndParked` (3 calls, `[500, 1000]` backoff, one `ParkAsync`, zero `RejectAsync`); pre-existing `SO4_RetriesATimedOutCommandUpToMaxAttempts...` and `ExhaustionParksWithTheAccumulatedAttemptsAndTheLastError` unmodified and green | Probe 1 left every transient test green while killing only the two `PRECONDITION_FAILED` tests — the split is per-code, not per-file |
| 3 — the row reaches a resolved end state rather than retrying indefinitely | `SagaCommandStoreTests.RejectAsync_MarksTheRowRejectedAccumulatesAttemptsClearsTheLease_AndClaimDueAsyncNeverReclaimsIt` (real MS-SQL) | Probes 2 and 3 below — both killed |

## Mutation probes run by this review (the point of it)

Backups taken to the scratchpad first; every restore verified with `diff` → zero output **and** followed by `dotnet build --no-incremental` before the confirming green run.

| # | Mutation | Named test that died | Verbatim |
|---|---|---|---|
| 1 | `NatsSagaCommandsAdapter.IsTerminalRpcErrorCode`: `"PRECONDITION_FAILED"` removed from the terminal set — i.e. the *exact live bug* re-introduced, one code only | `R42_ATerminalRpcErrorCode_MapsToSagaCommandBusinessRejectionErrorNotTransportError(terminalCode: "PRECONDITION_FAILED")` **and** `R42_ReleaseStockAgainstAnAlreadyConsumedReservation_PreconditionFailedIsTerminalNotTransport` | `Assert.Throws() Failure: Exception type was not an exact match / Expected: typeof(...SagaCommandBusinessRejectionError) / Actual: typeof(...SagaCommandTransportError) ---- ...SagaCommandTransportError : fulfillment.stock.release: transport failure: PRECONDITION_FAILED: reservation already consumed` — **2 failed, 21 passed** |
| 2 | `SagaCommandDispatcher`: `store.RejectAsync(...)` in the short-circuit replaced with `store.ParkAsync(...)` — the resolution changed in *kind*, from terminal to retry-eligible | `SagaCommandDispatcherTests.R42_ATerminalBusinessRejectionCallsThePortExactlyOnceDelaysZeroTimesAndRejectsRatherThanParking` | `Assert.Empty() Failure: Collection was not empty / Collection: [Tuple (85d7426c-…, 1, "fulfillment.stock.reserve: terminal business rejec"···)]` |
| 3 | `EfCoreSagaCommandStore.ClaimDueAsync`: the sweeper's SQL predicate widened to `(status = 'pending' OR status = 'rejected')` — i.e. the *structural exclusion* the design leans on, deleted | `SagaCommandStoreTests.RejectAsync_MarksTheRowRejectedAccumulatesAttemptsClearsTheLease_AndClaimDueAsyncNeverReclaimsIt` (real MS-SQL) | `Assert.DoesNotContain() Failure: Filter matched in collection / ↓ (pos 0) / Collection: [SagaCommandRecord { … OrderReference = ORD-000004, Command = StockRelease, … Attempts = 1 }]` |

**Probe 1 is the central claim re-armed, and it was armed at finer grain than the implementer's own.** The implementer disabled the whole classifier (`=> false`) and reported 10 of 23 tests failing. That proves the classifier is load-bearing, but it cannot distinguish "the `[Theory]` genuinely covers each code" from "one shared assertion covers all of them". Removing a **single** code killed exactly the two tests that name it and left the other seven terminal cases and all three transient cases green — so the theory's per-code coverage is real.

**Probe 3 is the specific trap the previous feature produced, and it is not present here.** The `Assert.DoesNotContain(due, …)` on the last line of the integration test is a negative assertion in a database holding exactly one row, so on its face it could pass because the sweep returned nothing for an unrelated reason — a guard on a *likelihood*, not a *kind*, and no arming the implementer performed reached it (mutating the status to `"parked"` fails at the first assertion, three lines earlier, and never executes the sweep). Probe 3 loaded the mutation that only that assertion can catch, and **it fired**: the row is genuinely due by every other criterion — `created_at` deliberately pushed to exactly the `PendingGraceMs` boundary (10 s, and the predicate is `<=`, evaluated against a frozen `FakeClock` so the equality is exact), lease cleared — and the *only* thing excluding it is `status = 'rejected'`. The negative assertion is live, not vacuous. This is the strongest single result in the review.

**One incident from my own probing, recorded because it is the protocol's own trap firing in real time.** My first confirming green run batched `dotnet build tests/Orders.UnitTests tests/Orders.IntegrationTests` into one invocation; MSBuild rejected the two-project argument (`For switch syntax, type "MSBuild -help"`), the build silently did not happen, `--no-build` then executed the **still-armed binaries**, and 1 unit + 1 integration test "failed" against restored, correct source. Rebuilding each project separately turned both green. That is exactly the stale-binary mechanism `CLAUDE.md` warns about, in its false-red direction — and it is the reason the rule says to *force* the rebuild and check its exit, not merely to run one.

## The closed set — verified against the contract, independently

`specs/shared/asyncapi.yaml:2839-2851` declares a closed twelve-value `RpcError.code` enum. Read directly. The implementation's split:

- **Terminal (9)** — `VALIDATION_FAILED`, `NOT_FOUND`, `CONFLICT`, `PRECONDITION_FAILED`, `ORDER_NOT_CANCELLABLE`, `STOCK_UNAVAILABLE`, `INVOICE_NOT_PAYABLE`, `PAYMENT_MISMATCH`, `DOMAIN_ERROR`.
- **Transient (3)** — `TIMEOUT`, `UNAVAILABLE`, `INTERNAL_ERROR`.

**Are all nine genuinely business outcomes?** Yes. Each names a decision the responder's own domain reached about *this* request: a malformed request (`VALIDATION_FAILED`), an absent or non-matching entity (`NOT_FOUND`, `CONFLICT`), a state precondition (`PRECONDITION_FAILED`, `ORDER_NOT_CANCELLABLE`, `INVOICE_NOT_PAYABLE`), an insufficiency (`STOCK_UNAVAILABLE`), an amount disagreement (`PAYMENT_MISMATCH`), or a rule violation (`DOMAIN_ERROR`). Re-sending the byte-identical request under the same idempotency key `(orderReference, operation)` cannot change any of them, because the responder's answer is a function of state the caller is not changing by asking again.

**Are the remaining three genuinely transport or infrastructure conditions?** Yes. `TIMEOUT` the schema itself marks as caller-produced (`asyncapi.yaml:2852-2855`) — it never reaches this branch in practice, since `NatsNoReplyException` and a null `Data` both throw `SagaCommandTimeoutError` earlier; listing it transient for completeness costs nothing and is the correct answer if a responder ever echoes it. `UNAVAILABLE` is a dependency being down. `INTERNAL_ERROR` is an unexpected fault, by definition not a decision about the request. **A later attempt can genuinely resolve all three, and can resolve none of the nine. The split is right, and it is right on the contract's own terms rather than by transcription.**

Cross-checked against #7's `isTerminalRpcErrorCode` (`nats-saga-commands.adapter.ts:79-100`): the nine and the three are **identical**, member for member. The implementer's claim to have cross-checked rather than transcribed holds; I reached the same nine from the contract before opening #7's file.

**A3 (advisory, no action).** The one code where the classification is a judgement rather than a tautology is `NOT_FOUND`: on a subject whose responder is eventually consistent, a `NOT_FOUND` can in principle be a visibility race that a retry *would* resolve. It is not one here — every saga command is issued after the fact that establishes its subject's state, and both sides are in this repository — and #7 made the same call, so re-cutting it unilaterally would break parity for a hypothetical. Recorded so that if a live `NOT_FOUND` ever parks a saga, this line is where the argument is already written down.

## The disclosed divergence from #7 — judged

An `RpcError` code **outside** the closed set falls to the transient side (`_ => false`), where #7's TypeScript has a `never`-typed exhaustive default that throws.

**The implementer's justification is factually correct, and I checked it rather than accepting it.** #7's dispatcher catch (`saga-command-dispatcher.ts:171-194`) tests `error instanceof SagaCommandBusinessRejectionError` first and otherwise falls through to a **generic** `error instanceof Error ? error.message : String(error)` retry path. So in #7, an unrecognised code throws a plain `Error` out of `call()`, is caught generically, and is **retried** — TypeScript's `never` branch is a compile-time device whose runtime behaviour is transient. #8's C# catch is *filtered* (`when (ex is SagaCommandTimeoutError or SagaCommandTransportError)`), so an unmodelled exception type would escape `DispatchClaimedAsync` entirely, past the lease, with no park and no reject. `_ => false` reproduces #7's observed behaviour; throwing would not. **The divergence is in the mechanism only; the behaviour matches.**

**Is failing open to "retry" actually safer than failing to "terminal", given the bug being fixed was 81+ futile retries?** Yes, and the argument is stronger than "it cannot happen":

- The two wrong answers are not symmetric in cost. A wrongly-terminal command is **unrecoverable by construction** — nothing in this codebase re-claims a `rejected` row (probe 3 proves it), and no operator redrive exists yet, so the saga is dead and the order sits in its intermediate status forever. A wrongly-transient command is bounded waste: three in-line attempts, then a park, then the sweeper's capped-backoff retry — cheap NATS calls at a rate ceiling, on a row whose `status`, `attempts` and `last_error` are all visible to an operator.
- **The transient side is the side that will alert.** `ParkAsync` is the branch feature 27 `observability_reliability` will hook for `<topic>.dlq` and `order.saga_failed.v1`; `RejectAsync` deliberately will not be (see below). So an *unanticipated* code lands in the branch that gets escalated, and an *anticipated* business "no" lands in the branch that is merely logged. That is the correct assignment: the thing you did not model is precisely the thing a human should be told about.
- The bug this feature fixes was a **known, in-contract** code whose semantics are definitively terminal. Nothing about that bug argues for guessing "terminal" about a code whose semantics are unknown by construction.

The counter-argument deserves stating: with the current code, an unknown code retries **indefinitely** at capped backoff via the sweeper, which is the same *shape* as the bug just fixed, and until feature 27 lands that indefinite retry is visible only in a database row. That is real, and it is why the default is defensible rather than free. It is unreachable against the current closed enum, both sides of every one of these subjects are in this repository, and the doc comment states the reasoning at the method. **Accepted as correct.**

## The inherited dead-letter deferral — confirmed, same shape, disclosed

Confirmed by reading, not by report: `src/Orders` contains **no** dead-letter or DLQ mechanism at all (the only repository-wide match is an unrelated doc comment in `Domain/CancellationReason.cs`), so there was nothing to half-wire. Feature 27 `observability_reliability` (phase 14) carries the acceptance bullet *"failed processing lands on `<topic>.dlq` after N attempts"* and is the real owner. #7's own record (`progress/impl_orders_saga_terminal_rejection.md` item 5, and its review's single non-blocking finding) deferred it identically and recommended folding `rejected` into the phase-14 feature. **#8 defers it the same way, for the same stated reason — "the responder legitimately said no" is a different signal from "the responder is broken" — and discloses it in two places in the implementation record.** Correct, and correctly disclosed.

**The cost of the deferral, stated plainly so it is not lost:** a terminal rejection leaves the order parked in an intermediate status with **only a log line** to say so — no `order.saga_failed.v1`, no DLQ, no timeline entry. That is still strictly better than the pre-fix state (an unbounded, near-silent retry loop), which is why it does not block. Feature 27 must include `rejected` rows in whatever it builds, and this paragraph is the record of that debt.

## The untouched split — confirmed

Feature 15 paid a blocking review defect (D1) to keep `UNAVAILABLE` (→ `SagaCommandTransportError`) distinct from `TIMEOUT` (→ `SagaCommandTimeoutError`). **This feature added a third category and re-cut neither.** Both existing types survive with their construction sites unchanged: `NatsNoRespondersException` → `SagaCommandTransportError`, `NatsNoReplyException` and null `Data` → `SagaCommandTimeoutError`, and the transient `RpcError` codes → `SagaCommandTransportError` with its message format byte-identical to before. The dispatcher's `catch … when (ex is SagaCommandTimeoutError or SagaCommandTransportError)` filter is unmodified; the new clause sits *before* it. `NatsStockAvailabilityChecker` — feature 15's own file — is untouched, and I confirmed by reading it that it has no `RpcError`-body branch to disturb in the first place.

## Correctness details checked beyond the acceptance bullets

- **No migration is needed, and none was written.** `status` is `nvarchar(10)` with no `CHECK` constraint; `rejected` is 8 characters. `design.md` §12 pre-authorised exactly this.
- **No other consumer of `saga_commands.status` exists.** Grepped the whole `src/` tree for status literals and comparisons: the only matches outside `EfCoreSagaCommandStore` are in `src/Seed` (a different table's `Status`) and `Order.cs` (the order aggregate's own enum). Nothing switches exhaustively on this column, so widening it breaks nothing.
- **`RejectAsync` accumulates rather than overwrites `attempts`** (`current.Attempts + attemptsMade`), matching `ParkAsync`, and truncates `last_error` at the same 4 000-character cap. `next_attempt_at` is set to `NULL`, which also releases the SO11 lease — correct, since the lease and the retry schedule share that column and neither has meaning for a terminal row.
- **Both dispatch paths are covered.** `TryClaimAsync` (the fast path) matches only `pending`/`parked`, and `ClaimDueAsync` (the sweeper) likewise; a `rejected` row is invisible to both. Probe 3 proves the second one against a real row rather than by reading the SQL.
- **`ISagaCommandStore` widening is complete** — the solution builds clean, which is itself the proof for an interface member, and the five fake/decorator implementations were updated in the shape each file already used (recording, delegating, or `NotSupportedException` where the path cannot be reached).

## Advisories (non-blocking)

**A1 — `specs/order_saga_orchestrator/design.md` still documents the pre-42 taxonomy, and #7 updated its equivalent as part of this feature.** §6.1's error table (line ~368) still reads *"A reply body that is an `RpcError` → `SagaCommandTransportError` → yes — **for now**"*, §6.3's column table (line 401) still reads *"a fourth `rejected` token is feature 42's"*, and §12's checklist row (line 476) still frames the token as pending. None of it is a false statement about feature 16 — the section is explicitly headed *"the pre-42 shape, deliberately"* — and the design doc belongs to a closed feature, so nothing in `CLAUDE.md` obliges a retro-edit. But it is now the only architecture-level record of an error taxonomy that the code no longer implements, and the next reader of §6.1 (features 25, 27) will find a table that disagrees with `NatsSagaCommandsAdapter`. #7 corrected §6.1/§6.3 inside this same feature. **Recommend a short docs pass folded into this feature's commit, for process parity with #7 and because it is three paragraphs.** Not blocking: documentation of a closed feature, self-labelled as superseded, with the successor named in the same sentence.

**A2 — a sibling of this very bug survives, untouched and out of scope, in `NatsStockAvailabilityChecker`.** `src/Orders/Infrastructure/Messaging/NatsStockAvailabilityChecker.cs:60` does `RpcJson.Deserialize<StockCheckReplyPayload>(reply.Data)` with **no `RpcError`-body discriminator at all**. If Fulfillment answers `fulfillment.stock.check` with an `RpcError`, `Available` deserialises to `false` and `Lines` to `null`, and the very next line — `payload.Lines.Select(...)` — throws a bare `NullReferenceException` out of order acceptance, mapped to nothing. Not reachable today (the Fulfillment responder lands in phase 9/10, and against a stand-in it never happens), and explicitly out of this feature's scope — the brief said not to disturb that file and the implementer correctly did not, having first read it to confirm the instruction held for a reason rather than by omission. **Recommend the leader file a backlog entry** so it is closed *with* feature 17/18 rather than discovered by an NRE in an acceptance path. This is the same class of defect feature 42 exists to fix, one file over.

**A3 — `NOT_FOUND`'s terminal classification** — see the closed-set section. No action; parity binds; the argument is now on record.

**A4 — no wire-level end-to-end test of a real `RpcError` body over NATS.** The classification is proven at unit level (a fake requester returning real `RpcError` JSON bytes, deserialised by the real `RpcJson`) and the terminal end state at integration level against real MS-SQL; what is missing is a Testcontainers test where a stand-in responder actually publishes an `RpcError{code: PRECONDITION_FAILED}` on the wire and the row ends `rejected`. `StandInSagaResponders` can only return typed success replies today, so this is a responder-harness addition rather than "add a test". **The implementer disclosed exactly this, and #7 made the identical call for the identical reason.** Accepted: the three armed guards cover each link of the chain, and the missing test would prove the joins, not the links. Recommend the capability be added when feature 17/18 builds the real responders, at which point the test is nearly free.

**A5 — the closing "ready for review" rested on a suite the implementer did not run.** Judged reasonable above, and the implementer's honesty about it is worth more than the coverage would have been. Recorded so the effort figures below are read correctly: **the coordinator's `quality.sh` run is part of this feature's cost**, not overhead outside it.

## Bookkeeping note

`feature_list.json` id 42 moved `pending` → `in_review` in the uncommitted state without an intermediate `in_progress` write, while `progress/current.md` correctly said `in_progress` throughout. `init.sh` passes either way and no information was lost. Mentioned only because the backlog file is the one artefact this repository has already lost work in.

## Benchmark note — how #8 compares on the cheapest feature #7 built

**#7: one implementer pass, approved first time, ≈9–10 min of captured writing, ≈20–25 min of review, one disclosed non-blocking finding.** One of the two or three cheapest features in either build.

**#8: one implementer pass, approved first time, ≈22 min of captured writing (17:18 → 17:40), plus the coordinator's `quality.sh` run, plus ≈35 min of review.** Roughly **2× #7's implementation figure and ~1.5× its review figure** — and both figures are honest only if the inheritance is stated with them, because **this feature had the most complete inheritance of any feature in this build**: #7's committed answer to the one real judgement (the nine codes), its test list, its arming evidence, its disclosed deferral, its disclosed divergence, *and* a live-bug reproduction, all available before the first file was opened. There was nothing left to decide.

**So the extra time is not design time — it is the same finding feature 16 produced, at one-twentieth the scale: the proof costs more here.** #7's `it.each` over twelve codes and its Drizzle store method are the same lines as #8's `[Theory]` and `ExecuteUpdateAsync`; what #8 additionally paid for was a real MS-SQL Testcontainers test to prove the sweeper exclusion (#7 read its query and asserted it structurally), three forced `--no-incremental` rebuild cycles the arming protocol demands and TypeScript does not, and a compile-time constraint (`false` is not a legal `when` filter under `TreatWarningsAsErrors`) that made the dispatcher's own arming a two-part edit instead of one. **None of that is waste, and the review's own probe 3 is what it bought:** an assertion #7's counterpart review did not have to reach, because #7 never wrote a sweeper-exclusion test to leave unarmed.

**The honest summary: a feature where reuse worked exactly as intended — zero design cost, zero rounds, zero blocking defects — and still ran about twice #7's clock, entirely inside the evidence.** That is the same conclusion feature 16 reached at 3× on a 8.7-hour feature; seeing it hold at 2× on a 22-minute one, with a *fully* settled design, is the more useful datum of the two, because at this scale there is nothing else it could be measuring.

## What would have to change before re-review

Nothing. Approved as it stands. A1 and A2 are recommended follow-ups for the leader — a docs pass and a backlog entry — neither of which is this feature's code.

## `./init.sh` after this review's two writes — exit 1, and correctly so

Re-run **after** setting id 42 `done` and appending the effort record: **exit 1**, with every backlog check green (44 features, none `in_progress`, SDD coherence satisfied, no superseded rule text) and a **single** `FAIL`:

```
[FAIL]  progress/current.md claims a feature while none is active: "**Feature:** `orders_saga_terminal_rejection_classification` (id 42, phase 8)"
```

That is a **true** statement about the working tree at the instant a feature closes, and it is the same state the previous review recorded and left (`progress/review_order_saga_orchestrator.md` §C1/§4.11). `progress/current.md` is the **leader's** file; this reviewer's bookkeeping mandate is the `feature_list.json` status and the `progress/history.md` entry, both done. It clears when the leader resets the session file at close. Recording the exit code as it actually was, not the one that would look tidier.

**Before the review's own writes**, the coordinator's run of `./init.sh` was exit 0 — that is the run the C1 box above is ticked against.
