# `billing_remittance_intake` (feature 22, phase 10, `sdd: false`) — implementation record

> No spec triple-doc exists for this feature (`sdd: false`) — the specification of record is `specs/shared/requirements.md` R47–R49 plus `feature_list.json` id 22's three acceptance bullets. This closes the order-to-cash cycle end to end for the first time in this repository, and closes Phase 10.

## What was built

The `billing.payment.register` NATS responder — the sole live caller of `Invoice.MarkPaid` (feature 21) and `BuyerCredit.Release` (feature 19), both delivered uncalled. A plain-class transactional service, `PaymentRegisterService`, follows `InvoiceIssueService`'s exact shape: an R48 fast path outside any transaction, then inside one `IUnitOfWork.ExecuteAsync` — the credit line locked first (`BI8`, extended to this subject), the target invoice's own row locked second, an authority re-read under that lock, `Invoice.MarkPaid` (which raises all three of R49's refusals itself — currency mismatch, amount mismatch, already-paid), `BuyerCredit.Release(reason: invoice_paid)`, then `invoices.MarkPaidAsync` persisted **before** `credits.SaveChangesAsync` so `payment.received.v1`'s outbox row is inserted (and assigned its `seq`) before `credit.released.v1`'s — R47's ordering, made structural by call order rather than incidental.

No new domain code was needed for the happy path: `Invoice.MarkPaid` and `BuyerCredit.Release` were correct and already unit-tested by features 21/19. The `payments` table (with its `payment_reference` UNIQUE constraint, R48's DB-level backstop) already existed in the phase-6 migration — verified via `SchemaColumnTypeTests.cs` before writing any code; **no migration was needed and none was added**.

**N11 fix built in from day one.** The brief named #7's counterpart finding (a sequential cross-invoice reuse of the same `paymentReference` could return a success-shaped `duplicate` naming a *different* invoice than the one the caller asked about, while the concurrent form of the identical condition correctly answered a conflict) and instructed it be inherited as prevention rather than rediscovered. `PaymentRegisterService.IdentityMatches` compares whichever of `invoiceId`/`invoiceReference` the caller supplied against the invoice a `paymentReference` lookup actually resolved, on the fast path, before any lock or transaction — closing the same hole with the SAME `PaymentReferenceConflictError` the concurrent path's `payments.payment_reference` UNIQUE-constraint backstop throws (`EfCoreInvoiceRepository.MarkPaidAsync`, MS-SQL 2601/2627 caught the same way `ProcessedEventLedger`/`EfCoreSagaCommandStore` already do). `PRECONDITION_FAILED`, deliberately never `CONFLICT` — `BillingErrorMapper`'s own class summary already bans `CONFLICT` (`BC27`, because `NatsSagaCommandsAdapter` classifies it as a terminal business rejection for a different, transient reason: a deadlock victim).

**`src/Orders/` needed no change.** `SagaStepTable.cs` and `SagaFactCommands.cs`/`SagaFactCommandHandlers.cs` already wire `payment.received.v1` (precondition `invoiced` → `order.MarkPaid`) and `credit.released.v1` (precondition `paid` → `order.Complete`, emitting `order.completed.v1`) — built ahead of time as part of the saga's foundation in earlier phases, not something feature 22 had to add. Confirmed both by `grep` (zero hits for `PaymentRegister`/`payment.register`/`billing.payment` anywhere under `src/Orders/` or `src/Fulfillment/`) and live, below.

## Files touched

### New

- `src/Billing/Application/Commands/RegisterPaymentCommand.cs` — `RegisterPaymentCommand` + `RegisterPaymentCommandHandler`
- `src/Billing/Application/PaymentRegisterService.cs` — the plain transactional class
- `src/Billing/Domain/PaymentSnapshot.cs` — the read-side projection of a `payments` row (no `Payment` aggregate — a remittance carries no invariant beyond what `Invoice.MarkPaid` already enforces)
- `src/Billing/Infrastructure/Persistence/PaymentRowMapper.cs` — row <-> `PaymentSnapshot`/`MarkPaidInput`
- `src/Billing/Presentation/Rpc/PaymentRegisterRequestValidator.cs` — the cross-field "at least one of `invoiceId`/`invoiceReference`" check, placed the same way `InvoiceRequestValidator`'s `discount` cross-field check is: before any lock, before any transaction
- `tests/Billing.UnitTests/PaymentRegisterServiceTests.cs` — the service's own unit suite, fakes, four armed deletion/suppression probes
- `tests/Billing.UnitTests/PaymentRegisterResponderValidationTests.cs` — the entry-observation theory (`BI2`'s shape, 9 malformed-request cases)
- `tests/Billing.IntegrationTests/PaymentRegisterTests.cs` — Testcontainers, R47/R48/R49, N11, the belt-and-braces backstop, the wire

### Modified

- `src/Billing/Application/Ports/IInvoiceRepository.cs` — extended (not replaced) with `FindByIdAsync`, `FindByInvoiceReferenceAsync`, `FindPaymentByReferenceAsync`, `FindPaymentByInvoiceIdAsync`, `LockByIdAsync`, `MarkPaidAsync`
- `src/Billing/Infrastructure/Persistence/EfCoreInvoiceRepository.cs` — the six new methods; `MarkPaidAsync` is the **one** `UPDATE` this repository ever issues (design.md §6.2's own anticipation), plus the `payments` INSERT wrapped in the SAME MS-SQL 2601/2627 duplicate-key idiom `ProcessedEventLedger`/`EfCoreSagaCommandStore` already establish
- `src/Billing/Infrastructure/Messaging/Rpc/InvoiceRpcPayloads.cs` — `PaymentRegisterRequestPayload`/`PaymentRegisterReplyPayload`, Billing's own transcription from `asyncapi.yaml` (never a reference to another service's payload file, per the established rule)
- `src/Billing/Application/InvoiceApplicationErrors.cs` — two new errors: `InvoiceNotFoundError` (identity resolution miss), `PaymentReferenceConflictError` (N11's fix and the DB-constraint backstop)
- `src/Billing/Presentation/Rpc/BillingErrorMapper.cs` (+ tests) — the two new cases mapped (`NOT_FOUND`, `PRECONDITION_FAILED`); R49's three domain errors were **already** mapped by feature 21, "declared here so the vocabulary is complete on delivery" — confirmed by reuse, nothing new needed for them
- `src/Billing/Presentation/Rpc/InvoiceSubjects.cs` — `PaymentRegister = "billing.payment.register"`, added to the SAME class (not a fourth subjects class): the primary written aggregate is `Invoice`, the one `InvoiceIssue` already answers for
- `src/Billing/Presentation/BillingRpcResponder.cs` (+ subject-coverage test) — `HandlePaymentRegisterAsync`, on the SAME `BackgroundService` (design.md §4.1's "one controller, not three" precedent, extended to a sixth subject rather than forked)
- `src/Billing/Infrastructure/BillingServiceCollectionExtensions.cs` — `PaymentRegisterService` registered `AddScoped`
- `tests/Billing.IntegrationTests/BillingHostFixture.cs` — `PaymentsOfAsync`, `FindPaymentByReferenceAsync`, `PaymentRequest` (guarded by `CentsRuleFixtureGuard`, the same convention `IssueRequest` already uses)
- `tests/Billing.UnitTests/InvoiceIssueServiceTests.cs` — its hand-built `RecordingInvoiceRepository` fake gained six throwing stubs for the port's new methods (mechanical, to keep the file compiling against the extended interface — none of the six is exercised by that file's own tests)
- `tests/Billing.UnitTests/BillingErrorMapperTests.cs`, `InvoiceRpcPayloadTests.cs`, `InvoiceSubjectsTests.cs`, `BillingResponderSubjectCoverageTests.cs` — feature 22's cases added to each existing theory/instrument rather than duplicated in a new file

### Specs

- `specs/shared/test-matrix.md` — R47, R48, R49 rows flipped to `DONE`, naming the real test files and case names; the `billing_invoicing` coverage-summary row updated `2 -> 5` green, `3 -> 0` not-yet-green; the grand total updated `42 -> 45` green, `17 -> 14` not-yet-green (Scoped stays `4`, recomputed from the rows, not hand-adjusted). R48/R49's sketch names an `API` level; the Gateway's `POST /invoices/:id/payments` (features 25/29) does not exist yet, so coverage is honestly recorded one layer down, at the `billing.payment.register` NATS/RPC responder this feature builds — the same substitution #7's own counterpart made and its reviewer accepted, not a new judgement call.

**No migration. No `Contracts` change.** Both were verified unnecessary before writing code, not merely unattempted: the `payments` table and its unique index already exist (`SchemaColumnTypeTests.cs`); `PaymentReceivedPayload`/`CreditReleasedPayload`/`InvoiceAlreadyPaidError` mapping etc. were already delivered, uncalled, by feature 21.

## The ordering property (R47) and its swap mutation

R47's ordering is structural, not asserted: the code never sorts or stamps anything, it makes the order a *consequence of call order* — `invoices.MarkPaidAsync` (drains `payment.received.v1` into the outbox, inside its own method) is `await`ed strictly before `credits.SaveChangesAsync` (drains `credit.released.v1`). MS-SQL's `outbox.seq` is `bigint IDENTITY(1,1)`, and every outbox INSERT in this codebase is a single awaited raw-SQL statement (never `AddRange`, never a batched `SaveChanges` — the `L16` ledger discipline `InvoiceIssueService`/`EfCoreBuyerCreditRepository` already established, exercised here for the first time by a transaction that writes two facts).

**The swap mutation, run for real, at both levels:**

- Unit (`tests/Billing.UnitTests/PaymentRegisterServiceTests.cs`, a shared `callLog` list both repository fakes append to): swapping `credits.SaveChangesAsync` above `invoices.MarkPaidAsync` in the source made `R47_LocksTheCreditLineBeforeTheInvoiceRow_...` fail with *"R47: invoices.MarkPaid must run (and its outbox row insert) BEFORE credits.SaveChanges."*
- Integration (`tests/Billing.IntegrationTests/PaymentRegisterTests.cs`, real MS-SQL): the SAME swap made `R47_RecordsThePayment_..._AndEmitsPaymentReceivedThenCreditReleasedInThatOrder` fail on `orderedFacts[0].EventType`: expected `"payment.received.v1"`, actual `"credit.released.v1"` — the real `seq` order inverted.

Both were restored from a byte-identical backup (`cmp` confirmed empty diff both times) and reconfirmed green after a forced rebuild (`--no-incremental`). See the arming table below for the verbatim messages.

## The timer-absence claim — command, complete output, classification

**Acceptance bullet:** *"no internal payment timer anywhere."* Treated as a search result, not a reading.

**(a) Command run** (from the repo root):

```
grep -rniE "timer|periodictimer|schedule|cron|sweeper|BackgroundService|Task\.Delay|IHostedService" --include=*.cs src/ | grep -v "bin/\|obj/"
```

**(b) Complete output** (105 lines):

```
src/Billing/Infrastructure/BillingServiceCollectionExtensions.cs:75:        services.AddHostedService<OutboxRelayBackgroundService>();
src/Billing/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:1:// COPY OF — src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs
src/Billing/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:14:public sealed class OutboxRelayBackgroundService(
src/Billing/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:17:    ILogger<OutboxRelayBackgroundService> logger) : BackgroundService
src/Billing/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:26:        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollIntervalMs));
src/Billing/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:28:        // PeriodicTimer does not queue missed ticks, so a slow cycle delays
src/Billing/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:31:        while (await timer.WaitForNextTickAsync(stoppingToken))
src/Billing/Infrastructure/Persistence/EfCoreInvoiceRepository.cs:14:/// the sweeper hits on every retry — `AsNoTracking`, no hint, no
src/Billing/Application/InvoiceIssueService.cs:36:        // transaction opened — the sweeper hits this on every retry.
src/Billing/Presentation/BillingRpcResponder.cs:18:/// ONE <see cref="BackgroundService"/>, FIVE subjects — <c>billing.credit.hold</c>,
src/Billing/Presentation/BillingRpcResponder.cs:39:/// is one <see cref="BackgroundService"/> per transport — Billing's RPC
src/Billing/Presentation/BillingRpcResponder.cs:51:    ILogger<BillingRpcResponder> logger) : BackgroundService
src/Billing/Presentation/BillingRpcResponder.cs:137:                TaskScheduler.Default);
src/Billing/Infrastructure/Outbox/OutboxRelay.cs:16:/// The one member <see cref="OutboxRelayBackgroundService"/> depends on —
src/Billing/Infrastructure/Outbox/OutboxRelay.cs:116:                // here — it propagates so the caller (the BackgroundService
src/Fulfillment/Presentation/StockRpcResponder.cs:19:/// ONE <see cref="BackgroundService"/>, six subjects — the five
src/Fulfillment/Presentation/StockRpcResponder.cs:22:/// transport rather than a second responder class — "one BackgroundService
src/Fulfillment/Presentation/StockRpcResponder.cs:45:    ILogger<StockRpcResponder> logger) : BackgroundService
src/Fulfillment/Presentation/StockRpcResponder.cs:132:                TaskScheduler.Default);
src/Fulfillment/Infrastructure/FulfillmentServiceCollectionExtensions.cs:56:        services.AddHostedService<OutboxRelayBackgroundService>();
src/Fulfillment/Infrastructure/Outbox/OutboxRelay.cs:16:/// The one member <see cref="OutboxRelayBackgroundService"/> depends on —
src/Fulfillment/Infrastructure/Outbox/OutboxRelay.cs:116:                // here — it propagates so the caller (the BackgroundService
src/Fulfillment/Infrastructure/Persistence/Configurations/ReservationConfiguration.cs:9:/// `(order_reference, status)` for the sweeper's/operator's "reservations of
src/Fulfillment/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:1:// COPY OF — src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs
src/Fulfillment/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:14:public sealed class OutboxRelayBackgroundService(
src/Fulfillment/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:17:    ILogger<OutboxRelayBackgroundService> logger) : BackgroundService
src/Fulfillment/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:26:        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollIntervalMs));
src/Fulfillment/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:28:        // PeriodicTimer does not queue missed ticks, so a slow cycle delays
src/Fulfillment/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:31:        while (await timer.WaitForNextTickAsync(stoppingToken))
src/Orders/Presentation/OrdersCreateResponder.cs:15:/// The <c>orders.create</c> NATS responder — ONE <see cref="BackgroundService"/>
src/Orders/Presentation/OrdersCreateResponder.cs:16:/// subscribing to ONE transport (CLAUDE.md: "One BackgroundService per
src/Orders/Presentation/OrdersCreateResponder.cs:26:/// singleton <see cref="BackgroundService"/>, so it creates ONE
src/Orders/Presentation/OrdersCreateResponder.cs:42:    ILogger<OrdersCreateResponder> logger) : BackgroundService
src/Orders/Presentation/SagaFactsConsumer.cs:17:/// The ONE Kafka <see cref="BackgroundService"/> in Orders (CLAUDE.md: "one
src/Orders/Presentation/SagaFactsConsumer.cs:18:/// BackgroundService per transport") — subscribes to all three fact topics
src/Orders/Presentation/SagaFactsConsumer.cs:27:    ILogger<SagaFactsConsumer> logger) : BackgroundService
src/Orders/Presentation/SagaFactsConsumer.cs:58:                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
src/Orders/Infrastructure/OrdersOutboxServiceCollectionExtensions.cs:54:        // the SAME scoped instance within one scope — the BackgroundService
src/Orders/Infrastructure/OrdersOutboxServiceCollectionExtensions.cs:67:        services.AddHostedService<OutboxRelayBackgroundService>();
src/Orders/Infrastructure/OrdersSagaOptions.cs:35:/// <summary>The sweeper's schedule and batch (SO5, design.md §6.4) — the durability backstop, structurally identical to <see cref="Outbox.OutboxRelayOptions"/>.</summary>
src/Orders/Infrastructure/OrdersSagaOptions.cs:36:public sealed class OrdersSagaSweeperOptions
src/Orders/Infrastructure/OrdersSagaOptions.cs:58:    public OrdersSagaSweeperOptions Sweeper { get; } = new();
src/Orders/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs:59:        // BackgroundService.ExecuteAsync runs on a thread-pool thread and
src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs:32:        // itself a singleton BackgroundService (every AddHostedService is),
src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs:65:        services.AddScoped<ISagaCommandSweeper, SagaCommandSweeper>();
src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs:67:        // Three BackgroundServices — one per transport/loop (CLAUDE.md).
src/Orders/Infrastructure/OrdersSagaServiceCollectionExtensions.cs:70:        services.AddHostedService<SagaCommandSweeperBackgroundService>();
src/Orders/Infrastructure/Persistence/Entities/SagaCommand.cs:7:/// over NATS (`sent`), and a background sweeper re-issues stale `pending`
src/Orders/Infrastructure/Persistence/Configurations/SagaCommandConfiguration.cs:10:/// `(status, created_at)` and `(status, next_attempt_at)` — the sweeper's
src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:13:public sealed class OutboxRelayBackgroundService(
src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:16:    ILogger<OutboxRelayBackgroundService> logger) : BackgroundService
src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:25:        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.PollIntervalMs));
src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:27:        // PeriodicTimer does not queue missed ticks, so a slow cycle delays
src/Orders/Infrastructure/Outbox/OutboxRelayBackgroundService.cs:30:        while (await timer.WaitForNextTickAsync(stoppingToken))
src/Orders/Infrastructure/Saga/ChannelSagaCommandSignal.cs:33:            // and SagaCommandSweeper re-issues any pending row older than
src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs:12:/// row plus <see cref="SagaCommandSweeper"/> — not this worker — is the
src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs:18:    ILogger<SagaCommandDispatchWorker> logger) : BackgroundService
src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs:35:                // recovery, exactly OutboxRelayBackgroundService's own stance.
src/Orders/Infrastructure/Saga/SagaCommandDispatchWorker.cs:38:                    "Saga command dispatch failed for order {OrderId}, command {Command}; the durable row remains for the sweeper.",
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:14:/// transaction), claim (SO11's bounded lease), claim-due (the sweeper's
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:99:    /// The sweeper's batch claim (design.md §6.4): every <c>pending</c> row
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:100:    /// past <see cref="OrdersSagaSweeperOptions.PendingGraceMs"/> (the SO3
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:106:    /// both selects and stamps the lease, so a concurrent sweeper (or a
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:112:        var pendingCutoff = now.AddMilliseconds(-options.Value.Sweeper.PendingGraceMs);
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:180:    /// <c>last_error</c>, and schedules <c>next_attempt_at</c> on capped
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:185:    /// every caller of this method (the dispatch worker, the sweeper) drives
src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:195:        var backoffMs = Math.Min(30_000d * Math.Pow(2, parkCycles), options.Value.Sweeper.ParkRetryCapMs);
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:9:/// The poll loop and graceful drain — <c>OutboxRelayBackgroundService</c>'s
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:11:/// <see cref="SagaCommandSweeper"/> itself stays a plain class with no host
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:14:public sealed class SagaCommandSweeperBackgroundService(
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:17:    ILogger<SagaCommandSweeperBackgroundService> logger) : BackgroundService
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:21:        if (!options.Value.Sweeper.Enabled)
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:26:        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Value.Sweeper.IntervalMs));
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:28:        // PeriodicTimer does not queue missed ticks, so a slow cycle delays
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:30:        // no second caller (OutboxRelayBackgroundService's own guarantee).
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:31:        while (await timer.WaitForNextTickAsync(stoppingToken))
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:34:            var sweeper = scope.ServiceProvider.GetRequiredService<ISagaCommandSweeper>();
src/Orders/Infrastructure/Saga/SagaCommandSweeperBackgroundService.cs:38:                await sweeper.RunOnceAsync(stoppingToken).ConfigureAwait(false);
src/Orders/Infrastructure/Outbox/OutboxRelay.cs:15:/// The one member <see cref="OutboxRelayBackgroundService"/> depends on —
src/Orders/Infrastructure/Outbox/OutboxRelay.cs:115:                // here — it propagates so the caller (the BackgroundService
src/Orders/Infrastructure/Saga/SagaCommandSweeper.cs:11:/// The one member <see cref="SagaCommandSweeperBackgroundService"/> depends
src/Orders/Infrastructure/Saga/SagaCommandSweeper.cs:12:/// on — resolved from DI, so <c>SagaCommandSweeperLoopTests</c> can
src/Orders/Infrastructure/Saga/SagaCommandSweeper.cs:13:/// substitute a fake and prove the loop's own re-entry/reschedule behaviour
src/Orders/Infrastructure/Saga/SagaCommandSweeper.cs:16:public interface ISagaCommandSweeper
src/Orders/Infrastructure/Saga/SagaCommandSweeper.cs:27:/// never through <see cref="ISagaCommandSignal"/>, because the sweeper must
src/Orders/Infrastructure/Saga/SagaCommandSweeper.cs:30:public sealed class SagaCommandSweeper(
src/Orders/Infrastructure/Saga/SagaCommandSweeper.cs:34:    ILogger<SagaCommandSweeper> logger) : ISagaCommandSweeper
src/Orders/Infrastructure/Saga/SagaCommandSweeper.cs:38:        var claimed = await store.ClaimDueAsync(options.Value.Sweeper.BatchSize, cancellationToken).ConfigureAwait(false);
src/Orders/Application/Ports/ISagaCommandSignal.cs:12:/// sweeper is (SO3).
src/Orders/Application/Ports/ISagaCommandSignal.cs:24:    /// <c>pending</c> and the sweeper will resume it.
src/Orders/Application/Ports/ISagaRetryDelay.cs:6:/// <c>SagaCommandDispatcherTests</c> can prove the exact backoff schedule
src/Orders/Application/Ports/ISagaCommandStore.cs:44:    /// currently held by a concurrent claimant/the sweeper — a SILENT no-op
src/Orders/Application/Ports/ISagaCommandStore.cs:49:    /// <summary>The sweeper's batch claim (design.md §6.4): every stale <c>pending</c> row (SO3's crash window) and every due <c>parked</c> row (SO5), up to <paramref name="batchSize"/>, each under the same bounded lease as <see cref="TryClaimAsync"/>.</summary>
src/Orders/Application/Ports/ISagaCommandStore.cs:55:    /// <summary>Marks a claimed row <c>parked</c> on exhaustion (SO5): accumulates <see cref="SagaCommandRecord.Attempts"/>, records the last error, and schedules the next retry on capped backoff.</summary>
src/Orders/Application/Ports/ISagaCommandStore.cs:63:    /// is no <c>next_attempt_at</c> to schedule — accumulates
src/Orders/Infrastructure/Saga/TaskDelaySagaRetryDelay.cs:5:/// <summary>The one production <see cref="ISagaRetryDelay"/> — a thin <see cref="Task.Delay(TimeSpan,CancellationToken)"/> wrapper, kept an adapter so it can be faked in <c>SagaCommandDispatcherTests</c>.</summary>
src/Orders/Infrastructure/Saga/TaskDelaySagaRetryDelay.cs:9:        Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken);
src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs:12:/// <see cref="SagaCommandSweeper"/> depend on — resolved from DI rather than
src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs:23:    /// <summary>The sweeper's path: the row is ALREADY claimed (<see cref="ISagaCommandStore.ClaimDueAsync"/> claimed it under its own lease), so this issues directly with no second claim — claiming twice would make the sweeper's own claim invisible to itself.</summary>
src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs:41:/// <see cref="SagaCommandSweeper"/> (the guarantee) — never through
src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs:48:/// = 16 500 ms. This runs on the dispatch worker or the sweeper, never the
src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs:69:            // concurrent claimant/the sweeper — a SILENT no-op (design.md §6.2).
src/Orders/Infrastructure/Saga/SagaCommandDispatcher.cs:82:        // this cycle AND across every sweeper re-issue of this same row —
src/Orders/Application/Ports/ISagaCommands.cs:12:/// every sweeper re-issue of the same row — a retry reuses the same value,
```

**(c) Classification, one line per hit (grouped — every hit falls into one of these six buckets, none unclassified):**

1. **`OutboxRelayBackgroundService` + `PeriodicTimer`, ×3 (Billing/Fulfillment/Orders)** — the pre-existing Kafka-publish poll loop from feature 14 (`outbox_and_idempotency`). Publishes *facts already written*; it never originates a payment and this feature added nothing to it.
2. **`BillingRpcResponder`/`StockRpcResponder`/`OrdersCreateResponder`/`SagaFactsConsumer` : `BackgroundService`, and the `TaskScheduler.Default` continuation scheduler inside the first two** — request/message-driven (NATS subscribe-loop or Kafka consume-loop), not time-driven. `billing.payment.register` is answered by exactly this mechanism: an inbound NATS request, and nothing else, triggers it.
3. **Comments containing "sweeper" inside `src/Billing/*` (`InvoiceIssueService.cs`, `EfCoreInvoiceRepository.cs`)** — prose referring to ORDERS' saga sweeper *retrying* `billing.invoice.issue`/`billing.credit.*` RPC calls, never `billing.payment.register`. Confirmed by a second, targeted search: `grep -rniE "PaymentRegister|payment\.register|billing\.payment" --include=*.cs src/Orders/ src/Fulfillment/` returns **zero** hits — nothing in Orders' or Fulfillment's saga machinery ever calls, schedules, or retries a payment registration.
4. **Orders' `SagaCommandSweeper*`, `TaskDelaySagaRetryDelay`, `EfCoreSagaCommandStore`, `SagaCommandDispatcher(Worker)`, `OrdersSagaOptions`/`ISagaCommand*`** — the saga's own retry/backoff engine for **outgoing saga commands** (`credit.hold`, `invoice.issue`, `stock.reserve`, …). It never sends a `payment.register` command (same zero-hit search as bucket 3) — payments only ever arrive as an external, unscheduled inbound request.
5. **`src/Orders/Presentation/SagaFactsConsumer.cs:58` — `Task.Delay(2s)`** — a Kafka-consumer retry backoff on a transient consume failure, unrelated to payments; it neither reads nor produces anything payment-shaped.
6. **`ReservationConfiguration.cs`, `KafkaFactStreamSubscriber.cs` and the remaining doc-comment cross-references** — index-naming prose and a comment about which thread a `BackgroundService.ExecuteAsync` body runs on; neither names, schedules, nor retries a payment.

**One additional, deliberately-checked candidate outside the `.cs` search:** `n8n/workflows/2-payment-robot.json` (copied verbatim from #7 by feature 3, `shared_spec`) is an **n8n-scheduled** workflow (`n8n-nodes-base.scheduleTrigger`) that would call the Gateway's REST payment endpoint (features 25/29, not built) — i.e. the intended EXTERNAL demo "payment robot" the domain model itself names, external to every .NET process. Confirmed inert: the n8n container is not part of the currently-running infra (`docker ps -a | grep -i n8n` returns nothing) and its target endpoint does not exist. Not an internal timer by any reading — it is the external caller the design requires.

No hit is unclassified. `Quartz`/`Hangfire` also searched (`grep -rniE "Quartz|Hangfire" --include=*.cs src/`) — zero hits.

## The idempotency guard (R48) — presence and absence

- **Presence:** `tests/Billing.IntegrationTests/PaymentRegisterTests.cs` › `R48_ASequentialRepeatOfTheSamePaymentReferenceAnswersDuplicate_RecordsNoSecondPayment_AndEmitsNoSecondFact` and `R48_TwoConcurrentRequestsForTheSamePaymentReferenceProduceExactlyOnePaymentRow_AndExactlyOneFactPair` (`Promise.all`-style `Task.WhenAll`, real MS-SQL, real NATS) assert the *presence* half: exactly one `accepted` outcome.
- **Absence, both halves:**
  - *No second payment row* — both tests re-query `PaymentsOfAsync` and assert `Assert.Single`; the concurrent test additionally asserts `db.OutboxMessages.CountAsync(m => m.EventType is "payment.received.v1" or "credit.released.v1") == 2` (never 4).
  - *No second effect even when the SAME reference targets a DIFFERENT invoice* — the belt-and-braces test (`TheBeltAndBracesUniqueConstraintBackstopCatchesTheRaceWhenTwoDifferentInvoicesUnderDifferentCreditLinesShareThePaymentReferenceConcurrently`) uses **two separate credit lines** deliberately, so `BI8`'s credit-row lock does **not** serialise the race (a single shared credit line would hide this path entirely, exactly as #7's own reviewer found for the CONFLICT case) — only the `payments.payment_reference` UNIQUE constraint can arbitrate, and the test asserts both `paymentsA.Count + paymentsB.Count == 1` and that the loser's reply is `PRECONDITION_FAILED` (never `CONFLICT`, never a raw `UNAVAILABLE`).
- **Armed:** the DB-constraint→`PaymentReferenceConflictError` translation in `EfCoreInvoiceRepository.MarkPaidAsync` was deleted (letting the raw `DbUpdateException` propagate) — the belt-and-braces test failed with `Expected: "PRECONDITION_FAILED" / Actual: "INTERNAL_ERROR"`. See the arming table.

## Arming table — every probe run, verbatim messages

All probes below were run against `src/Billing/Application/PaymentRegisterService.cs` unless noted; each was: introduce the mutation, forced rebuild (`--no-incremental`), run the named test(s) and record the FAIL verbatim, restore from a byte-identical backup (confirmed via `cmp`), forced rebuild again, confirm green.


| # | Mutation | Restore verified | Named test(s) that fail | Verbatim failure |
|---|---|---|---|---|
| A | `invoice.MarkPaid(...)` call deleted | `cmp` empty; rebuilt; 10/10 green | `R47_...`, `R49_AnAmountMismatch...`, `R49_ACurrencyMismatch...`, `R49_ADifferentPaymentReference...` (4 unit tests) | `R47`: `Assert.Equal() Failure … Expected: "paid" / Actual: "issued"`. `R49` × 3: `Assert.Throws() Failure: No exception was thrown` |
| B | `credit.Release(...)` call deleted | `cmp` empty; rebuilt; 10/10 green | `R47_...` (1 unit test) | `Assert.Single() Failure: The collection was empty` (`credits.Saved!.DomainEvents` — `CreditReleased` never raised) |
| C | R48 fast-path short-circuit removed (a deliberate SUPPRESSION) | `cmp` empty; rebuilt; 10/10 green | `R48_TheFastPathAnswersDuplicate...`, `N11_TheFastPathRaisesPaymentReferenceConflict...` (2 unit tests) | Both: `InvoiceNotFoundError : No invoice resolves for invoiceId '<none>' / invoiceReference 'INV-000001'` — falls through to the transactional path and cannot resolve identity, since the (unrelated, still-live) `FindByInvoiceReferenceResult` fake was never set for this scenario |
| D | Authority re-read's duplicate short-circuit removed (the SECOND line of defence) | `cmp` empty; rebuilt; 10/10 green | `R48_TheAuthorityReReadUnderTheInvoiceLock...` (1 unit test) | `OrderToCash.Billing.Domain.Errors.InvoiceAlreadyPaidError : Invoice 'INV-000001' has already been paid` — fails by **throwing**, not by a silent double-write: the aggregate's own B8 guard is a second line of defence behind the suppression, exactly as design intends |
| E | The belt-and-braces `try`/`catch` (DB-constraint → `PaymentReferenceConflictError`) removed in `EfCoreInvoiceRepository.MarkPaidAsync` | `cmp` empty; rebuilt; 9/9 green | `TheBeltAndBracesUniqueConstraintBackstopCatchesTheRace...` (1 integration test) | `Assert.Equal() Failure … Expected: "PRECONDITION_FAILED" / Actual: "INTERNAL_ERROR"` — the raw `DbUpdateException` falls through to the generic `_ => INTERNAL_ERROR` mapper arm |
| F | **Corruption, not deletion**: `command.PaymentReference` appended with `"-CORRUPT"` before being carried into `MarkPaidInput` | `cmp` empty; rebuilt; 9/9 green | `R47_...`, `N11_...`, `R48_ASequentialRepeat...`, `R48_TwoConcurrentRequests...` (4 integration tests) | `Expected: "PAY-000301" / Actual: "PAY-000301-CORRUPT"` (reply field, DB row field, and outbox payload field, all three); the R48 sequential test additionally failed with `Expected: "duplicate" / Actual: null` because the corrupted stored reference no longer matched the ORIGINAL reference on repeat lookup |
| Swap | `credits.SaveChangesAsync`/`invoices.MarkPaidAsync` call order swapped | `cmp` empty; rebuilt; unit 10/10 + integration 9/9 green | `R47_LocksTheCreditLine...` (unit), `R47_RecordsThePayment...` (integration) | Unit: *"R47: invoices.MarkPaid must run (and its outbox row insert) BEFORE credits.SaveChanges."* Integration: `Expected: "payment.received.v1" / Actual: "credit.released.v1"` |

Every restore was verified two ways before the confirming green run: `cmp` against the scratchpad backup (empty diff) **and** `touch` + `dotnet build --no-incremental` to force the rebuild, per the arming protocol's own warning about a stale-but-correct binary vouching for still-armed source.

## Live walkthrough against the composed stack

The compose infra (`otcnet-mssql`, `otcnet-nats`, `otcnet-kafka`, …) was already up; no app process (Billing, Orders, Fulfillment) was running at session start (`ps aux` confirmed empty, contrary to the brief's premise that they were already running — the databases' PERSISTED state matched the brief exactly, the running processes did not). I built and started `src/Billing` and `src/Orders` from this session's own build (`dotnet <service>.dll` against the compose infra's connection strings) to demonstrate the cross-service cycle, then stopped both afterward — neither was running before this session and neither is running after it.

**Pre-state**, queried directly from `otc_billing`/`otc_orders` (matches the brief exactly): five invoices `issued` — `INV-000006`(ORD-000007, 49998 EUR) … `INV-000010`(ORD-000013, 5547 EUR). `ORD-000011` sits at `despatched` with its credit hold already released by hand (feature 19's `I4` fixture) — confirmed via `credit_items` (`hold 1000`/`release 1000`, unrelated to the order's real total) and left exactly as found, per the brief.

**Chosen target: `INV-000006` / `ORD-000007`** (CarrefourEs/IBERFOODS, 49998 EUR, credit line `CR-000001`, `available_credit` before = 400004 — two orders, `ORD-000007` and `ORD-000010`, each holding 49998 with no release yet).

A raw NATS request was sent to `billing.payment.register` with `x-correlation-id` set to the order's OWN id (`8B0670D1-082D-4462-91B1-0495C488D3E2`), mirroring the wire convention `invoice.issue`'s own callers already use:

```
10:07:05.xxx → billing.payment.register {"invoiceReference":"INV-000006","paymentReference":"PAY-LIVE-1788689225553","amount":{"amount":49998,"currency":"EUR"},"valueDate":"2026-09-06T10:07:05.000Z","source":"test"}
10:07:07.056 ← reply: {"outcome":"accepted","paymentReference":"PAY-LIVE-1788689225553","invoiceReference":"INV-000006","orderReference":"ORD-000007","invoiceStatus":"paid","paidAt":"2026-09-06T10:07:07.056Z"}
```

**Observed, after** (all queried directly from the live databases):

| Where | Before | After |
|---|---|---|
| `otc_billing.invoices` (`INV-000006`) | `status='issued'`, `paid_at=NULL` | `status='paid'`, `paid_at='2026-09-06 10:07:07.056'` |
| `otc_billing.payments` | 0 rows for this invoice | 1 row: `PAY-LIVE-1788689225553`, `amount=49998`, `currency_code=EUR`, `source=test` |
| `otc_billing.credit_items` (`ORD-000007`) | `hold 49998`, `consume 49998` | + `release 49998` |
| `available_credit` (`CR-000001`) | `400004` (`500000 − 99996`) | `450002` (`500000 − 49998`) — exactly `+49998`, the released amount |
| `otc_billing.outbox` (correlation = the order id) | `credit.approved.v1` (seq 10002), `invoice.issued.v1` (seq 10009) | **+ `payment.received.v1` (seq 10015) THEN `credit.released.v1` (seq 10016)**, both `published_at` stamped ~0.5 s later — R47's ordering, live |
| `otc_orders.orders` (`ORD-000007`) | `invoiced` | **`completed`**, `updated_at='2026-09-06 10:07:07.057'` |

The order crossed `invoiced → paid → completed` **unattended**, driven entirely by Orders' pre-existing (unmodified) saga-step wiring consuming `payment.received.v1` then `credit.released.v1` — closing the order-to-cash cycle end to end for the first time in this repository. (`orders.updated_at` and `invoices.paid_at` are within 1 ms of each other because both `Order.MarkPaid`/`Order.Complete` take their timestamp from the FACT's own `occurredAt` — Billing's single `clock.UtcNow` read propagated through the wire — not from Orders' own wall clock at consumption time; this is by design, not a race.)

**Timings observed:** request → `accepted` reply ≈ 2.0 s (first-call JIT/EF-query-plan warmup on a cold process); reply → Kafka `published_at` ≈ 0.5 s (the relay's own poll interval); by the time a **3-second** poll of the database was issued after the reply returned, `ORD-000007` already read `completed` — the full cross-service cascade (Billing commit → Kafka publish → Orders consume → `MarkPaid` → `credit.released.v1` published → Orders consume → `Complete`) completed inside that 3-second window.

**The negative half, live, twice:**

1. **Repeat** the identical request (same `paymentReference`, same invoice) → `{"outcome":"duplicate", …, "paidAt":"2026-09-06T10:07:07.056Z"}` — the ORIGINAL `paidAt`, not a new one; no new row, no new fact (re-verified by the row/outbox counts already captured above being unchanged).
2. **A genuine R49 refusal against a still-open invoice** — `INV-000007`/`ORD-000008` (total 5547 EUR), payment registered for 5546 (one cent short): `{"code":"PRECONDITION_FAILED","message":"Payment amount 5546 does not equal the invoice's totalAmount 5547.","details":{"code":"INVOICE_PAYMENT_AMOUNT_MISMATCH"}}`. Re-queried after: `INV-000007` still `status='issued'`, `paid_at IS NULL`, 0 rows in `payments` for it, `ORD-000008` still `invoiced` — nothing moved.

Both app processes were stopped cleanly at the end of the walkthrough (`kill`, confirmed via `ps aux` afterward).

## Test counts and `quality.sh` — read off a real run

- `dotnet test tests/Billing.UnitTests` (scoped): **225/225 passed**. This feature's own additions: `PaymentRegisterServiceTests.cs` (10 new tests), `PaymentRegisterResponderValidationTests.cs` (9 new tests), plus 5 cases folded into existing theories/files (`InvoiceRpcPayloadTests.cs` +2, `InvoiceSubjectsTests.cs` +1, `BillingErrorMapperTests.cs` +2) — 24 tests in total for this feature, all green.
- `dotnet test tests/Billing.IntegrationTests` (scoped, Testcontainers — real MS-SQL, NATS, Kafka): **83/83 passed**, up from 74 before this feature (9 new cases in `PaymentRegisterTests.cs`).
- **`./quality.sh` — full solution, run to completion, figures read directly off that run:**
  - Format check: `dotnet format --verify-no-changes` — clean.
  - Build: succeeded, 0 warnings, 0 errors, across all 8 `src/` projects and all 15 test/tooling projects.
  - Test + coverage, **every** project in the solution, **all green, zero failures**:
    `Cqrs.UnitTests` 23/23 · `Contracts.UnitTests` 21/21 · `SharedKernel.UnitTests` 50/50 · `Fulfillment.UnitTests` 119/119 · `Billing.UnitTests` 225/225 · `Orders.UnitTests` 280/280 · `Notifications.IntegrationTests` 7/7 · `Seed.UnitTests` 34/34 · `Architecture.Tests` 16/16 · `Seed.IntegrationTests` 6/6 · `Fulfillment.IntegrationTests` 56/56 (3 m 4 s) · `Billing.IntegrationTests` 83/83 (5 m 7 s) · `Orders.IntegrationTests` 71/71 (5 m 50 s).
  - Coverage per assembly (line %, informational — the enforcing gate is feature 34, not yet built): reports ranged 0.0%–97.2% across the 13 coverage attachments (the 0.0% is a placeholder-only assembly — Gateway/Notifications/Projector still carry only `README_PLACEHOLDER.cs` types per feature_list.json's pending phases 11–13); Billing's own domain/application coverage sits in the high range consistent with the rest of the solution, not a regression this feature introduced.
  - `[OK] quality.sh finished`, exit code 0.
- `./init.sh`: re-run after the feature closed — exits 0.

## Traceability

| Req | Proven by |
|---|---|
| **R47** | Unit: `PaymentRegisterServiceTests.cs` › `R47_LocksTheCreditLineBeforeTheInvoiceRow_CallsMarkPaidAndRelease_PersistsTheInvoiceBeforeTheCreditLine_AndRepliesAccepted` (ordering armed by swap, fact-emission armed by probes A/B). Integration: `PaymentRegisterTests.cs` › `R47_RecordsThePayment_MovesTheInvoiceToPaid_ReleasesTheCreditHold_AndEmitsPaymentReceivedThenCreditReleasedInThatOrder` (real `seq` order, real payload fields, armed by the swap and by corruption probe F) |
| **R48** | Unit: `R48_TheFastPathAnswersDuplicateWithoutOpeningATransaction_...`, `R48_TheAuthorityReReadUnderTheInvoiceLockAnswersDuplicateAndWritesNothingNew_...` (both armed, probes C/D). Integration: `R48_ASequentialRepeatOfTheSamePaymentReferenceAnswersDuplicate_...`, `R48_TwoConcurrentRequestsForTheSamePaymentReferenceProduceExactlyOnePaymentRow_...`, `N11_TheSamePaymentReferenceReusedAgainstADifferentInvoice_...`, `TheBeltAndBracesUniqueConstraintBackstopCatchesTheRace...` (armed, probe E) |
| **R49** | Unit: `R49_AnAmountMismatchRaisesInvoicePaymentAmountMismatchError_AndWritesNothing`, `R49_ACurrencyMismatchRaisesInvoicePaymentCurrencyMismatchError_AndWritesNothing`, `R49_ADifferentPaymentReferenceAgainstAnAlreadyPaidInvoiceRaisesInvoiceAlreadyPaidError_AndNeverTouchesTheCreditLedger` (all armed via probe A). Integration: `R49_AnAmountMismatchRepliesPreconditionFailed_...`, `R49_ACurrencyMismatchRepliesPreconditionFailed_...`; live-verified twice (accepted + one refusal) in the walkthrough above |

## What was not done, and why

- **No migration, no `Contracts` change** — both verified unnecessary before writing code (see "Files touched → Specs" above), not merely unattempted.
- **The Gateway's "Register payment" HTTP endpoint** (features 25/29) does not exist yet, so R48/R49's sketch `Level: API` is answered one layer down, at the NATS/RPC responder this feature actually builds — the test-matrix rows say so explicitly rather than claiming API-level coverage that does not exist (the same substitution #7's own counterpart made, accepted by its reviewer).
- **`src/Orders/` untouched** — confirmed unnecessary rather than merely unattempted, both by a `grep` proving no saga-side reference to this subject exists, and live, by the fact that `ORD-000007` reached `completed` with zero changes to Orders in this feature.
- **A unit-level test of the raw `ER_DUP_ENTRY`→typed-error conversion in isolation** was not added (would need a fake `SqlException`, which `SqlExceptionFactory.cs` already supplies elsewhere in this suite for exactly this purpose — reused, not invented, in `BillingErrorMapperTests.cs`'s own transient-exception theory); the integration-level belt-and-braces test against a REAL constraint violation is the stronger, load-bearing proof for this genuinely rare race.

## Something that surprised me

`orders.updated_at` and `invoices.paid_at` landing within 1 millisecond of each other during the live walkthrough looked, at first glance, like an impossible causality violation (Orders "reacting" to a Kafka fact BEFORE that fact's outbox row was even marked `published_at`). It resolves cleanly: both `Order.MarkPaid`/`Order.Complete` are driven by the FACT's own `occurredAt` field (`SagaStepTable.cs`: `(order, fact) => order.MarkPaid(fact.OccurredAt)`), and both facts in this transaction carry the SAME `occurredAt` — `PaymentRegisterService` reads `clock.UtcNow` exactly once and threads it through both `InvoiceContext` and `CreditContext` (mirroring `BI13`'s existing convention). The timestamps are business time, not processing time, so consumption latency cannot be read off them — the 3-second poll window is the only honest processing-latency evidence this walkthrough can offer, and it is recorded as such above.
