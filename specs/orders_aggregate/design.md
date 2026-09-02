# Design — `orders_aggregate` (feature 13)

> **Where the value of this document is.** `specs/shared/` was inherited verbatim from #7, so the requirements were not written here — the *realisation* was. Everything below is .NET 10 / C# 14 specific and none of it exists anywhere else: which types, which `sealed record` versus which class, how Table T-1 is encoded, how domain events are collected and drained, how the aggregate maps onto an EF Core schema that is already migrated and seeded and may not change, and which `DomainError` codes callers will branch on from feature 15 onward.

## 0. Scope

| In scope | Out of scope (and who owns it) |
|---|---|
| `src/Orders/Domain/` — the `Order` aggregate root, the `OrderLine` child entity, `OrderStatus`, `CancellationReason`, the state machine, four domain events, the domain errors | The EF Core repository and the code-to-id resolution (§8 is its binding contract) — features 14 / 15 |
| `tests/Orders.UnitTests/` — pure domain unit tests, no framework, no store, no broker, no mock of infrastructure | Outbox rows, Kafka, NATS, the dispatcher — features 14, 43, 15, 16 |
| One new architecture test scoping the Orders domain's allowed dependencies | Any change to `src/Orders/Infrastructure/`, to the migration, or to the seed |

**The boundary is inherited, not invented here.** #7 drew the identical line at its own gate on this same feature: its `progress/history.md` entry for `orders_aggregate` records that *"the repository **adapter deliberately not built** — port interface only, adapter deferred to feature 15 (open point 5)"*. §8 below is therefore designed and not built, and features 14 and 15 are bound by it — a contract that predates them in both assessments.

The aggregate is **synchronous and pure**: it performs no I/O, so it has no `async` method and takes no `CancellationToken`. CLAUDE.md's *"async all the way down"* rule governs the layers that do I/O; a domain method that awaited anything would be a design error, not compliance.

## 1. Layout

```
src/Orders/Domain/
  Order.cs                          aggregate root       (class : AggregateRoot)
  OrderLine.cs                      child entity         (class : Entity)
  OrderStatus.cs                    enum + wire tokens
  CancellationReason.cs             enum + wire tokens + parse
  OrderStateMachine.cs              Table T-1 as data
  OrderTransition.cs                readonly record struct (From, To)
  CompensationStepKind.cs           enum + wire tokens
  OrderCompensationStep.cs          sealed record
  Events/
    OrderDomainEvent.cs             abstract record : IDomainEvent
    OrderPlaced.cs                  sealed record
    OrderConfirmed.cs               sealed record
    OrderCompleted.cs               sealed record
    OrderCancelled.cs               sealed record
  Errors/
    OrderMustHaveAtLeastOneLineError.cs
    OrderTotalMustNotBeNegativeError.cs
    OrderLinesAreFrozenError.cs
    OrderLineNotFoundError.cs
    OrderLineCurrencyMismatchError.cs
    IllegalOrderTransitionError.cs
    OrderNotCancellableError.cs        : IllegalOrderTransitionError
    CancellationReasonRequiredError.cs
    UnknownCancellationReasonError.cs
    CancellationReasonNotApplicableError.cs
  README_PLACEHOLDER.cs             KEEP — see §10.2
```

Namespaces: `OrderToCash.Orders.Domain`, `.Domain.Events`, `.Domain.Errors`. All three match `DomainAssemblies.DomainNamespacePattern` (`(^|\.)Domain(\.|$)`), so the twelve armed architecture rules cover every type added by this feature the moment it is added. That is deliberate: no new namespace is invented that would slip out from under them.

`Directory.Build.props` already sets `TreatWarningsAsErrors`; nothing in this feature needs a suppression, and none may be added.

## 2. Type shapes, and why each is what it is

| Type | C# shape | Reason |
|---|---|---|
| `Order` | `sealed class : AggregateRoot` | Identity equality, mutable state, collects domain events. CLAUDE.md: *"`Entity`/`AggregateRoot` are classes with identity equality"*. |
| `OrderLine` | `sealed class : Entity` | `domain-model.md` §3.1 gives it an `id` and calls it a child entity *"with identity within the aggregate"*. Two lines with the same product and price are **not** the same line. A record would make them equal and silently collapse in a set. |
| `OrderStatus`, `CancellationReason`, `CompensationStepKind` | `enum` + a static `*Tokens` class | Closed sets. The enum is the domain vocabulary; the snake_case token (`stock_reserved`) is the storage and wire vocabulary, produced by an explicit mapping so no one is ever tempted to `ToString().ToLower()` — which would emit `stockreserved` and pass every test that only round-trips through itself. |
| `OrderTransition` | `readonly record struct (OrderStatus From, OrderStatus To)` | Value equality is exactly what a set membership test wants; a struct avoids 72 allocations in the R9 test. |
| `OrderCompensationStep` | `sealed record` | Immutable value carried on one event. |
| `OrderDomainEvent` and its four subtypes | `abstract record` / `sealed record` | Immutable facts; value equality makes assertions in tests read directly. |
| Errors | `sealed class : DomainError` (except `OrderNotCancellableError`, §9) | `DomainError` is an `Exception`; C# exceptions are classes. |

`Money` is used for every amount and `Quantity` for every count, both from `SharedKernel`. No `decimal`, `float` or `double` appears anywhere in this feature — the two armed reflection rules in `tests/Architecture.Tests/DomainDecimalTests.cs` scan fields, properties, parameters, return types, constructor parameters and conversion operators, and there is no reviewed exception for anything in Orders.

## 3. The status state machine

### 3.1 The encoding: an enum, a table of edges, and no way to name a target

```csharp
public enum OrderStatus { Placed, StockReserved, CreditApproved, Confirmed, Despatched, Invoiced, Paid, Completed, Cancelled }
```

`OrderStateMachine` holds Table T-1's eleven **status-to-status** edges once, as data, each carrying the T-1 row number it transcribes:

```csharp
internal static class OrderStateMachine
{
    // Table T-1 rows 2–12. Row 1 ((none) -> placed) is creation, not a transition:
    // it is Order.Place, and it has no `from` to look up.
    public static readonly FrozenSet<OrderTransition> LegalEdges = new OrderTransition[]
    {
        new(Placed,         StockReserved),   // T-1 row 2
        new(StockReserved,  CreditApproved),  // T-1 row 3
        new(CreditApproved, Confirmed),       // T-1 row 4
        new(Confirmed,      Despatched),      // T-1 row 5
        new(Despatched,     Invoiced),        // T-1 row 6
        new(Invoiced,       Paid),            // T-1 row 7
        new(Paid,           Completed),       // T-1 row 8
        new(Placed,         Cancelled),       // T-1 row 9
        new(StockReserved,  Cancelled),       // T-1 row 10
        new(CreditApproved, Cancelled),       // T-1 row 11
        new(Confirmed,      Cancelled),       // T-1 row 12
    }.ToFrozenSet();

    public static bool IsLegal(OrderStatus from, OrderStatus to) => LegalEdges.Contains(new(from, to));
}
```

`System.Collections.Frozen` is BCL, not a framework: it is not on any banned list, adds no `PackageReference`, and buys a faster lookup for a set that is built once and read on every transition.

**`Status` has exactly one writer in the entire codebase**: a `private void TransitionTo(OrderStatus to, ...)` on `Order`. It is `private`, not `internal` and not `public`, and there is no property setter, no `WithStatus`, no `ApplyTransition(from, to)` and no reflection hook.

### 3.2 How "impossible to express" is discharged — and what remains

The requirement is that an illegal transition be *impossible to express rather than merely rejected at runtime*. The design discharges it in three layers and then states the residue plainly, because a design that claimed total compile-time impossibility here would be lying.

**Layer 1 — the caller cannot name a target state.** `Order` exposes one intention-revealing method per Table T-1 trigger, and each method's target status is a **compile-time constant in its own body**. There is no method that takes an `OrderStatus` parameter. A caller therefore cannot write `placed -> confirmed` at all; the expression does not exist in the API.

| # | T-1 row | Method | Target (constant in the body) | Fact raised |
|---|---|---|---|---|
| 1 | 1 | `static Order Place(...)` | `Placed` | `order.placed.v1` |
| 2 | 2 | `MarkStockReserved(...)` | `StockReserved` | — |
| 3 | 3 | `ApproveCredit(...)` | `CreditApproved` | — |
| 4 | 4 | `Confirm(...)` | `Confirmed` | `order.confirmed.v1` |
| 5 | 5 | `MarkDespatched(...)` | `Despatched` | — |
| 6 | 6 | `MarkInvoiced(...)` | `Invoiced` | — |
| 7 | 7 | `MarkPaid(...)` | `Paid` | — |
| 8 | 8 | `Complete(...)` | `Completed` | `order.completed.v1` |
| 9 | 9–12 | `Cancel(reason, steps, ...)` | `Cancelled` | `order.cancelled.v1` |

Nine methods for twelve rows: `Cancel` serves the four cancel edges, because they differ in their *source*, never in their target or their effect.

**Layer 2 — the edge set is data, and a test proves it equals T-1.** `LegalEdges` is the only authority `TransitionTo` consults. A named test transcribes Table T-1 independently, from the specification rather than from the constant, and asserts **set equality in both directions** — so an edge added to the code that T-1 does not have fails just as loudly as one deleted.

**Layer 3 — the precondition is checked before anything mutates.** `TransitionTo` evaluates `IsLegal(Status, to)` before it assigns `Status`, before it stamps `UpdatedAt`, and before it calls `Raise`. R9's three legs therefore hold structurally, not by careful ordering that a later edit could disturb: the guard is the first statement in the method and every mutation is below it.

**The residue, stated honestly.** Full compile-time impossibility would mean typestate — nine distinct types, `PlacedOrder.MarkStockReserved()` returning a `StockReservedOrder` — and the reason not to is concrete rather than aesthetic: the repository loads a status out of an `nvarchar(20)` column into a variable whose type is not known until run time, so every load would end in a nine-way switch that reintroduces exactly the run-time dispatch typestate was supposed to remove, and the saga orchestrator would need nine handler shapes for what `saga.md` describes as one state machine. What layers 1–3 buy is that **the illegal transition cannot be written**, the legal set cannot drift from T-1 unnoticed, and what remains rejected at run time is the *precondition* (`Confirm()` on an order that is not `credit_approved`) — which is precisely the thing R25 and `saga.md` §6 layer 2 rely on being a run-time check, since a redelivered fact is a run-time event.

### 3.3 Terminality (O7) needs no code

`Completed` and `Cancelled` appear in `LegalEdges` only as targets, never as a `From`. `IsLegal` therefore returns false for all sixteen outbound pairs without a single `if`. This is worth stating because a reviewer will look for a terminality check and should find its absence to be the design, not an omission — and because the `LegalEdges`-equals-T-1 test is what guards it.

### 3.4 The arithmetic the R9 test rests on

Nine statuses. Eight of them are reachable as a target (`Placed` is not: creation is not a transition, so no method targets it). That is **9 × 8 = 72** attemptable `(from, to)` pairs, of which **11** are legal and **61** must raise. The test enumerates all 72 from a dictionary of `OrderStatus -> Action<Order>` — the same nine methods of §3.2 — constructs an order in each `from` state, and asserts for each of the 61: an `IllegalOrderTransitionError` (or its subclass) is thrown, `Status` is unchanged, `DomainEvents.Count` is unchanged, and `UpdatedAt` is unchanged. The three counts (72 / 11 / 61) are asserted explicitly in the test so that adding a status without extending the test fails.

Constructing an order in an arbitrary `from` state uses `Order.Rehydrate` (§8.3) — including states like `Completed` that a legal walk can reach only through eight prior transitions, and including `Cancelled`, which a legal walk can reach only by consuming a reason. The complementary R8 test does the opposite and walks the real edges from `Order.Place`, so the two together prove the machine both ways: the legal walk is genuine, and the illegal matrix is exhaustive.

## 4. Totals (O3, R6)

### 4.1 State

`InitialAmount`, `InitialDiscount` and `TotalAmount` are `Money` properties with `private set`. `Currency` is a `string` property (the order's ISO 4217 code), set once at construction and never changed. There is no public writer for any of the four; O3's *"no caller may assign them"* is enforced by the language, not by a convention.

### 4.2 The computation

```
initialAmount   = Σ over lines of (unitPrice * quantity)      // Money.Multiply(Quantity) — long minor units
initialDiscount = Σ over lines of lineDiscount + orderDiscount   // orderDiscount is Money.Zero — §4.4
totalAmount     = initialAmount − initialDiscount
```

All three use `Money`'s own closed arithmetic (`Add`, `Subtract`, `Multiply`), which means a stray currency anywhere raises `money.cross_currency` (M2/R2) rather than producing a wrong number. The accumulator seed is `Money.Zero(Currency)`, so an order's currency governs even before the first line is added.

`Money.Multiply` is `long * int -> long` and is **not** `checked`. Nothing below it bounds the value — the money columns are `bigint` (§8.5), the same width as `MinorUnits` — and an order large enough to overflow `long` minor units (of the order of 92 quadrillion major units) is not reachable in this system. No guard is added for an unreachable condition.

### 4.3 "Leave the order unchanged" — candidate then commit

R6's negative-total clause and R7's *"leave every field of the order unchanged"* both require that a **rejected** mutation leaves no trace. A recompute-after-mutate design cannot honour that without an undo, so every mutating method follows one shape:

1. `EnsureLinesMutable()` — O4 / R7, first, before anything else (§5.2).
2. Validate the arguments in isolation (currency, line exists).
3. Build the **candidate** line collection — a local `List<OrderLine>` with the add / removal / change applied. The aggregate's own `_lines` is untouched.
4. Compute candidate totals from that list.
5. Apply O1 (candidate has ≥ 1 line) and O3 (candidate total not negative). Any failure throws **here**, with the aggregate still holding its original lines, totals, status, events and `UpdatedAt`.
6. Only now commit: assign `_lines`, assign the three totals, stamp `UpdatedAt`.

The tests for R6 and R7 assert the unchanged-ness explicitly, field by field, and include `DomainEvents.Count` in the comparison.

### 4.4 The order-level discount is always zero — #7's answer, inherited

The fixed schema has `initial_amount`, `initial_discount` and `total_amount` and no `order_discount`, while R6 requires initial discount to be *"the sum of line discounts plus any order-level discount"*.

**#7 faced this and answered it; the answer is inherited rather than re-decided.** In `apps/orders/src/domain/order-totals.ts:25` the order-level discount is a constant — `const orderDiscount = Money.zero(currency)` — written into the sum so R6's formula appears in full and evaluates to Σ line discounts. #7's aggregate carries no order-level discount field and no method that sets one. `orderDiscount` is nevertheless part of the wire contract (`openapi.yaml` `OrdersCreateRequest`, `asyncapi.yaml` `OrdersCreateRequestPayload` — optional in both), so a non-zero value is **refused rather than silently dropped**: `apps/orders/src/application/place-order.handler.ts:68-69` throws `OrderDiscountNotSupportedError`, whose own comment names this exact reasoning.

**#8 does the same.** `RecomputeTotals` writes the formula with `orderDiscount = Money.Zero(Currency)`; there is no `_orderDiscount` field, no `ApplyOrderDiscount` method, nothing to persist and nothing to derive on load. The refusal of a non-zero `orderDiscount` on the wire belongs to feature 15's place-order handler, not to the domain — it is a statement about what this implementation supports, not an order invariant — and it is recorded here so feature 15 inherits the refusal instead of inventing acceptance.

R6's third clause stays testable in the same sense in both assessments: the formula contains the term, the term is zero, and the request that would make it non-zero is rejected with a specific error rather than accepted and quietly ignored.

## 5. Lines

### 5.1 The mutating API

| Method | Purpose | R |
|---|---|---|
| `AddLine(string productCode, string? description, Quantity quantity, Money unitPrice, Money lineDiscount)` | append a line, returns its `UniqueId` | R6, R7 |
| `RemoveLine(UniqueId lineId)` | remove a line; refuses the last one | R5, R6, R7 |
| `ChangeLine(UniqueId lineId, Quantity quantity, Money unitPrice, Money lineDiscount)` | replace the three mutable fields of one line | R6, R7 |

`ChangeLine` replaces all three mutable fields at once rather than offering three setters: one method means one place where the freeze, the currency check and the recompute are applied, and a partial-update overload set is three places for them to drift apart.

`productCode` and `description` are snapshots and are immutable on an existing line — changing the product is removing a line and adding another, which is what an operator amending an order actually does. `Lines` is exposed as `IReadOnlyList<OrderLine>` over a private `List<OrderLine>`; no caller can reach the backing store.

**No live caller exists for any of these three methods yet.** Feature 15 places orders with all lines supplied at once, and nothing in the trilogy amends an order after placement (ORDCHG is out of scope by O4). They exist because R5, R6 and R7 are requirements about them, and their tests are the only thing that will ever exercise them — which is exactly the *"double force"* case in CLAUDE.md's fact-emission rule and the reason §11.3 arms them deliberately.

### 5.2 The freeze (O4, R7) — order of checks matters

`EnsureLinesMutable()` throws `OrderLinesAreFrozenError` when `Status` is one of `Confirmed`, `Despatched`, `Invoiced`, `Paid`, `Completed`, `Cancelled` — the exact six R7 lists. It is the **first statement** of all three mutating methods, before argument validation and before any structural check.

That ordering is a requirement, not a preference. Removing the last line of a `confirmed` order violates both O1 and O4; R7 says the answer is the frozen refusal (*"SHALL leave every field of the order unchanged"*), and a reader who saw `order.no_lines` there would conclude, wrongly, that lines are mutable after confirmation and this order merely happened to have one. The R7 test asserts the `Code`, not just that something was thrown.

The three states that permit mutation — `Placed`, `StockReserved`, `CreditApproved` — are the complement of R7's list, and that is deliberate in the shared spec (O4 is *"frozen from `confirmed` onwards"*, not "frozen once placed").

### 5.3 Single currency (O2)

`EnsureLineCurrency(Money unitPrice, Money lineDiscount)` compares both against `Currency` and throws `OrderLineCurrencyMismatchError` (`order.line_currency_mismatch`) on either mismatch. It runs before the candidate list is built, so a rejected line never reaches the arithmetic.

Without it the mismatch would still be caught, one step later, by `Money.Add` raising `money.cross_currency` — correct but wrong-flavoured: the caller sees a *shared kernel* arithmetic error where the actual violation is an *order* invariant, and the RPC boundary (§9.2) would map it to a less useful code. R2 remains the requirement; this is O2's enforcement point on this aggregate.

## 6. Cancellation (O6, R10)

### 6.1 The method

```csharp
public void Cancel(CancellationReason reason, IReadOnlyList<OrderCompensationStep> compensationSteps, DateTimeOffset occurredAt, UniqueId causationId)
```

`CancellationReason` is a nullable-valued property (`CancellationReason?`) with a `private set`, assigned exactly once inside `TransitionTo`'s accepted branch. Immutability is structural: `Cancelled` has no outbound edge, so a second `Cancel` is refused by the state machine before it could overwrite the reason — the reason cannot be changed because the state cannot be re-entered.

`compensationSteps` is copied defensively into a new list and carried on the `OrderCancelled` event only. It is **not** stored on the aggregate and not persisted: there is no column for it, and there does not need to be — `asyncapi.yaml`'s `OrderCancelledPayload` requires it on the fact, the projector puts it in the timeline (R50), and the timeline is where a reviewer reads it. Empty is legal and is the expected value for `stock_rejected` (R26: *"Empty for `stock_rejected` — nothing was ever acquired"*).

Which states may cancel is Table T-1's business, not `Cancel`'s: rows 9–12 give `Placed`, `StockReserved`, `CreditApproved`, `Confirmed`, and the absence of a row from `Despatched` onwards is what makes `Cancel` on a despatched order raise. No `if` in `Cancel` repeats that list; a second copy of it would be a second thing to keep in step with T-1.

**Which reason may accompany which source is a second rule, and it is #7's.** Table T-1's *Trigger* column pairs a reason with a source: row 9 admits `stock_rejected` (or `operator_cancelled`), row 10 admits `credit_rejected` (or `operator_cancelled`), rows 11 and 12 admit `operator_cancelled` only. #7 enforces exactly that pairing on the aggregate — `apps/orders/src/domain/order.ts:413-419` refuses `stock_rejected` unless the status is `placed` and `credit_rejected` unless it is `stock_reserved`, raising `CancellationReasonNotApplicableError` (its OA4). #8 inherits both the rule and a distinct error type (`order.cancellation_reason_not_applicable`, §9.1), because without it `Cancel(credit_rejected)` from `confirmed` would be accepted here and refused there — an observable divergence on a shared saga path. `operator_cancelled` is unrestricted, exactly as in T-1 and in #7: two `if`s, not four.

### 6.2 "IF no reason is supplied" — where that is even reachable

In C# an `enum` parameter cannot be absent, so on the method above R10's unwanted-behaviour clause has no reachable branch. It becomes reachable at the **parsing boundary**, which is where every real caller sits: the saga orchestrator reads a reason out of a fact payload, and feature 41's cancel responder reads one off an RPC request. The domain therefore owns the parse:

```csharp
public static CancellationReason Parse(string? token)   // in CancellationReasons
```

- `null`, empty or whitespace -> `CancellationReasonRequiredError` (`order.cancellation_reason_required`).
- A non-empty token outside the closed set -> `UnknownCancellationReasonError` (`order.cancellation_reason_unknown`), whose message names the offending token.
- Otherwise the matching member, compared with `StringComparison.Ordinal` against the three wire tokens.

Two codes, not one, because the two cases are different to a caller: a missing reason is a *contract* failure by the sender, an unknown one is a *vocabulary* failure — usually a version skew — and `saga.md`'s dead-letter reasoning needs to tell them apart. The parse is in the domain, not in the presentation layer, because the closed set is a domain rule (`domain-model.md` §3.1: *"`CancellationReason` is a closed set"*) and duplicating it into two responders is how the fourth value gets invented.

R10's clause is then satisfied end to end, and the named test proves both halves: the parse refuses, and — because the parse refuses before `Cancel` is ever called — `Status` is unchanged.

## 7. Domain events

### 7.1 The base record

```csharp
public abstract record OrderDomainEvent(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public abstract string EventType { get; }
}
```

Six of the seven envelope fields of `domain-model.md` §7.1 are fixed **inside the domain, at the moment the fact becomes true** — which is what §7.1 requires of `eventId` (*"Generated in the domain when the event is created, not when it is published"*) and of `occurredAt` (*"stamped by the aggregate, not when it was published or consumed"*). The seventh, `payload`, is the event's own fields.

- `EventId` = `UniqueId.New()`, minted by the aggregate.
- `AggregateId` = the order's id.
- `CorrelationId` = **also** the order's id — §7.1: *"Always the order id"*. The aggregate sets it itself rather than accepting it, because there is no correct alternative value and a parameter would be a way to get it wrong.
- `CausationId` = a **parameter** of every aggregate method. The aggregate cannot know what caused it: for `Place` it is the id of the originating `orders.create` command, for `Confirm` it is the `eventId` of the `credit.approved.v1` that drove the saga. Making it a required parameter means R12's causal chain is unbreakable by omission — there is no default and no null.
- `OccurredAt` = a **parameter** (§7.3).

### 7.2 The four events

| Type | `EventType` | Fields beyond the base |
|---|---|---|
| `OrderPlaced` | `order.placed.v1` | `OrderReference`, `RetailerCode`, `CompanyCode`, `BuyerGln`, `SupplierGln`, `Currency`, `OrderDate`, `Lines` (`IReadOnlyList<OrderPlacedLine>`), `InitialAmount`, `InitialDiscount`, `TotalAmount`, `Notes` |
| `OrderConfirmed` | `order.confirmed.v1` | `OrderReference`, `RetailerCode`, `CompanyCode`, `Currency`, `TotalAmount`, `ConfirmedAt` |
| `OrderCompleted` | `order.completed.v1` | `OrderReference`, `RetailerCode`, `CompanyCode`, `Currency`, `TotalAmount`, `CompletedAt` |
| `OrderCancelled` | `order.cancelled.v1` | `OrderReference`, `RetailerCode`, `CompanyCode`, `CancellationReason`, `CancelledAt`, `CompensationSteps` |

The field lists are the `required` lists of `asyncapi.yaml`'s four order payload schemas, plus `notes`, which is optional there and nullable here.

**These are domain types and they carry domain types** — `OrderNumber`, `Money`, `GLN`, `UniqueId`, `Quantity`, `DateTimeOffset` — not `OrderToCash.Contracts.Facts.Payloads.*`. `Orders.csproj` references `Contracts`, so nothing in the compiler stops the domain from using those records; the reason not to is that `Contracts` is the **wire** contract, versioned by `asyncapi.yaml` and shaped by `JsonWire`'s serializer options, and a domain that referenced it would make a wire change a domain change. The mapping domain-event -> `Envelope<TPayload>` belongs to feature 14's outbox writer, in `Infrastructure/`. A new architecture test (`OrdersDomainMustNotDependOnContracts`, §11.2) makes that a rule rather than an intention — scoped to Orders, because a consumer-side domain such as the projector's may legitimately want the payload types.

`ConfirmedAt` / `CompletedAt` / `CancelledAt` duplicate `OccurredAt` by value. They are kept because `asyncapi.yaml` requires them in the payload and the payload is the parity claim; the mapper sets both from the same instant.

### 7.3 Time: no clock in the domain

Every method that raises an event takes `DateTimeOffset occurredAt` as a parameter. There is no `IClock` field, no `DateTimeOffset.UtcNow`, no `TimeProvider`.

`test-matrix.md`'s domain-unit level says *"no clock — time comes from a controllable clock port"*. A parameter is the strictest reading of that: the aggregate becomes a pure function of its inputs, so a domain test needs no clock double at all, and the port itself (`IClock`, to be declared in `Orders/Application/Ports/` by feature 15) sits where CLAUDE.md puts ports — in the application layer, which the domain must not depend on. Putting `IClock` in the domain would invert that dependency; putting it in `SharedKernel` would widen a project that is deliberately dependency-free for a need only the callers have.

`DateTimeOffset` rather than `DateTime`: the offset is carried explicitly, so a UTC instant cannot be silently reinterpreted as local. §8.2 covers the conversion to the `datetime2(3)` column.

### 7.4 Which transitions raise an event — settled by #7, verified in its code

An event is raised for the **seven** Table T-1 rows that name a fact (rows 1, 4, 8, 9, 10, 11, 12 -> `order.placed.v1`, `order.confirmed.v1`, `order.completed.v1`, and `order.cancelled.v1` four times), and for **none** of the five silent rows (2, 3, 5, 6, 7).

**This is inherited behaviour, not a reading awaiting ratification.** #7 implemented precisely it, and the evidence is in three independent places:

1. `apps/orders/src/domain/order-transitions.ts` gives every edge an `emits` field typed `OrdersFactType | null`, and it is **`null` on five edges** — `stock_reserved`, `credit_approved`, `despatched`, `invoiced`, `paid` (lines 36, 42, 54, 60, 66). The field's own comment reads *"the T-1 'Fact emitted by Orders' cell — `null` when the edge emits nothing (OA2)"*.
2. `apps/orders/src/domain/order.ts` calls `this.transitionTo('stock_reserved', ctx)` with **no** event builder (line 361, and the same shape for the other four silent edges), against `this.transitionTo('confirmed', ctx, { buildEvent: orderConfirmedEvent })` for the fact-bearing ones (line 369) — and its funnel gates on `if (transition.emits && options.buildEvent)` (line 452), so even a builder passed by mistake on a silent edge would emit nothing.
3. #7's `progress/history.md` entry for `orders_aggregate` states the resolution outright: *"T-1 governs O8 so five internal edges emit nothing (OA2)"*.

**The shared documents corroborate it.** Invariant O8 says *"A successful state transition appends exactly one domain event to the aggregate's uncommitted-event collection"*, which read alone would demand an event on all twelve. Three pieces of the shared specification say otherwise, and they are not soft:

1. `domain-model.md` §7.2 catalogues **fourteen** facts and `asyncapi.yaml` declares exactly those fourteen. There is no `order.stock_reserved.v1`. An event raised on row 2 would have no `eventType` to carry, and R11 requires `eventType` to be present and to match `<aggregate>.<fact>.v<n>`.
2. Table T-1's own *"Fact emitted by Orders"* column is `—` for those five rows. It is a normative column of a normative table.
3. The intermediate states are already observable without an Orders fact: the timeline shows `stock.reserved.v1`, `credit.approved.v1`, `order.despatched.v1`, `invoice.issued.v1` and `payment.received.v1` from the contexts that own them (R50), which is why Orders emitting a duplicate would add nothing.

O8's operative half — the half a test can fail on — is its second sentence, *"A rejected transition appends none"*, and R9 states that normatively and independently. The first half is a statement about fact-bearing transitions, and both assessments read it that way.

**The alternative neither assessment took.** The aggregate could raise an internal `OrderStatusChanged` on every transition and let the outbox writer filter by fact type. It is implementable, and it is worse: it puts an event with no catalogued `eventType` into the same collection that feature 14 drains straight into the outbox, so the *only* thing preventing an uncatalogued fact from reaching Kafka would be a filter in infrastructure — precisely the class of guard R11 exists because someone forgot.

**Nothing shared is edited and no id is reinterpreted.** The suppression is guarded in the same direction #7 guards it: §11.3 arming row 5 fails when an emission is *added* to a silent edge.

### 7.5 Collection and drain

`AggregateRoot` (feature 7) already provides `Raise`, `DomainEvents` and `ClearDomainEvents`; this feature adds nothing to it. The contract this feature fixes for feature 14 is:

1. The aggregate appends via the protected `Raise` in the accepted branch of `TransitionTo`, **after** the edge is validated and the state assigned.
2. `DomainEvents` is exposed as `IReadOnlyList<IDomainEvent>` in raise order. For an order confirmed by the saga in a single load (`stock_reserved -> credit_approved -> confirmed` in one handler, `saga.md` §3.1), the collection holds exactly one event — `OrderConfirmed` — because row 3 is silent and row 4 is not.
3. **Feature 14 drains, the aggregate never does.** The repository's `SaveChangesAsync` reads `DomainEvents`, writes one `outbox` row per event **in the same transaction** as the aggregate rows (R13), and calls `ClearDomainEvents()` only after `SaveChangesAsync` returns. Calling it earlier loses the events if the transaction rolls back.
4. The aggregate publishes nothing and knows nothing about the outbox (R14; `domain-model.md` §8.5).

## 8. Persistence — mapping onto a schema that may not change

**Nothing in this section is implemented by this feature.** It exists to prove, before code is written, that the aggregate as designed is persistable through the configurations landed by feature 9 (`db_orders`) **without changing the migration**, and to bind features 14 and 15 to a mapping rather than leaving them to invent one.

### 8.1 Field by field

| Domain (`Order`) | Column in `otc_orders.orders` | Mapping |
|---|---|---|
| `Id : UniqueId` | `id uniqueidentifier` | `UniqueId.Value` / `UniqueId.From` |
| `OrderReference : OrderNumber` | `order_reference nvarchar(20)` | `.Value` / `OrderNumber.Parse`. `ORD-` + 6 digits = 10 chars, comfortably inside 20 |
| `OrderDate : DateTimeOffset` | `order_date datetime2(3)` | §8.2 |
| `CompanyCode`, `SupplierGln` | `company_id uniqueidentifier` | §8.3 — resolved through `companies` |
| `RetailerCode`, `BuyerGln` | `retailer_id uniqueidentifier` | §8.3 — resolved through `retailers` |
| `Currency : string` | `currency_id uniqueidentifier` | §8.3 — resolved through `currencies` |
| `InitialAmount : Money` | `initial_amount bigint` | `Money.MinorUnits` / `new Money(value, currency)` — same width, no conversion (§8.5) |
| `InitialDiscount : Money` | `initial_discount bigint` | as above |
| `TotalAmount : Money` | `total_amount bigint` | as above |
| `Status : OrderStatus` | `status nvarchar(20)` | `OrderStatuses.ToToken` / `Parse`. Longest token `credit_approved` = 15 |
| `CancellationReason : CancellationReason?` | `cancellation_reason nvarchar(100) NULL` | `CancellationReasons.ToToken` / `Parse`; `NULL` iff not `Cancelled` |
| `Notes : string?` | `notes nvarchar(max) NULL` | direct |
| — | `created_at`, `updated_at datetime2(3)` | infrastructure timestamps; `UpdatedAt` is stamped by the aggregate (§8.7) |

| Domain (`OrderLine`) | Column in `otc_orders.order_items` | Mapping |
|---|---|---|
| `Id : UniqueId` | `id uniqueidentifier` | direct |
| *(owner)* | `order_id uniqueidentifier` | the aggregate's id |
| `ProductCode : string` | `product_id uniqueidentifier` | §8.3 — resolved through `products` |
| `Description : string?` | `description nvarchar(255) NOT NULL` | `?? string.Empty` on write; the column is non-null and the domain field is optional |
| `UnitPrice : Money` | `price bigint` | `Money.MinorUnits` / `new Money(value, currency)` — same width, no conversion (§8.5) |
| `Quantity : Quantity` | `quantity int` | `.Value` / `new Quantity(...)` |
| `LineDiscount : Money` | `discount bigint` | as `price` |

Every column of both tables is accounted for, and every domain field has a home. **No column is added, dropped, retyped, renamed or re-indexed.**

### 8.2 Instants

The domain uses `DateTimeOffset`; the columns are `datetime2(3)`, which stores no offset. Write `value.UtcDateTime`; read `new DateTimeOffset(value, TimeSpan.Zero)`. Every instant in this system is UTC by CLAUDE.md's *Dates* row, so the round trip is lossless to millisecond precision — which is also the precision `occurredAt` carries on the wire.

### 8.3 Rehydration, and the codes-versus-ids gap

The aggregate speaks the **business vocabulary** the shared model mandates (`domain-model.md` §8.4: *"Business references are the inter-context vocabulary"*): `retailerCode`, `companyCode`, `productCode`, `buyerGln`, `supplierGln`, `currency`. The tables store **local foreign keys**: `retailer_id`, `company_id`, `currency_id`, `product_id`.

This is not a boundary violation. All four reference tables live in `otc_orders`, the Orders service's own database, and the Orders context owns *"the reference catalogue (products, retailers, suppliers, currencies) used to compose an order"* (§1). The rule that is never broken — no join across a *context* boundary — is untouched.

The repository therefore resolves in both directions, and the `Order` aggregate cannot be rehydrated from `orders` + `order_items` alone; it needs `retailers`, `companies`, `currencies` and `products` too. That is a fact features 14/15 must plan a query around, and it is stated here so it is not discovered late.

```csharp
public static Order Rehydrate(
    UniqueId id, OrderNumber orderReference, DateTimeOffset orderDate,
    string retailerCode, GLN buyerGln, string companyCode, GLN supplierGln,
    string currency, OrderStatus status, CancellationReason? cancellationReason,
    string? notes, IReadOnlyList<OrderLine> lines,
    DateTimeOffset createdAt, DateTimeOffset updatedAt);
```

**No totals parameters — this too is #7's answer, inherited.** #7's `OrderSnapshot` carries no totals fields at all and `Order.reconstitute` re-derives all three from the lines (`apps/orders/src/domain/order-snapshot.ts`, header comment; `apps/orders/src/domain/order.ts:215`), so that *"a stored/derived drift is unrepresentable rather than merely detected"* — recorded in #7's `progress/history.md` as OA3. #8 does the same: the repository reads `initial_amount`, `initial_discount` and `total_amount` for the projector and for anyone querying the table, but does not hand them to the aggregate, and O3's identities hold on load by construction rather than by a check that could disagree with the data. There is consequently no `order.totals_inconsistent` error and no `INTERNAL_ERROR` path here — a code #7 does not have would be a code features 15, 41 and 42 would have to branch on in only one of the two assessments.

`Rehydrate` restores a state the aggregate previously produced. It therefore **bypasses the state machine** (the seed already contains orders in `completed` and `cancelled`, and no legal walk can reach those from a factory call) and **raises no domain event** (they were published in the transaction that created them; re-raising on load would republish the whole history). Both properties get their own named tests, because a `Rehydrate` that silently raised would flood the outbox on the next save.

It is not a back door. It validates what a load can still meaningfully check — the same four #7's `reconstitute` checks:

- the status token is a member of the closed set (a `nvarchar(20)` column can hold anything);
- O1 — at least one line;
- O2 — every line's `unitPrice` and `lineDiscount` in the order's currency;
- O6 — `cancellationReason` present **iff** `status = Cancelled`, refusing both halves of the biconditional separately so the message says which one failed.

O3 needs no check because the totals are recomputed, not accepted.

### 8.4 Line order

`order_items` has no ordering column. Reload order is fixed as **ascending `id`** — deterministic, stable across reloads, and index-supported.

#7 leaves the item query unordered (`apps/orders/src/infrastructure/persistence/order.repository.ts:219-228` has no `orderBy`), so a deterministic clause here is strictly tighter than #7 and changes nothing observable.

That is enough because line order is observable on the wire in exactly one place: `order.placed.v1`, whose `lines[]` array is serialised from the **in-memory** aggregate inside the placing transaction, so the caller's own order is what ships. No other Orders fact carries lines; invoice and despatch lines are built by Billing and Fulfillment from their own aggregates. The `domain-model.md` §3.1 phrase *"ordered list"* is honoured where it is observable and given a deterministic answer where it is not.

### 8.5 Money columns

`Money.MinorUnits` is `long`, and every money column this aggregate touches — `orders.initial_amount`, `orders.initial_discount`, `orders.total_amount`, `order_items.price`, `order_items.discount` — is `bigint`. The storage type is the domain type: the mapping is an assignment in both directions and there is no narrowing boundary anywhere on this aggregate.

Provenance, in one sentence: `specs/shared/` requires *"an integer count of minor units"* (R1) and *"integer minor units only"* (M1) and has never specified a width, so `bigint` is the width that costs nothing and removes a boundary that could truncate. A narrowing cast on a money value is a defect under CLAUDE.md's *Money* row, not something to make loud.

### 8.6 No concurrency token

`orders` has no `version` or `rowversion` column, so the aggregate carries no optimistic-concurrency field and this feature invents none — a domain property with no column cannot be persisted, and a domain property that exists only in memory would be a lie about what is enforced.

`saga.md` §6 does not need one: the orchestrator is defended by the dedup record (R17/R18), by the state-machine precondition (R25 — *"The transition it performs is legal exactly once, because performing it changes the status away from S"*), and by idempotent commands (R29). Concurrent handlers racing on one order is nevertheless a real scenario, and the answer — a pessimistic read under `UPDLOCK, ROWLOCK` when loading an order for a saga step, or the equivalent — belongs to feature 16, which owns the transaction. Recorded here as an inherited constraint so feature 16 does not assume a token exists.

### 8.7 `UpdatedAt`

The aggregate holds `CreatedAt` and `UpdatedAt` as `DateTimeOffset` properties, and every accepted mutation — transition or line change — stamps `UpdatedAt` from the same `occurredAt` parameter that stamps the event. A rejected mutation leaves it alone, which is why the R7 and R9 tests can assert it as part of "every field unchanged". Letting the database or `SaveChanges` set it would put a field the domain tests assert on outside the domain's control.

## 9. Domain errors

### 9.1 The codes

Stable, because callers branch on them and they cross the RPC boundary from feature 15 onward. The shape follows the convention `shared_kernel` set (`money.cross_currency`, `quantity.must_be_strictly_positive_integer`): `<subject>.<snake_case_reason>`, lowercase, no versioning.

| Type | `Code` | Raised when | R |
|---|---|---|---|
| `OrderMustHaveAtLeastOneLineError` | `order.must_have_at_least_one_line` | `Place` with no lines; `RemoveLine` on the last line | R5 |
| `OrderTotalMustNotBeNegativeError` | `order.total_must_not_be_negative` | a candidate mutation yields `totalAmount < 0` | R6 |
| `OrderLinesAreFrozenError` | `order.lines_are_frozen` | any line mutation while `Status` is one of R7's six | R7 |
| `OrderLineNotFoundError` | `order.line_not_found` | `RemoveLine` / `ChangeLine` with an unknown `lineId` | R6, R7 |
| `OrderLineCurrencyMismatchError` | `order.line_currency_mismatch` | a line amount is not in the order's currency | R2 (O2) |
| `IllegalOrderTransitionError` | `order.illegal_transition` | `(from, to)` absent from T-1 | R8, R9 |
| `OrderNotCancellableError` | `order.not_cancellable` | `Cancel` from a status with no cancel edge | R8, R9 |
| `CancellationReasonRequiredError` | `order.cancellation_reason_required` | `Parse(null / empty / whitespace)` | R10 |
| `UnknownCancellationReasonError` | `order.cancellation_reason_unknown` | `Parse` of a token outside the closed set | R10 |
| `CancellationReasonNotApplicableError` | `order.cancellation_reason_not_applicable` | the reason does not pair with the current status per T-1's *Trigger* column (§6.1) | R10 (#7's OA4) |

Every message carries the specifics — the offending status pair, the line id, the two amounts — because `DomainError.Message` is what reaches an operator, while `Code` is what reaches a machine.

**`OrderNotCancellableError` derives from `IllegalOrderTransitionError`, which derives from `DomainError`.** It is not a peer: a refused cancellation *is* an illegal transition, and the inheritance means R9's test — which asserts over all 61 illegal pairs — catches the base type and still passes for the cancel pairs, while a caller that wants to say *"this order can no longer be cancelled"* branches on the specialised code. Two flat types would force the R9 test to special-case sixteen of its pairs, and a special case in an exhaustive test is where exhaustiveness goes to die.

### 9.2 Crossing the RPC boundary — #7's mapping, reproduced

`asyncapi.yaml`'s `RpcError.code` is a **closed enum** of twelve values, so a responder cannot put `order.lines_are_frozen` in `code`. #7 already fixed how that narrowing is done, and an RPC reply is on the wire — the same wire the API test script asserts against — so #8 reproduces #7's mapping rather than inventing a finer-grained one. The codes are minted here; the mapping is **implemented** by whichever feature owns the responder.

| Responder | Outcome | Reply |
|---|---|---|
| `orders.create` (feature 15) | any `DomainError` from this aggregate | `code: VALIDATION_FAILED`, `message` = the domain message, `details: { code: <domain Code> }` — `apps/orders/src/presentation/rpc-error-mapper.ts:72-75` |
| `orders.create` (feature 15) | unresolvable reference data | `NOT_FOUND` with `details: { field, value }` |
| `orders.create` (feature 15) | non-zero `orderDiscount` (§4.4) | `VALIDATION_FAILED` |
| `orders.create` (feature 15) | stock check unavailable / timed out | `UNAVAILABLE` / `TIMEOUT` with the subject in `details` |
| `orders.cancel` (feature 41) | no such order | `NOT_FOUND` |
| `orders.cancel` (feature 41) | status has no cancel edge — `OrderNotCancellableError` | `ORDER_NOT_CANCELLABLE` with `details: { status }` — `apps/orders/src/presentation/orders-cancel.controller.ts:50-61`; the gateway maps it to HTTP 409, never 503 |
| saga command replies (feature 16) | same vocabulary as above | a business rejection keeps its business code; only transport failures become `TIMEOUT` / `UNAVAILABLE` |

Two details are load-bearing and easy to get wrong. The `details` key is **`code`**, not `domainCode`: it is #7's key and it is on the wire. And every aggregate refusal reaching `orders.create` is client-caused, so collapsing them to `VALIDATION_FAILED` loses nothing a caller needs — the specific domain `Code` travels in `details.code`.

The distinction the mapping preserves matters to feature 42 (terminal-rejection classification): a `VALIDATION_FAILED` or an `ORDER_NOT_CANCELLABLE` reply is a *business* outcome and must not be retried at capped backoff forever, whereas a `TIMEOUT` is transport and must be. That depends on the domain codes being stable, which is why they are fixed at the point they are minted rather than by each responder.

## 10. Command-handler shape (for features 43, 15, 16)

The dispatcher is a settled ruling of this phase's gate and is binding across all six services. This feature builds none of it, but the aggregate's API is shaped so that the handlers written next are thin, and that shape is recorded here so feature 15 does not invent a different one.

### 10.1 The shape

```csharp
public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, PlaceOrderResult>
{
    public async Task<PlaceOrderResult> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var order = Order.Place(/* value objects built from the command */, occurredAt: _clock.UtcNow, causationId: command.RequestId);
        await _orders.AddAsync(order, cancellationToken);          // drains DomainEvents into outbox, one transaction
        return new PlaceOrderResult(order.Id, order.OrderReference);
    }
}
```

Four properties every handler over this aggregate holds to:

1. **One aggregate method per handler.** *"Consistency boundary = aggregate"* (`domain-model.md` §8.6). A handler that calls two intent methods on two aggregates is a saga step wearing a handler's clothes.
2. **The handler validates nothing the aggregate validates.** Duplicated invariants drift; the aggregate raises `DomainError` and the handler lets it propagate to the responder, which maps it by §9.2.
3. **The handler supplies `occurredAt` (from `IClock`) and `causationId` (from the triggering command or fact).** The aggregate never reaches for either (§7.1, §7.3).
4. **The handler never touches `DomainEvents`.** The repository drains them (§7.5). A handler that reads them is about to publish something.

### 10.2 The port

`IOrderRepository` is declared in `Orders/Application/Ports/` (application declares ports; infrastructure implements them) and is **not** part of this feature. Its shape is fixed here only so far as this aggregate constrains it: `AddAsync(Order, CancellationToken)`, `GetByIdAsync(UniqueId, CancellationToken)`, `GetByReferenceAsync(OrderNumber, CancellationToken)`, `SaveChangesAsync(CancellationToken)` — the last drains and clears (§7.5).

`src/Orders/Domain/README_PLACEHOLDER.cs` **must not be deleted**: `tests/Architecture.Tests/DomainAssemblies.cs` resolves the Orders assembly through `typeof(OrderToCash.Orders.Domain.OrdersDomainPlaceholder)`, and removing it breaks all twelve architecture rules for every service at once.

## 11. Test design

### 11.1 Project

New: `tests/Orders.UnitTests/` — `OrderToCash.Orders.UnitTests`, added to `OrderToCash.sln`, copying `tests/SharedKernel.UnitTests/SharedKernel.UnitTests.csproj` exactly: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`. Project references: `src/Orders/Orders.csproj` and `src/SharedKernel/SharedKernel.csproj`.

`coverlet.collector` is not optional — `quality.sh` warns when a test project produces no `coverage.cobertura.xml`, and the domain-layer coverage gate (≥ 80%) is what this feature's coverage is measured against when feature 34 arms it.

Files, mapped to `test-matrix.md`'s stack-neutral paths:

| Matrix path | #8 file |
|---|---|
| `orders/domain/order.spec` | `tests/Orders.UnitTests/OrderTests.cs` |
| `orders/domain/order-totals.spec` | `tests/Orders.UnitTests/OrderTotalsTests.cs` |
| `orders/domain/order-state-machine.spec` | `tests/Orders.UnitTests/OrderStateMachineTests.cs` |
| `orders/domain/order-cancellation.spec` | `tests/Orders.UnitTests/OrderCancellationTests.cs` |
| *(no matrix row — design guards)* | `tests/Orders.UnitTests/OrderEventsTests.cs`, `tests/Orders.UnitTests/OrderRehydrationTests.cs` |

A shared `OrderTestData` builder supplies a valid order (one retailer GLN, one supplier GLN, EUR, two lines) so no test spends its body on setup. It is a **builder, not a mock**: these tests touch no infrastructure, so there is nothing to mock, and CLAUDE.md's *"Domain unit tests are pure"* rule means a mocking library must not appear in this project's `PackageReference` list at all.

### 11.2 The new architecture rule

`OrdersDomainMustNotDependOnContracts`, added to `tests/Architecture.Tests/`, asserting that no type in `OrderToCash.Orders.Domain*` depends on `OrderToCash.Contracts`. Scoped to Orders deliberately (§7.2). It is armed like any other guard: add a `using` of a Contracts payload to an Orders domain event, watch it fail, record the message, restore, force the rebuild, re-run.

### 11.3 Arming — what must be armed, and why this feature is the hard case

CLAUDE.md: *"Every branch that emits — or deliberately suppresses — a domain fact must be guarded by a test that fails when the emission is deleted ... with double force where the branch has no live caller yet, because integration harnesses cannot reach it."*

**This whole feature is that case.** There is no Gateway, no responder, no saga and no outbox yet; the *only* thing that will ever execute these branches before feature 16 is the unit test written beside them. Ten branches must be armed:

| # | Branch | Arm by | Must fail |
|---|---|---|---|
| 1 | `Place` raises `OrderPlaced` | delete the `Raise` | `OrderEventsTests` |
| 2 | `Confirm` raises `OrderConfirmed` | delete the `Raise` | `OrderEventsTests` |
| 3 | `Complete` raises `OrderCompleted` | delete the `Raise` | `OrderEventsTests` |
| 4 | `Cancel` raises `OrderCancelled` carrying the reason | delete the `Raise` | `OrderCancellationTests` (R10) |
| 5 | the five silent edges raise **nothing** | add a `Raise` to `MarkStockReserved` | `OrderEventsTests` |
| 6 | a rejected transition appends no event | move the `Raise` above the `IsLegal` guard | `OrderStateMachineTests` (R9) |
| 7 | `Rehydrate` raises nothing | add a `Raise` to `Rehydrate` | `OrderRehydrationTests` |
| 8 | the freeze precedes the structural check | swap the two guards in `RemoveLine` | `OrderTests` (R7) |
| 9 | `LegalEdges` equals T-1 | delete `new(Confirmed, Cancelled)` | `OrderStateMachineTests` (R8) |
| 10 | the reason↔status pairing of §6.1 | delete the `credit_rejected` guard | `OrderCancellationTests` (R10) |

Rows 5 and 7 are *suppression* guards: they fail when an emission is **added**, which is the direction that matters for a fact catalogue closed at fourteen.

The protocol is CLAUDE.md's, in full and without shortcuts: back up the file by copy first (**never** restore with `git checkout --` — these files are untracked while the feature is in flight, and the restore silently does nothing), introduce the violation, run the specific named test, record the failure message **verbatim**, restore from the backup, **force the rebuild** (`touch` the restored file or `dotnet build --no-incremental`), then confirm green. An arming table produced without the forced rebuild proves nothing about the code on disk. All ten rows, with their verbatim messages, go in `progress/impl_orders_aggregate.md`.

### 11.4 Three completeness tests worth having

- **`LegalEdges` equals Table T-1**, both directions (§3.2 layer 2). The test transcribes T-1 from the specification, not from the constant.
- **The four `EventType` values are in `FactCatalog`.** `tests/Orders.UnitTests` may reference `Contracts` (it is a test project; only the *domain* is barred), so it can assert that `order.placed.v1`, `order.confirmed.v1`, `order.completed.v1` and `order.cancelled.v1` are all keys of `FactCatalog.PayloadTypesByEventType`. This catches a typo in an `EventType` literal at unit-test time rather than at the first Kafka publish in feature 14.
- **`Rehydrate` derives the three totals from the lines.** The signature takes no totals (§8.3), so the test's job is to prove the derivation runs on load and matches what `Place` would have computed from the same lines — the property that makes stored/derived drift unrepresentable here as it is in #7.

## 12. Explicit non-goals

- No repository, no `DbContext` change, no migration, no EF Core reference anywhere near `Domain/`.
- No outbox row, no Kafka, no NATS, no dispatcher, no handler, no DI registration.
- No `orders.create` validation, no availability check, no `requestId` dedup (R62 belongs to feature 27).
- No `OrderStatus` presentation mapping beyond the snake_case tokens the store and the wire already require.
- No `order.saga_failed.v1`. It is the fourteenth fact of the catalogue and it *is* an `Order` fact, appended without a status change (R29's dead-letter clause, OR3), but it belongs to the dead-letter feature — as it did in #7, which added `recordSagaFailure` to this same aggregate in a much later phase, not in `orders_aggregate`.
- No amendment, rewording or reinterpretation of anything under `specs/shared/`.
