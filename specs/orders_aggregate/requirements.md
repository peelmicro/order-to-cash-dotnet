# Requirements — `orders_aggregate` (feature 13)

> **This is a pointer document, not new specification.** Assessment #8 inherits `specs/shared/` **verbatim** from `peelmicro/order-to-cash-nestjs` (#7). The requirements this feature realises — **R5 – R10** — were written in #7 and are binding here with the same ids. Nothing in this file amends, reinterprets or extends them; where a `.NET` realisation needs a decision the shared spec does not take, that decision lives in [`design.md`](./design.md) and is named there as a design decision, never as a requirement.
>
> Quoting convention: every requirement below is reproduced **verbatim** from `specs/shared/requirements.md` §1. The only alteration is line-wrapping — this repository forbids hard-wrapped prose (CLAUDE.md, *Markdown* row), so a requirement that occupies five wrapped lines there occupies one line here. No word, no emphasis and no id differs.

---

## 1. The requirements this feature realises

Six requirements, all provable by **pure domain unit tests** — no store, no broker, no framework (`specs/shared/requirements.md` §1: *"All of these are provable by pure domain tests"*).

### R5 — no empty orders (invariant O1)

> **R5.** IF an order is created with no lines, or its last remaining line is removed, THEN THE SYSTEM SHALL raise a domain error and SHALL NOT persist the order (invariant **O1**).

**Realised here as:** the `Order.Place` factory refuses an empty line collection, and `Order.RemoveLine` refuses to remove the last remaining line, both raising a `DomainError` carrying a stable `Code`. "SHALL NOT persist" is satisfied structurally rather than by a persistence check: an `Order` with no lines cannot be constructed, so no repository can ever be handed one. See `design.md` §5.

### R6 — totals are derived and never negative (invariant O3)

> **R6.** WHEN any line of an order is added, removed or modified, THE SYSTEM SHALL recompute the order's initial amount as the sum over lines of `unitPrice × quantity`, its initial discount as the sum of line discounts plus any order-level discount, and its total amount as `initialAmount − initialDiscount`; and IF the resulting total amount is negative, THEN THE SYSTEM SHALL raise a domain error and leave the order unchanged (invariant **O3**).

**Realised here as:** `InitialAmount`, `InitialDiscount` and `TotalAmount` are `Money` (`long` minor units) properties with **no public setter**; they are recomputed by a private routine after every accepted line mutation. "Leave the order unchanged" is realised by computing the candidate totals **before** the mutation is committed to the aggregate's line collection — see `design.md` §4.3. The *order-level* discount term is `Money.Zero`, exactly as in #7, and a non-zero `orderDiscount` arriving on the wire is refused rather than silently dropped — `design.md` §4.4, with #7's code cited.

### R7 — lines are frozen from `confirmed` onwards (invariant O4)

> **R7.** IF a line addition, removal or modification is attempted while the order status is `confirmed`, `despatched`, `invoiced`, `paid`, `completed` or `cancelled`, THEN THE SYSTEM SHALL raise a domain error and SHALL leave every field of the order unchanged (invariant **O4**).

**Realised here as:** a single `EnsureLinesMutable()` guard evaluated **first** in every mutating method, before any structural check, so that removing the last line of a `confirmed` order raises the *frozen* error and not the *empty order* error. See `design.md` §5.2.

### R8 — only the edges of Table T-1

> **R8.** THE SYSTEM SHALL permit an order status change only along an edge listed in Table T-1 of `domain-model.md` §3.3, SHALL treat `completed` and `cancelled` as terminal, and SHALL allow `cancelled` to be reached from `placed`, `stock_reserved`, `credit_approved` and `confirmed` only.

**Realised here as:** the eleven status-to-status edges of Table T-1 (rows 2–12; row 1 is creation, not a transition) are encoded once, as data, in `OrderStateMachine.LegalEdges`, and the aggregate exposes **no method that lets a caller name a target status**. See `design.md` §3, which is where the *"illegal transition impossible to express rather than merely rejected at runtime"* obligation is discharged — and where the residue that C# cannot make impossible is stated honestly rather than glossed.

### R9 — an illegal transition raises, changes nothing, appends nothing

> **R9.** IF an order status change is attempted along a `(from, to)` pair absent from Table T-1, THEN THE SYSTEM SHALL raise a domain error, SHALL leave the status and every other field unchanged, and SHALL append no domain event to the aggregate.

**Realised here as:** the private `TransitionTo` is the only writer of `Status`, and it consults `OrderStateMachine.LegalEdges` **before** mutating anything and **before** raising any domain event. The requirement's three legs — raises, mutates nothing, appends nothing — are asserted separately by the named test, over all 61 illegal pairs (`design.md` §3.4 derives the count).

### R10 — cancellation carries an immutable reason from the closed set (invariant O6)

> **R10.** WHEN an order transitions to `cancelled`, THE SYSTEM SHALL require a cancellation reason drawn from `{stock_rejected, credit_rejected, operator_cancelled}`, SHALL record it immutably on the order, and SHALL emit `order.cancelled.v1` carrying it; and IF no reason is supplied, THEN THE SYSTEM SHALL raise a domain error and SHALL NOT change the status (invariant **O6**).

**Realised here as:** a closed `CancellationReason` enum whose three members are exactly the three tokens, a `CancellationReason?` property with a private setter that `Cancel` refuses to overwrite, and an `OrderCancelled` domain event carrying the reason plus the compensation steps `asyncapi.yaml`'s `OrderCancelledPayload` requires. "IF no reason is supplied" is reachable in .NET only across a parsing boundary (an enum parameter cannot be absent), so the domain owns the parse — see `design.md` §6.2, which is why there are **two** codes here and not one. The reason↔status pairing Table T-1's *Trigger* column states (`stock_rejected` from `placed`, `credit_rejected` from `stock_reserved`, `operator_cancelled` from any of the four) is enforced on the aggregate as #7 enforces it — `design.md` §6.1.

---

## 2. Requirements this feature depends on but does not close

| Id | Owner | Standing for this feature |
|---|---|---|
| **R1** | `shared_kernel` (feature 7) | Domain half `DONE`; API half outstanding and ratified-scoped. `Money` is the type every amount on this aggregate uses; this feature adds no monetary representation of its own. |
| **R2** | `shared_kernel` (feature 7) | `DONE`. Invariant **O2** (*single currency*) is enforced on this aggregate by an explicit currency check on every line, but the *rule* is R2's and no new id is minted for it — `specs/shared/requirements.md` §9 already maps `M2 → R2`. |
| **R3**, **R4** | `shared_kernel` (feature 7) | `DONE`. `Quantity` and `GLN` are used as-is. |
| **R11**, **R12**, **R13** | `outbox_and_idempotency` (feature 14) | This feature *produces* the domain events those requirements publish, and fixes their `eventId`, `occurredAt`, `correlationId` and `causationId` at the moment the aggregate raises them (`domain-model.md` §7.1: *"generated in the domain when the event is created, not when it is published"*). It writes no outbox row and publishes nothing. |
| **R19** – **R29** | `order_saga_orchestrator` (feature 16) | The state machine built here is the saga's state — `domain-model.md` §3.1: *"This is also the saga state — there is no separate saga record"*. Every precondition R19–R28 states is a precondition on `Status`; this feature must make each of those transitions expressible and every other one refused, and nothing more. |

---

## 3. New requirements: none

**This feature introduces no new `R<n>`.** R5 – R10 cover it completely, and the .NET realisation satisfies each of them in the same sense #7's does — which is what reusing an id asserts.

Four questions arose while designing that a careless author could have "answered" by editing `specs/shared/`. None of them is a specification defect; **#7 faced every one of them**, and its answer is in its code or its `progress/history.md`, so each is resolved by citation rather than by a fresh decision. They are listed here so a reviewer can check the citation rather than take the judgement on trust.

| # | The question | #7's answer, and where it is | Realised in |
|---|---|---|---|
| 1 | Invariant **O8** says *"A successful state transition appends exactly one domain event"*, but Table T-1 names a fact for only seven of its twelve rows, and `domain-model.md` §7.2's fact catalogue is closed at fourteen — there is no `order.stock_reserved.v1`. | T-1 governs. `order-transitions.ts` types every edge's `emits` as `OrdersFactType \| null` and sets it `null` on the five silent edges; `order.ts` passes no event builder on those edges and its funnel gates on `transition.emits && options.buildEvent`; #7's history records *"T-1 governs O8 so five internal edges emit nothing (OA2)"*. | `design.md` §7.4 |
| 2 | The fixed `orders` table has **no `order_discount` column**, yet R6 requires initial discount to include *"any order-level discount"*. | The term is a constant zero — `order-totals.ts:25` — and no aggregate field exists for it; a non-zero `orderDiscount` on the wire is **refused** by the place-order handler (`place-order.handler.ts:68-69`), not dropped. | `design.md` §4.4 |
| 3 | `order_items` has **no ordering column**, yet `domain-model.md` §3.1 says lines are an *"ordered list"*. | #7 leaves the item query unordered and relies on line order being observable only in `order.placed.v1`, which ships from the in-memory aggregate. #8 adds a deterministic ascending-`id` clause: strictly tighter, nothing observable changes. | `design.md` §8.4 |
| 4 | The `orders` table has **no version / rowversion column**, so the aggregate carries no optimistic-concurrency token. | #7 carries none either and defends the saga with `saga.md` §6's three layers. Recorded as an inherited constraint so feature 16 does not assume a token exists. | `design.md` §8.6 |

Two further places where this design once had an answer of its own and now has #7's, because the divergence would have been observable: `Rehydrate` takes **no totals parameters** and re-derives all three from the lines (#7's OA3 — *"stored/derived drift is unrepresentable rather than merely detected"*), which removes the `order.totals_inconsistent` code entirely; and the reason↔status pairing of T-1's *Trigger* column is enforced on the aggregate (#7's OA4). Both are in `design.md` §8.3 and §6.1 with the #7 file and line cited.

If a shared document is ever genuinely wrong, that is a spec amendment: its own commit, its own gate, back-ported to #7. This feature makes no such claim, and — having checked #7 first — needs none.

---

## 4. Invariant coverage

`domain-model.md` §3.2 defines eight invariants on this aggregate. All eight are enforced here; the shared spec maps each to a requirement, and this feature adds no mapping of its own.

| Invariant | Statement (abridged) | Requirement | Enforced by |
|---|---|---|---|
| **O1** | An order always has ≥ 1 line | R5 | `Order.Place`, `Order.RemoveLine` |
| **O2** | Every line's `unitPrice` and `lineDiscount` share the order's currency | R2 | `Order.EnsureLineCurrency`, on add and on change |
| **O3** | Totals are derived, never set; `totalAmount ≥ 0` | R6 | `Order.RecomputeTotals`, candidate-then-commit |
| **O4** | Lines frozen from `confirmed` onwards | R7 | `Order.EnsureLinesMutable` |
| **O5** | Only legal transitions | R8, R9 | `OrderStateMachine.LegalEdges` + private `TransitionTo` |
| **O6** | Cancellation carries an immutable reason | R10 | `Order.Cancel`, `CancellationReason` |
| **O7** | Terminal states are terminal | R8 | absence of any outbound edge from `completed`/`cancelled` in `LegalEdges` |
| **O8** | Events accompany state; a rejected transition appends none | R9, R11 | `TransitionTo` raises the event only after the edge is accepted, and only on the seven fact-bearing rows of T-1 — #7's behaviour, cited in `design.md` §7.4 |

---

## 5. Traceability

Rows R5 – R10 of `specs/shared/test-matrix.md` §1 belong to this feature and are **`TODO`**. This spec pass **flipped no Status cell** — the implementer flips a row only when the named test exists and has been observed green (matrix rule 2).

The intended .NET test names are fixed here so the implementer has no room to invent different ones, and so the reviewer can check the mapping mechanically. The stack-neutral *Test file › case* sketch in columns 3–4 of the matrix is the contract; the names below are #8's realisation of it.

| Id | Matrix sketch (columns 3–4, unchanged) | #8 test file | #8 case name |
|---|---|---|---|
| **R5** | `orders/domain/order.spec` › *refuses to create an order with no lines and to remove the last remaining line* | `tests/Orders.UnitTests/OrderTests.cs` | `R5_Order_RefusesToCreateAnOrderWithNoLinesAndToRemoveTheLastRemainingLine` |
| **R6** | `orders/domain/order-totals.spec` › *recomputes initialAmount, initialDiscount and totalAmount after each mutation and rejects a negative total* | `tests/Orders.UnitTests/OrderTotalsTests.cs` | `R6_Order_RecomputesInitialAmountInitialDiscountAndTotalAmountAfterEachMutation`, `R6_Order_RejectsAMutationWhoseResultingTotalAmountWouldBeNegativeAndLeavesTheOrderUnchanged` |
| **R7** | `orders/domain/order.spec` › *refuses to add, remove or modify a line once the order is confirmed and leaves every field unchanged* | `tests/Orders.UnitTests/OrderTests.cs` | `R7_Order_RefusesToAddRemoveOrModifyALineOnceTheOrderIsConfirmedAndLeavesEveryFieldUnchanged` |
| **R8** | `orders/domain/order-state-machine.spec` › *walks every legal edge of Table T-1 and reaches cancelled only from placed, stock_reserved, credit_approved and confirmed* | `tests/Orders.UnitTests/OrderStateMachineTests.cs` | `R8_Order_WalksEveryLegalEdgeOfTableT1`, `R8_Order_ReachesCancelledOnlyFromPlacedStockReservedCreditApprovedAndConfirmed`, `R8_Order_TreatsCompletedAndCancelledAsTerminal` |
| **R9** | `orders/domain/order-state-machine.spec` › *raises on every (from, to) pair absent from Table T-1 without mutating state or appending an event* | `tests/Orders.UnitTests/OrderStateMachineTests.cs` | `R9_Order_RaisesOnEveryFromToPairAbsentFromTableT1WithoutMutatingStateOrAppendingAnEvent` |
| **R10** | `orders/domain/order-cancellation.spec` › *requires a reason from the closed set, records it immutably and carries it on order.cancelled.v1* | `tests/Orders.UnitTests/OrderCancellationTests.cs` | `R10_Order_RequiresAReasonFromTheClosedSetRecordsItImmutablyAndCarriesItOnOrderCancelledV1`, `R10_Order_RaisesWhenNoCancellationReasonIsSuppliedAndDoesNotChangeTheStatus`, `R10_Order_RefusesACancellationReasonTableT1DoesNotPairWithTheCurrentStatus` |

The naming shape (`R<n>_<Type>_<WhatItProves>`) is the one `shared_kernel` established and the matrix already records for R1 – R4; keeping it means a reviewer can find the test for a requirement by grep.

Tests that carry **no** `R<n>` prefix, because they guard a design decision rather than a shared requirement, are listed in [`tasks.md`](./tasks.md) §4 with the same specificity. Three of them are fact-emission guards required by CLAUDE.md's arming rule, and this feature is the *"double force"* case that rule names: **no live caller exists yet** for any of these branches, so no integration harness can reach them.

---

## 6. Open points for the human gate: none

The three points this document carried into the gate are closed, and none of them closed by a decision — each closed on evidence.

1. **The O8 reading.** Not open, and never was: #7 answered it in code and recorded the answer. `order-transitions.ts` gives five edges `emits: null`, `order.ts` passes no builder on those edges and gates on `transition.emits && options.buildEvent`, and #7's `progress/history.md` entry for `orders_aggregate` says *"T-1 governs O8 so five internal edges emit nothing (OA2)"*. `design.md` §7.4 now states it as inherited behaviour with the citation; the shared-document reasoning stays, corroborating it.
2. **Money column width.** The premise was wrong, so the point is gone rather than resolved. `specs/shared/` requires *"an integer count of minor units"* (R1) and *"integer minor units only"* (M1) and never a width; `int` was #7's MySQL choice, recorded in a document that describes #7's implementation rather than the shared contract. Feature 44 made all thirteen of #8's money columns `bigint` and deleted every narrowing cast on money. `design.md` §8.5 now says only that the storage type is the domain type; a narrowing cast on money is a defect under CLAUDE.md's *Money* row.
3. **Scope boundary.** Confirmed, and inherited: this feature writes `src/Orders/Domain/` and `tests/Orders.UnitTests/` only, while §8 designs the repository, EF mapping and outbox drain that features 14 and 15 are bound by. #7 drew the identical line at its own gate — *"the repository adapter deliberately not built — port interface only, adapter deferred to feature 15 (open point 5)"* — so feature 15 inherits a contract that predates it in both assessments.

**The rule these three taught, applied throughout this document.** Before a question goes to the gate, ask *"did #7 face this, and what did it do?"* — the answer is in its code or its `progress/history.md`, and fetching it is cheaper and more reliable than a gate round-trip. Only what #7 **could not** face, because the language or the engine differs, is genuinely a decision. Applying that test left nothing outstanding here: the two places where C# genuinely differs from TypeScript — an `enum` parameter cannot be absent, so R10's unwanted branch lives at the parse boundary (`design.md` §6.2), and full typestate would cost a nine-way switch on every load (`design.md` §3.2) — are both resolved in the design, not carried to the gate.
