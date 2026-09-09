# impl_orders_catalog_responder

Feature id 40, phase 13, `sdd: false`. Status set to `in_review`.

## What was built

A second RPC subject, `catalog.reference.list`, added to Orders' existing NATS
responder — never a second `BackgroundService` (CLAUDE.md: "One
BackgroundService per transport"; `BillingRpcResponder`'s own precedent for
extending rather than forking). `OrdersCreateResponder` now runs two
concurrent `SubscribeAsync` loops via `Task.WhenAll`: the pre-existing
`orders.create` loop, unchanged in behaviour, and the new
`catalog.reference.list` loop.

The listing itself goes through **the same registered `IOrderReferenceCatalog`
implementation** `PlaceOrderCommandHandler` already depends on — four new
list-shaped methods added to that one port and its one adapter
(`EfCoreOrderReferenceCatalog`), never a second adapter or a direct
`OrdersDbContext` query written beside it. See the ported-idiom ledger below
for how this is guarded, not merely asserted in a doc comment.

### Files touched

**Application**
- `src/Orders/Application/Ports/IOrderReferenceCatalog.cs` — added
  `ProductCatalogEntry`, `PartyCatalogEntry`, `CurrencyCatalogEntry` and four
  `List*Async` port methods, alongside the existing find-shaped methods.
- `src/Orders/Application/Queries/ListCatalogReferenceQuery.cs` (new) —
  `ListCatalogReferenceQuery`/`CatalogReferenceListResult`/
  `ListCatalogReferenceQueryHandler`. The handler's only dependency is
  `IOrderReferenceCatalog`.

**Infrastructure**
- `src/Orders/Infrastructure/Persistence/EfCoreOrderReferenceCatalog.cs` —
  `ListProductsAsync`/`ListRetailersAsync`/`ListCompaniesAsync` (join on
  currency, `includeDisabled` filter, ordered by code) and
  `ListCurrenciesAsync` (no disable concept — the table has no
  `disabled_at` column).
- `src/Orders/Infrastructure/Messaging/Rpc/RpcSubjects.cs` — added
  `CatalogReferenceList = "catalog.reference.list"`.

**Presentation**
- `src/Orders/Presentation/Rpc/CatalogReferenceListPayloads.cs` (new) — wire
  DTOs (`CatalogReferenceListRequestPayload`, `ProductPayload`,
  `PartyPayload`, `CurrencyViewPayload`, `CatalogReferenceListReplyPayload`)
  and the `CatalogReferenceKinds` closed-set constant.
- `src/Orders/Presentation/Rpc/CatalogReferenceListRequestValidator.cs`
  (new) — `InvalidCatalogReferenceListRequestError` +
  `CatalogReferenceListRequestValidator` (wire-shape validation before the
  query is built, matching `OrdersCreateRequestValidator`'s A2 discipline).
- `src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs` — one added case
  mapping `InvalidCatalogReferenceListRequestError` to `VALIDATION_FAILED`.
  Extended rather than forked into a second mapper (matches
  `BillingErrorMapper`'s own precedent of one shared mapper per responder).
- `src/Orders/Presentation/OrdersCreateResponder.cs` — extended with the
  second subscribe loop, `HandleCatalogReferenceListAsync`, and the
  `CatalogReferenceListResult` → `CatalogReferenceListReplyPayload` mapping.

**Tests**
- `tests/Orders.UnitTests/RpcSubjectsTests.cs` — one case added (subject
  matches the spec's channel address).
- `tests/Orders.UnitTests/CatalogReferenceListRequestValidatorTests.cs` (new).
- `tests/Orders.UnitTests/CatalogReferenceListPayloadTests.cs` (new) — the
  `StockRpcPayloadTests`/`BC23` instrument applied to this feature's payloads.
- `tests/Orders.UnitTests/ListCatalogReferenceQueryHandlerTests.cs` (new).
- `tests/Orders.UnitTests/CatalogReferenceListPortReuseTests.cs` (new) — the
  ledger's own guard (below).
- `tests/Orders.UnitTests/OrdersCreateErrorMapperTests.cs` — one case added.
- `tests/Orders.UnitTests/PlaceOrderTestDoubles.cs` — `FakeOrderReferenceCatalog`
  extended with the four list methods (same fake class, not a second one).
- `tests/Orders.IntegrationTests/EfCoreOrderReferenceCatalogListTests.cs`
  (new) — real MS-SQL, the adapter's own list methods.
- `tests/Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs`
  (new) — real NATS + real MS-SQL, the responder end to end.

## Traceability

`sdd: false`, no `requirements.md`, no `R<n>` — `specs/shared/test-matrix.md`
has no row for this feature and none was added, matching the other `sdd:
false` features in this backlog. Every test is named for the acceptance
bullet or design point it proves; see the file list above.

**Acceptance bullet 1** — "GET /catalog/products, /catalog/retailers,
/catalog/companies return real data through the Gateway": the Gateway (id
25) does not exist yet (`src/Gateway/` is still four `README_PLACEHOLDER.cs`
files — confirmed by directory listing before writing any code). This
feature's actual deliverable is the NATS responder those endpoints will call,
proven at the NATS boundary by `CatalogReferenceListAcceptanceTests` — a real
NATS broker, a real MS-SQL database, real seeded reference data, real
round-trip requests. `GET /catalog/*` itself is feature 25's claim to prove,
not this one's.

**Acceptance bullet 2** — "no new bounded context": no new database, no new
service; the four list methods live on the existing `IOrderReferenceCatalog`
port against the existing `otc_orders` reference tables.

**Acceptance bullet 3** — "reuses the same reference-data lookup
`PlaceOrderHandler` already calls, exposed as a query rather than
duplicated": `ListCatalogReferenceQueryHandler`'s only dependency is
`IOrderReferenceCatalog`, resolved from the same single DI registration
`PlaceOrderCommandHandler` uses. Guarded by
`CatalogReferenceListPortReuseTests` (armed below).

## The ported-idiom ledger

Per the amended CLAUDE.md rule (bound to the port, not to a `design.md` this
`sdd: false` feature does not have):

| #7 relied on | In #8 that property is supplied by | Guard |
|---|---|---|
| **Corrected in the fix round below (D2) — this row originally read "NestJS's module-scoped provider singletons … declaratively, with nothing to configure wrong," which is false; see the fix-round section for the citation and the corrected text.** An explicit `useExisting` alias between TWO distinct DI tokens (`ORDER_REFERENCE_DATA` and `CATALOG_REFERENCE_LIST`), `apps/orders/src/app.module.ts:123-137`, carrying its own multi-line comment explaining why it is an alias and not a second `useFactory` — a deliberate configuration decision, not a property NestJS's DI supplied for free. #7 also kept TWO SEPARATE ports on that one adapter (`catalog-reference-list.port.ts:1-21`), deliberately rejecting a widened single port. | One `IServiceCollection` registration decision: `OrdersAcceptanceServiceCollectionExtensions.AddOrdersAcceptance`'s ONE `AddScoped<IOrderReferenceCatalog, EfCoreOrderReferenceCatalog>()` line, resolved per-request-scope by both `PlaceOrderCommandHandler` and `ListCatalogReferenceQueryHandler` — plus a WIDENED single port (#8's divergence from #7's two-ports shape), so there is no second token to alias and nothing to mis-alias. Nothing stops a second, parallel registration (or a query handler that bypasses the port for a direct `OrdersDbContext` query) unless a test checks it. | `CatalogReferenceListPortReuseTests.AddOrdersAcceptance_RegistersExactlyOneIOrderReferenceCatalogImplementation` (registration count == 1) and `...BothDeclareAConstructorDependencyOnIOrderReferenceCatalog` (dependency shape, now `Assert.Equal` on the exact parameter-type list — A1). **Armed** — see below. |

## Arming table

Every mutation below: introduced on the real file, confirmed FAIL with the
verbatim message, restored from a `cp` backup, restore verified with `cmp`
(byte-identical), rebuilt with `dotnet build --no-incremental` before the
confirming green run.

| # | File mutated | Mutation | Named test | FAIL message (verbatim) | Restored + green after forced rebuild |
|---|---|---|---|---|---|
| 1 | `src/Orders/Infrastructure/OrdersAcceptanceServiceCollectionExtensions.cs` | Duplicated `services.AddScoped<IOrderReferenceCatalog, EfCoreOrderReferenceCatalog>();` — a second, parallel registration. | `CatalogReferenceListPortReuseTests.AddOrdersAcceptance_RegistersExactlyOneIOrderReferenceCatalogImplementation` | `Assert.Single() Failure: The collection contained 2 items` `Collection: [ServiceType: ...IOrderReferenceCatalog Lifetime: Scoped ImplementationType: ...EfCoreOrderReferenceCatalog, ServiceType: ...IOrderReferenceCatalog Lifetime: Scoped ImplementationType: ...EfCoreOrderReferenceCatalog]` | Yes |
| 2 | `src/Orders/Presentation/OrdersCreateResponder.cs` | `ToPartyPayload`: swapped `party.Name`/`party.Vat` in the mapping — a field-swap corruption within one party's own payload. | `CatalogReferenceListAcceptanceTests.CatalogReferenceList_KindsOmitted_ReturnsAllFourCollectionsFromTheSeededReferenceData` | `Assert.Equal() Failure: Strings differ` `↓ (pos 0)` `Expected: "Test Retailer"` `Actual:   "FR00000000000"` | Yes |
| 3 | `src/Orders/Application/Queries/ListCatalogReferenceQuery.cs` | `retailers` fetched unconditionally, ignoring `query.Kinds` — the "only requested collections present" claim deleted for one collection. | `ListCatalogReferenceQueryHandlerTests.HandleAsync_KindsCarriesOnlyProducts_ReturnsProductsAndLeavesEveryOtherCollectionNull` | `Assert.Null() Failure: Value is not null` `Expected: null` `Actual:   [PartyCatalogEntry { Code = RETAILER-01, Name = Test Retailer, Country = FR, Vat = FR00000000000, Gln = 4006381333931, Currency = EUR, Enabled = True }]` | Yes |
| 4 | `src/Orders/Infrastructure/Persistence/EfCoreOrderReferenceCatalog.cs` | `ListProductsAsync`: `includeDisabled` filter (`.Where(row => row.Product.DisabledAt == null)`) deleted entirely. | `EfCoreOrderReferenceCatalogListTests.ListProductsAsync_IncludeDisabledFalse_ExcludesTheDisabledRowAndCarriesEveryFieldFromTheEnabledOne` | `Assert.Single() Failure: The collection contained 2 items` `Collection: [ProductCatalogEntry { Code = PROD-001, ... Enabled = True }, ProductCatalogEntry { Code = PROD-002, ... Enabled = False }]` | Yes |

Row 1 is the ledger's own guard. Rows 2–4 are the two mutation families
(deletion, payload corruption) applied to the two other countable claims
this feature makes: field-for-field correctness of the wire mapping, and
"only the requested collections are present."

## Verification

- `./quality.sh`: green. `dotnet format --verify-no-changes`: clean.
  Solution-wide `dotnet test`: **1239 passed, 0 failed, 0 skipped** across 16
  projects (counted from this session's own run —
  `grep -c "Passed!"` = 16 project lines, summed pass counts 50+23+21+119+
  307+226+65+52+12+34+87+16+6+56+83+82 = 1239). `Orders.UnitTests`: 307
  (was 280, +27 this feature). `Orders.IntegrationTests`: 82 (was 71, +11
  this feature).
- `./init.sh`: exit 0. Backlog coherence, session file, superseded rules,
  commit-msg hook all green; 1 feature `in_progress`... no — after my edit,
  0 `in_progress`, `orders_catalog_responder` reported as the session's
  active feature by `progress/current.md` (leader-owned, untouched by me).
- `feature_list.json`: `git diff feature_list.json` shows exactly one line
  changed for id 40 (`"status": "pending"` → `"status": "in_review"`). A
  second, unrelated diff block exists in that same file (an `env_var_guard`
  backlog entry's acceptance text) — pre-existing, uncommitted, not written
  by me; `init.sh`'s own run at session start already reported "21
  uncommitted change(s) — expected mid-session" before I touched anything.

## What I could not do, and why

- Acceptance bullet 1's literal `GET /catalog/*` cannot be proven — the
  Gateway does not exist yet (feature 25, later this phase). Proven at the
  NATS boundary instead, as the brief anticipated.
- Currencies carry no `includeDisabled` toggle: `otc_orders.currencies` has
  no `disabled_at` column (confirmed by reading the entity and the
  Databases doc reference in existing comments), so `ListCurrenciesAsync`
  takes no such parameter and the wire's `includeDisabled` flag has no
  effect on that one collection. This is a property of the existing schema,
  not something this feature could add without a migration outside its
  scope.

## What surprised me

- The generic single-body-per-shared-interface approach I first tried for
  `ListRetailersAsync`/`ListCompaniesAsync` (a shared private method over an
  `IPartyRow` interface both `Retailer` and `Company` would implement) was
  abandoned before it was ever built: EF Core's LINQ provider translates
  expression trees against the concrete entity's own reflected members, and
  a generic method's expression tree built through an interface-typed
  parameter risks a runtime translation failure that a compile-time check
  cannot catch. Two small, direct, near-identical methods are what shipped
  instead — safer than a saved handful of lines.
- Fabricating two more test GLNs (for a disabled retailer and a disabled
  company fixture) failed at run time against the real
  `GLN` value object's mod-10 check-digit validation on the first attempt —
  a good sign the domain's own invariant was doing its job in an
  integration test, not a defect in the feature.

## Fix round — response to `progress/review_orders_catalog_responder.md`'s rejection

**Verdict addressed: REJECTED, two blocking defects (D1, D2).** Both closed
below. The two cheap advisories (A1, A2) were closed too, since the review
flagged them as "optional but cheap." No redesign — the review found the
implementation itself correct (one responder, two loops, one port, one
adapter, one query) and this round touches nothing beyond what D1/D2/A1/A2
name.

### D1 — `includeDisabled` was unguarded end to end; now guarded at the wire

**File added to:** `tests/Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs`
— one new test,
`CatalogReferenceList_IncludeDisabledTrueVersusOmitted_TheDisabledProductAppearsOnlyWhenRequested`.
It seeds ONE disabled product **local to itself** (never widening the shared
`OrderPersistenceTestSupport.SeedReferenceDataAsync` fixture other suites
depend on — the review's own instruction), then drives THREE requests
through the real NATS wire against the real responder: `IncludeDisabled:
true` (must surface the row with `enabled: false`), `IncludeDisabled: null`
(must not), and `IncludeDisabled: false` (must not, proving the responder
does not merely special-case `null`).

**Arming table (both directions, per the review's explicit requirement):**

| # | File mutated | Mutation | Named test | FAIL message (verbatim) | Restored + green after forced rebuild |
|---|---|---|---|---|---|
| D1a | `src/Orders/Presentation/OrdersCreateResponder.cs:124` | `var includeDisabled = request.IncludeDisabled ?? false;` → `var includeDisabled = true;` (constant true — ignores the flag, always includes disabled rows) | `CatalogReferenceList_IncludeDisabledTrueVersusOmitted_TheDisabledProductAppearsOnlyWhenRequested` | `Assert.DoesNotContain() Failure: Filter matched in collection` `↓ (pos 2)` `Collection: [ProductPayload { Code = PROD-001, ... }, ProductPayload { Code = PROD-002, ... }, ProductPayload { Code = PROD-DISABLED, Ean = 1000000000031, Name = Disabled Product, Description = Withdrawn from sale, Price = 250, Currency = EUR, Enabled = False }]` | Yes |
| D1b | same line | `var includeDisabled = request.IncludeDisabled ?? false;` → `var includeDisabled = false;` (constant false — ignores the flag, always excludes disabled rows) | same test | `Assert.Single() Failure: The collection did not contain any matching items` `Expected:   (predicate expression)` `Collection: [ProductPayload { Code = PROD-001, ... Enabled = True }, ProductPayload { Code = PROD-002, ... Enabled = True }]` | Yes |

Both mutations: applied to the real file, `dotnet build src/Orders
--no-incremental` (0 warnings/errors each time), the named test run and
confirmed FAILING with the message above, restored from a `cp` backup taken
before D1a, `cmp` byte-identical confirmed after each restore, `touch`ed and
rebuilt `--no-incremental` before the confirming green run (`Passed! - 1,
Passed: 1` both times).

`Orders.IntegrationTests` count: 82 → 84 (D1's test **and** A2's test below;
D1 alone is +1).

### D2 — the ledger row's history half corrected from #7's actual source

**What was false, and where it lived:** the row at (pre-fix)
`progress/impl_orders_catalog_responder.md:111` said #7 got one-instance
reuse from "NestJS's module-scoped provider singletons ... declaratively,
with nothing to configure wrong." Opening #7's actual checkout
(`/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`)
disproves this:

- `apps/orders/src/app.module.ts:123-137` registers **two distinct DI
  tokens** — `ORDER_REFERENCE_DATA` (a `useFactory`) and
  `CATALOG_REFERENCE_LIST` (an explicit `useExisting: ORDER_REFERENCE_DATA`
  alias) — carrying its own multi-line comment: *"an ALIAS (`useExisting`),
  not a second `useFactory` ... this token resolves to the EXACT SAME
  instance ... never a second repository that could read the four reference
  tables differently."* An alias is a configuration decision that can be
  got wrong (point it at the wrong token, forget it, or write a second
  `useFactory` instead) — not a property NestJS's DI supplied for free.
- `apps/orders/src/application/ports/catalog-reference-list.port.ts:1-21`
  shows the divergence the original row omitted entirely: #7 kept **TWO
  SEPARATE ports** on one adapter, deliberately rejecting a widened single
  port — *"Widening that interface would also force every existing fake
  typed against it (`place-order.handler.spec.ts`) to grow a method it
  never uses."* #8 chose the opposite: **one widened port**
  (`IOrderReferenceCatalog`), so there is no second token to alias and
  nothing to mis-alias — `FakeOrderReferenceCatalog`
  (`tests/Orders.UnitTests/PlaceOrderTestDoubles.cs:87-134`) duly grew four
  methods place-order never calls, exactly the cost #7's port header
  predicted widening would carry.

**Files corrected (three places, all restating the same false claim):**

1. `progress/impl_orders_catalog_responder.md` line 111 (the ledger table
   row itself) — rewritten in place to cite `app.module.ts:123-137` and
   `catalog-reference-list.port.ts:1-21` by file and line, name the
   `useExisting` mechanism precisely, and record the two-ports-vs-one-port
   divergence. The row's first cell quotes the old wrong text verbatim and
   points to this section, rather than silently deleting it, so the
   correction itself is traceable.
2. `src/Orders/Application/Queries/ListCatalogReferenceQuery.cs:28-54` (the
   XML doc on `ListCatalogReferenceQueryHandler`) — rewritten to the same
   corrected mechanism.
3. `tests/Orders.UnitTests/CatalogReferenceListPortReuseTests.cs:11-29` (the
   file-level XML doc) — rewritten likewise.

**The guard itself needed no change** — `AddOrdersAcceptance_RegistersExactlyOneIOrderReferenceCatalogImplementation`
and (now-strengthened, see A1) `...BothDeclareAConstructorDependencyOnIOrderReferenceCatalog`
were already correct and already armed in round 1 (P1 in the review, and
row 1 of round 1's own arming table); D2 was a documentation defect, not a
code or test defect. Per CLAUDE.md's ledger rule, read fresh from disk
before writing this: *"a ledger row's '#7 relied on X' half is a claim
about #7's source, read out of #7's checkout with a file and line, never
inferred from what the framework would plausibly have done."* Both
citations above were opened and read in this checkout during this fix
round, not inferred.

### A1 — exact constructor parameter list

`tests/Orders.UnitTests/CatalogReferenceListPortReuseTests.cs` —
`AssertHasConstructorDependencyOn<T>` (which used `Assert.Contains`) was
replaced by `AssertConstructorParameterTypesEqual<T>` (`Assert.Equal` on
the full ordered parameter-type list). `PlaceOrderCommandHandler`'s six
parameters and `ListCatalogReferenceQueryHandler`'s one are now asserted
exactly, closing the hole the review named: a handler that grew a second
data-access dependency (e.g. `OrdersDbContext` alongside the port) would
now fail this test instead of passing it silently. Not separately armed —
the review classed this as an advisory strengthening of an already-armed
guard (P1/D2 above already prove the underlying registration claim fails on
mutation), and `Assert.Equal` vs `Assert.Contains` is a strictly stronger
predicate over the same verified-correct data.

### A2 — the "ordered by code" claim now guarded

`tests/Orders.IntegrationTests/EfCoreOrderReferenceCatalogListTests.cs` —
new test `ListProductsAsync_ThreeRowsSeededOutOfOrder_ReturnsThemOrderedByCode`,
seeding three products in deliberately non-alphabetical insertion order
(`PROD-C`, `PROD-A`, `PROD-B`) against a real MS-SQL database and asserting
the returned order is `["PROD-A", "PROD-B", "PROD-C"]` — insertion order
cannot coincidentally satisfy this the way it could with an
already-alphabetical seed.

**Armed:** mutated `EfCoreOrderReferenceCatalog.cs:58` to delete
`.OrderBy(row => row.Product.Code)` from `ListProductsAsync`, rebuilt
`--no-incremental`, ran the named test:

```
Assert.Equal() Failure: Collections differ
                                                    ↓ (pos 0)
Expected: <generated>                              ["PROD-A", "PROD-B", "PROD-C"]
Actual:   ListSelectIterator<ProductCatalogEntry, string> ["PROD-B", "PROD-A", "PROD-C"]
                                                    ↑ (pos 0)
```

— confirming the assertion is live against real MS-SQL's own (non-`ORDER
BY`) return order, not an accident. Restored from a `cp` backup, `cmp`
byte-identical, rebuilt `--no-incremental`, confirming run green (8/8 in
that file, up from 7).

`Orders.IntegrationTests` count: 82 → 84 total (D1 + A2, +1 each).

### Full-suite verification, this round

- `dotnet build src/Orders --no-incremental` and equivalents for both test
  projects: 0 warnings, 0 errors, throughout every arm/restore cycle above.
- `dotnet test tests/Orders.UnitTests`: **307 passed, 0 failed, 0 skipped**
  (unchanged — A1 strengthened an existing test in place, no unit test
  added or removed).
- `dotnet test tests/Orders.IntegrationTests`: **84 passed, 0 failed, 0
  skipped** (was 82; +1 D1, +1 A2).
- `./quality.sh`: green end to end.
  `dotnet format --verify-no-changes` clean (implicit in the script's own
  step 1, which reported OK). Solution-wide `dotnet test`: **1241 passed, 0
  failed, 0 skipped** across 16 projects — counted from this session's own
  run: `grep -c "^Passed!" quality.log` → 16 project lines; pass counts
  23+50+21+65+119+307+226+34+87+6+12+52+16+56+83+84 = 1241 (was 1239 before
  this round; +2 matches the two new integration tests, D1 and A2).
  `Architecture.Tests`: 16/16, unchanged.
- `./init.sh`: exit 0. Backlog coherence, session file, superseded rules,
  backlog tripwire and commit-msg hook all `[OK]`; 1 feature `in_progress`
  reported (`orders_catalog_responder`, correctly, going into this edit).
- `feature_list.json`: single-line edit, id 40 `"in_progress"` →
  `"in_review"` (the review's rejection had set it to `in_progress`, not
  `pending` — confirmed by reading the file before editing). `git diff
  feature_list.json` shows exactly that one line plus the pre-existing id
  56 (`composition_root_env_reads_are_unguarded`) acceptance-text edit,
  which is the leader's and was left untouched.

## What I could not do, and why (this round)

Nothing outstanding. Both blocking defects and both advisories from the
review are closed, all four independently armed except A1 (a strengthening
of an already-armed guard, per its own note above — re-arming it would
re-prove P1/D2's already-verified claim, not a new one).

## What surprised me (this round)

- The review's own D1 prescription — seed one disabled row locally, drive
  both spellings of the flag through the real wire — turned out to double
  as the strongest possible regression guard for the exact bug it named:
  the constant-`true` and constant-`false` mutations produced two
  completely different, unrelated xUnit assertion failures
  (`DoesNotContain` vs `Single`), which is a good sign the single test
  really does examine both directions rather than one direction twice.
- Re-reading #7's `app.module.ts` for D2 surfaced a detail round 1 missed
  entirely even in its correct parts: #7's alias comment names the SPECIFIC
  risk it exists to prevent ("never a second repository that could read the
  four reference tables differently") in almost the same words #8's own
  registration-count test's XML doc uses for its own risk. The two repos
  independently converged on the same risk description for structurally
  different mechanisms (an alias vs. a widened port) — worth noting because
  it suggests the risk being guarded is real and stack-independent, not an
  artifact of either framework.
