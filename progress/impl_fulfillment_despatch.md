# Implementation report — `fulfillment_despatch` (feature 18, phase 9)

**Status set at end of this pass:** `in_review` (`feature_list.json`). `sdd: false` — no triple-doc; built from the acceptance list, `specs/shared/saga.md` §3.1 step 3 / §6, `specs/shared/domain-model.md` §4.2–§4.3 (F6/F7/F8), `specs/shared/requirements.md` R36, `specs/shared/asyncapi.yaml` (`despatchCreate`/`despatchCreateReply`, `OrderDespatched`, `DespatchCreateRequestPayload`/`DespatchCreateReplyPayload`), and `specs/fulfillment_stock/design.md` — the shape this feature copies verbatim (lock protocol, outbox recorder, responder pattern, DI wiring, §15's ported-idiom ledger).

## 1. What was built

### Domain (`src/Fulfillment/Domain/`)

- `DespatchAdvice.cs` — the `DespatchAdvice` aggregate root (domain-model.md §4.3) and the `DespatchLineEntry` record. `Create()` is the only constructor: refuses an empty `lines` list (F6, `EmptyDespatchLinesError`) and appends exactly one `order.despatched.v1` fact to its own event collection before returning, so a caller can never observe an aggregate whose fact was not recorded. The fact's `AggregateId` is the `DespatchAdvice`'s own id — unlike `stock.reserved.v1`/`.released.v1`, which pick a `StockItem` as carrier because no despatch exists yet at that point in the saga. No `Reconstitute`: a despatch is created once and never mutated again, so the F8 read path only ever needs `Application.Ports.DespatchSnapshot`, never a live aggregate.
- `Events/OrderDespatched.cs` — reuses the existing `StockDomainEvent` envelope base (feature 17's generic Fulfillment-bounded-context envelope, not stock-specific in shape) and reuses `Contracts.Facts.DespatchLine` directly for `Lines` — the same domain-event-may-reference-Contracts-for-payload-record-types precedent `StockReserved` already sets for `ReservationRef`.
- `OrderDespatch.cs` — the pure order-scoped domain service (`DespatchOrderInput`, `DespatchOutcomeKind`, `DespatchOrderOutcome`, `OrderDespatch.Create`), the sibling `OrderStockReservation` never got in feature 17 (deliberately left for this feature, design.md §16). Consumes every item's `reserved` reservations of the order (`StockItem.Consume`, already landed in feature 17), collects one `DespatchLineEntry` per consumed reservation (F7 — 1:1, never merged, mirroring `stockReservedEvent`'s `reservations[]` shape), and — if anything was consumed — creates exactly one `DespatchAdvice` (F6, F8's creation half). Both `CompanyCode` and `RetailerCode` are read from the **same** first consumed reservation, deliberately — this is prevention of #7's non-blocking review finding N2 (`companyCode`/`retailerCode` sourced asymmetrically), not a reproduction of it. Returns `NoReservations` when nothing was consumed — a defensive, expected-unreachable branch; the real R36 refusal decision is made by the application layer, which can tell "never reserved" apart from "F8 idempotent repeat" from the state of the order's reservations under lock, something the loaded `StockItem`s alone cannot answer.
- `Errors/EmptyDespatchLinesError.cs` — F6, stable code `EMPTY_DESPATCH_LINES`.
- `OrderStockReservation.cs` — **backlog 49 closed**: `Release` now takes the same `Func<UniqueId> newId` seam `Reserve` already had, instead of minting its fact's `EventId` with `UniqueId.New()` directly. See §4.

### Application (`src/Fulfillment/Application/`)

- `Ports/IDespatchRepository.cs` — `DespatchSnapshot` (the flat F8/race read shape) + `IDespatchRepository` (`FindByOrderReferenceAsync` — non-locking, F8 fast path and the in-flight race re-read; `SaveAsync` — drain-on-save, R13).
- `Ports/IDespatchNumberAllocator.cs` — mirrors `IOrderNumberAllocator`.
- `DespatchCreationService.cs` — the plain-class transactional flow (mirrors `StockReservationService`), reusing feature 17's lock protocol unchanged:
  1. F8 fast path: `despatchRepository.FindByOrderReferenceAsync` — a hit returns `created: false` immediately, **no transaction opened**.
  2. `stockRepository.ProductCodesOfOrderAsync(orderReference)` (feature 17's non-locking pre-read, reused verbatim) — empty ⇒ `NoReservedStockForDespatchError`, no transaction.
  3. Inside `unitOfWork.ExecuteAsync`: `stockRepository.LockForOrderAsync` (feature 17's stock-rows-first `FOR UPDATE`, reused verbatim — the SAME lock ordering `stock.release` uses, which is what makes this cannot-deadlock against a concurrent reserve/release/despatch). Then: any `reserved` reservation for the order ⇒ proceed; none, but some `consumed` ⇒ a concurrent committer raced the fast path and already committed — re-read `despatchRepository` (guaranteed current, since we hold the same stock-row lock it held) and return the existing despatch, still `created: false`; none `reserved` and none `consumed` (i.e. all `released`) ⇒ `NoReservedStockForDespatchError`.
  4. Genuine creation: allocate `DES-######`, call `OrderDespatch.Create`, `stockRepository.SaveChangesAsync` (consumes the reservations, drains `StockItem`'s own — always empty — pending events), `despatchRepository.SaveAsync` (inserts `despatches` + `despatch_items`, drains the aggregate's ONE `order.despatched.v1` into the outbox). All inside the one transaction (R13).
- `Application/Commands/CreateDespatchCommandHandler.cs` — thin `ICommandHandler` delegation (mirrors `ReserveStockCommandHandler`).
- `StockApplicationErrors.cs` — added `NoReservedStockForDespatchError` (R36's refusal) and `ConcurrentDespatchChangeError` (the defensive in-flight-race branch, mirrors `ConcurrentReservationChangeError`).

### Infrastructure (`src/Fulfillment/Infrastructure/`)

- `Messaging/Rpc/DespatchRpcPayloads.cs` — `DespatchCreateRequestPayload`/`DespatchCreateReplyPayload`, Fulfillment's own copy (design.md §6.3's "RPC payloads live in the service that speaks them"). `Contracts.Facts.Payloads.OrderDespatchedPayload` and `Contracts.Facts.DespatchLine` already existed (prepared in phase 6/feature 17) — no `src/Contracts` changes were needed.
- `Persistence/EfCoreDespatchRepository.cs` — plain `AsNoTracking` SELECT for `FindByOrderReferenceAsync`; plain INSERT (never upsert — a despatch is created once) for `SaveAsync`, draining the outbox via the **same** `OutboxWriter` `EfCoreStockItemRepository` uses (its `BuildRows` casts to the shared `StockDomainEvent` base, so `OrderDespatched` needed no new writer).
- `Persistence/EfCoreDespatchNumberAllocator.cs` — byte-for-byte the same InnoDB-era-fixed counter-table recipe as `EfCoreOrderNumberAllocator.cs` (numeric `MAX(...)` cast over `despatches.despatch_reference`, the atomic self-initialising `INSERT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK) …)`, then `SELECT … WITH (UPDLOCK, ROWLOCK)` + `UPDATE`) — substituting table/column names only.
- No migration written. Every table this feature writes (`despatches`, `despatch_items`, `despatch_number_sequences`, `outbox`) already existed since phase 6, including the `uq_despatches_order_reference` unique index that gives F8 a DB-level backstop (`src/Fulfillment/Infrastructure/Persistence/Configurations/DespatchConfiguration.cs`, already tested by `tests/Fulfillment.IntegrationTests/UniqueConstraintTests.Despatches_Rejects_A_Duplicate_OrderReference`, unchanged). Checked column-by-column against the shape needed before writing any code; confirmed sufficient.

### Presentation (`src/Fulfillment/Presentation/`)

- `Rpc/StockSubjects.cs` — added `DespatchCreate = "fulfillment.despatch.create"`, the sixth subject.
- `StockRpcResponder.cs` — extended (not replaced) with a sixth subscription loop and `HandleDespatchCreateAsync`, joining the **same** NATS `BackgroundService` rather than a second responder class — "one BackgroundService per transport" (`CLAUDE.md`) is about not multiplexing *different* transports through one class; `despatch.create` is the same NATS transport the five `stock.*` subjects already use, in the same process. FS3 header discipline applies identically: `x-correlation-id`/`x-request-id` are required, refused with `VALIDATION_FAILED` before any dispatch on absence/malformation, matching every other saga command (`despatch.create` is sent by Orders through the same `SagaCommandMeta`-carrying path as `stock.reserve`/`.release`).
- `Rpc/StockRequestValidator.cs` — added `ValidateDespatchCreate` (`orderReference` format only — the request carries nothing else).
- `Rpc/StockErrorMapper.cs` — `NoReservedStockForDespatchError → PRECONDITION_FAILED` (the same slot `ReservationTerminalError` uses one branch above it, for the same reason: the order and its reservations genuinely exist, they are simply not in the state `despatch.create` requires); `ConcurrentDespatchChangeError → UNAVAILABLE` (transient, mirrors `ConcurrentReservationChangeError`). `CONFLICT` remains produced by nothing, confirmed by test.

### Wiring

- `Infrastructure/FulfillmentServiceCollectionExtensions.cs` — `IDespatchRepository`, `IDespatchNumberAllocator`, `DespatchCreationService` registered explicitly (scoped). `CreateDespatchCommandHandler` needs no explicit registration — it is picked up by `AddDispatcher(Assembly.GetExecutingAssembly())`'s assembly scan, confirmed by the existing `FulfillmentDispatcherRegistrationTests` (both cases stayed green with no edits: DI-graph validation and the "exactly one handler per command" check).
- No new `PackageReference`. No `.env.example` change — no new external dependency, the allocator reuses the existing `FulfillmentDb` connection.

### Orders side

**Untouched.** The orchestrator already knew how to issue `despatch.create` (feature 16 / the .NET saga orchestrator) and `SagaCommandRequestFactory`/`SagaCommandPayloads.cs` already built `DespatchCreateRequestPayload { orderReference }`, matching this feature's request shape exactly. No `src/Orders/` file was edited (confirmed: `git status --porcelain` lists no file under `src/Orders/`).

## 2. Backlog id 49 — judged, and closed here

**Judgement: genuinely cheap, closed in this feature.** `OrderStockReservation.Release` minted its fact's `EventId` with `UniqueId.New()` directly while `Reserve` already took a `Func<UniqueId> newId` delegate — the one id design.md §3.3's "no ids beyond those `newId` supplies" promise did not actually keep. Fixing it was a signature change (`Release(items, input, context, newId)`) plus updating every call site:

- `src/Fulfillment/Application/StockReservationService.cs` — `ReleaseAsync` now passes `UniqueId.New` (mirrors `ReserveAsync`'s own `OrderStockReservation.Reserve(..., UniqueId.New)`).
- `tests/Fulfillment.UnitTests/OrderStockReservationTests.cs` — 3 call sites updated, plus a new dedicated test (§4).
- `tests/Fulfillment.IntegrationTests/StockItemRepositoryTests.cs` — 1 call site updated.

**No third pattern was introduced.** `OrderDespatch.Create` and `DespatchAdvice.Create` use the SAME `newId()`-delegate discipline `Reserve` already established — `Consume`'s facts (via `DespatchAdvice`) never call `UniqueId.New()` directly anywhere.

## 3. Ported-idiom ledger — which of the twelve properties apply, and how supplied

Per `CLAUDE.md`'s ledger rule and the brief's instruction, every place this feature's new code locks a row, reads a row to decide something, or writes a fact:

| New code | What it does | Ledger property | How supplied |
|---|---|---|---|
| `DespatchCreationService.CreateAsync` | Reads `despatchRepository.FindByOrderReferenceAsync` (F8 fast path) | Not a decision-bearing lock read — it decides only whether to *open a transaction*, mirroring `stock.release`'s non-locking pre-read (design.md §4.4 step 0). No lock hint needed; the authoritative decision is re-made under lock inside the transaction. | Inherited unchanged from feature 17's design; not a new property, a reused one. |
| `DespatchCreationService.CreateAsync` (inside the transaction) | Takes a lock via `stockRepository.LockForOrderAsync` | **L1** (blocking, current read) and **L2** (deterministic global lock order) | Not newly supplied — this feature calls feature 17's `IStockItemRepository.LockForOrderAsync` verbatim, unmodified. The stock-rows-first, `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` protocol is exactly what makes `despatch.create` cannot-deadlock against a concurrent `stock.reserve`/`.release`, the same reasoning `stock.release` already relies on. |
| `EfCoreDespatchNumberAllocator.AllocateNextAsync` | Takes `WITH (UPDLOCK, HOLDLOCK)` / `WITH (UPDLOCK, ROWLOCK)` on `despatch_number_sequences`' single row | **L5**'s sibling — "insert-or-leave-alone as one atomic statement" | Copied verbatim (table/column names substituted only) from `EfCoreOrderNumberAllocator`, which is itself the **fixed** rendering (feature 45 closed the check-then-act defect that idiom originally shipped with — `IF NOT EXISTS (SELECT …) INSERT` under RCSI, where an un-hinted read takes no lock at all). `DespatchNumberSequence.NextValue`'s own doc comment (written in feature 17/phase 6) already flagged this table as one that must follow the fixed pattern. Never rendered as check-then-act here. |
| `EfCoreDespatchRepository.SaveAsync` | Writes the despatch header + lines | **L5** (for the despatch/despatch_items rows themselves) | **Not needed, and not rendered.** The despatch row is inserted exactly once, under the F8/application-level guarantee that this branch is reached only when no despatch exists for the order (confirmed by the fast path + the in-transaction re-read), backstopped by the pre-existing `uq_despatches_order_reference` DB constraint. A plain `Add` is exact; no `MERGE`, no `IF NOT EXISTS … INSERT` anywhere in this file. |
| `EfCoreDespatchRepository.SaveAsync` | Writes exactly one `order.despatched.v1` fact | **L8** (publication order via per-row awaited insert, not `AddRange`) | Copied verbatim (reasoning and shape) from `EfCoreStockItemRepository.InsertOutboxRowAsync` — one `ExecuteSqlInterpolatedAsync` per outbox row, awaited before the next, never `AddRange` (EF Core's SQL Server provider does not preserve `Add` order when assigning `IDENTITY` values, and `seq` is the entire publication-order guarantee). |
| `OrderStockReservation.Release` (backlog 49) | Mints a fact's `EventId` | The "no ids beyond `newId`" discipline itself (the property L4/L8's neighbours all assume) | Fixed to take the same `newId` delegate `Reserve` already had, closing the one place that discipline was not actually kept. |

No new lock ordering (L2) or collation (L3) property was introduced — `despatch.create` locks the SAME stock rows `stock.reserve`/`.release` already lock, through the SAME repository method, so no new multi-row statement exists to get the order wrong.

## 4. Arming — both mutation families, verbatim

All mutations applied to files in `src/Fulfillment/`, backed up to a scratch copy first (never `git checkout --`, since every file this feature added is untracked), restored byte-exact (`sha256sum` verified against the backup after restore), and force-rebuilt (`dotnet build --no-incremental`, since a same-mtime restore risks MSBuild skipping the recompile) before the confirming green run.

| # | Mutation | File | Deleted/corrupted | Named test(s) killed | Message (verbatim) |
|---|---|---|---|---|---|
| **M1 — emission deleted** | `advice.Raise(fact);` commented out in `DespatchAdvice.Create` | `src/Fulfillment/Domain/DespatchAdvice.cs` | The fact's own emission | `DespatchAdviceTests.Create_CreatesTheAggregateAndEmitsExactlyOneOrderDespatchedV1_WhosePayloadTracesEachLine`; `DespatchCreationServiceTests.HappyPath_ConsumesTheReservationCreatesTheDespatchAndReturnsCreatedTrue`; `OrderDespatchTests.Create_ConsumesEveryReservedReservationOfTheOrderAcrossTwoItems_MovesThemToConsumed_AndCreatesOneDespatchAdviceWithOneFact`; `OrderDespatchTests.Create_TheAdvicesIdAndTheFactsEventId_AreBothMintedByTheNewIdDelegate`; **and**, over the real MsSql+NATS+Kafka host, `DespatchCreateTests.HappyPath_ConsumesTheReservation_CreatesTheDespatchAndDespatchItemRows_AndEmitsExactlyOneOrderDespatchedV1CarryingTheDespatchedFields` | `Assert.Single() Failure: The collection was empty` (all five; the integration case's own message identical, stack trace at the `Assert.Single(factRows)` line) |
| **M2 — payload corrupted** | `l.Units.Value` → `l.Units.Value + 1` on every fact line, inside `DespatchAdvice.Create`'s fact construction (emission and row count untouched) | `src/Fulfillment/Domain/DespatchAdvice.cs` | The fact's `Lines[].units` field | `DespatchAdviceTests.Create_CreatesTheAggregateAndEmitsExactlyOneOrderDespatchedV1_WhosePayloadTracesEachLine`; **and**, over the real host, `DespatchCreateTests.HappyPath_...` | Domain: `Assert.Collection() Failure: Item comparison failure ↓ (pos 0) … Expected: 3 Actual: 4`. Integration: `Assert.Equal() Failure: Values differ Expected: 4 Actual: 5` (at the fact-payload `Units` assertion — the persisted `despatch_items` row, built from the aggregate's own untouched `Lines`, stayed correct, which is itself evidence the mutation reached only the fact) |
| **M3 — suppression deleted (F8 fast path)** | The `if (existing is not null) { return BuildReply(existing, created: false); }` early return commented out in `DespatchCreationService.CreateAsync` | `src/Fulfillment/Application/DespatchCreationService.cs` | The F8 fast-path suppression (an already-created despatch would fall through into the transaction and mis-route) | `DespatchCreationServiceTests.F8_FastPath_ReturnsTheExistingDespatchWithCreatedFalse_OpeningNoTransactionAndAllocatingNoNumber` | `OrderToCash.Fulfillment.Application.NoReservedStockForDespatchError : Order 'ORD-000001' holds no reservation in status 'reserved' — nothing for despatch.create to consume.` (the deleted suppression let the fake's un-set `ProductCodesLookup` reach the precondition throw instead of the expected `created: false` reply) |
| **backlog 49 arming** | `newId()` → `UniqueId.New()` in `OrderStockReservation.Release`'s fact construction | `src/Fulfillment/Domain/OrderStockReservation.cs` | The delegate-sourced `EventId` (the exact property backlog 49 names) | `OrderStockReservationTests.Release_TheFactsEventId_IsTheOneTheNewIdDelegateReturned` | `Assert.Equal() Failure: Values differ Expected: b7ed35e8-0efb-44fe-b627-1732cb1865f2 Actual: 71c48e9f-5910-475d-8859-475913f4e92a` |

Every restore was confirmed identical to its pre-mutation `sha256sum` (`DespatchAdvice.cs`: `0b1185256152f26de72f999809825250efe5510d9b1534c0f71a1212ae01ad28` before, during-restore, and after; `OrderStockReservation.cs`: `9516a4e96ea35ed4536a0d3c99f4caf62224745fd2348bd8212cbd4980e4df11`; `DespatchCreationService.cs`: `e6724480ebd7205cf88eb1bf898755c6c5ab49742cb7394ecaa02182f2fa44da`), and each restore was followed by `dotnet build --no-incremental` and a full green re-run of the affected suite before moving to the next mutation.

M1 and M2 together satisfy "every fact-emitting branch needs both a deletion guard and a payload guard" for this feature's one fact-emitting branch (`order.despatched.v1`). M3 is the suppression-branch companion — the F8 fast path is the one place this feature deliberately does **not** emit a fact for an already-satisfied request, and it is guarded the same way FS5's rejected-defect class taught feature 17 to guard `stock.reserve`'s own short-circuit.

## 5. R/F → test mapping

| Id | Test |
|---|---|
| **R36** (consume, one despatch, one fact; F6/F7/F8) | Domain: `tests/Fulfillment.UnitTests/OrderDespatchTests.cs` (5 cases — happy path across two items, company/retailer sourced from the same reservation [N2 prevention], defensive `NoReservations`, a released item alongside a reserved one, both minted ids distinct); `tests/Fulfillment.UnitTests/DespatchAdviceTests.cs` (2 cases — creation + fact payload, F6 empty-lines refusal). Application (fakes): `tests/Fulfillment.UnitTests/DespatchCreationServiceTests.cs` (5 cases — F8 fast path, never-reserved precondition, all-released precondition, happy path, F8 in-flight race). Integration (real MsSql+NATS+Kafka): `tests/Fulfillment.IntegrationTests/DespatchCreateTests.cs` (5 cases — happy path, F8 idempotent repeat, never-reserved precondition, all-released precondition, FS3 header discipline) |
| **F8** DB-level | `tests/Fulfillment.IntegrationTests/UniqueConstraintTests.Despatches_Rejects_A_Duplicate_OrderReference` — pre-existing since phase 6, unchanged, re-confirmed green |
| Backlog 49 | `tests/Fulfillment.UnitTests/OrderStockReservationTests.Release_TheFactsEventId_IsTheOneTheNewIdDelegateReturned` |
| Presentation (subject, FS3 header discipline, error mapping) | `tests/Fulfillment.UnitTests/StockSubjectsTests.cs` (+1 case, reads `asyncapi.yaml` as text); `tests/Fulfillment.UnitTests/StockResponderHeaderTests.cs` (+2 cases); `tests/Fulfillment.UnitTests/StockRequestValidatorTests.cs` (+2 cases); `tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs` (+2 cases, wire shape); `tests/Fulfillment.UnitTests/StockRpcErrorMapperTests.cs` (+3 cases — PRECONDITION_FAILED, transient inclusion, CONFLICT-from-nothing) |

`specs/shared/test-matrix.md` row **R36** flipped `TODO → DONE` (§4 `fulfillment_stock`); coverage-summary row 4 `Green` 6 → 7, `Not yet green` 1 → 0; grand total `Green` 31 → 32, `Not yet green` 28 → 27.

## 6. Verification (real output)

### Fulfillment suites, standalone

- Unit: **103 / 103** passed (was 80 before this feature's tests were added — 23 new: 5 `OrderDespatchTests`, 2 `DespatchAdviceTests`, 5 `DespatchCreationServiceTests`, 1 `OrderStockReservationTests` backlog-49 arming test, +1 `StockSubjectsTests`, +2 `StockResponderHeaderTests`, +2 `StockRequestValidatorTests`, +2 `StockRpcPayloadTests`, +3 `StockRpcErrorMapperTests`).
- Integration: **53 / 53** passed (5 new in `DespatchCreateTests.cs`; the rest — including the 3 modified call sites in `StockItemRepositoryTests.cs` — unchanged and green).
- `dotnet build OrderToCash.sln --no-incremental`: clean, 0 warnings, 0 errors.
- `tests/Architecture.Tests`: **16 / 16** passed — domain purity holds for every new `Domain/` file (`DespatchAdvice.cs`, `OrderDespatch.cs`, `Events/OrderDespatched.cs`, `Errors/EmptyDespatchLinesError.cs` reference only `OrderToCash.SharedKernel` and, for payload record types, `OrderToCash.Contracts.Facts` — the same allowance `StockReserved`/`StockReleased` already use); no `Domain/` namespace references `OrderToCash.Cqrs`; no `Microsoft.EntityFrameworkCore`/`NATS.*`/`Confluent.Kafka`/`System.Text.Json` in `Domain/`; `decimal` never appears (this service handles no money).

### `./quality.sh` (real run, full monorepo)

Format check clean (`dotnet format --verify-no-changes`), build succeeded (0 warnings, 0 errors), and every test project passed:

| Project | Result |
|---|---|
| SharedKernel.UnitTests | 47/47 |
| Cqrs.UnitTests | 23/23 |
| Contracts.UnitTests | 21/21 |
| Fulfillment.UnitTests | 103/103 |
| Orders.UnitTests | 262/262 |
| Architecture.Tests | 16/16 |
| Notifications.IntegrationTests | 7/7 |
| Seed.UnitTests | 34/34 |
| Seed.IntegrationTests | 6/6 |
| Billing.IntegrationTests | 23/23 |
| Fulfillment.IntegrationTests | 53/53 |
| Orders.IntegrationTests | 70/70 |

Exit code **0**. Coverage is reported (not gated — feature 34 not yet landed) per test-project against everything it references transitively; the Fulfillment-referencing reports read 87.8%–97.2% line coverage, consistent with the domain/application layers this feature added being fully exercised.

### `./init.sh`

Exit code **0** — "environment and state are coherent". 1 feature `in_progress` at the time of the run (`fulfillment_despatch`, correctly — the run was taken before the final status edit below), 36 uncommitted changes (expected mid-session), backlog tripwire clean (no feature lost, no `done` reverted), session file in lockstep.

## 7. Deviations / open points

- **`StockRpcResponder` gained a sixth subject rather than a new responder class.** Judged deliberately: `CLAUDE.md`'s "one `BackgroundService` per transport" rule exists to stop a bare pattern registering on every connected transport (the #7 defect it inherits the lesson from); `despatch.create` is the SAME NATS transport the five `stock.*` subjects already speak, in the same Fulfillment process, so a second responder class would duplicate the concurrency/scope/error-mapping machinery for no isolation benefit. The class's own doc comment was updated to say so explicitly, in case a future reader expects a `DespatchRpcResponder`.
- **`DespatchCreationService`'s "in-flight race, but no despatch found" branch (`ConcurrentDespatchChangeError`) is defensive and, by construction, unreachable in this feature's own tests** — a despatch and its reservations' `consumed` transition commit together in one transaction, so a re-read under the SAME lock the writer held cannot legitimately miss it. It is unit-tested only via the mapper (`StockRpcErrorMapperTests`), not exercised end-to-end; this mirrors `ConcurrentReservationChangeError`'s own status in feature 17.
- No `.env.example` change, no new `PackageReference`, no `src/Contracts` change, no `src/Orders` change — all confirmed by `git status --porcelain` scoped to this session's diff.

## 8. Files touched

New: `src/Fulfillment/Domain/{DespatchAdvice,OrderDespatch}.cs`, `src/Fulfillment/Domain/Events/OrderDespatched.cs`, `src/Fulfillment/Domain/Errors/EmptyDespatchLinesError.cs`; `src/Fulfillment/Application/DespatchCreationService.cs`, `src/Fulfillment/Application/Commands/CreateDespatchCommandHandler.cs`, `src/Fulfillment/Application/Ports/{IDespatchRepository,IDespatchNumberAllocator}.cs`; `src/Fulfillment/Infrastructure/Messaging/Rpc/DespatchRpcPayloads.cs`, `src/Fulfillment/Infrastructure/Persistence/{EfCoreDespatchRepository,EfCoreDespatchNumberAllocator}.cs`; `tests/Fulfillment.UnitTests/{DespatchAdviceTests,OrderDespatchTests,DespatchCreationServiceTests}.cs`; `tests/Fulfillment.IntegrationTests/DespatchCreateTests.cs`.

Edited: `src/Fulfillment/Domain/OrderStockReservation.cs` (backlog 49); `src/Fulfillment/Application/{StockApplicationErrors,StockReservationService}.cs`; `src/Fulfillment/Infrastructure/FulfillmentServiceCollectionExtensions.cs`; `src/Fulfillment/Infrastructure/Outbox/StockFactPayloadMapper.cs`; `src/Fulfillment/Presentation/Rpc/{StockSubjects,StockRequestValidator,StockErrorMapper}.cs`; `src/Fulfillment/Presentation/StockRpcResponder.cs`; `tests/Fulfillment.UnitTests/{Fakes,OrderStockReservationTests,StockRequestValidatorTests,StockResponderHeaderTests,StockRpcErrorMapperTests,StockRpcPayloadTests,StockSubjectsTests}.cs`; `tests/Fulfillment.IntegrationTests/{FulfillmentHostFixture,StockItemRepositoryTests}.cs`; `specs/shared/test-matrix.md` (Status column only); `feature_list.json` (id 18 status → `in_review`, nothing else changed).

---

# Fix round 1 (review REJECTED — 1 blocking defect, 1 required report change, 4 advisories)

`progress/review_fulfillment_despatch.md` reviewed. Backlog id 49 stands as the reviewer closed it (verified independently, armed, `done`) — untouched here. This section addresses D1 (blocking), D2 (required report change) and the four advisories, for feature id 18 only.

## D1 — fixed and armed

`tests/Fulfillment.UnitTests/OrderDespatchTests.cs` — `Create_TheAdvicesIdAndTheFactsEventId_AreBothMintedByTheNewIdDelegate` now queues two **known** ids and asserts each returned value **equals** the specific queued id, in the order `OrderDespatch.Create` calls `newId()` (advice id first, fact `EventId` second) — not merely "two distinct GUIDs, however obtained":

```csharp
var expectedAdviceId = UniqueId.New();
var expectedFactEventId = UniqueId.New();
var minted = new Queue<UniqueId>([expectedAdviceId, expectedFactEventId]);
var input = new DespatchOrderInput(orderReference, UniqueId.New());

var outcome = OrderDespatch.Create([item], input, "DES-000001", ReservationTests.SampleContext(), () => minted.Dequeue());

var advice = outcome.Advice!;
var fact = Assert.Single(advice.DomainEvents);
Assert.Equal(expectedAdviceId, advice.Id);
Assert.Equal(expectedFactEventId, ((OrderDespatched)fact).EventId);
```

**Arming — both probes, run myself, on `src/Fulfillment/Domain/OrderDespatch.cs`.** Backed up first (`sha256sum` before mutating: `5374af017ebe0114295430158751052efc3a09f4044e0e92465fa91f4e54ff95` — the file the reviewer read), restored with `cmp` verified `IDENTICAL` after each probe, `touch` + `dotnet build --no-incremental` before each confirming green run.

| Probe | Mutation | Result | Message (verbatim) |
|---|---|---|---|
| **P2** | line 93 — the fact's `eventId` argument, last `newId()` → `UniqueId.New()` | **FAILED** | `Assert.Equal() Failure: Values differ`<br>`Expected: 0a8d0de4-096a-45ac-8304-1a67ec0caa31`<br>`Actual:   a00b3f87-5517-4960-9dce-4c9c56f23cc5`<br>at `OrderDespatchTests.cs:line 116` |
| **P3** | line 84 — the advice's `id` argument, first `newId()` → `UniqueId.New()` | **FAILED** | `Assert.Equal() Failure: Values differ`<br>`Expected: 2e3e8229-ed5a-48c9-9a45-1733b146d778`<br>`Actual:   d5045f50-42b0-47dd-a5bc-d887cf39505b`<br>at `OrderDespatchTests.cs:line 115` |

Both restores confirmed `IDENTICAL` against the backup (`cmp`), rehashed to the same `5374af01…` after restore, then `dotnet build OrderToCash.sln --no-incremental` (0 warnings, 0 errors) and `dotnet test tests/Fulfillment.UnitTests` — **105/105** (103 baseline + 2 new A1 tests, below).

## What would have caught this when I wrote the test, in my own words

The test's assertions were generated to match its *shape* — "two ids, minted, not reused" — rather than transcribed from its own **name**, which already said the stronger thing: "ARE the delegate's returned values". `Assert.NotEqual(default, x)` and `Assert.NotEqual(a, b)` are the two assertions I'd reach for reflexively to prove "two independently-sourced, non-default ids exist" — but that is a weaker claim than the test's own docstring, and I did not notice the gap between what I wrote and what I titled it. The transferable check is mechanical, not conceptual: **when a test's own name or doc comment contains the word "is" or "are" applied to a specific value ("IS the delegate's returned value"), the assertion must be `Assert.Equal(knownValue, actual)` against a value the test itself supplied — never `Assert.NotEqual`/`Assert.NotDefault` against nothing more than an absence of collision.** `NotEqual` proves non-collision; it can never prove provenance. Reading the assertion back against the test's own title, out loud, before moving on, would have caught this without needing the reviewer's mutation probes at all — the mismatch was visible in the same twenty lines the whole time.

**And this is the same property as backlog id 49, in the feature that closes id 49, written after that lesson was paid for.** Backlog 49 was a production-code gap (`Release` minted its own id instead of taking the delegate); this was a test-code gap one layer up — the delegate discipline was correctly *implemented* in `OrderDespatch.Create`/`DespatchAdvice.Create` (both already took `newId`/`eventId`/`id` as parameters, never called `UniqueId.New()` directly), but the **test written specifically to prove that** did not prove it. Paying for the same lesson twice in one feature, once in code and once in the test that was supposed to guard the code, is the concrete argument for the mechanical check above over a vaguer "be more careful" — a title-to-assertion read is cheap enough to run on every id/provenance test from here on.

## A1 — closed, not filed (extended the id guard to `Reserve`'s two facts)

The reviewer found the same unguarded property in `OrderStockReservation.Reserve`'s two facts (`StockReserved`, `StockRejected`) while arming P1 — a stray `sed` matched all three `EventId: newId(),` sites and only `Release`'s test failed. Cheap to close in the same pass, so I did, in `tests/Fulfillment.UnitTests/OrderStockReservationTests.cs`:

- `Reserve_TheReservedFactsEventId_IsTheOneTheNewIdDelegateReturnedLast` — `Reserve` calls `newId()` once per reservation line before once more for the fact's own `EventId`, so a two-value queue's **last** dequeue is pinned to `StockReserved.EventId`.
- `Reserve_TheRejectedFactsEventId_IsTheOneTheNewIdDelegateReturned` — the rejection branch calls `newId()` exactly once (no reservations are created before the shortage check short-circuits), so a single known value pins the whole call.

Both written with `Assert.Equal(knownValue, actual)` from the start, per the mechanical check above — never `NotEqual`.

**Armed — both probes, on `src/Fulfillment/Domain/OrderStockReservation.cs`.** Backed up first (`sha256sum` = `9516a4e96ea35ed4536a0d3c99f4caf62224745fd2348bd8212cbd4980e4df11`, matching the reviewer's and implementer's recorded hash for this file), restored+`cmp`+rehashed+`touch`+rebuild between each probe.

| Probe | Mutation | Result | Message (verbatim) |
|---|---|---|---|
| Rejected-fact id | line 161 — `EventId: newId()` → `EventId: UniqueId.New()` | **FAILED** | `Assert.Equal() Failure: Values differ`<br>`Expected: b031b88a-7778-4d71-8e60-fe9edcd0d698`<br>`Actual:   87129029-e680-401f-b29c-f0fc739e3c90` |
| Reserved-fact id | line 185 — `EventId: newId()` → `EventId: UniqueId.New()` | **FAILED** | `Assert.Equal() Failure: Values differ`<br>`Expected: 87b2f3c0-1299-4f30-a696-ea2bdba0632f`<br>`Actual:   6ec00108-5f96-4afd-92c0-f9737eb1f141` |

Both restores confirmed `IDENTICAL`/rehashed to the original `9516a4e9…`, full-solution `dotnet build --no-incremental` clean, `dotnet test tests/Fulfillment.UnitTests` **105/105** after restore.

## D2 — the absence claims, redone as command + complete output + classification

**§58 claim — "no third id pattern; `Consume`'s facts never call `UniqueId.New()` directly anywhere":**

```
$ grep -rn "UniqueId\.New(" src/Fulfillment/Domain/ --include=*.cs
src/Fulfillment/Domain/OrderStockReservation.cs:208:    /// fact's <c>EventId</c> with <c>UniqueId.New()</c> directly, the one id
```

Classification: 1 hit, inside a `///` doc comment (backlog-49 narration), not code. Zero calls inside any `Domain/` method body. Claim holds.

**§69 claim — "no `MERGE`, no `IF NOT EXISTS … INSERT` anywhere in this file" (`EfCoreDespatchRepository.cs`):**

```
$ grep -rniE "MERGE |IF NOT EXISTS" src/Fulfillment/Infrastructure/Persistence/EfCoreDespatchRepository.cs
$ echo "exit: $?"
exit: 1
```

Classification: 0 hits (grep exit 1 = no match). Claim holds.

**§32 claim — "checked column-by-column against the shape needed before writing any code; confirmed sufficient" (no migration needed):**

```
$ git status --porcelain -- src/Fulfillment/Infrastructure/Persistence/Migrations/
$ echo "exit: $?"
exit: 0
(no output)

$ find src/Fulfillment -iname "*Migrations*" -type d
src/Fulfillment/Infrastructure/Persistence/Migrations

$ grep -rl "despatches\|despatch_items\|despatch_number_sequences" src/Fulfillment/Infrastructure/Persistence/Migrations/
src/Fulfillment/Infrastructure/Persistence/Migrations/20260901103111_InitialCreate.cs
src/Fulfillment/Infrastructure/Persistence/Migrations/20260901103111_InitialCreate.Designer.cs
src/Fulfillment/Infrastructure/Persistence/Migrations/FulfillmentDbContextModelSnapshot.cs
```

Classification: no migration file is in this feature's change set (`git status` scoped to `Migrations/` is empty); the three tables this feature writes are all defined in `20260901103111_InitialCreate`, a migration dated 2026-09-01, predating this feature's work. This is an enumerable proxy for the process claim ("checked, confirmed sufficient") — the checking itself is not a repository fact, but its outcome (no new migration needed) is, and the enumeration confirms the outcome.

**§48/§139 claim — "No `src/Orders/` file was edited"; "No `.env.example` change, no new `PackageReference`, no `src/Contracts` change" (command named, output not previously given):**

```
$ git status --porcelain
 M feature_list.json
 M progress/current.md
 M progress/history.md
 M specs/shared/test-matrix.md
 M src/Fulfillment/Application/StockApplicationErrors.cs
 M src/Fulfillment/Application/StockReservationService.cs
 M src/Fulfillment/Domain/OrderStockReservation.cs
 M src/Fulfillment/Infrastructure/FulfillmentServiceCollectionExtensions.cs
 M src/Fulfillment/Infrastructure/Outbox/StockFactPayloadMapper.cs
 M src/Fulfillment/Presentation/Rpc/StockErrorMapper.cs
 M src/Fulfillment/Presentation/Rpc/StockRequestValidator.cs
 M src/Fulfillment/Presentation/Rpc/StockSubjects.cs
 M src/Fulfillment/Presentation/StockRpcResponder.cs
 M tests/Fulfillment.IntegrationTests/FulfillmentHostFixture.cs
 M tests/Fulfillment.IntegrationTests/StockItemRepositoryTests.cs
 M tests/Fulfillment.UnitTests/Fakes.cs
 M tests/Fulfillment.UnitTests/OrderStockReservationTests.cs
 M tests/Fulfillment.UnitTests/StockRequestValidatorTests.cs
 M tests/Fulfillment.UnitTests/StockResponderHeaderTests.cs
 M tests/Fulfillment.UnitTests/StockRpcErrorMapperTests.cs
 M tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs
 M tests/Fulfillment.UnitTests/StockSubjectsTests.cs
?? progress/impl_fulfillment_despatch.md
?? progress/review_fulfillment_despatch.md
?? src/Fulfillment/Application/Commands/CreateDespatchCommandHandler.cs
?? src/Fulfillment/Application/DespatchCreationService.cs
?? src/Fulfillment/Application/Ports/IDespatchNumberAllocator.cs
?? src/Fulfillment/Application/Ports/IDespatchRepository.cs
?? src/Fulfillment/Domain/DespatchAdvice.cs
?? src/Fulfillment/Domain/Errors/EmptyDespatchLinesError.cs
?? src/Fulfillment/Domain/Events/OrderDespatched.cs
?? src/Fulfillment/Domain/OrderDespatch.cs
?? src/Fulfillment/Infrastructure/Messaging/Rpc/DespatchRpcPayloads.cs
?? src/Fulfillment/Infrastructure/Persistence/EfCoreDespatchNumberAllocator.cs
?? src/Fulfillment/Infrastructure/Persistence/EfCoreDespatchRepository.cs
?? tests/Fulfillment.IntegrationTests/DespatchCreateTests.cs
?? tests/Fulfillment.UnitTests/DespatchAdviceTests.cs
?? tests/Fulfillment.UnitTests/DespatchCreationServiceTests.cs
?? tests/Fulfillment.UnitTests/OrderDespatchTests.cs
```

(This run is taken **after** this fix round's own edits — `OrderDespatchTests.cs`, `OrderStockReservationTests.cs`, `DespatchCreateTests.cs`, `specs/shared/test-matrix.md` are now also modified/present; `feature_list.json`'s id-18 status line is not yet flipped at the moment this snapshot was taken.)

Classification: **0** lines under `src/Orders/`. **0** under `src/Contracts/`. **0** `.csproj` files (no new `PackageReference`). **0** `.env.example`. **1** line under `specs/` (`specs/shared/test-matrix.md`, the sanctioned Status-column edit). Every other line is under `src/Fulfillment/`, `tests/Fulfillment.*/`, or `progress/`/`feature_list.json` bookkeeping. Claims hold.

## A2 — the ledger gap (un-hinted in-transaction re-read)

Judged: **correct finding, declined to act.** The reviewer's own analysis (review §5, row L1 second face) is already a complete, verified ledger entry in prose — it names the property (`DespatchCreationService.cs:67`'s un-hinted re-read is safe only because it runs after the lock and under RCSI's statement-scoped snapshot), verifies the mechanism (`EfCoreUnitOfWork.cs:30`'s explicit `ReadCommitted` + RCSI), and states the forward risk (#9 on PostgreSQL `REPEATABLE READ` would see a stale snapshot). Copying it into `specs/fulfillment_stock/design.md` §15 verbatim is the right final home, but doing so is a `specs/fulfillment_stock/` edit and this fix round's scope is `tests/Fulfillment.UnitTests/`, `tests/Fulfillment.IntegrationTests/`, `specs/shared/test-matrix.md`, and this report. **Backlog wording, for the leader to file (not filed here — `feature_list.json` touched only for id 18's own status transition per the brief):** *"`specs/fulfillment_stock/design.md` §15's ported-idiom ledger has no row for the un-hinted in-transaction re-read `DespatchCreationService.cs:67` performs (F8 in-flight race decision) — it is safe only because it runs after `LockForOrderAsync` and under RCSI's statement-scoped `ReadCommitted` snapshot (`EfCoreUnitOfWork.cs:30`), and a `REPEATABLE READ` isolation level (e.g. #9 on PostgreSQL) would make the same code return a stale snapshot and turn a correct idempotent repeat into a permanent `UNAVAILABLE`. Add the row; text is in `progress/review_fulfillment_despatch.md` §5, L1 second face, ready to copy."*

## A4 — closed (asserted `despatchDate` on the wire)

`tests/Fulfillment.IntegrationTests/DespatchCreateTests.cs`'s `HappyPath_…` now captures `before`/`after` timestamps bracketing the RPC call and asserts `factPayload.DespatchDate` falls inside that window (`Assert.InRange`, ±1s slack for clock/serialization skew) — the one required wire field sourced from the clock rather than the request or the reservations, previously asserted nowhere. Re-run as part of the full `Fulfillment.IntegrationTests` suite below (54/54 green, including this case).

## A3 — closed (added the named concurrency case, not just relabelled)

`test-matrix.md`'s R36 row named `fulfillment/integration/despatch-create.spec › concurrency against a simultaneous stock.release` and the Status cell previously argued the case was covered by the shared lock protocol rather than probing it. Added `tests/Fulfillment.IntegrationTests/DespatchCreateTests.cs`'s `Concurrency_DespatchCreateRacingASimultaneousStockRelease_ExactlyOneWinsAndEmitsExactlyOneFact`: fires a real `despatch.create` and a real `stock.release` for the same order concurrently (10 iterations, fresh order/product per iteration, mirroring `StockReserveRaceTests`'s pattern), does not assume which side wins, and asserts:

- exactly one of `despatchCreated`/`releaseReleased` is true (XOR) — never both, never neither;
- when despatch wins: the reservation is `consumed`, the release loser is refused `PRECONDITION_FAILED` (re-reading under the SAME lock and finding the reservation already `consumed` — a terminal state, F4/FS10 — which `StockItem.Release` refuses rather than silently no-opping, since the F5 "nothing to release" no-op applies only when a reservation was never held or was already released, never when it was consumed), exactly one `order.despatched.v1` is emitted, and the release side's correlation id has no outbox row at all;
- when release wins: the reservation is `released`, the despatch loser is refused `PRECONDITION_FAILED` (`NoReservedStockForDespatchError`), exactly one `stock.released.v1` is emitted, and the despatch side's correlation id has no outbox row and no despatch row exists.

**This caught a real defect in my own first draft.** My initial version assumed the despatch-wins loser would see `outcome: "already_released"` (F5's success no-op) — but `StockItem.Release` throws `ReservationTerminalError` (→ `PRECONDITION_FAILED`) when it finds a `consumed` reservation, which is a *different* terminal-state guard (F4/FS10) than F5's "nothing was ever reserved" no-op. Running the test caught this immediately (`KeyNotFoundException` on `GetProperty("outcome")` because the actual reply was an error payload with `"code"`, not a success payload with `"outcome"`), and the fix — asserting `"code": "PRECONDITION_FAILED"` on the loser in both directions — is now the correct, verified behaviour. Re-run **5 times independently** after the fix (`dotnet test … --filter "...Concurrency..."`), all green, both race outcomes (despatch-wins and release-wins) observed to occur naturally across the runs and both branches exercised and asserted correctly — this is the test's own two-outcome shape doing the arming: a wrong assertion on either branch was caught live during authoring, not left for a later mutation pass.

`test-matrix.md`'s R36 row's Status cell now cites this test in place of the "not separately probed" prose; the row stays `DONE` (it always was — the gap was in the citation, not in a missing capability) and the coverage-summary counts are unchanged (still one `DONE` row, no reclassification needed).

## A5 — declined

`progress/current.md`'s stale status word is the leader's file per the brief's scope (`tests/Fulfillment.UnitTests/`, `tests/Fulfillment.IntegrationTests/`, `specs/shared/test-matrix.md` Status column, this report) — not touched here, as instructed.

## Final verification

- `dotnet test tests/Fulfillment.UnitTests` — **105/105** (103 baseline + `Reserve_TheReservedFactsEventId_IsTheOneTheNewIdDelegateReturnedLast` + `Reserve_TheRejectedFactsEventId_IsTheOneTheNewIdDelegateReturned`).
- `dotnet test tests/Fulfillment.IntegrationTests` (full suite, real MS-SQL + NATS + Kafka) — **54/54** (53 baseline + `Concurrency_DespatchCreateRacingASimultaneousStockRelease_ExactlyOneWinsAndEmitsExactlyOneFact`), 3m16s.
- `dotnet test tests/Architecture.Tests` — **16/16**.
- `dotnet build OrderToCash.sln --no-incremental` — clean, 0 warnings, 0 errors.
- `./quality.sh` (full run, all twelve test projects): **exit 0**. `dotnet format --verify-no-changes` clean. Every project passed — SharedKernel.UnitTests 47/47, Cqrs.UnitTests 23/23, Contracts.UnitTests 21/21, Fulfillment.UnitTests 105/105, Orders.UnitTests 262/262, Architecture.Tests 16/16, Notifications.IntegrationTests 7/7, Seed.UnitTests 34/34, Seed.IntegrationTests 6/6, Billing.IntegrationTests 23/23, Fulfillment.IntegrationTests 54/54, Orders.IntegrationTests 70/70. Coverage reported (not gated — feature 34 not yet landed).
- `./init.sh` — **exit 0**, "environment and state are coherent"; 0 features `in_progress` (id 18 → `in_review` by this fix round's own edit), backlog tripwire clean, session file in lockstep (pre-existing warning about `progress/current.md`'s status word, A5, declined above), 39 uncommitted changes (expected mid-session).

## Files touched this fix round

`tests/Fulfillment.UnitTests/OrderDespatchTests.cs` (D1), `tests/Fulfillment.UnitTests/OrderStockReservationTests.cs` (A1, +2 tests), `tests/Fulfillment.IntegrationTests/DespatchCreateTests.cs` (A3 +1 test, A4 wire assertion), `specs/shared/test-matrix.md` (R36 Status cell citation only), `progress/impl_fulfillment_despatch.md` (this section), `feature_list.json` (id 18 status `in_progress` → `in_review`, single line, `git diff` read and confirmed to contain nothing else beyond the reviewer's already-committed-to-working-tree id-49 `done` transition from round 1).

**Status set at end of this fix round:** `in_review` (`feature_list.json`, id 18).
