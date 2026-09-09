# review_orders_catalog_responder

**Verdict: REJECTED.** Feature id 40 set back to `in_progress` in `feature_list.json` (single-line edit; `git diff feature_list.json` shows that line plus the leader's pre-existing id-56 acceptance edit, which is untouched). No entry appended to `progress/history.md` — a rejected feature is not closeable.

Two defects, one of them the very class this phase's amended ledger rule was adopted to catch. The delivered code is correct on every path I exercised; both defects are about what is *guarded* and what the ledger *claims*, which is exactly where this repository's expensive misses have lived.

## What I ran (independent verification, not a re-run of the world)

| Command | Result |
|---|---|
| `dotnet build --no-incremental` | 0 warnings, 0 errors |
| `dotnet test tests/Orders.UnitTests` | **307 passed, 0 failed, 0 skipped** (implementer's figure confirmed) |
| `dotnet test tests/Orders.IntegrationTests --filter "…CatalogReferenceListAcceptanceTests\|…EfCoreOrderReferenceCatalogListTests"` | **11 passed, 0 failed** against real MS-SQL + real NATS Testcontainers (docker verified live) |
| `dotnet test tests/Architecture.Tests` | **16 passed** — the NetArchTest suite run, not eyeballed |
| `git status --porcelain -- specs/shared/` | empty |

I did **not** re-run the full 16-project `dotnet test` or `./quality.sh`; the implementer's run is the evidence for those, and the claims I tested are per-suite. Everything else below is my own probe.

## Mutation probes I ran myself (both families)

Every mutation: applied to the real file, `dotnet build --no-incremental`, named tests run, then restored from a `cp` backup, `cmp` byte-identical, `touch`ed, rebuilt `--no-incremental`, and the confirming green run recorded at the bottom of this table.

| # | Family | Mutation | Outcome |
|---|---|---|---|
| P1 | reuse break (the ledger's own claim, stronger form than the implementer's) | Added a **second, DIFFERENT** adapter — a new `ProbeSecondCatalog : IOrderReferenceCatalog` registered alongside `EfCoreOrderReferenceCatalog` in `OrdersAcceptanceServiceCollectionExtensions.cs:41` | **KILLED.** `CatalogReferenceListPortReuseTests.AddOrdersAcceptance_RegistersExactlyOneIOrderReferenceCatalogImplementation` — `Assert.Single() Failure: The collection contained 2 items … ImplementationType: …EfCoreOrderReferenceCatalog, … ImplementationType: …ProbeSecondCatalog` |
| P2 | payload corruption, cross-collection | `OrdersCreateResponder.cs:168-169` — transposed `Retailers:`/`Companies:` in `ToReplyPayload(CatalogReferenceListResult)` | **KILLED.** `CatalogReferenceListAcceptanceTests.CatalogReferenceList_KindsOmitted_ReturnsAllFourCollectionsFromTheSeededReferenceData` — `Assert.Single() Failure: The collection did not contain any matching items` |
| P3 | deletion | `OrdersCreateResponder.cs:121` — deleted the `CatalogReferenceListRequestValidator.Validate(request);` call | **KILLED.** `CatalogReferenceListAcceptanceTests.CatalogReferenceList_ARequestWithAnUnknownKind_RefusesAsValidationFailedNotInternalError` — `Assert.Equal() Failure: Strings differ` |
| P4 | payload corruption, request side | `OrdersCreateResponder.cs:124` — `var includeDisabled = request.IncludeDisabled ?? false;` → `var includeDisabled = true;` (the wire flag ignored **and** the spec's `default: false` inverted) | **SURVIVED.** Orders.UnitTests **307/307 green**, the 11 catalog integration tests **11/11 green**. See D1. |

Restore confirmed: `cmp` byte-identical against backups for both mutated source files, the probe file deleted, `git status --porcelain` identical to its pre-probe listing, and after a forced `--no-incremental` rebuild: Orders.UnitTests 307, Architecture.Tests 16, catalog integration 11 — all green.

## Defects

### D1 (blocking) — the `includeDisabled` wire field is unguarded at the responder boundary; both mutation families survive

**File:** `src/Orders/Presentation/OrdersCreateResponder.cs:124` — `var includeDisabled = request.IncludeDisabled ?? false;`

Replacing that line with a constant `true` — which simultaneously *ignores the caller's flag* and *inverts `asyncapi.yaml`'s `CatalogReferenceListRequestPayload.includeDisabled: default: false`* — leaves the entire Orders unit suite (307) and all 11 new integration tests green. So does the mirror-image mutation (always `false`), for the same reason.

**Why nothing catches it, enumerated rather than swept.** Command and complete output are the `grep` of `IncludeDisabled|includeDisabled` across `tests/` with `--exclude-dir=bin --exclude-dir=obj` (**52 hits**, counted from that run: `grep -rn --include=*.cs --exclude-dir=bin --exclude-dir=obj "IncludeDisabled\|includeDisabled" tests/ | wc -l` → 52), classified:

- `tests/Orders.UnitTests/CatalogReferenceListRequestValidatorTests.cs:18,25,34,43` — the validator never inspects `IncludeDisabled`; line 25 passes `true` and asserts nothing about it.
- `tests/Orders.UnitTests/PlaceOrderTestDoubles.cs:101-126` — the fake's recording of the flag, port level.
- `tests/Orders.UnitTests/CatalogReferenceListPayloadTests.cs:26,96` — key-*name* assertions only, no behaviour.
- `tests/Orders.UnitTests/ListCatalogReferenceQueryHandlerTests.cs:40-124` — handler→port pass-through, both values. **Below** the responder: these construct `ListCatalogReferenceQuery(..., IncludeDisabled: x)` directly.
- `tests/Orders.IntegrationTests/EfCoreOrderReferenceCatalogListTests.cs:43-122` — adapter SQL filter, both values. **Below** the query.
- `tests/Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs:48,126,168,211,249` — the only five requests that ever cross the wire, and **all five pass `IncludeDisabled: null`**.

No hit is unclassified, and no hit drives the responder's own translation of the flag. Compounding it: `OrderPersistenceTestSupport.SeedReferenceDataAsync` seeds **no disabled row**, so even a wire request carrying `true` could not currently distinguish the two behaviours — the end-to-end effect of `includeDisabled` has never been observed at all.

**Why this is blocking rather than an advisory.** This is precisely the standing probe CLAUDE.md states: *"a corruption probe only bites on a field whose expected value the test supplied"* — no test supplies this one. It is one of only **two** fields in the request schema this feature implements; the other (`kinds`) is guarded three ways. And **#7 guarded exactly this seam**: `apps/orders/src/presentation/catalog-reference-list.controller.spec.ts:33` — *"defaults an omitted request body to kinds: [] (=> all) and includeDisabled: false"*, asserting `seenQuery?.includeDisabled` is `false` at the controller→query hop. #8 ported the mechanism and dropped its guard. Feature 25's Gateway will forward `?includeDisabled=true` straight into this seam (`apps/gateway/src/presentation/catalog.controller.ts:14-32` in #7), so the unguarded hop is on the next feature's critical path.

**To clear it:** a guard that drives the flag **through the wire** — a `CatalogReferenceListAcceptanceTests` case seeding a disabled row (locally, not by widening the shared `SeedReferenceDataAsync`, which other suites depend on) and asserting that `IncludeDisabled: true` returns it with `enabled: false` while `IncludeDisabled: null`/`false` does not. Then arm it: record the verbatim failure for `= true` **and** for `= false`, since the field is a boolean and one direction proves one direction only.

### D2 (blocking) — the ledger row's "#7 relied on X" column names a mechanism #7 did not use

**File:** `progress/impl_orders_catalog_responder.md:111`, and the same claim restated in `src/Orders/Application/Queries/ListCatalogReferenceQuery.cs:28-44` and `tests/Orders.UnitTests/CatalogReferenceListPortReuseTests.cs:11-24`.

The row says: *"NestJS's module-scoped provider singletons — a query handler that injects the same repository token as the place-order handler receives the LITERAL SAME instance, **declaratively, with nothing to configure wrong**."*

#7's actual code says the opposite. `apps/orders/src/app.module.ts:123-137` registers **two different tokens** and joins them with an explicit alias:

```
{ provide: ORDER_REFERENCE_DATA, useFactory: (db) => new DrizzleOrderReferenceDataRepository(db), inject: [ORDERS_DB] },
// `orders_catalog_responder` — an ALIAS (`useExisting`), not a second `useFactory` …
{ provide: CATALOG_REFERENCE_LIST, useExisting: ORDER_REFERENCE_DATA },
```

`useExisting` versus `useClass`/`useFactory` is a configuration decision that can be got wrong in exactly the way the ledger says only .NET can — #7's own module comment exists because of it. So the property was **not** free in #7, and the row's central assertion is false. This is the feature-24 shape again: the right conclusion reached through the wrong mechanism, written confidently enough that #9 will inherit it instead of re-deriving it.

The row also omits the one genuinely translated design decision. #7 kept **two ports on one adapter** and its port header (`apps/orders/src/application/ports/catalog-reference-list.port.ts:1-21`) explicitly rejects the alternative: *"Deliberately a SEPARATE port … Widening that interface would also force every existing fake typed against it (`place-order.handler.spec.ts`) to grow a method it never uses."* #8 chose the widened single port — and duly grew `FakeOrderReferenceCatalog` by four methods it never uses for place-order (`tests/Orders.UnitTests/PlaceOrderTestDoubles.cs:87-134`). That choice is defensible, arguably stronger for the reuse guarantee, and it is exactly what a ledger row is for. It is not recorded anywhere.

**To clear it:** rewrite the row from #7's source rather than from an assumption about NestJS — *"#7 relied on a `useExisting` alias between two tokens onto one adapter instance; in #8 that property is supplied by one `AddScoped<IOrderReferenceCatalog, EfCoreOrderReferenceCatalog>()` plus a widened single port, guarded by …"* — and add the port-shape line. Correct the two source comments that restate the false claim. The guard itself needs no change: P1 shows it has teeth, in a stronger form than the arming table used.

**What the ledger obligation was worth here (the question the brief asked).** Real, not ceremonial. Its guard is the only artefact in the feature that constrains the reuse claim at all, and it killed a mutation the rest of the suite could not see. The obligation did work; the row's history half was written without opening #7, which is the half that will be inherited.

## Advisories (not blocking)

- **A1 — `PlaceOrderCommandHandlerAndListCatalogReferenceQueryHandler_BothDeclareAConstructorDependencyOnIOrderReferenceCatalog` (`tests/Orders.UnitTests/CatalogReferenceListPortReuseTests.cs:57-67`) uses `Assert.Contains` over constructor parameters.** By construction it cannot fail when a *second* data-access dependency is **added** beside the port — a handler declaring `(IOrderReferenceCatalog, OrdersDbContext)` and querying the context directly satisfies it, and there is no Application→Infrastructure architecture rule in `tests/Architecture.Tests/` to catch it either (I read the assertion; I did not run this mutation). Same hole one layer up: the responder itself could resolve `OrdersDbContext` and bypass the query entirely with every current test still green. If the reuse claim is worth a guard, `Assert.Equal` on the exact parameter-type list is one line more.
- **A2 — the "ordered by code" behaviour** in `EfCoreOrderReferenceCatalog.ListProductsAsync/ListRetailersAsync/ListCompaniesAsync` (`.OrderBy(...)`, lines 58/81/98) is claimed in `progress/impl_orders_catalog_responder.md:35-37` and asserted by nothing; `asyncapi.yaml` does not require an order, so this is a claim to drop or to guard, not a defect.
- **A3 — the `message.Data is null` branch** (`OrdersCreateResponder.cs:115-118`) has no test; per the enumeration in D1, all five wire requests carry a serialised payload. Cosmetic — `orders.create`'s equivalent branch is in the same position.

## Acceptance-bullet → test mapping I verified

`sdd: false`, so there are no `R<n>` ids and `specs/shared/test-matrix.md` correctly gains no row.

| Bullet | Test(s) | Verified |
|---|---|---|
| 1 — `GET /catalog/*` returns real data through the Gateway | Not provable: `src/Gateway/` is still placeholders (feature 25). Proven at the NATS boundary instead by `CatalogReferenceListAcceptanceTests` (4 cases, real NATS + real MS-SQL) | Accepted as the correct substitute — same position #7 was in, and its own review accepted the equivalent |
| 2 — no new bounded context | `EfCoreOrderReferenceCatalogListTests` (7 cases) run against the existing `otc_orders` reference tables; no new DbContext, no new service, no new database; `Architecture.Tests` 16/16 green | Verified |
| 3 — reuses the same lookup `PlaceOrderHandler` calls, exposed as a query | `CatalogReferenceListPortReuseTests` (2 cases) + `ListCatalogReferenceQueryHandlerTests` (6 cases) against the same `FakeOrderReferenceCatalog` | **Verified by tracing the registration and both method bodies, not the signature**: one `AddScoped<IOrderReferenceCatalog, EfCoreOrderReferenceCatalog>()` at `OrdersAcceptanceServiceCollectionExtensions.cs:41`, resolved per-request-scope; `ListCatalogReferenceQueryHandler`'s only constructor parameter is that port; the four `List*Async` bodies sit in the same `EfCoreOrderReferenceCatalog` as `FindRetailerAsync`/`FindCompanyAsync`/`FindProductsAsync`/`CurrencyExistsAsync` and read the same `db.Products/Retailers/Companies/Currencies` sets over the same `OrdersDbContext`. No second adapter and no second `OrdersDbContext` query path exists — and P1 proves the guard fails if one is introduced |

**Existing place-order path unchanged:** `git diff src/Orders/Infrastructure/Persistence/EfCoreOrderReferenceCatalog.cs` and `…/Ports/IOrderReferenceCatalog.cs` are pure additions — lines 16-46 of the adapter (the four find-shaped methods) and the four original interface members are byte-identical to HEAD. `PlaceOrderCommandHandlerTests` and `OrdersCreateAcceptanceTests` still mean what they meant; `FakeOrderReferenceCatalog` gained methods but changed none.

**Wire shape:** subject `catalog.reference.list` matches `asyncapi.yaml channels.catalogReferenceList.address` and is pinned by `RpcSubjectsTests.RpcSubjects_CatalogReferenceList_EqualsTheAsyncApiCatalogReferenceListChannelAddress`, which reads the spec as text. Payload keys are camelCase and nulls-omitted through the one shared `JsonWire.Options`, pinned against the spec by `CatalogReferenceListPayloadTests.BC23_…` (5 schemas) with its own armed `G5` scratch-copy case. `CatalogReferenceListAcceptanceTests` line 139-143 asserts absent-versus-null on the raw JSON, which is the right instrument for "only the requested collections are present". Money crosses as `long` minor units (`ProductPayload.Price`); no `decimal` anywhere in the feature.

## CHECKPOINTS.md walk

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md`, `progress/history.md` exist
- [x] `.claude/agents/` holds the five agents
- [x] Every agent definition declares its model
- [x] `./init.sh` exits 0 (leader's run this session; not re-run by me)

**C2 — state coherent**
- [x] At most one feature `in_progress` — id 40, after this rejection
- [x] Every status in `rules.valid_status`
- [x] Every `done` feature has passing tests
- [x] `progress/current.md` describes this session
- [x] No `blocked` feature

**C3 — architecture**
- [x] No framework reference in any `Domain/` — `Architecture.Tests` 16/16 run, not eyeballed
- [x] No cross-service DB access — the four reference tables are `otc_orders`' own
- [x] No shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs`
- [x] No `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `SharedKernel` still has zero `PackageReference`
- [x] No `decimal` in domain arithmetic
- [x] Interaction correctly classified: a read-only request/reply for form data is **NATS-RPC**, matching `asyncapi.yaml`'s own `requestCatalogReferenceList` operation — not a fact, so not Kafka
- [x] No stray debug logging, no context-free TODOs (the one `REVIEW PROBE` comment was mine and is removed; `cmp` clean)

**C4 — verification real**
- [x] `./quality.sh` passes — **implementer's run**; mine were targeted: Orders.UnitTests 307, Architecture.Tests 16, catalog integration 11
- [x] Domain tests pure
- [x] Integration tests use real Testcontainers (MS-SQL + NATS), verified by running them
- [ ] **Coverage thresholds — not independently verified by me** (implementer reports `quality.sh` green, which includes the coverlet gate)
- [x] No Jest

**C5 — session close**
- [x] No suspicious untracked files
- [ ] `progress/history.md` entry with effort record — **correctly absent: rejected features are not closed**
- [x] `feature_list.json` reflects true state (id 40 → `in_progress`)
- [ ] Human told what was done / how to test manually — the leader's step after this verdict
- [x] Claude did not commit

**C6 — SDD** — not applicable to this feature (`sdd: false`); no `specs/orders_catalog_responder/` is required and none was created, which is right.

**C7 — reuse fidelity**
- [x] `specs/shared/` untouched — `git status --porcelain -- specs/shared/` empty
- [x] No amendment attempted by this feature
- [x] `R<n>` ids — none claimed, correctly
- [ ] n8n workflows / black-box API script — not applicable until the Gateway exists (feature 25)
- [ ] Effort record — pending, this feature is not closeable yet
- [ ] README benchmark section — pending

## Effort comparison (for the record; not yet appended to `history.md`)

#7's counterpart: 1 implementer pass, approved first time, ≈21 min measured implementation plus ≈35 min review. #8 is at 1 implementer pass **plus one rejection round**, so it will not beat #7 on passes. Two honest asymmetries to record when this does close: (a) #7's review did **not** independently re-run its live verification and relied on the implementer's narrative, while this review ran the containers, the architecture suite and four of its own mutations — the extra round is partly bought by a stricter gate, not only by weaker work; (b) #8 shipped more guard surface than #7 (a registration-count reuse test #7 had no need for, a `BC23` spec-parity theory over five schemas, a raw-JSON absent-key assertion) and still missed the one responder-level guard #7 *did* write.

## What must change before re-review

1. **D1** — a wire-level guard for `includeDisabled` (a disabled row seeded locally; `true` and `null`/`false` both driven through NATS and asserted to differ), armed in **both** directions with verbatim failures in `progress/impl_orders_catalog_responder.md`.
2. **D2** — the ledger row rewritten from #7's actual `useExisting` mechanism (`apps/orders/src/app.module.ts:123-137`), with the two-ports-versus-one-widened-port divergence recorded, and the two source comments that restate the false claim corrected.
3. Optional but cheap: **A1** (`Assert.Equal` on the exact constructor parameter list) and **A2** (drop or guard the ordering claim).

Nothing else in the feature needs to move. The implementation itself — one responder, two loops, one port, one adapter, one query — I found correct.

## Phase status

**Phase 13 is not closed by this feature.** Ids **41** (`orders_cancel_responder`), **25** (`gateway_rest_auth`), **26** (`gateway_sse_push`), **56** (`composition_root_env_reads_are_unguarded`) and **60** (`envelope_fixture_collisions_defeat_provenance_assertions`) all remain `pending`.

---

# Round 2 — re-review of the fix round

**Status: APPROVED.** Feature id 40 set `done` in `feature_list.json` (single-line edit at line 569, `in_review` → `done`); effort record appended to `progress/history.md`. Round 1 above is closed and unamended. Both blocking defects (D1, D2) are genuinely closed — verified by my own mutations and by reading #7's source, not by accepting the fix round's tables. Three non-blocking advisories carry forward or are newly opened (A4, A5, A6). **Phase 13 is not closed by this feature.**

## What I ran (round 2)

| Command | Result |
|---|---|
| `dotnet build --no-incremental` (solution) | 0 warnings, 0 errors |
| `dotnet test tests/Orders.IntegrationTests --no-build` (**full suite**) | **84 passed, 0 failed, 0 skipped** — the fix round's `82 → 84` claim confirmed on my own run |
| `dotnet test tests/Orders.UnitTests --no-build` | **307 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Architecture.Tests --no-build` | **16 passed** — the NetArchTest suite run, not eyeballed |
| `dotnet test … --filter "CatalogReferenceListAcceptanceTests\|OrdersCreateAcceptanceTests\|NatsStockAvailabilityCheckerTests\|EfCoreOrderReferenceCatalogListTests"` | **26 passed** — the whole `NatsCollection` plus the adapter list tests, run together, for the schedule-dependency question |
| `docker ps` | MS-SQL 2022-CU26, NATS 2.14.5, Kafka, Mongo all up and healthy — the integration tests hit real containers |
| `git status --porcelain -- specs/shared/` | empty |
| `git status --porcelain -- tests/Orders.IntegrationTests/OrderPersistenceTestSupport.cs` | empty — the shared seed fixture was **not** widened, as round 1 required |

I did **not** re-run `./quality.sh` or the full 16-project solution pass; the fix round's run is the evidence for those, and the leader independently reports 1241/0/0. Every figure above is off my own run.

## Mutation probes I ran myself (round 2)

Each: applied to the real file, `dotnet build --no-incremental`, named test(s) run, restored from a `cp` backup taken before the first mutation, `cmp` byte-identical against that backup, the restored line re-read, `touch`ed, solution rebuilt `--no-incremental`, and confirming green runs recorded below.

| # | Family | Mutation | Outcome |
|---|---|---|---|
| Q1 | request-field corruption | `OrdersCreateResponder.cs:124` → `var includeDisabled = true;` | **KILLED.** `CatalogReferenceList_IncludeDisabledTrueVersusOmitted_…` — `Assert.DoesNotContain() Failure: Filter matched in collection ↓ (pos 2) … ProductPayload { Code = PROD-DISABLED, … Enabled = False }`. Matches the fix round's D1a message |
| Q2 | request-field corruption, mirror direction | `OrdersCreateResponder.cs:124` → `var includeDisabled = false;` | **KILLED.** Same test — `Assert.Single() Failure: The collection did not contain any matching items … Collection: [PROD-001 …, PROD-002 …]`. Matches D1b |
| Q3 | **my own, subtler than either armed direction** | `OrdersCreateResponder.cs:124` → `request.IncludeDisabled ?? true` — the wire flag still honoured, only `asyncapi.yaml`'s `default: false` inverted | **KILLED.** `Assert.DoesNotContain() Failure` — 4 passed / 1 failed in the class. This is the mutation that proves the *schema default* is guarded and not merely the two explicit spellings; neither armed direction covers it alone |
| Q4 | **payload corruption on a different field** | `OrdersCreateResponder.cs:167` → `ProductPayload(…, p.Price.Currency, true)` — the `enabled` flag hard-coded on the wire | **KILLED.** `Assert.False() Failure — Expected: False, Actual: True`. The new test guards the `enabled` payload field too, which nothing did before this round |
| Q5 | deletion | `EfCoreOrderReferenceCatalog.cs:58` → `.OrderBy(row => row.Product.Code)` removed | **KILLED.** `ListProductsAsync_ThreeRowsSeededOutOfOrder_ReturnsThemOrderedByCode` — `Assert.Equal() Failure: Collections differ — Expected: ["PROD-A","PROD-B","PROD-C"], Actual: ["PROD-A","PROD-C","PROD-B"]`. Note my unordered result differs from the fix round's (`["PROD-B","PROD-A","PROD-C"]`), which is the useful part: real MS-SQL's no-`ORDER BY` return order is genuinely non-deterministic here, so the assertion is not passing on a coincidence of insertion order |
| Q6 | **A1's own guard, which the fix round declined to arm** | `ListCatalogReferenceQueryHandler` grew a second constructor dependency (`IClock`, actually read in the body so `CS9113` did not mask it), and the six now-broken call sites in `ListCatalogReferenceQueryHandlerTests` were patched to compile | **KILLED.** `PlaceOrderCommandHandlerAndListCatalogReferenceQueryHandler_BothDeclareAConstructorDependencyOnIOrderReferenceCatalog` — `Assert.Equal() Failure: Collections differ — Expected: [typeof(IOrderReferenceCatalog)], Actual: [typeof(IOrderReferenceCatalog), typeof(IClock)]`. Round 1's `Assert.Contains` version could not have failed here |

**Restore evidence.** `cmp` byte-identical against backups for all four mutated files (`OrdersCreateResponder.cs`, `EfCoreOrderReferenceCatalog.cs`, `ListCatalogReferenceQuery.cs`, `ListCatalogReferenceQueryHandlerTests.cs`); the four load-bearing lines re-read individually (`:124`, `:167`, adapter `:58`, handler `:55`) and each is the delivered text; `git status --porcelain` byte-identical to its pre-probe listing (23 entries, same set); solution rebuilt `--no-incremental` after restore, then Orders.UnitTests 307, Architecture.Tests 16, catalog integration 13 — all green. I did not use `git diff` as restore evidence: most of these files are untracked, where it cannot fail.

## D1 — CLOSED, and it survives the harder question

The new test is `tests/Orders.IntegrationTests/CatalogReferenceListAcceptanceTests.cs:169`. It kills **four** distinct mutations of the seam (Q1–Q4), two of which the fix round did not run. The `enabled`-field kill (Q4) is a bonus the round-1 prescription did not ask for and got anyway.

**The schedule-dependency question, answered with runs rather than reasoning.** The test cannot have one:

- It seeds into **its own database** — `mssql.CreateFreshDatabaseAsync($"otc_catalog_accept5_{Guid.NewGuid():N}")` at line 171, migrated and seeded inside the test, exactly as the other four cases in the class each do with their own `accept1`/`accept2`/`accept3`/`accept4` GUID-suffixed names. The disabled row is unreachable from any other test by construction, so "does it clean up" is moot: there is nothing shared to clean.
- `OrderPersistenceTestSupport.SeedReferenceDataAsync` is **unmodified** (`git status` on that path is empty), so no other suite's fixture moved.
- Run alone: green. Run with the whole `NatsCollection` plus the MS-SQL list tests (26 tests): green. Run inside the **full 84-test `Orders.IntegrationTests` suite**: green, 0 failed, 0 skipped, 5 m 59 s. Three schedules, three greens.
- The one residual cross-talk risk in this class is structural and pre-existing, not D1's: every host in these suites subscribes to `catalog.reference.list` without a NATS queue group, so two concurrently-running hosts pointed at different databases could in principle answer each other's requests. `NatsCollection` and `SagaCollection` are both declared `[CollectionDefinition(…, DisableParallelization = true)]`, which is what prevents it, and that is the same protection `orders.create` has relied on since feature 15. No change owed by this feature; worth remembering when feature 25's Gateway adds a seventh subscriber.

## D2 — CLOSED, and I disproved the correction the same way I disproved the original

I opened #7's checkout rather than reading the fix round's account of it. Every claim in the corrected row is true, at the cited lines:

| Corrected row's claim | What #7's source actually shows |
|---|---|
| `app.module.ts:123-137` is an explicit `useExisting` alias between two tokens | **Exact.** Lines 123–126 are `provide: ORDER_REFERENCE_DATA, useFactory: (db) => new DrizzleOrderReferenceDataRepository(db), inject: [ORDERS_DB]`; 127–133 the alias comment; 134–137 `{ provide: CATALOG_REFERENCE_LIST, useExisting: ORDER_REFERENCE_DATA }`. The cited range is neither padded nor short |
| The comment warns why it is an alias and not a second factory | **Exact**, quoted correctly: *"an ALIAS (`useExisting`), not a second `useFactory` … this token resolves to the EXACT SAME instance … never a second repository that could read the four reference tables differently"* |
| `catalog-reference-list.port.ts:1-21` keeps two separate ports on one adapter | **True, and structurally confirmed rather than taken from the comment**: `order-reference-data.repository.ts:31` reads `export class DrizzleOrderReferenceDataRepository implements OrderReferenceDataPort, CatalogReferenceListPort`. Two ports, one class, one `OrdersDb`. The port header's stated reason for refusing a widened interface is quoted correctly, and lines 1–21 of a 38-line file is the header, as cited |
| #8 diverges by widening one port, and that divergence carries #7's predicted cost | **Stated correctly and not flattered.** The row records the divergence, and the longer-form correction records the cost — `FakeOrderReferenceCatalog` grew four methods place-order never calls — as *"exactly the cost #7's port header predicted widening would carry"*. The row also keeps the honest residual: *"Nothing stops a second, parallel registration … unless a test checks it"* |

The two source comments that restated the false claim are corrected: `src/Orders/Application/Queries/ListCatalogReferenceQuery.cs:28-54` and `tests/Orders.UnitTests/CatalogReferenceListPortReuseTests.cs:11-29`. Both now cite `app.module.ts:123-137` and `catalog-reference-list.port.ts:1-21` by file and line, name `useExisting` precisely, and state the divergence with its trade-off. The row's original wrong text is preserved inline in `progress/impl_orders_catalog_responder.md:111` rather than deleted, which is the right call — the correction is traceable, and #9 inherits both the answer and the mistake that produced it.

**This is the first ledger row in the repository to be checked against #7's source and found accurate.** The two before it (feature 24's, twice) were not.

## A1 and A2

- **A1 — closed and now proven.** `AssertConstructorParameterTypesEqual<T>` asserts the exact ordered parameter-type list for both handlers. The fix round declined to arm it, arguing it is a strictly stronger predicate over already-verified data. That argument is sound as far as it goes but is not what the on-disk rule asks for, so I armed it myself: **Q6**, above, kills it, and round 1's `Assert.Contains` version would have passed the same mutation. The claim is now evidenced; see A4 for the process point.
- **A2 — closed for products, and I confirmed the new test can fail (Q5).** It seeds `PROD-C`, `PROD-A`, `PROD-B` out of order into a fresh database, so insertion order cannot satisfy it. See A5 for what the guard does **not** cover.

## Advisories (none blocking)

- **A4 — a guard named in a ledger row was shipped unarmed.** The corrected row's Guard column names two tests; only one had ever been seen to fail. `CLAUDE.md` on disk is explicit: *"a ledger row's Guard column is itself a countable claim, and a countable claim is not done until it has been seen to fail"* and *"naming a guard in `tasks.md` creates the obligation to arm it; it does not discharge it."* The fix round's reasoning — that `Assert.Equal` is stronger than the already-armed `Assert.Contains` over the same data — is exactly the reasoning feature 19's decorative guard would have supported. Not blocking, because Q6 supplies the missing evidence and it passes; recorded so the next implementer arms what its ledger row names.
- **A5 — the "ordered by code" claim is guarded for one of four list methods, and I proved the other three are not.** `progress/impl_orders_catalog_responder.md:35-38` claims ordering for products, retailers, companies and (in the code) currencies. Deleting `.OrderBy` from `EfCoreOrderReferenceCatalog.cs:81`, `:98` and `:107` **simultaneously** left everything green: catalog integration 13/13 and Orders.UnitTests 307/307. Non-blocking — `asyncapi.yaml` requires no ordering and round 1 offered "drop or guard" — but the doc claim is now four-quarters written and one-quarter guarded, which is the shape this repository keeps paying for. Either narrow the claim to products or extend the test; a `[Theory]` over the four methods is the cheap version.
- **A6 — round 1's A3 stands.** The `message.Data is null` branch (`OrdersCreateResponder.cs:115-118`) still has no test; every wire request in the suite carries a payload. Cosmetic, unchanged, and `orders.create`'s equivalent branch is in the same position.

## Acceptance-bullet → test mapping (round 2 state)

`sdd: false`, so there are no `R<n>` ids and `specs/shared/test-matrix.md` correctly gains no row.

| Bullet | Test(s) | Verified |
|---|---|---|
| 1 — `GET /catalog/*` returns real data through the Gateway | `CatalogReferenceListAcceptanceTests`, now **5** cases over real NATS + real MS-SQL. `GET /catalog/*` itself remains feature 25's claim | Accepted substitute, as in round 1; the seam is now stronger than it was, because `includeDisabled` — the exact query parameter feature 25 will forward — is proven end to end |
| 2 — no new bounded context | `EfCoreOrderReferenceCatalogListTests`, now **8** cases against the existing `otc_orders` reference tables; `Architecture.Tests` 16/16 green on my own run | Verified |
| 3 — reuses the same lookup `PlaceOrderHandler` calls, exposed as a query | `CatalogReferenceListPortReuseTests` (2 cases, both now armed — P1 in round 1, Q6 here) + `ListCatalogReferenceQueryHandlerTests` (6 cases) | Verified in round 1 by tracing the registration and both method bodies; nothing in the fix round changed the handler's signature or the adapter's bodies — `ListCatalogReferenceQuery.cs`'s only edit was its XML doc, and the handler still declares exactly one constructor parameter |

## CHECKPOINTS.md walk (round 2)

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md`, `progress/history.md` exist
- [x] `.claude/agents/` holds the five agents
- [x] Every agent definition declares its model
- [x] `./init.sh` exits 0 — the leader's run this session; not re-run by me

**C2 — state coherent**
- [x] At most one feature `in_progress` — zero, after this approval
- [x] Every status in `rules.valid_status` (39 `done` → 40, 19 `pending`, 0 `in_review` after this edit)
- [x] Every `done` feature has passing tests
- [x] `progress/current.md` describes this session (leader-owned; its body still says `in_progress` for id 40 and is the leader's to refresh at this transition)
- [x] No `blocked` feature

**C3 — architecture**
- [x] No framework reference in any `Domain/` — `Architecture.Tests` 16/16, run
- [x] No cross-service DB access — the four reference tables are `otc_orders`' own
- [x] No shared runtime code beyond `SharedKernel`, `Contracts`, `Cqrs`
- [x] No `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `SharedKernel` still has zero `PackageReference`
- [x] No `decimal` in domain arithmetic — money crosses as `long` minor units (`ProductPayload.Price`)
- [x] Interaction correctly classified — read-only request/reply for form data is NATS-RPC, matching `asyncapi.yaml`'s `requestCatalogReferenceList`
- [x] No stray debug logging, no context-free TODOs; no probe residue from either round (`git diff` of the two tracked mutated files greps clean for `probe|TODO|HACK|= true;|= false;`, and its only deletions are the responder's rewritten XML doc and the `HandleAsync` → `HandleOrdersCreateAsync` rename)

**C4 — verification real**
- [x] `./quality.sh` passes — **implementer's run, not mine**; mine were Orders.UnitTests 307, Architecture.Tests 16, Orders.IntegrationTests 84 (full), plus six mutation cycles
- [x] Domain tests pure
- [x] Integration tests use real Testcontainers (MS-SQL + NATS), verified by running them against live containers
- [ ] **Coverage thresholds — not independently verified by me.** Unchanged from round 1; the repository-wide open item A7 (inert coverage gate, open since phase 12) is still open and is not this feature's to close
- [x] No Jest

**C5 — session close**
- [x] No suspicious untracked files — the 23-entry `git status` is this feature's own artefacts plus the leader's `CLAUDE.md`/`.superseded-rules`/`current.md`/`feature_list.json`
- [x] `progress/history.md` entry with effort record — appended with this approval
- [x] `feature_list.json` reflects true state (id 40 → `done`; the leader's id-56 acceptance edit left untouched, verified by reading the diff after my edit)
- [ ] Human told what was done / how to test manually — the leader's step after this verdict
- [x] Claude did not commit

**C6 — SDD** — not applicable (`sdd: false`). No `specs/orders_catalog_responder/` is required and none exists.

**C7 — reuse fidelity**
- [x] `specs/shared/` untouched — `git status --porcelain -- specs/shared/` empty
- [x] No amendment attempted by this feature
- [x] `R<n>` ids — none claimed, correctly
- [ ] n8n workflows / black-box API script — not applicable until the Gateway exists (feature 25)
- [x] Effort record — appended, below and in `progress/history.md`
- [ ] README benchmark section — the leader's, at phase close

## Effort record and the honest comparison

**#8:** 1 implementer pass + 1 fix round; 2 review rounds. Wall-clock from artefact mtimes (local, 2026-09-08), bracketed below by the leader's session file at **17:23:38** and the phase-12 close commit at **16:28**:

- **Implementation pass ≈17:30:32 → ≈17:45** — first production file (`IOrderReferenceCatalog.cs`) to the last pass-1 test artefact (`OrdersCreateErrorMapperTests.cs`, 17:35:30) plus its unmeasured verification tail (`quality.sh` + a 16-project solution run, which alone is several minutes). **≈20–25 min of writing, ≈30 min including verification.**
- **Review round 1 ≈17:45 → 18:10:40** — **≈25 min**, including four mutation cycles, the containers, and the trip into #7's checkout that produced D2.
- **Fix round 18:11 → 18:39:35** — **≈28 min**, D1 + D2 + A1 + A2 with two armed directions and a full re-verification.
- **Review round 2 ≈18:40 → ≈19:10** — **≈30 min**, six mutation cycles including a full 84-test integration run and a compile-broken-and-patched probe.
- **Total ≈1 h 45 min** across four passes.

**#7's counterpart** (`## orders_catalog_responder (id 40, phase 13)` in #7's `progress/history.md`): **1 implementer pass, approved first time — ≈21 min measured implementation plus an unmeasured live-E2E tail, and ≈35 min review. ≈56 min.**

**#8 is ≈1.9× #7 on this feature, and it did not beat #7 on passes.** The confound column, which is the point of recording this at all:

| Gap component | Size | #8-only process, or a real miss? |
|---|---|---|
| Review round 1's extra depth over #7's (containers, four mutations, opening #7's source) | ≈10 min | **#8-only process.** #7's own review record says it *"relied on the report's own live-verification narrative (not independently re-run)"* |
| The fix round + review round 2, attributable to **D1** | ≈30 min of the ≈58 | **A real miss, and the interesting one.** #7 had this guard: `apps/orders/src/presentation/catalog-reference-list.controller.spec.ts:33` asserts the controller defaults an omitted body to `includeDisabled: false`. #8 ported the mechanism and dropped the guard. **Would #7's standard have caught it? #7 never had to — its implementer wrote the guard in the first pass.** So this is not review inflation; it is a translation loss that cost #8 a round, and it belongs in the "not faster, and our fault" column |
| The fix round + review round 2, attributable to **D2** | ≈28 min of the ≈58 | **#8-only process, entirely.** #7 had no ledger and asked none of these questions. The defect was prose with no code or test consequence; the row's guard was already sound |

**Did the ledger obligation earn its place on its first port-bound outing?** **Yes, but not for the reason the round appears to show, and #9 should have the precise version.** What the obligation caught was a false sentence that no test exercises and that would have shipped invisibly — worth catching, since it is exactly the half #9 inherits, and it produced a binding `CLAUDE.md` convention that generalises beyond this feature. But it cost ≈28 min and changed no behaviour. The stronger argument for keeping it is a side effect nobody designed: **the rule forces the implementer to open #7's source for this feature — and #7's source is where D1's missing guard was sitting.** Had the ledger row been written from #7's checkout in the first pass, as the rule now requires, the same reading that produced the correct `useExisting` citation would have walked past `catalog-reference-list.controller.spec.ts` and its `includeDisabled: false` assertion. The obligation's real dividend is not the row; it is that writing the row honestly is the cheapest way to notice what the original guarded and the port did not. That is the version worth carrying to #9.

## Phase status

**Phase 13 is not closed by this feature.** Ids **41** (`orders_cancel_responder`), **25** (`gateway_rest_auth`), **26** (`gateway_sse_push`), **56** (`composition_root_env_reads_are_unguarded`) and **60** (`envelope_fixture_collisions_defeat_provenance_assertions`) all remain `pending`. Two items to carry into id 25's brief: the un-queue-grouped `catalog.reference.list` subscription noted under D1, and A5's three-quarters-unguarded ordering claim, which the Gateway's list endpoints are the first consumer of.
