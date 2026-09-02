# Tasks — `orders_aggregate` (feature 13)

> Ordered, and the order is load-bearing: the value objects and the state machine come before the aggregate that uses them, the aggregate before its events, and every test is written **inside** the loop beside the code it proves — never in a batch at the end. Tick a box only when the thing is done and, where it names a test, when that test has been observed green.
>
> **Files this feature may touch:** `src/Orders/Domain/**` (new files only), `tests/Orders.UnitTests/**` (new project), `tests/Architecture.Tests/` (one new rule, §5), `OrderToCash.sln`, `progress/impl_orders_aggregate.md`, `feature_list.json`, and the Status column of rows R5 – R10 in `specs/shared/test-matrix.md`.
>
> **Files this feature must not touch:** anything under `src/Orders/Infrastructure/`, the migration, the seed, `src/SharedKernel/`, `src/Contracts/`, `specs/shared/` **except** the six Status cells named above, and `src/Orders/Domain/README_PLACEHOLDER.cs`, which must survive (`design.md` §10.2).

## 1. Scaffolding

- [x] 1.1 Create `tests/Orders.UnitTests/Orders.UnitTests.csproj` — `RootNamespace`/`AssemblyName` `OrderToCash.Orders.UnitTests`, `IsPackable false`; packages `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`; project references `src/Orders/Orders.csproj` and `src/SharedKernel/SharedKernel.csproj`. Copy `tests/SharedKernel.UnitTests/SharedKernel.UnitTests.csproj` rather than writing it fresh. No mocking library.
- [x] 1.2 Add the project to `OrderToCash.sln`; `dotnet build` clean with `TreatWarningsAsErrors` in force.
- [x] 1.3 Confirm `src/Orders/Domain/README_PLACEHOLDER.cs` is still present and untouched.

## 2. Vocabulary and the state machine (`design.md` §2, §3)

- [x] 2.1 `OrderStatus.cs` — the nine-member enum plus `OrderStatuses.ToToken` / `Parse` for the snake_case tokens of `openapi.yaml`'s `OrderStatus` enum. Ordinal comparison; no `ToString().ToLower()`.
- [x] 2.2 `CancellationReason.cs` — the three-member enum, `ToToken`, and `Parse(string?)` raising `CancellationReasonRequiredError` on null/empty/whitespace and `UnknownCancellationReasonError` on an out-of-set token (`design.md` §6.2).
- [x] 2.3 `CompensationStepKind.cs` and `OrderCompensationStep.cs` — the two-member enum and the `sealed record` carrying `Step`, `EventId`, `EventType`, `OccurredAt`, `Summary`, per `asyncapi.yaml`'s `CompensationStep`.
- [x] 2.4 `OrderTransition.cs` and `OrderStateMachine.cs` — the eleven edges of Table T-1 rows 2–12 as a `FrozenSet`, each with its row number in a comment, plus `IsLegal`.
- [x] 2.5 **Test** `OrderStateMachineTests.LegalEdges_Are_Exactly_The_Eleven_Status_To_Status_Edges_Of_Table_T1` — transcribe T-1 from `specs/shared/domain-model.md` §3.3 independently and assert set equality **both ways**. Arming row 9.
- [x] 2.6 **Test** `OrderStatuses_And_CancellationReasons_RoundTrip_Their_Wire_Tokens` — every enum member maps to the exact token published in `openapi.yaml` / `asyncapi.yaml` and parses back.
- [x] 2.7 **Test** `R10_CancellationReasons_Parse_RaisesWhenTheTokenIsMissingOrOutsideTheClosedSet` — both codes, distinctly.

## 3. The aggregate (`design.md` §4, §5, §6)

- [x] 3.1 `Errors/` — the ten `DomainError` subclasses of `design.md` §9.1, with the exact `Code` strings in that table and messages carrying the specifics. `OrderNotCancellableError` derives from `IllegalOrderTransitionError`.
- [x] 3.2 `OrderLine.cs` — `sealed class : Entity`; `ProductCode` and `Description` immutable, `Quantity` / `UnitPrice` / `LineDiscount` settable only through the aggregate.
- [x] 3.3 `Order.cs` skeleton — properties per `design.md` §8.1, all setters `private`; private `List<OrderLine> _lines` exposed as `IReadOnlyList<OrderLine>`; private `_orderDiscount`.
- [x] 3.4 `Order.Place(...)` — the T-1 row 1 factory. Validates at least one line (O1), line currencies (O2), computes totals (O3), sets `Placed`, raises `OrderPlaced`.
- [x] 3.5 Private `TransitionTo(OrderStatus to, ...)` — the sole writer of `Status`: `IsLegal` first, then assign, then stamp `UpdatedAt`, then `Raise` where the edge bears a fact. `Cancel`'s illegal case raises `OrderNotCancellableError`.
- [x] 3.6 The eight transition methods of `design.md` §3.2 — `MarkStockReserved`, `ApproveCredit`, `Confirm`, `MarkDespatched`, `MarkInvoiced`, `MarkPaid`, `Complete`, `Cancel`. No method takes an `OrderStatus` parameter.
- [x] 3.7 `EnsureLinesMutable()`, `EnsureLineCurrency(...)`, `RecomputeTotals(...)` — the private guards.
- [x] 3.8 `AddLine`, `RemoveLine`, `ChangeLine` — each following the candidate-then-commit shape of `design.md` §4.3, with the freeze evaluated **first**. No `ApplyOrderDiscount`: the order-level discount term is `Money.Zero` (`design.md` §4.4).
- [x] 3.9 **Test** `R5_Order_RefusesToCreateAnOrderWithNoLinesAndToRemoveTheLastRemainingLine` — `tests/Orders.UnitTests/OrderTests.cs`. Asserts `Code = order.must_have_at_least_one_line` in both cases.
- [x] 3.10 **Test** `R6_Order_RecomputesInitialAmountInitialDiscountAndTotalAmountAfterEachMutation` — `OrderTotalsTests.cs`. Covers add, remove and change, asserting all three totals after each, and that `initialDiscount` is Σ line discounts because the order-level term is zero.
- [x] 3.11 **Test** `R6_Order_RejectsAMutationWhoseResultingTotalAmountWouldBeNegativeAndLeavesTheOrderUnchanged` — `OrderTotalsTests.cs`. Asserts every field, including `DomainEvents.Count` and `UpdatedAt`, is untouched.
- [x] 3.12 **Test** `R7_Order_RefusesToAddRemoveOrModifyALineOnceTheOrderIsConfirmedAndLeavesEveryFieldUnchanged` — `OrderTests.cs`. All four mutators, from `confirmed` and from a later state; asserts `Code = order.lines_are_frozen`, which is what makes the guard ordering of `design.md` §5.2 provable. Arming row 8.
- [x] 3.13 **Test** `O2_Order_RefusesALineWhosePriceOrDiscountIsNotInTheOrdersCurrency` — `OrderTests.cs`. Asserts `order.line_currency_mismatch`, **not** `money.cross_currency` (`design.md` §5.3).

## 4. State machine behaviour and events (`design.md` §3.4, §7)

- [x] 4.1 `Events/OrderDomainEvent.cs` plus the four `sealed record`s of `design.md` §7.2, carrying domain types only and each with its `EventType` literal.
- [x] 4.2 Wire `Raise` into `Place`, `Confirm`, `Complete`, `Cancel` — and into nothing else (`design.md` §7.4).
- [x] 4.3 **Test** `R8_Order_WalksEveryLegalEdgeOfTableT1` — `OrderStateMachineTests.cs`. Starts from `Order.Place` and walks real transitions, never `Rehydrate`.
- [x] 4.4 **Test** `R8_Order_ReachesCancelledOnlyFromPlacedStockReservedCreditApprovedAndConfirmed` — the four legal sources succeed, `Despatched`, `Invoiced`, `Paid`, `Completed` and `Cancelled` raise `OrderNotCancellableError`.
- [x] 4.5 **Test** `R8_Order_TreatsCompletedAndCancelledAsTerminal` — all sixteen outbound attempts refused.
- [x] 4.6 **Test** `R9_Order_RaisesOnEveryFromToPairAbsentFromTableT1WithoutMutatingStateOrAppendingAnEvent` — all 72 attemptable pairs, asserting the 72 / 11 / 61 counts explicitly and, for each of the 61: throws, `Status` unchanged, `DomainEvents.Count` unchanged, `UpdatedAt` unchanged. Arming row 6.
- [x] 4.7 **Test** `R10_Order_RequiresAReasonFromTheClosedSetRecordsItImmutablyAndCarriesItOnOrderCancelledV1` — `OrderCancellationTests.cs`. Asserts the reason lands on the aggregate, that a second `Cancel` is refused (immutability via terminality), and that `OrderCancelled` carries the reason **and** the compensation steps, empty for `stock_rejected` per R26. Arming row 4.
- [x] 4.8 **Test** `R10_Order_RaisesWhenNoCancellationReasonIsSuppliedAndDoesNotChangeTheStatus` — through `CancellationReasons.Parse`, asserting the status is untouched.
- [x] 4.8a **Test** `R10_Order_RefusesACancellationReasonTableT1DoesNotPairWithTheCurrentStatus` — `OrderCancellationTests.cs`. `stock_rejected` from anything but `Placed` and `credit_rejected` from anything but `StockReserved` raise `order.cancellation_reason_not_applicable`, `operator_cancelled` is accepted from all four cancellable states, and the status is unchanged in every refusal (`design.md` §6.1, #7's OA4). Arming row 10.
- [x] 4.9 **Test** `O8_Order_AppendsExactlyOneDomainEventForEachFactBearingEdgeOfTableT1` — `OrderEventsTests.cs`. Arming rows 1, 2, 3.
- [x] 4.10 **Test** `O8_Order_AppendsNoDomainEventOnTheFiveSilentEdgesOfTableT1` — `OrderEventsTests.cs`. The suppression guard; fails when an emission is **added**. Arming row 5.
- [x] 4.11 **Test** `R12_Order_StampsEveryDomainEventWithAFreshEventIdTheOrderIdAsCorrelationIdAndTheSuppliedCausationId` — `OrderEventsTests.cs`. Two events from one order carry different `EventId`s and the same `CorrelationId`. (Prepares R12; the matrix row stays with feature 14, which owns the outbox half.)
- [x] 4.12 **Test** `Order_EventTypes_AreAllDeclaredInTheSharedFactCatalog` — `OrderEventsTests.cs`, referencing `Contracts` from the test project only (`design.md` §11.4).
- [x] 4.13 **Test** `Order_ClearDomainEvents_EmptiesThePendingListAndLeavesEveryOtherFieldUntouched` — the drain contract feature 14 depends on.

## 5. Rehydration and the architecture rule (`design.md` §8.3, §11.2)

- [x] 5.1 `Order.Rehydrate(...)` — the signature in `design.md` §8.3: **no totals parameters**. Bypasses the state machine, raises nothing, re-derives the three totals from the lines, and validates the status token, O1, O2 and O6 (`cancellationReason` present iff `Cancelled`, both halves refused separately).
- [x] 5.2 **Test** `Order_Rehydrate_RestoresATerminalOrderWithoutWalkingTheStateMachineAndWithoutRaisingAnyEvent` — `OrderRehydrationTests.cs`, using `completed` and `cancelled`, the two states the seed actually contains. Arming row 7.
- [x] 5.3 **Test** `Order_Rehydrate_DerivesTheThreeTotalsFromTheLinesRatherThanFromStoredValues` — the same lines rehydrated give the totals `Place` computes, and there is no parameter through which a stored total could contradict them (`design.md` §8.3, #7's OA3).
- [x] 5.4 **Test** `Order_Rehydrate_RefusesAStatusTokenOutsideTheClosedSetAndAReasonThatDoesNotMatchTheStatus` — both halves of the O6 biconditional, separately.
- [x] 5.5 Add `OrdersDomainMustNotDependOnContracts` to `tests/Architecture.Tests/` — scoped to `OrderToCash.Orders.Domain*`, message naming the offending types like the other twelve rules.

## 6. Arming, gates and closure

- [x] 6.1 Arm all ten rows of `design.md` §11.3, one at a time. Back up by **file copy** first; never restore with `git checkout --` (the files are untracked). After each restore, **force the rebuild** (`touch` the file or `dotnet build --no-incremental`) before the confirming green run, and re-read the restored line to confirm it.
- [x] 6.2 Arm `OrdersDomainMustNotDependOnContracts` the same way.
- [x] 6.3 Record every arming in `progress/impl_orders_aggregate.md`: the branch, the mutation, the test that failed, and the failure message **verbatim**. An arming table without the forced rebuild proves nothing and will be rejected.
- [x] 6.4 `dotnet test` on the whole solution green, including the twelve existing architecture rules — none of which may need an exemption for anything this feature adds.
- [x] 6.5 `./quality.sh` green: format, build, test, and a `coverage.cobertura.xml` produced for `Orders.UnitTests` (no "coverlet.collector may not be wired" warning). Report the Orders domain line coverage figure against the ≥ 80% target even though feature 34 has not yet armed the gate.
- [x] 6.6 `./init.sh` exit 0.
- [x] 6.7 Flip rows **R5 – R10** in `specs/shared/test-matrix.md` from `TODO` to `DONE`, each naming the real `tests/Orders.UnitTests/*.cs` file and the real case name from `requirements.md` §5. Update the coverage-summary counts for feature 1 and the totals accordingly. **Column 5 only** — columns 1–4 and every other row are inherited verbatim and stay byte-for-byte.
- [x] 6.8 Write `progress/impl_orders_aggregate.md`: what was built, the arming table, the acceptance-criteria walk from `feature_list.json`, the coverage figure, and — separately and prominently — anything the implementation learned that bears on `requirements.md` §3 or §6, which carry **no** open points: all three were closed on evidence from #7 before implementation began, and reopening one is a report to the human, not a change made in code.
- [x] 6.9 Set feature 13 to `in_review` in `feature_list.json`. **Do not set it to `done`** — the reviewer does that.
- [x] 6.10 Stop. Report what was done and how to test it by hand. **No `git commit`, no `git push`.**

## 7. Traps this feature is known to contain

Written down because each is a specific way to produce a green suite that proves less than it appears to.

1. **Restoring an armed file with `git checkout --`.** Every file this feature creates is untracked while it is in flight, so the command fails, restores nothing, and leaves the file armed while its own error scrolls past. Copy-backup, always.
2. **Confirming a restore without forcing the rebuild.** MSBuild sees the source as older than its output and skips the compile, so the "confirming green run" executes the previously armed binary — a stale-but-correct binary vouching for source that is still armed. `cmp` against the backup is a source-level check and does not catch this.
3. **Testing the state machine against `OrderStateMachine.LegalEdges` instead of against Table T-1.** A test that reads the same constant the code reads proves the constant equals itself. Transcribe T-1 from the specification.
4. **Letting `RemoveLine` on a `confirmed` order raise `order.must_have_at_least_one_line`.** Both invariants are violated; R7 says the answer is the frozen one. Assert the `Code`, not merely that something threw.
5. **Recomputing totals before validating them.** A rejected mutation must leave the aggregate byte-identical; assert `UpdatedAt` and `DomainEvents.Count` in the unchanged-ness checks, not just the three totals.
6. **Raising an event on a silent edge because O8 seems to ask for it.** T-1 governs, and this is settled behaviour inherited from #7, not an open reading — `design.md` §7.4 cites the three places #7 records it. The suppression is a guard in its own right (arming row 5): it must fail when an emission is *added*.
7. **Reaching for `Contracts` payload types inside `Domain/`.** It compiles — `Orders.csproj` references `Contracts` for the infrastructure layer. Task 5.5 is what makes it fail.
8. **Deleting `README_PLACEHOLDER.cs` now that real types exist.** `DomainAssemblies.cs` resolves the Orders assembly through it, and its removal breaks all twelve architecture rules for every service at once.
9. **Re-deciding something #7 already settled.** The O8 emission rule, the zero order-level discount, the totals-free `Rehydrate` and the reason↔status pairing are all #7's answers, cited with file and line in `design.md`. If one looks wrong while implementing, the move is to stop and report — not to choose differently, because a silent divergence here is a parity break the benchmark cannot see.
10. **Editing anything in `specs/shared/` other than the six Status cells.** That file is inherited verbatim; a change to columns 1–4, to a rule, or to another feature's row is a spec amendment and needs its own human gate.
