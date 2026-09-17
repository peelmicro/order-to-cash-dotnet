# impl: api_tests (feature 31, phase 18)

**Status: implementation complete, PASS.** `feature_list.json` id 31 set to
`in_review`. Full `tests/Gateway.IntegrationTests` project: **77/77**
(70 pre-existing + 7 new), reconciled exactly, run three times, 0 failures
in every run after the collection fix (see §3).

New file: `tests/Gateway.IntegrationTests/BlackBoxApiTests.cs` (665 lines).
Touched: `tests/Gateway.IntegrationTests/SagaEndToEndVerificationTests.cs`
(one line added then removed again — net diff against `HEAD` is zero for
that reason; see §3), `specs/shared/test-matrix.md` (R24/R49 Status cells
only, verified cell-by-cell), `feature_list.json` (id 31's status line
only, verified via diff).

---

## 1. Answers to the brief's questions

**Does id 31 reuse `SagaFleet` or build its own?** Reuses the type
directly — `SagaFleet` (defined in `SagaEndToEndVerificationTests.cs`) is
`internal`, so it is visible to every class in this assembly without
extraction. `BlackBoxApiTests` calls `SagaFleet.BuildAsync(...)` itself,
producing its OWN, separate, statically-held fleet instance (own 3 fresh
MS-SQL databases, own 5 real hosts, own ports) — never the same live
instance `SagaEndToEndVerificationTests` holds, so no order either file
places can collide with the other's criteria.

**Is `GatewayTestHost` a real HTTP surface, or `TestServer`?** Real. It
boots the real `GatewayHost` graph over real Kestrel on an OS-assigned
ephemeral port (`http://127.0.0.1:{port}`) and returns a live `HttpClient`
bound to that base address — `GatewayTestHost.cs`'s own class summary
states this explicitly ("never `TestServer`/`WebApplicationFactory`'s
in-memory transport"). Every call in `BlackBoxApiTests.cs` is
`Fleet.Gateway.Client.{Get,PostAsJsonAsync}(...)`, a genuine socket round
trip. This satisfies CLAUDE.md's "black-box through the Gateway,
`HttpClient` as the client only".

**Where do the tests live?** `tests/Gateway.IntegrationTests`, the existing
project — no new project, so `./quality.sh`'s `dotnet test` already picks
it up (it is the same project `SagaEndToEndVerificationTests.cs` lives in).

**R49's read path — which, and is it read-only?** `OpenBillingDb()` opens a
fresh `BillingDbContext` directly against `Fleet.BillingConnectionString`
(EF Core over the real MS-SQL Testcontainer), and every query is
`.AsNoTracking()` — `Invoices`/`Payments`/`CreditItems` `CountAsync`/
`SingleAsync` calls, no `SaveChanges` ever called on this context. Same
idiom `SagaEndToEndVerificationTests.OpenBillingDb()`/`OpenOrdersDb()`
already use.

---

## 2. Assertion inventory — #7's `black-box-api.integration.spec.ts` (648 lines)

Enumerated by content, one row per `expect(...)`/`toHaveLength`/etc.

### Scenario 1 — happy path (`:485-527`)

| #7 assertion | Disposition |
|---|---|
| `placed.totalAmount === 49_998` | **Ported** — `HappyPath...`: `placeDoc.RootElement.GetProperty("totalAmount") == 3000` (own fixture, per-line `unitPrice`, not #7's 24999-priced catalog product) |
| `placed.projectionPending === true` | **Ported** — same test, `GetProperty("projectionPending").GetBoolean()` |
| `placed.status === 'placed'` | **Ported** — same test |
| `invoiced.references.invoiceReference` truthy | **Ported** — `Assert.NotNull(...)` |
| `invoice.totalAmount === 49_998` | **Ported** — `Assert.Equal(3000L, invoice.TotalAmount)` |
| `paymentResponse.status === 201` | **Ported** |
| `paymentResponse.body.outcome === 'accepted'` | **Ported** |
| `completed.status === 'completed'` | **Ported** |
| completion triple present in `events[]` | **Ported** |
| `assertCausalOrder(completed.events)` | **Ported** — `AssertCausalOrder`, C# port of the same algorithm, same skip-if-cause-outside-timeline rule |

### Scenario 2 — compensation (`:529-600`)

| #7 assertion | Disposition |
|---|---|
| `placed.totalAmount % 100 === 99` | **Deliberately not ported** — guaranteed algebraically by the `unitPrice: 1099` construction; same precedent as `SagaEndToEndVerificationTests.Criterion2`, which also does not assert this |
| `cancelled.cancellationReason === 'credit_rejected'` | **Ported** |
| three indices present, `creditRejectedIndex < stockReleasedIndex`, `< orderCancelledIndex` | **Ported**, verbatim structure |
| `stockReleasedIndex < orderCancelledIndex` (R28's own clause) | **Ported** |
| `stockReleasedOccurredAt === orderCancelledOccurredAt` (the documented tie) | **Ported** — kept as documentation only, per #7's own final disposition (§5 of #7's review: "asserted, correctly framed as documenting a deliberate design property, never as the ordering proof") |
| `assertCausalOrder(cancelled.events)` | **Ported** |
| reservation row: length 1, `status === 'released'` | **Ported** |

### Scenario 3 — R48/B10 idempotency (`:602-636`)

| #7 assertion | Disposition |
|---|---|
| order not `.99` | **Not applicable** — implicit in fixture construction (`2499 × 3 = 7497`) |
| `invoiced.references.invoiceReference` truthy | **Not applicable** — not re-asserted; `FindInvoiceAsync` would time out and fail loudly if no invoice existed, so the property is exercised, not merely skipped |
| `first.status === 201`, `outcome === 'accepted'` | **Ported** |
| `second.status === 200`, `outcome === 'duplicate'` | **Ported** |
| `Idempotent-Replay: true` | **Ported** |
| Billing DB `COUNT(*) === 1` | **Ported** |

### Scenario 4 — auth/error shapes (`:638-647`)

| #7 assertion | Disposition |
|---|---|
| no auth → 401 | **Deliberately not ported here** — already covered, `tests/Gateway.IntegrationTests/AuthAndRateLimitHttpTests.cs:46` (`Me_WithoutABearerToken_Returns401`) and `:35` (login) |
| malformed id → 400 | **Deliberately not ported here** — already covered and explicitly cited as #7's own port: `OrdersHttpTests.cs:151` (`GetOrder_ForAMalformedId_Answers400`), whose own doc comment reads *"Ported from #7's own black_box_api scenario 4"* |
| unknown id → 404 | **Deliberately not ported here** — already covered, `OrdersHttpTests.cs:139` (`GetOrder_ForAnIdThisGatewayNeverIssued_Answers404`) |

### R49 — never existed as a scenario in #7 at all

#7's own `black-box-api.integration.spec.ts` has no R49 scenario (grepped
and confirmed via `specs/shared/test-matrix.md`'s own #7 R49 cell, read
from the #7 checkout: it stayed a NATS-responder-level/unit closure,
*"same NATS-level caveat as R48 (no Gateway yet)"*, never ported forward
even after R48 itself dropped that caveat). So the three `R49_*` tests in
`BlackBoxApiTests.cs` are **new, not ported** — #8 closes R49 at API level
first in the trilogy, per the acceptance bullet's own text.

---

## 3. Ported-idiom ledger

| # | #7 relied on | In #8 that property is supplied by | Guard |
|---|---|---|---|
| L1 | A real spawned Gateway OS process (`spawn-real-service.ts`), because NestJS's DI graph could only be composed from a process boundary (#7 `black-box-api.integration.spec.ts:1-13`, `progress/impl_saga_e2e_verification.md`). | `GatewayHost.CreateBuilder`/`.Build()` run **in-process**, over real Kestrel — `GatewayTestHost.StartAsync` (`tests/Gateway.IntegrationTests/GatewayTestHost.cs:62-88`). #8's services already expose real, static composition roots (`XyzHost.CreateBuilder`), so no process boundary is needed. | Structural — every request in `BlackBoxApiTests.cs` goes over `Fleet.Gateway.Client`, a real `HttpClient` against a real socket; `AssemblyBehavior.cs`'s own precedent (`GatewayTestHost.StartAsync`'s bind-retry) already proves this is a genuine Kestrel bind, not an in-memory shim. |
| L2 | `mysql-worker-client.ts`, a dedicated worker-process DB client, because `apps/gateway` was forbidden from resolving `mysql2`/`drizzle-orm` at all (`no-write-database-client.spec.ts`). | Nothing needed — CLAUDE.md's domain-purity rule reaches only `Domain/` namespaces under `src/`, never a test project, so `BlackBoxApiTests.cs` opens `BillingDbContext`/`FulfillmentDbContext` directly via EF Core. | N/A — no guard needed; confirmed by reading CLAUDE.md's rule text and by the absence of any `tests/Architecture.Tests` rule reaching test projects (same finding `SagaEndToEndVerificationTests.cs`'s own remarks already record). |
| L3 | `PRD-0001` seeded at 24999 so quantity 1 totals `.99` (R42's trigger) — a fixture-level workaround (#7 file header, `:44-48`). | `PlaceOrderRequestLineDto.UnitPrice`, a genuine per-line override in the wire contract (`src/Gateway/Presentation/Dto/RequestDtos.cs:16`) — `SagaEndToEndVerificationTests.Criterion2` already established the technique (`unitPrice: 1099`), reused here for every scenario. | N/A — not a hand-built property, a real feature of #8's own contract. |
| L4 | The completion-triple causal-order defect class (#7's D4): a status-rank tiebreak (`statusRank`) got the compensation pair right and the completion triple backwards, because it totalised *order status*, not *causal sequence* (#7 `progress/review_api_tests.md` §2, Option A vs Option B). | #8 never carries the D4-shaped defect: `src/Projector/Infrastructure/Persistence/TimelineOrder.cs` sorts `(occurredAt, a topological causal depth computed via a bounded `$reduce` fixpoint over `causationId`/`eventId`, eventId)` — #7's own review recommended "Option B" (a causation-chain tiebreak) as *"correct in general"* but *"more invasive"* and never implemented it; #8 built Option B from the start. | Armed here, structurally: `HappyPath_ReachesCompleted_WithTheCompletionTripleCausallyOrdered` asserts the completion triple present AND causally ordered via `AssertCausalOrder`, the GENERAL invariant, never a hand-written sequence — the exact shape #7's D4 slipped past. `AssertCausalOrder_WhenAnEffectPrecedesItsCause_FailsNamingTheViolation` (no fleet needed) proves the check itself has teeth against exactly D4's shape (an effect sorted before its cause). |
| L5 | R28's `occurredAt`-tie documentation assertion, kept post-fix as "documenting a deliberate design property" once the array-order assertions became the actual proof (#7 review §5, §2.6). | `SagaFactHandler.cs:292` — `order.Cancel(cancel.Reason(fact), cancel.CompensationSteps(fact), fact.OccurredAt, UniqueId.From(fact.EventId), note)` — reuses the triggering `stock.released.v1` fact's own `OccurredAt` verbatim for `order.cancelled.v1`, never a fresh clock read. Confirmed by reading the call site directly, not inferred. | `NinetyNineOrder_CompensatesVisibly...`'s `Assert.Equal(stockReleasedOccurredAt, orderCancelledOccurredAt)` — kept as documentation, explicitly commented as such, never load-bearing for the ordering proof (that is the array-index assertions immediately above it). |

**Two-party claims, both probed:** L1 and L4 each describe a property of
#8's OWN code (not a two-writer/two-ordering engine claim), so the
"probed both ways" rule does not apply to either row.

---

## 4. Arming — verbatim failures

Protocol followed exactly: `cp` a backup of each touched source file
before mutating, mutate, run the ONE named test, record the failure
verbatim, restore from the **backup** (never `git checkout` — these files
were dirty from another in-flight feature), confirm restore with `cmp`,
force a rebuild (`--no-incremental` or `touch` + rebuild), re-run green.
Backups kept in the session scratchpad (`arm_backups/`), not in the repo.

### Arm 1 — the compensation reason (bullet: "the compensation reason")

**Mutation:** `src/Orders/Application/Sagas/SagaStepTable.cs:106` —
`"credit_rejected" => CancellationReason.CreditRejected,` →
`"credit_rejected" => CancellationReason.StockRejected,`

**Test:** `BlackBoxApiTests.NinetyNineOrder_CompensatesVisibly_WithTheCompensationChainCausallyOrdered`

**Verbatim failure:**
```
System.TimeoutException : order ccf4d212-4786-4f1a-ac17-6f8995ae381f never reached status "cancelled" within 00:01:30 (last observed: "stock_reserved").
```

Not the failure shape I initially expected (a wrong `cancellationReason`
string) — the domain's own precondition guard
(`Domain.Errors.CancellationReasonNotApplicableError`, per
`SagaStepTable.MapReason`'s own doc comment) refuses `StockRejected` from
`StockReserved`, so the saga's `Cancel` step throws and the order never
transitions at all. This is stronger evidence than the anticipated one: it
shows the reason mapping is checked *twice* — once by this table, once by
the aggregate's own precondition — and corrupting either one is caught.

**Restore:** `cp` from backup, `cmp` identical, `touch` + `dotnet build
--no-incremental`, re-run: **1/1 passed.**

### Arm 2 — R49 amount mismatch, the rejection code + "nothing changed"

**Mutation:** `src/Billing/Domain/Invoice.cs:331` —
`if (input.Amount.MinorUnits != TotalAmount.MinorUnits)` →
`if (false && input.Amount.MinorUnits != TotalAmount.MinorUnits)`

**Test:** `BlackBoxApiTests.R49_AMismatchedAmount_IsRejectedWithAMachineReadableCode_AndBillingsOwnDatabaseShowsNothingChanged`

**Verbatim failure:**
```
Assert.Equal() Failure: Values differ
Expected: UnprocessableEntity
Actual:   Created
```

The mismatched-amount payment was wrongly **accepted** (201), proving the
guard is load-bearing for both the HTTP status/code claim and the
"nothing changed" claim in one strike (a written payment row would follow
from the same defect).

**Restore:** `cp` from backup, `cmp` identical, forced rebuild, re-run:
**1/1 passed.**

### Arm 3 — R49 already-paid, the rejection code + "nothing changed"

**Mutation:** `src/Billing/Domain/Invoice.cs:321` —
`if (_state is InvoiceState.Paid)` → `if (false && _state is InvoiceState.Paid)`

**Test:** `BlackBoxApiTests.R49_ADifferentPaymentReferenceAgainstAnAlreadyPaidInvoice_IsRejectedWithAMachineReadableCode_AndBillingsOwnDatabaseShowsNothingElseChanged`

**Verbatim failure:**
```
Assert.Equal() Failure: Values differ
Expected: Conflict
Actual:   Created
```

The second, genuinely different `paymentReference` was wrongly
**accepted** (201/Created instead of 409/Conflict) — with the B8 guard
gone, `Invoice.MarkPaid` proceeded and a genuine SECOND payment row was
written for the already-paid invoice, proving the guard is what had
prevented it.

**Restore:** `cp` from backup, `cmp` identical, forced rebuild, re-run:
**1/1 passed.**

### Arm 4 — the duplicate-payment count (bullet: "the duplicate-payment count")

**Mutation:** `src/Billing/Application/PaymentRegisterService.cs`, BOTH
idempotency layers neutralised in the same mutation (neither alone
changes the outcome — see the note below):
- `:56` `if (existingPayment is not null)` → `if (false && existingPayment is not null)`
- `:98` `if (authorityPayment is not null && string.Equals(...))` → `if (false && authorityPayment is not null && string.Equals(...))`

**Test:** `BlackBoxApiTests.DuplicatePaymentReference_YieldsExactlyOnePayment_InBillingsOwnDatabase`

**Verbatim failure:**
```
second (duplicate) payment did not answer 200: 409 Conflict: {"type":"about:blank","title":"Invoice already paid","status":409,"detail":"Invoice 'INV-000001' has already been paid.","code":"INVOICE_ALREADY_PAID","correlationId":"a81c57b6-e56d-4bfb-82f6-4594de621eb7","occurredAt":"2026-09-17T10:34:50.286Z"}
```

**Why both layers had to be broken together:** neutralising only the fast
path (`:56`) leaves the authority re-read (`:97-101`) — which, for the
SAME invoice and SAME `paymentReference`, still correctly answers
`duplicate` — so the test stays green with only one guard removed. This is
genuine defense in depth, discovered by first mutating `:56` alone,
observing no failure, and reasoning through the second layer before
mutating it too (recorded here rather than silently re-tried, per the
arming protocol's evidentiary standard).

**Restore:** `cp` from backup, `cmp` identical for both edits, forced
rebuild, re-run: **1/1 passed.**

### Arm 5 (meta) — R24 ordering: "swap two timeline entries and the check must fail"

No fleet, no source mutation — this arms the TEST'S OWN structural check
directly, per the brief's own framing.
`BlackBoxApiTests.AssertCausalOrder_WhenAnEffectPrecedesItsCause_FailsNamingTheViolation`
feeds `AssertCausalOrder` a hand-built two-entry array where the FIRST
array element's `causationId` names the SECOND element's `eventId` (the
exact D4 inversion shape) and asserts it throws:

```csharp
var failure = Assert.Throws<Xunit.Sdk.TrueException>(() => AssertCausalOrder(doc.RootElement));
Assert.Contains("causal order violated", failure.Message, StringComparison.Ordinal);
Assert.Contains("b.happened.v1", failure.Message, StringComparison.Ordinal);
```

Passing (confirmed in every full run, including the final 77/77) — the
guard fires, names the violating `eventType`, and names both indices in
its message.

### Consolidated confirmation

After all four source-level arms were restored, `cmp`-verified and
force-rebuilt, the FULL `Gateway.IntegrationTests` project was run once
more end to end: **77/77, 0 failures.**

---

## 5. Defeat list — which rows apply

Run against `BlackBoxApiTests.cs`'s own guards (not a syntax/text
scanner — a behavioural integration suite against real infrastructure, so
most of the text-shadowing rows do not apply):

1. **Delete the behaviour** — applies, exercised in all four arms above.
2. **Corrupt a field the test supplied** — applies: arm 5's meta-guard
   corrupts the causal ordering of a hand-built `causationId` field the
   test itself supplies, and the array-order assertions in scenarios 1/2
   are themselves corruption-probes over real `eventType`/index data.
3. **Substitute a valid sibling identifier** — does not apply. This suite
   reads no environment-keyed identifier family (`MSSQL_DB_*`, a subject,
   a topic) as a parameter; every subject/table name is a literal.
4–8, 10–11 (shadow in a comment/string, hide in `#if`, hide in a raw
   string, drop an optional element, compare literal to literal, let
   build output join the population, write it in an unrecognised form) —
   **do not apply**: none of these attacks a real-infrastructure
   behavioural test, which has no source-scanning instrument to defeat.
9. **Satisfy the closer half and leave the premise stale** — checked
   directly for the ledger (§3): every row's "#7 relied on X" half is
   cited to a specific #7 file:line I read myself, not inferred; every
   row's "in #8" half is cited to a specific #8 file:line I read myself.
12. **Serve the failure through a path the population never drives** —
    applies and is answered by construction: every assertion in this
    suite is reached through the REAL Gateway → REAL NATS RPC → REAL
    Billing/Orders domain code → REAL MS-SQL, proven directly by the four
    arms above, each of which mutated production domain/application code
    and observed the SAME test fail through that exact path.

---

## 6. Developer infrastructure down

Confirmed BEFORE spinning any Testcontainers fixture: `ss -ltn` on
9092/1433/4222/27017 returned nothing, and `docker ps` showed zero
containers. All Testcontainers-based runs in this session therefore prove
the suite passes with the developer infrastructure down, per CLAUDE.md's
binding rule (id 104's own lesson).

---

## 7. A real design change made mid-implementation, and why

**First attempt:** `BlackBoxApiTests` joined `SagaEndToEndVerificationTests`'s
own `SagaE2ECollection`, reusing its four container fixtures. This
**failed live**: a full `dotnet test` run of the whole project produced
**5 spurious failures, all in `SagaEndToEndVerificationTests`, none in the
new file** — every one a `TimeoutException` waiting for a status that
never arrived. Root cause: `AssemblyBehavior.cs`'s
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`
serialises **collections**, not classes within one collection, and both
classes' statically-held `SagaFleet` instances (ten real service hosts,
six MS-SQL databases total) were live SIMULTANEOUSLY once each class had
run at least one `[Fact]` — resource contention, not a business-logic
defect (0 of the 5 reproduced when the offending class was run alone).

**Fix:** `BlackBoxApiTests` now uses its OWN, separate,
`DisableParallelization = true` collection (`BlackBoxApiCollection`) with
its own four container fixtures — the same pattern
`NatsCollection`/`StreamProjectorEndToEndCollection` already use in this
project, per `AssemblyBehavior.cs`'s own header. This costs real extra
container-boot time (a second Kafka/MS-SQL-server/NATS/MongoDB spin-up)
but guarantees xUnit fully finishes and DISPOSES `SagaE2ECollection`
before `BlackBoxApiCollection` starts, so the two fleets are never live at
once. Re-measured after the change: **77/77, run three times total across
this session, 0 failures in any of them.**

`SagaEndToEndVerificationTests.cs` was touched only to add, then remove
again, one `ICollectionFixture<>` clause during this investigation — its
`git diff` against `HEAD` is unrelated pre-existing work from another
in-flight feature (`dead_letter_producer_defaults_to_localhost`), verified
line-by-line.

---

## 8. Counts

- New test file: `tests/Gateway.IntegrationTests/BlackBoxApiTests.cs`, 665 lines, **7 `[Fact]`s** (6 integration-fleet scenarios + 1 no-fleet meta-guard).
- `tests/Gateway.IntegrationTests` project total: **77/77** (previous baseline 70/70, `./quality.sh`, 2026-09-17, + 7 new = 77 — reconciled exactly). Run three times in this session after the collection split, 0 failures each time.
- `dotnet build`: 0 warnings, 0 errors (multiple runs, including one `--no-incremental`).
- `dotnet format --verify-no-changes`: exit 0.
- `specs/shared/test-matrix.md`: exactly 2 lines changed (R24, R49), verified cell-by-cell that only column 5 (Status) differs on each.
- `feature_list.json`: exactly 1 line changed (id 31's status), verified via `git diff`.
- Arming: 4 source-level mutations (each `cp`-backed-up, `cmp`-restored, forced-rebuilt, re-confirmed green) + 1 no-fleet meta-guard, covering the four claims the brief named explicitly.

---

## 9. What I could not do / did not do

- Did not run `./quality.sh` (brief's own instruction — the leader runs it at wrap-up).
- Did not touch `src/` except transiently during arming (all three files
  `cmp`-verified restored to their pre-arming byte content; `git status`
  confirms no residual diff against the pre-existing dirty state on any of
  them beyond what another in-flight feature already carried).
- Did not extend `Gateway.IntegrationTests`' 401/400/404 coverage — #8
  already ports #7's scenario 4 elsewhere (§2), so re-porting it here
  would duplicate existing, cited coverage rather than close a gap.
- Nothing surprised me structurally, but two things were worth recording:
  (a) the credit-item baseline bug caught by the FIRST real test run
  (§4's arms rely on a corrected version of this fix — placing an order
  already writes a `credit_items` "hold" row, so "the credit ledger shows
  nothing" is the wrong claim; "the credit ledger shows nothing NEW" is
  the right one, and the test now captures a baseline before each rejected
  attempt); (b) the collection-sharing regression in §7, found only
  because the brief's own "reconcile counts" instruction forced a
  full-project run rather than trusting the new file's own 7/7 in
  isolation.
