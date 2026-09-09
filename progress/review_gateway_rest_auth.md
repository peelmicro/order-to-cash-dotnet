# Review — feature 25 `gateway_rest_auth`

**Verdict: REJECTED.** Feature set back to `in_progress` in `feature_list.json` (id 25, line 381 — single-line edit, `in_review` → `in_progress`; no other line of that file touched, and `git checkout --` was never run on it).

The build is substantial, honest and in most places stronger than #7's. Three defects are blocking, and the first is, structurally, **the same defect #7's own `gateway_rest_auth` was rejected for** — proved here by mutation, not by argument.

## What I ran (independent verification, not a re-run of the world)

| Command | Result |
|---|---|
| `dotnet test tests/Gateway.UnitTests` | 115 passed, 0 failed |
| `dotnet test tests/Gateway.IntegrationTests` (real NATS / Mongo / MS-SQL / Kafka containers) | 26 passed, 0 failed |
| `dotnet test tests/Architecture.Tests` | 16 passed, 0 failed |
| 6 mutation probes (below), each `dotnet build --no-incremental` then the named suites | 3 killed, **3 survived** |

I did **not** re-run the full 18-project / 1425-test suite. The implementer's claim about it is not what is under test here; the claims under test are the three acceptance bullets, the ledger, the #7-guard enumeration and the seam. All four mutated source files were restored from `cp` backups and verified byte-identical with `cmp`; the confirming green runs above were made after a forced `--no-incremental` rebuild.

## Mutation probes

| # | Mutation | Suites run | Outcome |
|---|---|---|---|
| P1 | `GatewaySubjects`: `orders.cancel`→`orders.cancelled`, `catalog.reference.list`→`catalog.references.list`, `billing.payment.register`→`billing.payments.register` | unit + integration | **SURVIVED — 115/115 and 26/26 green** |
| P2 | `.AllowAnonymous()` added to `GET /orders` | unit + integration | **SURVIVED — 115/115 and 26/26 green** |
| P3 | `OrderReadModelMapper`: transpose `InitialAmount` / `InitialDiscount` in the `OrderSummary` totals | unit | **SURVIVED — 115/115 green** |
| P4 | `GatewayHost`: add `app.MapGet("/orders/not-in-the-spec", …)` | unit | KILLED — `MappedEndpoints_MatchOpenApiYamlExactly_ForEveryPathThisFeatureBuilds` |
| P5 | `RpcErrorClassification`: `UNAVAILABLE` Problem code → `UPSTREAM_TIMEOUT` | unit | KILLED — `Classify_MapsEveryGenericRpcErrorCode_…(rpcCode: "UNAVAILABLE")` and `Classify_TimeoutAndUnavailable_MapToTheSameStatusButDifferentProblemCodes` |
| P6 | (implementer's own #1) remove `app.MapCreditsEndpoints()` | — | accepted as recorded; P4 is its opposite direction |

---

## Blocking defects

### D1 — The Gateway's eight RPC subject constants are unguarded. This is #7's rejection defect, reproduced.

**File:** `src/Gateway/Application/Rpc/GatewaySubjects.cs:13-20`. **Evidence:** probe P1 — three of the eight constants corrupted, **141 tests green**.

Every other service in this repository asserts its subject constants against `specs/shared/asyncapi.yaml`'s channel `address:` lines. Enumerated:

```
$ ls tests/*/[A-Za-z]*SubjectsTests.cs tests/Orders.UnitTests/RpcSubjectsTests.cs
tests/Billing.UnitTests/CreditSubjectsTests.cs
tests/Billing.UnitTests/InvoiceSubjectsTests.cs
tests/Fulfillment.UnitTests/StockSubjectsTests.cs
tests/Orders.UnitTests/RpcSubjectsTests.cs
tests/Orders.UnitTests/SagaRpcSubjectsTests.cs
$ grep -rn "asyncapi\|AsyncApi" tests/Gateway.UnitTests/ tests/Gateway.IntegrationTests/ --include=*.cs
tests/Gateway.UnitTests/CancelOrderCommandHandlerTests.cs:14:    /// (asyncapi.yaml <c>rpcCorrelationId</c>: "the order id when the
```

One classification line: the single hit is a doc-comment, not an assertion. **The Gateway is the only service with no asyncapi-derived subject or payload test — and it is the only service that calls all eight subjects.**

Every Gateway test that names a subject names it as `GatewaySubjects.X` on *both* sides (`PlaceOrderCommandHandlerTests.cs:33`, `ReadOnlyQueryHandlerTests.cs:22,38,54,83`, `OrdersHttpTests.cs:86`), so the assertion is `X == X` and cannot fail. Only `fulfillment.stock.list` and `fulfillment.stock.replenish` are proved against a live responder (`FulfillmentStockEndToEndTests.cs` — genuinely real, `FulfillmentHost.CreateBuilder` verbatim, real MS-SQL/NATS/Kafka; credit where due). The other six — `orders.create`, `orders.cancel`, `catalog.reference.list`, `billing.credit.list`, `billing.invoice.list`, `billing.payment.register` — are tested only against a stub the implementer wrote, which is the brief's stated defect shape verbatim.

Mitigating: I mechanically diffed all 26 payload records in `GatewayRpcPayloads.cs` against their production counterparts (`OrdersCreatePayloads`, `OrdersCancelPayloads`, `CatalogReferenceListPayloads`, `StockRpcPayloads`, `CreditRpcPayloads`, `InvoiceRpcPayloads`) — parameter names, order and arity match on every one. The transcription is currently correct. It is simply unguarded, and all six responders exist in this repository and are bootable, so the guards are cheap.

**Required:** a `GatewaySubjectsTests.cs` in the shape of `tests/Fulfillment.UnitTests/StockSubjectsTests.cs` (all eight, read from `asyncapi.yaml` as text), and payload-record-vs-asyncapi tests in the shape of `tests/Billing.UnitTests/CreditRpcPayloadTests.cs` (reflection over the record, not a hand-typed key list — see backlog id 64). At least one further real-responder end-to-end test would close the seam properly; `billing.invoice.list` + `billing.payment.register` is the natural pair, since it is also the endpoint D5 leaves unproven.

### D2 — The anonymous-route sweep derives its candidate set from the metadata it is testing, so it cannot catch a route wrongly made public.

**File:** `tests/Gateway.IntegrationTests/DocsAndAnonymousRouteHttpTests.cs:46-51`. **Evidence:** probe P2 — `GET /orders` made anonymous, **141 tests green**, and no other test asserts that `/orders` requires a token.

The sweep filters to endpoints `.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)`. Marking a route `.AllowAnonymous()` removes it from the candidate set rather than failing the test.

#7's F8 does the opposite and would have caught it — `apps/gateway/src/auth.integration.spec.ts:68,79`:

```js
const PUBLIC_ROUTES = new Set(['POST /auth/login', 'GET /health/live', 'GET /health/ready']);
const protectedRoutes = routes.filter((route) => !PUBLIC_ROUTES.has(`${route.method} ${route.path}`));
```

The public set is a **literal**, and the protected set is derived by subtraction from the full route list — so a wrongly-public route stays in the loop and answers 200 where 401 is asserted. #7 even guards the escape hatch (`expect(protectedRoutes.some((route) => route.path === '/docs')).toBe(false)`).

The impl report classifies `auth.integration.spec.ts` as **PORTED** and names this case; the weakening is not noted. This is the same shape as both phase-13 rejections: a guard #7 had, dropped in translation.

**Required:** hard-code the two public routes this feature builds (`POST /auth/login`, `GET /docs`) and derive the protected set by subtraction from *all* mapped routes, plus an assertion that the `AllowAnonymous` set is exactly that pair.

### D3 — The JWT secret, issuer and lifetime are not configurable. #7 reads all three from the environment.

**Files:** `src/Gateway/Infrastructure/Auth/JwtOptions.cs:5-9` (hard-coded `"otc_dev_jwt_secret_change_me"`, `3600`, `"order-to-cash"`), `src/Gateway/Program.cs:9-14` (never sets `options.Jwt`).

```
$ grep -rn "JWT_SECRET\|JWT_ISSUER\|JWT_EXPIRES" . --include=*.cs --include=*.yml --include=*.example --include=*.md --include=*.sh
(no hits, repository-wide)
```

One classification line: zero hits — the variables exist nowhere in #8, not in code, not in `.env.example`, not in `docker-compose.infra.yml`.

#7 has them: `apps/gateway/src/infrastructure/auth/jwt.config.ts` —

```ts
export function loadJwtConfig(env: NodeJS.ProcessEnv = process.env): JwtConfig {
  return {
    secret: env.JWT_SECRET ?? 'otc_dev_jwt_secret_change_me',
    ...
```

and `.env.example:339-342` declares `JWT_SECRET` / `JWT_EXPIRES_IN` / `JWT_ISSUER`, with `:418-419` warning that production must set a real 32+ character secret.

This is not backlog id 56 (a *guard* for env reads, correctly deferred). It is the absence of the env read itself, and it is the odd one out in this very feature: `GatewayMongoOptions.FromEnvironment()`, `LoginThrottleOptions.FromEnvironment()` and `OperatorIdentityLoader.FromEnvironment()` all read the environment. It reads as an oversight, and it is not listed under "What was NOT done". The Gateway's JWT signing key is currently a compile-time constant.

**Required:** a `JwtOptions.FromEnvironment()` in the shape of the three siblings, wired in `Program.cs`, the three variables added to `.env.example` and `docker-compose.infra.yml`, and a `JwtOptionsTests` in the shape of `LoginThrottleOptionsTests`.

### D4 — The #7-test enumeration missed the one in-scope file, and two of its group counts are wrong.

**File:** `progress/impl_gateway_rest_auth.md:110-142`. The enumeration claims "**44 files.** One line per file below". 44 is correct; "one line per file" is not.

```
$ R=progress/impl_gateway_rest_auth.md
$ cd ../order-to-cash-nestjs && find apps/gateway -iname "*.spec.ts" | sed 's|apps/gateway/src/||' | sort \
    | while read f; do grep -qF "$(basename "$f" .spec.ts)" "$R" || echo "UNCLASSIFIED: $f"; done
```

19 hits, classified one line each:

- `application/commands/{login,place-order,register-payment,replenish-stock}` (4) and `application/queries/{get-current-user,get-order,list-catalog,list-credits,list-invoices,list-orders,list-stock}` (7) — **accounted** by the group row, but the row says "`application/queries/*.spec.ts` (6 files)"; there are **7**.
- `application/stream-hub`, `domain/sse/cursor`, `domain/sse/replay-buffer`, `stream.integration`, `stream-projector-e2e.integration` (5) — **accounted** by the group row, but the row says "(3 files)"; there are **5**.
- `infrastructure/messaging/nats-stream-signal.adapter`, `infrastructure/messaging/nats-stream-signal-log-trace-id` (2) — **unaccounted**; they match neither `stream*.spec.ts` nor `domain/sse/*.spec.ts`. Immaterial (feature 26's scope) but they are unclassified lines.
- **`orders.integration.spec.ts` — unaccounted, and squarely in this feature's scope.** 240 lines, 11 cases, real NATS + real MongoDB.

That last one is the material miss, and it is not merely bookkeeping — four of its guards have no #8 equivalent at any level:

| #7 case (`orders.integration.spec.ts`) | #8 equivalent |
|---|---|
| `:211` `R27/R28 — POST /orders/{id}/cancel translates to orders.cancel … answers 202 with compensationPlanned` | **none over HTTP** — `CancelOrderCommandHandlerTests` is a unit test of the handler; the endpoint's status code, body and header shaping are unproven |
| `:200` `POST /orders rejects a malformed request body with 400 VALIDATION_FAILED before any RPC call` | **none** — `OrdersEndpoints.PlaceOrderAsync:32-34` throws on an empty `lines`, nothing tests it and nothing proves no RPC was issued |
| `:129` `R53/R54 — GET /orders excludes a placeholder document from the list` | at unit level only (`OrderReadModelMapperTests`, `ListOrdersQueryHandlerTests`); `GET /orders` has **no HTTP-level test at all** |
| `:117` `R54 — GET /orders/{id} returns the projected document once one exists in MongoDB` | adapter-level only (`MongoOrderReadModelIntegrationTests`); no HTTP → real-Mongo path |

The enumeration is the artefact `CLAUDE.md` added specifically because two phase-13 features dropped guards #7 had. Here it ran, and the file it missed is the one holding the dropped guards. **Required:** re-run the enumeration with the command above, classify all 44, correct the two counts, and port or explicitly decline each of the four cases with a reason.

---

## Non-blocking defects

### D5 — A payload-corruption survivor on the REST wire: `initialAmount` and `initialDiscount` are never asserted.

`src/Gateway/Domain/Projection/OrderReadModelMapper.cs:76`. Probe P3 transposed the two arguments; 115/115 green. The cause is the one `CLAUDE.md` names: the fixture (`OrderReadModelMapperTests.cs:18`) uses `new OrderReadModelTotals(124250, 0, 124250)`, so `initialDiscount` is `0` and only `TotalAmount` is asserted (`:32`). `ListOrdersQueryHandlerTests.cs:15` uses `(100, 0, 100)` — same shape. A corruption probe bites only where the test supplied a distinguishing expected value; here it supplied none. openapi.yaml's `OrderTotals` requires all three fields. Fix: distinct non-zero values and an assertion on each of the three.

### D6 — Four of ten `ProblemJsonMiddleware.Classify` branches are never exercised over the wire; one is never exercised at all.

The brief asked for this enumeration after the live 404-vs-500 bug. Walking `src/Gateway/Presentation/Problem/ProblemJsonMiddleware.cs:82-134`:

| Branch | Unit | End-to-end |
|---|---|---|
| `RpcCallError` | yes | yes (`OrdersHttpTests` 409 + 503) |
| `InvalidCredentialsError` | yes | yes (`Login_WithTheWrongPassword_Returns401ProblemJson`) |
| `InvalidTokenError` | yes | partly — only the *missing*-token flavour (`Me_WithoutABearerToken_Returns401`); no HTTP test sends a tampered or expired token |
| `InvoiceNotFoundError` | yes | **no** |
| `GatewayNotFoundError` | yes | yes — the branch the live bug was in |
| `InvoiceScanBudgetExceededError` | yes | **no** |
| `OrderNotYetProjectedError` | yes | **no** |
| `UnknownOperatorError` | **no** | **no** |
| `GatewayRequestValidationError` | yes | yes (`GetOrder_ForAMalformedId_Answers400`) |
| `default` | yes | **no** |

The three unproven `Invoice*`/`OrderNotYetProjected` branches all reach the wire only through `POST /invoices/{id}/payments` — an endpoint with **no HTTP-level test whatsoever**. That is precisely the configuration that produced the live bug: correct domain classification, unproven wire translation. The implementer's own reading of the bug was right; the conclusion was not carried to its siblings. (`UnknownOperatorError` is behaviourally identical to `default`, so its absence is cosmetic — but it is untested.)

More broadly, only 7 of the 14 built paths have any HTTP-level test beyond the 401 sweep. `GET /orders`, `POST /orders/{id}/cancel`, `GET /invoices`, `POST /invoices/{id}/payments`, `GET /credits` and the three `/catalog/*` paths are exercised over HTTP only by `EveryRegisteredRouteExceptTheTwoDocumentedPublicOnes_RejectsAnAnonymousRequestWith401` — which proves they reject anonymous callers and nothing else about their behaviour.

### D7 — Ledger row 6 is an engine claim probed in neither direction.

Row 6 asserts "ASP.NET Core Minimal API routing is **not** order-sensitive: a literal route segment always outranks a route-parameter segment at the same position, regardless of `Map*` call order". The claim is correct, but `CLAUDE.md`'s ledger section (as amended after feature 24) requires that a row asserting how an engine behaves either be probed both ways or state which way it was run. This row records neither, has no guard, and its whole purpose is to save feature 26 from re-deriving it — which is exactly the "confidently inherited rather than re-derived" risk that rule was written for. One `Map*`-order-reversed test would settle it.

### D8 — `InvoiceViewPayload` carries a field no responder sends (advisory only).

`src/Gateway/Application/Rpc/GatewayRpcPayloads.cs` declares `InvoiceViewPayload` with 13 members including `IReadOnlyList<InvoiceLinePayload>? Lines`; Billing's own record (`src/Billing/Infrastructure/Messaging/Rpc/InvoiceRpcPayloads.cs:58-70`) has 12 and no `lines`, matching `asyncapi.yaml`'s `InvoiceView`. openapi.yaml's `Invoice` (`:1752`) *does* declare an optional `lines`, so the extra member is contract-legal and deserialises to `null` (omitted on the wire). Not a defect; worth a comment saying so, since it is the one field of the 26 records that does not round-trip.

### D9 — The JWT ledger row does not name the properties `jsonwebtoken` supplied (advisory).

Row 3 is the highest-risk row in the build and its citation half is accurate (`apps/gateway/src/infrastructure/auth/jwt-token.adapter.ts:8,35` — a plain `jwt.sign`/`jwt.verify` pair, no `algorithms` allowlist, so the library's own defaults are what reject `alg: none`). The row does not enumerate what the library supplied: expiry enforcement, issuer enforcement, structural rejection, and algorithm pinning.

I probed the implementation by reading `JwtTokenService.Verify` (`src/Gateway/Infrastructure/Auth/JwtTokenService.cs:52-125`): the header is **never parsed**, `Sign` is unconditionally `HMACSHA256`, and a forged `alg: none` token's empty signature fails the length check at `:81` before `FixedTimeEquals`. So algorithm confusion is structurally impossible — the property is supplied, and more strongly than #7 supplies it. It is simply neither stated in the ledger nor pinned by a test. `JwtTokenServiceTests` covers round-trip, expiry, tampered signature, wrong secret, wrong issuer and four malformed shapes; add an `alg: none` / empty-signature case and a tampered-**payload** case (`sub` → `admin`) and the row's guard column becomes complete.

---

## What is genuinely good, and should survive the fix round

- **Acceptance bullet 1 is met and falsifiable in both directions.** `OpenApiContractTests` derives both sides from real artefacts — the embedded `openapi.yaml` bytes and `((IEndpointRouteBuilder)app).DataSources`. The implementer armed the "declared but not mapped" direction; I armed the opposite (P4) and it fired. This is not backlog id 64's shape.
- **Acceptance bullet 2 is met per case.** Twelve `RpcError` codes each have their own `[InlineData]` row, `TIMEOUT` and `UNAVAILABLE` are distinguished by a dedicated case, and P5 killed on both. The `NatsRpcClientIntegrationTests` distinction between `NatsNoRespondersException` and `NatsNoReplyException` over a real broker is a real improvement on #7's mocked adapter test.
- **Acceptance bullet 3 is met as an enumeration.** `WriteDatabaseAbsenceTests` runs four independent enumerations (two NetArchTest assembly scans, a `.csproj` text scan, a source `MSSQL_` scan) and the implementer armed two of them. `Architecture.Tests` passes 16/16 with `src/Gateway` in `DomainAssemblies.cs:33`, so domain purity is verified by running the suite, not by eye.
- **`FulfillmentStockEndToEndTests` is the real thing** — `FulfillmentHost.CreateBuilder` verbatim against real MS-SQL, NATS and Kafka, driven through real Kestrel, with the replenish mutation read back through an independent `DbContext` rather than trusted from a 200. It is the model the other five subjects need.
- **Every ledger citation into #7 checks out.** I verified all six cited #7 files exist and say what the rows claim (`DETAIL_CODE_OVERRIDES` really does key on `PAYMENT_REFERENCE_CONFLICT` at `rpc-error-mapping.ts:41`; `auth.controller.ts:28` really is `@UseGuards(ThrottlerGuard)` route-scoped). The history half — the half the last two features got wrong — is right here. The gaps are in guards (D7, D9), which is the displacement `CLAUDE.md` predicts.
- **Row 7 is a real find**, not a translation: #7's `cancel-order.command.ts` hard-codes `cancellationReason`, #8 passes the responder's own nullable value through, and there is a named guard for it.
- **The live-bug disclosure is exemplary** and its self-diagnosis is correct. D6 asks only that the diagnosis be applied to the branches that did not get lucky.

## `specs/shared/test-matrix.md` edit — judged on content, approved

The leader's correction is right: the edit is obligatory, not a fork. `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` reports **only** `test-matrix.md` differing, so C7's byte-identity box holds. The content is sound and the arithmetic is internally consistent (`projector_read_model` 4→5 DONE, gateway edge 0→1, total 49→51, TODO 10→8). R55 is honestly split — the 202-vs-404 half claimed DONE, the SSE and web halves explicitly left owed to feature 26 and `apps/web`. R54's gateway half is claimed via `MongoOrderReadModelIntegrationTests` + `WriteDatabaseAbsenceTests`, which is a fair reading of "served from the read model only", though the matrix's own prescribed case ("answers order list **and detail** queries with every write model disconnected") is closer to an HTTP-level test than to an adapter-level one — see D6. No row needs reverting.

## `R<n>` → test mapping verified

| R | Claimed test | Verified |
|---|---|---|
| **R54** (gateway half) | `MongoOrderReadModelIntegrationTests.cs` (real `mongo:8.3.8`), `WriteDatabaseAbsenceTests.cs` ×4 | Yes — 4 absence cases and the Mongo adapter cases exist and pass; 2 of the 4 armed by the implementer. `FulfillmentStockEndToEndTests` correctly covers `GET /stock` as openapi's documented exception. Caveat in D6: no HTTP → Mongo path. |
| **R55** (202-vs-404 half) | `OrdersHttpTests.GetOrder_RightAfterPlacingIt_Answers202ProjectionPending_NotAFalse404`, `…ForAnIdThisGatewayNeverIssued_Answers404` | Yes — both exist, run over real Kestrel, and the underlying `IssuedOrderWindow` wiring is armed at both ends (implementer's probes 6 and 7). |
| **R63** | `AuthAndRateLimitHttpTests` ×3 | Yes — all three cases exist, over real Kestrel; implementer's probe 5 killed on removal of `.RequireRateLimiting`. |

No `R<n>` id is claimed that is not satisfied.

## CHECKPOINTS walk

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds the five agents
- [x] every agent definition declares its model
- [x] `./init.sh` exits 0 (leader-verified before dispatch)

**C2 — state coherent**
- [x] exactly one feature `in_progress` (id 25, after this rejection)
- [x] every status in `rules.valid_status`
- [x] every `done` feature has passing tests
- [x] `progress/current.md` describes the active session
- [x] no `blocked` feature

**C3 — architecture**
- [x] no banned framework reference in any `Domain/` folder — **verified by running** `tests/Architecture.Tests`, 16/16, with `src/Gateway` included at `DomainAssemblies.cs:33`
- [x] no cross-service DB access — the Gateway reads only the projector's `order_timeline` collection (R54's own instruction) and reaches every write model by RPC; `WriteDatabaseAbsenceTests` enumerates the absence
- [x] no shared runtime code beyond `SharedKernel` / `Contracts` / `Cqrs` — `Gateway.csproj` references exactly those three
- [x] no `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `src/SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic — money is `long` throughout `GatewayRpcPayloads.cs` and `OrderTotalsView`
- [x] every interaction classifiable as Kafka-fact or NATS-RPC — the Gateway publishes no facts and consumes none; all eight interactions are NATS request-reply, correctly
- [x] no stray debug logging, no context-free TODOs

**C4 — verification real**
- [ ] `./quality.sh` passes — **not re-run by me** (the implementer reports 1425/18/0-failed; that claim is not what this review tests). Reported here as unverified, not as failing.
- [x] domain tests pure — `RpcErrorClassifierTests`, `IssuedOrderWindowTests`, `OrderReadModelMapperTests`, `OperatorAuthenticatorTests` reference no framework
- [x] integration tests use Testcontainers against real MsSql / Kafka / NATS / MongoDB — confirmed by reading the fixtures and by 26/26 passing with containers up
- [ ] coverage thresholds — not independently checked this round
- [x] no Jest anywhere

**C5 — session closed cleanly**
- [x] no suspicious untracked files; all four files I mutated restored and `cmp`-verified
- [ ] `progress/history.md` entry with effort record — **correctly absent**: the feature is rejected, so no closing entry is owed yet
- [x] `feature_list.json` reflects true state (id 25 → `in_progress`)
- [ ] human told what was done and how to test manually — leader's step
- [x] Claude did not commit

**C6 — SDD** — not applicable, `sdd: false`.

**C7 — reuse fidelity**
- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — verified with a real `diff -rq` against the #7 checkout
- [x] no silent fork; the `test-matrix.md` edit is the obligatory Status-column update
- [x] the `R<n>` ids are #7's and the realisations genuinely satisfy them (table above)
- [ ] `n8n/workflows/*.json` fire green against the .NET Gateway — **not exercised**. The Gateway is now the first .NET target these workflows could run against; this is the sharpest available parity test and nothing in this feature attempts it. Not a blocker for id 25 (no acceptance bullet names it), but it should be scheduled explicitly rather than drifting.
- [ ] black-box API script proves the same saga steps as #7 — not exercised this feature
- [ ] effort records complete — pending, see C5
- [ ] README benchmark section — pending

## What must change before re-review

1. **D1** — `GatewaySubjectsTests.cs` covering all eight subjects against `asyncapi.yaml` (shape: `tests/Fulfillment.UnitTests/StockSubjectsTests.cs`), plus reflection-over-the-record payload tests (shape: `tests/Billing.UnitTests/CreditRpcPayloadTests.cs:29-35`). **Armed:** corrupt one subject constant and one payload property name; record both verbatim failures.
2. **D2** — rewrite the F8 sweep so the public set is a literal and the protected set is derived by subtraction, plus an assertion that the `AllowAnonymous` set is exactly `{POST /auth/login, GET /docs}`. **Armed:** re-run probe P2 and show it now fails.
3. **D3** — `JwtOptions.FromEnvironment()` reading `JWT_SECRET` / `JWT_EXPIRES_IN` / `JWT_ISSUER` with #7's defaults, wired in `Program.cs`, the variables added to `.env.example` and `docker-compose.infra.yml`, and a `JwtOptionsTests` in the shape of `LoginThrottleOptionsTests`.
4. **D4** — re-run the enumeration with the command in D4, classify all 44 with one line each, correct the two group counts, and for each of the four `orders.integration.spec.ts` guards either port it or decline it in writing with a reason. At minimum, `POST /orders/{id}/cancel` needs an HTTP-level test.
5. **D5** — distinct non-zero totals in the mapper fixtures and an assertion on each of `initialAmount`, `initialDiscount`, `totalAmount`. **Armed:** re-run probe P3.
6. **D6** — an HTTP-level test for `POST /invoices/{id}/payments` covering `InvoiceNotFoundError` (404), `InvoiceScanBudgetExceededError` (503 `SCAN_BUDGET_EXCEEDED`) and `OrderNotYetProjectedError` (503 `UPSTREAM_UNAVAILABLE`); an HTTP test sending a tampered bearer token; and a `Classify` unit case for `UnknownOperatorError`.
7. **D7** — probe the ASP.NET route-precedence claim, or state in the row which way it was probed.
8. **D9** — an `alg: none` / empty-signature case and a tampered-payload case in `JwtTokenServiceTests`, and a ledger row 3 that names the four properties `jsonwebtoken` supplied.

D8 is a comment, not a change.

## Effort

No effort record is appended to `progress/history.md` — the feature is not closed. When it is, the comparison against #7's counterpart (**1 implementation session + 3 review passes + 2 fix passes, rejected twice, ≈2 h 45 min**, its fix pass parallelised across three agents on disjoint directories) should be recorded with the confound stated plainly: **#8 is now also on its second pass, rejected once, for a defect in the same class #7 was first rejected for.** The honest reading is that the ledger and the guard-enumeration rules changed *which* review pass found the seam, not whether a fix round was needed. That is a result worth recording precisely because it is not a win.

---

**Phase 13 is not closed.** Ids **26** (`gateway_sse_push`), **56**, **60**, **61**, **63** and **64** remain, in addition to id 25's fix round.

---

# Review round 2 — feature 25 `gateway_rest_auth`

**Verdict: APPROVED.** Feature set `in_review` → `done` in `feature_list.json` (id 25, line 381 — single-line edit; no other line of that file touched, `git checkout --` never run on it). Round 1 above is closed and unamended.

All eight round-1 items are addressed, and I killed every one of them with my own mutations rather than accepting the implementer's arming table. Two residues are recorded below as non-blocking: one live payload-corruption survivor of the D5 family in the sibling method (`ToOrderDetail`), and a ledger **table** that is stale relative to its own corrections. Neither blocks; both are named precisely so they can be scheduled rather than lost.

## What I ran (independent probes, not a re-run of the world)

| Command | Result |
|---|---|
| `dotnet test tests/Gateway.UnitTests` | **161 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Gateway.IntegrationTests` (real NATS / Mongo / MS-SQL / Kafka containers) | **36 passed, 0 failed, 0 skipped** |
| `dotnet test tests/Architecture.Tests` | **16 passed, 0 failed** |
| `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` | only `test-matrix.md` differs (round 1's approved edit) — untouched this round |
| 9 mutation probes, each `dotnet build --no-incremental` then the named suites | **8 killed, 1 SURVIVED** |
| `./init.sh` | exit 0, `environment and state are coherent` |

I did **not** re-run the full 18-project suite. The implementer's `1481 / 18 projects / 0 failed` claim is not what this round tests; what I can confirm from my own runs is its arithmetic — Gateway went **115 → 161** unit and **26 → 36** integration, a delta of **+56**, which is exactly the 56 new tests claimed and exactly `1481 − 1425`. Architecture is unchanged at 16. Coverage thresholds were not independently re-checked this round.

**Scope confirmation, as a search result rather than a reading:**

```
$ find src tests -name "*.cs" -newermt "2026-09-09 07:05" -not -path "*/obj/*" -not -path "*/bin/*" | sort
```
25 hits, every one under `src/Gateway/`, `tests/Gateway.UnitTests/` or `tests/Gateway.IntegrationTests/`. Zero hits elsewhere. The `src/Orders/`, `tests/Orders.*` modifications visible in `git status` all predate 07:05 and belong to ids 40/41; this round did not touch them. `Directory.Packages.props` has a **zero diff** — no new NuGet package, confirming the leader's report at the only place central package management could record one.

## Mutation probes

| # | Mutation | Suites run | Outcome |
|---|---|---|---|
| R1 | `GatewaySubjects`: three constants corrupted, *different ones from the implementer's* — `catalog.reference.list`→`catalog.references.list`, `fulfillment.stock.list`→`fulfillment.stocks.list`, `billing.payment.register`→`billing.payments.register` | unit | **KILLED** ×3 — `GatewaySubjectsTests.GatewaySubjects_EqualTheAsyncApiChannelAddress(channelKey: "catalogReferenceList" \| "stockList" \| "paymentRegister")` |
| R2 | `GatewayRpcPayloads`: two property renames, again different ones — `StockViewPayload.LowStockThreshold`→`LowStockLimit`, `CreditViewPayload.CreditLimit`→`CreditCap` | unit | **KILLED** ×2 — `GatewayPayload_CarriesExactlyThePropertyNamesAsyncApiDeclares_…(schemaName: "StockView" \| "CreditView")` |
| R3 | Round 1's probe **P2** re-run verbatim: `.AllowAnonymous()` on `GET /orders` | integration | **KILLED** — `EveryRegisteredRouteExceptTheTwoDocumentedPublicOnes_RejectsAnAnonymousRequestWith401`, `Assert.Equal() Failure: HashSets differ / Expected: ["POST /auth/login", "GET /docs"] / Actual: ["POST /auth/login", "GET /orders", "GET /docs"]` |
| R4 | R3 **plus** the metadata-set assertion neutralised (`Assert.Equal(publicRoutes, anonymousRoutes)` → `Assert.NotNull(anonymousRoutes)`) — isolates the subtraction loop | integration | **KILLED by the loop alone** — `GET /orders was expected to reject an anonymous request with 401, but answered 500.` |
| R5 | `JwtOptions.FromEnvironment`: the entire `JWT_ISSUER` read deleted | unit | **KILLED** — `JwtOptionsTests.FromEnvironment_ReadsAllThreeVariables_WhenSet`, `Expected: "otc-test-issuer" / Actual: "order-to-cash"` |
| R6 | Round 1's probe **P3** re-run: transpose `InitialAmount`/`InitialDiscount` in `OrderReadModelMapper.ToOrderSummary` | unit | **KILLED** — `ToOrderSummary_ReturnsARow_ForAFullyProjectedDocument`, `Expected: 124950 / Actual: 700` |
| **R7** | **The same transposition in the sibling `OrderReadModelMapper.ToOrderDetail` only** | unit **+** integration | **SURVIVED — 161/161 and 36/36 green** |
| R8 | `JwtTokenService.Verify`: honour the header's `alg` — skip signature verification when `alg == "none"` (classic algorithm confusion) | unit | **KILLED** — `Verify_Throws_ForAnAlgNoneForgedToken_WithAnEmptySignatureSegment` |
| R9 | `ProblemJsonMiddleware.Classify`: **swap** the two 503 problem codes between `InvoiceScanBudgetExceededError` and `OrderNotYetProjectedError` (a payload-family probe, not a deletion) | integration | **KILLED** ×2 — `RegisterPayment_WhenTheInvoiceScanBudgetIsExceeded_Returns503ScanBudgetExceeded` and `RegisterPayment_WhenTheInvoicesOrderIsNotYetProjected_Returns503UpstreamUnavailable` |

Every mutated file was restored from a `cp` backup, `cmp`-verified byte-identical, the changed line re-read, and `touch`ed before a forced `dotnet build --no-incremental`. The confirming green runs (161/36) above were made after that rebuild, on eight source files and one test file.

## Round-1 items, one line each

| # | Round-1 requirement | Verdict |
|---|---|---|
| 1 | **D1** subject + payload guards, armed | **CLOSED** — R1 and R2 |
| 2 | **D2** literal public set, subtraction, `AllowAnonymous` set equality | **CLOSED** — R3 and R4 |
| 3 | **D3** `JwtOptions.FromEnvironment()` wired, env vars declared, named test | **CLOSED** — R5 |
| 4 | **D4** enumeration corrected, four `orders.integration` guards ported | **CLOSED** — see below |
| 5 | **D5** distinct totals + per-field assertions | **CLOSED for `ToOrderSummary`; the identical gap is open in `ToOrderDetail`** — R6 killed, R7 survived (defect **E1**) |
| 6 | **D6** `POST /invoices/{id}/payments` HTTP tests, tampered token, `UnknownOperatorError` | **CLOSED** — R9 |
| 7 | **D7** route-precedence claim probed or direction stated | **CLOSED behaviourally; the ledger row itself still carries no guard** (defect **E2**) |
| 8 | **D9** `alg: none` + tampered-payload cases, row 3 names the four properties | **CLOSED behaviourally; the ledger row itself is stale** (defect **E2**) — R8 |

### D1 — closed, and the `AsyncApiSchema` question answered

**It reads the real contract, not a snapshot.** `tests/Gateway.UnitTests/AsyncApiSchema.cs:18,21` — `RepositoryPaths.Find(Path.Combine("specs","shared","asyncapi.yaml"))` walks up to `OrderToCash.sln` and `File.ReadAllText`s the live file at test time; `GatewaySubjectsTests.cs:39-40` does the same independently. There is no embedded resource, no copied fixture, no committed snapshot. The one file it writes is `G5`'s scratch copy under `Path.GetTempPath()`, which is a deliberate corruption of a *copy* precisely so the real read-only spec is never mutated. Drift in `specs/shared/asyncapi.yaml` would be detected.

The direction is right too: in `GatewaySubjectsTests` the *expected* value is the C# constant and the *actual* is parsed from the spec, and in `GatewayRpcPayloadTests` the expected set is parsed from the spec and the actual is **reflection over the record's own properties** (`payloadType.GetProperties()`), never a hand-typed list — the `CreditRpcPayloadTests` shape round 1 asked for, not `StockRpcPayloadTests`' retyped-list shape. The `InvoiceViewPayload` superset assertion (`Assert.Subset` plus an exact `{"lines"}` difference) is the correct handling of D8 and is itself falsifiable: it would fail if a *second* extra member appeared.

**The stub question, answered honestly and unchanged:**

```
$ grep -rn "CreateBuilder\|Host\." tests/Gateway.IntegrationTests/*.cs | grep -v "GatewayHost\|GatewayTestHost"
tests/Gateway.IntegrationTests/RoutePrecedenceTests.cs:27   (a bare WebApplication probe, no responder)
tests/Gateway.IntegrationTests/FulfillmentStockEndToEndTests.cs:37,120,162   (OrderToCash.Fulfillment.FulfillmentHost)
tests/Gateway.IntegrationTests/InvoicesHttpTests.cs:21   (a doc-comment)
tests/Gateway.IntegrationTests/OrdersHttpTests.cs:14   (a doc-comment)
```

Four hits, classified: one real responder host (`FulfillmentHost`), one unrelated probe, two doc-comments. So **yes — six of the eight subjects are still exercised only against stubs**, exactly as in round 1. What has changed is that the two things a stub could get wrong silently — the subject string and the payload key set — are now both derived from `asyncapi.yaml` and both fail when corrupted. That is what round 1 *required*; the real-responder end-to-end was phrased there as what "would close the seam properly", and remains the recommended follow-up. `billing.invoice.list` + `billing.payment.register` is still the natural pair, and `InvoicesHttpTests` now supplies the HTTP half it would plug into.

### D2 — closed, and checked against the convention it produced

`CLAUDE.md:223-225` on disk (written after round 1, off this finding) asks: *what would a violation do to the candidate list — if the answer is "leave it", the sweep cannot fail.* Checked against the new sweep rather than assumed:

- `DocsAndAnonymousRouteHttpTests.cs:73` — the public set is a **literal**, `{"POST /auth/login", "GET /docs"}`.
- `:86-88` — `protectedRoutes` is `allOperations` **minus that literal**. `allOperations` (`:65-67`) is derived from `IHttpMethodMetadata` over every `RouteEndpoint`, which has nothing to do with `IAllowAnonymous`. A wrongly-public route therefore **stays in the population**.
- `:75-80` is a second, independent check that pins the `IAllowAnonymous` set to the same literal.

Probe R4 is the part that matters and is the reason I did not stop at R3: with the metadata assertion neutralised, the subtraction loop **on its own** failed with `GET /orders was expected to reject an anonymous request with 401, but answered 500` — the violation landed *in* the loop, reached the handler, and was caught there. The two checks are genuinely independent, and the sweep no longer filters by the property under test.

I also confirmed the population is complete and reachable: `BearerAuthenticationMiddleware.cs:36-41` is the **only** reader of `IAllowAnonymous` in the Gateway (no `AddAuthentication`/`AddAuthorization` pipeline exists), so there is no second mechanism by which a route could become public and escape both checks. All 14 mapped routes parameterise on `{id}` only, so the loop's `path.Replace("{id}", …)` produces a real URL for every one of them — no route silently 404s its way past the 401 assertion.

### D3 — closed, and the shape is inheritable

`Program.cs:16` — `options.Jwt = JwtOptions.FromEnvironment();`, on the path `Program.cs` actually takes, alongside its three siblings. Not defined-and-unused. #7's defaults verified against `../order-to-cash-nestjs/apps/gateway/src/infrastructure/auth/jwt.config.ts:12-14` — `otc_dev_jwt_secret_change_me`, `order-to-cash`, `'1h'` — and `#8`'s `3_600` seconds is that same hour. R5 killed on deleting one variable's read.

**One divergence, benign and worth recording:** `.env.example:191` declares `JWT_EXPIRES_IN=3600` where #7's `.env.example:341` says `1h`. `int.TryParse` rejects `"1h"` and falls back to 3600, so an `.env` copied across from #7 yields the identical lifetime. No behaviour is lost; a reader comparing the two files should know the units differ.

**On backlog id 56 arriving early, since the brief asks:** the shape here is the right one for the other five composition roots — a static `FromEnvironment()` on the options class itself, defaults living in the property initialisers (single source of truth), empty-string treated as unset, malformed numerics falling back rather than throwing, and a unit test class per options type. It matches `GatewayMongoOptions`, `LoginThrottleOptions`, `OperatorIdentityLoader` and `ProjectorNatsOptions` already. **What id 56 still owes is unchanged by this feature**: no test in the repository reaches `Program.cs`.

```
$ grep -rn "GatewayHost.Build\|OrderToCash.Gateway.Program\|<Program>" tests/ --include=*.cs
(no hits)
```

Zero hits — so deleting the `options.Jwt = …` **assignment** in `Program.cs` leaves the whole suite green, exactly as deleting the `Mongo` or `LoginThrottle` assignment would. That is precisely id 56's subject (a guard for env *wiring*, as distinct from env *reading*), it is uniform across all four options types rather than a new gap this feature introduced, and round 1 already recorded it as correctly deferred. Recorded here as evidence for id 56's brief, not as a defect.

### D4 — closed, verified by enumeration rather than by reading

The correction is exact. Not a prose sweep — the command and its result:

```
$ cd ../order-to-cash-nestjs && find apps/gateway/src -iname "*.spec.ts" | sed 's|apps/gateway/src/||' | sort > /tmp/n7files.txt
$ wc -l < /tmp/n7files.txt
44
$ sed -n '478,524p' progress/impl_gateway_rest_auth.md | grep -oP '\| `\K[^`]+(?=` \|)' | sort > /tmp/reported.txt
$ wc -l < /tmp/reported.txt
44
$ diff /tmp/n7files.txt /tmp/reported.txt
(no output)
```

**Exact match: 44 rows, one per real file, nothing grouped, nothing extra, nothing missing.** The two group counts that were wrong (`6` for 7 `application/queries/*`, `3` for 5 stream files) are gone because nothing is grouped; the two previously-unaccounted `nats-stream-signal*` files are rows 30 and 31.

Every #8 test file named in the 44 rows exists on disk — 25 distinct names, checked with `find`, zero missing. Classifications spot-checked against #7's actual content, not against filenames:

- Row 5 looked wrong and is right: `replenish-stock.command.spec.ts` → `ReadOnlyQueryHandlerTests.cs`. #7's spec is a one-case `ReplenishStockHandler` test; #8's counterpart is `ReadOnlyQueryHandlerTests.cs:73` `ReplenishStockCommandHandler_TranslatesToFulfillmentStockReplenish_AndPassesTheAffectedItemsThrough`. Odd file name, correct content.
- Rows 8/9/10/12 all point at `ReadOnlyQueryHandlerTests.cs`; its lines 13, 29, 43, 60 are the four list handlers. Correct.

**Row 36, the material miss, is genuinely closed.** #7's `orders.integration.spec.ts` has ten `it(` cases (`grep -n "  it(" …` → lines 70, 80, 112, 117, 129, 147, 171, 190, 200, 211). All ten now have an HTTP-level `OrdersHttpTests` counterpart, and `POST /orders/{id}/cancel` specifically has one (`CancelOrder_TranslatesToOrdersCancel_AndReturns202WithCompensationPlanned`, `:209`) that asserts the subject, the 202, the `compensationPlanned` body **and** that the RPC correlation id is the known order id. `PlaceOrder_WithNoLines_…_BeforeAnyRpcCall` asserts `rpc.CallCount == 0`, which is the half of #7's `:200` case that actually carries the claim.

One honest qualification the impl report already words correctly as *"PORTED at HTTP level"*: #7's `:117` case reads through **real MongoDB**; #8's reads through an `InMemoryOrderReadModel` substituted after `CreateBuilder`. The real-Mongo path is covered separately by `MongoOrderReadModelIntegrationTests`. The composed **HTTP → real Mongo** path still does not exist in #8. Round 1 flagged this under D6 and it is unchanged; not a regression, and not something D4 required.

### D6 — closed, and probed in the corruption family

`InvoicesHttpTests.cs` is real Kestrel over a real socket with a fake `IRpcClient` (unavoidable — the responder is Billing's) and covers all three previously wire-unproven branches plus the 201 happy path, which additionally asserts the `X-Correlation-Id` equals the projected order's id. Probe R9 is the reason I am satisfied: I did not delete a branch, I **swapped the two 503 codes**, which leaves the status codes identical and changes only the payload's `code` string. Both tests failed. A guard that only counted statuses would have passed that.

`Me_WithATamperedBearerToken_Returns401` and `Classify_UnknownOperatorError_MapsTo500InternalError` both exist. The implementer's disclosure about the last-base64url-character flip not changing the decoded signature bytes is a genuine and correctly-diagnosed finding, and the test now flips a middle character. `ProblemJsonMiddleware`'s bare `default` branch still has no HTTP-level test; D6 did not ask for one and it is behaviourally identical to `UnknownOperatorError`, which now has a unit case.

### D7 / D9 — behaviourally closed; see defect E2 for the documentation half

`RoutePrecedenceTests.cs` mounts `/probe/{id}` **before** `/probe/literal` on a bare `WebApplication`, starts real Kestrel, and asserts `GET /probe/literal` answers `"literal"` — the reversed direction, the one Express would fail, and the file states which direction it probed. That satisfies `CLAUDE.md`'s "both ways **or** state which way it was run".

`Verify_Throws_ForAnAlgNoneForgedToken_WithAnEmptySignatureSegment` and `Verify_Throws_WhenThePayloadSubjectIsTamperedWith_EvenIfTheSignatureBytesAreUntouched` are genuine forgery probes against production code — self-arming by construction, as claimed. I did not take that on trust: probe **R8** introduced the actual vulnerability the row is about (parse the header, skip signature verification when `alg == "none"`) and the named test **failed**. So row 3's algorithm-pinning claim is not merely true, it is now pinned by a test that can fail — which is the standard `CLAUDE.md` sets after feature 19, and the one this repository has previously missed.

---

## Defects found this round (2, neither blocking)

### E1 — `OrderReadModelMapper.ToOrderDetail` transposes `initialAmount`/`initialDiscount` with the whole Gateway suite green

**File:** `src/Gateway/Domain/Projection/OrderReadModelMapper.cs:84`.

```csharp
: new OrderTotalsView(doc.Totals.InitialAmount ?? 0, doc.Totals.InitialDiscount ?? 0, doc.Totals.TotalAmount.Value);
```

**Evidence — probe R7,** the two arguments transposed in this line **only** (the `ToOrderSummary` line at `:76` left correct): `Gateway.UnitTests` **161/161 green**, `Gateway.IntegrationTests` **36/36 green**, after `dotnet build --no-incremental` on both.

The code is **correct**; what is missing is any assertion on it. The two `ToOrderDetail` tests assert something else entirely — `ToOrderDetail_AlwaysReturnsADocument_PlaceholderOrNot` (`OrderReadModelMapperTests.cs:92`) asserts `Assert.Null(detail.Totals)` for a *placeholder*, so it never inspects a populated totals object at all, and `ToOrderDetail_PassesTheTimelineThroughUnmodified_IncludingCausationId` (`:112`) asserts event causation. At HTTP level, `OrdersHttpTests.GetOrder_ReturnsTheProjectedDocument_OnceOneExistsInTheReadModel` (`:266`) asserts `orderReference`, `headerComplete` and the events array length — not `totals`. So three money fields on `GET /orders/{id}`'s response body have no assertion anywhere in the repository.

**Why it is not blocking.** Round 1 graded the identical defect in `ToOrderSummary` **non-blocking**, and grading its sibling blocking now would be moving the line rather than holding it. More to the point, round 1's own prescription is what scoped the fix: D5 cited `:76` and the `OrderSummary` totals, and asked for "distinct non-zero values and an assertion on each of the three". The implementer did exactly that — it even widened the *shared* `CompleteDocument()` fixture to `(124950, 700, 124250)`, so the distinguishing values are already in place for a detail assertion. **This is my under-scoping, not the implementer's shortfall,** and the remedy is three `Assert.Equal` lines in a test that already builds the right document. No acceptance bullet and no `R<n>` is unmet: R54 and bullet 3 are claims about *where* list and detail are served from, which `WriteDatabaseAbsenceTests` and `MongoOrderReadModelIntegrationTests` prove, not claims about field identity.

**Why it must still be scheduled.** It is a live payload-corruption survivor of the exact class `CLAUDE.md` says is the more expensive question to leave unasked, on a built wire path, in the file the round was fixing. It should go on the backlog with E2, not into the next feature's assumptions.

### E2 — the ledger *table* is stale relative to its own corrections (rows 3 and 6)

**File:** `progress/impl_gateway_rest_auth.md:103` (row 3) and `:106` (row 6).

Both corrections were genuinely made — they are just not in the table. Row 3's Guard column still reads *"`JwtTokenServiceTests.cs` (7 cases: round-trip, expired, tampered signature, wrong secret, wrong issuer, 4× malformed-token shapes)"*, with no mention of the four properties `jsonwebtoken` supplied and no mention of the two new cases; the enumeration of expiry / issuer / structural / algorithm-pinning lives 550 lines further down under `### D9`. Row 6's Guard column is still **empty**, with `RoutePrecedenceTests.cs` named only in the fix-round prose.

This matters for one reason and it is the reason the ledger exists: **row 6's stated purpose is to save feature 26 from re-deriving the route-precedence question.** A feature-26 implementer will read the table. It will see a confident engine claim with an empty Guard cell — which is what the row looked like when I rejected it — and will have no pointer to the test that settles it. `CLAUDE.md`'s ledger section is explicit that a row's Guard column is a countable claim and that a hand-built property needs its guard *named in the row*.

**Not blocking, and not a re-review item.** The fix is an edit to two table cells in a `progress/` file, which the leader may make directly under `CLAUDE.md`'s own carve-out for changes outside `src/`, `tests/` and `apps/web/`. **It should be made before this feature is committed**, so the artefact #9 and feature 26 inherit is the corrected one. Specifically: row 3's Guard column should name expiry / issuer / structural-rejection / algorithm-pinning and cite the two new case names; row 6's Guard column should cite `tests/Gateway.IntegrationTests/RoutePrecedenceTests.cs` and state that the reversed registration order is the direction probed.

## `R<n>` → test mapping verified

| R | Claimed test | Verified this round |
|---|---|---|
| **R54** (gateway half) | `MongoOrderReadModelIntegrationTests.cs`, `WriteDatabaseAbsenceTests.cs` ×4, and now `OrdersHttpTests.GetOrder_ReturnsTheProjectedDocument_…` / `ListOrders_ExcludesAPlaceholderDocumentWithNoOrderReferenceYet` at HTTP level | Yes — both new HTTP cases exist and pass over real Kestrel; the "list and detail served from the read model" claim is proved at HTTP level for the first time. Caveat E1: the detail response's `totals` fields are unasserted. |
| **R55** (202-vs-404 half) | `OrdersHttpTests` 202/404 pair | Yes — unchanged from round 1, still passing |
| **R63** | `AuthAndRateLimitHttpTests` ×4 (three cases plus the new tampered-token case) | Yes — all four exist and pass over real Kestrel |

No `R<n>` moves Status this round and `specs/shared/test-matrix.md` is correctly untouched — the new tests strengthen the proof of rows already marked DONE. `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` reports only `test-matrix.md` differing, exactly as round 1 approved.

## CHECKPOINTS walk — round 2

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds the five agents
- [x] every agent definition declares its model
- [x] `./init.sh` exits 0 — **run by me**, `environment and state are coherent`

**C2 — state coherent**
- [x] exactly one feature in flight (id 25, `in_review` → `done` by this review); `in_progress` list is empty
- [x] every status in `rules.valid_status`
- [x] every `done` feature has passing tests
- [x] `progress/current.md` describes the active session
- [x] no `blocked` feature

**C3 — architecture**
- [x] no banned framework reference in any `Domain/` folder — **verified by running** `tests/Architecture.Tests`, **16/16**, with `src/Gateway` in `DomainAssemblies.cs:33`
- [x] no cross-service DB access — `WriteDatabaseAbsenceTests` enumerates the absence four independent ways; the Gateway reaches every write model by NATS RPC and reads only the projector's Mongo collection
- [x] no shared runtime code beyond `SharedKernel` / `Contracts` / `Cqrs` — `Gateway.csproj` references exactly those three
- [x] no `Domain/` namespace references `OrderToCash.Cqrs`
- [x] `src/SharedKernel` still has zero `PackageReference`
- [x] no `decimal` in domain arithmetic — money is `long` throughout `GatewayRpcPayloads.cs` and `OrderTotalsView`; the new `JwtOptions` touches no money
- [x] every interaction classifiable as Kafka-fact or NATS-RPC — all eight are NATS request-reply, and all eight subject strings are now asserted against `asyncapi.yaml`'s channel `address:` lines
- [x] no stray debug logging, no context-free TODOs

**C4 — verification real**
- [ ] `./quality.sh` passes — **not re-run by me.** The implementer reports 1481 / 18 projects / 0 failed / 0 skipped; I confirm its arithmetic from my own Gateway runs (+56 = 1481 − 1425) but report the full-suite figure as the implementer's, not mine.
- [x] domain tests pure — `RpcErrorClassifierTests`, `IssuedOrderWindowTests`, `OrderReadModelMapperTests`, `OperatorAuthenticatorTests` reference no framework; the new `GatewaySubjectsTests` / `GatewayRpcPayloadTests` / `JwtOptionsTests` live in the unit project and use only `System.IO`/reflection
- [x] integration tests use Testcontainers against real MsSql / Kafka / NATS / MongoDB — 36/36 passing with containers up; `FulfillmentStockEndToEndTests` boots a real `FulfillmentHost`
- [ ] coverage thresholds — not independently re-checked this round
- [x] no Jest anywhere

**C5 — session closed cleanly**
- [x] no suspicious untracked files; all eight source files and one test file I mutated restored, `cmp`-verified, lines re-read, forced rebuild, confirming green
- [x] `progress/history.md` entry with effort record — appended by this review
- [x] `feature_list.json` reflects true state (id 25 → `done`, single-line edit)
- [ ] human told what was done and how to test manually — leader's step
- [x] Claude did not commit

**C6 — SDD** — not applicable, `sdd: false`.

**C7 — reuse fidelity**
- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — verified by `diff -rq` against the #7 checkout, this round
- [x] no silent fork; the `test-matrix.md` edit is round 1's approved Status-column update and was not touched again
- [x] the `R<n>` ids are #7's and the realisations genuinely satisfy them
- [ ] `n8n/workflows/*.json` fire green against the .NET Gateway — **still not exercised.** Carried from round 1: the Gateway is the first .NET target these workflows could run against, this is the sharpest available parity test, and no acceptance bullet names it. It should be scheduled explicitly rather than drifting.
- [ ] black-box API script proves the same saga steps as #7 — not exercised this feature
- [x] effort record complete — appended to `progress/history.md`
- [ ] README benchmark section — leader's step

## Effort

Recorded in `progress/history.md`. Summarised: **1 implementation pass + 1 fix pass, 2 review rounds — REJECTED at round 1, APPROVED at round 2, ≈2 h 35 min** (four passes; wall-clock bracketed by artefact mtimes, `progress/current.md` 05:29:49 to this review at ≈08:05). #7's counterpart: **1 implementation session + 3 review passes + 2 fix passes, rejected twice, ≈2 h 45 min.**

**The confound, kept in round 1's framing because it has not changed:** #8 was rejected **once**, and the defect it was rejected for (unguarded RPC subject constants) is in the **same class** #7 was first rejected for. The ledger and guard-enumeration rules changed *which* review pass found the seam, not *whether* a fix round was needed. This is not a win and should not read like one. What #8 can claim is fewer passes for a comparable outcome (4 vs 6), and that is a smaller and more honest claim than the wall-clock near-parity suggests.

---

**One expected follow-on, leader's step.** Closing id 25 leaves no feature active, so `./init.sh` now reports `[FAIL] progress/current.md claims a feature while none is active: "**Feature:** \`gateway_rest_auth\` (id 25, phase 13)"`. That is the session-close reset, not a defect — `progress/current.md` is the leader's file and I deliberately did not touch it. `./init.sh` exited 0 at the start of this review and every other check still passes; this is the only failing line.

**Phase 13 is not closed.** Ids **26** (`gateway_sse_push`), **56**, **60**, **61**, **63** and **64** remain. Defects **E1** and **E2** from this round should be added to the backlog by the leader; **E2** is a two-cell edit to `progress/impl_gateway_rest_auth.md`'s ledger table and should be made before this feature is committed.
