# impl_notifications_service — feature 23, phase 11

`sdd: false`. Spec of record: `feature_list.json` id 23's three acceptance bullets, `specs/shared/domain-model.md` §6/§7.3, and R17/R18 (`specs/shared/requirements.md`). No new `R<n>` ids invented — local ids `NS1`–`NS8` used instead, defined and traced below, mirroring #7's own convention for this feature exactly. `specs/shared/test-matrix.md` is **left untouched**: R17/R18's row is already `DONE`, owned by feature `outbox_and_idempotency` (feature 14) and cited against Orders' own `IdempotentConsumerTests`, not against this feature. This feature copies the canonical pattern; it does not re-prove the shared requirement under a different id, and the matrix's own rule 4 ("renaming a test means editing its row") does not apply here since no cited test was touched. #7's own round-1 implementer made the identical call for the identical reason (`impl_notifications_service.md:3`, order-to-cash-nestjs) — inherited, not rediscovered.

## The durable-ledger inheritance — stated explicitly, because it is otherwise invisible

#7's round 1 shipped an in-memory dedup ledger, was rejected (three emails for one `eventId` against a real broker), and needed a whole second implementation round to add a fourth MySQL database, `otc_notifications`, mid-feature. **None of that was needed here.** Phase 6 (`db_notifications`) already provisioned `otc_notifications` with a `processed_events` table and `ProcessedEventConfiguration.cs`, byte-identical in shape to Orders'/Fulfillment's/Billing's own copies. Checked first, per the brief's instruction: `docker exec otcnet-mssql ... -Q "SELECT name FROM sys.tables;"` against the live compose database returned exactly `__EFMigrationsHistory` and `processed_events` — no migration needed, none created. `specs/outbox_and_idempotency/design.md` §6.3 had already flagged, at design time, that "Notifications (feature 23) has a database but nothing transactional to bind to... must choose between recording before the send and recording after it" — the open question was named in advance rather than discovered by a live rate-limit storm. This is a reuse dividend that shows up as an absence (no N1/N2/N3-shaped defect to fix, no round 2) and is otherwise invisible in the numbers, so it is recorded here as the brief asked.

The `AutoOffsetReset.Latest` decision (below) is the same shape of dividend: #7 arrived at "do not replay full history" as a **post-hoc fix** after the fact (N3, round 2); here it is the only value this consumer's `KafkaFactStreamSubscriber` copy has ever used, decided at design time from Orders' own `Earliest` precedent and the reasoning for why Notifications' case is the opposite.

## What was built

- **`INotificationSender`/`NotificationMessage`** (`Application/Ports`) — the port, with two adapters: `ConsoleNotificationSender` (no I/O, bound by every automated test) and `MailKitNotificationSender` (real SMTP via `MailKit.Net.Smtp.SmtpClient`, bound only by `Program.cs`, against Mailpit locally). A `Func<ISmtpTransport>` seam (`Infrastructure/Notification/ISmtpTransport.cs`) keeps the MailKit adapter unit-testable without a socket — MailKit's own `IMailTransport` is a ~20-member interface, and this narrows it to the three calls the adapter actually makes (mirrors this repository's existing narrow-seam convention, e.g. `IIdempotentSagaRunner` over `IdempotentConsumer`).
- **Seven templates** (`Application/Templates/*Template.cs`) — pure functions, `Envelope<TPayload> → NotificationMessage`, matching `domain-model.md` §7.3's exact seven facts. `NotificationFormat.cs` carries `FormatMoney` (integer-only division/modulo, never a float or `decimal`), `RecipientFor`, `SubjectWithCorrelationId`, `EscapeHtml`.
  - **Deliberate layering correction against #7's own file location.** #7 put its templates under `infrastructure/templates/`. Doing that here would have made `Application/Commands` depend on `Infrastructure/`, which CLAUDE.md's dependency direction forbids (Application must not depend on Infrastructure). Templates are pure, so they belong in `Application/Templates/` — moved on purpose, not copied blind.
- **`NotificationDispatchService`** (`Application/`) — the one dispatch unit every handler delegates to; composes `INotificationIdempotency` (a thin seam over the canonical `IdempotentConsumer`, exactly Orders' `IIdempotentSagaRunner` shape) and `INotificationSender`. Insert-first (the canonical's dedup INSERT, with a no-op `work`), then send, then delete-on-throw if the send fails — the R17 divergence `outbox_and_idempotency/design.md` §6.3 flagged, argued in the class's own doc comment.
- **Seven commands + handlers** (`Application/Commands/`) — `NotifyOrderPlacedCommand`/Handler etc., the explicit-command shape the dispatcher ruling requires, mirroring Orders' `HandleXFactCommandHandler` pattern one-for-one.
- **The canonical copies** (`Infrastructure/Messaging/IdempotentConsumer.cs`, `ProcessedEventLedger.cs`) — byte-identical to `src/Orders/Infrastructure/Messaging/*.cs` outside the banner and the `namespace` line, per `outbox_and_idempotency/design.md` §6.4's OI12 parity guard. `NotificationIdempotency.cs` composes the canonical with `ConsumerName.Notifications` fixed and adds the compensating `DeleteRecordAsync` — new code, not part of the compared pair.
- **The Kafka consumer** (`Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs`, `NotificationFactTopics.cs`; `Presentation/NotificationFactsConsumer.cs`) — copies Orders' `KafkaFactStreamSubscriber`/`SagaFactsConsumer` shape (offset-commit-after-handler, one `BackgroundService`), with two deliberate differences: `GroupId`/`ClientId` ("notifications"/"otc-notifications"), and `AutoOffsetReset.Latest` instead of Orders' `Earliest` — argued in the class's own remarks (below).
- **DI wiring** (`Infrastructure/NotificationsServiceCollectionExtensions.cs`, `NotificationsOptions.cs`, `NotificationsHost.cs`, `Program.cs`) — the `AddFulfillment`/`AddBilling` shape, `ValidateOnBuild`/`ValidateScopes` forced on, `AddDispatcher` last.

## Why `AutoOffsetReset.Latest`, not `Earliest` — the ported-idiom ledger entry this feature owns

**#7 relied on** a post-hoc, round-2 fix (`main.ts`'s `fromBeginning: false`) to stop a fresh consumer group from mailing a historical backlog — arrived at only after the durable ledger was added and the reviewer asked the question directly. **In #8 that property is supplied by** a design-time decision: `KafkaFactStreamSubscriber.BuildConsumerConfig` sets `AutoOffsetReset.Latest` unconditionally, argued in the class's own XML doc from Orders' own precedent (`Earliest`, because the saga orchestrator owns a state machine that must reach a terminal state for pre-existing orders) — Notifications owns no aggregate and no state machine, so a fact this consumer group has never seen is an omission, not an inconsistency, and mailing months of history on a fresh group would be exactly the incident the durable ledger is not designed to prevent (the ledger only stops a *duplicate* of a fact already recorded; it cannot un-see a fact this group never received). No round-1/round-2 split was needed because the design-time argument was available up front — Orders' own two consumer configurations (`Earliest` for the saga, and the reasoning for why Notifications needs the opposite) already existed on disk before this feature started.

## Idempotency — NS8, both halves, three levels of proof

1. **Unit** (`NotificationDispatchServiceTests.cs`, fakes only): first delivery records+sends; a redelivered `eventId` sends **no second email** and `buildMessage` is **never invoked** (the absence half, proven via a call counter, not merely "not observed" — see arming below); a distinct `eventId` is never treated as a duplicate.
2. **N6/N13** (same file): a failed send deletes the just-committed ledger row and rethrows the *original* error even when the compensating delete *also* throws — the compensation must never mask the real cause.
3. **Integration** (`NotificationConsumptionTests.cs`, real Kafka + real MS-SQL): a real fact is consumed exactly once with a ledger row; a real in-process redelivery sends no second email; a real cross-restart redelivery (fresh `IHost`, same durable MS-SQL, same consumer group) is **not** re-sent by the fresh host — the exact scenario #7's round-1 in-memory store failed on, proven here without ever having shipped the defect; a real send failure deletes the ledger row and a real Kafka-driven redelivery (no test-side republish) recovers and sends exactly once.

## A genuine harness gap found and fixed — OI12 did not survive its own first real second copy

`outbox_and_idempotency/design.md` §6.4 requires every consuming write model's copy of `IdempotentConsumer.cs`/`ProcessedEventLedger.cs` to be **byte-identical** to Orders' canonical outside two normalised regions: the leading banner, and the `namespace` line. Neither `fulfillment_stock` nor `billing_credit` ever copies this pattern (their own `requirements.md` says so explicitly — they don't consume facts). **Notifications is the first feature in this repository to add a genuine second copy**, and the guard — `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs` — failed on it immediately:

```
Notifications's ProcessedEventLedger.cs (at src/Notifications/Infrastructure/Messaging/ProcessedEventLedger.cs)
diverges from the canonical src/Orders/Infrastructure/Messaging/ProcessedEventLedger.cs outside the banner
and the namespace line.
```

**Root cause.** The canonical file's `using OrderToCash.Orders.Application.Ports;` line names Orders by construction — every legitimate copy must spell its own service's namespace there instead (`OrderToCash.Notifications.Application.Ports;`), and design.md §6.4's own words say this is judged "by suffix rather than by literal text." That suffix-matching was implemented for the guard's *adoptability* check (`AssertAdoptable`, which judges the canonical file alone) but was never carried into the *byte-identity* comparison (`NormalizeCanonical`), because nothing had ever exercised that comparison with two different services before. This is the exact "ported-idiom" shape CLAUDE.md's own ledger names — #7 got this property for free from TypeScript's relative import paths (`../../application/ports/x`, textually identical regardless of which service's file it sits in); C#'s namespace-qualified `using` has no equivalent, and nobody had translated that difference into the guard until a real second copy forced the question.

**Why this was fixed rather than reported and left red.** `tests/Orders.UnitTests/` is outside this feature's granted scope (`src/Notifications/`, `tests/Notifications.*`), and the brief's own "Not yours" section models the correct default for out-of-scope discoveries: stop and report. But CLAUDE.md's own non-negotiable is sharper here — *"A red test anywhere in your suite is inside your scope"* — and this red test's cause is squarely mine: my own canonical copy is what makes it fire for the first time, and there is no way to satisfy R17/R18 while following "the shapes the other three services established... do not invent a fourth pattern" without creating exactly this second copy. Leaving it red would have meant shipping a broken `./quality.sh`; inventing a fourth, non-canonical pattern to dodge the guard would have contradicted the brief directly. The fix (`ExtractNamespaceService`/`NormalizeOwnServiceUsingLine` in `IdempotentConsumerParityTests.cs`) is narrow: it neutralises **only** a `using` line whose namespace is `OrderToCash.<the file's own declared service>.Application.Ports` (or `.Infrastructure.Persistence.Entities`) — a copy that imports the *wrong* service's ports still fails, proven below. `AssertAdoptable` (the canonical-only check) was not touched.

**Arming, both mutation families, on the fix itself:**

| Probe | Mutation | Result | Restored |
|---|---|---|---|
| Body divergence | `ProcessedEventLedger.cs`'s `DuplicateKeyConstraint` changed `2627 → 2628` | `FAIL`: *"Notifications's ProcessedEventLedger.cs ... diverges from the canonical ... outside the banner and the namespace line."* | `cmp` confirmed identical; forced rebuild; green |
| Wrong-service using line (proves the fix is narrow, not "any suffix accepted") | `IdempotentConsumer.cs`'s `using OrderToCash.Notifications.Application.Ports;` → `using OrderToCash.Fulfillment.Application.Ports;` | `FAIL`: *"Notifications's IdempotentConsumer.cs ... diverges from the canonical ... outside the banner and the namespace line."* | `cmp` confirmed identical; forced rebuild; green |

All four `IdempotentConsumerParityTests` cases pass after the fix (`4/4`); the full `Orders.UnitTests` suite (`280/280`) and the whole solution (below) are unaffected.

## Live Mailpit verification — one send per notified fact, observed through the real API

Real compose stack (`otcnet-kafka`, `otcnet-mssql`, `otcnet-mailpit`, all `healthy`). Baseline confirmed empty first: `otc_notifications.processed_events` had `0` rows, Mailpit's `/api/v1/messages` reported `"total":0`.

Started the **real** `src/Notifications` host (`dotnet run --project src/Notifications`, unmodified `Program.cs`, `SenderKind = Smtp`) against the live broker/database/Mailpit. Confirmed the `notifications` consumer group held all 18 topic-partitions (3 topics × 6 partitions) via `kafka-consumer-groups.sh --describe`. Published all seven notified facts plus one `stock.reserved.v1` control, once each, with a throwaway producer (`/tmp/live-verify`, deleted afterward) using the real `Envelope<TPayload>`/`JsonWire` types from `src/Contracts`.

**Result — `GET http://localhost:8025/api/v1/messages` → `"total": 7"`.** All seven, and only seven:

| Fact | `MessageID` | `To` | Subject |
|---|---|---|---|
| `order.placed.v1` | `5895ebce-c4fd-45ed-85eb-ac1759d2ab74@order-to-cash` | `carrefoures@retailer.order-to-cash.example` | `[order-to-cash] Order ORD-LIVE-1788705257 placed (correlationId: 620a94f5-01ed-42cc-a7ff-3c2d8699be42)` |
| `order.confirmed.v1` | `875d5200-4f4d-43b4-bc15-8ebac296226e@order-to-cash` | `carrefoures@retailer.order-to-cash.example` | `... confirmed ...` |
| `order.despatched.v1` | `c8998110-5ac9-441f-8213-edbae756226a@order-to-cash` | `carrefoures@retailer.order-to-cash.example` | `... despatched (DES-LIVE-1788705257) ...` |
| `invoice.issued.v1` | `370d618c-6916-47f8-8327-f627f2edb3eb@order-to-cash` | `carrefoures@retailer.order-to-cash.example` | `... Invoice INV-LIVE-1788705257 issued ...` |
| `payment.received.v1` | `9ca4957e-f3c6-4996-be4d-f0249060b924@order-to-cash` | `ord-live-1788705257@retailer.order-to-cash.example` | `... Payment received for invoice INV-LIVE-1788705257 ...` |
| `order.completed.v1` | `62918b92-deb1-46a7-b8e6-d756c8040060@order-to-cash` | `carrefoures@retailer.order-to-cash.example` | `... completed ...` |
| `order.cancelled.v1` | `979d7f9e-f5a3-41dd-bac2-e3bee05f52af@order-to-cash` | `carrefoures@retailer.order-to-cash.example` | `... cancelled ...` |

Every `MessageID` matches `<eventId>@order-to-cash` exactly. `payment.received.v1`'s recipient is correctly derived from `orderReference`, not `retailerCode` — the payload carries none. `GET /api/v1/message/{id}` on the `invoice.issued.v1` message confirmed real, rendered HTML (`<p>Invoice <strong>INV-LIVE-1788705257</strong> has been issued...`) and text bodies, not a stub. `otc_notifications.processed_events` held exactly `7` rows, matching.

**The suppression control.** `stock.reserved.v1` was published to the same run and produced **no** email: Mailpit's total stayed at `7`, not `8`, and the EF Core log showed exactly seven `INSERT INTO [processed_events]` statements, not eight.

**Live idempotency (NS8, against the real stack, not a test double).** The identical `order.placed.v1` envelope (same `eventId`, same bytes) was republished. The EF Core log showed the expected `DbUpdateException` (`Error Number:2601`) — the real unique-index violation, caught internally by `ProcessedEventLedger.TryInsertAsync` exactly as designed, not an unhandled crash (`ps aux` confirmed the host process was still running afterward). Both counts stayed unchanged: Mailpit `total: 7`, ledger `row_count: 7`.

The live host was stopped and the throwaway publisher project deleted afterward; `git status` on `src/Notifications/**` shows no stray files.

## Arming — every branch that sends or suppresses, both mutation families

Per CLAUDE.md's arming protocol exactly: introduce the violation, run the named test, record the verbatim failure, restore from a backup copy (never `git checkout --`, most of these files are untracked), confirm restoration with `cmp`, force a rebuild (`touch` + `dotnet build --no-incremental`), confirm the re-run is green.

| # | Branch | Mutation (family) | Named test(s) | Verbatim failure | Restore proof |
|---|---|---|---|---|---|
| 1 | All 7 `NotifyXCommandHandler`s' dispatch call | Deletion — all 7 handlers' `dispatch.DispatchAsync(...)` replaced with a no-op `Task.FromResult(...)` | `NotifyFactCommandHandlersTests` (all 7 cases) | `Assert.Single() Failure: The collection was empty` (7/7 tests) | `cmp` identical; `dotnet build --no-incremental` green; 7/7 green |
| 2 | `OrderPlacedTemplate.Build`'s subject | Corruption (field mutation, not deletion) — `payload.OrderReference` swapped for `payload.RetailerCode` in the subject line | `OrderPlacedTemplateTests`, `NS1_...` | `Assert.Equal() Failure: Strings differ` / `Assert.Contains() Failure: Sub-string not found` (both, real content mismatch not a collapse to empty) | `cmp` identical; forced rebuild; both green |
| 3 | `NotificationDispatchService`'s duplicate-suppression branch | Deletion — the `if (outcome == Duplicate) return outcome;` block removed | `DispatchAsync_RedeliveredEventId_SendsNoSecondEmailAndNeverBuildsTheMessage` | `Assert.Equal() Failure: Values differ — Expected: 1 / Actual: 2` (`buildMessage` ran twice) | `cmp` identical; forced rebuild; green |
| 4 | N6 — the compensating delete on a failed send | Deletion — `await idempotency.DeleteRecordAsync(...)` removed from the `catch` block | `DispatchAsync_WhenSendThrows_DeletesTheJustRecordedRowAndRethrows` | `Assert.Equal() Failure: Collections differ — Expected: [<eventId>] / Actual: []` | `cmp` identical; forced rebuild; green |
| 5 | N13 — never let a compensation failure mask the original error | Corruption — the compensation `catch` block made to `throw new InvalidOperationException(compensationError.Message)` instead of falling through to the outer `throw;` | `DispatchAsync_WhenBothSendAndCompensationThrow_RethrowsTheOriginalSendError` | `Assert.Equal() Failure: Strings differ — Expected: "SMTP down" / Actual: "database unreachable"` | `cmp` identical; forced rebuild; green |
| 6 | `NotificationFactsConsumer`'s exclusion filter (`stock.*`/`credit.*`/`order.saga_failed.v1`) | Deletion — the `if (!_notifiedFactTypes.Contains(...)) return;` guard replaced with a condition that never matches | `AStockReservedFact_IsAcknowledgedButNeverDispatched` (real Kafka + real MS-SQL) | `Assert.Equal() Failure: Values differ — Expected: 1 / Actual: 0` — the now-unfiltered `stock.reserved.v1` hits the routing switch's default arm, throws, and (same fixed test partition key) blocks the control fact behind it forever | `cmp` identical; forced rebuild; full `12/12` integration suite reconfirmed green, twice |
| 7 | OI12's byte-identity comparison (harness gap, see above) | Deletion (body) + corruption (wrong-service `using`) | `HoldsEveryWriteModelsCopyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine` | See the OI12 table above | `cmp` identical both times; forced rebuild; `4/4` green, `280/280` `Orders.UnitTests` green |

Every fact-emitting branch (the seven sends) and every fact-suppressing branch (the exclusion filter) has now been seen to fail on its own deletion, and at least one branch has been seen to fail on payload corruption, satisfying both mutation families CLAUDE.md requires.

## NS-trace (local ids)

| id | Claim | Test(s) |
|---|---|---|
| NS1 | `order.placed.v1` template + handler | `OrderPlacedTemplateTests`, `NotifyFactCommandHandlersTests.NS1_...` |
| NS2 | `order.confirmed.v1` | `OrderConfirmedTemplateTests`, `NS2_...` |
| NS3 | `order.despatched.v1` | `OrderDespatchedTemplateTests`, `NS3_...` |
| NS4 | `invoice.issued.v1` | `InvoiceIssuedTemplateTests`, `NS4_...` |
| NS5 | `payment.received.v1` (recipient synthesised from `orderReference` — no `retailerCode` in the payload) | `PaymentReceivedTemplateTests` (incl. the HTML-escaping/XSS-shaped case for `paymentReference`), `NS5_...` |
| NS6 | `order.completed.v1` | `OrderCompletedTemplateTests`, `NS6_...` |
| NS7 | `order.cancelled.v1` | `OrderCancelledTemplateTests`, `NS7_...` |
| NS8 | Idempotent by `eventId`, both halves, incl. cross-restart durability | `NotificationDispatchServiceTests` (unit) + `NotificationConsumptionTests` (real Kafka + real MS-SQL) — see arming rows 3/4/5/6 above and the live-verification section |
| — | Console adapter reachable through the same port as the real adapter, in every test | `ConsoleNotificationSenderTests`; every integration test binds `INotificationSender` via `Replace(...)`, never `SenderKind = Smtp` |
| — | `stock.*`/`credit.*`/`order.saga_failed.v1` excluded | `AStockReservedFact_IsAcknowledgedButNeverDispatched` (arming row 6) |

## Test counts, read off real runs

- `dotnet test tests/Notifications.UnitTests/Notifications.UnitTests.csproj` → **43/43**, 0 failed, 0 skipped (confirmed on three separate runs, including inside the full-solution run below).
- `dotnet test tests/Notifications.IntegrationTests/Notifications.IntegrationTests.csproj` (real Kafka + real MS-SQL, `apache/kafka:4.3.1` / `mssql/server:2022-CU26-ubuntu-22.04`) → **12/12** (7 pre-existing phase-6 schema tests + 5 new consumption tests), 0 failed — confirmed stable on **four independent runs** (two isolated, one inside the full solution run, one filtered single-test re-run after the OI12/N6/N13/suppression arming passes), durations 1m24s–1m53s.
- `./init.sh` → exit 0, both before and after setting id 23 to `in_review`. Coherence: `1 feature in_progress` before the status flip, `no feature in_progress` after; `progress/current.md is in lockstep with the backlog` both times.
- `./quality.sh` (whole solution — format, build, test, coverage) → **exit 0**. Format: clean. Build: succeeded, 0 warnings, 0 errors. Test: **every project passed**, no failures anywhere:

  | Project | Result |
  |---|---|
  | SharedKernel.UnitTests | 50/50 |
  | Cqrs.UnitTests | 23/23 |
  | Contracts.UnitTests | 21/21 |
  | Fulfillment.UnitTests | 119/119 |
  | **Notifications.UnitTests** | **43/43** |
  | Seed.UnitTests | 34/34 |
  | Billing.UnitTests | 225/225 |
  | Orders.UnitTests | 280/280 |
  | Architecture.Tests | 16/16 |
  | Seed.IntegrationTests | 6/6 |
  | **Notifications.IntegrationTests** | **12/12** (1m53s) |
  | Fulfillment.IntegrationTests | 56/56 (3m31s) |
  | Billing.IntegrationTests | 83/83 (4m38s) |
  | Orders.IntegrationTests | 71/71 (5m19s) |

  Summed directly off this table: **1039 tests, 1039 passed, 0 failed** across the whole solution.

  Coverage (informational only — CLAUDE.md's ≥80%/≥60% gate is not yet enforced anywhere in this repository; `quality.sh`'s own header comment names feature 34/phase 21 as the feature that will enforce it, and this feature does not add that enforcement): the coverage summary's own printed lines include `41.2%` and `33.3%` line coverage, which independent re-runs isolate as exactly `OrderToCash.Notifications.IntegrationTests` (41.20% line / 29.71% branch of `OrderToCash.Notifications.dll`) and `OrderToCash.Notifications.UnitTests` (33.31% line / 21.71% branch) respectively — each figure is coverage of the **whole** `Notifications.dll` from that one test project alone, not a union; the two suites exercise different code (unit tests reach the templates/dispatch/console/MIME-builder/MailKit-transport-via-fake logic, integration tests reach the Kafka consumer and real-database wiring the unit suite cannot). Nothing in `Notifications.dll` is dead: `Program.cs`/`NotificationsHost.cs` (the composition root, matching every other service's own convention of not unit-testing `Program.cs`) and the real `SmtpClient`-touching lines inside `MailKitSmtpTransport` (proved instead by the live Mailpit verification above, deliberately never exercised by an automated test since that would mean sending real mail from CI) account for the untested remainder.

## Files touched

**Created** (`src/Notifications/`): `Application/Ports/{ConsumerName,IClock,IFactStreamSubscriber,INotificationIdempotency,IUnitOfWork,NotificationMessage,UnknownConsumerNameError}.cs`; `Application/Templates/{NotificationFormat,OrderPlaced,OrderConfirmed,OrderDespatched,InvoiceIssued,PaymentReceived,OrderCompleted,OrderCancelled}Template.cs`; `Application/Commands/{NotifyFactCommands,NotifyFactCommandHandlers}.cs`; `Application/NotificationDispatchService.cs`; `Infrastructure/Messaging/{IdempotentConsumer,ProcessedEventLedger,NotificationIdempotency}.cs`; `Infrastructure/Messaging/Consumers/{KafkaFactStreamSubscriber,NotificationFactTopics}.cs`; `Infrastructure/Notification/{ConsoleNotificationSender,ISmtpTransport,MailKitSmtpTransport,MailKitNotificationSender,NotificationMimeMessageBuilder}.cs`; `Infrastructure/Persistence/EfCoreUnitOfWork.cs`; `Infrastructure/SystemClock.cs`; `Infrastructure/{NotificationsOptions,NotificationsServiceCollectionExtensions}.cs`; `Presentation/NotificationFactsConsumer.cs`; `NotificationsHost.cs`; `Program.cs`.

**Deleted**: `Application/README_PLACEHOLDER.cs`, `Presentation/README_PLACEHOLDER.cs` (superseded by real content; `Domain/README_PLACEHOLDER.cs` kept — this service owns no aggregate).

**Modified**: `src/Notifications/Notifications.csproj` (`OutputType Exe`, `Cqrs` project reference, `Confluent.Kafka`/`MailKit`/`Microsoft.Extensions.{Hosting,Options,Logging.Abstractions}` package references — all already pinned in `Directory.Packages.props`, no new versions installed).

**Created** (`tests/Notifications.UnitTests/`, new project, added to `OrderToCash.sln`): 14 test files + `TestSupport/{FakeNotificationIdempotency,FakeNotificationSender}.cs` + the `.csproj`.

**Created/Modified** (`tests/Notifications.IntegrationTests/`): `KafkaContainerFixture.cs`, `NotificationConsumptionTestSupport.cs`, `NotificationConsumptionTests.cs` (new); `.csproj` modified (added `Testcontainers`, `Confluent.Kafka`, `Contracts`/`Cqrs` project references).

**Out-of-scope files touched, with justification (both reported per the "Not yours"/red-test-is-your-scope instructions, neither silent):**
- `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs` — the OI12 harness-gap fix, argued at length above. `apps/orders`'/`src/Orders/` **production** code untouched; only this one test file, and only its `NormalizeCanonical` helper.
- `.env.example` — `NOTIFICATIONS_SMTP_HOST`/`PORT`/`FROM_ADDRESS` (explicitly permitted by the brief: *"`.env.example` if SMTP settings need entries"*) plus a warning comment on the `notifications` consumer-group id, citing the specific `#7` incident it is inherited from. The real `.env` was **not** touched — `Program.cs`'s own fallback defaults (`localhost` / `MAILPIT_SMTP_HOST_PORT` / the fixed `no-reply@...` address) already resolve correctly against the existing `.env`, verified by the live run above.
- `OrderToCash.sln` — `dotnet sln add` for the new `Notifications.UnitTests` project only; `git diff --stat` shows the expected 15-line addition, nothing else.

**Backlog id 57** (the Billing completion-pair causal-edge defect) was never opened, read, or touched — confirmed by `git status --porcelain` showing no path under `src/Billing/**` or `specs/billing_*/**`.

## What I could not do / deviated from, and why

- **Nothing was left undone against the three acceptance bullets.** Bullet 1 ("real email verified in the Mailpit inbox for each notified fact") is verified directly in this session, unlike #7's own round-1/round-2 split where an agent had to carry it to the human — Mailpit's own HTTP API is queryable from this environment, so no human step was required to close it.
- **Coverage is reported, not gated**, matching the established, repository-wide convention (`quality.sh`'s own header comment, feature 34 not yet landed) — this feature does not add a gate no other feature has, and does not claim one either.

## How to test manually

1. `./init.sh` — expect exit 0, `1 feature in_progress: notifications_service` (or `in_review` after this report).
2. `./quality.sh` — expect exit 0, every project green (table above).
3. Live: `docker compose -f docker-compose.infra.yml up -d` (if not already up), then `dotnet run --project src/Notifications`, publish a fact to `otc.orders.facts.v1`/`otc.fulfillment.facts.v1`/`otc.billing.facts.v1` and check `http://localhost:8025` (Mailpit UI) for the arrival, or query `curl http://localhost:8025/api/v1/messages`.

---

# Fix round — `progress/review_notifications_service.md`, two blocking defects + one medium

Verdict was REJECTED with D1 (blocking), D2 (blocking), D3 (medium). All three are guard defects, not behaviour defects — the review's own words: *"Everything the feature claims to do, it does."* This section is scoped to the three findings only. The OI12 parity fix, the dispatch service, the ledger composition, the seven templates' rendering logic, the host wiring and the live behaviour were judged correct and are untouched.

## D1 — the notified-fact filter and routing switch collapsed into one table, and guarded by a test that traverses the real consumer

**Root cause, as the review found it.** `NotificationFactsConsumer` carried two independently-written lists naming the same seven `eventType` strings: a `HashSet<string> _notifiedFactTypes` (consulted only to decide *whether* to dispatch) and a `switch` in a private `DispatchAsync` (deciding *what* to dispatch). Nothing forced them to agree. Deleting five entries from the `HashSet` left both suites green because the seven handler-level guards (`NotifyFactCommandHandlersTests`) construct each handler directly and never traverse the consumer's filter or routing switch at all.

**Fix — derived, not restated.** `src/Notifications/Presentation/NotificationFactsConsumer.cs` now carries exactly one `IReadOnlyDictionary<string, Func<Envelope<JsonElement>, IDispatcher, CancellationToken, Task>> _routes`. `Dictionary.Keys` **is** the notified-fact set — there is no second list to agree with, and no `DispatchAsync` switch survives to drift from it. `HandleMessageAsync` does a single `_routes.TryGetValue(envelope.EventType, out var route)`: a miss is the exclusion path (unchanged behaviour — `stock.*`, `credit.*`, `order.saga_failed.v1`, any future fact), a hit invokes the closure that both deserialises the payload and dispatches the command. This directly answers the review's own suggestion ("consider whether the set should be derived from the handler registrations rather than restated") in the cheapest form available here: the routing table already had to name each command type once; the fix removes the second, independent naming of the same seven strings rather than adding a third list.

**Guard — a new test that goes through the real consumer, not around it.** `tests/Notifications.UnitTests/NotificationFactsConsumerTests.cs` (new file), modelled byte-for-shape on Orders' own `SagaFactsConsumerTests.cs` (same `FakeFactStreamSubscriber`/`RecordingDispatcher`/`CountingScopeFactory` triple, same `[Theory]`/`[InlineData]` structure) — it drives `NotificationFactsConsumer.ExecuteAsync` itself, so the filter and the routing table are both genuinely traversed, not bypassed:

- `EachOfTheSevenNotifiedFacts_ReachesItsOwnCommand` — `[Theory]`, one case per notified fact, asserts the **command type** that lands in the recording dispatcher.
- `AnExcludedFact_ReachesNoDispatchAndOpensNoScope` — `[Theory]`, `stock.reserved.v1` / `credit.approved.v1` / `order.saga_failed.v1`, asserts zero dispatches and zero scopes opened.
- `AMalformedValue_IsAcknowledgedAndDispatchesNothing`, `AnUnknownEventType_IsAcknowledgedAndDispatchesNothing`.

12 new cases (`58` total unit tests, up from `43`).

**Arming, both mutation families, run by me:**

| # | Mutation | Result | Restore |
|---|---|---|---|
| Deletion | `"order.confirmed.v1"` entry removed from `_routes` | `EachOfTheSevenNotifiedFacts_ReachesItsOwnCommand(eventType: "order.confirmed.v1", …)` — `Assert.Single() Failure: The collection was empty` | `cmp` identical to backup; `dotnet build --no-incremental`; re-run green (58/58) |
| Corruption (routing arms swapped) | `["order.completed.v1"]` and `["order.cancelled.v1"]` closures swapped (each still deserialises against its own now-wrong payload type) | Both corresponding theory cases fail: `Assert.Equal() Failure: Values differ — Expected: typeof(NotifyOrderCancelledCommand) / Actual: typeof(NotifyOrderCompletedCommand)` (and the symmetric case) | `cmp` identical; forced rebuild; re-run green (58/58) |

Both mutations restored from a pre-mutation backup copy (`/tmp/.../scratchpad/NotificationFactsConsumer.cs.bak`), confirmed with `cmp`, `touch`ed, rebuilt `--no-incremental`, re-run green before moving on. Never `git checkout --` (the file is untracked).

## D2 — a full mutation sweep of all seven templates' string-typed payload fields; 70/70 now caught

**The sweep, run by me, exactly as specified: for each `payload.<Field>` reference to a string-typed field, on a live (non-comment) line, across the seven `*Template.cs` files, replace that one reference with a literal, rebuild, run the 58 (now 43+15) unit tests, restore, repeat.** Script: `sweep3.py` (kept under the scratchpad, never committed). Command and full unmodified output are reproduced below in full — this is a search result, not a summary of one.

```
$ python3 sweep3.py   # cwd = repo root; runs `dotnet test tests/Notifications.UnitTests/... --no-restore` per mutation
```

70 mutation sites total (field × occurrence, one interpolation slot at a time):

| File | Fields mutated (occurrence count) |
|---|---|
| `OrderPlacedTemplate.cs` | `OrderReference`(3), `RetailerCode`(3), `CompanyCode`(2), `Currency`(1) = 9 |
| `OrderConfirmedTemplate.cs` | `OrderReference`(3), `RetailerCode`(3), `CompanyCode`(2), `Currency`(1) = 9 |
| `OrderDespatchedTemplate.cs` | `OrderReference`(3), `DespatchReference`(3), `RetailerCode`(3), `CompanyCode`(2) = 11 |
| `InvoiceIssuedTemplate.cs` | `InvoiceReference`(3), `OrderReference`(2), `RetailerCode`(3), `CompanyCode`(2), `Currency`(1) = 11 |
| `PaymentReceivedTemplate.cs` | `InvoiceReference`(3), `PaymentReference`(2), `OrderReference`(3), `Currency`(1), `Source`(2) = 11 |
| `OrderCompletedTemplate.cs` | `OrderReference`(3), `RetailerCode`(3), `CompanyCode`(2), `Currency`(1) = 9 |
| `OrderCancelledTemplate.cs` | `OrderReference`(3), `CancellationReason`(2), `RetailerCode`(3), `CompanyCode`(2) = 10 |

**Result: `TOTAL=70 SURVIVED=0 CAUGHT=70 OTHER=0`.** Every one of the 70 is classified below (CAUGHT — no survivor to classify as acceptable-vs-gap, so no residual to justify separately):

<details>
<summary>Full verbatim sweep output (70/70 CAUGHT)</summary>

```
('InvoiceIssuedTemplate.cs', 14, 'Currency', 'CAUGHT', 'var total = FormatMoney(payload.TotalAmount, payload.Currency);')
('InvoiceIssuedTemplate.cs', 16, 'InvoiceReference', 'CAUGHT', 'var subject = SubjectWithCorrelationId($"Invoice {payload.InvoiceReference} issued", envel')
('InvoiceIssuedTemplate.cs', 20, 'InvoiceReference', 'CAUGHT', '$"Invoice {payload.InvoiceReference} has been issued for order {payload.OrderReference}.",')
('InvoiceIssuedTemplate.cs', 20, 'OrderReference', 'CAUGHT', '$"Invoice {payload.InvoiceReference} has been issued for order {payload.OrderReference}.",')
('InvoiceIssuedTemplate.cs', 21, 'RetailerCode', 'CAUGHT', '$"Retailer: {payload.RetailerCode}",')
('InvoiceIssuedTemplate.cs', 22, 'CompanyCode', 'CAUGHT', '$"Company: {payload.CompanyCode}",')
('InvoiceIssuedTemplate.cs', 29, 'InvoiceReference', 'CAUGHT', '$"<p>Invoice <strong>{EscapeHtml(payload.InvoiceReference)}</strong> has been issued for o')
('InvoiceIssuedTemplate.cs', 29, 'OrderReference', 'CAUGHT', '$"<p>Invoice <strong>{EscapeHtml(payload.InvoiceReference)}</strong> has been issued for o')
('InvoiceIssuedTemplate.cs', 31, 'RetailerCode', 'CAUGHT', '$"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",')
('InvoiceIssuedTemplate.cs', 32, 'CompanyCode', 'CAUGHT', '$"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",')
('InvoiceIssuedTemplate.cs', 38, 'RetailerCode', 'CAUGHT', 'return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);')
('OrderCancelledTemplate.cs', 18, 'OrderReference', 'CAUGHT', 'var subject = SubjectWithCorrelationId($"Order {payload.OrderReference} cancelled", envelo')
('OrderCancelledTemplate.cs', 22, 'OrderReference', 'CAUGHT', '$"Order {payload.OrderReference} has been cancelled.",')
('OrderCancelledTemplate.cs', 23, 'CancellationReason', 'CAUGHT', '$"Reason: {payload.CancellationReason}",')
('OrderCancelledTemplate.cs', 24, 'RetailerCode', 'CAUGHT', '$"Retailer: {payload.RetailerCode}",')
('OrderCancelledTemplate.cs', 25, 'CompanyCode', 'CAUGHT', '$"Company: {payload.CompanyCode}",')
('OrderCancelledTemplate.cs', 32, 'OrderReference', 'CAUGHT', '$"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> has been cancelled.</p>",')
('OrderCancelledTemplate.cs', 34, 'CancellationReason', 'CAUGHT', '$"<li>Reason: {EscapeHtml(payload.CancellationReason)}</li>",')
('OrderCancelledTemplate.cs', 35, 'RetailerCode', 'CAUGHT', '$"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",')
('OrderCancelledTemplate.cs', 36, 'CompanyCode', 'CAUGHT', '$"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",')
('OrderCancelledTemplate.cs', 42, 'RetailerCode', 'CAUGHT', 'return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);')
('OrderCompletedTemplate.cs', 14, 'Currency', 'CAUGHT', 'var total = FormatMoney(payload.TotalAmount, payload.Currency);')
('OrderCompletedTemplate.cs', 16, 'OrderReference', 'CAUGHT', 'var subject = SubjectWithCorrelationId($"Order {payload.OrderReference} completed", envelo')
('OrderCompletedTemplate.cs', 20, 'OrderReference', 'CAUGHT', '$"Order {payload.OrderReference} is complete — despatched, invoiced and paid.",')
('OrderCompletedTemplate.cs', 21, 'RetailerCode', 'CAUGHT', '$"Retailer: {payload.RetailerCode}",')
('OrderCompletedTemplate.cs', 22, 'CompanyCode', 'CAUGHT', '$"Company: {payload.CompanyCode}",')
('OrderCompletedTemplate.cs', 29, 'OrderReference', 'CAUGHT', '$"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> is complete — despatched,')
('OrderCompletedTemplate.cs', 31, 'RetailerCode', 'CAUGHT', '$"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",')
('OrderCompletedTemplate.cs', 32, 'CompanyCode', 'CAUGHT', '$"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",')
('OrderCompletedTemplate.cs', 38, 'RetailerCode', 'CAUGHT', 'return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);')
('OrderConfirmedTemplate.cs', 14, 'Currency', 'CAUGHT', 'var total = FormatMoney(payload.TotalAmount, payload.Currency);')
('OrderConfirmedTemplate.cs', 16, 'OrderReference', 'CAUGHT', 'var subject = SubjectWithCorrelationId($"Order {payload.OrderReference} confirmed", envelo')
('OrderConfirmedTemplate.cs', 20, 'OrderReference', 'CAUGHT', '$"Order {payload.OrderReference} has been confirmed — stock reserved and credit approved."')
('OrderConfirmedTemplate.cs', 21, 'RetailerCode', 'CAUGHT', '$"Retailer: {payload.RetailerCode}",')
('OrderConfirmedTemplate.cs', 22, 'CompanyCode', 'CAUGHT', '$"Company: {payload.CompanyCode}",')
('OrderConfirmedTemplate.cs', 29, 'OrderReference', 'CAUGHT', '$"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> has been confirmed — stoc')
('OrderConfirmedTemplate.cs', 31, 'RetailerCode', 'CAUGHT', '$"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",')
('OrderConfirmedTemplate.cs', 32, 'CompanyCode', 'CAUGHT', '$"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",')
('OrderConfirmedTemplate.cs', 38, 'RetailerCode', 'CAUGHT', 'return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);')
('OrderDespatchedTemplate.cs', 17, 'OrderReference', 'CAUGHT', '$"Order {payload.OrderReference} despatched ({payload.DespatchReference})",')
('OrderDespatchedTemplate.cs', 17, 'DespatchReference', 'CAUGHT', '$"Order {payload.OrderReference} despatched ({payload.DespatchReference})",')
('OrderDespatchedTemplate.cs', 22, 'OrderReference', 'CAUGHT', '$"Order {payload.OrderReference} has been despatched.",')
('OrderDespatchedTemplate.cs', 23, 'DespatchReference', 'CAUGHT', '$"Despatch reference: {payload.DespatchReference}",')
('OrderDespatchedTemplate.cs', 25, 'RetailerCode', 'CAUGHT', '$"Retailer: {payload.RetailerCode}",')
('OrderDespatchedTemplate.cs', 26, 'CompanyCode', 'CAUGHT', '$"Company: {payload.CompanyCode}",')
('OrderDespatchedTemplate.cs', 32, 'OrderReference', 'CAUGHT', '$"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> has been despatched.</p>"')
('OrderDespatchedTemplate.cs', 34, 'DespatchReference', 'CAUGHT', '$"<li>Despatch reference: {EscapeHtml(payload.DespatchReference)}</li>",')
('OrderDespatchedTemplate.cs', 36, 'RetailerCode', 'CAUGHT', '$"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",')
('OrderDespatchedTemplate.cs', 37, 'CompanyCode', 'CAUGHT', '$"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",')
('OrderDespatchedTemplate.cs', 42, 'RetailerCode', 'CAUGHT', 'return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);')
('OrderPlacedTemplate.cs', 14, 'Currency', 'CAUGHT', 'var total = FormatMoney(payload.TotalAmount, payload.Currency);')
('OrderPlacedTemplate.cs', 16, 'OrderReference', 'CAUGHT', 'var subject = SubjectWithCorrelationId($"Order {payload.OrderReference} placed", envelope.')
('OrderPlacedTemplate.cs', 20, 'OrderReference', 'CAUGHT', '$"Order {payload.OrderReference} has been placed.",')
('OrderPlacedTemplate.cs', 21, 'RetailerCode', 'CAUGHT', '$"Retailer: {payload.RetailerCode}",')
('OrderPlacedTemplate.cs', 22, 'CompanyCode', 'CAUGHT', '$"Company: {payload.CompanyCode}",')
('OrderPlacedTemplate.cs', 29, 'OrderReference', 'CAUGHT', '$"<p>Order <strong>{EscapeHtml(payload.OrderReference)}</strong> has been placed.</p>",')
('OrderPlacedTemplate.cs', 31, 'RetailerCode', 'CAUGHT', '$"<li>Retailer: {EscapeHtml(payload.RetailerCode)}</li>",')
('OrderPlacedTemplate.cs', 32, 'CompanyCode', 'CAUGHT', '$"<li>Company: {EscapeHtml(payload.CompanyCode)}</li>",')
('OrderPlacedTemplate.cs', 38, 'RetailerCode', 'CAUGHT', 'return new NotificationMessage(RecipientFor(payload.RetailerCode), subject, text, html);')
('PaymentReceivedTemplate.cs', 14, 'Currency', 'CAUGHT', 'var amount = FormatMoney(payload.Amount, payload.Currency);')
('PaymentReceivedTemplate.cs', 17, 'InvoiceReference', 'CAUGHT', '$"Payment received for invoice {payload.InvoiceReference}",')
('PaymentReceivedTemplate.cs', 22, 'PaymentReference', 'CAUGHT', '$"Payment {payload.PaymentReference} has been received for invoice {payload.InvoiceReferen')
('PaymentReceivedTemplate.cs', 22, 'InvoiceReference', 'CAUGHT', '$"Payment {payload.PaymentReference} has been received for invoice {payload.InvoiceReferen')
('PaymentReceivedTemplate.cs', 22, 'OrderReference', 'CAUGHT', '$"Payment {payload.PaymentReference} has been received for invoice {payload.InvoiceReferen')
('PaymentReceivedTemplate.cs', 25, 'Source', 'CAUGHT', '$"Source: {payload.Source}",')
('PaymentReceivedTemplate.cs', 34, 'PaymentReference', 'CAUGHT', '$"<p>Payment <strong>{EscapeHtml(payload.PaymentReference)}</strong> has been received for')
('PaymentReceivedTemplate.cs', 34, 'InvoiceReference', 'CAUGHT', '$"<p>Payment <strong>{EscapeHtml(payload.PaymentReference)}</strong> has been received for')
('PaymentReceivedTemplate.cs', 34, 'OrderReference', 'CAUGHT', '$"<p>Payment <strong>{EscapeHtml(payload.PaymentReference)}</strong> has been received for')
('PaymentReceivedTemplate.cs', 38, 'Source', 'CAUGHT', '$"<li>Source: {EscapeHtml(payload.Source)}</li>",')
('PaymentReceivedTemplate.cs', 44, 'OrderReference', 'CAUGHT', 'return new NotificationMessage(RecipientFor(payload.OrderReference), subject, text, html);')
=== SUMMARY ===
TOTAL=70 SURVIVED=0 CAUGHT=70 OTHER=0
```

</details>

**What changed to get there.** Every one of the seven `*TemplateTests.cs` files gained explicit assertions on: the recipient (`message.To`, previously **absent** from `OrderCompletedTemplateTests` and `OrderCancelledTemplateTests` — the review's named instance), the greeting-line `OrderReference` in **both** `Text` and `Html`, `RetailerCode`/`CompanyCode` ("Retailer: …"/"Company: …") in **both** bodies, and each template's distinguishing field (`DespatchReference` for despatch, `InvoiceReference`/`PaymentReference`/`Source` for the two money/payment templates, `CancellationReason` for cancellation) in **both** bodies. `Currency` needed no new assertion — every template's pre-existing `"1242.50 USD"`/`"124.25 USD"` total assertion already interpolates it, confirmed caught in the sweep without a fix.

**The named survivor, re-armed after the fix, to confirm the exact mutation the review used is now caught:**

```
sed -i 's/RecipientFor(payload.RetailerCode)/RecipientFor(payload.CompanyCode)/' \
  OrderCompletedTemplate.cs OrderCancelledTemplate.cs
```

```
Failed OrderToCash.Notifications.UnitTests.OrderCompletedTemplateTests.Build_ProducesASubjectCarryingTheOrderReferenceAndTheCorrelationId
  Assert.Equal() Failure: Strings differ
Expected: "carrefoures@retailer.order-to-cash.exampl"···
Actual:   "comp01@retailer.order-to-cash.example"

Failed OrderToCash.Notifications.UnitTests.OrderCancelledTemplateTests.Build_ProducesASubjectCarryingTheOrderReferenceAndTheReason
  Assert.Equal() Failure: Strings differ
Expected: "carrefoures@retailer.order-to-cash.exampl"···
Actual:   "comp01@retailer.order-to-cash.example"

Failed!  - Failed:     2, Passed:    56, Skipped:     0, Total:    58
```

Restored from `/tmp/.../scratchpad/{OrderCompletedTemplate,OrderCancelledTemplate}.cs.bak`, `cmp` confirmed identical both, forced rebuild, re-run green (58/58).

**No residual to justify.** CLAUDE.md's instruction was "it does not have to be zero, but the recipient and the fact-identifying references must all be caught, and the record must state the residual" — the residual is zero, stated here rather than omitted.

## D3 — `AutoOffsetReset.Latest` (and the group/client identity, and the store-after-handler contract) now guarded

**New file** `tests/Notifications.UnitTests/KafkaFactStreamSubscriberConfigTests.cs`. `KafkaFactStreamSubscriber.BuildConsumerConfig` is `private static` by design (nothing outside the class needs it) so the guard reaches it via reflection (`BindingFlags.NonPublic | BindingFlags.Static`) — the SAME method the runtime calls, not a re-implementation that could itself drift. Three cases:

- `BuildConsumerConfig_SetsLatestNotEarliest_SoAFreshConsumerGroupDoesNotMailTheHistoricalBacklog` — `Assert.Equal(AutoOffsetReset.Latest, config.AutoOffsetReset)`.
- `BuildConsumerConfig_UsesTheNotificationsConsumerGroupAndClientIdentity` — `GroupId == "notifications"`, `ClientId == "otc-notifications"` (the review's own suggestion to pin these alongside `AutoOffsetReset` while in the file).
- `BuildConsumerConfig_StoresOffsetsOnlyAfterTheHandlerRuns` — `EnableAutoCommit == true`, `EnableAutoOffsetStore == false`.

**Arming — the exact mutation the review performed:**

```
AutoOffsetReset = AutoOffsetReset.Earliest,   // was .Latest
```

```
Failed OrderToCash.Notifications.UnitTests.KafkaFactStreamSubscriberConfigTests.BuildConsumerConfig_SetsLatestNotEarliest_SoAFreshConsumerGroupDoesNotMailTheHistoricalBacklog
  Assert.Equal() Failure: Values differ
Expected: Latest
Actual:   Earliest
```

Restored from `/tmp/.../scratchpad/KafkaFactStreamSubscriber.cs.bak`, `cmp` confirmed byte-identical, `touch`ed, `dotnet build --no-incremental`, re-run green (58/58, then reconfirmed against the full 1054-test solution below).

## Superseded description in the original submission

The original "Arming" table's row 6 and the class remarks it described (`_notifiedFactTypes` HashSet + a `DispatchAsync` switch) no longer exist in the source — replaced by the single `_routes` dictionary above. Left as originally written rather than edited, since it accurately records what round 1 shipped and why the review rejected it; this fix-round section is the current state.

## Verification — figures read off real runs, this round

- `dotnet test tests/Notifications.UnitTests` → **58/58**, 0 failed (was 43; +12 D1 routing cases, +3 D3 config cases).
- `dotnet test tests/Notifications.IntegrationTests` (real Kafka + real MS-SQL) → **12/12**, 1m50s — unaffected by the D1 refactor (no test there touches the private routing internals).
- `./quality.sh` → **exit 0**. Every project green:

  | Project | Result |
  |---|---|
  | Cqrs.UnitTests | 23/23 |
  | SharedKernel.UnitTests | 50/50 |
  | Contracts.UnitTests | 21/21 |
  | **Notifications.UnitTests** | **58/58** |
  | Fulfillment.UnitTests | 119/119 |
  | Billing.UnitTests | 225/225 |
  | Orders.UnitTests | 280/280 |
  | Seed.UnitTests | 34/34 |
  | Architecture.Tests | 16/16 |
  | Seed.IntegrationTests | 6/6 |
  | **Notifications.IntegrationTests** | **12/12** (1m50s) |
  | Fulfillment.IntegrationTests | 56/56 (3m11s) |
  | Billing.IntegrationTests | 83/83 (4m57s) |
  | Orders.IntegrationTests | 71/71 (5m36s) |

  **1054 tests, 1054 passed, 0 failed** (1039 in round 1 + 15 new: 12 in `NotificationFactsConsumerTests` + 3 in `KafkaFactStreamSubscriberConfigTests`). Format check clean, build 0 warnings/0 errors.

- `./init.sh` → exit 0. `1 feature in_progress: notifications_service` before the status flip below; backlog coherence, SDD coherence and the session-file lockstep check all `[OK]`.
- `specs/shared/test-matrix.md` — **left untouched**, confirmed correct again this round: `grep` for every new test name (`NotificationFactsConsumerTests`, `KafkaFactStreamSubscriberConfigTests`, the seven `*TemplateTests`) against the matrix returns nothing, so no row cites them and none needed a Status-column update. R17/R18's row is still owned by feature 14 against Orders' own tests, untouched by this round.
- `feature_list.json` id 23 — the single `"status"` line edited from `"in_progress"` to `"in_review"`; `git diff feature_list.json` shows exactly that one line.

## Scope discipline this round

Touched only: `src/Notifications/Presentation/NotificationFactsConsumer.cs` (D1 refactor), `tests/Notifications.UnitTests/NotificationFactsConsumerTests.cs` (new, D1 guard), `tests/Notifications.UnitTests/KafkaFactStreamSubscriberConfigTests.cs` (new, D3 guard), the seven `tests/Notifications.UnitTests/*TemplateTests.cs` files (D2 assertions), `feature_list.json` (the one status line), this file. `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs` (round 1's accepted OI12 fix) was **not** re-touched — confirmed by re-reading it unchanged and by `Orders.UnitTests` staying at 280/280 with no new failures. No file under `src/Billing/`, `src/Fulfillment/`, `src/Orders/`, or `specs/` was opened or edited this round.
