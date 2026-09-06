# `billing_credit` — Design (.NET 10 / C# 14 / EF Core / MS-SQL, assessment #8)

> **Stack-specific.** This file is where the .NET, EF Core, MS-SQL, `NATS.Net`, `Confluent.Kafka`, `src/Cqrs` and Testcontainers detail lives. Nothing here belongs in `specs/shared/`; #7 wrote its own equivalent against the same `R37` – `R41`, and #9 will write a third.
>
> **Post-gate revision, 2026-09-05.** The human gate **overruled both** of this spec's GATE recommendations and attached a standing instruction (*"stop leaving issues to the next phase — fix them"*). What changed, and only what changed: **§8.2 – §8.4** now specify the seven-file relay-parity family at #7's full scope, together with the two structural enablers the first pass called a redesign — the neutral `FactEvent` domain-event base and the per-service `IFactPayloadMapper` port — plus the `KafkaOptions` unification and its compile-time client-id guard (`BC29`); **§3.3, §3.5, §4.5 and §15's `L25`** now supply the overflow property instead of stating it (`BC30`); and **§10.3** has become a per-entry verdict on backlog ids 48, 52, 53 and 54, of which three are now closed inside this feature (§10.4 – §10.6). Everything else in this file is unchanged from the pass the gate reviewed.
>
> **This is a port with a delta analysis.** #7's `specs/billing_credit/design.md`, its gate record (`progress/spec_billing_credit.md`, 22 open points, 5 flagged for conscious approval) and its review (`progress/review_billing_credit.md`, **REJECTED** on defect `D1`, approved on pass 2 with `N1`–`N5` recorded) were read first. Everything stack-agnostic is ported as content; the effort went into §4.7, §5.5, §7, §8.2, §10, §11 and **§15** — the places .NET, MS-SQL and #8's own backlog genuinely differ.
>
> Authorities: [`specs/shared/domain-model.md`](../shared/domain-model.md) §5.1 (`BuyerCredit`, `CreditLedgerEntry`, **B1** – **B5**), §7.1/§7.2 (the envelope and the fact catalogue), §8 (cross-cutting rules — rule 6 is satisfied **literally** here, see §5.5); [`specs/shared/saga.md`](../shared/saga.md) §2, §3.1, §4.2 – §4.3, §5, §6; [`specs/shared/asyncapi.yaml`](../shared/asyncapi.yaml) (`creditHold`, `creditRelease`, `creditList`, `RpcHeaders`, `RpcError`, `CreditApproved`/`CreditRejected`/`CreditReleased`, the `billingFacts` topic); [`specs/outbox_and_idempotency/design.md`](../outbox_and_idempotency/design.md) (the unit of work, repository-drains-aggregate, the writer, the relay, `OI9`'s drained-events hazard — **copied, never re-designed**); [`specs/fulfillment_stock/design.md`](../fulfillment_stock/design.md) (the reference service: §6.2 concurrency, §6.5 the error mapper, §8 the outbox copies, **§15 the ledger this one is modelled on**); [`specs/order_saga_orchestrator/design.md`](../order_saga_orchestrator/design.md) §6 (the caller these responders answer, and feature 42's terminal/transient `RpcError` split).

## 1. Scope

**In scope.**

- The **`BuyerCredit` aggregate** and its `CreditLedgerEntry` child entity, pure domain: `EvaluateHold`, `Approve`, `Refuse`, `Release`, `Consume`, invariants **B1** – **B5** enforced inside the aggregate, and the one pure function `CreditExposure.Summarise` that is the whole feature's arithmetic (§3.3).
- The **three NATS responders** — `billing.credit.hold`, `billing.credit.release`, `billing.credit.list` — as **one** `BackgroundService` over one transport, dispatching through the existing `src/Cqrs` dispatcher, request and reply bare JSON exactly per `asyncapi.yaml` (§4, §5).
- The **hold transaction** under MS-SQL with `READ_COMMITTED_SNAPSHOT ON`: an exclusive lock on the single `credits` row taken **before** any ledger read, every decision-bearing `credit_items` read locking, and the honest statement that this service — unlike Fulfillment — satisfies `domain-model.md` §8 rule 6 literally (§5.5).
- **Responder idempotency** by `orderReference` (`saga.md` §2, §6 layer 3): a re-issued `credit.hold` answers `already_held` on **any** recorded `hold` entry whatever its net exposure; a re-issued `credit.release` reports `released: false` (§5.7).
- The **credit-decision port**, feature 20's seam, fixed now so that feature 20 is a DI change and not a redesign (§6).
- **Billing's copies** of the unit of work, the outbox writer, the relay and the Kafka publisher, **the service-neutral refactor of the canonical relay family that the third copy makes possible, and the `OB1` parity guard over all seven of its files** (§8) — deferred to this feature by `specs/fulfillment_stock/design.md` §8.3 and its `tasks.md` I3 hand-over, and widened to #7's full seven-file scope by the gate ruling of 2026-09-05. This carries two structural changes into `src/Orders/` and `src/Fulfillment/`: a neutral `FactEvent` name for each service's domain-event base (§8.2.2) and a per-service `IFactPayloadMapper` port (§8.2.3).
- **Five backlog closures inside this feature**, because it is the feature that already has the files open (§10): **id 50** (RPC responder shutdown fault isolation, specified for Billing and back-fitted to Fulfillment), **id 51** (RPC payload schema parity derived from `asyncapi.yaml` rather than hand-retyped), and — added by the 2026-09-05 standing instruction — **id 48** (§10.4, a fresh `NatsHeaders` per request, test-only), **id 53** (§10.5, three reply-shape assertions that only throw, test-only) and **id 54** (§10.6, the missing Fulfillment ledger row, in a file `tasks.md` G3 already edits). **Id 52 stays open**, with the reason in §10.3.
- The **designed first boot** against the live compose stack: what the five parked `credit.hold` commands do when a responder finally answers (§11), read from the running stack while this design was written.
- The **ported-idiom ledger** (§15), derived from an enumerated boundary list rather than from recollection — Phase 9's closing assessment named that as the experiment Phase 10 should run.

**Out of scope, and owned elsewhere.**

| Not here | Owned by |
|---|---|
| The `.99` rule, `CREDIT_FAILURE_RATE`, `R42` – `R44` | **feature 20 `billing_credit_simulator`** — the port is here, the simulator is not (§6.3) |
| `billing.invoice.issue`, the `Invoice` aggregate, the `Consume` caller | feature 21 `billing_invoicing` — `Consume` ships ready, unit-tested and uncalled |
| `billing.payment.register`, the `invoice_paid` release caller | feature 22 `billing_remittance_intake` — see §8.5's note it must not rediscover |
| The `orders.cancel` responder that calls `billing.credit.release`, and `saga.md` §2's missing table row for that subject | feature 41 `orders_cancel_responder` (`requirements.md` §3) |
| Gateway callers of `billing.credit.list` | feature 25 `gateway_rest_auth` |
| DLQ, retries, metrics, tracing, `traceparent` / `x-deadline-ms` | feature 27 `observability_reliability` |
| A parity guard over the messaging family (`RpcJson`, `RpcErrorPayload`) | recorded, not taken — §8.3 |
| Backlog id 52 | not this feature — §10.3 says why. Ids 48, 53 and 54 **are** this feature's after the 2026-09-05 ruling — §10.4 – §10.6 |

## 2. Where everything lives

```
src/Billing/
  Domain/
    README_PLACEHOLDER.cs                 KEPT — tests/Architecture.Tests/DomainAssemblies.cs selects this type by name
    BuyerCredit.cs                        aggregate root (§3.1): EvaluateHold / Approve / Refuse / Release / Consume
    BuyerCreditSnapshot.cs                BuyerCreditSnapshot + CreditLedgerEntrySnapshot — the plain shapes the mapper reconstitutes from
    CreditLedgerEntry.cs                  child entity, no mutator of any kind (B2)
    CreditEntryType.cs                    enum + CreditEntryTypes token map (the ReservationStatuses convention)
    CreditExposure.cs                     PURE: Summarise(entries) -> LedgerSummary — BC5/BC6, the crux (§3.3)
    CreditRejectionReason.cs              closed set: over_limit | simulated_cents_rule | simulated_failure_rate
    CreditReleaseReason.cs                closed set: invoice_paid | order_cancelled
    HoldEvaluation.cs                     already_held | currency_mismatch | over_limit | fits (§3.1)
    Events/FactEvent.cs                   abstract base — the NEUTRAL name all three services now share (§8.2.2)
    Events/CreditApproved.cs              credit.approved.v1
    Events/CreditRejected.cs              credit.rejected.v1   -- ONE builder, ONE call site (§3.4)
    Events/CreditReleased.cs              credit.released.v1
    Errors/*.cs                           six DomainError subclasses with stable codes (§3.5)
  Application/
    README_PLACEHOLDER.cs                 KEPT (same reason)
    Ports/IClock.cs, IUnitOfWork.cs, IFactPublisher.cs + PublishableFact.cs   copies of Orders' (§8.1)
    Ports/IFactPayloadMapper.cs           the seam the canonical OutboxWriter dispatches through (§8.2.3)
    Ports/IBuyerCreditRepository.cs       the locking write-side port (§5.2)
    Ports/ICreditReadPort.cs              the non-locking read side (§5.2)
    Ports/ICreditDecisionPort.cs          feature 20's seam (§6.1)
    Commands/HoldCreditCommand.cs, ReleaseCreditCommand.cs + their ICommandHandler classes
    Queries/ListCreditQuery.cs + its IQueryHandler class
    CreditHoldService.cs                  the hold transactional unit as a plain class the handler delegates to (§5.3)
    CreditReleaseService.cs               the release transactional unit (§5.3)
    CreditApplicationErrors.cs            CreditLineNotFoundError, CreditCurrencyMismatchError (§5.4)
  Infrastructure/
    SystemClock.cs                        copy of Orders'
    BillingOptions.cs                     MS-SQL / NATS / Kafka / relay / responder settings (§14.1)
    BillingServiceCollectionExtensions.cs AddBilling — explicit registration, one port at a time
    Credit/AlwaysApproveCreditDecision.cs the bound adapter until feature 20 (§6.3)
    Persistence/                          UNCHANGED from phase 6 (DbContext, entities, configurations, migration)
    Persistence/EfCoreUnitOfWork.cs       copy of Orders', over BillingDbContext
    Persistence/EfCoreBuyerCreditRepository.cs   the lock protocol (§5.5) + save + outbox drain (§7.2)
    Persistence/EfCoreCreditReadRepository.cs    the three non-locking queries (§7.3)
    Persistence/BuyerCreditRowMapper.cs   rows <-> BuyerCredit (snapshot in, snapshot out), instants per §7.4
    Outbox/BillingFactTopic.cs            otc.billing.facts.v1, guarded by a read-the-spec test
    Outbox/CreditFactPayloadMapper.cs     sealed class : IFactPayloadMapper — domain event -> Contracts payload record
    Outbox/OutboxWriter.cs                copy of Orders' (usings + banner + namespace only) -- OB1 set (§8.3)
    Outbox/OutboxEnvelopeMapper.cs        copy of Orders'                        -- OB1 set
    Outbox/OutboxRelay.cs                 copy of Orders' (banner per §8.1)      -- OB1 set
    Outbox/OutboxRelayOptions.cs          copy of Orders'                        -- OB1 set
    Outbox/OutboxRelayBackgroundService.cs copy of Orders'                       -- OB1 set
    Outbox/KafkaFactPublisher.cs          copy of Orders'                        -- OB1 set
    Outbox/KafkaOptions.cs                copy of Orders'; ClientId is `required`, no default   -- OB1 set (§8.3)
    Messaging/NatsOptions.cs              copy of Orders'
    Messaging/Rpc/CreditRpcPayloads.cs    the six request/reply records, transcribed from asyncapi.yaml (§4.3)
    Messaging/Rpc/RpcJson.cs              copy of Orders' — the one shared JsonWire.Options
    Messaging/Rpc/RpcErrorPayload.cs      copy of Orders'
  Presentation/
    README_PLACEHOLDER.cs                 KEPT (same reason)
    CreditRpcResponder.cs                 ONE BackgroundService, three subjects, bounded concurrency, scope per request (§4.1, §4.2, §4.7)
    Rpc/CreditSubjects.cs                 the three subject constants, guarded by a read-the-spec test
    Rpc/CreditRequestValidator.cs         hand-rolled validation (§4.4)
    Rpc/CreditErrorMapper.cs              exception -> RpcError, BC27's transient/terminal discipline (§4.5)
    Rpc/RpcMeta.cs                        x-correlation-id / x-request-id extraction and refusal (§4.6)
  BillingHost.cs                          composition root — the FulfillmentHost shape (§14.3)
  Program.cs                              NEW — the first runnable Billing host
  Billing.csproj                          gains Cqrs, NATS.Net, Confluent.Kafka, Hosting, Options, Logging.Abstractions

src/Orders/Domain/Events/                 §8.2.2 — OrderDomainEvent renamed FactEvent (5 files)
src/Orders/Domain/Order.cs                §8.2.2 — one signature (TransitionTo's Func<FactEvent>)
src/Orders/Application/Ports/IFactPayloadMapper.cs        §8.2.3 — NEW
src/Orders/Infrastructure/Outbox/         §8.2 — OutboxWriter, OrderFactPayloadMapper, KafkaOptions, OutboxRelay,
                                          KafkaFactPublisher, OutboxRelayBackgroundService
src/Orders/Infrastructure/OrdersOutboxOptions.cs, OrdersOutboxServiceCollectionExtensions.cs   §8.2.3, §8.2.4
src/Fulfillment/Domain/Events/            §8.2.2 — StockDomainEvent renamed FactEvent (5 files)
src/Fulfillment/Domain/StockItem.cs       §8.2.2 — one signature (RecordOrderFact)
src/Fulfillment/Application/Ports/IFactPayloadMapper.cs   §8.2.3 — NEW
src/Fulfillment/Infrastructure/Outbox/    §8.2 — the same six as Orders'
src/Fulfillment/Infrastructure/FulfillmentOptions.cs, FulfillmentServiceCollectionExtensions.cs  §8.2.3, §8.2.4
src/Fulfillment/Presentation/StockRpcResponder.cs   §10.1 — the StopAsync fix, backlog id 50
src/SharedKernel/Money.cs                 §3.3 — Add/Subtract/Multiply become `checked` (BC30)

tests/Billing.UnitTests/                  NEW project (added to OrderToCash.sln)
tests/Billing.IntegrationTests/           EXISTING project, extended (phase-6 schema tests stay untouched)
tests/Orders.UnitTests/OutboxRelayParityTests.cs    NEW — OB1 over SEVEN files (§8.4)
tests/Orders.UnitTests/                   §10.2 payload/enum parity, §10.4 NatsHeaders guard, OutboxWriter ctor
tests/Orders.IntegrationTests/            §8.2.3 OutboxWriter ctor (6 files), §8.2.4 StandInSagaResponders, §10.5
tests/Fulfillment.UnitTests/              §10.1 shutdown test, §10.2 payload-parity extension
tests/Fulfillment.IntegrationTests/       §8.2.3 OutboxWriter ctor (2 files), §10.5 reply-shape (2 files)
tests/SharedKernel.UnitTests/MoneyTests.cs         §3.3 — the three overflow cases (BC30)
```

**No migration.** Every table this feature writes exists since phase 6: `credits`, `credit_items`, `outbox`, `processed_events` (`src/Billing/Infrastructure/Persistence/Migrations/20260901110439_InitialCreate.cs`). The schema was checked column by column against this design before it was written — §7.1 tables the result. **The implementer must not write a migration**; if a column looks absent, stop and report.

**Layering.** `Domain/` references only `OrderToCash.SharedKernel` and, for payload record types on the fact builders, `OrderToCash.Contracts.Facts` — the precedent `src/Orders/Domain/Events/` and `src/Fulfillment/Domain/Events/` already set. No `Domain/` namespace may reference `OrderToCash.Cqrs`, `Microsoft.EntityFrameworkCore`, `NATS.*`, `Confluent.Kafka` or `System.Text.Json`; `DomainPurityTests` and `CqrsDomainPurityTests` scan this assembly through `DomainAssemblies.All`. **`decimal` never appears** — every amount is `SharedKernel.Money`, `long` minor units, and `DomainDecimalTests` already covers it. This is the first #8 service whose domain is *entirely about money*, so that rule is load-bearing here rather than vacuous (§15, `L15`).

## 3. The domain

### 3.1 `BuyerCredit` — the aggregate root

```csharp
public readonly record struct CreditContext(DateTimeOffset OccurredAt, UniqueId CausationId);

public sealed record HoldRequest(OrderNumber OrderReference, Money Amount, UniqueId CorrelationId);

public abstract record HoldEvaluation
{
    public sealed record AlreadyHeld(Money HeldAmount)      : HoldEvaluation;  // BC7
    public sealed record CurrencyMismatch(string Expected)  : HoldEvaluation;  // BC4
    public sealed record OverLimit(Money AvailableCredit)   : HoldEvaluation;  // R39
    public sealed record Fits                               : HoldEvaluation;  // the ONLY case that reaches the port
}

public sealed class BuyerCredit : AggregateRoot
{
    public static BuyerCredit Reconstitute(BuyerCreditSnapshot snapshot);   // refuses a snapshot breaking B1 or B3
    public CreditLineReference Code { get; }
    public string RetailerCode { get; }
    public string CompanyCode { get; }
    public Money CreditLimit { get; }
    public Money AvailableCredit { get; }         // BC5 — CreditLimit − committedExposure
    public LedgerSummary Summary { get; }         // BC6

    /// PURE. No mutation, no event, no port. The single decision function; the
    /// application layer consults ICreditDecisionPort ONLY on Fits (§6.1). The
    /// case order is fixed by BC26 and asserted, not incidental.
    public HoldEvaluation EvaluateHold(HoldRequest request);

    /// Appends ONE `hold` entry and exactly one credit.approved.v1 whose
    /// availableCreditAfter is recomputed WITH the new entry (BC10). Throws
    /// CreditLimitExceededError unless EvaluateHold would answer Fits — a caller
    /// cannot bypass B1 by skipping the evaluation.
    public CreditLedgerEntry Approve(HoldRequest request, CreditContext ctx, Func<UniqueId> newId);

    /// Appends NO entry and exactly one credit.rejected.v1 carrying `reason`
    /// (R39, B1). Throws CreditRefusalMismatchError if `reason` is over_limit
    /// while the amount actually fits — a refusal must not lie about why.
    public void Refuse(HoldRequest request, CreditRejectionReason reason, CreditContext ctx);

    /// Appends ONE `release` entry for the order's outstanding exposure and
    /// exactly one credit.released.v1 with `reason`. Returns null — no entry, no
    /// fact — when the order has no outstanding exposure (BC11, B5).
    public CreditLedgerEntry? Release(OrderNumber orderReference, CreditReleaseReason reason, UniqueId correlationId, CreditContext ctx, Func<UniqueId> newId);

    /// Appends ONE `consume` entry of the order's active hold. Emits NOTHING —
    /// invoice.issued.v1 is feature 21's Invoice fact (R40, BC12). Throws
    /// NoActiveHoldError when the order holds nothing.
    public CreditLedgerEntry Consume(OrderNumber orderReference, CreditContext ctx, Func<UniqueId> newId);

    public IReadOnlyList<CreditLedgerEntry> AppendedEntries { get; }   // what the repository INSERTs — the loaded ones are never re-written (B2)
    public BuyerCreditSnapshot ToSnapshot();
}
```

**Invariant B1 lives here, not in the schema — and that was decided in phase 6.** `CreditConfiguration` carries no `CHECK`, deliberately: `B1` is a derived quantity over the whole ledger, not a value the `credits` row holds, and breaking it must produce a `credit.rejected.v1` **fact** rather than a raw provider error. This feature honours that: `Approve` throws unless the amount fits, `Reconstitute` refuses a snapshot whose `committedExposure` already exceeds `CreditLimit`, and nothing in `Infrastructure/` writes a ledger row except through `AppendedEntries` of an aggregate that has already enforced it.

**Invariant B2 (append-only) is enforced by shape, not by a rule.** `CreditLedgerEntry` exposes no setter and no mutating method; `BuyerCredit` exposes no way to reach a loaded entry; `EfCoreBuyerCreditRepository.SaveChangesAsync` only ever `Add`s `AppendedEntries` and never updates or deletes a `credit_items` row (§7.2). The `R37` domain test asserts the runtime half directly: asking the aggregate to reverse an entry appends a new `release` instead.

**What the aggregate holds, honestly.** Like `StockItem` and its `ReservedUnits`, `BuyerCredit` **preserves** `B1` rather than recomputing it from everything it can see. It is reconstituted with (a) the `credits` row, (b) `committedExposure` — one scalar, `BC5`'s two-term sum over the *whole* line, computed in SQL — and (c) the complete entry list of the **one order** the command names, which is all `B4`/`B5`/`BC7` need. It never loads the whole ledger. The consequence is checked where a drift would actually be visible: an integration test recomputes `availableCredit` from every row of `credit_items` after each committed operation and compares it with the fact's `availableCreditAfter`.

### 3.2 `CreditLedgerEntry` — the child entity

```csharp
public enum CreditEntryType { Hold, Consume, Release }

public sealed class CreditLedgerEntry : Entity
{
    public static CreditLedgerEntry Create(UniqueId id, OrderNumber orderReference, Money amount, CreditEntryType type, DateTimeOffset entryDate);
    public static CreditLedgerEntry Reconstitute(CreditLedgerEntrySnapshot snapshot);
    public OrderNumber OrderReference { get; }
    public Money Amount { get; }
    public CreditEntryType Type { get; }
    public DateTimeOffset EntryDate { get; }
    // No mutator of any kind — B2.
}
```

`Create` rejects a non-positive amount and a currency other than the line's (**B3**, checked by the aggregate, which is the only construction site). `CreditEntryTypes.Parse` refuses anything outside the closed set — the `ReservationStatuses` convention — so a corrupt `type` column is a loud failure rather than a silently-ignored row that would move `Σ` (§15, `L12`).

### 3.3 `CreditExposure.Summarise` — the crux, one pure function

This is the file the whole feature turns on, and it is deliberately the smallest one.

```csharp
public sealed record OrderExposure(
    string OrderReference,
    long Exposure,        // Σ hold − Σ release, minor units — what the order still ties up
    long OpenExposure,    // min(Σ consume, Exposure) — the invoiced portion
    long ActiveHold,      // Exposure − OpenExposure
    bool HasHoldEntry);   // BC7's idempotency predicate: a `hold` was recorded, whatever happened since

public sealed record LedgerSummary(
    IReadOnlyList<OrderExposure> ByOrder,
    long CommittedExposure,   // Σ Exposure over every order = Σ_line hold − Σ_line release
    long ActiveHolds,
    long OpenExposure);

/// PURE. The only place BC5 and BC6 are computed — by the aggregate and by the read side alike.
public static LedgerSummary Summarise(IReadOnlyList<CreditLedgerEntrySnapshot> entries);
```

**Why this formula and not `domain-model.md` §5.1's literal one.** The shared model writes `activeHold = Σhold − Σconsume − Σrelease applied to holds` and `openExposure = Σconsume − Σrelease applied to exposures`. The qualifier *"applied to holds"* is not computable: a `release` row carries a type, an amount and an order reference, and nothing that says which of the two quantities it unwinds. Worse, the naive un-qualified reading breaks **B5**: for an order cancelled before invoicing (`h`, then `r = h`, `c = 0`) it yields `openExposure = c − r = −h`, a negative exposure the model explicitly forbids. The two-term identity below is exact, needs no qualifier, and is what this assessment implements — **identically to #7**, whose gate ruled the same way (its row 2) and recorded it as promotion candidate 4 for a later shared pass that has not happened. #8 inherits the ruling; it does not re-derive it and it does not edit `domain-model.md`.

```
exposure(order)     = Σ hold(order) − Σ release(order)             -- B5 keeps this ≥ 0
openExposure(order) = min( Σ consume(order), exposure(order) )
activeHold(order)   = exposure(order) − openExposure(order)
committedExposure   = Σ_orders exposure(order) = Σ_line hold − Σ_line release
availableCredit     = creditLimit − committedExposure
```

Three consequences worth naming, because each is a requirement elsewhere:

1. **`consume` is numerically neutral by construction.** It appears in neither term of `availableCredit`. `R40` is therefore not a rule the code applies — it is a property of this function, and it cannot be broken by a future change to the consume path without changing this function.
2. **`availableCredit` needs no grouping at all.** It is a single scalar aggregate over `credit_items WHERE credit_id = @id` — no `GROUP BY`, an index-range scan on `IX_credit_items_credit_id_order_reference`. That is what makes the hold transaction (§5.5) cheap enough to run under an exclusive row lock.
3. **Only the *view* needs the per-order split**, and the view is a non-locking read outside any transaction (§7.3). The write path never pays for it.

**Grouping key.** `ByOrder` is grouped with `StringComparer.OrdinalIgnoreCase` and the key is the entry's stored `OrderReference`. The database matches `order_reference` under MS-SQL's server-default `SQL_Latin1_General_CP1_CI_AS`, i.e. **case-insensitively**; an ordinal in-memory grouping would split into two orders what one `WHERE` clause already merged into one row set, and `BC7`'s `HasHoldEntry` would then answer `false` for an order that does hold. #7 grouped with JavaScript `===` against MySQL's equally case-insensitive `utf8mb4_0900_ai_ci` and had the same latent disagreement; the alphabet is closed at the edge by §4.4's `^ORD-[0-9]{6,}$` (uppercase only) exactly as `FS19`/ledger `L3` closed it for product codes, and the comparer makes the residual unreachable rather than merely unlikely (`BC28`, §15 `L14`).

**Cost, stated rather than hidden.** `committedExposure` is `O(entries on the line)`, so it grows with a retailer's history — three rows per completed order. At demo and assessment volumes this is a few thousand rows behind a covering index inside a millisecond-scale transaction, and it buys the property that the append-only ledger is the *single* source of truth with nothing to drift against. The alternatives are recorded as rejected, with reasons, in §12.

**Arithmetic width — supplied, not argued (`BC30`, gate ruling 2026-09-05).** Every `Σ` is `long`, and `long` **wraps silently**: `Directory.Build.props` sets no `CheckForOverflowUnderflow`, so every `+` and `-` in the domain is an unchecked operation unless it says otherwise. The first pass of this spec argued the sum is bounded by construction — positive entry amounts, a `Reconstitute` that refuses a line already over its limit, and `~9.2 × 10^18` minor units needed to wrap — and recommended leaving it unguarded. **The gate overruled that**, on the ground that #8 has already shipped one money-width defect whose requirement text was satisfied exactly (`CLAUDE.md`'s ledger table, row 2) and that *"we reasoned it cannot happen"* is the sentence that preceded it. The obstacle the first pass named is also narrower than it stated: `Reconstitute`'s refusal blocks one **route** to an overflowing ledger, not the arithmetic, and the arithmetic is a `static` function anyone can call with any list.

Three lines, in order of how far they are from the caller:

1. **The summation raises.** `CreditExposure.Summarise` accumulates inside an explicit `checked` region and converts the failure into a domain error:

```csharp
public static LedgerSummary Summarise(IReadOnlyList<CreditLedgerEntrySnapshot> entries)
{
    try
    {
        checked
        {
            // every += and every − below is inside this region: the per-order
            // hold/release/consume accumulators, exposure(order), the committed
            // exposure across orders, and openExposure/activeHold.
            ...
        }
    }
    catch (OverflowException ex)
    {
        throw new CreditLedgerOverflowError(ex);
    }
}
```

   The accumulation is written as explicit `checked` loops and **not** as `Enumerable.Sum`. `Sum(IEnumerable<long>)` does happen to throw on overflow in the current BCL, but nothing in its signature says so, and *"the library did it"* is precisely the shape of answer this repository's ported-idiom ledger exists to refuse (§15 `L25`). `checked` regions do **not** propagate into called methods, so the region has to contain the arithmetic itself — which it does, because `Summarise` is the only place `BC5` and `BC6` are computed.

2. **`Money`'s own arithmetic raises.** `availableCredit = creditLimit − committedExposure` is a `Money.Subtract`, and `src/SharedKernel/Money.cs`'s `Add`, `Subtract` and `Multiply` are unchecked today. Making `Summarise` checked while leaving them unchecked would guard the ledger and not the quantity the ledger exists to produce, so all three become `checked` expressions in the same pass. This is a change to shared code: it is three expressions, it changes behaviour **only** on inputs that previously produced a wrong answer, and it is guarded by three named cases in `tests/SharedKernel.UnitTests/MoneyTests.cs` (`tasks.md` B13). Going one file beyond the letter of the ruling is deliberate and is recorded here so it is read rather than discovered.

3. **`Reconstitute` refuses the state** whose committed exposure already exceeds the limit (`tasks.md` B10). This was the first pass's only line and is now the **outer bound**: it makes an overflowing ledger unreachable through the aggregate, while (1) and (2) make it loud if it is ever reached another way.

The guard the ruling specifically asked for is `tasks.md` B12: a unit test that drives `CreditExposure.Summarise` **directly** with entry snapshots whose amounts overflow it. It needs no aggregate, no `Reconstitute`, no database and no fixture — `CreditLedgerEntrySnapshot` is a plain record, so two `hold` entries of `long.MaxValue / 2 + 1` are one line of test data.

### 3.4 The three fact builders

`Domain/Events/` mirrors `src/Fulfillment/Domain/Events/` exactly: an abstract **`FactEvent`** base carrying the seven envelope fields — the neutral name all three services adopt in §8.2.2, each in its own `Domain.Events` namespace, payload record types from `OrderToCash.Contracts.Facts`, `CorrelationId` = the order id (from `x-correlation-id`), `AggregateId` = the credit line's id (`domain-model.md` §7.2 facts 5/6/7 name `BuyerCredit` as the producing aggregate), `CausationId` and `OccurredAt` from `CreditContext`. `creditCode` is carried in all three payloads — optional in `CreditRejectedPayload`'s `required` list, always known here because the line was resolved before any refusal could be decided.

**`credit.rejected.v1` has exactly one builder and exactly one call site** — `Refuse`. There is no separate branch for an adapter refusal anywhere in the domain, the application layer, the outbox record or the RPC reply. That is `BC14` / `R44`, achieved structurally: a genuine `over_limit` fact and a `simulated_cents_rule` fact are produced by the same lines of code and differ in the one value that was passed in.

### 3.5 Domain errors

All extend `DomainError` with a stable `Code`: `CreditLimitExceededError` (`CREDIT_LIMIT_EXCEEDED`, carries requested and available), `CreditRefusalMismatchError` (`CREDIT_REFUSAL_MISMATCH`), `CreditReleaseUnderflowError` (`CREDIT_RELEASE_UNDERFLOW`), `NoActiveHoldError` (`NO_ACTIVE_HOLD`), `InvalidBuyerCreditSnapshotError` (`INVALID_BUYER_CREDIT_SNAPSHOT`), `FactAggregateMismatchError` (`FACT_AGGREGATE_MISMATCH`) and **`CreditLedgerOverflowError`** (`CREDIT_LEDGER_OVERFLOW`, wrapping the `OverflowException` as its inner exception — `BC30`, §3.3). §4.5 is where these codes become wire codes.

### 3.6 Invariants → where they are enforced

| Invariant | Enforced by | Proven by |
|---|---|---|
| **B1** `Σ holds + Σ exposure ≤ creditLimit` | `EvaluateHold` (decides), `Approve` (throws), `Reconstitute` (refuses) | `R37` domain unit, `BC9` integration |
| **B2** append-only | no mutator on `CreditLedgerEntry`; the repository only `Add`s `AppendedEntries` | `R37` domain unit, `BC10` integration |
| **B3** one currency per line | entries are constructed in the line's currency only; `EvaluateHold` answers `CurrencyMismatch` before `OverLimit` | `BC4`, `BC26` |
| **B4** at most one active hold per order | `EvaluateHold` answers `AlreadyHeld` on any recorded `hold` entry | `BC7` unit + integration |
| **B5** release never goes below zero | `Release` releases exactly `exposure(order)` and returns `null` when it is zero | `BC11` domain unit |

## 4. Presentation — one responder, three subjects

### 4.1 `CreditRpcResponder : BackgroundService`

One `BackgroundService` per **transport** (`CLAUDE.md`) — NATS is one transport, and three separate classes would triplicate the concurrency, scope and error-mapping logic. The class is a copy of `StockRpcResponder`'s shape **with the §4.7 shutdown correction applied**: three `await foreach` loops over `INatsConnection.SubscribeAsync<byte[]>`, a `SemaphoreSlim` acquired **before** the DI scope, one `IServiceScope` per request, tracked in-flight tasks, and `ProcessRequestAsync` / `DispatchAsync` split `internal` so unit tests can drive them with a fake `IDispatcher` and no NATS.

Per message: deserialise with `RpcJson` → validate (§4.4) → extract `RpcMeta` where required (§4.6) → resolve `IDispatcher` from a **fresh scope** → `SendAsync`/`QueryAsync` → `message.ReplyAsync(RpcJson.Serialize(reply))`. **The responder never throws and never leaves a request unanswered**: every path is wrapped and the catch replies a mapped `RpcErrorPayload` (§4.5).

Billing registers **no Kafka consumer** (§9), so this is the service's only transport class.

### 4.2 Subjects

`Presentation/Rpc/CreditSubjects.cs` holds `CreditHold = "billing.credit.hold"`, `CreditRelease = "billing.credit.release"`, `CreditList = "billing.credit.list"`, guarded by a read-the-spec-as-text unit test asserting each equals its `asyncapi.yaml` channel `address` — the `RpcSubjectsTests` / `StockSubjectsTests` instrument.

### 4.3 The wire — bare JSON, and why #8 has nothing to build

#7's largest gate row on this feature (its §4.3) was that `@nestjs/microservices`' NATS server treats an id-less bare request as an **event** and never replies, and wraps replies in a `{response, isDisposed, id}` packet — so Billing had to install a copied `BareJsonNatsDeserializer` / `BareJsonNatsSerializer` pair, becoming the **third** byte-identical copy of a file family nothing guarded.

**#8 has no such layer, and therefore no third copy.** `SubscribeAsync<byte[]>` yields the raw payload and `NatsMsg<byte[]>.ReplyAsync(byte[])` publishes the raw reply. The saving is real and belongs in the effort record; the **assertion is kept anyway** (`BC2`), because "the reply body has no `response`/`isDisposed`/`id` key and deserialises directly into the AsyncAPI reply schema" is a trilogy contract, not a framework artefact. #7's open point 19 (a messaging-family parity guard) therefore **does not arise in #8** — the family it would have guarded does not exist here.

The six request/reply records live in `Infrastructure/Messaging/Rpc/CreditRpcPayloads.cs` — Billing's own copy, not a reference to Orders' `SagaCommandPayloads.cs`, per the established rule that *RPC payloads live in the service that speaks them*. `CreditHoldRequestPayload.Amount` is a nested `{ amount, currency }` object, not a flattened pair: `asyncapi.yaml`'s `Money` schema declares `amount`, Orders' existing `SagaMoney(long Amount, string Currency)` already sends exactly that, and #7's review ruled on the same point (its deviation `D4`). **Following the generated contract over any design prose is the only correct choice**, and `BC23`'s parsed-from-the-spec test is what makes that mechanical rather than a matter of care (§10.2).

### 4.4 Validation

`CreditRequestValidator` is a static class in `Presentation/Rpc/`, mirroring `StockRequestValidator`, throwing `InvalidCreditRequestError` which the mapper turns into `VALIDATION_FAILED`. Rules, from `asyncapi.yaml`: `orderReference` matches `^ORD-[0-9]{6,}$`; `retailerCode`/`companyCode` 1–20 characters; `amount.amount` an integer `≥ 0`; `amount.currency` matches `^[A-Z]{3}$`; `page ≥ 1` (default 1) and `pageSize` 1–200 (default 25) on `credit.list`. The uppercase alphabets are what close §3.3's collation residual and §7.4's currency-comparison residual at the edge.

### 4.5 `CreditErrorMapper` — and the one code this service may never send

A pure function, the shape of `StockErrorMapper` but with this service's own cases:

| Thrown | `RpcError.code` | Class |
|---|---|---|
| `InvalidCreditRequestError` (validation, missing/malformed headers) | `VALIDATION_FAILED` | terminal — a malformed request never becomes well-formed |
| `CreditLineNotFoundError` | `NOT_FOUND` (+ `details.retailerCode`, `details.companyCode`) | terminal — `BC3` |
| `CreditCurrencyMismatchError` | `VALIDATION_FAILED` (+ `details.expected`, `details.received`) | terminal — `BC4` |
| `CreditReleaseUnderflowError`, `NoActiveHoldError` | `PRECONDITION_FAILED` (+ `details.code`) | terminal |
| **`CreditLedgerOverflowError`** | `DOMAIN_ERROR` (+ `details.code` = `CREDIT_LEDGER_OVERFLOW`) | **terminal**, and deliberately so — a ledger whose sums overflow will overflow again on every retry, so a retryable code would spin the saga forever against a state only an operator can repair (`BC30`) |
| any other `DomainError` | `DOMAIN_ERROR` (+ `details.code`) | terminal |
| `SqlException` 1205 / 1222 / any other; `DbUpdateConcurrencyException` | **`UNAVAILABLE`** | **transient — retried by the orchestrator** |
| `RequestDeadlineElapsedError` | `TIMEOUT` | transient |
| anything else | `INTERNAL_ERROR` | transient |

**`CONFLICT` is banned from this service's mapper** (`BC27`), for the same mechanical reason `FS21` banned it from Fulfillment's: `NatsSagaCommandsAdapter.IsTerminalRpcErrorCode` classifies `CONFLICT` as a terminal business rejection, so `SagaCommandDispatcher` would mark the `saga_commands` row `rejected` — a status `ClaimDueAsync` structurally never re-claims — and the order's saga would end permanently over a transient failure. The unit test reads the terminal set from `NatsSagaCommandsAdapter`'s own classification rather than retyping it, exactly as `StockRpcErrorMapperTests` already does.

**A business rejection is never an `RpcError`.** An over-limit or adapter-refused hold resolves with `outcome: "rejected"` and a `reason` (`saga.md` §7, `SO6`). Only `BC3`, `BC4` and a malformed request produce an error reply.

### 4.6 `RpcMeta` — the correlation and request headers

Billing copies `src/Fulfillment/Presentation/Rpc/RpcMeta.cs` verbatim with a `// COPY OF —` banner. Both headers are **required on `billing.credit.hold` and `billing.credit.release`** and refused with `VALIDATION_FAILED` before any dispatch when absent or malformed (`BC1`); `billing.credit.list` requires neither. The refusal is what keeps `R15` true: a fact published without the order id as its key would land on an arbitrary partition and break per-order ordering for the orchestrator.

### 4.7 Shutdown — the correction this feature makes, and back-fits (backlog id 50)

`StockRpcResponder.StopAsync` today is:

```csharp
var pending = _inFlight.Keys.ToArray();
if (pending.Length > 0) { await Task.WhenAll(pending).ConfigureAwait(false); }
```

`Task.WhenAll` **rethrows** the first faulted task's exception once every task has completed. A drained request whose `ReplyAsync` faults — a NATS connection torn down mid-shutdown is the ordinary way this happens — therefore propagates out of `StopAsync` and into `IHost.StopAsync`, which logs it and continues, but the exception is reported against the *host's* shutdown rather than against the request, and any `IHostedService` ordering guarantee a later feature relies on becomes conditional on a reply succeeding. It is cosmetic today and it is about to be copied a third time, which is why the backlog entry says *"worth doing BEFORE Billing and Despatch copy the responder"*.

**The specified behaviour, for both services (`BC22`):**

```csharp
public override async Task StopAsync(CancellationToken cancellationToken)
{
    await base.StopAsync(cancellationToken).ConfigureAwait(false);

    var pending = _inFlight.Keys.ToArray();
    if (pending.Length == 0) { return; }

    // Wait for EVERY in-flight request — the drain is the point (design.md
    // §4.7) — but observe each task's outcome individually. Task.WhenAll
    // rethrows the first fault, which would abort the host's shutdown
    // sequence over one request whose reply could not be delivered.
    var completion = await Task.WhenAll(pending.Select(WrapAsync)).ConfigureAwait(false);
    ...
}

private static async Task<Exception?> WrapAsync(Task task)
{
    try { await task.ConfigureAwait(false); return null; }
    catch (Exception ex) { return ex; }
}
```

Each captured exception is logged once at `Warning` with the responder's name and the count of faulted requests; `StopAsync` then returns normally. **The drain is unchanged** — every in-flight task is still awaited to completion before `StopAsync` returns, and that half is what the guard test must also pin, because a "fix" that stopped waiting would satisfy the fault-isolation half perfectly.

The same edit lands in `src/Fulfillment/Presentation/StockRpcResponder.cs`, and the same named test is written in both unit projects. That is the whole of backlog id 50: **two production edits, two tests, both armed.**

## 5. The application layer — the existing `src/Cqrs` dispatcher

### 5.1 Messages and handlers

| Subject | Message | Handler | Transactional? |
|---|---|---|---|
| `billing.credit.hold` | `HoldCreditCommand : ICommand<CreditHoldReplyPayload>` | `HoldCreditCommandHandler` → `CreditHoldService.HoldAsync` | yes — §5.5 |
| `billing.credit.release` | `ReleaseCreditCommand : ICommand<CreditReleaseReplyPayload>` | `ReleaseCreditCommandHandler` → `CreditReleaseService.ReleaseAsync` | yes — §5.6 |
| `billing.credit.list` | `ListCreditQuery : IQuery<CreditListReplyPayload>` | `ListCreditQueryHandler` → `ICreditReadPort.ListAsync` | no — three plain `SELECT`s |

The dispatcher is binding in all six services (`CLAUDE.md`, Phase 8 gate ruling) — not reopened. `AddDispatcher(Assembly.GetExecutingAssembly())` runs **after** every port is registered, so a missing or duplicated handler is a `DispatcherValidationException` at boot. The two commands carry `CorrelationId` and `RequestId` as `UniqueId`; the query carries neither. The handler classes are thin delegations so `CreditHoldService` and `CreditReleaseService` stay plain classes a unit test can `new` with fakes — the split `StockReservationService` already uses.

**No `IEventHandler` and no in-process fan-out.** Billing owes no post-commit in-process hop: its post-commit obligation is the relay's, and durability never depends on an in-memory bus. The command dispatch is awaited by the responder, so "reply after commit" is structural.

### 5.2 Ports

```csharp
public interface IBuyerCreditRepository
{
    /// §5.5 steps 1-3: an exclusive lock on the `credits` row of (retailerCode, companyCode);
    /// then, under that lock, the BC5 committed-exposure scalar and the complete entry list of
    /// `orderReference`. Returns null when no credit line exists (BC3) — the caller turns that
    /// into CreditLineNotFoundError, and no transaction has written anything.
    Task<BuyerCredit?> LockForOrderAsync(string retailerCode, string companyCode, OrderNumber orderReference, CancellationToken ct);

    /// Adds `credit.AppendedEntries` (never an UPDATE, never a DELETE — B2), then drains
    /// `credit.PullDomainEvents()` into outbox rows, then SaveChangesAsync — all inside the
    /// ambient transaction. Never opens its own (R13). The `credits` row is NEVER written.
    Task SaveChangesAsync(BuyerCredit credit, CancellationToken ct);
}

public interface ICreditReadPort   // never locks, never mutates, no transaction
{
    Task<CreditListReplyPayload> ListAsync(CreditListRequestPayload query, CancellationToken ct);
}
```

There is **no `tx` parameter** anywhere. #8's unit of work opens a transaction on the scoped `DbContext` and every collaborator resolved from the same scope enlists automatically — the same shape `IOrderRepository` and `IStockItemRepository` already have, and a real simplification over #7's Drizzle-shaped `TransactionContext`.

### 5.3 The transactional units

`CreditHoldService.HoldAsync`: `unitOfWork.ExecuteAsync(ct => LockForOrderAsync → null ⇒ throw CreditLineNotFoundError → EvaluateHold → AlreadyHeld / CurrencyMismatch short-circuit → OverLimit ⇒ Refuse(over_limit) → Fits ⇒ decision.Decide(...) ⇒ Approve | Refuse(reason) → SaveChangesAsync → map the outcome to a reply)`. The reply is built from the domain outcome inside the delegate but **returned only after `ExecuteAsync` resolves**, so a rollback can never have produced a success reply. `ctx = new CreditContext(clock.UtcNow, command.RequestId)` (`BC1`, `R12`).

`CreditReleaseService.ReleaseAsync`: §5.6 — same lock protocol, `Release(orderReference, order_cancelled, ...)`, `released: false` and no write when the aggregate returns `null`.

A **business rejection is a resolved reply, never a throw** (`saga.md` §7, `SO6`).

### 5.4 Application errors

`CreditLineNotFoundError` (`BC3`) and `CreditCurrencyMismatchError` (`BC4`) live in `Application/`, not `Domain/`: neither is a statement about a credit line's state, both are contract violations of the incoming command, and the domain layer has no vocabulary for *"the pair you named does not exist"*. This mirrors `src/Fulfillment/Application/StockApplicationErrors.cs`.

### 5.5 The hold transaction, and the lock protocol under `READ_COMMITTED_SNAPSHOT ON`

Inside one `IUnitOfWork.ExecuteAsync` (`IsolationLevel.ReadCommitted`, stated explicitly), in this order and nothing else:

```sql
-- 1. claim the credit line — the ONE row this transaction ever locks
SELECT id, code, retailer_code, company_code, credit_limit, currency_code, created_at, updated_at
FROM   dbo.credits WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
WHERE  retailer_code = @retailer AND company_code = @company;
-- no row -> ROLLBACK, CreditLineNotFoundError -> RpcError NOT_FOUND (BC3). Nothing written, no fact.

-- 2. the whole line's committed exposure — ONE scalar, no GROUP BY (BC5)
SELECT COALESCE(SUM(CASE type WHEN 'hold' THEN amount WHEN 'release' THEN -amount ELSE 0 END), 0)
FROM   dbo.credit_items WITH (UPDLOCK, HOLDLOCK)
WHERE  credit_id = @id;

-- 3. the subject order's entries — index seek on IX_credit_items_credit_id_order_reference (B4/B5/BC7)
SELECT id, credit_id, order_reference, amount, type, credit_date, created_at, updated_at
FROM   dbo.credit_items WITH (UPDLOCK, HOLDLOCK)
WHERE  credit_id = @id AND order_reference = @orderReference;

-- 4. domain: EvaluateHold(request) -> AlreadyHeld | CurrencyMismatch | OverLimit | Fits
--    Fits ONLY: consult ICreditDecisionPort (§6.1)
--    approve -> Approve(...)   | refuse -> Refuse(..., reason)   | OverLimit -> Refuse(..., over_limit)

-- 5. approved: INSERT credit_items (one `hold` row) + INSERT outbox (credit.approved.v1)
--    rejected: INSERT nothing into credit_items       + INSERT outbox (credit.rejected.v1)
--    already_held / currency_mismatch: nothing written at all
-- COMMIT
```

Four things about this differ from #7's `SELECT … FOR UPDATE` under InnoDB `REPEATABLE READ`, and each is load-bearing.

- **The hint is `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`, and it is not optional.** `READ_COMMITTED_SNAPSHOT` is **ON** for all four databases (`infra/mssql/init/01-create-databases.sql`), so an **un-hinted read under `IsolationLevel.ReadCommitted` takes no lock at all** and returns a row version. Step 1 would then not serialise two concurrent holds against one line, and steps 2 and 3 would read the pre-image of a competitor's uncommitted `hold` — the `Σ` and the `already_held` predicate would both be computed from a snapshot that is already false. `UPDLOCK` overrides row versioning for that reference; `HOLDLOCK` additionally takes a key-range lock, so step 1's answer for a **missing** `(retailer, company)` pair is stable for the transaction and steps 2/3 block a concurrent insert into the range rather than racing it.
- **Steps 2 and 3 are locking reads for a *different* reason than #7's.** #7 made them `FOR UPDATE` to remove the "when does InnoDB establish the consistent-read view" argument — a correct-but-subtle isolation question. Here the question is not subtle and the answer is not benign: under RCSI an un-hinted read is a **statement-scoped snapshot**, so it does not merely read an old view, it reads one taken *after* the competitor's write began and before it committed. The hint is what makes the read current. This is the same property `L1` in `specs/fulfillment_stock/design.md` §15 named, and it is named again here because Billing's rows are different rows.
- **Why one row lock is enough, and why no deadlock is possible.** A credit line is a single aggregate in a single row. A hold or release transaction locks exactly **one** `credits` row and holds it for the whole unit of work; two transactions can only ever contend on the *same* row, and a lock-ordering cycle needs at least two. Fulfillment had to argue **F3** against `domain-model.md` §8 rule 6 (`fulfillment_stock/design.md` §4.2); **Billing satisfies rule 6 literally** — one transaction mutates exactly one aggregate instance plus its outbox records. That is a genuinely stronger position and it is worth stating, because a reviewer arriving from Fulfillment will look for the same argument and should find its absence explained.
- **Deadlock is not assumed impossible.** The protocol makes the known shape unformable, but MS-SQL can still pick a victim (1205) for reasons outside this design's control. A deadlock victim is **transient** and is mapped accordingly (`BC27`, §4.5) — never to `CONFLICT`.

**Why the transaction is short.** Three statements, one pure evaluation, one synchronous port call (the bound adapter is pure — §6.1 forbids I/O inside the lock) and at most two inserts. No NATS, no Kafka, one `IClock.UtcNow` read.

### 5.6 The release transaction

Identical protocol, one statement shorter in effect: steps 1–3 as above, then `Release(orderReference, order_cancelled, correlationId, ctx, newId)`. If the aggregate returns `null` — no outstanding exposure — **nothing is written and no fact is recorded**, and the reply is `released: false` with the line's current available credit. There is deliberately **no** non-locking pre-read of the `Fulfillment.stock.release` kind (`fulfillment_stock/design.md` §4.4 step 0): that pre-read exists to avoid opening a transaction for an order with no rows at all, and here the decision needs the `credits` row anyway to answer `BC3` and to report `availableCreditAfter`. One protocol, not two.

### 5.7 Responder idempotency — the keys, stated once

| Command | Idempotency key (`saga.md` §2) | What a repeat observes | Reply | Fact |
|---|---|---|---|---|
| `credit.hold` | `orderReference` | **any** `hold` entry for the order on this line, whatever its net | `already_held` + the recorded `heldAmount` + current `availableCredit` | none |
| `credit.hold` | `orderReference` | a previously **rejected** hold — nothing was recorded (**B1**) | re-evaluated from scratch (`BC8`) | possibly a second `credit.rejected.v1` |
| `credit.release` | `orderReference` | exposure already zero — never held, or already released | `released: false` + current `availableCreditAfter` | none |
| `credit.list` | — (read) | — | — | none |

**`heldAmount` on an `already_held` reply is the amount of the recorded `hold` entry, not the currently outstanding exposure** — the reply's job is *"I already handled this order's hold, and here is what I recorded"*. `availableCredit` is always current. The one case where they differ is an order whose hold was released by a cancellation and whose `credit.hold` is then re-issued by the sweeper; `BC7` refuses to re-acquire, exactly as `FS5` refuses to re-reserve released stock. Cross-service consistency was #7's deciding argument (its gate row 4) and it is inherited, not re-litigated.

**`x-request-id` is not the idempotency key.** `saga.md` fixes the key as `(orderReference, operation)`, and a repeat must behave identically whether the retry came from the in-line policy (same row id) or from an operator re-running the step with a new row. The header is the *causation* carrier (`BC1`), nothing more.

**Evaluation precedence is fixed and asserted** (`BC26`): `AlreadyHeld` → `CurrencyMismatch` → `OverLimit`. #7's review found this precedence real, defensible and pinned by nothing, and asked for it to be written down so #8 and #9 would not order the two checks differently. It is written down here and it carries a named test.

**`BC8`'s second rejection fact is safe, and it is safe by machinery that already exists.** A rejection records nothing, so a re-issued rejected hold is re-evaluated and emits a *new* `credit.rejected.v1` with a distinct `eventId`. `saga.md` §6's redelivery table covers exactly that row, and `EfCoreSagaCommandStore.EnqueueAsync` has caught the `(order_id, command)` duplicate-key `SqlException` and returned `EnqueueOutcome.AlreadyEnqueued` since phase 8 — #8 never had the crash-loop #7 fixed as `FS1`. A fourth `credit_items` type recording refusals is rejected (§12).

## 6. The credit-decision port — feature 20's seam, fixed now

### 6.1 The contract

```csharp
/// Everything an adapter may see. It is told the amount and the line's state; it is NOT
/// given the aggregate, the repository, the transaction or the clock.
public sealed record CreditDecisionRequest(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string CreditCode,
    long AmountMinorUnits,
    string Currency,
    long AvailableCreditMinorUnits);   // BEFORE the hold, AFTER the aggregate found it fits

public abstract record CreditDecision
{
    public sealed record Approve : CreditDecision;
    /// `over_limit` is deliberately unconstructible here: it is the aggregate's word,
    /// and only the aggregate may say it (BC14).
    public sealed record Refuse(AdapterRejectionReason Reason) : CreditDecision;
}

/// The two simulator reasons ONLY — `over_limit` is absent by construction.
public enum AdapterRejectionReason { SimulatedCentsRule, SimulatedFailureRate }

public interface ICreditDecisionPort
{
    /// Called ONCE per hold, and ONLY when the aggregate has already answered Fits (BC13).
    /// MUST NOT perform I/O — it runs inside the credit line's row lock.
    ValueTask<CreditDecision> DecideAsync(CreditDecisionRequest request, CancellationToken ct);
}
```

**#7 expressed the exclusion as `Exclude<CreditRejectionReason, 'over_limit'>`, a structural type operation C# does not have.** #8 achieves the same guarantee with a **separate closed enum** whose members are the two simulator reasons, plus a total mapping `AdapterRejectionReason → CreditRejectionReason` in one place. An adapter therefore *cannot type* `over_limit`, which is the property `R44` needs; the difference is that #8's guard is a distinct type rather than a subtraction of one, and `BC14`'s "type half" test is a compile-level assertion in the same spirit (a test that would not compile if the enum gained the member, expressed as an exhaustive `switch` over `AdapterRejectionReason` that the compiler forces to be updated). Ledger row `L24`.

### 6.2 Why the ordering is the guarantee

`R44` requires that the simulator cannot bypass `R37` — a genuine over-limit rejection must stay reachable with the simulator bound. Three approaches were considered and #7's ruling is inherited:

| Approach | Why not / why yes |
|---|---|
| Trust the adapter not to approve over-limit holds | A guarantee by discipline, which is what `R44` exists to rule out |
| Give the adapter the aggregate and let it call `Approve` | The invariant survives (it throws) — but the adapter would own domain vocabulary and the failure would be an exception rather than a decision |
| **Evaluate `B1` first; consult the port only on `Fits`; make `over_limit` untypeable by an adapter** | **Chosen.** An adapter is structurally incapable of approving an over-limit hold, because it is never asked about one. It can only ever narrow approvals, never widen them |

`BC13`'s unit test is the direct probe: a recording fake port, driven with an over-limit request, must record **zero** calls and still yield a `credit.rejected.v1` with `reason: over_limit`.

### 6.3 What is bound today, and what feature 20 changes

`Infrastructure/Credit/AlwaysApproveCreditDecision.cs` — a pure, dependency-free class returning `CreditDecision.Approve`. It is registered in `BillingServiceCollectionExtensions` as

```csharp
services.AddSingleton<ICreditDecisionPort, AlwaysApproveCreditDecision>();
```

**Feature 20's entire footprint is that one line plus one new file.** It adds `Infrastructure/Credit/SimulatorCreditDecision.cs` (the `.99` rule, `CREDIT_FAILURE_RATE`, `R43`'s start-up validation) and changes the registration. No `Domain/`, `Application/` or `Presentation/` file changes, no port, no payload record, no fact builder, and no test of this feature changes. `BC15` asserts the smaller half (the adapter approves everything); this design records the larger half so feature 20's reviewer can check it as a diff.

**`CREDIT_FAILURE_RATE` is deliberately not introduced here** — it is feature 20's, and adding it now would put an unused knob in the environment that `R43`'s start-up validation does not yet police.

### 6.4 Indistinguishability, concretely

`R44` asks that a simulated and a genuine rejection be indistinguishable downstream except by `reason`. In this design that is not a property tested at three levels — it is a property of there being **one** `Refuse`, **one** `CreditRejected` builder, **one** outbox row shape and **one** `rejected` reply branch. `BC14`'s named test is a *positive assertion of sameness*: build both facts from one fixture and assert envelope and payload equality after normalising `EventId`, `OccurredAt` and `Reason`. Feature 20 then flips `R44`'s own integration row end to end.

**#7 was rejected on exactly this seam and the rejection is inherited as prevention.** Its defect `D1`: deleting the `refuseHold(...)` call from the port-refusal branch left the reply correct, the type-check clean and **all 56 unit tests green**, because every integration harness bound an always-approving adapter and no test anywhere drove the handler with a refusing port. `tasks.md` C7 names the fake, the assertion and the arming, and `tasks.md` C8 arms the two payload corruptions the reviewer's later probes `W1` and `W3` found (`reason` wrong; `requestedAmount` wrong — the second **survived** #7's fix and was recorded as its nit `N5`).

## 7. Persistence — the EF Core adapters

### 7.1 The phase-6 schema is sufficient — checked, not assumed

| This design needs | Exists as | Where |
|---|---|---|
| one row per `(retailer_code, company_code)`, unique | `credits` + unique index on `(retailer_code, company_code)` | `CreditConfiguration` |
| a unique business reference | unique index on `credits.code`, `nvarchar(30)` | `CreditConfiguration` |
| the limit as integer minor units, no narrowing | `credit_limit` **`bigint`** | migration `20260901110439_InitialCreate.cs:22` |
| the line's currency | `currency_code` `char(3)`, required | `CreditConfiguration` |
| the ledger, one row per movement | `credit_items` (`order_reference`, `amount`, `type`, `credit_date`) | `CreditItemConfiguration` |
| the ledger amount as integer minor units | `amount` **`bigint`** | migration `…:113` |
| the "movements of this line for this order" seek | index `(credit_id, order_reference)` | `CreditItemConfiguration` |
| ledger → line referential integrity | FK `credit_id → credits.id`, `ON DELETE NO ACTION` | `CreditItemConfiguration` |
| the outbox, with `seq IDENTITY` publication order | `outbox` | `OutboxMessageConfiguration` |
| `processed_events` | present (unused by this feature — §9) | `ProcessedEventConfiguration` |

Nothing is missing, and **both money columns are already `bigint`** — feature 44 widened them and `tests/Billing.IntegrationTests/NoMoneyColumnIsIntTests.cs` enumerates every remaining `int` column and names why each is legitimately not money. `tests/Billing.IntegrationTests`' existing phase-6 tests stay untouched and must stay green.

### 7.2 `EfCoreBuyerCreditRepository`

- **`LockForOrderAsync`** issues §5.5's three statements through `FromSqlInterpolated`, **tracked** for the `credits` row (so a stray change would be visible) and for the entry rows. Both statements name **every mapped column literally** — `FromSqlInterpolated` requires the full projection, and an interpolation hole would become a bound parameter rather than a column list, exactly as `OutboxRelay.ClaimColumnNames` documents. A `…ProjectionTests` case compares those literal lists against the `IEntityType`'s mapped properties mechanically — the E7 instrument already in the repository. The scalar of step 2 is executed with `SqlQueryRaw<long>` (or an equivalent that keeps the hint), never `db.CreditItems.SumAsync(...)`, because LINQ composition would drop the table hint and hand back an unlocked snapshot read (§15 `L7`).
- **`SaveChangesAsync`** adds one `CreditItem` row per `AppendedEntries` member, drains `PullDomainEvents()` into outbox rows, then calls `DbContext.SaveChangesAsync`, clearing the domain events only after everything above returned (`OI9`: clearing early loses the events on a rollback). Outbox rows are inserted **one awaited statement at a time**, copied verbatim from `EfCoreOrderRepository.InsertOutboxRowAsync` together with its comment — EF Core's SQL Server provider does not preserve `Add` order when assigning `IDENTITY` values, and `seq` is the entire publication-order guarantee (§15 `L16`).
- **The `credits` row is never written.** Nothing in this feature changes a credit limit. The repository contains no `UPDATE` and no `DELETE` against either table, which is `B2` made mechanical.
- **No upsert is rendered anywhere.** Every row this feature writes is an `INSERT` of a new ledger entry or a new outbox row; nothing is "insert-or-update". `tasks.md` forbids `IF NOT EXISTS … INSERT` and `MERGE` in this service and tells the reviewer to grep for both (§15 `L17`, and feature 45's defect).
- The repository **drains**; the service never does.

### 7.3 `EfCoreCreditReadRepository`

Three queries, no transaction, no hint — under RCSI these are versioned reads that block nobody:

1. the page of `credits` — optional `retailerCode`/`companyCode` filters, `ORDER BY retailer_code, company_code`, `Skip`/`Take`;
2. `CountAsync` over the same filter, for `PageInfo.total`;
3. one read of `credit_items` restricted to the page's `credit_id` values, returning each entry's `(credit_id, order_reference, amount, type)`.

The three amounts of each `CreditView` are folded by the **same** `CreditExposure.Summarise` the aggregate uses — one implementation of `BC5`/`BC6`, two callers. `availableCredit` is `creditLimit − committedExposure` and is never read from a column, because there is no column.

### 7.4 Instants, currency and enum tokens across the row boundary

`BuyerCreditRowMapper` follows the convention `OrderRowMapper` fixed and documents it in the same words: **`value.UtcDateTime` to write, `new DateTimeOffset(value, TimeSpan.Zero)` to read.** `datetime2(3)` carries no offset and EF Core hands back `DateTimeKind.Unspecified`; `new DateTimeOffset(unspecified)` applies the **machine's local offset**, so a CI runner in `Europe/Madrid` would read back an instant two hours earlier than it wrote. `CreditLedgerEntry.EntryDate` is the first #8 child entity that carries a date across this boundary — `ReservationSnapshot` carries none — so the convention is stated here rather than assumed, and `BC24` is an integration test that runs the round trip under a non-UTC `TZ` (§15 `L11`).

`currency_code` is `char(3)` under a case-insensitive collation while `Money.EnsureSameCurrency` compares with `StringComparison.Ordinal`. The seed writes uppercase, §4.4's validator refuses anything but `^[A-Z]{3}$` on the request, and `Money`'s own constructor refuses a malformed code — so the two comparisons cannot disagree on any value that reaches the aggregate. Recorded rather than assumed (§15 `L13`).

`type` is stored as the lowercase tokens `hold` / `consume` / `release` — the values `domain-model.md` §5.1 and the seed both use — and parsed through `CreditEntryTypes.Parse`, which **throws** on anything outside the closed set. An unparsed row must never be silently skipped: a skipped row would move `Σ` and therefore move `availableCredit` (§15 `L12`).

## 8. The outbox: the third copy, and the refactor it makes possible

### 8.1 What Billing copies

`IUnitOfWork` + `EfCoreUnitOfWork`, `IClock` + `SystemClock`, `IFactPublisher` + `PublishableFact`, `IFactPayloadMapper`, `OutboxWriter`, `OutboxEnvelopeMapper`, `OutboxRelay`, `OutboxRelayOptions`, `OutboxRelayBackgroundService`, `KafkaFactPublisher`, `KafkaOptions` — taken from `src/Orders/` after §8.2's refactor, at which point **seven of them are byte-identical** and the only per-copy edits are a `// COPY OF — src/Orders/Infrastructure/<path>.cs` banner, the `namespace` line and the `using` lines. Same claim (`WITH (UPDLOCK, READPAST, ROWLOCK)`, `ORDER BY seq`), same stamp-after-acknowledgement, same publish timeout, same self-scheduling loop.

`CLAUDE.md`: *"The only shared runtime code is `src/SharedKernel`, `src/Contracts` and `src/Cqrs`. Nothing else is shared."* A shared outbox project would be a fourth, and it would couple three services' release cadence for ~300 lines over a table that database-per-service already duplicates. #7 ruled identically for the same reason.

### 8.2 The service-neutral refactor — at #7's full scope

`specs/fulfillment_stock/design.md` §8.3 deferred this to *"the feature that creates the third copy"*, with a mechanical reason: `OutboxRelay` names `OrdersDbContext` in its constructor, so a byte-identical copy is impossible **without first editing the canonical**. This feature creates the third copy and therefore owns both halves.

The first pass of this spec proposed doing only the cheap part — three files, two using-aliases and a comment reword — and narrowing `OB1` from #7's seven files to five. **The gate overruled that on 2026-09-05**: reusing `BC17`'s id claimed #7's obligation at full scope, so the two excluded files come into the set and the structural work they need is the work. §8.2.1 is the cheap part as first specified; §8.2.2 – §8.2.4 are what the ruling adds.

#### 8.2.1 The using alias — `OutboxRelay`, `KafkaFactPublisher`, `OutboxRelayBackgroundService`

A `grep` of those three files for the five service tokens, outside `using` and `namespace` lines, returns exactly **three** hits:

| File | Hit | After |
|---|---|---|
| `OutboxRelay.cs:33` | `OrdersDbContext db,` | `WriteModelDbContext db,` — each service's relay file gains a **using alias** `using WriteModelDbContext = OrderToCash.<Service>.Infrastructure.Persistence.<Service>DbContext;` in place of its plain namespace `using` |
| `KafkaFactPublisher.cs:70` | `OrdersFactTopic.Name` | `FactTopic.Name` — each service's publisher gains `using FactTopic = OrderToCash.<Service>.Infrastructure.Outbox.<Service>FactTopic;` |
| `OutboxRelayBackgroundService.cs:33` | a **comment** saying *"the OrdersDbContext and its change…"* | reworded to *"the write model's DbContext and its change…"* |

**Two using-alias lines and one comment reword per service**, zero behavioural change, zero DI change (`WriteModelDbContext` *is* the concrete type, so `AddScoped<OutboxRelay>()` resolves exactly as before). A C# using alias at a fixed per-file position is the direct rendering of #7's `export type WriteModelDb = <Service>Db;` — indirection through a name each service defines for itself, the mechanism the parity instrument's `using` whitelist already trusts.

**Rejected alternative, recorded because it is the obvious one:** widen the constructor to `DbContext db` and register `services.AddScoped<DbContext>(sp => sp.GetRequiredService<XDbContext>())`. It works, but it costs a body change (`db.OutboxMessages` → `db.Set<OutboxMessage>()`, twice), a new DI registration in three services, and a `DbContext` service token that means "whichever one" — a resolution ambiguity waiting for the first service with two contexts. The alias costs one line and changes nothing.

#### 8.2.2 `FactEvent` — the neutral domain-event base

`OutboxWriter`'s body casts the incoming `IDomainEvent` to the service's own base record, and the three names differ: `OrderDomainEvent`, `StockDomainEvent`, `CreditDomainEvent`. A using alias **cannot** fix this one the way §8.2.1 fixes the DbContext, because the name also appears in the `IFactPayloadMapper` signature the writer calls (§8.2.3) and in each service's own event subtypes — an alias would make the writer identical while leaving three different public shapes behind it, which is the divergence the guard is for rather than a rendering of it.

So the type is **renamed**, in each service's own `Domain.Events` namespace, to **`FactEvent`**:

```csharp
namespace OrderToCash.<Service>.Domain.Events;

public abstract record FactEvent(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt) : IDomainEvent, IDomainEventEnvelope
{
    public abstract string EventType { get; }
}
```

The name is not invented for the occasion: this repository's outbox vocabulary is already `FactCatalog`, `IFactPublisher`, `PublishableFact`, `<Service>FactTopic`, `otc.<service>.facts.v1`. `FactEvent` is *"the domain event that becomes a published fact"*, which is exactly what the writer's `FactCatalog.PayloadTypesByEventType` membership check already requires of it. Three namespaces each declare their own `FactEvent`; nothing is shared, and `using OrderToCash.<Service>.Domain.Events;` — a line the guard strips — is what selects which.

**Footprint, counted rather than estimated.** `grep -rn 'OrderDomainEvent' src tests` returns **13** hits in 12 files; `StockDomainEvent` returns **9** in 8. Every one is listed by name in `tasks.md` A1 and A2. Two of the thirteen are prose (`src/Orders/Application/Ports/UnknownConsumerNameError.cs`'s doc comment and `src/Fulfillment/Domain/Events/StockDomainEvent.cs`'s "the `OrderDomainEvent` shape" remark) and are reworded, not renamed. The base files are renamed to `FactEvent.cs` — `.editorconfig` requires a file to match its type.

No architecture test selects any of these by name: `tests/Architecture.Tests/` matches on assemblies and namespaces (`DomainAssemblies.cs`, `DomainPurityTests`, `CqrsDomainPurityTests`), never on `*DomainEvent`. Checked before the rename was specified.

#### 8.2.3 `IFactPayloadMapper` — the per-service seam

`OutboxWriter`'s other per-service call is `OrderFactPayloadMapper.ToPayload(...)` / `StockFactPayloadMapper.ToPayload(...)` — a **static** call on a differently-named class, which no using alias can neutralise without leaving a static dependency the writer cannot be tested against. The gate named the remedy: a per-service port, declared where the other application ports live.

```csharp
// src/<Service>/Application/Ports/IFactPayloadMapper.cs
using OrderToCash.<Service>.Domain.Events;

namespace OrderToCash.<Service>.Application.Ports;

/// <summary>
/// The seam the outbox writer dispatches through to turn a domain event into
/// its OrderToCash.Contracts.Facts.Payloads record. One implementation per
/// service, in Infrastructure/Outbox/, because the mapping is this service's
/// own; the writer that calls it is byte-identical in all three (design.md
/// §8.2.3). Pure: no I/O, no clock, no DI graph of its own.
/// </summary>
public interface IFactPayloadMapper
{
    /// <summary>Throws, naming the CLR type and the eventType, for an event this service does not map.</summary>
    object ToPayload(FactEvent factEvent);
}
```

Each existing mapper stops being a `static class` and becomes a `sealed class : IFactPayloadMapper` whose `ToPayload` is an instance method over the same `switch` expression — the bodies of `OrderFactPayloadMapper` and `StockFactPayloadMapper` are otherwise untouched, which matters because `tests/Orders.IntegrationTests/OutboxWireParityTests.cs` and `tests/Contracts.UnitTests/GoldenEnvelopeParityTests.cs` assert the exact bytes those mappers produce against twelve captured #7 envelopes. **That is the refactor's safety net and it is named here so the implementer knows what is watching**: if the port refactor changed a single payload key or value, those two suites fail.

Registration is `services.AddSingleton<IFactPayloadMapper, <Service>FactPayloadMapper>()` — stateless, no scoped dependency, and a singleton injected into the scoped `OutboxWriter` is the legal direction under `ValidateScopes = true`. The canonical writer becomes:

```csharp
public sealed class OutboxWriter(IClock clock, IFactPayloadMapper payloadMapper)
{
    public IReadOnlyList<OutboxMessage> BuildRows(IReadOnlyList<IDomainEvent> domainEvents)
    {
        var rows = new List<OutboxMessage>(domainEvents.Count);
        var createdAt = clock.UtcNow.UtcDateTime;

        foreach (var domainEvent in domainEvents)
        {
            var factEvent = (FactEvent)domainEvent;

            DomainEventEnvelope.Validate(factEvent);

            if (!FactCatalog.PayloadTypesByEventType.ContainsKey(factEvent.EventType))
            {
                throw new InvalidOperationException(
                    $"Outbox writer refuses to store a fact whose eventType '{factEvent.EventType}' is not in the declared FactCatalog.");
            }

            var payload = payloadMapper.ToPayload(factEvent);

            rows.Add(new OutboxMessage { /* … unchanged, factEvent.* … */ });
        }

        return rows;
    }
}
```

The XML doc goes with it, and must be **neutral prose**: Orders' current `<see cref="Persistence.EfCoreOrderRepository.SaveChangesAsync"/>` becomes `<c>SaveChangesAsync</c>` on "the aggregate repository", because a `cref` to a service-specific type would not compile in the other two copies and would fail the guard's service-token scan even if it did.

**The cost is a second constructor argument, and it is paid in tests.** `new OutboxWriter(clock)` appears at **21 call sites in 7 files**, all integration tests: `tests/Orders.IntegrationTests/{OutboxEnvelopeTests,OutboxAtomicityTests,OutboxWireParityTests,OutboxRelayTests,IdempotentConsumerTests}.cs` and `tests/Fulfillment.IntegrationTests/{StockItemRepositoryTests,FulfillmentOutboxRelayTests}.cs`. Each gains `, new OrderFactPayloadMapper()` or `, new StockFactPayloadMapper()`. `tests/Orders.UnitTests/OrdersOutboxRegistrationTests.cs` gains `typeof(IFactPayloadMapper)` to its `_expectedSingleRegistrations` list. Fulfillment needs no equivalent list edit — `FulfillmentDispatcherRegistrationTests` builds the real host, and `ValidateOnBuild = true` fails if the port is unregistered. This is mechanical work and `tasks.md` A5 names every file, so it can be dispatched as such.

#### 8.2.4 `KafkaOptions` — unified, and the property that had to be re-supplied (`BC29`)

`KafkaOptions.cs` differed in exactly one value — the `ClientId` default, `otc-orders` versus `otc-fulfillment` — plus doc prose. It was excluded from #7's parity set for the same reason `kafka.config.ts` was: *it is the per-service part*. Unifying it therefore takes a real property away, and §15's question has to be asked of the change itself: **what made this correct before, and does that thing exist afterwards?** Before: the default lived in the file, so a service that configured nothing still got its own client id. After unification there is no per-service file to hold it, and an empty `ClientId` does not fail — librdkafka substitutes its own default `rdkafka`, so two services would share a producer identity **silently**, which is exactly what `.env.example`'s `*_KAFKA_CLIENT_ID` comment exists to prevent.

The canonical file re-supplies it at compile time rather than at run time:

```csharp
namespace OrderToCash.<Service>.Infrastructure.Outbox;

/// <summary>The outbox relay producer's own configuration. Bound by the service's own registration extension, never by this class.</summary>
public sealed class KafkaOptions
{
    /// <summary><c>kafka:29092</c> inside compose; <c>localhost:9092</c> for a host process. <c>KAFKA_INTERNAL_HOST</c> / <c>KAFKA_HOST_PORT</c> in <c>.env</c> stay the source of truth for the broker itself.</summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// The producer's Kafka client id. Deliberately has NO default and is
    /// <c>required</c>: this file is byte-identical in every service
    /// (design.md §8.2.4), so the one value that must differ per service is
    /// pushed out to that service's own options class, and omitting it is a
    /// CS9035 build error rather than a producer that silently registers as
    /// librdkafka's default and shares an identity with another service.
    /// </summary>
    public required string ClientId { get; set; }
}
```

The per-service value moves one file outwards, into code that is already per-service and already outside the parity set:

| Service | Where the id now lives | Value |
|---|---|---|
| Orders | `src/Orders/Infrastructure/OrdersOutboxOptions.cs` — `public KafkaOptions Kafka { get; } = new() { ClientId = "otc-orders" };` | `otc-orders` |
| Fulfillment | `src/Fulfillment/Infrastructure/FulfillmentOptions.cs`, same shape | `otc-fulfillment` (its `Program.cs` already overrides from `FULFILLMENT_KAFKA_CLIENT_ID`) |
| Billing | `src/Billing/Infrastructure/BillingOptions.cs`, same shape | `otc-billing` (from `BILLING_KAFKA_CLIENT_ID`, §14.1) |

**The guard is the compiler, and it is armed like any other** (`tasks.md` A8): delete the initialiser from `OrdersOutboxOptions`, and `dotnet build` fails with `CS9035: Required member 'KafkaOptions.ClientId' must be set…`. That error message is recorded verbatim in the arming table, exactly as a failing test's message would be. One existing call site also has to be repaired by the change and is named for it: `tests/Orders.IntegrationTests/StandInSagaResponders.cs:226` constructs `new KafkaOptions { BootstrapServers = bootstrapServers }` with no client id and will not compile until it names one.

A runtime check inside `KafkaFactPublisher` was considered and **rejected**: it would put a throw in the canonical body to catch a state the type system can already make unrepresentable, and it would only fire once a producer was resolved — later, and less loudly, than a build failure.

### 8.3 What is parity-guarded, and what deliberately is not — said out loud

| File | In the `OB1` set? | Why |
|---|---|---|
| `OutboxRelay.cs`, `OutboxRelayOptions.cs`, `OutboxRelayBackgroundService.cs`, `OutboxEnvelopeMapper.cs`, `KafkaFactPublisher.cs` | **yes** — banner-, namespace- and using-normalised byte identity across all three copies | One implementation duplicated by the database-per-service rule; drift between them is a defect by definition |
| **`OutboxWriter.cs`** | **yes**, after §8.2.2 and §8.2.3 | It used to dispatch to the service's own domain-event base type and its own *static* payload mapper. The neutral `FactEvent` name and the `IFactPayloadMapper` port remove both, leaving a body that is genuinely one implementation. **Added by the gate ruling of 2026-09-05**, which held that narrowing a reused id's scope is not a decision this spec gets to take |
| **`KafkaOptions.cs`** | **yes**, after §8.2.4 | The `ClientId` default was the only difference; it now lives in each service's own options class and the canonical property is `required`, so the file has nothing left that may legitimately differ. `BC29` names the property that move had to re-supply |
| `SystemClock.cs`, `EfCoreUnitOfWork.cs`, `IClock`/`IUnitOfWork`/`IFactPublisher`/`IFactPayloadMapper` | **no** | Copies with banners; never in a parity set, and not added to one here. `IFactPayloadMapper` in particular is per-service **by design** — its signature names the service's own `FactEvent` — and putting it in the set would be a category error. Out of scope, explicitly |
| the messaging family (`RpcJson.cs`, `RpcErrorPayload.cs`, `RpcMeta.cs`) | **no** | #7 recorded a messaging-family guard as an open point because its bare-JSON pair had become a third copy. **#8 has no bare-JSON pair at all** (§4.3); the remaining three files are three-line wrappers over `JsonWire.Options`. Recorded and not taken |

### 8.4 `OB1` — the guard itself

`tests/Orders.UnitTests/OutboxRelayParityTests.cs`, modelled case-for-case on `IdempotentConsumerParityTests` and reusing its helper shapes (`RepositoryPaths.Find`, banner stripping, namespace-line stripping, the service-token scan) so a reader who has read one has read both. Pure text, `System.IO` only, no container, runs in the ordinary `dotnet test` pass.

**The guarded family is seven files**: `OutboxRelay.cs`, `OutboxRelayOptions.cs`, `OutboxRelayBackgroundService.cs`, `OutboxEnvelopeMapper.cs`, `KafkaFactPublisher.cs`, `OutboxWriter.cs`, `KafkaOptions.cs`.

1. **Byte identity.** For every `src/<Service>` owning `Infrastructure/Persistence/Configurations/OutboxMessageConfiguration.cs`, each of the seven guarded files must equal the canonical after banner, namespace-line and `using`-line normalisation. **Non-vacuity: the discovered set must have at least three members**, and the failure message names them — the assertion that says out loud that the guard is armed rather than comparing Orders with itself. **The failure message must also name the diverging file and the first differing line**, because the arming in `tasks.md` F6 mutates each of the seven in turn and a message that says only *"a file differs"* cannot show that all seven are covered.
2. **Adoptability.** The canonical bodies must not name `Orders`, `Fulfillment`, `Billing`, `Projector` or `Notifications` (case-insensitively, un-word-bounded, so `OrdersDbContext` is caught) outside the banner and the namespace line, and every `using` — plain or aliased — must resolve to a namespace on the whitelist: `System.Data`, **`System.Text.Json`**, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.*`, `Confluent.Kafka`, `OrderToCash.SharedKernel`, `OrderToCash.Contracts.*`, and the suffixes `.Application.Ports`, **`.Domain.Events`**, `.Infrastructure.Persistence`, `.Infrastructure.Persistence.Entities`, `.Infrastructure.Outbox`. The two additions are `OutboxWriter.cs`'s: `System.Text.Json` for the payload serialisation and `.Domain.Events` for `FactEvent`.
3. **Census.** Every service that owns a relational `outbox` configuration must own all seven files — the case that fails the day a fourth write model appears with a hand-rolled relay. Today: Orders, Fulfillment, Billing. Notifications has `processed_events` only and is correctly outside the set.

**One consequence for the guard, unchanged from the first pass:** `IdempotentConsumerParityTests`' `UsingDirectiveRegex` is `^\s*using\s+([A-Za-z0-9_.]+)\s*;`, which does **not** match an alias form, so an alias line would fall through to the service-token scan and fail. `OutboxRelayParityTests` therefore defines its own using matcher covering both forms and whitelists the aliased namespaces by suffix. `IdempotentConsumerParityTests` is **not** modified — a landed guard is not widened to accommodate a new one.

### 8.5 Topic, key, headers — and one note feature 22 must not have to rediscover

Billing publishes **only** to `otc.billing.facts.v1` (one topic per service). `BillingFactTopic` is guarded by a read-the-spec-as-text test asserting it equals the `billingFacts` channel's `bindings.kafka.topic`; the topic already exists with 6 partitions (`infra/kafka/create-topics.sh`). Key = `correlationId` = the order id from `x-correlation-id` (`R15`), which is why `BC1` refuses a hold without it.

**For feature 22:** `saga.md` §6 and `asyncapi.yaml`'s `billingFacts` description require `payment.received.v1` and `credit.released.v1` to be written to the outbox **in one transaction, in that order, on the same partition key**. The relay's `ORDER BY seq` claim and the `correlationId` key preserve that ordering for free, **provided both rows are inserted in emission order by the same awaited per-row loop** (§7.2). Nothing in this feature exercises it; the constraint is written down so feature 22 does not rediscover it.

## 9. Consumers — none, and no idempotent-consumer copy

Per `saga.md` §5, Billing consumes **no** fact: `credit.hold`, `credit.release`, `invoice.issue` and `payment.register` are all command-driven, and Billing's own facts are produced, never consumed, by it. The host starts no Kafka consumer; the relay's producer is its only Kafka client, which keeps `FactConsumerConfinementTests` trivially satisfied.

#7 nonetheless copied its idempotent-consumer pair into Billing to arm its `OI12` case 1 and to give feature 22 a guarded starting point. **#8 does not**, and the reason is the gate ruling already taken for Fulfillment (`fulfillment_stock/design.md` §9, its gate row 1): #8's `IdempotentConsumerParityTests` case 3 requires a copy only from a service that has **both** a `processed_events` configuration **and** a Kafka consumer `BackgroundService`, and C# has no empty-enum bottom type with which to render #7's *uncallable* `CONSUMER_NAMES = [] as const`. A copy here would be live code that looks callable, with a ledger it must never write to. `tasks.md` F5 confirms `IdempotentConsumerParityTests` stays green with no Billing copy.

Feature 22's remittance intake dedups by `paymentReference`, a different key and a different table; it starts from the pattern when it needs it.

## 10. Five backlog entries closed inside this feature

Phase 9's closing assessment names the mechanism: *"the one thing that has been shown to work in this build is closing a backlog entry inside a feature that already has the file open"*. All five of these are that — ids **50** and **51** from the first pass, and ids **48**, **53** and **54** added by the 2026-09-05 standing instruction (§10.3 gives the verdict per entry, including the one that stands).

### 10.1 Id 50 — RPC responder shutdown fault isolation

The behaviour is specified in §4.7 and required by `BC22`. Its footprint: `src/Billing/Presentation/CreditRpcResponder.cs` (written correctly from the start), `src/Fulfillment/Presentation/StockRpcResponder.cs` (the four-line correction), and one named test in each of `tests/Billing.UnitTests` and `tests/Fulfillment.UnitTests`. The acceptance criteria of the entry are met verbatim: *a faulted in-flight task no longer propagates out of `StopAsync`*; *the drain still waits for every in-flight request*; *a named test proves shutdown completes with one faulted and one healthy in-flight task*. **Both halves are armed** (`tasks.md` E7): reverting to `Task.WhenAll(pending)` must fail the fault-isolation assertion, and removing the wait must fail the drain assertion.

### 10.2 Id 51 — RPC payload schema parity parsed from `asyncapi.yaml`

`tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs` and `tests/Orders.UnitTests/SagaCommandPayloadTests.cs` call `AssertKeys(json, "orderReference", "retailerCode", …)` against **hand-retyped** key lists, while `fulfillment_stock/design.md` §6.3, its `tasks.md` C3 and the tests' own XML docs all say the instrument *"reads `asyncapi.yaml` as text"*. Billing's payload tests would be the fourth retyped copy, and the same entry records that the `RpcError.code` enum now has **three** hand-retyped copies (`OrdersCreateErrorMapper._contractRpcErrorCodes`, its test's own copy, and `NatsSagaCommandsAdapter`'s terminal-code set) — the second of which was added by a *fix* for the first.

What this feature does (`BC23`):

- A shared test helper — `AsyncApiSchema.PropertyNamesOf(schemaName)` and `AsyncApiSchema.EnumValuesOf(schemaPath)` — that reads `specs/shared/asyncapi.yaml` as text with the `RpcSubjectsTests` block-extraction technique, resolves a one-level `allOf` (`CreditListRequestPayload` needs it) and returns the declared property names in declaration order. It lives beside the first test that uses it and is referenced by the others; it is **not** a new shared project.
- **Billing's own** `CreditRpcPayloadTests` derives every expected key set from that helper — never a literal list — for all six `billing.credit.*` request and reply schemas.
- `StockRpcPayloadTests` and `SagaCommandPayloadTests` each **gain one case** asserting their existing retyped lists equal the parsed sets, schema by schema. The existing cases are not rewritten: they are cheap, readable and they do catch unilateral drift of the code; what they cannot catch is the *correlated* authoring error, and one added case closes exactly that.
- `OrdersCreateErrorMapperTests` gains one case asserting the **three** retyped `RpcError.code` sets all equal the twelve values parsed from `asyncapi.yaml`'s `RpcError.code.enum`.
- All four are armed against a **scratch copy** of `asyncapi.yaml` — never against the real one — by pointing the helper at a temporary path with one property renamed and one enum value removed.

### 10.3 The other phase-10 backlog entries — re-decided against the 2026-09-05 standing instruction

The first pass of this spec deferred ids 48, 52, 53 and 54, one reason each. The gate attached a standing instruction to its ruling — *"stop leaving issues to the next phase — fix them"* — so each of the four was re-read against it. **Three are overturned and closed here; one stands.** The verdict is given per entry, and where the entry stands, that one line is now the argument the human reads rather than a deferral they never saw.

| Id | First pass's reason | Verdict | Why |
|---|---|---|---|
| **48** — `NatsHeaders` thread-safety guard | *"Its file and its test are Orders'. This feature opens neither"* | **OVERTURNED — closed here (§10.4)** | The reason was true and is no longer sufficient. The entry needs **no production change at all**: the property already holds, only the guard is missing, so it is one test method plus one arming in one file. This feature already makes the implementer read `NatsSagaCommandsAdapter` (task E6 reads its terminal-code classification, task G4 asserts against its retyped enum copy), and the entry's own note says *"five more services will copy this shape"* — the cost of doing it now is a fraction of the cost of doing it after the copies exist |
| **52** — retroactive ledger boundary sweep | *"A sweep over four shipped services whose own acceptance forbids fixing anything in place"* | **STANDS** | Its own acceptance requires *"NOTHING is fixed in place — a fix inside a sweep is a change nobody reviewed against a spec"*, and its output is a per-service ledger section **plus a separate backlog entry for every gap found**. Folding it into a feature would violate the entry's own terms and would put un-specced fixes into a feature review; it is not deferral, it is that the entry is defined as standalone work. §15's enumerated method is the template it should follow |
| **53** — reply-shape assertions that only throw | *"Its three named sites are Orders' and Fulfillment's integration tests; this feature opens none of them"* | **OVERTURNED — closed here (§10.5)** | Test-only in the end state, three assertions. The arming needs both integration suites running against containers — and **tasks A5, A6 and A9 already run both suites**, so the containers are already up and the marginal cost is the three edits and their armings |
| **54** — the missing Fulfillment ledger row | *"It edits `specs/fulfillment_stock/design.md`, a document this feature only reads"* | **OVERTURNED — closed here (§10.6)** | **The reason was factually wrong when written.** `tasks.md` G3 already amends two sentences in `specs/fulfillment_stock/design.md` §6.3 as part of closing backlog id 51, so this feature does open that file. The remaining work is pasting a row whose text is already drafted verbatim in `progress/review_fulfillment_despatch.md` §5 into the §15 table beside it — the cheapest of the four by a wide margin |

### 10.4 Id 48 — a fresh `NatsHeaders` per saga-command request (`BC31`)

`src/Orders/Infrastructure/Messaging/NatsSagaCommandsAdapter.cs`'s class remark asserts that a fresh `NatsHeaders` is constructed per call. `NatsHeaders` is mutable and documented as not thread-safe; the adapter is `AddScoped` and the sweeper dispatches sequentially, so the hazard is latent — but the property is asserted by prose and held by nothing.

**Test-only, no production change.** `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs` gains `BC31_PassesAFreshlyConstructedHeaderCollectionOnEveryRequest_NeverOneReusedBetweenCalls`: the existing `RawRequester` fake captures the `NatsHeaders` instance of two consecutive `SendAsync` calls and the case asserts they are **reference-distinct** (`Assert.NotSame`), not merely equal by value — value equality would pass against a single instance cleared and re-populated, which is precisely the shape the entry names. **Armed** (`tasks.md` K1): hoist the instance to a field and clear/re-add per call; the new case must fail while **every existing header-value assertion in the file stays green**, and both halves of that are recorded, because a mutation that also broke the value assertions would not have demonstrated what this guard adds.

### 10.5 Id 53 — reply-shape assertions that assert (`BC32`)

C# deserialises an `RpcError` body into an all-defaults reply record **without throwing**, so a test that deserialises and then reads a collection detects a wrong-shaped reply only by a null dereference — or, at `OrdersCreateAcceptanceTests.cs:159`, not at all, where the comment *"// the request succeeded"* is the entire claim. #7's JavaScript destructuring produced `undefined` and its assertions caught it; this is a property the port lost, which is why it carries a `BC` id rather than being filed as tidying.

Three sites, each gaining the shape `FS22` adopted after feature 46's `D1` — **read the body, assert this reply's own discriminating field, only then touch a collection**:

| Site | Discriminating field to assert first |
|---|---|
| `tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs:159` | the reply's `orderReference` matches `^ORD-[0-9]{6,}$` (today it asserts nothing) |
| `tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:52` | the reply's own `productCode` / outcome field, before `Items` |
| `tests/Fulfillment.IntegrationTests/StockListTests.cs:27` | the reply's `total` / paging field, before `Items` |

**Armed** (`tasks.md` K2): make the responder answer a real `RpcError` for the request each test sends, and confirm each of the three fails **on its own named assertion**, with the message recorded — not on a `NullReferenceException` and not by passing.

### 10.6 Id 54 — the Fulfillment ledger row nobody wrote

`specs/fulfillment_stock/design.md` §15's table gains one row, `L13`, for the un-hinted in-transaction re-read at `src/Fulfillment/Application/DespatchCreationService.cs:67`. The text is drafted verbatim in `progress/review_fulfillment_despatch.md` §5, row *"L1, second face"*, and the row must carry all three of the entry's acceptance clauses: that the read is issued only **after** `LockForOrderAsync` is granted; that `EfCoreUnitOfWork.cs:30` opens `IsolationLevel.ReadCommitted` under RCSI, which makes the snapshot **statement-scoped** rather than transaction-scoped; and the consequence for #9 stated explicitly — under a transaction-scoped snapshot (PostgreSQL `REPEATABLE READ`) the same code returns a stale read and turns a correct idempotent repeat into a permanent `UNAVAILABLE`.

**Documentation only. No code changes, no test changes, and nothing else in that file changes** beyond the two sentences `tasks.md` G3 already corrects. This is a ledger **gap** — the ledger's own failure mode, the row nobody wrote — so the guard is the row's existence, not a new test.

## 11. First boot against the live compose stack — worked out, not discovered

**Pre-state, read from the running stack while this design was written** (not assumed):

| Order | Retailer / company | Status | Total (minor) | `saga_commands` |
|---|---|---|---|---|
| `ORD-000007` | `CarrefourEs` / `IBERFOODS` | `stock_reserved` | 49 998 | `stock.reserve` `sent`, `credit.hold` **`parked`** (attempts 6) |
| `ORD-000008` | `AldiEs` / `IBERFOODS` | `stock_reserved` | 5 547 | idem |
| `ORD-000009` | `AldiEs` / `IBERFOODS` | `stock_reserved` | 5 547 | idem |
| `ORD-000010` | `CarrefourEs` / `IBERFOODS` | `stock_reserved` | 49 998 | idem |
| `ORD-000011` | `CarrefourEs` / `IBERFOODS` | `stock_reserved` | 1 000 | idem (attempts 3) |

`otc_billing.credits` holds **154** rows — 7 retailers × 22 companies, every reachable pair — each with a 500 000 minor-unit limit. `otc_billing.credit_items` holds **15** rows, three per seeded completed saga (`hold`, `consume`, `release` of the same amount), which net to **zero** exposure on every line; the seeded cancelled saga `ORD-000006` wrote nothing, because a rejection records nothing. Every seeded line therefore starts at `availableCredit = 500 000`. All ten reservations for the five parked orders are in status `reserved`.

**This is a difference from #7 worth stating.** #7's gate row 12 flagged that its seed covered only 7 of 7 × 22 pairs, so three of its five parked orders would get `BC3`'s `NOT_FOUND` and never advance; the human ruled at the gate that the baseline lines be added. **#8 inherited the amended seed** — `src/Seed/Domain/Data/Credits` builds `_primaryCredits` plus `_baselineCredits` "for every retailer against every OTHER company", and the live count of 154 confirms it. So #8 has no seed decision to take and no split outcome to explain: **all five orders advance.**

**Expected sequence, unattended**, once the Billing host is started (Orders and Fulfillment must be running, unchanged — this feature makes no Orders- or Fulfillment-side behavioural change beyond §8.2's aliases and §4.7's shutdown fix):

1. Within one sweeper interval (`OrdersSagaSweeperOptions.IntervalMs` 30 s, park backoff capped at 15 min) of each parked row's `next_attempt_at`, Orders re-issues `credit.hold` with `x-correlation-id` = the order id and `x-request-id` = the `saga_commands` row id.
2. Billing answers `approved`. `otc_billing.credit_items` gains exactly one `hold` row per order; `otc_billing.outbox` gains exactly one `credit.approved.v1`, which the relay stamps published within `OUTBOX_POLL_INTERVAL_MS`; the reply marks the `saga_commands` row `sent`.
3. The orchestrator consumes the fact from `otc.billing.facts.v1`: `stock_reserved → credit_approved → confirmed`, `order.confirmed.v1`, then `despatch.create` — **which Fulfillment answers**, because feature 18 landed. Reservations move `reserved → consumed`, `DES-000006` … `DES-000010` are created, `order.despatched.v1` is published, the order reaches `despatched`, and the orchestrator issues `invoice.issue` — for which this feature registers **no** responder, so NATS answers `NoResponders`, the dispatcher treats it as a transport failure, and after three attempts the row **parks**.
4. **Resting state: five orders `despatched`, five `invoice.issue` rows `parked`** — the first time orders in this repository cross three services.

**The arithmetic, per line, so the implementer verifies rather than eyeballs:**

- `CR-000001` (`CarrefourEs` / `IBERFOODS`, EUR 500 000): holds of 49 998 + 49 998 + 1 000 = **100 996**; final `availableCredit` **399 004**, whatever order the three land in.
- `CR-000092` (`AldiEs` / `IBERFOODS`, EUR 500 000): holds of 5 547 + 5 547 = **11 094**; final `availableCredit` **488 906**.

**Is a genuine over-limit rejection constructible today, with no simulator bound? Yes, and it is the demo.** `PRD-0001` costs 24 999 minor units and `IBERFOODS` holds 500 units of it with 5 reserved, so quantity is not a constraint and neither `orders.create`'s DTO nor `Quantity` caps a line. **Before** the three `CR-000001` holds land, `21 × PRD-0001 = 524 979 > 500 000`; **after** them, `16 × PRD-0001 = 399 984 > 399 004`. Both totals end in `79` and `84` respectively — **neither is `mod 100 = 99`**, so neither can be confused with feature 20's affordance. Placing one of them produces the first end-to-end compensation this repository has run: `credit.rejected.v1` with `reason: over_limit` → the orchestrator's `stock.release` → `stock.released.v1` → the order `cancelled` with `cancellationReason: credit_rejected` and `compensationSteps[]` carrying the released stock. That is `R44`'s last clause satisfied **before** feature 20 exists.

**No seed re-run is part of this procedure**, and that is a #8 difference: #7 had to re-run `pnpm seed` and therefore had to fix its verifier first (`requirements.md` §1.8). `src/Seed` here prints counts and asserts nothing, and the pre-state above is already correct.

The implementer records the actual `SELECT` outputs and structured log lines, with timestamps, in `progress/impl_billing_credit.md` § Live boot; the human's manual verification script is derived from that section.

## 12. Rejected alternatives, recorded

| Alternative | Why not |
|---|---|
| A materialised `available_credit` or `active_holds` column on `credits` | A second source of truth for a quantity the ledger already determines, kept in step by discipline; needs a migration; and it is exactly the drift `B2`'s append-only rule exists to prevent |
| A projection table maintained in the same transaction | Same objection, plus a second write per hold and a rebuild story nobody would exercise |
| Periodic compaction entries collapsing history | Would bound the `Σ` scan but makes the rows no longer a faithful audit trail — the one property `B2` is for |
| A fourth `credit_items` type recording refusals, to make `BC8` idempotent | Contradicts **B1** (*"not recorded"*), changes the shared schema, and makes every `Σ` type-dependent |
| Auto-creating a credit line on first hold (`BC3`) | Would let a typo in `companyCode` silently mint credit. Master data is created by the seed or by an operator, never by a saga |
| `db.CreditItems.Where(...).SumAsync(...)` for §5.5 step 2 | LINQ composition drops the table hint, so the `Σ` would be an unlocked snapshot read inside a locking transaction — correct-looking, and wrong under exactly the concurrency `BC9` exists to survive |
| `OutboxRelay(DbContext db, …)` + a `DbContext` DI registration for §8.2 | Works, but costs a body change in three services and a service token that means "whichever one". The using alias costs one line — §8.2.1 |
| Narrowing `OB1` to five files and excluding `OutboxWriter.cs` / `KafkaOptions.cs` | **Proposed by this spec's first pass and overruled at the 2026-09-05 gate.** Reusing `BC17`'s id claimed #7's obligation at full scope; narrowing the scope of a reused id is the quiet divergence the trilogy exists to prevent. §8.2.2 – §8.2.4 are what the full scope costs |
| A using alias for the payload mapper (`using FactPayloadMapper = …OrderFactPayloadMapper;`) instead of `IFactPayloadMapper` | It would make `OutboxWriter.cs` byte-identical for one line less work, and it was rejected on the gate's own terms: it leaves the writer statically bound to a concrete class, so the "identical" bodies would still be three different programs behind the same text, and the writer would remain untestable against a substitute mapper. The gate asked for a port; a port is also the better answer |
| A runtime check for an empty `ClientId` in `KafkaFactPublisher` | A throw in the canonical body to catch a state `required` makes unrepresentable, firing later and more quietly than a build error — §8.2.4 |
| Leaving `src/SharedKernel/Money.cs` unchecked and wrapping only `CreditExposure.Summarise` | Guards the ledger and not the quantity the ledger exists to produce: `availableCredit = creditLimit − committedExposure` is a `Money.Subtract`. Three expressions and three tests close the class instead of one instance — §3.3 |
| A `CheckForOverflowUnderflow` property in `Directory.Build.props` | Would make every arithmetic operation in six services checked at once, including hot paths and third-party-shaped code, as a side effect of one feature's guard. The explicit `checked` regions say **where** the property is claimed, which is what §15 asks a ledger row to name |
| Copying the idempotent-consumer pair into Billing "for feature 22" | #8's guard does not ask for it and C# cannot render #7's uncallable version — §9 |
| Deferring `billing.credit.release` to feature 41 as #7 deferred it | #7 deferred it because **no such subject existed**; #8 inherits a contract in which it does, with full idempotency semantics. Deferring would leave `Release` a caller-less fact-emitting branch — the exact shape that got #7 rejected twice — for no gain |

## 13. Testing approach

| File | Level | Runner / infrastructure | Proves |
|---|---|---|---|
| `tests/Billing.UnitTests/BuyerCreditTests.cs` | domain unit | xUnit, pure | `R37` (matrix `buyer-credit.spec`), `BC5` |
| `…/CreditHoldTests.cs` | domain unit | pure | `R38`, `R39` (matrix `credit-hold.spec`), `BC10` domain half, `BC14` domain half, `BC26` |
| `…/CreditLedgerTests.cs` | domain unit | pure | `R40`, `R41` (matrix `credit-ledger.spec`), `BC11`, `BC12` |
| `…/CreditExposureTests.cs` | domain unit | pure | `BC5`/`BC6` as identities including the cancelled-before-invoice case the literal §5.1 formula gets wrong; `BC28` grouping half |
| `…/CreditHoldServiceTests.cs`, `CreditReleaseServiceTests.cs` | unit, fakes | pure | `BC7`, `BC13`, `BC14` handler half, reply-after-commit, rollback ⇒ no reply |
| `…/CreditDecisionPortTests.cs`, `AlwaysApproveCreditDecisionTests.cs` | unit | pure | `BC14` type half, `BC15` |
| `…/CreditResponderHeaderTests.cs`, `CreditErrorMapperTests.cs`, `CreditRequestValidatorTests.cs` | unit | pure | `BC1`, `BC27`, §4.4's rules |
| `…/CreditResponderConcurrencyTests.cs`, `CreditResponderShutdownTests.cs` | unit | pure | `BC21` scope half, **`BC22`** |
| `…/CreditSubjectsTests.cs`, `BillingFactTopicTests.cs`, `CreditRpcPayloadTests.cs` | unit | read `asyncapi.yaml` as text | subjects, topic and **`BC23`** |
| `…/CreditClaimProjectionTests.cs` | unit | EF model only | the literal column lists in §5.5's SQL match the mapped entity types (the E7 instrument) |
| `…/BillingDispatcherRegistrationTests.cs` | unit | real DI graph | boot fails if a port or handler is missing |
| `tests/Billing.IntegrationTests/CreditHoldTests.cs` | integration | MsSql + NATS + Kafka | `R38`/`R39` integration halves, `BC1`, `BC3`, `BC4`, `BC7`, `BC8`, `BC28` |
| `…/CreditHoldRaceTests.cs` | integration | MsSql + NATS | **`BC9`** |
| `…/CreditReleaseTests.cs` | integration | MsSql + NATS | **`BC25`** |
| `…/CreditListTests.cs` | integration | MsSql + NATS | `BC6` integration half |
| `…/CreditWireTests.cs` | integration | MsSql + NATS | `BC2` |
| `…/BuyerCreditRepositoryTests.cs` | integration | MsSql | `BC10` transaction half, `BC24`, append-only at SQL level, forced rollback leaves neither |
| `…/CreditResponderConcurrencyTests.cs` | integration | MsSql + NATS | **`BC21`** — the held-lock proof |
| `…/BillingOutboxRelayTests.cs` | integration | MsSql + Kafka | `BC16` |
| `tests/Orders.UnitTests/OutboxRelayParityTests.cs` | unit | pure text | **`BC17`** / `OB1`, three cases over **seven** files (§8.4) |
| `tests/Billing.UnitTests/CreditExposureTests.cs` (overflow cases) | domain unit | pure | **`BC30`** — `Summarise` driven directly with overflowing amounts, no aggregate state |
| `tests/SharedKernel.UnitTests/MoneyTests.cs` | unit | pure | **`BC30`** — `Add`/`Subtract`/`Multiply` raise rather than wrap |
| `tests/Orders.UnitTests/NatsSagaCommandsAdapterTests.cs` | unit | pure | **`BC31`** (backlog id 48) |
| `tests/Orders.IntegrationTests/OrdersCreateAcceptanceTests.cs`, `tests/Fulfillment.IntegrationTests/StockReplenishTests.cs`, `StockListTests.cs` | integration | existing harnesses | **`BC32`** (backlog id 53) |
| `tests/Orders.UnitTests/SagaCommandPayloadTests.cs`, `OrdersCreateErrorMapperTests.cs`, `tests/Fulfillment.UnitTests/StockRpcPayloadTests.cs`, `StockResponderShutdownTests.cs` | unit | pure | **`BC23`** (three added cases), **`BC22`** (Fulfillment half) |

**Which existing tests cover the refactored path** (§8.2's changes cross into two shipped services, so this is stated rather than assumed). `tests/Orders.IntegrationTests/OutboxWireParityTests.cs` and `tests/Contracts.UnitTests/GoldenEnvelopeParityTests.cs` assert the **exact bytes** the payload mappers produce against twelve captured #7 envelopes — they are what proves the `IFactPayloadMapper` refactor changed no payload. `tests/Orders.IntegrationTests/OutboxEnvelopeTests.cs` drives `OutboxWriter.BuildRows` directly **and** through the repository, column by column. `OutboxAtomicityTests.cs` proves the writer still runs inside the caller's transaction; `OutboxRelayTests.cs`, `IdempotentConsumerTests.cs`, `tests/Fulfillment.IntegrationTests/{StockItemRepositoryTests,FulfillmentOutboxRelayTests}.cs` cover the relay and the Fulfillment copies; `tests/Orders.UnitTests/{OrdersOutboxRegistrationTests,OutboxClaimProjectionTests,OutboxRelayLoopTests,KafkaFactPublisherConfigTests}.cs` cover registration, the claim SQL, the loop and the producer config. **All of them must be green before and after every task in group A**, and `tasks.md` A1 – A9 each end with the specific suites to run.

**Fixtures.** `MsSqlContainerFixture` already exists in `tests/Billing.IntegrationTests` (phase 6). `NatsContainerFixture` and `KafkaContainerFixture` are copied from `tests/Fulfillment.IntegrationTests` with banners and the same pinned images. A `BillingHostFixture` boots the **real** `BillingHost.CreateBuilder` graph against the containers, so the integration suites exercise the same DI wiring, responder and options binding the live process uses; callers are raw `NatsConnection` clients, the production caller's shape.

**Every integration harness must be able to bind a refusing `ICreditDecisionPort`.** #7's harness bound `AlwaysApproveCreditDecision` unconditionally in every integration file, which is *why* the port-refusal branch was structurally unreachable and why its defect `D1` could not be caught at that level. `BillingHostFixture` takes the decision port as a construction parameter defaulting to the always-approve adapter.

**The synchronisation rule is binding** (reviewer ruling, feature 16): wait only on **terminal or monotonic** evidence — an outbox row's `published_at` (set once, never cleared), the count of rows in the append-only `credit_items` table, the `OutboxRelayResult` of a hand-driven `RunOnceAsync()`, a Kafka consumer's received-message list, or a reply. **Never poll `availableCredit` or any derived quantity mid-flight** — it is a computed value the system passes through, and polling it is a race by construction. For `BC9`: `Task.WhenAll` two raw NATS requests for **different** orders against a line with room for exactly one, assert on the **replies** (one `approved`, one `rejected`), on the **final** `Σ hold − Σ release ≤ creditLimit`, and on the outbox holding exactly one `credit.approved.v1` and one `credit.rejected.v1`; repeat on **10 fresh credit lines** so a scheduling fluke is visible rather than lucky.

**Matrix name mapping** (`specs/shared/test-matrix.md` §5's stack-neutral paths → #8):

| Matrix path | #8 file |
|---|---|
| `billing/domain/buyer-credit.spec` | `tests/Billing.UnitTests/BuyerCreditTests.cs` |
| `billing/domain/credit-hold.spec` | `tests/Billing.UnitTests/CreditHoldTests.cs` |
| `billing/domain/credit-ledger.spec` | `tests/Billing.UnitTests/CreditLedgerTests.cs` |

## 14. Configuration and packages

### 14.1 Settings

| Setting | Default | Note |
|---|---|---|
| `MSSQL_*` (host, port, `MSSQL_DB_BILLING`, app user/password) | as `.env` | read exactly as Fulfillment's `Program.cs` reads its own |
| `NATS_URL` | `nats://localhost:4222` | the responder's connection |
| `KAFKA_BOOTSTRAP_SERVERS` | `localhost:9092` | the relay's producer |
| `BILLING_KAFKA_CLIENT_ID` | `otc-billing` | **new** — a third service must not silently share a client id |
| `OUTBOX_RELAY_ENABLED`, `OUTBOX_POLL_INTERVAL_MS`, `OUTBOX_BATCH_SIZE`, `OUTBOX_PUBLISH_TIMEOUT_MS` | as Orders | same names, same semantics |
| `BILLING_MAX_CONCURRENT_REQUESTS` | `32` | §4.1 — must stay below the ADO.NET `Max Pool Size` of 100, for the reason `fulfillment_stock/design.md` §6.2 gives |

`.env.example` gains `BILLING_KAFKA_CLIENT_ID` and `BILLING_MAX_CONCURRENT_REQUESTS` beside the existing `FULFILLMENT_*` entries.

### 14.2 Packages

**No new `PackageVersion`.** `src/Billing/Billing.csproj` adds a `ProjectReference` to `src/Cqrs` and `PackageReference`s to `NATS.Net`, `Confluent.Kafka`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Options` and `Microsoft.Extensions.Logging.Abstractions` — all already pinned in `Directory.Packages.props` and already referenced by `src/Fulfillment/Fulfillment.csproj`, each with the same confinement comment. `tests/Billing.UnitTests` is a new project (xunit, runner, coverlet, references `src/Billing`); `tests/Billing.IntegrationTests` gains `Testcontainers` (generic), `Confluent.Kafka` and `NATS.Net`. The commit message's package section reads **"none installed; existing pinned packages newly referenced by `src/Billing` and `tests/Billing.*`"**.

### 14.3 The host

`BillingHost.CreateBuilder(args, configure…)` mirrors `FulfillmentHost.CreateBuilder` exactly, including `ValidateOnBuild = true` / `ValidateScopes = true` forced in **every** environment (feature 15's review `D3`). Registration order: persistence and clock → outbox and relay → credit-decision port → messaging and responder options → `AddDispatcher(Assembly.GetExecutingAssembly())` **last**, so a missing port or handler is a boot failure. `Program.cs` is the thin `Host.CreateApplicationBuilder` shim.

## 15. Ported-idiom ledger

> Binding since the Phase 8 gate (`CLAUDE.md`, *"The ported-idiom ledger"*). One line per idiom: **#7 relied on X; in #8 that property is supplied by Y.** Where the property came free from #7's engine, language or library and must be hand-built here, `tasks.md` names a guard test — the **Guard** column is the contract between the two documents.
>
> **Derived from an enumerated boundary list, not from recollection.** Phase 9's closing assessment found the ledger's failure mode to be *"the row nobody wrote"*, and prescribed the method backlog id 52 already uses: enumerate the places where a value crosses into or out of the process, then walk them. §15.1 is that enumeration; §15.2 has **one row per enumerated boundary**, including the boundaries whose answer is *"nothing special is required here, and this is why"* — a boundary considered and dismissed is a row, a boundary never listed is the failure.
>
> **After the 2026-09-05 revision: 25 boundaries, 27 rows.** The enumeration in §15.1 is unchanged — the ruling moved work into this feature, it did not open a new place where a value crosses the process. But two boundaries acquired a **second** property that this feature must now supply, and each gets its own row rather than a clause appended to an existing one: `L26` at **B20**, because unifying `KafkaOptions.cs` (§8.2.4) took the per-service producer identity out of the file that used to hold it, and `L27` at **B16**, because making one `OutboxWriter` serve three services replaced a property C#'s type system used to give for free. `L25` at **B25** is rewritten: the property is now **supplied**, not stated.

### 15.1 The boundary enumeration

Twenty-five places where a value crosses into or out of the Billing process in this feature, listed before any of them was analysed:

**Inbound decode** — B1 `billing.credit.hold` request bytes → record; B2 `billing.credit.release` request bytes → record; B3 `billing.credit.list` request bytes → record (paging defaults); B4 request headers → `RpcMeta`; B5 request `amount` object → `Money`.
**Store reads that decide something** — B6 `credits` row → aggregate identity and limit; B7 `credit_items` committed-exposure scalar; B8 `credit_items` order-scoped rows (`already_held`); B9 `credit_items` grouped read for the view; B10 `credits` page read for the view; B11 `credit_date` / timestamps → domain instants; B12 `type` column → `CreditEntryType`; B13 `currency_code` → `Money.Currency`; B14 `order_reference` matching in SQL versus grouping in memory.
**Store writes** — B15 `credit_items` INSERT; B16 `outbox` INSERT; B17 the `credits` row, which is never written.
**Outbound encode** — B18 success reply encode; B19 error reply encode; B20 Kafka publish of the envelope and its key.
**Process and host** — B21 request concurrency and DI scope; B22 shutdown drain; B23 transaction and isolation defaults; B24 the in-process call out of the domain into the decision adapter and back; B25 `Σ` arithmetic width.

### 15.2 The ledger

| # | Boundary | Property | #7 got it from | #8 supplies it by | Guard in `tasks.md` |
|---|---|---|---|---|---|
| **L1** | B1–B3 | A bare-JSON request is answered with a bare-JSON reply | A hand-written `BareJsonNatsDeserializer`/`Serializer` pair, written to defeat the Nest packet — #7's largest gate row | **Nothing.** `SubscribeAsync<byte[]>` + `ReplyAsync(byte[])` is bare by construction, through the one shared `JsonWire.Options`. A **saving**, recorded as such | `BC2` is asserted anyway (`H9`), because the wire is a trilogy contract, not a framework artefact |
| **L2** | B1–B3 | The declared request keys are the keys the record reads | Generated `@otc/contracts` types the DTO `implements` — a compile error on drift | Hand-transcribed records **plus** `BC23`'s parsed-from-`asyncapi.yaml` key check. C# has no generated contract type for RPC payloads, and three existing test files retype their key lists (backlog 51) | `BC23` (`G1`–`G4`), armed against a **scratch** `asyncapi.yaml` |
| **L3** | B1–B3 | A malformed request is refused, not half-processed | `class-validator` decorators on DTO classes | A hand-rolled `CreditRequestValidator`, one unit case per rule; it is also where the `^ORD-[0-9]{6,}$` and `^[A-Z]{3}$` alphabets close `L13` and `L14` at the edge | `CreditRequestValidatorTests`, one case per rule (`E4`) |
| **L4** | B4 | A missing or malformed correlation header mutates nothing | The controller read `ctx.getHeaders()` and refused before dispatch | `RpcMeta` copied verbatim from Fulfillment, refusal **before** dispatch, on `hold` and `release` only | `BC1` unit `[Theory]` asserting the dispatcher was never called (`E5`) |
| **L5** | B5 | The order total cannot silently narrow | JavaScript numbers have no narrowing conversion | `long` end to end: `MinorUnits` is `format: int64`, `SagaMoney.Amount` is `long`, `Money.MinorUnits` is `long`, `credit_limit`/`amount` are `bigint` (feature 44). **No cast anywhere** | `NoMoneyColumnIsIntTests` (already green); `tasks.md` D3 forbids any `(int)` cast on a money value and the reviewer greps for it |
| **L6** | B6 | A blocking, current read of the row the decision depends on | `SELECT … FOR UPDATE` under InnoDB `REPEATABLE READ` | Explicit `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`. `READ_COMMITTED_SNAPSHOT` is **ON**, so an un-hinted read takes **no lock** and returns a row version — two holds against one line would both proceed | `BC9` integration (`H5`); armed by removing the hint (`D5`) |
| **L7** | B7 | The `Σ` is computed from committed state, not a snapshot | `FOR UPDATE` on the aggregate read | The hint on a `FromSqlInterpolated`/`SqlQueryRaw` statement. **`db.CreditItems.SumAsync(...)` is forbidden**: LINQ composition drops the hint and hands back an unlocked snapshot read that looks identical in code review | `BC9`; `tasks.md` D3 forbids LINQ aggregation on the locked path and the reviewer greps for `SumAsync` |
| **L8** | B8 | The idempotency predicate reads current rows | `FOR UPDATE` on the order's entries | The same hint. Without it, two `credit.hold` for the **same** order would both read "no hold recorded" and both write one — the `B4` violation `BC7` exists to prevent | `BC7` integration re-issue case (`H4`); armed with `D5`'s hint removal |
| **L9** | B9–B10 | A read-only list blocks nobody | MySQL's non-locking consistent read | `AsNoTracking`, no hint, no transaction — under RCSI a versioned read. Nothing special is required, and this row exists so nobody "helpfully" adds a hint to the view | `BC6` integration (`H6`) asserts the reconciliation identity, not the locking; the absence is enforced by `tasks.md` D6's explicit instruction |
| **L10** | B10 | Paging defaults are applied where the schema says | `class-validator`'s `@IsOptional()` + default | The validator supplies `page = 1`, `pageSize = 25` before the query is built; the records use nullable properties so an absent key is distinguishable from `0` | `CreditRequestValidatorTests` paging cases (`E4`) |
| **L11** | B11 | An instant read back equals the instant written | A `mysql2` pool created with `timezone: 'Z'` — one clause in #7's `design.md` §7.1 | `value.UtcDateTime` to write, `new DateTimeOffset(value, TimeSpan.Zero)` to read. `datetime2(3)` carries no offset and EF Core returns `DateTimeKind.Unspecified`; `new DateTimeOffset(unspecified)` applies the **machine's local** offset. **`CreditLedgerEntry.EntryDate` is the first #8 child entity carrying a date across this boundary** | **`BC24`** integration round trip under a non-UTC `TZ` (`H8`); armed by dropping `TimeSpan.Zero` |
| **L12** | B12 | An unrecognised ledger type is loud | TypeScript's union type plus a runtime guard; an unknown value could not be constructed | `CreditEntryTypes.Parse` **throws** on anything outside the closed set. A silently-skipped row would move `Σ` and therefore `availableCredit` — the failure would present as a wrong credit limit, not as a parse error | `CreditLedgerTests` case: an unknown `type` token raises rather than being ignored (`B3`) |
| **L13** | B13 | Two currency codes compare equal iff they are the same currency | MySQL's CI collation on the column; JS `===` on the string | `Money.EnsureSameCurrency` uses `StringComparison.**Ordinal**` while `char(3)` matches under `SQL_Latin1_General_CP1_CI_AS`. The two disagree on case; the disagreement is unreachable because the seed writes uppercase, `Money`'s constructor refuses a malformed code and §4.4 refuses anything but `^[A-Z]{3}$`. **Closed at the edge, not assumed away** | `CreditRequestValidatorTests` currency case (`E4`) + `BC4` integration (`H3`) |
| **L14** | B14 | The in-memory grouping key agrees with the database's own matching | JS `===` (ordinal) against MySQL `utf8mb4_0900_ai_ci` — the same latent disagreement, never stated | `StringComparer.OrdinalIgnoreCase` on `orderReference` in `CreditExposure.Summarise`, matching MS-SQL's CI collation, plus §4.4's uppercase-only reference alphabet | **`BC28`** unit case (`B4`); armed by switching the comparer to `Ordinal` |
| **L15** | B15 | Money never becomes `decimal` or a float on the way to the column | JavaScript had one number type; the risk did not exist | `Money` is `long` minor units; `decimal` is banned from `Domain/` by `DomainDecimalTests`. **This is the first #8 service whose domain is entirely money**, so the rule is load-bearing rather than vacuous | `DomainDecimalTests` (already green, now non-trivially so); `tasks.md` B11 records the coverage |
| **L16** | B16 | Facts are published in the order they were emitted | MySQL `AUTO_INCREMENT` assigned in insert order through the ORM's batch | The per-row awaited `INSERT` copied verbatim from `EfCoreOrderRepository.InsertOutboxRowAsync` — EF Core's SQL Server provider does **not** preserve `Add` order when assigning `IDENTITY` values, measured in feature 14 | Inherited `OutboxSeqIdentityTests` (phase 6) + `BC16`; `tasks.md` D3 forbids replacing the loop with `AddRange` and F4 arms it |
| **L17** | B15, B17 | "Insert-or-leave-alone" is never rendered as check-then-act | `INSERT … ON DUPLICATE KEY UPDATE`, where the statement *is* the unit of atomicity | **Not needed, and deliberately not rendered.** Every row written here is a brand-new ledger or outbox row inside a transaction that already holds the line's exclusive lock; the `credits` row is never written at all. Stated because feature 45's defect was exactly a check-then-act rendering of this idiom and the next reader will look for it here | The absence is the guard: `tasks.md` D3 forbids `IF NOT EXISTS … INSERT` and `MERGE` in this service, and the reviewer is told to grep for both |
| **L18** | B18 | An absent optional field is omitted, not sent as `null` | `class-transformer` + Nest's serialiser | `JsonWire.Options` (`camelCase`, `DefaultIgnoreCondition = WhenWritingNull`) and **nullable** optional properties on the reply records. `CreditHoldReplyPayload.reason` must be absent on an approval, not `null` | `F3` asserts the exact key set for each `outcome`, armed by making `Reason` non-nullable |
| **L19** | B19 | Only codes the contract declares reach the wire, and a transient failure stays retryable | #7's orchestrator retried **every** `RpcError` code, so any code was safe | A closed mapping (§4.5) in which `CONFLICT` is **banned** and every transient store failure yields `UNAVAILABLE`/`TIMEOUT`/`INTERNAL_ERROR`. Feature 42 made nine codes terminal in #8; a deadlock victim answered `CONFLICT` would end the order's saga permanently while satisfying `R39`'s text exactly | **`BC27`** unit, reading the terminal set from `NatsSagaCommandsAdapter`'s own classification (`E6`); armed by mapping 1205 to `CONFLICT` |
| **L20** | B20 | Every fact lands on its order's partition | The relay's `correlationId` key, unchanged | The same relay, copied — and `BC1`'s refusal of a header-less hold, which is what keeps the key non-arbitrary | `BC16` integration reads the key back off a real broker (`F5`) |
| **L21** | B21 | Independent requests are handled concurrently, each with its own injection scope | `@nestjs/microservices`' NATS server ran every request on its own promise chain with its own request scope | A bounded `SemaphoreSlim` acquired before the scope, a tracked task, and **one `IServiceScope` per request** — `BillingDbContext` is scoped and not thread-safe. Copied from `StockRpcResponder`, whose own precedent (`OrdersCreateResponder`) is deliberately **sequential** | **`BC21`** integration (answer a second request while the first is blocked on a held `credits` row lock) + unit scope case (`E8`, `H7`); armed by reverting to `await HandleAsync(...)` |
| **L22** | B22 | Shutdown drains in-flight work without one failure aborting it | Nest's `enableShutdownHooks()` + `app.close()` awaited each transport's own teardown; one handler's rejection did not abort the sequence | `StopAsync` awaits every in-flight task **individually**, logs faults and returns normally (§4.7). `Task.WhenAll` rethrows the first fault — the shape `StockRpcResponder` has today (backlog id 50) | **`BC22`**, both halves, in **both** services (`E7`); armed twice — revert to `Task.WhenAll` (fault half), remove the wait (drain half) |
| **L23** | B23 | One transaction per unit of work, at a stated isolation level, without a `tx` parameter | Drizzle's explicit `TransactionContext` threaded through every repository call | The scoped `DbContext`'s ambient transaction — every collaborator in the same DI scope enlists automatically. `IsolationLevel.ReadCommitted` is stated explicitly, not inherited. A **simplification**; and under RCSI "read committed" means *statement-scoped snapshot*, which is why `L6`–`L8` need hints that #7 did not | `BC10` integration: a forced rollback leaves neither the ledger row nor the outbox row (`D7`) |
| **L24** | B24 | An adapter cannot say `over_limit` | `AdapterRejectionReason = Exclude<CreditRejectionReason, 'over_limit'>` — a structural type subtraction | A **separate closed enum** with the two simulator members and a total mapping to `CreditRejectionReason` in one place. C# has no type subtraction; the compile-time guarantee is a distinct type rather than a narrowed one, and the exhaustive `switch` is what makes adding a member a compile error | **`BC14`** type half (`C4`) + the handler-level fact assertion (`C7`) that #7 was **rejected** for not having |
| **L25** | B25 | The exposure sums cannot wrap | JavaScript numbers do not wrap (they lose precision above 2⁵³, which no amount here approaches), so there was nothing to supply | **Three lines, §3.3.** (1) `CreditExposure.Summarise` accumulates inside an explicit `checked` region and raises `CreditLedgerOverflowError`; explicit loops, **not** `Enumerable.Sum`, because *"the library happens to throw"* is the answer this ledger exists to refuse. (2) `Money.Add`/`Subtract`/`Multiply` in `src/SharedKernel/Money.cs` become `checked`, so `creditLimit − committedExposure` cannot wrap either. (3) `Reconstitute`'s refusal of an over-limit snapshot is the **outer bound** — it blocks one route to an overflowing ledger, which is why it was never sufficient on its own. The first pass of this spec recommended stating the bound and not guarding it; the gate overruled it, on the ground that #8 has already shipped one money-width defect whose requirement text was satisfied exactly | **`BC30`.** `tasks.md` **B12** — a unit test driving `Summarise` **directly** with overflowing entry snapshots (no aggregate, no database), armed by removing `checked`; **B13** — the three `Money` cases, armed the same way; **B10** — `Reconstitute`'s refusal, the outer bound, unchanged |
| **L26** | B20 | Two services' producers never share a Kafka client identity | The per-service `kafka.config.ts`, which #7 **excluded** from its parity family precisely so it could hold this value — the property came free from the file's existence | `KafkaOptions.cs` is now byte-identical in all three services (§8.2.4), so the file can no longer hold it. `ClientId` is declared `required` with **no default**, and each service's own options class supplies it; librdkafka would otherwise substitute its own default `rdkafka` for an empty value, **silently**, which is exactly the shared identity `.env.example` warns against. This row exists because the widening of `OB1` *removed* a property, and a ledger that only records properties gained would not have noticed | **`BC29`.** The guard is the **compiler** (`CS9035`), armed in `tasks.md` **A8** by deleting the initialiser and recording the build error verbatim |
| **L27** | B16 | The outbox writer is one implementation, not three that resemble each other | TypeScript's **structural** typing: #7's `outbox-recorder.ts` accepted anything with the envelope's shape and called a mapper resolved by shape, so one file served every service without a seam | C# is **nominally** typed, so identity has to be constructed: a neutral base-record name (`FactEvent`, §8.2.2) in each service's own `Domain.Events` namespace, and a per-service `IFactPayloadMapper` port (§8.2.3) the byte-identical writer dispatches through. This is the row the first pass would not have written, because it called the work *"a redesign of two shipped services in service of a guard"* rather than *"the property #7 got from its type system, priced"* | **`BC17`** at full scope. `tasks.md` **F6** arms `OB1` file by file over all **seven**; `tests/Orders.IntegrationTests/OutboxWireParityTests.cs` and `tests/Contracts.UnitTests/GoldenEnvelopeParityTests.cs` are the net that proves the refactor changed no payload byte |

## 16. Out of scope — restated

- **The `.99` rule, `CREDIT_FAILURE_RATE`, `R42` – `R44`**: feature 20. The port is here; the simulator is not.
- **`invoice.issue`, the `Invoice` aggregate, the `Consume` caller**: feature 21. `Consume` is delivered, unit-tested and uncalled.
- **`payment.register`, the `invoice_paid` release caller**: feature 22, with §8.5's ordering note waiting for it.
- **The `orders.cancel` responder** that will call `billing.credit.release`, and `saga.md` §2's missing table row for that subject: feature 41.
- **DLQ, retries, metrics, tracing, `traceparent` / `x-deadline-ms`**: feature 27.
- **Gateway callers of `billing.credit.list`**: feature 25.
- **Backlog id 52**: §10.3 — it stands, because its own acceptance defines it as standalone work that fixes nothing in place. Ids **48**, **53** and **54** are **in** scope after the 2026-09-05 ruling — §10.4, §10.5, §10.6.
- **A messaging-family parity guard**: recorded in §8.3 and not taken — #8 has no bare-JSON pair, so the family #7 worried about does not exist here.
