# `billing_invoicing` (id 21, phase 10) — implementation record

## What was built

The `Invoice` aggregate (EDI INVOIC), its closed `issued → paid` state machine, the `invoice.issue`/`invoice.list` NATS RPC subjects on the existing Billing responder (renamed service-scoped per the gate ruling), the `INV-######` allocator, `R40`'s first live caller (the `consume` ledger entry), and the two carried backlog items (id 55, and feature 20's review finding N2).

### Domain (`src/Billing/Domain/`)
- `InvoiceState.cs` — the closed hierarchy (**see below**, this is the property the feature turns on), plus `InvoiceStatuses` (`ToToken`/`Parse`).
- `Invoice.cs` — aggregate root: `Issue`, `Reconstitute`, `MarkPaid`, `ToSnapshot`.
- `InvoiceLine.cs`, `InvoiceSnapshot.cs` (+ `InvoiceLineSnapshot`).
- `Errors/` — nine new `DomainError` subclasses (`EmptyInvoiceLinesError`, `InvoiceLineCurrencyMismatchError`, `NegativeInvoiceTotalError`, `InvalidInvoiceSnapshotError`, `InvoiceAlreadyPaidError`, `InvoicePaymentAmountMismatchError`, `InvoicePaymentCurrencyMismatchError`, `InvoiceTotalOverflowError`, `UnknownInvoiceStatusError`).
- `Events/InvoiceIssued.cs`, `Events/PaymentReceived.cs`.

### Persistence (`src/Billing/Infrastructure/Persistence/`)
- `InvoiceRowMapper.cs`, `EfCoreInvoiceRepository.cs`, `EfCoreInvoiceNumberAllocator.cs` (verbatim copy of `EfCoreDespatchNumberAllocator`), `EfCoreInvoiceReadRepository.cs`.
- Ports: `IInvoiceRepository`, `IInvoiceNumberAllocator`, `IInvoiceReadPort` (`src/Billing/Application/Ports/`).
- **No migration, no `*Configuration.cs` change** — confirmed by reading `InvoiceConfiguration.cs`/`InvoiceItemConfiguration.cs`/`InvoiceNumberSequenceConfiguration.cs`, all already present from phase 6 with the columns and indexes this feature needs.

### Application (`src/Billing/Application/`)
- `InvoiceIssueService.cs` — the two-aggregate transaction (`Invoice` + `BuyerCredit`), the three-lock order (`credits` → `invoices` → `invoice_number_sequences`).
- `Commands/IssueInvoiceCommand.cs`, `Queries/ListInvoicesQuery.cs`, `InvoiceApplicationErrors.cs` (`InvoiceCurrencyMismatchError`).

### Presentation (`src/Billing/Presentation/`)
- `Rpc/InvoiceSubjects.cs`, `Rpc/InvoiceRequestValidator.cs`.
- The four-type rename (gate-approved): `CreditRpcResponder`→`BillingRpcResponder`, `CreditResponderOptions`→`BillingResponderOptions`, `CreditErrorMapper`→`BillingErrorMapper`, `CreditFactPayloadMapper`→`BillingFactPayloadMapper` — plus the two new subscription loops and dispatch arms.
- `Infrastructure/Messaging/Rpc/InvoiceRpcPayloads.cs` — the seven records, parsed-from-spec guarded (`BI28`).

### Wiring
`BillingServiceCollectionExtensions.cs` — four new `AddScoped` registrations, no new hosted service.

### Backlog closures riding this feature
- **Id 55** (`BC32`'s universal claim false at six sites) — fixed in `CreditListTests.cs` (3 sites) and `StockListTests.cs` (3 sites). See § `BI30` below for the enumerating command and its complete output.
- **Feature 20's review finding N2** (the `.99` cents-rule fixture guard was prose, not code) — `CentsRuleFixtureGuard` (duplicated into both Billing test projects — see the file's own remarks for why a project reference was rejected) plus `CentsRuleFixtureGuardTests.cs`, wired into `BillingHostFixture.SeedCreditLineAsync`, `SeedLedgerEntryAsync`, `SeedInvoiceAsync` and the new `IssueRequest` builder.

## The invoice-state rendering, and a genuine deviation from `design.md §3.2`'s literal sketch

`design.md` sketched `InvoiceState` as an `abstract record` with a `private protected` base constructor and two nested `sealed record`s. **That sketch compiles, but cannot express the intended accessibility** (corrected wording — the reviewer built the sketch and confirmed it compiles; the earlier "does not compile" phrasing was wrong about the language, though right about the consequence), and the reason is a C# language rule design.md's author could not have known without building it: a non-sealed record's compiler-synthesised copy constructor (the one that backs `with` and record equality) is required by the language to be **at least `protected`**, so declaring it `private protected` — the accessibility the closure actually needs — is what triggers `CS8878`. `protected` is reachable from any derived type regardless of assembly, which is exactly the second, wider-than-intended constructor `BI23`'s closure claim exists to rule out.

**Resolution:** `InvoiceState`/`Issued`/`Paid` are rendered as plain classes, not records. A plain class has no compiler-synthesised copy constructor, so the explicit `private protected InvoiceState()` is the *only* constructor the type has — the property the ledger row `L4` names is fully closed, more tightly than the literal sketch would have achieved. `Paid` keeps a primary constructor (`sealed class Paid(DateTimeOffset paidAt) : InvoiceState`) so `PaidAt` still cannot be omitted. This is recorded in the file's own header (`src/Billing/Domain/InvoiceState.cs`) as a deviation argued in place, per `CLAUDE.md`'s instruction for a task whose wording turns out to be wrong — found only by writing `BI23`'s own guard and watching it fail against the literal sketch (see the arming table below, `B7`).

### `BI23`'s guard, and how it is armed (two mutations, both against the ORIGINAL record-based sketch before the class rewrite, and both re-confirmed against the final class-based rendering)

`InvoiceStateTests.cs` › `BI23_InvoiceStateIsAnAbstractClosedHierarchyWithExactlyTwoCases_AndNoConstructorAccessibleOutsideTheDomainAssembly` reflects over `typeof(InvoiceState)`: `IsAbstract`, exactly two nested subtypes, and every instance constructor `IsFamilyAndAssembly` (`private protected`).

- **Mutation 1 — widen the constructor** (`private protected` → `protected`): confirmed FAIL with message:
  `Assert.All() Failure: 1 out of 1 items in the collection did not pass. [0]: Item: Void .ctor() Error: constructor Void .ctor() must be private protected (IsFamilyAndAssembly).`
  Restored; forced rebuild (`touch` + `dotnet build --no-incremental`); re-run green (1/1).
- **Mutation 2 — add a third nested case** (`Voided`): confirmed FAIL with message:
  `Assert.Equal() Failure: Values differ Expected: 2 Actual: 3`
  Restored; forced rebuild; re-run green (9/9 `InvoiceStateTests`).

## The rename (`E4`, `BI31`, gate-approved 2026-09-06)

Fourteen files touched, exactly as `design.md §4.1`'s table enumerates: `src/Billing/Presentation/CreditRpcResponder.cs`→`BillingRpcResponder.cs`; `src/Billing/Presentation/Rpc/CreditErrorMapper.cs`→`BillingErrorMapper.cs`; `src/Billing/Infrastructure/Outbox/CreditFactPayloadMapper.cs`→`BillingFactPayloadMapper.cs`; `src/Billing/Infrastructure/BillingOptions.cs`, `BillingServiceCollectionExtensions.cs`, `src/Billing/Presentation/Rpc/CreditRequestValidator.cs` (doc comment), `src/Billing/InternalsVisibleTo.cs` (doc comment, not in the design's table but the same drift class `BI20` exists to prevent — fixed while here); `tests/Billing.UnitTests/{CreditResponderConcurrencyTests,CreditResponderHeaderTests,CreditResponderShutdownTests}.cs` (mechanical type-name updates only); `tests/Billing.UnitTests/CreditErrorMapperTests.cs`→`BillingErrorMapperTests.cs`; `tests/Billing.UnitTests/SqlExceptionFactory.cs` (doc comment); `tests/Billing.IntegrationTests/{BillingOutboxRelayTests,BuyerCreditRepositoryTests}.cs` (mechanical type-name updates).

**Condition proven — the three pre-existing responder test files are green afterwards with NO assertion changed.** Confirmed by full-suite runs after the rename:

- `dotnet test tests/Billing.UnitTests` → **199/199 passed**, including `CreditResponderConcurrencyTests` (2/2), `CreditResponderHeaderTests` (9/9), `CreditResponderShutdownTests` (1/1) — every original assertion text is untouched; only `CreditRpcResponder`/`CreditResponderOptions` tokens were replaced with `BillingRpcResponder`/`BillingResponderOptions`.
- `dotnet test tests/Billing.IntegrationTests` → **74/74 passed**, including the pre-existing `CreditHoldTests`, `CreditReleaseTests`, `CreditSimulatorTests`, `CreditWireTests`, `CreditListTests`, `BuyerCreditRepositoryTests`, `BillingOutboxRelayTests`, `IndexTests`, `SchemaColumnTypeTests`, `ForeignKeyTests`, `UniqueConstraintTests`, `NoMoneyColumnIsIntTests`, `MigrationTests`, `OutboxSeqIdentityTests`, `ReliabilityTableParityTests`, `RoundTripTests`.

The rename turned out to be entirely mechanical — no half-renamed state was ever reached.

## `BI30` — backlog id 55, the enumerating command and its complete output

Command: `grep -rn 'reply\.Items\|\.Items\[' --include=*.cs tests`

Complete output:
```
tests/Fulfillment.IntegrationTests/StockListTests.cs:85:        Assert.NotNull(reply.Items);
tests/Fulfillment.IntegrationTests/StockListTests.cs:86:        Assert.Single(reply.Items);
tests/Seed.IntegrationTests/SeedIntegrationTests.cs:307:            var actualItem = actual.Items[i];
tests/Billing.IntegrationTests/CreditListTests.cs:45:        Assert.Equal(4, reply.Items.Count);
tests/Billing.IntegrationTests/CreditListTests.cs:47:        foreach (var item in reply.Items)
tests/Fulfillment.UnitTests/StockReplenishServiceTests.cs:49:        Assert.Equal(2, reply.Items.Count);
```

Classification, one line per hit:
1. `StockListTests.cs:85` `Assert.NotNull(reply.Items)` — **preceded by / itself a discriminating-field assertion** — `Items` non-null is the check itself, already compliant before this feature (not one of the three filed Fulfillment sites, which were lines 33/38/44 as originally filed).
2. `StockListTests.cs:86` `Assert.Single(reply.Items)` — same case as above, preceded by line 85's `NotNull`. Compliant.
3. `SeedIntegrationTests.cs:307` `actual.Items[i]` — **not applicable**: `actual` here is a seeded order compared against a JSON fixture, not an RPC list reply with a `Page`/discriminating field. Confirmed by reading the surrounding code (lines 285–312): `Assert.Equal(expectedItems.Count, actual.Items.Count)` runs immediately before this line, which is itself the discriminating check for *this* object shape. Out of `BC32`/`BI30`'s scope.
4. `CreditListTests.cs:45` `Assert.Equal(4, reply.Items.Count)` — **fixed by this feature**: now preceded by `Assert.NotNull(reply.Page); Assert.Equal(4, reply.Page.Total);`.
5. `CreditListTests.cs:47` `foreach (var item in reply.Items)` — same fix, preceded by the same two lines.
6. `StockReplenishServiceTests.cs:49` `Assert.Equal(2, reply.Items.Count)` — **not applicable**: a UNIT test where `reply` comes directly from `StockReplenishService.ReplenishAsync(...)`, never deserialised from NATS bytes. There is no RPC wire path here, so the RpcError-into-all-defaults hazard `BC32`/`BI30` guards against does not exist on this path. `StockReplenishReplyPayload` also has no second field to assert first (design's own advisory `A7`, not reopened).

Two additional sites (`CreditListTests.cs:77` — the paging case) were fixed in the same pass even though the original enumerating command's line numbers (39/73/77, as filed) shifted after the file was edited; the paging case now reads `Assert.NotNull(page1.Page); Assert.Equal(4, page1.Page.Total); Assert.Single(page1.Items);`.

**`StockListTests.cs`'s three filed sites (lines 33, 38, 44 as filed)** were fixed: `byProduct`, `belowThreshold` and `page1` each now assert `Page`/`Page.Total` before touching `Items`. Confirmed green: `dotnet test tests/Fulfillment.IntegrationTests --filter FullyQualifiedName~StockListTests` → **2/2 passed**.

**Armed** (at `CreditListTests.BC6`, by making `HandleCreditListAsync` throw before dispatch, forcing an `RpcError` reply): confirmed FAIL on the **named** `Assert.NotNull(reply.Page)` — verbatim message `Assert.NotNull() Failure: Value is null` at `CreditListTests.cs:43` — rather than a null dereference or a silent pass. Restored; forced rebuild; re-run green.

## `A5` — the `BillingHostFixture.cs` enumeration

Every `public static` method in `tests/Billing.IntegrationTests/BillingHostFixture.cs` (13 methods), and whether it calls `CentsRuleFixtureGuard.AssertNotCentsRuleAmount`:

| Method | Money-valued parameter? | Calls the guard? | Why |
|---|---|---|---|
| `StartHostAsync` | no | — | host bootstrap only |
| `RequestBareAsync` | no | — | transport helper |
| `SeedCreditLineAsync` | `creditLimit` | **yes** | added this feature; deliberately broader than the hazard (design.md §10.1) |
| `SeedLedgerEntryAsync` | `amount` | **yes** | added this feature |
| `FindCreditAsync` | no | — | plain read |
| `LedgerOfAsync` | no | — | plain read |
| `CommittedExposureOfAsync` | no (returns a money value, takes none) | — | plain read |
| `OutboxRowsForAsync` (x2 overloads) | no | — | plain read |
| `SeedInvoiceAsync` | `amount`, `discount`, `totalAmount` | **yes** (on the computed `totalAmount`) | new this feature |
| `InvoicesOfAsync` | no | — | plain read |
| `InvoiceItemsOfAsync` | no | — | plain read |
| `IssueRequest` | line unit prices, `discount` | **yes** (on the computed total) | new this feature; the load-bearing computed case |
| `WaitForAsync<T>` | no (generic) | — | polling helper |

**A finding, not a gap:** `design.md §10.1`/task `A5` names *"the hold-request builder"* as one of the methods to guard. **No such method exists in `BillingHostFixture.cs`** — every existing Credit-hold integration test (`CreditHoldTests.cs`, `CreditHoldRaceTests.cs`, `CreditResponderConcurrencyTests.cs`, `CreditSimulatorTests.cs`, `CreditReleaseTests.cs`, `CreditWireTests.cs`) hand-builds its own `CreditHoldRequestPayload` inline; there is no shared builder to wire the guard into. Introducing one now would mean touching every one of those pre-existing files for reasons this feature does not otherwise need, which the task list's own scope preamble does not authorise. The guard's coverage is therefore exactly the three methods this feature actually touches or adds — `SeedCreditLineAsync`, `SeedLedgerEntryAsync`, `SeedInvoiceAsync` and `IssueRequest` — and the fixture-amount hazard for **hand-built `CreditHoldRequestPayload` literals** is covered by the text-scan half (`FindUnguardedLiterals`), not the runtime half, exactly as `design.md §10.1` describes for "payloads assembled by hand that never reach a builder."

**Armed:** a temporary fixture whose three lines total `24_999` (`3 × 8_333`), built via `IssueRequest`, confirmed to throw at fixture-build time — see `BI17` below (`CentsRuleFixtureGuardTests.RefusesAFixtureAmountThatWouldTriggerTheSimulatedCentsRuleUnlessTheFixtureOptsIn_IncludingAComputedTotalOfThreeTimes8333`, which drives the identical computation).

## `BI17` — the `.99` fixture guard, both halves

`CentsRuleFixtureGuard` (duplicated byte-for-byte into `tests/Billing.IntegrationTests/` and `tests/Billing.UnitTests/`, the `SqlExceptionFactory` precedent, rather than a `Billing.UnitTests` → `Billing.IntegrationTests` project reference that would pull Testcontainers/Confluent.Kafka/NATS.Net into a project whose entire point is to run with none of them — recorded as a deliberate deviation from the design's literal single-file placement, in the file's own header).

Three `CreditSimulatorTests.cs` occurrences of `24_999` needed the `// cents-rule-intentional` marker, **not two** as `design.md §10.1`'s prose states — the scan (regex `(?<![\w"'-])\d[\d_]*(?!\w)`, comment-stripped, excluding literals embedded in identifiers or string literals) found lines 35, 53 and 149: the two request constructions (`R42`'s and `R44`'s) plus one assertion (`Assert.Equal(24_999, factPayload.RequestedAmount)`) echoing the same fixture amount within `R42`'s own test. All three now carry the marker. This is recorded here as a small, disclosed correction to the design's prose (the property — "the two intentional literals are marked" — was never wrong; the literal count was).

**Non-vacuity, run for real:** `CentsRuleFixtureGuardTests.TheScanGenuinelyFiresAgainstAScratchFixtureRatherThanAssertingEmptinessVacuously` writes a scratch `.cs` file into `tests/Billing.IntegrationTests/` containing one un-opted-in `24_999` and asserts the scan reports **exactly one** hit at that line — passes.

**Armed (two mutations):**
1. Delete the `% 100 != 99` condition from `AssertNotCentsRuleAmount` → the computed-total case and the scan case both fail (confirmed via test run before restoring — the guard becomes a no-op).
2. Delete the marker from one `CreditSimulatorTests.cs` line → the scan case fails, reporting the now-unmarked line as a hit.

Both restored; forced rebuild; re-run green (3/3 `CentsRuleFixtureGuardTests`).

## The arming table

Both mutation families — **deletion** (does the guard fail when the row/behaviour is absent) and **corruption** (does it fail when a field is wrong) — per `CLAUDE.md`'s binding rule. Every row below was actually mutated, run, and restored; the message is copied verbatim from the failing run.

| Task | Mutation | Named test | Verbatim message | Restored & green |
|---|---|---|---|---|
| `B7`/`BI23` (1) | Widen `private protected` → `protected` | `InvoiceStateTests.BI23_...` | `constructor Void .ctor() must be private protected (IsFamilyAndAssembly).` | yes |
| `B7`/`BI23` (2) | Add a third nested case (`Voided`) | `InvoiceStateTests.BI23_...` | `Assert.Equal() Failure: Values differ Expected: 2 Actual: 3` | yes |
| `B8`/`BI25` | Remove `checked`/`catch` conversion in `Invoice.Issue` | `InvoiceTests.BI25_...` | `Assert.Throws() Failure: Exception type was not an exact match Expected: InvoiceTotalOverflowError Actual: NegativeInvoiceTotalError` (the wrapped sum tripped a DIFFERENT, still-wrong domain error — the guard is genuine either way) | yes |
| `B9`/`BI13` | Swap `AggregateId`/`CorrelationId` in the `Raise` call | `InvoiceFactTests.BI13_...` | `Assert.Equal() Failure: Values differ Expected: 1827d9bb-... Actual: c3068d94-...` | yes |
| `D3`/`BI8` (1) | Move `AllocateNextAsync` above the invoice lock | `InvoiceIssueServiceTests.BI8_...` | `Expected: [credits.Lock, invoices.Lock, allocator.AllocateNext] Actual: [credits.Lock, allocator.AllocateNext, invoices.Lock]` | yes |
| `D3`/`BI8` (2) | Invert the first two locks | `InvoiceIssueServiceTests.BI8_...` | `Actual: [invoices.Lock, credits.Lock, allocator.AllocateNext]` | yes |
| `D4`/`BI26` | Map `NoActiveHoldError` → `UNAVAILABLE` | `BillingErrorMapperTests.BI26_...` | `Assert.Equal() Failure: Strings differ Expected: PRECONDITION_FAILED Actual: UNAVAILABLE` | yes |
| `E2`/`BI16` | Change one char of `InvoiceIssue` subject | `InvoiceSubjectsTests.InvoiceSubjects_EqualTheAsyncApiChannelAddress` | `Expected: billing.invoice.ISSUE Actual: billing.invoice.issue` | yes |
| `E3`/`BI25` (1) | Remove the cross-field check | `InvoiceResponderValidationTests.BI2_...(DiscountExceedsSum)` | `Assert.ThrowsAny() Failure: No exception was thrown` | yes |
| `E3`/`BI25` (2) | Remove the `checked` region | `InvoiceResponderValidationTests.BI25_...` | `Assert.Contains() Failure: Sub-string not found Not found: "overflow"` (still refused, but for the wrong reason — the wrapped sum tripped the ordinary `>` check; genuine failure of the intended guarantee) | yes |
| `E4`/`BI31` | Register a second responder-shaped `BackgroundService` | `BillingResponderSubjectCoverageTests.BI31_...` | `HashSets differ ... Actual: [..., ArmProbeSecondResponder]` | yes |
| `E6`/`BI28` | Make `InvoiceViewPayload.PaidAt` non-nullable | `InvoiceRpcPayloadTests.BI28_AnIssuedInvoiceViewOmitsThePaidAtKeyEntirely...` | `Assert.DoesNotContain() Failure: Item found in set ... Found: "paidAt"` | yes |
| `E7`/`L20` | `IInvoiceRepository`: `AddScoped` → `AddSingleton` | `BillingDispatcherRegistrationTests.InvoicingPorts_EachResolve_AndAreEachRegisteredScoped` | `Assert.Equal() Failure: Values differ Expected: Scoped Actual: Singleton` | yes |
| `E8`/`BI1` | Register a trivial second `BackgroundService` | `BillingConsumesNoFactsTests.BI1_...` | `HashSets differ ... Actual: [..., ArmProbeTrivialHostedService]` | yes |
| `E9`/`BI25` | Map `InvoiceTotalOverflowError` → `INTERNAL_ERROR` | `BillingErrorMapperTests.BI25_MapsInvoiceTotalOverflowErrorToDomainError` | `Expected: DOMAIN_ERROR Actual: INTERNAL_ERROR` | yes |
| `F1`/`BI2` | Move header extraction after the dispatch call (using placeholder ids) | `InvoiceResponderValidationTests.BI2_...(MissingCorrelationId)`, `(MalformedRequestId)` | `the dispatcher must never be called when validation fails.` (both rows) | yes |
| `F7`/`R45`,`BI13` | Delete `Raise(new InvoiceIssued(...))` | `InvoiceTests.R45_...`, `InvoiceFactTests.BI13_...` | `Assert.Single() Failure: The collection was empty` (both) | yes |
| `F8`/`R46` | Delete `Raise(new PaymentReceived(...))` (double force — no live caller) | `InvoiceTests.R46_...` | `Assert.Single() Failure: The collection was empty` | yes |
| `F9`(a) | `BillingFactPayloadMapper`: `lines[0].UnitPrice + 1` | `InvoiceIssueTests.R45_...` (integration, real fact read back) | `Expected: 2000 Actual: 2001` | yes |
| `F9`(b) | `BillingFactPayloadMapper`: constant `RetailerCode` | `InvoiceIssueTests.R45_...` | `Expected: CarrefourEs Actual: ARM-CONSTANT` | yes |
| `F9`(c) | `BillingFactPayloadMapper`: constant `PaymentReference` | `BillingFactPayloadMapperTests.ToPayload_MapsPaymentReceived_...` (**new unit test — `PaymentReceived` has no live caller, so no integration path exercises this arm at all**) | `Expected: PMT-000001 Actual: ARM-CONSTANT` | yes |
| `F10`/`R40` inverted | Spurious outbox row on `consume`, carrying the credit line's own id and a different `correlationId` | `InvoiceRepositoryTests.BI7_...` (repository) AND `InvoiceIssueTests.R45_...` (responder) | Repository: `Expected: 1 Actual: 2`. Responder: `Expected: 1 Actual: 2` | yes (both) |
| `A2`/`BI30` | Force `HandleCreditListAsync` to throw (`RpcError` reply) | `CreditListTests.BC6_...` | `Assert.NotNull() Failure: Value is null` (fails on the named discriminating-field assertion, not a null dereference, not silently) | yes |
| `C5`/`BI7` | `credits.SaveChangesAsync` run as its own, separately-committed transaction before the throwing one (test-file mutation) | `InvoiceRepositoryTests.BI7_...` | `Assert.Empty() Failure: Collection was not empty Collection: [CreditItem { Amount = 2000, ... }]` | yes |
| `C6`/`BI24` | Drop `SpecifyKind` from the `paid_at` branch | `InvoiceRepositoryTests.BI24_...` | `Expected: 2026-06-20T14:00:00.0000000+00:00 Actual: 2026-06-20T14:00:00.0000000-04:00` | yes |
| `C7`/`BI15` | Read `DateTimeOffset.UtcNow` instead of the supplied `now` | `InvoiceReadRepositoryTests.IssuedBeforeMinutes_UsesTheSuppliedNowRatherThanAnAmbientClock_...` | `Expected: 1 Actual: 0` | yes |
| `C8`/`BI29` (1) | Check-then-act instead of the atomic seed | `InvoiceNumberAllocatorTests.BI29_...` (integration) | `Microsoft.Data.SqlClient.SqlException: Violation of PRIMARY KEY constraint 'PK_invoice_number_sequences'. Cannot insert duplicate key ... (1).` | yes |
| `C8`/`BI12` (2) | Remove the `MAX(...)` sub-select | `InvoiceNumberAllocatorTests.BI12_...` (integration) | `Expected: INV-000006 Actual: INV-000001` | yes |

Every mutation above followed the arming protocol exactly: backup copy taken before mutating, `touch`/`dotnet build --no-incremental` forced rebuild before the failing run **and** before the confirming green run, restore confirmed by re-reading the changed line.

### `F9`'s own gap, closed

`F9`(c) required a **unit** test (`tests/Billing.UnitTests/BillingFactPayloadMapperTests.cs`, new) because `PaymentReceived` has no live caller in this feature — no integration test exercises `BillingFactPayloadMapper`'s `PaymentReceived` arm at all. This file also covers `InvoiceIssued` field-by-field, and was the vehicle that caught mutation `F9`(a) failing to fail on the first attempt (see next paragraph).

### A defect this feature's own arming caught and fixed

`F9`(a)'s first attempt corrupted `lines[0].UnitPrice` and re-ran `InvoiceIssueTests.R45_...` — **it passed**, because the original test asserted `factPayload.Lines.Count` and the invoice's totals, but never the individual line fields inside the *fact payload* (it checked the DB row's lines, not the published payload's). This is exactly the feature-17 defect class `F9` exists to catch, caught by the arming step itself: the test was strengthened to assert `factPayload.Lines[0].ProductCode/.Units/.UnitPrice` field-by-field against the request, re-armed, and confirmed failing (`Expected: 2000 Actual: 2001`) before being restored.

## `F11` — walking the `design.md §16.2` ledger

Every row whose Guard column names a task is covered by the arming table above (`L4`→`B7`; `L7`→`B8`/`E3`/`E9`; `L13`→`C6`; `L18`→`C8`; `L19`→`E6`; `L20`→`C5`/`E7`; `L21`→`D4`; `L23`→`F9`; `L24`→`E4`; `L27`→`C8`; `L28`→`C7`; `L30`→`F1`; `L31`→`A4`; `L32`→`A2`). `L5` (`BI27`) is unit-tested (`InvoiceStateTests` §`BI27`) rather than separately armed as a mutation-table row — its assertion (raises on `"Paid"`/`"PAID"`/`"settled"`/`""`) is itself the guard and cannot silently pass on a coercion, since `InvoiceStatuses.Parse`'s only non-throwing branches are the two exact literal tokens.

Rows whose answer is *"nothing special is required"* — enforced by absence, evidenced by grep:

- **`L1`** (bare JSON, nothing new) — `BI16` asserts it anyway (`InvoiceWireTests`, 3/3 green).
- **`L6`** (no narrowing cast on the units path) — `grep -n "(int)" src/Billing/Infrastructure/Persistence/InvoiceRowMapper.cs src/Billing/Infrastructure/Persistence/EfCoreInvoiceRepository.cs src/Billing/Domain/InvoiceLine.cs src/Billing/Domain/Invoice.cs` → **zero lines** (exit 1).
- **`L11`** (fast path takes no hint) — `grep -n "UPDLOCK" src/Billing/Infrastructure/Persistence/EfCoreInvoiceRepository.cs` → two lines: line 15 (a doc comment describing `LockByOrderReferenceAsync`) and line 57 (the actual SQL, inside `LockByOrderReferenceAsync` itself). **One code-level occurrence, and it is inside the one method that is supposed to have it** — matches `C2`'s claim once doc-comment self-reference is excluded (see `C2` below for the same pattern and the same honest classification).
- **`L12`** (list query takes no hint, no transaction) — `grep -n "UPDLOCK\|HOLDLOCK\|ROWLOCK\|BeginTransaction" src/Billing/Infrastructure/Persistence/EfCoreInvoiceReadRepository.cs` → **zero lines** (exit 1).
- **`L16`** (never `AddRange`) — `grep -n "AddRange(" src/Billing/Infrastructure/Persistence/EfCoreInvoiceRepository.cs` → **zero lines** (exit 1).
- **`L17`** (never check-then-act on the invoice/line insert path) — `grep -n "IF NOT EXISTS\|MERGE\|ON DUPLICATE" src/Billing/Infrastructure/Persistence/EfCoreInvoiceRepository.cs` → **zero lines** (exit 1; a separate, broader `MERGE`-word grep hits only the doc comment at line 82, "no `MERGE`", which is prose describing the absence).
- **`L29`** (the `credits` row itself is never written) — `grep -n "db.Credits.Add\|db.Credits.Update\|db.Credits.Remove" src/Billing/Infrastructure/Persistence/EfCoreBuyerCreditRepository.cs` → **zero lines** (exit 1). File untouched by this feature.

**A ledger row whose guard did not fail is a defect in the row, not in the code** — every row above either failed on mutation (arming table) or is a genuine absence confirmed by a zero-hit grep. None declined to fail.

## `C2` — the two absence claims, evidence and honest classification

**Claim 1:** `grep -n 'UPDATE\|DELETE\|MERGE\|IF NOT EXISTS\|AddRange' src/Billing/Infrastructure/Persistence/EfCoreInvoiceRepository.cs`

Complete output:
```
16:/// never UPDATEs, never DELETEs, never MERGEs — every invoice and every line
81:    /// after everything above returned (`OI9`). No `UPDATE`, no `DELETE`,
82:    /// no `MERGE`.
105:    /// <summary>Copied verbatim (reasoning and shape) from <c>EfCoreBuyerCreditRepository.InsertOutboxRowAsync</c> (ledger `L16`) — never `AddRange`.</summary>
```
Classification: **all four hits are inside XML doc comments** — English prose stating the absence, never a SQL statement or a `.AddRange(` call. Zero hits in executable code. The identical pattern exists, unremarked, in the two files this class was modelled on (`EfCoreBuyerCreditRepository.cs` lines 81/112, `EfCoreDespatchRepository.cs` line 96) — confirmed by running the same grep against them. The task's literal "returns zero lines" is not quite true of the raw command against comments; it is true of the code, which is what the guard is actually for.

**Claim 2:** `grep -n 'UPDLOCK' src/Billing/Infrastructure/Persistence/EfCoreInvoiceRepository.cs`

Complete output:
```
15:/// (`BI8`), under `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`. <see cref="SaveAsync"/>
57:                   FROM   dbo.invoices WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
```
Classification: line 15 is a doc comment; line 57 is the actual SQL hint, and it is inside `LockByOrderReferenceAsync`, the one method the design names as needing it. **Exactly one occurrence in code**, matching the task's claim.

## `C3` — the allocator copy, identity evidence

Command: normalise `src/Fulfillment/Infrastructure/Persistence/EfCoreDespatchNumberAllocator.cs` with the named substitutions (`DES-`→`INV-`, `despatch_reference`→`invoice_reference`, `despatches`→`invoices`, `despatch_number_sequences`→`invoice_number_sequences`, `DespatchNumberSequences`→`InvoiceNumberSequences`, type/namespace) and diff against `src/Billing/Infrastructure/Persistence/EfCoreInvoiceNumberAllocator.cs`.

The diff shows only: the `// COPY OF —` banner (added, naming the source and the reason — feature 45's check-then-act defect), and the doc-comment/XML-summary prose (which differs because the two files' headers explain the same idiom in different words, referencing different sibling files). **Zero differences in executable code** — the SQL, the method body, the constants and the format string are byte-identical modulo the named substitutions.

## The 32-row ledger — properties and how each is supplied

Every row of `design.md §16.2` is supplied exactly as that document states; the implementation did not diverge from any row's "#8 supplies it by" column except `L4` (documented above — a stronger closure than the literal sketch, for a reason the design's author could not have anticipated). Rows with a named guard are covered in the arming table and the `F11` absence-grep list above; rows without one (`L2`, `L3`, `L9`, `L10`, `L14`, `L15`, `L22`, `L25`, `L26`) are covered by pre-existing inherited guards (`BI28`'s parity check for `L2`; the validator theory for `L3`; feature 19's `LockForOrderAsync` hints for `L9`; `C5`'s whole-table delta for `L10`; the ASCII/uppercase alphabets for `L14`/`L15`; the relay's unchanged key derivation for `L22`; `CreditResponderShutdownTests` for `L25`; `EfCoreUnitOfWork`'s explicit isolation level, unchanged, for `L26`).

## Live boot (`H2`–`H4`) — NOT performed this session, and why

The compose stack (`otcnet-mssql`, `otcnet-nats`, `otcnet-kafka`, etc.) is running throughout this session, but **the Orders, Fulfillment and Billing service *processes* were not started** against it — doing so, placing a control order, and observing the parked `invoice.issue` rows unpark over one sweeper interval is a multi-service, wall-clock-bound exercise this session did not have the remaining time budget to run safely (starting three long-running processes, waiting for a sweeper interval, and querying live tables without disturbing them, then shutting them down cleanly). All of the code and every test the live boot would exercise (`InvoiceIssueService`, the responder's five subjects, the allocator, `R40`'s neutrality) are in place and green under Testcontainers-based integration tests, which cover the identical code paths against real MS-SQL/NATS/Kafka.

**This is reported honestly as not done, not ticked, and not claimed.** `BI22`'s row in `requirements.md` is left `TODO` with a note explaining why. `H1`–`H4` in `tasks.md` are left unticked. A future session (or the reviewer, if it prefers to verify this directly) can run:

```bash
docker compose up -d
dotnet run --project src/Orders &
dotnet run --project src/Fulfillment &
dotnet run --project src/Billing &
# wait one sweeper interval, then query otc_orders.saga_commands / otc_orders.orders /
# otc_billing.invoices / otc_billing.invoice_number_sequences / otc_billing.credit_items
```

## Packages added: none

Confirmed by reading `src/Billing/Billing.csproj` and both Billing test `.csproj` files before and after this feature — no new `PackageReference` anywhere. `.env.example` untouched by this feature (its earlier diff, visible in `git status`, predates this session and belongs to feature 20).

## Test counts (read off real runs)

- `dotnet test tests/Billing.UnitTests` → **199 passed, 0 failed, 0 skipped, 199 total**.
- `dotnet test tests/Billing.IntegrationTests` (real Testcontainers MS-SQL/NATS/Kafka) → **74 passed, 0 failed, 0 skipped, 74 total**.
- `dotnet test tests/Orders.UnitTests` → **280 passed, 0 failed, 0 skipped, 280 total** (includes the new `BI21` case).
- `dotnet test tests/Fulfillment.IntegrationTests --filter FullyQualifiedName~StockListTests` → **2 passed** (the `BI30` sites).
- `dotnet test tests/Architecture.Tests` → **16 passed** (domain purity holds for every new `Domain/` file).

## `./init.sh` and `./quality.sh`

- `./init.sh` — exit 0, confirmed before starting and re-confirmed after the format fix (see below).
- `./quality.sh` — first run failed at the format-check step (`dotnet format --verify-no-changes`) on two genuine issues introduced by this feature: an import-ordering violation in `BillingFactPayloadMapper.cs` and a missing `_` prefix on a private static field in `InvoiceTests.cs` (`Ctx` → `_ctx`). Both fixed; `dotnet format OrderToCash.sln --verify-no-changes` now exits 0.

  **Second, full run — exit 0.** `format check: clean` → `build: succeeded` → `dotnet test` over the **whole solution** (every service's unit AND integration suite, real Testcontainers throughout, no service skipped): **all passed**, no failures anywhere in the trilogy's existing test base, including `Fulfillment.IntegrationTests` (56/56), `Orders.IntegrationTests` (71/71), `Notifications.IntegrationTests` (7/7), `Billing.IntegrationTests` (74/74), `Billing.UnitTests` (199/199), `Orders.UnitTests` (280/280), `Fulfillment.UnitTests` (119/119), `SharedKernel.UnitTests` (50/50), `Contracts.UnitTests` (21/21), `Seed.UnitTests` (34/34), `Architecture.Tests` (16/16), plus `Seed.IntegrationTests` and `Gateway`/`Projector` where present — thirteen coverage reports emitted, one per test project, `quality.sh finished` printed at the end.

  **Billing-specific coverage** (merged from the `Billing.UnitTests`, `Billing.IntegrationTests` and the cross-service `ReliabilityTableParityTests` cobertura reports — a line counts as covered if ANY report hit it, the correct way to read three partial views of the same assembly): **`src/Billing/Domain/` 550/621 lines = 88.6%** (≥ 80% gate met), **`src/Billing/` overall 3452/3677 lines = 93.9%** (≥ 60% gate met). Read directly from the cobertura XML the full-solution `quality.sh` run produced, not estimated.

## Deviations from the spec, argued

1. **`InvoiceState`/`Issued`/`Paid` rendered as classes, not records** (`design.md §3.2`'s literal sketch) — argued above under "The invoice-state rendering." The property (closure) is unchanged and, in fact, strengthened; only the C# construct changes, because the literal sketch compiles but cannot express the intended `private protected` accessibility (`CS8878` on the synthesised copy constructor).
2. **`BI17`'s "two intentional `24_999`s" is three** — argued above under `BI17`. A wording correction, not a behavioural one; all three are now marked and the scan reports zero.
3. **`A5`'s "hold-request builder"** does not exist in `BillingHostFixture.cs`; the guard covers the methods this feature actually touches, and the text-scan half covers every hand-built `CreditHoldRequestPayload` literal instead. Argued above under `A5`.
4. **`H2`–`H4` (live boot) not performed** — argued above, with a runnable recipe for whoever performs it next.

## What I could not do, and why

Only the live-boot exercise (`H2`–`H4`), for the reasons stated above. Everything else in `tasks.md` groups A–G and the H5/H6/H7/H8/H9 documentation tasks is complete and ticked.

---

## H1–H4 — the live-stack walkthrough, performed by the COORDINATOR (2026-09-06)

The implementer left `H1`–`H4` unticked and disclosed it honestly, arguing time budget. **The human's standing instruction of 2026-09-05 is *stop leaving issues to the next phase — fix them*, and a live-boot task is exactly the kind that is deferred indefinitely**, so the coordinator ran it rather than carrying it forward. This section is the coordinator's own observation, not the implementer's, and the reviewer should verify it as such.

### Starting state, queried before any service process was started

```
ORD-000007 | invoice.issue | parked | attempts=12 | billing.invoice.issue: transport failure: no responder
ORD-000008 | invoice.issue | parked | attempts=12 | (same)
ORD-000009 | invoice.issue | parked | attempts=12 | (same)
ORD-000010 | invoice.issue | parked | attempts=12 | (same)
ORD-000011 | invoice.issue | parked | attempts=12 | (same)
orders: 5 despatched, 5 completed, 2 cancelled · invoices: 5 (all seeded, all paid)
```

### H2 — the parked commands resolved unattended

Orders, Fulfillment and Billing started at **07:12:25Z**. No operator action of any kind after that.

| Elapsed | `invoice.issue` parked | sent | invoices | orders at `invoiced` |
|---|---|---|---|---|
| t+10 s | 5 | 0 | 5 | 0 |
| t+20 s | 5 | 0 | 5 | 0 |
| **t+30 s** | **0** | **4** | **9** | **4** |

Four invoices issued — `INV-000006` … `INV-000009` for `ORD-000007` … `ORD-000010` — and those four orders moved to `invoiced`. **Zero parked rows remained.**

### The fifth command resolved `rejected`, and that is feature 42's mechanism observed live for the first time

`ORD-000011`'s command did **not** go to `sent`. It went to a fourth status:

```
ORD-000011 | invoice.issue | rejected | attempts=13 |
  billing.invoice.issue: terminal business rejection (PRECONDITION_FAILED)
```

The Billing log gives the domain reason: `Order 'ORD-000011' holds no active hold to consume` — `NoActiveHoldError`. The credit ledger confirms it is correct rather than a defect:

```
ORD-000010 | hold 49998 | consume 49998      → invoice issued
ORD-000011 | hold  1000 | release  1000      → nothing left to consume
```

That order's credit hold had already been **released**, so an invoice against it is a genuine precondition failure. **Feature 42 classified it terminal and stopped, instead of retrying at capped backoff indefinitely** — which is precisely the live failure #7 reproduced (a command against an already-consumed reservation retrying 81+ times, permanently unresolvable) and the reason feature 42 exists. It had never been observed end to end until now.

### H3 — the negative half, which is the designed end state and not a stall

```
orders at 'paid'                        : 0
invoices at 'paid' beyond the 5 seeded  : 0
otc.billing.facts.v1 eventTypes         : 4 × invoice.issued.v1
                                          (no payment.received.v1)
```

Correct: remittance intake is **feature 22** and does not exist yet. The five rows in `payments` are phase 7's seed, not new.

### H4 — a fresh control order through the whole chain

`ORD-000013`, `AldiEs` / `IBERFOODS`, `PRD-0002` × 3 = **5 547** minor units — deliberately not `≡ 99 (mod 100)`, and well within credit.

Reached **`invoiced` within 8 seconds** of placement, traversing `placed → stock_reserved → credit_approved → confirmed → despatched → invoiced` across three services and five RPC hops with no operator action. `INV-000010` issued against it at total `5547`.

### Shutdown

All three processes stopped cleanly. **Zero occurrences of `unhandled`, `fatal` or `crash` in any of the three logs.**

### Verdict on the tasks

`H1`–`H4` are **performed and ticked**, by the coordinator rather than the implementer, with the evidence above. `BI22`'s traceability row is flipped from `TODO` on this evidence. The implementer's disclosure was correct and its reasoning was sound; what it lacked was the time, not the argument — and the standing instruction is that a task nobody has time for today is a task nobody will have time for in phase 11 either.

---

## FIX ROUND (round 2) — `progress/review_billing_invoicing.md`'s three blocking defects, id 55's evidence, and two corrections

The review REJECTED with three blocking defects (D1, D2, D3), six non-blocking findings and one advisory. This round closes D1, D2, D3, replaces backlog id 55's enumerating evidence (N4), corrects the `CS8878` sentence (A1), and adds the hand-over note for the deliberately corrupted `ORD-000011` fixture (N6). Per the leader's scoping brief, N1/N2/N3/N5 and the rest of A1 are **not** touched this round.

### D1 — `payment.received.v1`'s payload now asserted field-by-field, driven from test-supplied values

**File touched:** `tests/Billing.UnitTests/InvoiceTests.cs` (test-side only — `src/Billing/Domain/Invoice.cs` needed no code change; the raise site was already correct, only unobserved).

`R46_AllowsOnlyTheTransitionFromIssuedToPaid_…` previously asserted only the fact's count, type and `PaymentReference`. It now supplies a `valueDate` and a `source` that are **deliberately distinct** from `paidCtx.OccurredAt`/`paidInstant`, so a corrupted raise-site value cannot hide behind a value the fact would carry anyway, and asserts every field `BI14` names: `PaymentReference`, `Amount.MinorUnits`, `Amount.Currency`, `ValueDate`, `Source`, plus `OrderReference` and `InvoiceReference` (the two identifiers the raise site must carry from the aggregate rather than the input).

**Arming, exactly as the protocol requires:**

1. Backup taken: `Invoice.cs` copied to a scratch path before mutating.
2. Mutation (the corruption named in the review): `ValueDate: input.ValueDate` → `input.ValueDate.AddDays(1)`, `Source: input.Source` → `"ARM-CONSTANT-SOURCE"`.
3. `touch` + confirmed the file was mutated by re-reading the changed lines.
4. `dotnet build tests/Billing.UnitTests/Billing.UnitTests.csproj --no-incremental` — succeeded (mutation is a value change, not a type change, so it still compiles).
5. `dotnet test … --filter FullyQualifiedName~InvoiceTests.R46` → **FAILED**, verbatim:
   ```
   Assert.Equal() Failure: Values differ
   Expected: 2026-09-08T00:00:00.0000000+00:00
   Actual:   2026-09-09T00:00:00.0000000+00:00
   ```
   (the `Source` corruption would have failed the very next assertion had `ValueDate`'s not failed first — both fields are now covered, xUnit just stops at the first failing `Assert`).
6. Restored from the backup, `cmp` confirmed byte-identical, and the changed lines re-read to confirm.
7. `touch` + `dotnet build --no-incremental` forced rebuild.
8. `dotnet test tests/Billing.UnitTests/Billing.UnitTests.csproj` → **199/199 passed**.

**What input the test now supplies that makes the corruption visible:** a `valueDate` and `source` that are test-chosen constants distinct from every other timestamp/string already in scope (`bank-file-import`, `2026-09-08`), so `Assert.Equal(valueDate, fact.ValueDate)` and `Assert.Equal(source, fact.Source)` can only pass if the raise site actually forwards the input unchanged — there is no other value in the test that a corrupted raise could coincidentally match.

### D2 — the request's `discount` can no longer be silently dropped by the responder

**File touched:** `tests/Billing.IntegrationTests/InvoiceIssueTests.cs` (test-side only — `src/Billing/Presentation/BillingRpcResponder.cs` needed no code change; the wiring was already correct, only unobserved because every existing test sent `discount = 0`, which `BillingHostFixture.IssueRequest` maps to `null`, omitting the key from the wire entirely).

`R45_…_TheIntegrationHalf` now issues an invoice with `grossAmount = 9_000`, `discount = 1_000` (**non-zero**) and `totalAmount = 8_000` — three distinct numbers — and seeds the credit hold at the discounted total (`8_000`) so the consume ledger entry is also request-derived rather than an incidental constant equal to the gross amount. Every assertion that previously compared to a literal `9_000`/`0` now compares to `grossAmount`/`discount`/`totalAmount`: `payload.TotalAmount`, `invoiceRow.Amount`, `invoiceRow.Discount`, `invoiceRow.TotalAmount`, `consumeEntry.Amount`, `factPayload.Amount`, `factPayload.Discount`, `factPayload.TotalAmount`.

**Arming, exactly as the protocol requires:**

1. Backup taken: `BillingRpcResponder.cs` copied to a scratch path before mutating.
2. Mutation (the corruption named in the review): `request.Discount,` → `0L,` at the `IssueInvoiceCommand` construction site.
3. `touch` + confirmed the file was mutated by re-reading the changed line.
4. `dotnet build tests/Billing.IntegrationTests/Billing.IntegrationTests.csproj --no-incremental` — succeeded.
5. `dotnet test … --filter FullyQualifiedName~InvoiceIssueTests.R45` → **FAILED**, verbatim:
   ```
   Assert.Equal() Failure: Values differ
   Expected: 8000
   Actual:   9000
   ```
   (fails at `invoiceRow.TotalAmount`, the first of the now-distinct-valued assertions the corruption reaches).
6. Restored from the backup, `cmp` confirmed byte-identical, and the changed line re-read to confirm.
7. `touch` + `dotnet build --no-incremental` forced rebuild.
8. `dotnet test tests/Billing.UnitTests/Billing.UnitTests.csproj` → **199/199 passed**; `dotnet test tests/Billing.IntegrationTests/Billing.IntegrationTests.csproj` → **74/74 passed** (real Testcontainers MS-SQL/NATS/Kafka), Duration 4 m 25 s.

**What input the test now supplies that makes the corruption visible:** a request whose `discount` (`1_000`) is neither `0` nor equal to `amount` or `totalAmount` — so a dropped discount changes `totalAmount` from `8_000` to `9_000`, `invoiceRow.Discount` from `1_000` to `0`, and `factPayload.Discount`/`factPayload.TotalAmount` likewise, none of which a zero-discount request could ever have exposed. This closes the receiving half of the property `BI21` (Orders, feature 20) already guards on the sending side — Orders sending a non-zero discount, and Billing now provably reading it rather than silently defaulting it away.

### D3 — `specs/shared/test-matrix.md`'s coverage summary recomputed from the rows

**File touched:** `specs/shared/test-matrix.md` (Coverage summary table only).

Recomputed the whole summary from the Status column, one row at a time, per the table's own preamble ("Counted from the Status column as it actually stands, one row at a time"), using this command run from the repository root:

```python
python3 - <<'EOF'
import re
with open("specs/shared/test-matrix.md") as f:
    lines = f.readlines()
groups = [
    ("1", range(1, 11)), ("2", range(11, 19)), ("3", range(19, 30)),
    ("4", list(range(30, 37)) + [61]), ("5", range(37, 45)), ("6", range(45, 50)),
    ("7", range(50, 56)), ("8", list(range(56, 61)) + [62]), ("8.1", [63]),
]
rows = {}
for line in lines:
    m = re.match(r"\|\s*\*\*R(\d+)\*\*\s*\|", line)
    if m: rows[int(m.group(1))] = line
def classify(line):
    parts = line.split("|")
    status_field = parts[5] if len(parts) > 5 else ""
    if "TODO" in status_field: return "todo"
    if "DONE" in status_field:
        return "scoped" if re.search(r"outstanding|ratified|scoped", status_field, re.I) else "green"
    return "todo"
grand = [0, 0, 0, 0]
for name, rng in groups:
    g = s = t = 0
    for n in rng:
        c = classify(rows[n])
        g += c == "green"; s += c == "scoped"; t += c == "todo"
    total = len(list(rng))
    grand[0]+=total; grand[1]+=g; grand[2]+=s; grand[3]+=t
    print(f"group {name}: total {total} green {g} scoped {s} todo {t}")
print(f"TOTAL: total {grand[0]} green {grand[1]} scoped {grand[2]} todo {grand[3]}")
EOF
```

Output:

```
group 1: total 10 green 9 scoped 1 todo 0
group 2: total 8 green 7 scoped 0 todo 1
group 3: total 11 green 9 scoped 2 todo 0
group 4: total 8 green 7 scoped 1 todo 0
group 5: total 8 green 8 scoped 0 todo 0
group 6: total 5 green 2 scoped 0 todo 3
group 7: total 6 green 0 scoped 0 todo 6
group 8: total 6 green 0 scoped 0 todo 6
group 8.1: total 1 green 0 scoped 0 todo 1
TOTAL: total 63 green 42 scoped 4 todo 17
```

Manually verified group 5 (`billing_credit`, R37–R44): all eight rows read plainly `DONE` with no "outstanding"/scoped caveat in the Status cell — `R42`, `R43`, `R44` were flipped to `DONE` by feature 20 in this same working tree without their group's row or the Total row being updated, which is exactly the drift D3 named. The table now reads group 5 `8 | 8 | 0 | 0` and Total `63 | 42 | 4 | 17`, matching this recomputation exactly.

### Backlog id 55 — the enumerating command replaced, complete output, one classification line per hit

**Not closed this round** (per the leader's instruction — the entry stays `pending`; only its evidence is fixed). The prior evidence (`grep -rn 'reply\.Items\|\.Items\['`) did not enumerate the candidate set (finding N4). Replaced with:

```
$ grep -rn '\.Items\b' --include=*.cs tests
```

Complete output (34 lines):

```
tests/Seed.IntegrationTests/SeedIntegrationTests.cs:185:        Assert.NotEmpty(completed.Items);
tests/Seed.IntegrationTests/SeedIntegrationTests.cs:303:        Assert.Equal(expectedItems.Count, actual.Items.Count);
tests/Seed.IntegrationTests/SeedIntegrationTests.cs:307:            var actualItem = actual.Items[i];
tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:52:        Assert.NotNull(payload.Items);
tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:53:        var item = Assert.Single(payload.Items);
tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:71:    /// <c>payload.Items</c> (<c>Assert.Single(payload.Items)</c>) with no
tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:96:        Assert.NotNull(payload.Items);
tests/Fulfillment.IntegrationTests/StockReplenishTests.cs:97:        var item = Assert.Single(payload.Items);
tests/Fulfillment.IntegrationTests/StockListTests.cs:27:        Assert.NotNull(byCompany.Items);
tests/Fulfillment.IntegrationTests/StockListTests.cs:28:        Assert.Equal(2, byCompany.Items.Count);
tests/Fulfillment.IntegrationTests/StockListTests.cs:29:        Assert.All(byCompany.Items, item => Assert.Equal("ACME", item.CompanyCode));
tests/Fulfillment.IntegrationTests/StockListTests.cs:39:        Assert.Equal(2, byProduct.Items.Count);
tests/Fulfillment.IntegrationTests/StockListTests.cs:40:        Assert.All(byProduct.Items, item => Assert.Equal("P1", item.ProductCode));
tests/Fulfillment.IntegrationTests/StockListTests.cs:46:        var single = Assert.Single(belowThreshold.Items);
tests/Fulfillment.IntegrationTests/StockListTests.cs:54:        Assert.Single(page1.Items);
tests/Fulfillment.IntegrationTests/StockListTests.cs:64:    /// `K2` (backlog id 53) — <c>byCompany.Items.Count</c> at line 27 above
tests/Fulfillment.IntegrationTests/StockListTests.cs:85:        Assert.NotNull(reply.Items);
tests/Fulfillment.IntegrationTests/StockListTests.cs:86:        Assert.Single(reply.Items);
tests/Billing.IntegrationTests/InvoiceReadRepositoryTests.cs:36:        Assert.DoesNotContain(olderThanOneDay.Items, i => i.CompanyCode == "CompanyRecent");
tests/Billing.IntegrationTests/InvoiceReadRepositoryTests.cs:39:        var paidView = Assert.Single(byStatus.Items);
tests/Billing.IntegrationTests/InvoiceReadRepositoryTests.cs:44:        var issuedView = (await repo.ListAsync(new InvoiceListRequestPayload(1, 25, Status: "issued", RetailerCode: "RetailerRead"), fixedNow, CancellationToken.None)).Items;
tests/Billing.IntegrationTests/InvoiceReadRepositoryTests.cs:52:        Assert.Single(page1.Items);
tests/Billing.IntegrationTests/InvoiceListTests.cs:31:        Assert.Equal(3, byRetailer.Items.Count);
tests/Billing.IntegrationTests/InvoiceListTests.cs:37:        var paidView = Assert.Single(paidOnly.Items);
tests/Billing.IntegrationTests/InvoiceListTests.cs:44:        Assert.All(issuedOnly.Items, i => Assert.Null(i.PaidAt));
tests/Billing.IntegrationTests/InvoiceListTests.cs:50:        Assert.Equal("CompanyIssuedRecent", Assert.Single(byOrder.Items).CompanyCode);
tests/Billing.IntegrationTests/InvoiceListTests.cs:56:        Assert.All(olderThan.Items, i => Assert.NotEqual("CompanyIssuedRecent", i.CompanyCode));
tests/Billing.IntegrationTests/InvoiceListTests.cs:62:        Assert.Single(page1.Items);
tests/Billing.IntegrationTests/CreditListTests.cs:42:        // `.Items.Count` assertion would pass silently on a refusal.
tests/Billing.IntegrationTests/CreditListTests.cs:45:        Assert.Equal(4, reply.Items.Count);
tests/Billing.IntegrationTests/CreditListTests.cs:47:        foreach (var item in reply.Items)
tests/Billing.IntegrationTests/CreditListTests.cs:81:        var single = Assert.Single(filteredByCompany.Items);
tests/Billing.IntegrationTests/CreditListTests.cs:87:        Assert.Single(page1.Items);
tests/Fulfillment.UnitTests/StockReplenishServiceTests.cs:49:        Assert.Equal(2, reply.Items.Count);
```

Classification, one line per hit — the candidate set the backlog entry's claim is actually about is *"every integration test that deserialises an RPC reply"*:

| # | Site | Classification |
|---:|---|---|
| 1–3 | `SeedIntegrationTests.cs:185,303,307` | **Not applicable** — MongoDB read-model document compared field-by-field against a JSON fixture; never an RPC reply, never `RpcJson.Deserialize`. Line 303's `Assert.Equal(expectedItems.Count, actual.Items.Count)` is itself the discriminating check for this shape; 307 is preceded by it; 185 is a plain field assertion on an already-validated document. |
| 4–5 | `StockReplenishTests.cs:52-53` (happy path) | **Compliant** — `Assert.NotNull(payload.Items)` at 52 IS the discriminating field (`StockReplenishReplyPayload` has no second field — backlog id 55's own advisory A7), preceding `Assert.Single` at 53. |
| 6 | `StockReplenishTests.cs:71` | Doc comment (prose), not code. |
| 7–8 | `StockReplenishTests.cs:96-97` (`BC32` case) | **Compliant** — same pattern as 52-53. |
| 9–11 | `StockListTests.cs:27-29` (`byCompany`) | **Compliant for BC32's own claim** (opaque-failure avoidance) — `Assert.NotNull(byCompany.Items)` at 27 fails cleanly rather than NRE-crashing on an `RpcError` body, which is the property backlog id 53 established. Not one of the six sites backlog id 55 was filed against, so no further obligation here. |
| 12–13 | `StockListTests.cs:39-40` (`byProduct`) | **Compliant** — one of the six originally-filed sites; preceded by `Assert.NotNull(byProduct.Page); Assert.Equal(2, byProduct.Page.Total);` at 37-38. |
| 14 | `StockListTests.cs:46` (`belowThreshold`) | **Compliant** — filed site; preceded by `Page`/`Page.Total` at 44-45. |
| 15 | `StockListTests.cs:54` (`page1`) | **Compliant** — filed site; preceded by `Page`/`Page.Total` at 52-53. |
| 16 | `StockListTests.cs:64` | Doc comment, not code. |
| 17–18 | `StockListTests.cs:85-86` (`BC32` case) | **Compliant** — `Assert.NotNull(reply.Items)` IS the discriminating assertion for this test's own purpose (proving the guard fires on a real `RpcError`). |
| 19–22 | `InvoiceReadRepositoryTests.cs:36,39,44,52` | **Not applicable** — calls `EfCoreInvoiceReadRepository.ListAsync` directly, never over NATS; there is no `RpcError` body this hazard could ever produce on this path. |
| 23–28 | `InvoiceListTests.cs:31,37,44,50,56,62` | **Compliant** — every one preceded on the same variable by `Assert.NotNull(x.Page); Assert.Equal(n, x.Page.Total);` (lines 29-30, 35-36, 42-43, 48-49, 54-55, 60-61 respectively). |
| 29 | `CreditListTests.cs:42` | Comment, not code. |
| 30–31 | `CreditListTests.cs:45,47` (`reply`) | **Compliant** — filed site (originally line 39); preceded by `Assert.NotNull(reply.Page); Assert.Equal(4, reply.Page.Total);` at 43-44. |
| 32 | `CreditListTests.cs:81` (`filteredByCompany`) | **Compliant** — filed site (originally line 73); preceded by `Page`/`Page.Total` at 79-80. |
| 33 | `CreditListTests.cs:87` (`page1`) | **Compliant** — filed site (originally line 77); preceded by `Page`/`Page.Total` at 85-86. |
| 34 | `StockReplenishServiceTests.cs:49` | **Not applicable** — unit test, `reply` returned directly from `StockReplenishService.ReplenishAsync`, no RPC wire path exists on this call at all. |

**Result: of the 34 raw hits, 3 are doc comments, 3+4=7 are not applicable (not an RPC-reply path), and the remaining 24 are all compliant** — either preceded by the reply's own `Page`/`Page.Total` assertion, or (for the two `StockReplenish*`/`StockList` reply types that carry no second field) the `Assert.NotNull(Items)` itself is the discriminating check. **Zero unguarded RPC-reply candidate sites found.** This is the same conclusion the review reached with its own wider enumeration; the fix here is that this evidence — the full command and its complete, classified output — is now the record, replacing the narrower one that only matched literal `reply.Items`/`.Items[` tokens. Backlog id 55 itself remains `pending`: its third acceptance line ("an enumerating command and its complete output are recorded") is now satisfied, but closing the entry is the reviewer's/leader's call, not made here.

### `CS8878` sentence — corrected

Per the review's correction: the `design.md §3.2` sketch **compiles**. What fails is expressing the *intended accessibility* — a non-sealed record's synthesised copy constructor reflects as `protected` (`IsFamily`), not `private protected` (`IsFamilyAndAssembly`), and *declaring* it `private protected` is what triggers `CS8878`. Both occurrences of the earlier "does not compile" phrasing in this file (the "invoice-state rendering" section and the Deviations list) have been reworded above to say the sketch compiles but cannot express the intended `private protected` accessibility. The deviation and the strengthened closure were correct throughout; only the sentence was wrong.

### N6 — the deliberately corrupted fixture, named in the hand-over

`specs/billing_invoicing/design.md` §18 now names `ORD-000011` explicitly: its credit hold was released by hand over raw NATS during feature 19's live-boot task `I4` (`progress/impl_billing_credit.md:128`), not by any saga path, and this feature's own live boot correctly observed the resulting mismatch and refused to invoice against it (`PRECONDITION_FAILED` / `NoActiveHoldError`, feature 42's terminal-rejection path). Phase 11 is told to read this as the first live observation of that mechanism, not a regression to chase.

### Verification run for this round

- `dotnet test tests/Billing.UnitTests` → **199 passed, 0 failed, 199 total**.
- `dotnet test tests/Billing.IntegrationTests` (real Testcontainers MS-SQL/NATS/Kafka) → **74 passed, 0 failed, 74 total**, Duration 4 m 25 s (standalone run) / 5 m 1 s (inside the full `quality.sh` run below).
- `git diff --stat src/Billing/Domain/Invoice.cs src/Billing/Presentation/BillingRpcResponder.cs` → **empty** — both source files are restored byte-identical to their pre-arming state; D1 and D2 are entirely test-side fixes, exactly as the leader anticipated.
- **`./quality.sh`** → **exit 0**. Format check clean. Build succeeded, 0 warnings, 0 errors. Every project passed: `SharedKernel.UnitTests` 50/50, `Cqrs.UnitTests` 23/23, `Contracts.UnitTests` 21/21, `Fulfillment.UnitTests` 119/119, `Billing.UnitTests` 199/199, `Orders.UnitTests` 280/280, `Notifications.IntegrationTests` 7/7, `Seed.UnitTests` 34/34, `Architecture.Tests` 16/16, `Seed.IntegrationTests` 6/6, `Fulfillment.IntegrationTests` 56/56, `Billing.IntegrationTests` 74/74, `Orders.IntegrationTests` 71/71. Thirteen coverage reports emitted; `quality.sh finished` printed at the end.
- **`./init.sh`** → **exit 0**, run both before this round's edits (with `billing_invoicing` `in_progress`) and again after flipping `feature_list.json` id 21 to `in_review` (with no feature `in_progress`) — both runs green, backlog tripwire clean, session file in lockstep, 30/55 done.

### Files touched this round

- `tests/Billing.UnitTests/InvoiceTests.cs` — D1.
- `tests/Billing.IntegrationTests/InvoiceIssueTests.cs` — D2.
- `specs/shared/test-matrix.md` — D3 (Coverage summary table only).
- `specs/billing_invoicing/design.md` — N6 (§18 hand-over note only).
- `progress/impl_billing_invoicing.md` — this section, plus the two `CS8878` wording corrections above.
- `src/Billing/Domain/Invoice.cs`, `src/Billing/Presentation/BillingRpcResponder.cs` — touched only transiently for arming; both restored to their original state, confirmed by empty `git diff`.
- `feature_list.json` id 21 → `in_review` (single-line edit, no other backlog entry touched; id 55 left `pending` as instructed).
