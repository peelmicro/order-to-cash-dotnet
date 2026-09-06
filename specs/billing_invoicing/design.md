# `billing_invoicing` — Design (.NET 10 / EF Core / MS-SQL, assessment #8)

> **Stack-specific.** This file is where the C#, EF Core, MS-SQL, `NATS.Client.Core`, `Confluent.Kafka`, `src/Cqrs` and Testcontainers detail lives. Nothing here belongs in `specs/shared/`; assessment #9 writes its own equivalent against the same `R45` and `R46`.
>
> Authorities: [`specs/shared/domain-model.md`](../shared/domain-model.md) §5.2 – §5.3 (`Invoice`, `InvoiceLine`, `Payment`, **B6** – **B10**), §5.1 (the `consume` entry), §7.1 – §7.2 (the envelope and fact 10), §8 (cross-cutting rules); [`specs/shared/saga.md`](../shared/saga.md) §2, §3.1 steps 4 – 5, §5, §6; [`specs/shared/asyncapi.yaml`](../shared/asyncapi.yaml); [`specs/outbox_and_idempotency/design.md`](../outbox_and_idempotency/design.md) §4 – §6; **and above all [`specs/billing_credit/design.md`](../billing_credit/design.md), which is this service's shape.** Invoicing *extends* the Billing service built by feature 19 — the same responder, the same bare-JSON wire, the same `IUnitOfWork`, the same repository-drains-the-aggregate discipline, the same error vocabulary, the same outbox writer. Nothing about that shape is re-invented here; every section below says either "feature 19's, reused" or "new, and why".
>
> **#7's design is the port source, not the template.** `order-to-cash-nestjs/specs/billing_invoicing/design.md` is read section by section in §15's delta table. Three of its largest sections — the migration, the DTO/`class-validator` layer and the Nest controller — have no counterpart here, and two of its own review findings (`N2` blocking, `N10` non-blocking) are written into this design as prevention rather than inherited as risk.

## 1. Scope

**In scope.**

- The `Invoice` aggregate root, the `InvoiceLine` child entity, the closed `InvoiceState` hierarchy, invariants **B6** – **B9**, the `InvoiceIssued` and `PaymentReceived` domain facts, and `MarkPaid` (delivered, uncalled — feature 22's seam).
- Two NATS RPC subjects on the existing responder: `billing.invoice.issue` (`R45`) and `billing.invoice.list`.
- The `Consume` ledger call that gives `R40` its first live caller, inside the invoice-issue transaction.
- The `INV-######` allocator: a port and an EF Core counter-table adapter, a verbatim copy of the **fixed** despatch allocator.
- Four shipped Billing types renamed from credit-scoped to service-scoped (`BI31`, gated).
- The two carried backlog items: the `.99` fixture guard (`BI17`) and `BC32`'s six remaining sites (`BI30`), plus the stale `AlwaysApproveCreditDecision` header (`BI20`) and the missing `BI21` guard in Orders' unit tests.

**Out of scope.** The remittance intake — `billing.payment.register`, the `Payment` entity, the `payments` table writes, dedup by `paymentReference`, the `Release(invoice_paid)` call, `credit.released.v1` on payment (feature 22, `R47` – `R49`); the Gateway's callers of `billing.invoice.list` and the "Register payment" button (features 25 and 29); DLQ, retry policy, metrics, tracing, health (feature 27); the projector's timeline entries for `invoice.issued.v1` (feature 24); credit notes, dunning, partial payment and partial invoicing (out of the model, `domain-model.md` §9); backlog ids 47, 52 and 56.

## 2. Where everything lives

```
src/Billing/
  Domain/
    Invoice.cs                       aggregate root (§3.1): Issue / MarkPaid / Reconstitute / ToSnapshot, B6-B9
    InvoiceState.cs                  the closed two-case hierarchy + InvoiceStatuses tokens (§3.2, BI23, BI27)
    InvoiceLine.cs                   child entity (§3.3) — maps to the `invoice_items` table
    InvoiceSnapshot.cs               the plain snapshot shapes the row mapper reconstitutes from
    Events/InvoiceIssued.cs          FactEvent subtype (§3.5)
    Events/PaymentReceived.cs        FactEvent subtype (§3.5)
    Errors/EmptyInvoiceLinesError.cs
    Errors/InvoiceLineCurrencyMismatchError.cs
    Errors/NegativeInvoiceTotalError.cs
    Errors/InvalidInvoiceSnapshotError.cs
    Errors/InvoiceAlreadyPaidError.cs
    Errors/InvoicePaymentAmountMismatchError.cs
    Errors/InvoicePaymentCurrencyMismatchError.cs
    Errors/InvoiceTotalOverflowError.cs        BI25
    Errors/UnknownInvoiceStatusError.cs        BI27
  Application/
    Ports/IInvoiceRepository.cs                §6.1 — FindByOrderReference / LockByOrderReference / SaveAsync
    Ports/IInvoiceReadPort.cs                  §6.1 — ListAsync(query, now, ct); never locks, never mutates
    Ports/IInvoiceNumberAllocator.cs           §6.1 — AllocateNextAsync(ct)
    Commands/IssueInvoiceCommand.cs            command + thin ICommandHandler delegating to the service
    Queries/ListInvoicesQuery.cs               query + thin IQueryHandler
    InvoiceIssueService.cs                     the transactional unit as a plain class (§5.2, §7.2)
    InvoiceApplicationErrors.cs                InvoiceCurrencyMismatchError (§5.3)
  Infrastructure/
    Persistence/
      EfCoreInvoiceRepository.cs               §6.2
      InvoiceRowMapper.cs                      §6.3 — the ONE place `status`/`paid_at` meet InvoiceState
      EfCoreInvoiceReadRepository.cs           §6.5
      EfCoreInvoiceNumberAllocator.cs          §6.4 — COPY OF EfCoreDespatchNumberAllocator
    Messaging/Rpc/InvoiceRpcPayloads.cs        §4.3 — Billing's OWN records, parsed-from-spec guarded
    Outbox/BillingFactPayloadMapper.cs         renamed from CreditFactPayloadMapper, + two arms (§4.6, BI31)
  Presentation/
    BillingRpcResponder.cs                     renamed from CreditRpcResponder, + two subjects (§4.1, BI31)
    Rpc/InvoiceSubjects.cs                     §4.2
    Rpc/InvoiceRequestValidator.cs             §4.4
    Rpc/BillingErrorMapper.cs                  renamed from CreditErrorMapper, + this feature's cases (§4.5)
  Infrastructure/BillingOptions.cs             CreditResponderOptions -> BillingResponderOptions (BI31)
  Infrastructure/BillingServiceCollectionExtensions.cs   four new scoped registrations (§5.4)
  Infrastructure/CreditDecisions/AlwaysApproveCreditDecision.cs   header only (BI20)

tests/Billing.UnitTests/
  InvoiceTests.cs                    R45/R46 domain halves, BI10, BI11, BI14, BI25
  InvoiceStateTests.cs               BI23, BI27
  InvoiceFactTests.cs                BI13
  InvoiceIssueServiceTests.cs        BI5 unit half, BI8 three-lock order, fast path, reply-after-commit
  InvoiceResponderValidationTests.cs BI2 (entry observation), BI25 validator half
  InvoiceSubjectsTests.cs            BI16 half
  InvoiceRpcPayloadTests.cs          BI28
  InvoiceNumberAllocatorTests.cs     BI12 formatting half
  BillingConsumesNoFactsTests.cs     BI1
  BillingResponderSubjectCoverageTests.cs  BI31
  CentsRuleFixtureGuardTests.cs      BI17
  BillingErrorMapperTests.cs         renamed from CreditErrorMapperTests, + BI25/BI26 cases
tests/Billing.IntegrationTests/
  InvoiceIssueTests.cs               R45 integration half, BI2-BI6, BI9
  InvoiceIssueRaceTests.cs           BI8
  InvoiceListTests.cs                BI15
  InvoiceWireTests.cs                BI16
  InvoiceRepositoryTests.cs          BI7, BI10 store half, BI24
  InvoiceReadRepositoryTests.cs      BI15 SQL half
  InvoiceNumberAllocatorTests.cs     BI12 concurrency half, BI29
  CentsRuleFixtureGuard.cs           the runtime half of BI17 (test-support, not a test)
  BillingHostFixture.cs              EXTENDED: SeedInvoiceAsync, InvoicesOfAsync, IssueRequest builder
  CreditListTests.cs                 BI30 — three sites
tests/Fulfillment.IntegrationTests/
  StockListTests.cs                  BI30 — three sites
tests/Orders.UnitTests/
  SagaCommandPayloadTests.cs         BI21
```

**Layering.** Unchanged from feature 19: `Domain/` references only `OrderToCash.SharedKernel`; ports live in `Application/`; every adapter in `Infrastructure/`; `OrderToCash.Cqrs` is an Application-layer concern and no `Domain/` namespace may reference it. NetArchTest already enforces all of it, and `DomainDecimalTests` already forbids `decimal` in `Domain/`.

## 3. The domain

### 3.1 `Invoice` — the aggregate root

```csharp
public readonly record struct InvoiceContext(DateTimeOffset OccurredAt, UniqueId CausationId);

public sealed record IssueInvoiceInput(
    UniqueId Id,
    string InvoiceReference,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    IReadOnlyList<InvoiceLineInput> Lines,   // ProductCode, Quantity Units, Money UnitPrice
    Money Discount,                          // Money.Zero(currency) when the request omits it
    UniqueId CorrelationId);                 // the ORDER id

public sealed record MarkPaidInput(
    string PaymentReference,
    Money Amount,                            // must equal TotalAmount exactly — B10
    DateTimeOffset ValueDate,
    string Source,                           // operator | robot | test
    UniqueId CorrelationId);

public sealed class Invoice : AggregateRoot
{
    private readonly List<InvoiceLine> _lines = [];
    private InvoiceState _state;              // ONE field. §3.2.

    public static Invoice Issue(IssueInvoiceInput input, InvoiceContext ctx, Func<UniqueId> newId);
    public static Invoice Reconstitute(InvoiceSnapshot snapshot);

    public string InvoiceReference { get; }
    public DateTimeOffset InvoiceDate { get; }
    public OrderNumber OrderReference { get; }
    public string RetailerCode { get; }
    public string CompanyCode { get; }
    public string Currency { get; }
    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public Money Amount { get; }
    public Money Discount { get; }
    public Money TotalAmount { get; }

    public string Status => InvoiceStatuses.ToToken(_state);        // projection of _state
    public DateTimeOffset? PaidAt => _state.PaidAtOrNull;           // projection of _state

    public void MarkPaid(MarkPaidInput input, InvoiceContext ctx, Func<UniqueId> newId);
    public InvoiceSnapshot ToSnapshot();
}
```

`Issue` is the only way an `Invoice` comes into being. It derives the totals, refuses every **B6** violation, and `Raise`s exactly one `InvoiceIssued` **before returning**, so a caller can never observe an `Invoice` whose fact was not recorded — the `OrderDespatch.Create` precedent from feature 18. There is no `Cancel`, no `Void`, no `CreditNote`: `domain-model.md` §5.3 gives the invoice exactly one edge, and every other method a reader might expect is deliberately absent so an illegal transition is a compile error wherever the type system can reach.

### 3.2 `InvoiceState` — the design decision this feature turns on, and the C# rendering that keeps the property

The acceptance line asks for *"`paidAt` set exactly when status becomes paid"*. #7 did not enforce that with a check; it made it **unrepresentable**, with `status` and `paidAt` as one TypeScript discriminated union rather than two fields a mapper could desynchronise. The property must port. The mechanism cannot, because **C# has no structural unions**, so the rendering has to be chosen and justified rather than transliterated.

**Chosen: a closed abstract reference hierarchy.**

```csharp
public abstract record InvoiceState
{
    private protected InvoiceState() { }          // no external type can derive: the ctor is unreachable

    public abstract DateTimeOffset? PaidAtOrNull { get; }

    public sealed record Issued : InvoiceState
    {
        public override DateTimeOffset? PaidAtOrNull => null;
    }

    public sealed record Paid(DateTimeOffset PaidAt) : InvoiceState
    {
        public override DateTimeOffset? PaidAtOrNull => PaidAt;
    }
}
```

- `Paid` **cannot be constructed without its instant** — that is the half of #7's property that ports for free.
- `Issued` **has no `PaidAt` to set** — that is the other half.
- `private protected` on the base constructor closes the hierarchy: no type outside `OrderToCash.Billing` can derive from `InvoiceState`, so the two nested records are the whole of it.
- The aggregate holds **one** field; `Status` and `PaidAt` are read-only projections of it, and `MarkPaid` assigns `_state = new InvoiceState.Paid(ctx.OccurredAt)` in a single statement. There is no code path that writes one without the other, because there is nothing to write.

**Rejected: `readonly record struct` with a private constructor and factory methods.** This is the rendering that looks most idiomatic and it **loses the property**. C# gives every struct a `default` value that no constructor can prevent: `default(InvoiceState)` would have a null `Status` token and a null `PaidAt`, satisfying neither case, and it is reachable through an uninitialised field, an array allocation, `Array.Empty<T>`-style zeroing or a `default!` in a mapper. #7's union had no zero value; a struct always does. This is exactly the class the ported-idiom ledger exists for — a rendering that satisfies the requirement's words while dropping the property that made it correct — so the rejection is recorded here and the ledger carries it as `L4`.

**Rejected: `enum InvoiceStatus` plus a `DateTimeOffset?` field.** Two fields, discipline plus a test, which is the thing #7 deliberately did not build.

**What C# still does not give, stated plainly.** There is no exhaustiveness check over a class hierarchy: a `switch` on `InvoiceState` needs a `_ =>` arm, and the compiler will not fail a future third case. `InvoiceStatuses.ToToken` therefore throws `UnknownInvoiceStatusError` on that arm rather than returning a default, and `BI23`'s structural test asserts the hierarchy is still exactly two cases with an inaccessible base constructor — which is what makes adding a third case loud. That test is the guard the ledger names, and it fails if `private protected` is widened or a third nested case is added.

**The store side is the remaining hole, and it is the same hole #7 had.** `invoices` has two independent columns (`status nvarchar(20)`, `paid_at datetime2(3) NULL`), and a hand-edited row or a mapper bug can present `paid` with a null `paid_at`. `Reconstitute` closes it by **refusing** the snapshot (`InvalidInvoiceSnapshotError`, `BI10`), and `InvoiceRowMapper` is the single place the two representations meet (§6.3). The outbound direction cannot desynchronise at all, because `ToSnapshot` derives both columns from `_state`.

`InvoiceStatuses` is the token map: `ToToken(InvoiceState) → "issued" | "paid"` and `Parse(string) → InvoiceState` given a `paid_at`, raising `UnknownInvoiceStatusError` on anything outside the closed set (`BI27`). Parsing is **ordinal**; writing is always the lower-case contract token; the column's CI collation therefore never gets a chance to disagree with the domain's ordinal comparison.

### 3.3 `InvoiceLine` — the child entity

```csharp
public sealed class InvoiceLine : Entity
{
    public static InvoiceLine Create(UniqueId id, string productCode, Quantity units, Money unitPrice);
    public static InvoiceLine Reconstitute(InvoiceLineSnapshot snapshot);
    public string ProductCode { get; }
    public Quantity Units { get; }
    public Money UnitPrice { get; }
    public Money LineTotal => UnitPrice.Multiply(Units);   // the only arithmetic on a line
    // No mutator: lines are snapshotted at issue and never change (the OrderLine precedent).
}
```

**Naming, reconciled once.** `domain-model.md` §5.2 and `asyncapi.yaml` both call this an `InvoiceLine`; the phase-6 table is `invoice_items` and its row entity is `Entities.InvoiceItem`. The domain class follows the shared model — `InvoiceLine`, matching `OrderToCash.Contracts.Facts.InvoiceLine` — and `InvoiceRowMapper` is the single place the two vocabularies meet. `Domain.InvoiceLine` and `Contracts.Facts.InvoiceLine` are two different types with the same simple name in different namespaces, exactly as `Domain.BuyerCredit` and `Entities.Credit` already are; the payload mapper aliases them the way `CreditFactPayloadMapper` already aliases `ContractsPayloads`.

### 3.4 Totals — derived, never assigned (**B6**), and `checked`

```
amount      = Σ (unitPrice × units)      -- Money arithmetic, one currency, integer minor units
totalAmount = amount − discount
```

Computed inside `Issue` and recomputed inside `Reconstitute` for comparison against the stored columns. `Invoice` exposes no setter for any of the three. Refusals: an empty line list (`EmptyInvoiceLinesError`), a line whose currency differs from the invoice's (`InvoiceLineCurrencyMismatchError`), a negative `totalAmount` (`NegativeInvoiceTotalError`), a snapshot whose stored totals disagree with its lines or whose `status`/`paid_at` disagree (`InvalidInvoiceSnapshotError`).

**The accumulation runs inside one explicit `checked` region and converts `OverflowException` into `InvoiceTotalOverflowError`** — the shape `CreditExposure.Summarise` already ships (`billing_credit/design.md` §3.3, `BC30`). Written as an explicit loop over `LineTotal`, deliberately **not** `Enumerable.Sum`: `Money.Multiply` and `Money.Add` are already `checked` and would raise anyway, but "the library happens to throw" is the answer the ledger exists to refuse, and the point of the region is the **conversion** — a `DomainError` with a stable `Code` instead of a framework exception with none. §4.5 explains why the difference is not cosmetic.

### 3.5 The two facts

`Domain/Events/InvoiceIssued.cs` and `Domain/Events/PaymentReceived.cs` are `FactEvent` subtypes, exactly the shape `CreditApproved`/`CreditRejected`/`CreditReleased` already have: `EventId`, `AggregateId`, `CorrelationId`, `CausationId`, `OccurredAt` from the base, `EventType` overridden to the literal, and this fact's own fields.

- `InvoiceIssued` — `AggregateId` = the invoice's own id (`domain-model.md` §7.2 names `Invoice` as the producing aggregate of facts 10 and 11), `CorrelationId` = the order id, `CausationId` = `ctx.CausationId` (the request id), `OccurredAt` = `ctx.OccurredAt`, plus `OrderReference`, `InvoiceReference`, `InvoiceDate`, `RetailerCode`, `CompanyCode`, `Lines`, `Amount`, `Discount`, `TotalAmount`.
- `PaymentReceived` — the same envelope plus `PaymentReference`, `Amount`, `ValueDate`, `Source`.

One `Raise` call site each: `Invoice.Issue` and `Invoice.MarkPaid`. Neither is reachable from the application layer, the repository or the responder, so "the fact accompanies the state change" is structural. `FactCatalog.PayloadTypesByEventType` already carries `invoice.issued.v1` and `payment.received.v1`, so `OutboxWriter`'s catalogue check passes without a `Contracts` change.

### 3.6 Invariants → where they are enforced

| Invariant | Enforced by | Proven by |
|---|---|---|
| **B6** totals derive from lines, non-negative, one currency | `Issue` computes inside `checked`; `Reconstitute` refuses a disagreeing snapshot; no setter exists | `R45` domain unit, `BI11`, `BI25` |
| **B7** exactly one invoice per `orderReference` | the fast-path read, the in-transaction locking re-read under the `credits` lock, **and** the unique index that already exists | `BI9`, `BI8` |
| **B8** `issued → paid` is the only transition | there is no other mutator; `MarkPaid` throws on `paid` | `R46` domain unit, `BI14` |
| **B9** `paidAt` iff `paid` | one `InvoiceState` field, closed hierarchy; `Reconstitute` refuses a disagreeing row | `BI10`, `BI23` |
| **B10** amount must match (payment) | `MarkPaid` refuses a mismatched amount or currency | `BI14` (the `paymentReference` uniqueness half is feature 22's) |

## 4. Presentation

### 4.1 One responder, five subjects (`BI31` — gated)

`CreditRpcResponder` is **renamed `BillingRpcResponder`** and gains two subscription loops. Everything else about it is untouched: the `SemaphoreSlim` bound acquired before the scope, one `IServiceScope` per request, the `_inFlight` set, the individually-awaited `StopAsync` drain, the never-throws `try/catch` that replies a mapped `RpcErrorPayload`.

**Why not a second responder class, which is what #7 did.** #7 declared a second `@Controller` and paid nothing: NestJS supplied concurrency, per-request DI scope and graceful shutdown. In #8 all three are hand-built inside this class and each is guarded by a named test — `BC21` (a distinct scope per request), `BC22` (the drain, both halves), backlog id 50 (fault isolation, `Task.WhenAll` would rethrow the first fault and abort the host's shutdown). A second responder would fork four armed behaviours into an unguarded copy, and `CLAUDE.md`'s non-negotiable is **one `BackgroundService` per transport** — Billing's RPC transport is one. The rename follows the same reasoning #7 applied to its own integration harness (its open point 12: *"no longer credit-specific — extended, not forked"*); it simply lands on the responder here because the responder is where #8's hand-built behaviour lives.

**The full rename footprint, enumerated so the brief can be derived from it rather than guessed at:**

| Renamed | To | Files touched |
|---|---|---|
| `CreditRpcResponder` | `BillingRpcResponder` | `src/Billing/Presentation/CreditRpcResponder.cs` → `BillingRpcResponder.cs`; `src/Billing/Infrastructure/BillingServiceCollectionExtensions.cs`; `tests/Billing.UnitTests/{CreditResponderConcurrencyTests,CreditResponderHeaderTests,CreditResponderShutdownTests}.cs` |
| `CreditResponderOptions` | `BillingResponderOptions` | `src/Billing/Infrastructure/BillingOptions.cs`; `BillingServiceCollectionExtensions.cs`; `BillingRpcResponder.cs` |
| `CreditErrorMapper` | `BillingErrorMapper` | `src/Billing/Presentation/Rpc/CreditErrorMapper.cs` → `BillingErrorMapper.cs`; `BillingRpcResponder.cs`; `CreditRequestValidator.cs` (doc comment); `tests/Billing.UnitTests/CreditErrorMapperTests.cs` → `BillingErrorMapperTests.cs`; `tests/Billing.UnitTests/SqlExceptionFactory.cs` (doc comment) |
| `CreditFactPayloadMapper` | `BillingFactPayloadMapper` | `src/Billing/Infrastructure/Outbox/CreditFactPayloadMapper.cs` → `BillingFactPayloadMapper.cs`; `BillingServiceCollectionExtensions.cs`; `tests/Billing.IntegrationTests/{BillingOutboxRelayTests,BuyerCreditRepositoryTests}.cs` |

Fourteen files, every change mechanical, no behaviour change. `CreditRequestValidator`, `CreditSubjects` and `CreditRpcPayloads` keep their names — they really are credit-specific, and this feature adds `InvoiceRequestValidator`, `InvoiceSubjects` and `InvoiceRpcPayloads` beside them. `.editorconfig` requires the file name to match the type name, so each rename is a file rename too.

`ExecuteAsync` becomes five loops; `DispatchAsync`'s `switch` gains two arms. **Order inside the two new arms:** for `billing.invoice.issue`, extract and validate the headers **first**, then deserialise, then validate the payload, then dispatch — matching `BI2`'s wording. The existing `hold`/`release` arms validate the payload before the headers; that ordering is not this feature's to churn, both throw before dispatch, and both are already covered.

`billing.invoice.list` reads no headers, exactly as `billing.credit.list` does not.

### 4.2 Subjects

```csharp
public static class InvoiceSubjects
{
    public const string InvoiceIssue = "billing.invoice.issue";
    public const string InvoiceList  = "billing.invoice.list";
}
```

`InvoiceSubjectsTests` reads `specs/shared/asyncapi.yaml` as text and asserts each constant equals its channel's `address` character for character — the `CreditSubjectsTests`/`OrdersFactTopicTests` discipline, unchanged.

### 4.3 `InvoiceRpcPayloads.cs` — Billing's own records

Billing declares its own request/reply records rather than referencing Orders' `SagaCommandPayloads.cs`, per the established rule that RPC payloads live in the service that speaks them. Transcribed from `asyncapi.yaml`:

```csharp
public sealed record InvoiceIssueRequestPayload(
    string OrderReference, string RetailerCode, string CompanyCode, string Currency,
    IReadOnlyList<InvoiceLine> Lines, long? Discount = null);          // Contracts.Facts.InvoiceLine

public sealed record InvoiceIssueReplyPayload(
    string OrderReference, string InvoiceReference, DateTimeOffset InvoiceDate,
    string Currency, long TotalAmount, string Status, bool Created, Guid? InvoiceId = null);

public sealed record InvoicePageInfo(int Page, int PageSize, int Total);

public sealed record InvoiceListRequestPayload(
    int? Page, int? PageSize, string? Status = null, string? RetailerCode = null,
    string? CompanyCode = null, string? OrderReference = null, int? IssuedBeforeMinutes = null);

public sealed record InvoiceViewPayload(
    Guid InvoiceId, string InvoiceReference, DateTimeOffset InvoiceDate, string OrderReference,
    string RetailerCode, string CompanyCode, string Currency,
    long Amount, long Discount, long TotalAmount, string Status, DateTimeOffset? PaidAt = null);

public sealed record InvoiceListReplyPayload(IReadOnlyList<InvoiceViewPayload> Items, InvoicePageInfo Page);
```

`Contracts.Facts.InvoiceLine` is reused for the request's `lines` — it is already the `asyncapi.yaml` `InvoiceLine` shape, already used by Orders' own request record, and re-declaring it would create a second copy of a schema whose whole point is one copy.

`BI28`'s guard is `CreditRpcPayloadTests`' shape, one `[MemberData]` row per schema, resolving `allOf` through the existing `AsyncApiSchema.PropertyNamesOf` (which already handles `PageRequest`'s `allOf` for `CreditListRequestPayload`). Its second half asserts the serialised key set: an `issued` `InvoiceViewPayload` **omits** `paidAt` (nullable property + `JsonWire.Options`' `WhenWritingNull`), a `paid` one carries it. This is `L18`'s shape from feature 19, and a **recorded divergence from #7's bytes** — see §16 `L19` and §18.

### 4.4 `InvoiceRequestValidator`

Hand-rolled, the shape of `CreditRequestValidator` — no `class-validator` equivalent exists and none is added. `ValidateIssue`:

- `orderReference` matches `^ORD-[0-9]{6,}$` (the same alphabet `CreditRequestValidator` uses, which is also what closes the collation residual at the edge);
- `retailerCode`, `companyCode` non-empty and ≤ 20 characters;
- `currency` matches `^[A-Z]{3}$`;
- `lines` non-null and non-empty; each line's `productCode` non-empty, `units ≥ 1`, `unitPrice ≥ 0`;
- `discount`, if present, `≥ 0`;
- **the cross-field check**: `discount ≤ Σ(unitPrice × units)`, with the sum computed **inside an explicit `checked` region**. On `OverflowException` the validator reports a validation error naming the line rather than letting the sum wrap.

`ValidateList`: `page ≥ 1` (default 1), `pageSize` in `1..200` (default 25), `status` in `{issued, paid}` if present, party codes and `orderReference` validated when present, `issuedBeforeMinutes ≥ 0` if present.

Both raise `InvalidInvoiceRequestError`, mapped to `VALIDATION_FAILED`.

**Why the cross-field check is here and not only in the domain.** #7 put it only in the domain and was **rejected** for it: the request passed validation, the handler opened the transaction, took the `credits` lock and reached `invoiceNumbers.next(tx)` — the service's hottest row — before `NegativeInvoiceTotalError` rolled it back, and the sweeper's retries would contend there repeatedly on a permanently bad payload. The domain refusal stays as defence in depth; the validator is what makes `BI2`'s "before dispatching" clause true.

**Why the `checked` region is load-bearing.** C# arithmetic is unchecked by default and `CheckForOverflowUnderflow` is not set in `Directory.Build.props`. `unitPrice` is `int64` on the wire with no upper bound in the schema, so two lines of `long.MaxValue` wrap to a negative sum, and an unchecked `discount ≤ sum` would then **accept** a payload the domain refuses inside the transaction — reintroducing exactly the defect this check exists to prevent, one level down. Guarded by `BI25`'s validator case and armed by removing the `checked`.

### 4.5 `BillingErrorMapper` — extended, not replaced

Cases added ahead of the generic `DomainError` fallback:

| Error | Code | `details` |
|---|---|---|
| `InvalidInvoiceRequestError` (presentation) | `VALIDATION_FAILED` | message only — `BI2` |
| `InvoiceCurrencyMismatchError` (application) | `VALIDATION_FAILED` | `{ expected, received }` — `BI4`, identical treatment to `CreditCurrencyMismatchError` |
| `NegativeInvoiceTotalError`, `EmptyInvoiceLinesError`, `InvoiceLineCurrencyMismatchError` (domain) | `VALIDATION_FAILED` | `{ code }` — statements about the *request's* lines, not about an invoice's state |
| `InvoiceTotalOverflowError` (domain) | `DOMAIN_ERROR` | `{ code }` — `BI25`; **terminal on purpose** |
| `InvoiceAlreadyPaidError`, `InvoicePaymentAmountMismatchError`, `InvoicePaymentCurrencyMismatchError` (domain) | `PRECONDITION_FAILED` | `{ code }` — feature 22 maps them at its own subject; declared here so the vocabulary is complete on delivery |

`NoActiveHoldError` and `CreditLineNotFoundError` are **reused unchanged** from feature 19 — the same errors, raised from the same aggregate and repository, already mapped to `PRECONDITION_FAILED` with `{ code: "NO_ACTIVE_HOLD" }` and `NOT_FOUND` with `{ retailerCode, companyCode }`. `BI3` and `BI5` therefore need no mapper change at all; that is the measure of how well feature 19's seam was cut. `InvalidInvoiceSnapshotError` falls to the existing `DomainError → DOMAIN_ERROR` branch, as a programming-error guard rather than client input.

**`CONFLICT` stays banned** (`BC27`) and every transient store failure stays `UNAVAILABLE`.

**Why the terminal/transient split matters for two of this feature's codes, concretely.** `NatsSagaCommandsAdapter.IsTerminalRpcErrorCode` classifies `VALIDATION_FAILED`, `NOT_FOUND`, `PRECONDITION_FAILED` and `DOMAIN_ERROR` as terminal, and `INTERNAL_ERROR`, `UNAVAILABLE`, `TIMEOUT` and anything unrecognised as transient. So:

- `BI5`'s `PRECONDITION_FAILED` makes `SagaCommandDispatcher` short-circuit the retry loop, call `RejectAsync` with the message and log at `Error` — the two write models' disagreement stops immediately, visibly and with its reason recorded. #7 got the same property indirectly, by retrying to exhaustion and then parking. `BI26` records the dependency so it is explicit rather than accidental, and its test reads the classification from the adapter's own closed set rather than restating it — `BC27`'s shape.
- `BI25`'s overflow **must not** be allowed to fall to `INTERNAL_ERROR`, because a permanently-overflowing payload would then be retried on every sweep forever. Converting it to a `DomainError` with a stable code is what makes it terminal. This is the whole reason the `checked` region converts rather than merely raises.

### 4.6 `BillingFactPayloadMapper` — two arms added

`ToPayload`'s `switch` gains `InvoiceIssued → Contracts.Facts.Payloads.InvoiceIssuedPayload` and `PaymentReceived → PaymentReceivedPayload`, mapping `Money.MinorUnits` to `long` and `Domain.InvoiceLine` to `Contracts.Facts.InvoiceLine`. The existing `_ => throw` arm is unchanged and still names the CLR type and the `eventType`. No `Contracts` change: both payload records and both `FactCatalog` entries already exist.

## 5. The application layer

### 5.1 Buses and handlers

| Subject | Message | Handler | Transactional? |
|---|---|---|---|
| `billing.invoice.list` | `ListInvoicesQuery(InvoiceListRequestPayload)` | `ListInvoicesQueryHandler` → reads `IClock.UtcNow` **once**, calls `IInvoiceReadPort.ListAsync(query, now, ct)` | no — two plain `SELECT`s |
| `billing.invoice.issue` | `IssueInvoiceCommand(request, CorrelationId, RequestId)` | `IssueInvoiceCommandHandler` → `InvoiceIssueService.IssueAsync` | yes — §7 |

The same thin-handler / plain-service split feature 19 uses, for the same reason: the plain class can be `new`ed with fakes in a unit test. Registration is by assembly scan (`AddDispatcher`), and `ValidateOnBuild = true` fails the boot if a command has no handler or two.

### 5.2 Ports

```csharp
public interface IInvoiceRepository
{
    /// B7 fast path: a non-transactional, un-hinted read by orderReference, before any transaction is opened.
    Task<InvoiceSnapshot?> FindByOrderReferenceAsync(OrderNumber orderReference, CancellationToken ct);

    /// B7 authority: the same read under WITH (UPDLOCK, HOLDLOCK, ROWLOCK), INSIDE the ambient
    /// transaction and AFTER the credits row lock (§7.2 step 2).
    Task<InvoiceSnapshot?> LockByOrderReferenceAsync(OrderNumber orderReference, CancellationToken ct);

    /// INSERTs the invoice row and its line rows, then drains invoice.DomainEvents into outbox rows,
    /// then SaveChangesAsync — all inside the ambient transaction. Never an UPDATE on this path.
    Task SaveAsync(Invoice invoice, CancellationToken ct);
}

public interface IInvoiceReadPort
{
    Task<InvoiceListReplyPayload> ListAsync(InvoiceListRequestPayload query, DateTimeOffset now, CancellationToken ct);
}

public interface IInvoiceNumberAllocator
{
    Task<string> AllocateNextAsync(CancellationToken ct);
}
```

**No `tx` parameter anywhere**, exactly as `IBuyerCreditRepository`, `IStockItemRepository` and `IOrderRepository` already have none: the ambient transaction comes from the caller's DI scope. §7.3 is where that simplification is priced.

`ListAsync` takes `now` as a parameter rather than injecting a clock into the adapter, so the SQL adapter stays a pure translation of a query into statements and `BI15`'s `issuedBeforeMinutes` case is drivable by a fixed instant with no container-level fake. `now.UtcDateTime` is what reaches the parameter, so a `datetime2(3)` column is never compared against a `datetimeoffset`.

**`IBuyerCreditRepository` is reused unchanged.** `LockForOrderAsync(retailerCode, companyCode, orderReference, ct)` already returns exactly what the consume needs — the locked credit line with that order's entries — and `SaveChangesAsync(credit, ct)` already inserts `AppendedEntries` and drains the (empty) event list. No port change, no new method, no signature change.

### 5.3 Application errors

`InvoiceApplicationErrors.cs`: `InvoiceCurrencyMismatchError(expected, received)` (`BI4`) — a contract violation of the incoming command rather than a statement about an invoice's state, mirroring `CreditCurrencyMismatchError`. `NoActiveHoldError` is **not** re-declared: feature 19 already put it in `Domain/Errors/` with the stable code `NO_ACTIVE_HOLD`, raised by `BuyerCredit.Consume` itself, which is where `BI5`'s refusal comes from.

### 5.4 Registrations

Four lines in `BillingServiceCollectionExtensions.AddBilling`, all **scoped**, beside the existing credit ones:

```csharp
services.AddScoped<IInvoiceRepository, EfCoreInvoiceRepository>();
services.AddScoped<IInvoiceReadPort, EfCoreInvoiceReadRepository>();
services.AddScoped<IInvoiceNumberAllocator, EfCoreInvoiceNumberAllocator>();
services.AddScoped<InvoiceIssueService>();
```

The lifetime is not incidental: everything that must share the ambient transaction must share the scope, and `ValidateScopes = true` (forced on in every environment by `BillingHost`) turns a singleton that captures the scoped `BillingDbContext` into a **boot failure** rather than a silent cross-transaction write. §16 `L20` records that this is what replaces #7's explicit `TransactionContext` parameter.

No new hosted service. No change to `Program.cs`.

## 6. Persistence

### 6.1 What already exists

`invoices`, `invoice_items`, `invoice_number_sequences` and `payments` were created by the phase-6 migration. `invoices` already has the unique index on `invoice_reference`, the unique index on `order_reference` (which is `B7` made mechanical) and the `(status, invoice_date)` index the demo bank robot will poll; `IndexTests` already asserts all three, `SchemaColumnTypeTests` asserts the column types, `ForeignKeyTests` asserts `invoice_items → invoices` cascade, and `NoMoneyColumnIsIntTests` already enumerates every `int` column in `otc_billing` and permits exactly `invoice_items.units`, `invoice_number_sequences.id` and `invoice_number_sequences.next_value`. **This feature adds no migration and touches no `*Configuration.cs`.**

### 6.2 `EfCoreInvoiceRepository`

- `FindByOrderReferenceAsync` — `AsNoTracking()` `SingleOrDefaultAsync` on `invoices` by `order_reference`, plus one read of `invoice_items` by `invoice_id`; no hint, no transaction. Under `READ_COMMITTED_SNAPSHOT` this takes no lock and blocks nobody, which is the point of a fast path the sweeper hits routinely. Returns the snapshot, not the aggregate — the fast path only needs to build a reply, and reconstituting would raise on a hand-broken row the caller is not being asked to repair. (The `EfCoreDespatchRepository.FindByOrderReferenceAsync` precedent, verbatim.)
- `LockByOrderReferenceAsync` — the same two reads inside the ambient transaction, the parent under `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` via `FromSqlInterpolated`, with the literal column list exposed as a `static readonly IReadOnlyList<string>` so a projection test can compare it against the `IEntityType`'s mapped properties mechanically — the `EfCoreBuyerCreditRepository.CreditClaimColumnNames` shape. **Never `db.Invoices.Where(...).FirstOrDefaultAsync()` on this path**: LINQ composition drops the hint and hands back an unlocked versioned read that looks identical in code review (ledger `L7`'s established hazard).
- `SaveAsync` — `db.Invoices.Add(row)`, then one `db.InvoiceItems.Add(...)` per line, then `foreach (var outboxRow in outboxWriter.BuildRows(invoice.DomainEvents)) await InsertOutboxRowAsync(...)` — one awaited `INSERT` at a time, copied verbatim from `EfCoreBuyerCreditRepository.InsertOutboxRowAsync`, never `AddRange` — then `db.SaveChangesAsync`, then `invoice.ClearDomainEvents()` only after everything above returned (`OI9`). There is no `UPDATE`, no `DELETE` and no `MERGE` anywhere in this file; feature 22 adds the `MarkPaid` update to this same class.

### 6.3 `InvoiceRowMapper`

The one place the two representations meet, and the one place `BI10`'s store-side hole is closed.

- `ToSnapshot(Entities.Invoice row, IReadOnlyList<Entities.InvoiceItem> items)` → `InvoiceSnapshot`, with `InvoiceStatuses.Parse(row.Status, row.PaidAt)` producing the `InvoiceState` and raising `UnknownInvoiceStatusError` on an unrecognised token; `Reconstitute` is what refuses the `paid`-with-null / `issued`-with-a-date disagreement, because that is an aggregate invariant rather than a mapping question.
- `ToNewRow(Invoice invoice, DateTime now)` → both columns derived from `_state` via `ToSnapshot`; they cannot disagree.
- Instants: `value.UtcDateTime` on the way to the column; `new DateTimeOffset(DateTime.SpecifyKind(row.InvoiceDate, DateTimeKind.Utc))` on the way back, and for the nullable one `row.PaidAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(row.PaidAt.Value, DateTimeKind.Utc))`. `datetime2(3)` carries no offset and EF Core returns `DateTimeKind.Unspecified`; `new DateTimeOffset(unspecified)` applies the **machine's local** offset. `BI24` is the guard, run under a non-UTC `TZ`, covering both the null and the non-null case.
- Amounts: `long` minor units both ways. No cast, no `decimal`, anywhere on this path.

### 6.4 `EfCoreInvoiceNumberAllocator`

A verbatim copy of `src/Fulfillment/Infrastructure/Persistence/EfCoreDespatchNumberAllocator.cs` with `DES-` → `INV-`, `despatches` → `invoices`, `despatch_reference` → `invoice_reference` and `despatch_number_sequences` → `invoice_number_sequences`, carrying a `// COPY OF —` banner naming its source **and** naming why the source is the one to copy:

```sql
INSERT INTO dbo.invoice_number_sequences (id, next_value)
SELECT 1, seed.next_value
FROM (
    SELECT ISNULL(MAX(CAST(SUBSTRING(invoice_reference, 5, LEN(invoice_reference) - 4) AS int)), 0) + 1 AS next_value
    FROM dbo.invoices
) AS seed
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.invoice_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1
);

SELECT * FROM dbo.invoice_number_sequences WITH (UPDLOCK, ROWLOCK) WHERE id = 1;   -- then next_value += 1
```

The seed and its existence test are **one statement**. The check-then-act rendering (`IF NOT EXISTS (SELECT …) INSERT`) is the defect feature 45 fixed for `ORD-`; this is the third instance of the idiom and it is copied, not re-derived. `BI29` is the guard: N concurrent allocations against a **fresh** database with no counter row, asserting N distinct references, no duplicate-key failure and no gap.

The self-initialising `MAX(CAST(SUBSTRING(...)))` is what makes the first live allocation `INV-000006` rather than colliding with the seed's `INV-000001` … `INV-000005`. Backlog id 47 (the scan's cost) is that family's, not this feature's.

**Not added to a parity guard.** There are now three near-identical allocators. Unlike the outbox family they are not one implementation duplicated — each names its own table, column and prefix — so a byte-identity guard would need the same service-neutral indirection feature 19 built for the relay. Recorded, deliberately not smuggled in.

### 6.5 `EfCoreInvoiceReadRepository`

Two queries, no transaction, no lock hint — under RCSI these are versioned reads that block nobody. `AsNoTracking()`, the four optional equality filters, plus `invoice_date <= @cutoff` where `cutoff = (now - TimeSpan.FromMinutes(issuedBeforeMinutes)).UtcDateTime` when that filter is supplied; `ORDER BY invoice_date DESC, invoice_reference DESC`; `Skip`/`Take`; and a `CountAsync` over the same filter for `PageInfo.Total`. Lines are **not** joined — `InvoiceView` does not carry them and the robot pages over hundreds of rows. `PaidAt` is a nullable `DateTimeOffset?` on the reply record, so `JsonWire.Options` omits the key for an `issued` invoice (§4.3, `BI28`).

This row exists in the ledger (`L12`) precisely so nobody "helpfully" adds a hint here.

## 7. The transaction, the lock protocol, and the two-aggregate deviation

### 7.1 The fast path

`InvoiceIssueService.IssueAsync` first calls `invoices.FindByOrderReferenceAsync(orderReference)` **outside any transaction**. On a hit it returns `created: false` immediately — no transaction opened, no lock taken. This matters because the sweeper retries and `saga.md` §6 layer 3 makes repeats routine rather than exceptional. (The `DespatchCreationService` precedent, verbatim.)

### 7.2 Inside one `IUnitOfWork.ExecuteAsync`

```
1. credits.LockForOrderAsync(retailerCode, companyCode, orderReference)     -- ALWAYS the first lock (BI8)
     WITH (UPDLOCK, HOLDLOCK, ROWLOCK) on `credits`, then the committed-exposure scalar
     and this order's `credit_items`, both under the same hints.
   null  -> CreditLineNotFoundError -> NOT_FOUND (BI3). Nothing written, no fact.

2. invoices.LockByOrderReferenceAsync(orderReference)                        -- the B7 authority (BI8)
   hit   -> return created:false with the existing values (BI9). Nothing was written; the
            transaction commits having done nothing.

3. currency check against the credit line -> InvoiceCurrencyMismatchError (BI4)

4. credit.Summary.ByOrder -> ActiveHold(order) == 0 -> NoActiveHoldError (BI5).
   Nothing written, no fact, and the order's existing ledger rows are untouched.

5. invoiceNumbers.AllocateNextAsync()                                        -- the LAST lock (BI8)
     WITH (UPDLOCK, ROWLOCK) on `invoice_number_sequences`.

6. var invoice = Invoice.Issue(...)          -> raises exactly one InvoiceIssued
   var entry   = credit.Consume(orderRef,..) -> appends ONE `consume` entry, raises NOTHING (R40)

7. invoices.SaveAsync(invoice)   -> INSERT invoices + INSERT invoice_items + ONE outbox row
   credits.SaveChangesAsync(credit) -> INSERT one credit_items row; drains an EMPTY event list
   COMMIT
```

- **`ctx = new InvoiceContext(clock.UtcNow, command.RequestId)` is read once** and used for the invoice's `InvoiceDate`, the fact's `OccurredAt` and the ledger entry's `EntryDate`, so all three agree exactly (`BI13`).
- **Why the credit line is locked first.** Two concurrent issues for the same order contend on the `credits` row and are fully serialised before either looks at `invoices`. The loser then finds the committed invoice at step 2 and answers `created: false`. Under RCSI, the loser's step-2 statement begins *after* the winner committed, so it reads the winner's row: the ordering is what makes the read current, and the hint is defence in depth rather than the mechanism. §16 `L8` says so rather than claiming otherwise.
- **Why the allocator is last.** The counter row is a global hot spot — every invoice in the service contends on it. Taking it last minimises how long it is held, and taking it *always* last means it can never be the first edge of a cycle. `BI8` states the order as a service-wide rule so feature 22 inherits it rather than rediscovering it.
- **The reply is built inside the delegate and returned only after `ExecuteAsync` resolves**, so a rollback can never have produced a success reply — feature 19's rule, unchanged.
- **The unique index is the belt to those braces.** It cannot be reached in normal operation; if a future change removes the credits lock it turns a silent double invoice into a loud constraint violation.

### 7.3 What `SaveChangesAsync` being called twice does, and does not, mean

Both repositories call `db.SaveChangesAsync` on the **same scoped `BillingDbContext`** inside the **same** ambient transaction, so the first call flushes whatever is tracked at that moment and the second flushes the rest. Two consequences worth stating because neither is obvious:

- **Statement order between the two aggregates is not guaranteed** and does not need to be: they write different tables and there is no foreign key between `invoice_items`/`invoices` and `credit_items`.
- **Outbox `seq` order is not at risk here**, because this transaction writes exactly **one** outbox row. `Consume` raises nothing, so there is no second fact whose order could be wrong. Feature 22 writes two facts in one transaction (`payment.received.v1` then `credit.released.v1`, `R47`) and inherits `L16`'s one-awaited-`INSERT`-at-a-time discipline; that ordering constraint is named here so feature 22 does not have to rediscover it.

### 7.4 The two-aggregate deviation, **re-derived against #8's code**

`domain-model.md` §8 rule 6 says *"one transaction mutates exactly one aggregate instance plus its outbox records"*. This transaction mutates two: an `Invoice` and a `BuyerCredit`. #7 took this deviation and its gate approved it. **That approval is not evidence about #8**, so the argument is rebuilt here from what this repository actually contains.

| Alternative | Why not, in #8 |
|---|---|
| Invoice first, `Consume` in a second transaction | A crash between them leaves an invoice issued with the hold still active. Nothing can detect it: `BuyerCredit.Consume` raises no event, there is **no `CreditConsumed` type in `Domain/Events/`**, `BillingFactPayloadMapper` has no arm for one and `FactCatalog` has no `credit.consumed.v1` key — so `OutboxWriter` would refuse to store one even if it were raised. The suppression is structural in three places, which is exactly why the gap would be unobservable. |
| `Consume` first, invoice in a second transaction | Symmetric and worse: the hold is converted for an invoice that may never exist, and every `invoice.issue` retry then hits `NO_ACTIVE_HOLD` — which in #8 is **terminal** (`BI26`), so the order's saga stops permanently on the first retry rather than eventually. |
| Make `Consume` emit a fact and drive the invoice from it | A fourteenth fact in a thirteen-fact catalogue, a Billing-side Kafka consumer `saga.md` §5 forbids and `BI1` guards against, a `Contracts` change and a trilogy-wide contract change — to remove a window one transaction removes for free. |
| Merge `Invoice` and the credit ledger into one aggregate | Collapses two genuinely independent lifecycles (an invoice is per order; a credit line is per party pair and outlives every order) and makes the credit line a write hot spot for every invoice. |
| **One transaction, two aggregates, one fixed lock order** | **Chosen.** |

**Three things make the deviation cheaper in #8 than it was in #7, and one makes it no cheaper.**

1. #7 threaded an explicit `TransactionContext` into every repository call, so composing two repositories meant passing the same `tx` to both and *hoping* nobody passed a different one. #8 has no `tx`: both repositories resolve the same scoped `BillingDbContext` from the same DI scope and enlist automatically. There is no parameter to get wrong.
2. The composition is already proven in this codebase: `DespatchCreationService` (feature 18) calls `stockRepository.SaveChangesAsync` and `despatchRepository.SaveAsync` inside one `unitOfWork.ExecuteAsync`, across two aggregate types, and its integration tests are green. This feature is the same shape with different tables.
3. `IsolationLevel.ReadCommitted` is stated explicitly in `EfCoreUnitOfWork`, not inherited, and the whole thing routes through `CreateExecutionStrategy()`.
4. **No cheaper:** the property #7's `tx` parameter *did* buy — visibility, in the signature, that two writes share a transaction — is gone. #8 replaces it with `ValidateScopes = true` (a singleton capturing the scoped context is a boot failure) plus `BI7`'s forced-rollback integration test. Ledger `L20`.

The precedent is exact: feature 17 accepted a multi-aggregate transaction because **F3** (*reservation is all-or-nothing per order*) is an invariant no single aggregate owns. Here the invariant is *an issued invoice's hold is consumed*, which likewise spans two aggregates and has no fact to repair it, and which `BI7`'s test checks with a single query. In both cases the deviation is justified by an invariant, never by convenience — the test any future deviation must pass.

**The deviation is written into the code, not only into this file:** `InvoiceIssueService`'s class remark cites `domain-model.md` §8 rule 6, states that it deviates, and names the invariant. A reader who finds the two `SaveChangesAsync` calls must find the reason next to them.

## 8. No migration — what phase 6 already built, and what that removes

#7's `design.md` §6 and its entire task group B are a migration: a new `invoice_number_sequences` table, a new unique index over live rows, a new `(status, invoice_date)` index, a duplicate-check before the index, a hand-trimmed `drizzle-kit` diff, and a confirmation that the outbox parity guard was untouched. **None of it exists here.** Phase 6 created the Billing schema whole, from `docs/Databases.md` §6, including all four invoice-related tables and all three indexes, and phase 8's `money_column_width` widened every money column to `bigint`.

Consequences, stated so nobody re-does them:

- No `dotnet ef migrations add`, no `Migrations/` file, no `BillingDbContextModelSnapshot` change, no `docker compose down -v`.
- No duplicate-`order_reference` pre-check: the unique index has been in force since the database was created and the seed writes one invoice per completed order.
- `IndexTests`, `SchemaColumnTypeTests`, `ForeignKeyTests`, `UniqueConstraintTests`, `NoMoneyColumnIsIntTests` and `ReliabilityTableParityTests` already cover the schema; this feature runs them, and changes none of them.
- This is the single largest source of the effort delta against #7, and §15 records it.

## 9. Consumers — still none (`BI1`)

`Program.cs` and `BillingHost.cs` are **unchanged**. Billing's only Kafka client remains the relay's producer.

`BillingConsumesNoFactsTests` is the structural guard, and it is deliberately **not** #7's text scan (whose own review recorded `N8`: *"named so no assessment reads it as a container-level guarantee"*). #8 can do better because the host is buildable in a unit test: the guard builds the real host with `BillingHost.CreateBuilder`, resolves `IEnumerable<IHostedService>` and asserts the concrete type set is exactly `{ OutboxRelayBackgroundService, BillingRpcResponder }`. A second, cheap half remains a source scan — no file under `src/Billing/` may reference `IConsumer<`, `ConsumerConfig` or `ConsumerBuilder` — and it is proved non-vacuous against a fixture string, because a container-level assertion cannot see a consumer that has been written but not yet registered.

## 10. The inherited findings and the two backlog entries

### 10.1 `BI17` — the `.99` fixture guard, both halves, with the computed half load-bearing

`review_billing_credit_simulator.md` `N2` asked for a durable guard and got a search plus prose. The hazard is specific to this feature: an invoicing fixture's credit-relevant amount is usually **computed**, and `3 × 8_333 = 24_999` ends in 99 while no scan of literals would ever see it. Since feature 20, `SimulatorCreditDecision` is the unconditional production binding for every Billing integration test, so such a fixture produces an inexplicable `simulated_cents_rule` rejection in a test about invoicing.

```csharp
// tests/Billing.IntegrationTests/CentsRuleFixtureGuard.cs
internal static class CentsRuleFixtureGuard
{
    public const string OptIn = "cents-rule-intentional";

    /// Throws unless minorUnits % 100 != 99 or the caller opts in explicitly.
    public static void AssertNotCentsRuleAmount(long minorUnits, string context, string? optIn = null);

    /// The backstop: scans tests/Billing.IntegrationTests/*.cs for integer literals in money
    /// positions and reports every un-opted-in `…99`.
    public static IReadOnlyList<(string File, int Line, long Value)> FindUnguardedLiterals(string testRoot);
}
```

- The **runtime half** is called by every `BillingHostFixture` builder whose amount can reach the credit-decision port — `SeedCreditLineAsync`, `SeedLedgerEntryAsync`, the `HoldRequest` builder and the new `IssueRequest` builder — on the **computed** total, so a three-line invoicing fixture totalling `24_999` throws at fixture-build time naming the file and the amount.
- The **text half** is `BI17`'s spec, catching payloads assembled by hand that never reach a builder. The opt-in is an inline `// cents-rule-intentional` comment on the offending line rather than a file allow-list, so it cannot rot when a file is renamed. The two intentional `24_999`s in `CreditSimulatorTests.cs` get the marker.
- **Scope, stated honestly.** The `.99` rule lives behind the credit-decision port, consulted **only** on `billing.credit.hold`. An invoice total ending in 99 is by itself harmless. The guard is deliberately broader than the hazard, because a guard whose scope a reader must reason about is a guard that gets bypassed; the marker is the escape hatch.
- **Non-vacuity is mandatory.** The scan must be proved to fire against a scratch fixture file written into the test's temp directory and asserted to produce exactly one hit — never asserted empty and believed. This is `G5`'s discipline from feature 19, and `CLAUDE.md`'s rule that a negative claim about the repository is a search result rather than a reading.

**Rejected alternative, recorded:** binding `AlwaysApproveCreditDecision` in the integration fixture, which would remove the hazard entirely *and* give `BI20`'s header a real consumer. Rejected for the same reason #7 rejected it: `BillingHostFixture` deliberately builds the real host with the production DI graph and no overrides, which is the property that makes an integration test evidence about what `Program.cs` boots. Trading it away to avoid writing a fixture guard would be a bad exchange.

### 10.2 `BI30` — backlog id 55, `BC32`'s six remaining sites

`BC32` claims *every* integration test that deserialises an RPC reply asserts that reply's own discriminating field before touching any collection. It is false at six sites. The fix is one assertion inserted ahead of the first collection access at each:

| File | Line (as filed) | Reply type | Discriminating field to assert first |
|---|---|---|---|
| `tests/Billing.IntegrationTests/CreditListTests.cs` | 39 | `CreditListReplyPayload` | `Assert.NotNull(reply.Page)` then `Assert.Equal(4, reply.Page.Total)` before `reply.Items` |
| `tests/Billing.IntegrationTests/CreditListTests.cs` | 73 | `CreditListReplyPayload` | same shape, `Total == 1` |
| `tests/Billing.IntegrationTests/CreditListTests.cs` | 77 | `CreditListReplyPayload` | same shape, `Total == 4` |
| `tests/Fulfillment.IntegrationTests/StockListTests.cs` | 33 | `StockListReplyPayload` | its own `Page`/`Total` before `Items` |
| `tests/Fulfillment.IntegrationTests/StockListTests.cs` | 38 | `StockListReplyPayload` | same |
| `tests/Fulfillment.IntegrationTests/StockListTests.cs` | 44 | `StockListReplyPayload` | same |

The Fulfillment three are included because the entry's own acceptance names them and one pass reaches them; leaving them would half-close the entry, which is what feature 20 correctly declined to do. **The evidence is an enumerating command and its complete output**, recorded in `progress/impl_billing_invoicing.md`, with one classification line per hit — not a sentence saying the sweep is clear. Advisory `A7` on the same backlog entry (`design.md` §10.5's `total`/paging claim was unachievable for `StockReplenishReplyPayload`, which has no other field) is **not** reopened here; it is a wording matter for whoever revisits `BC32`'s text.

### 10.3 `BI20` — the stale header

`AlwaysApproveCreditDecision.cs` still opens *"The credit-decision port bound today (design.md §6.3)"*. Feature 20 replaced its registration with `SimulatorCreditDecision`, and `BillingServiceCollectionExtensions` says so correctly. The header is reworded to claim only what is true — the port's reference implementation, and the provider a future fixture may bind — and §10.1 records that this feature considered being that fixture and declined. Comment only; the class, its registration and `AlwaysApproveCreditDecisionTests` are untouched.

### 10.4 `BI21` — the guard Orders is missing

`SagaCommandRequestFactory.BuildInvoiceIssue` already passes `order.InitialDiscount.MinorUnits`, so the production behaviour #7 had to add is already here. What is missing is the guard: `SagaCommandPayloadTests`' invoice case is built from hand-written literals with `Discount: 0` and asserts only the key set, so deleting that argument leaves the whole suite green. One case is added, driven from **one real `Order`** with a non-zero discount, asserting

```
Σ(line.unitPrice × line.units) − invoiceRequest.discount  ==  creditHoldRequest.amount.amount  ==  order.TotalAmount.MinorUnits
```

with both payloads built from that same `Order`, which is what makes the comparison mean anything. Two files under `tests/Orders.UnitTests/` and nothing else in `src/Orders/`.

## 11. First boot against the live compose stack

**Pre-state, as of this spec pass.** `progress/impl_billing_credit.md` § Live boot records **five** `invoice.issue` rows `parked` at `attempts = 9` — `ORD-000007` … `ORD-000011` — each because no responder is subscribed to `billing.invoice.issue`, and all five orders sitting at `status = despatched`. Their holds are `CR-000001`: `ORD-000007` 49 998, `ORD-000010` 49 998, `ORD-000011` 1 000; `CR-000092`: `ORD-000008` 5 547, `ORD-000009` 5 547. **The implementer re-reads the live tables before booting** rather than trusting this paragraph; the set may have moved. `EfCoreSagaCommandStore`'s sweeper claims `parked` rows whose `next_attempt_at` is due, so no operator action is needed to unpark them.

**What happens on the first boot with the responder registered.** For each parked row the sweeper re-dispatches, Billing now answers, and in one transaction per order: the credit line is locked, no invoice is found, the currency matches, the order's `ActiveHold` is its `hold` amount, `INV-00000n` is allocated (continuing past the seed's `INV-000005`), the invoice and its lines are inserted, one `consume` entry is appended, one `invoice.issued.v1` outbox row is written. The relay publishes it to `otc.billing.facts.v1` keyed by the order id; the orchestrator consumes it, finds the order in `despatched`, and moves it to **`invoiced`**.

**And then it stops.** `saga.md` §3.1 step 5 is explicit: the saga waits for the outside world. Expected steady state:

| Where | Expected |
|---|---|
| `otc_orders.orders` | the five orders at `status = 'invoiced'` |
| `otc_orders.saga_commands` | every `invoice.issue` row `sent`, **zero** parked, and **no new row of any kind** |
| `otc_billing.invoices` | the five seeded rows plus one per unparked order, each `status = 'issued'`, `paid_at` NULL |
| `otc_billing.invoice_number_sequences` | `next_value` advanced by exactly the number of new invoices |
| `otc_billing.credit_items` | exactly one new `consume` row per order, amount equal to that order's `hold` |
| `availableCredit` per line | **numerically unchanged** — `CR-000001` 399 004, `CR-000092` 488 906, identical to `impl_billing_credit.md`'s figures. `R40`'s neutrality, visible in production data for the first time |
| `otc_billing.outbox` | one `invoice.issued.v1` per order, all with `published_at` stamped |

**What must NOT happen**, asserted rather than assumed: no `payment.received.v1`, no `credit.released.v1`, no order at `paid` or `completed`, no parked `payment.register` row, and no new `saga_commands` row. A saga that stalls at `invoiced` is the **designed** end state of this phase, not a defect, and `progress/impl_billing_invoicing.md` says so explicitly next to the `saga.md` citation so the human's manual verification does not read it as a stall.

**A fresh end-to-end control order** is placed (a non-`.99` total well within credit) and must traverse `placed → stock_reserved → credit_approved → confirmed → despatched → invoiced` unattended.

## 12. Testing approach

| File | Level | Proves |
|---|---|---|
| `tests/Billing.UnitTests/InvoiceTests.cs` | domain unit | `R45` and `R46` (matrix names verbatim), `BI10`, `BI11`, `BI14`, `BI25` |
| `tests/Billing.UnitTests/InvoiceStateTests.cs` | unit (structural) | `BI23`, `BI27` |
| `tests/Billing.UnitTests/InvoiceFactTests.cs` | domain unit | `BI13`, **provenance-asserting** |
| `tests/Billing.UnitTests/InvoiceIssueServiceTests.cs` | unit | `BI5` unit half; the fast path opens no transaction; `BI8`'s **three**-lock order; reply only after commit; rollback ⇒ no reply |
| `tests/Billing.UnitTests/InvoiceResponderValidationTests.cs` | unit | `BI2`'s entry observation; `BI25`'s validator half |
| `tests/Billing.UnitTests/InvoiceSubjectsTests.cs` | unit | `BI16` half |
| `tests/Billing.UnitTests/InvoiceRpcPayloadTests.cs` | unit | `BI28` |
| `tests/Billing.UnitTests/InvoiceNumberAllocatorTests.cs` | unit | `BI12` formatting half |
| `tests/Billing.UnitTests/BillingConsumesNoFactsTests.cs` | unit (structural) | `BI1` |
| `tests/Billing.UnitTests/BillingResponderSubjectCoverageTests.cs` | unit | `BI31` |
| `tests/Billing.UnitTests/CentsRuleFixtureGuardTests.cs` | unit | `BI17` |
| `tests/Billing.UnitTests/BillingErrorMapperTests.cs` | unit | every added error class → its code and `details`; `BI25` mapping; `BI26` |
| `tests/Billing.IntegrationTests/InvoiceIssueTests.cs` | integration (Testcontainers MsSql + NATS + Kafka) | `R45` integration half, `BI2` – `BI6`, `BI9` |
| `tests/Billing.IntegrationTests/InvoiceIssueRaceTests.cs` | integration | `BI8` |
| `tests/Billing.IntegrationTests/InvoiceListTests.cs` | integration | `BI15` |
| `tests/Billing.IntegrationTests/InvoiceWireTests.cs` | integration | `BI16` |
| `tests/Billing.IntegrationTests/InvoiceRepositoryTests.cs` | integration | `BI7`, `BI10` store half, `BI24` |
| `tests/Billing.IntegrationTests/InvoiceReadRepositoryTests.cs` | integration | `BI15` SQL half |
| `tests/Billing.IntegrationTests/InvoiceNumberAllocatorTests.cs` | integration | `BI12` concurrency half, `BI29` |
| `tests/Orders.UnitTests/SagaCommandPayloadTests.cs` | unit | `BI21` |

**The synchronisation rule is binding.** Wait only on **terminal or monotonic** evidence: an RPC reply, an outbox row's `published_at` (set once, never cleared), the row count of the append-only `credit_items` table, the presence of an `invoices` row (inserted once, never deleted on this path), a hand-driven `OutboxRelay.RunOnceAsync` result, or a Kafka consumer's received-message list. **Never** poll a status a correct system passes through, and never poll `availableCredit`.

**For `BI8`:** fire two raw-NATS `invoice.issue` requests with `Task.WhenAll`, then assert on the **replies** (exactly one `Created: true`, exactly one `Created: false`, both naming the same `InvoiceReference`), on the **final** counts of `invoices` rows and `consume` entries for the order (both exactly 1), and on the outbox holding exactly one `invoice.issued.v1`. Repeat on ten fresh orders so a scheduling fluke is visible rather than lucky. **What this test cannot prove is lock ordering** — two instances of the same transaction taking locks in a consistently inverted order still cannot cycle; #7's reviewer established that empirically (`N5`). The ordered call log in `InvoiceIssueServiceTests` is the sole guard for `BI8`'s ordering clause, it covers all three locks, and it is armed.

**The fact-emission rule is binding, and this feature has three branches under it.**

1. `Invoice.Issue`'s `invoice.issued.v1` — live caller, reachable from integration.
2. `Invoice.MarkPaid`'s `payment.received.v1` — **no caller**, reachable only from `InvoiceTests`. Double force.
3. `BuyerCredit.Consume`'s deliberate **suppression** (`R40`) — the inverted case. Deleting a fact that is not there is impossible, so the guard is a whole-table outbox **delta** of exactly 1 across an issue, armed by *adding* a spurious `credit.consumed`-shaped outbox row inside `EfCoreBuyerCreditRepository.SaveChangesAsync`. #7's review found this guard genuine at the repository level and initially **missing** at the responder level, because the responder assertion was `correlationId`-scoped and a consume-path regression would write its row under the credit line's own id (`N7`). #8 carries the whole-table delta at **both** layers from the start.

**And deletion is one mutation family, not the whole of arming.** Each of the three branches gets the second question asked of it too: does the guard fail when a **field** is wrong? `invoice.issued.v1`'s `lines[0].unitPrice` and `retailerCode`, and `payment.received.v1`'s `paymentReference`, are corrupted on the wire and the failing test recorded. Feature 17 shipped a payload defect on a fully green suite for exactly the want of this.

**Coverage:** ≥ 80 % domain, ≥ 60 % overall, via `./quality.sh`.

## 13. Configuration and packages

**No new NuGet package. No new environment variable. No new configuration key.** Everything this feature needs — `Microsoft.EntityFrameworkCore.SqlServer`, `NATS.Client.Core`, `Confluent.Kafka`, `Testcontainers.MsSql`/`.Kafka`, xUnit — is already referenced by `src/Billing/Billing.csproj` and the two Billing test projects from feature 19. `BILLING_PORT`, `NATS_URL`, `KAFKA_BROKERS`, `CREDIT_FAILURE_RATE` and the `OUTBOX_*` settings are unchanged, and `.env.example` is untouched. This is stated explicitly so that a phase commit message listing no packages reads as a fact rather than an omission.

## 14. Rejected alternatives, recorded

| Alternative | Why not |
|---|---|
| A Kafka consumer in Billing for `order.despatched.v1` | Duplicates the trigger, bypasses the saga's dedup record and order-status precondition, puts a second unsequenced writer on the invoice, and contradicts `saga.md` §5. `BI1` guards it structurally |
| A second `BackgroundService` for the two invoice subjects | Forks the concurrency bound, the per-request scope, the drain and the fault isolation — four hand-built, separately-armed behaviours — into an unguarded copy. §4.1, `BI31` |
| `readonly record struct InvoiceState` | `default(T)` is always constructible and satisfies neither case, losing the exact property #7's union supplied. §3.2, ledger `L4` |
| `enum InvoiceStatus` + `DateTimeOffset?` | Two fields; makes an acceptance criterion depend on discipline plus a test |
| Deriving the invoice lines from Billing's own data | Billing has none: no order, no despatch, no catalogue. The lines arrive in the request (`asyncapi.yaml`: *"The invoice mirrors them exactly"*, **F7**) |
| Checking the order's status before invoicing | Would require Billing to read the Orders write model, which `domain-model.md` §1 boundary rule 1 forbids. The active hold is the proxy, and `BI5` says so out loud rather than pretending the check exists |
| Emitting a rejection fact when there is no active hold | No such fact exists in the thirteen-fact catalogue, and inventing one is a trilogy-wide contract change to describe a disagreement a human must look at |
| Keying idempotency on `requestId` instead of `orderReference` | The sweeper mints a fresh `x-request-id` per attempt, so it would deduplicate nothing, and two different commands could mint two invoices for one order — exactly what **B7** forbids |
| Letting `Money`'s `checked` arithmetic raise `OverflowException` uncaught | `INTERNAL_ERROR` is **transient** to the saga dispatcher, so a permanently-overflowing payload would be retried on every sweep forever. §4.5, `BI25` |
| `db.Invoices.Where(...)` for the in-transaction re-read | LINQ composition drops the table hint and returns an unlocked versioned read that looks identical in code review. §6.2, ledger `L7` |
| Adding a lock hint to the read-side list query | Under RCSI it blocks nobody today; a hint would make a read-only view take locks. §6.5, ledger `L12` |
| A parity guard over the three `*NumberAllocator` files | They are not one implementation duplicated — each names its own table, column and prefix. §6.4 |
| Emitting `paidAt: null` for an `issued` invoice, to match #7's bytes | Contradicts the ratified nulls-omitted non-negotiable set once in `JsonWire.Options`. §4.3, ledger `L19`, hand-over §18 |
| A `Payment` child collection on `Invoice` now | Feature 22's. `MarkPaid` takes the payment's fields as arguments and raises the fact; persisting a `payments` row, deduplicating by `paymentReference` and releasing the credit are 22's additions to the same aggregate and the same repository class |

## 15. Delta against #7, section by section

Recorded because `progress/history.md` compares effort per feature and the interesting number is *why*, not *how long*.

| #7 section | #8 | Delta |
|---|---|---|
| §6 the migration + task group B (7 tasks) | **absent** | Phase 6 created the whole Billing schema, including all three invoice indexes. Largest single saving |
| §4.2 `class-validator` DTOs | `InvoiceRequestValidator`, hand-rolled | Roughly neutral: no decorator library, but the cross-field check and its `checked` region are new work |
| §4.1 a second Nest `@Controller` | two arms on the existing responder, plus a four-type rename | New work #7 did not have (§4.1) |
| §3.1 the TypeScript union | a closed abstract record hierarchy plus `BI23`'s structural guard | New work: the property has to be re-supplied and re-guarded |
| §7.3 the allocator | a verbatim copy of the already-fixed `DES-` allocator | Cheaper than #7's, because feature 45 already paid for the idiom |
| §5.2 ports with a `tx` parameter | ports with no `tx`; the ambient scoped `DbContext` | Cheaper, and §7.4 point 4 prices what it costs |
| §11.2 the `.99` fixture guard | the same design, in C# | Equal |
| §12 the `apps/orders` discount field, gated | already shipped; only the guard is missing | Much cheaper — and the gate round #7 spent is not spent here |
| its blocking review findings `N2`, `N3`, `N4`, `N7`, `N10` | written into `BI2`, `BI5`, `BI8` and §12 as prevention | Saves a rejection round if honoured |
| — | `BI24`, `BI25`, `BI26`, `BI27`, `BI29` | New work: five properties #7 got free from its engine or language |

## 16. Ported-idiom ledger

> Binding since the Phase 8 gate (`CLAUDE.md`, *"The ported-idiom ledger"*). One line per idiom: **#7 relied on X; in #8 that property is supplied by Y.** Where the property came free from #7's engine, language or library and must be hand-built here, `tasks.md` names a guard test — the **Guard** column is the contract between the two documents, and a Guard column entry is **itself a countable claim**, so every guard named here is armed by a `⚑ ARM` task and none is decorative. Feature 19's `L11` is the standing warning: a row whose guard cannot fail is worse than no row.
>
> **Derived from an enumerated boundary list, not from recollection.** §16.1 enumerates every place a value crosses into or out of the Billing process **in this feature**, listed before any of them was analysed. §16.2 has one row per boundary, including the boundaries whose answer is *"nothing special is required here, and this is why"* — a boundary considered and dismissed is a row; a boundary never listed is the failure mode.
>
> **The preamble pattern this feature makes explicit.** Three of the rows below (`L4`, `L20`, `L23`) exist because **#7 got a property from TypeScript being structurally typed** — a union that could not be constructed wrong, a `tx` object accepted by shape, a mapper resolved by shape. That is now the second and third and fourth instance in this build; the first was feature 19's `L27`, the outbox writer. C# is **nominally** typed, so in every one of these cases the property has to be *constructed* — by a closed hierarchy, by a container lifetime rule, by an explicit port — and constructed properties need guards where structural ones needed none. **When porting a #7 mechanism, ask first whether the thing that made it correct was the type system**, because that answer is invisible in the code being copied.
>
> **30 boundaries, 32 rows.** Two boundaries carry a second property that gets its own row rather than a clause appended: `B4` at `L6` and `L7`, and `B25` at `L20` and `L23`.

### 16.1 The boundary enumeration

**Inbound decode** — B1 `billing.invoice.issue` request bytes → record; B2 `billing.invoice.list` request bytes → record (paging and filter defaults); B3 request headers → `RpcMeta`; B4 request `lines[]` → `Quantity` + `Money`; B5 request `discount` (optional `long?`) → `Money`; B6 request `currency` → `Money.Currency` and the comparison against the credit line's.

**Store reads that decide something** — B7 the un-hinted fast-path `invoices` read → the `created:false` decision; B8 the in-transaction `invoices` re-read → the `B7` authority; B9 `credits` + `credit_items` → `ActiveHold(order)`; B10 `invoice_number_sequences` → the allocated reference; B11 `MAX(CAST(SUBSTRING(invoice_reference…)))` → the seed value; B12 the `invoices` page read for the view; B13 `invoice_items` read for the reply and for reconstitution; B14 `invoice_date` / `paid_at` columns → domain instants; B15 the `status` column → `InvoiceState`; B16 `currency_code` column → `Money.Currency`; B17 `order_reference` matching in SQL versus comparison in memory; B18 `issuedBeforeMinutes` → a SQL date predicate against the handler's `now`.

**Store writes** — B19 `invoices` INSERT; B20 `invoice_items` INSERT; B21 `credit_items` INSERT (the `consume` entry, first live call); B22 `outbox` INSERT; B23 the `invoice_number_sequences` seed and increment; B24 the `credits` row, which is still never written.

**Outbound encode** — B25 the issue success reply; B26 the list success reply (`paidAt` present or absent); B27 the error reply and its terminal/transient classification in Orders; B28 the Kafka publish of `invoice.issued.v1` and its key.

**Process and host** — B29 request concurrency, DI scope and shutdown for two more subjects; B30 the transaction, its isolation, and the two-aggregate composition; and, inside B4/B5, the `Σ` arithmetic width.

### 16.2 The ledger

| # | Boundary | Property | #7 got it from | #8 supplies it by | Guard in `tasks.md` |
|---|---|---|---|---|---|
| **L1** | B1–B2 | A bare-JSON request is answered with a bare-JSON reply | A hand-written `BareJsonNatsDeserializer`/`Serializer` pair written to defeat the Nest packet | **Nothing new.** `SubscribeAsync<byte[]>` + `ReplyAsync(byte[])` through the one shared `JsonWire.Options`; the two new subjects join four existing ones on the same path. A saving, inherited | `BI16` (`F6`), asserted anyway because the wire is a trilogy contract |
| **L2** | B1–B2 | The declared request keys are the keys the record reads | Generated `@otc/contracts` types the DTO `implements` — a compile error on drift | Hand-transcribed records **plus** `BI28`'s parsed-from-`asyncapi.yaml` key check, resolving `allOf` through the existing `AsyncApiSchema` helper. C# has no generated contract type for RPC payloads | **`BI28`** (`E6`), armed against a **scratch** copy of the spec, never the real read-only one |
| **L3** | B1–B2 | A malformed request is refused, not half-processed | `class-validator` decorators on DTO classes | A hand-rolled `InvoiceRequestValidator`, one unit case per rule, raising before dispatch | `InvoiceResponderValidationTests` (`E3`, `F1`) |
| **L4** | B4, B15, B19 | `paidAt` exists **iff** the status is `paid`, and no third state exists | A TypeScript **structural** discriminated union: `{status:'paid'; paidAt}` cannot be written without its instant, and a union has no zero value | A **closed abstract record hierarchy** with a `private protected` base constructor and two nested sealed cases; one field on the aggregate; `Status`/`PaidAt` are projections. The obvious `readonly record struct` rendering was **rejected because `default(T)` is always constructible** and satisfies neither case — the property #7 had for free would have been silently dropped by a rendering that reads as equivalent. §3.2 | **`BI23`** (`B7`) — reflection over the hierarchy and the base constructor's accessibility; armed by widening `private protected` to `protected` and by adding a third nested case |
| **L5** | B15, B19 | A status token round-trips to the same case, and a filter selects the same rows the domain would | JS `===` against a TypeScript union no unknown value could inhabit; MySQL's own CI collation never met an ordinal comparison | `InvoiceStatuses.Parse` **throws** `UnknownInvoiceStatusError` outside the closed set; the writer emits only the lower-case contract tokens; the validator restricts the `status` filter to the same two. MS-SQL's `SQL_Latin1_General_CP1_CI_AS` would match `'Paid'` where `StringComparison.Ordinal` would not — closed at the edge, not assumed away. `L13`/`L14`'s sibling | **`BI27`** (`B7`) — an unrecognised token raises rather than being coerced; armed by returning a default instead of throwing |
| **L6** | B4 | A unit count cannot silently narrow or go non-positive | `Quantity` refused zero and negatives; JS had no narrowing | `Quantity(int)` already refuses ≤ 0; the wire `units` is `int32` and the column is `int`; **no cast anywhere**. Nothing extra is required, and this row exists so nobody adds a `(int)` on the way in | `BI11` (`B8`); `tasks.md` forbids any narrowing cast on this path and the reviewer greps for it |
| **L7** | B4, B5 | The line totals and `amount − discount` cannot wrap | JavaScript numbers do not wrap (they lose precision above 2⁵³, which no amount here approaches), so there was nothing to supply | **Two places.** (1) `Invoice.Issue` accumulates inside an explicit `checked` region and converts `OverflowException` into `InvoiceTotalOverflowError` — a `DomainError` with a stable `Code`, because a framework exception falls to `INTERNAL_ERROR`, which the saga dispatcher retries **forever**. (2) `InvoiceRequestValidator`'s cross-field sum is also `checked`, because C# arithmetic is unchecked by default (`CheckForOverflowUnderflow` is not set) and a wrapped sum would **accept** a payload the domain then refuses inside the transaction. `CreditExposure.Summarise` is the shipped precedent | **`BI25`** (`B8`, `E3`, `E9`) — three cases: the domain raise, the validator refusal, the mapping to the terminal `DOMAIN_ERROR`; each armed by removing its `checked` or its conversion |
| **L8** | B8 | The `B7` authority read sees the competitor's committed row | InnoDB `SELECT … FOR UPDATE` under `REPEATABLE READ`, which is a current read and takes a gap lock on the non-matching unique-index range | The `credits` row lock, taken **first**, is the mechanism: under RCSI the loser's step-2 statement begins only after the winner committed, so it reads the winner's row. `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)` is **defence in depth and is not claimed as load-bearing** — with the credits lock in place, removing it would not fail a test, and a row whose guard cannot fail is worse than no row (feature 19 `L11`). What *is* load-bearing and *is* guarded: the ordering, and the unique index | `BI8`'s ordered three-lock unit log (`D3`) and `BI8`'s race (`F5`); the index by `IndexTests`, already green. The hint's presence is deliberately **not** given a guard, and this row says so |
| **L9** | B9 | The active-hold predicate reads current, committed rows | `FOR UPDATE` on the order's entries | Unchanged: `EfCoreBuyerCreditRepository.LockForOrderAsync`'s existing `WITH (UPDLOCK, HOLDLOCK)` statements, reused with no signature change. Feature 19 already armed this by removing the hints | Inherited `BC9`/`BC7`, already green; `BI7` re-exercises it through this feature's path |
| **L10** | B9, B21 | A `consume` moves neither term of `availableCredit` | The formula in `credit-exposure.ts`, plus the absence of any consume fact builder | The same formula in `CreditExposure.Summarise` (`exposure = hold − release`, so `consume` contributes 0), plus **three** structural absences: no `CreditConsumed` type, no mapper arm, no `FactCatalog` key. Unchanged from feature 19 — this feature is the first **caller** | **`BI7`** whole-table outbox delta at the repository (`C5`) **and** `R45`'s at the responder (`F3`); armed by *adding* a spurious row (`F10`) — the inverted case |
| **L11** | B7 | A read-only fast path blocks nobody | MySQL's non-locking consistent read | `AsNoTracking()`, no hint, no transaction — under RCSI a versioned read. Nothing special is required, and this row exists so nobody "helpfully" adds a hint to a path the sweeper hits on every retry | The absence is enforced by `tasks.md` `C2`'s explicit instruction and by the reviewer grepping this file for `UPDLOCK` outside `LockByOrderReferenceAsync` |
| **L12** | B12–B13 | The list query blocks nobody and mutates nothing | MySQL's non-locking consistent read | `AsNoTracking()`, no hint, no transaction. `BI15`'s integration case re-reads a row afterwards to prove nothing changed — the `StockListTests` precedent | `BI15` (`C7`); the absence enforced by `tasks.md` `C4` |
| **L13** | B14 | An instant read back equals the instant written | A `mysql2` pool created with `timezone: 'Z'` — one clause | `value.UtcDateTime` to write; `new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc))` to read. `datetime2(3)` carries no offset and EF Core returns `DateTimeKind.Unspecified`; `new DateTimeOffset(unspecified)` applies the **machine's local** offset. `paid_at` is the first **nullable** instant column any #8 aggregate reads, and the null-conditional rendering is where the `SpecifyKind` is easiest to drop | **`BI24`** (`C6`) — a round trip under a non-UTC `TZ`, covering both `NULL` and non-null `paid_at`; armed by dropping `SpecifyKind` |
| **L14** | B16 | Two currency codes compare equal iff they are the same currency | MySQL's CI collation on the column; JS `===` on the string | `Money.EnsureSameCurrency` is `StringComparison.Ordinal` while `char(3)` matches case-insensitively. The disagreement is unreachable because the seed writes uppercase, `Money`'s constructor refuses a malformed code and §4.4 refuses anything but `^[A-Z]{3}$`. Closed at the edge, exactly as feature 19 closed it | `InvoiceRequestValidator` currency case (`E3`) + `BI4` integration (`F2`) |
| **L15** | B17 | The in-memory order-reference comparison agrees with the database's matching | JS `===` (ordinal) against MySQL's CI collation — the same latent disagreement, never stated | `OrderNumber.Parse` + §4.4's uppercase-only `^ORD-[0-9]{6,}$` alphabet, and `CreditExposure.Summarise`'s `StringComparer.OrdinalIgnoreCase` grouping (feature 19's `L14`), reused unchanged. The invoice side compares `order_reference` in SQL only | `BI3`/`BI9` integration (`F2`, `F4`); the shared half by feature 19's `BC28`, already green |
| **L16** | B19–B22 | Facts are stored in the order they were raised | MySQL `AUTO_INCREMENT` assigned in insert order through the ORM's batch | The per-row awaited `INSERT` copied verbatim from `EfCoreBuyerCreditRepository.InsertOutboxRowAsync` — EF Core's SQL Server provider does **not** preserve `Add` order when assigning `IDENTITY` values, measured in feature 14. **This transaction writes one outbox row, so the property is not exercised here**; the discipline is kept because feature 22 writes two in one transaction and will exercise it | Inherited `OutboxSeqIdentityTests` (already green); `tasks.md` `C2` forbids `AddRange` and the reviewer greps for it |
| **L17** | B19–B20 | "Insert-or-leave-alone" is never rendered as check-then-act | `INSERT … ON DUPLICATE KEY UPDATE`, where the statement *is* the unit of atomicity | **Not needed on this path, and deliberately not rendered.** Every invoice and line row is brand new inside a transaction already holding the credit line's exclusive lock. Stated because feature 45's defect was exactly a check-then-act rendering of this idiom and the next reader will look for it here | The absence is the guard: `tasks.md` `C2` forbids `IF NOT EXISTS … INSERT`, `MERGE` and `onDuplicateKey`-style upserts in this file, and the reviewer greps |
| **L18** | B23 | The counter row is seeded exactly once under concurrency | `INSERT … ON DUPLICATE KEY UPDATE`, unconditional — one statement | `INSERT … SELECT … WHERE NOT EXISTS (… WITH (UPDLOCK, HOLDLOCK) …)`, copied verbatim from the **fixed** `EfCoreDespatchNumberAllocator`. #8's first rendering of this idiom for `ORD-` was check-then-act and was a real, fixed defect (feature 45); this is the third instance and it is copied, not re-derived | **`BI29`** (`C8`) — N concurrent allocations against a **fresh** database with no counter row; armed by reverting to `IF NOT EXISTS (SELECT …) INSERT` |
| **L19** | B25–B26 | An absent optional field is omitted, not sent as `null` | `class-transformer` + Nest's serialiser — and #7 chose to send `paidAt: null` | `JsonWire.Options` (`DefaultIgnoreCondition = WhenWritingNull`) plus **nullable** reply properties. `InvoiceIssueReplyPayload.InvoiceId` and `InvoiceViewPayload.PaidAt` are the two. **This is a recorded divergence from #7's bytes for `paidAt`**: `asyncapi.yaml` leaves it out of `required` and declares `oneOf [Instant, null]`, so both validate, and #8's nulls-omitted rule is ratified and set once so no service can drift. Consumers must read absent as "not paid" — §18 | **`BI28`** second half (`E6`) — the exact key set for an `issued` and a `paid` view; armed by making `PaidAt` non-nullable |
| **L20** | B25, B30 | Two aggregates' writes are in **one** transaction, and it is visible that they are | An explicit `TransactionContext` threaded into every repository call — TypeScript accepted it structurally, and its presence in the signature was the documentation | The **ambient** transaction of the one scoped `BillingDbContext`; there is no `tx` to thread and no signature that shows the sharing. The visibility is replaced by (a) `ValidateScopes = true`, forced on in every environment, which turns a singleton capturing the scoped context into a **boot failure**, and (b) a class remark on `InvoiceIssueService` that cites §8 rule 6 and names the invariant. This is the third structural-typing row | **`BI7`** (`C5`) — a forced rollback after **both** saves leaves no invoice, no line, no `consume` entry and no outbox row; armed by moving one save outside `ExecuteAsync`. Plus `E7`'s registration-lifetime assertion |
| **L21** | B27 | Only codes the contract declares reach the wire, and a transient failure stays retryable | #7's orchestrator retried **every** `RpcError` code, so any code was safe | A closed mapping (§4.5) in which `CONFLICT` stays banned and every transient store failure yields `UNAVAILABLE`. Feature 42 made nine codes **terminal** in #8, which changes two of this feature's outcomes: `PRECONDITION_FAILED` now stops the command row immediately (`BI26` — the property #7 got by retrying to exhaustion and parking, delivered more directly), and an uncaught `OverflowException` would fall to the **transient** `INTERNAL_ERROR` and retry forever (`L7`) | **`BI26`** (`D4`) — reads the terminal set from `NatsSagaCommandsAdapter`'s own classification; armed by moving `PRECONDITION_FAILED` out of it |
| **L22** | B28 | Every fact lands on its order's partition | The relay's `correlationId` key, unchanged | The same relay, unchanged — `PublishableFact.Key` is `correlationId.ToString()`. What keeps the key non-arbitrary is `BI2`'s refusal of a header-less issue request | `BI13` (`B9`) for the stamping; the relay half by feature 19's `BC16`, already green |
| **L23** | B28 | The outbox writer maps this service's facts without knowing their types | TypeScript's **structural** typing: one `outbox-recorder.ts` accepted anything with the envelope's shape and resolved a mapper by shape | The per-service `IFactPayloadMapper` port feature 19 built (`L27`), extended with two arms. **The extension is where the nominal type system bites**: adding a `FactEvent` subtype with no mapper arm compiles fine and throws at run time, in a transaction, on a path with a live caller. The fourth structural-typing row | `R45`'s integration half (`F3`) reads the published payload back and asserts its fields, so a missing arm fails there; **`F9`** additionally corrupts `lines[0].unitPrice` and `retailerCode` to prove the assertion reads the payload rather than counting rows |
| **L24** | B29 | Independent requests are handled concurrently, each in its own injection scope | `@nestjs/microservices`' NATS server gave every request its own promise chain and request scope; a second `@Controller` inherited all of it | The existing `BillingRpcResponder`'s bounded `SemaphoreSlim`, one `IServiceScope` per request and tracked in-flight tasks, **extended to five subjects rather than forked**. A second responder class would duplicate four separately-armed behaviours into an unguarded copy — the property #7 got from its framework has to be *not re-lost* here, which is a different problem from supplying it. §4.1 | **`BI31`** (`E4`) — the built host registers exactly one RPC responder and it subscribes all five subjects; armed by adding a second responder registration. Inherited `BC21`/`BC22` re-run unchanged |
| **L25** | B29 | Shutdown drains in-flight work without one failure aborting it | Nest's `enableShutdownHooks()` + `app.close()` | The existing `StopAsync`, which awaits every in-flight task **individually** — unchanged, and now covering two more subjects because they share the class. Nothing new is required, and that is the whole argument for §4.1 | Inherited `BC22`, both halves, re-run after the rename (`E4`) |
| **L26** | B30 | One transaction per unit of work, at a stated isolation level | Drizzle's explicit transaction callback | `EfCoreUnitOfWork`, unchanged: `IsolationLevel.ReadCommitted` stated explicitly, routed through `CreateExecutionStrategy()`. Under RCSI "read committed" means *statement-scoped snapshot*, which is why `L8`/`L9` reason about statement boundaries rather than transaction boundaries | `BI7` (`C5`) |
| **L27** | B10–B11 | The first live allocation continues past the seed's highest reference | A `MAX(CAST(SUBSTRING(...)))` self-initialisation | The same, in T-SQL, inside the atomic seed statement. The seed writes `INV-000001` … `INV-000005`, so the first live allocation must be `INV-000006` | **`BI12`** (`C8`) — seed a row, assert the next allocation; armed by removing the `MAX(...)` sub-select so the seed starts at 1 |
| **L28** | B18 | `issuedBeforeMinutes` filters against a clock a test can control | The handler read the clock and passed `now` into the query | The same: `IInvoiceReadPort.ListAsync(query, now, ct)` and `now.UtcDateTime` at the parameter, so a `datetime2(3)` column is never compared against a `datetimeoffset`. Inherited design, restated because the type conversion is #8's own hazard | `BI15` SQL half (`C7`) — a fixed `now`; armed by making the adapter read `DateTimeOffset.UtcNow` |
| **L29** | B24 | The `credits` row itself is never written | `save` inserted `appendedEntries` only | Unchanged: `EfCoreBuyerCreditRepository.SaveChangesAsync` adds `credit_items` rows and nothing else. Stated because this feature is the first to call `Consume`, and a reader may expect a balance column to move | `BI7`'s rollback assertions include the `credits` row's `updated_at` (`C5`) |
| **L30** | B3 | A missing or malformed correlation header mutates nothing | The controller read `ctx.getHeaders()` and refused before dispatch | `RpcMetaExtractor`, reused unchanged, called **before** deserialisation on the issue arm; refusal before dispatch. `billing.invoice.list` requires neither header, exactly as `billing.credit.list` does not | **`BI2`** (`F1`) — a `[Theory]` asserting the dispatcher was never called; armed by moving the extraction after the dispatch |
| **L31** | test fixtures | A test amount never accidentally trips a production business rule | #7 had the same hazard and built the same two-half guard; #8 inherited only the search and the prose | `CentsRuleFixtureGuard`'s runtime half on **computed** totals plus the text backstop, with an inline opt-in marker. The hazard grew in #8 because feature 20 made the simulator the unconditional binding for every Billing integration test | **`BI17`** (`A4`) — the computed half must be shown to bite on `3 × 8_333`, and the scan must be shown to fire against a scratch fixture; both armed |
| **L32** | test assertions | A wrong-shaped RPC reply fails on a named assertion, not on a dereference or not at all | #7 destructured a JavaScript object and read `undefined`, which its assertions caught | C# deserialises an `RpcError` body into an **all-defaults reply record without throwing**, so the same test shape silently passes. `BC32` supplies the property by asserting the reply's own discriminating field first; six sites still do not, and this feature fixes them | **`BI30`** (`A2`) — an enumerating command and its complete output, one classification line per hit; armed by making a responder answer an `RpcError` on one of the fixed sites and confirming it fails on the named assertion |

## 17. Out of scope — restated

- **`billing.payment.register`, the `Payment` entity, `payments` writes, dedup by `paymentReference`, `Release(invoice_paid)`, `credit.released.v1` on payment, `R47` – `R49`**: feature 22. `MarkPaid` is delivered, unit-tested and uncalled; `BI8`'s lock order and §7.3's same-transaction fact ordering are binding on it.
- **The Gateway's callers of `billing.invoice.list` and the "Register payment" button**: features 25 and 29.
- **DLQ, retries, metrics, tracing, `traceparent` / `x-deadline-ms`, health**: feature 27.
- **The projector's `order_timeline` entries for `invoice.issued.v1`**: feature 24.
- **Credit notes, dunning, partial payment, partial invoicing, invoice cancellation**: out of the model (`domain-model.md` §9).
- **Backlog id 47** (the allocator's `MAX(CAST(SUBSTRING(...)))` scan cost) — phase 21, and it belongs to the allocator family as a whole.
- **Backlog id 52** (the retroactive boundary ledger for pre-ledger services) — its own acceptance defines it as standalone work that fixes nothing in place.
- **Backlog id 54** (a Fulfillment ledger row for an un-hinted in-transaction re-read) — Fulfillment's, not Billing's.
- **Backlog id 56** (unguarded env reads in every `Program.cs`) — phase 13, standalone, because all 34 reads across three composition roots are equally unguarded and three more arrive in phases 11–13.
- **A parity guard over the three `*NumberAllocator` files** — §6.4.
- **`BC32`'s advisory `A7`** (the `StockReplenishReplyPayload` wording) — a matter for whoever revisits `BC32`'s requirement text.

## 18. Hand-over

- **Feature 22** inherits: `BI8`'s three-lock order, restated as *read the invoice unlocked to find its party pair, then take the `credits` lock, then re-read the invoice under `UPDLOCK, HOLDLOCK, ROWLOCK`*; §7.3's note that it will be the first transaction to write **two** outbox rows and therefore the first to exercise `L16`'s ordering discipline; `MarkPaid`, delivered and uncalled, with the `UPDATE` path to add to `EfCoreInvoiceRepository`; and `BillingErrorMapper`'s three already-declared `PRECONDITION_FAILED` cases.
- **Feature 25 (Gateway) and feature 33 (the n8n bank robot)** inherit `L19`: an `InvoiceView` for an `issued` invoice **omits** `paidAt` rather than sending `null`. Both must read absent as "not paid". This is the one recorded divergence from #7's bytes in this feature.
- **Feature 24 (the projector)** inherits that `invoice.issued.v1` carries the full line list, so a timeline summary can be built without a call back to Billing.
- **The allocator family** — three near-identical files, no parity guard, no owner yet (§6.4, backlog id 47 is adjacent but narrower).
- **The live stack now carries a deliberately corrupted fixture — `ORD-000011`.** It sits at `despatched` with its 1000-unit credit hold already released, and its `invoice.issue` saga command permanently `rejected` (`PRECONDITION_FAILED` / `NoActiveHoldError`). This is **not** a saga regression: the hold was released by hand over raw NATS during feature 19's live-boot task `I4` (`progress/impl_billing_credit.md:128`), eleven hours before this feature's own live boot observed the resulting mismatch and correctly refused to invoice against a consumed hold. `otc_orders.saga_commands` for `ORD-000011` holds no `credit.release` row and the order was never cancelled, confirming no saga path produced it — see `progress/review_billing_invoicing.md` §8 for the full evidence chain. **Phase 11 should read this as the intended first live observation of the terminal-rejection path (feature 42), not rediscover it as a defect.**
