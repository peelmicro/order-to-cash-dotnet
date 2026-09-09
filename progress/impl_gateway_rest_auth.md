# `gateway_rest_auth` — implementation report

Feature id 25, phase 13, `sdd: false`. Sixth and last service; `src/Gateway/`
built from four `README_PLACEHOLDER.cs` files and a bare `.csproj` into a
running ASP.NET Core Minimal API host.

## What was built

**Paths built (14 of the 17 in `openapi.yaml`)** — every operation except the
three named as out of scope:

| Method | Path | Tag |
|---|---|---|
| POST | `/auth/login` | auth |
| GET | `/auth/me` | auth |
| POST | `/orders` | orders |
| GET | `/orders` | orders |
| GET | `/orders/{id}` | orders |
| POST | `/orders/{id}/cancel` | orders |
| GET | `/stock` | fulfillment |
| POST | `/stock/replenish` | fulfillment |
| GET | `/invoices` | billing |
| POST | `/invoices/{id}/payments` | billing |
| GET | `/credits` | billing |
| GET | `/catalog/products` | catalog |
| GET | `/catalog/retailers` | catalog |
| GET | `/catalog/companies` | catalog |
| GET | `/docs` | ops |

**Deliberately NOT built** (per the brief, confirmed against the spec):
- `GET /orders/stream` — feature `gateway_sse_push` (id 26).
- `GET /health/live`, `GET /health/ready` — phase 14 (`observability_reliability`).

`tests/Gateway.UnitTests/OpenApiContractTests.cs` asserts this split is
correct on both sides: the 14 built paths match the app's own real endpoint
metadata exactly, and the 3 excluded paths are genuinely declared in
`openapi.yaml` (not a typo) and genuinely absent from the app's own
`EndpointDataSource`/`IEndpointRouteBuilder.DataSources`.

## Architecture

```
src/Gateway/
  Domain/            OperatorIdentity + OperatorAuthenticator (pure credential
                     match), RpcErrorClassifier (pure RpcError → HTTP status +
                     Problem.code), IssuedOrderWindow (pure, TTL'd recency
                     cache), OrderReadModelDocument + OrderReadModelMapper
                     (pure BSON-shape → wire-shape mapping)
  Application/       12 commands/queries (LoginCommand, PlaceOrderCommand,
                     CancelOrderCommand, RegisterPaymentCommand,
                     ReplenishStockCommand, GetCurrentUserQuery,
                     ListOrdersQuery, GetOrderQuery, ListStockQuery,
                     ListInvoicesQuery, ListCreditsQuery, ListCatalogQuery),
                     each with exactly one handler (dispatcher-validated);
                     ports (IRpcClient, IOrderReadModel, ITokenService,
                     IClock); the Gateway's own transcribed RPC subject
                     constants and request/reply payload records
  Infrastructure/    NatsRpcClient (real NATS.Client.Core request-reply),
                     MongoOrderReadModel (direct, read-only Mongo query
                     against the projector's own collection), JwtTokenService
                     (hand-rolled HS256), OperatorIdentityLoader,
                     LoginThrottleOptions, EmbeddedOpenApiDocument,
                     GatewayServiceCollectionExtensions (AddGateway)
  Presentation/      6 endpoint-mapping files (one per openapi.yaml tag),
                     BearerAuthenticationMiddleware, ProblemJsonMiddleware,
                     CorrelationIdMiddleware, LoginRateLimiterExtensions,
                     request DTOs, GatewayRequestValidationError /
                     GatewayNotFoundError
  GatewayHost.cs     CreateBuilder / Configure / Build — the composition root,
                     split into two testable steps the same way every other
                     service's *Host class is
  Program.cs         Environment wiring only
```

## Packages

**Zero new NuGet packages.** `Gateway.csproj` switched SDK to
`Microsoft.NET.Sdk.Web` (a framework-reference change, not a package), which
brings Kestrel, Minimal APIs, `Microsoft.AspNetCore.Authorization` (used only
as a metadata marker — `.AllowAnonymous()` — no `AddAuthentication()`
pipeline) and `Microsoft.AspNetCore.RateLimiting` (R63's limiter) in for
free. `NATS.Net` and `MongoDB.Driver` were already centrally pinned in
`Directory.Packages.props` (used by other services) and needed no new
`PackageVersion` entry. `tests/Gateway.IntegrationTests` references
`Microsoft.EntityFrameworkCore.SqlServer` (already pinned) purely to drive
`FulfillmentDbContext` for the one true end-to-end test — never referenced
from `src/Gateway`.

**JWT, specifically**: `Microsoft.AspNetCore.Authentication.JwtBearer` is
**not** part of the ASP.NET Core shared framework — verified directly:
`find /usr/lib/dotnet/shared/Microsoft.AspNetCore.App/10.0.11/ -iname
"*JwtBearer*"` finds nothing. Adopting it would have been a genuinely new
`PackageVersion` for a single, statically-configured identity that needs no
discovery document or key rotation. `JwtTokenService` hand-rolls HS256 over
`System.Security.Cryptography.HMACSHA256` instead — see the ledger below.

## The ported-idiom ledger

| # | #7 relied on | In #8 that property is supplied by | Guard |
|---|---|---|---|
| 1 | `IssuedOrderWindow` (`apps/gateway/src/domain/orders/issued-order-window.ts`), a bounded/TTL'd in-process recency cache, to answer `GET /orders/{id}`'s 202-vs-404 distinction (R55) — review finding F3: before it existed, every read-model miss answered 202, making the documented 404 unreachable. | `src/Gateway/Domain/Orders/IssuedOrderWindow.cs`, ported unchanged in shape (a `Func<DateTimeOffset>` clock instead of an injected `IssuedOrderWindowClock`), wired the same way: `PlaceOrderCommandHandler.HandleAsync` records the reply's `OrderId`, `GetOrderQueryHandler.HandleAsync` reads it on a miss. | `tests/Gateway.UnitTests/IssuedOrderWindowTests.cs` (9 cases incl. constructor guards), `GetOrderQueryHandlerTests.cs` (3-way Found/Pending/Unknown), `PlaceOrderCommandHandlerTests.cs` (`HandleAsync_RecordsTheReplysOrderId_InTheIssuedOrderWindow`), `tests/Gateway.IntegrationTests/OrdersHttpTests.cs` (202 vs 404 over real HTTP) — armed, see below. |
| 2 | `classifyRpcError`'s `DETAIL_CODE_OVERRIDES` (`apps/gateway/src/domain/problem/rpc-error-mapping.ts`) keys on `details.code === 'PAYMENT_REFERENCE_CONFLICT'` to answer `PAYMENT_REFERENCE_REUSED`. | **Does not transfer as-is.** Billing's `PaymentReferenceConflictError` (`src/Billing/Application/InvoiceApplicationErrors.cs:37-44`) extends plain `Exception`, not `DomainError` — it carries no `Code` at all, and `BillingErrorMapper.cs:141-149` emits `PRECONDITION_FAILED` with `details.paymentReference` only, never `details.code`. `RpcErrorClassifier.Classify` (`src/Gateway/Domain/Problem/RpcErrorClassification.cs`) keys the SAME override on the **shape** of `details` instead: `PRECONDITION_FAILED` carrying a `paymentReference` key and no `code` key. openapi.yaml's `Problem.code` is a plain string, not a schema-enforced enum, so this coarser signal is contract-legal even though the property #7 used to detect it does not survive the trip through #8's Billing mapper. Reaching into Billing to add a `Code` was out of this feature's scope ("do not modify any other service"). | `tests/Gateway.UnitTests/RpcErrorClassifierTests.cs` › `Classify_PreconditionFailedCarryingOnlyAPaymentReferenceDetail_AnswersPaymentReferenceReused`, › `Classify_PreconditionFailedWithNeitherCodeNorPaymentReference_FallsBackToTheGenericPreconditionFailedClassification`. |
| 3 | `jsonwebtoken` (npm), one adapter, one library import (`apps/gateway/src/infrastructure/auth/jwt-token.adapter.ts`) — reasoned there as "there is exactly one statically-configured identity to authenticate, so a strategy/guard framework buys nothing". | `Microsoft.AspNetCore.Authentication.JwtBearer` is the .NET off-the-shelf equivalent but is **not** in the shared framework (verified above) — adopting it would be a new package for the identical single-identity case #7's own comment argues against a framework for. `JwtTokenService` (`src/Gateway/Infrastructure/Auth/JwtTokenService.cs`) hand-signs/verifies HS256 directly over `HMACSHA256`, narrower than even #7's own dependency, and needs zero new NuGet packages. | `tests/Gateway.UnitTests/JwtTokenServiceTests.cs` — **11 cases**, counted off `dotnet test --filter FullyQualifiedName~JwtTokenServiceTests` (7 `[Fact]` + one `[Theory]` with 4 `[InlineData]`): round-trip, expired, tampered signature, wrong secret, wrong issuer, 4× malformed-token shapes, plus the two the fix round added — **`alg: none` with an empty signature, and a tampered payload**. The algorithm-pinning property was proved by *introducing* the `alg: none` vulnerability, not by re-reading the code. |
| 4 | `@nestjs/throttler`'s `ThrottlerGuard`, applied route-scoped via `@UseGuards(ThrottlerGuard)` on `POST /auth/login` only (`apps/gateway/src/presentation/auth.controller.ts`). | `Microsoft.AspNetCore.RateLimiting` (part of the shared framework — no new package), a fixed-window limiter policy applied via `.RequireRateLimiting("login")` on the same one route (`src/Gateway/Presentation/RateLimiting/LoginRateLimiterExtensions.cs`) — same "opt in per route, never global" shape. | `tests/Gateway.IntegrationTests/AuthAndRateLimitHttpTests.cs` (7 cases, real Kestrel, real socket round trip) — armed, see below. |
| 5 | A **runtime** sweep of every response field (`apps/gateway/money-representation.integration.spec.ts`) to prove no monetary value is a float — necessary because TypeScript/JavaScript numbers carry no type distinct from a float, so the class of bug is only visible by inspecting values at runtime. | Every money field in `src/Gateway/Application/Rpc/GatewayRpcPayloads.cs` is declared `long`/`int` in the C# type system. A `decimal`/`double` field would not compile against these records' declared types, and `DomainDecimalTests`/`DomainPurityTests` (already existing, `tests/Architecture.Tests`) ban `decimal`/`float` from `Domain/` entirely. **Not ported as a runtime sweep — structurally prevented instead**, matching CLAUDE.md's own Money non-negotiable ("a narrowing cast on a money value is a defect"). No new test needed for this row; it is a "not applicable" row, not a gap. |  |
| 6 | Express's registration-order-sensitive router — `contract-drift.integration.spec.ts` finding F9: `/orders/stream` must be registered BEFORE `/orders/:id`, or the literal `stream` segment is swallowed by the `:id` wildcard. | ASP.NET Core Minimal API routing is **not** order-sensitive: a literal route segment always outranks a route-parameter segment at the same position, regardless of `Map*` call order (documented endpoint-selection precedence — most-specific match wins, not first-registered). **Not applicable** — no F9-equivalent guard is needed in #8. Recorded now, though `/orders/stream` is feature 26's own path, because feature 26 will hit the identical question and should not have to re-derive this. | `tests/Gateway.IntegrationTests/RoutePrecedenceTests.cs` — added by the fix round (review item D7). **Probed in one direction, and the row says which**: the parameter route `/probe/{id}` is mapped **before** the literal `/probe/literal` — the reversed order, i.e. the one under which Express (the engine #7's F9 was about) would swallow the literal segment — and the literal still wins, over a real Kestrel round trip rather than a route-table inspection, because precedence is match-time behaviour. The forward order is **not** probed: it is the case that would also pass under an order-sensitive router, so it distinguishes nothing. **Feature 26 registers `/orders/stream` and will read this row**: #7's F9 ordering constraint does not transfer, but this row licenses only *literal-beats-parameter at the same position*, not route precedence in general. |
| 7 | #7's own `cancel-order.command.ts` hard-codes `cancellationReason: 'operator_cancelled'` in its response **unconditionally**, regardless of whether the order actually reached `cancelled`. | This is a genuine **divergence from #7's own code**, found while porting, not a translation gap: #8's `OrdersCreateResponder.ToReplyPayload(CancelOrderResult)` (`src/Orders/Presentation/OrdersCreateResponder.cs:213-219`) populates `CancellationReason` only once the order has actually reached `cancelled` — nullable, matching `openapi.yaml`'s own documented wording for `CancelOrderResponse.cancellationReason` ("present only once the order has actually reached cancelled"). `CancelOrderCommandHandler` (`src/Gateway/Application/Commands/CancelOrderCommand.cs`) passes the **responder's own** value through rather than echoing #7's hard-coded shape. | `tests/Gateway.UnitTests/CancelOrderCommandHandlerTests.cs` › `HandleAsync_PassesThroughTheRespondersOwnCancellationReason_NeverHardCodingIt`. |
| 8 | NestJS's decorator-driven OpenAPI document generator (`apps/gateway/src/presentation/setup-docs.ts`) serves a live Swagger UI reflecting the running app's own decorators. | Minimal APIs have no equivalent decorator-reflection generator that would stay byte-identical to the COMMITTED `specs/shared/openapi.yaml` (`Microsoft.AspNetCore.OpenApi` generates from C# types, which could silently drift from the contract). `GET /docs` instead embeds the actual `openapi.yaml` bytes as a build-time `EmbeddedResource` (`Gateway.csproj`) and renders them verbatim inside a plain HTML page (`DocsEndpoints.cs`) — guarantees byte-identity with the committed contract, at the cost of no live "try it out" UI. A deliberate translation, not a lesser copy. | `tests/Gateway.IntegrationTests/DocsAndAnonymousRouteHttpTests.cs` › `Docs_Answers200TextHtml_Unauthenticated`. |

## Enumeration — #7's Gateway test files, classified

Command: `find apps/gateway/src -iname "*.spec.ts"` (run against
`order-to-cash-nestjs`, sibling checkout). **44 files.** One line per file
below (SSE/`stream.*`, `sse/*`, health-probe/health-check and
observability/request-latency files are **N/A** — features 26 and 27's own
scope, not this one, and are grouped rather than listed individually).

| #7 file | Classification |
|---|---|
| `domain/auth/operator-credentials.spec.ts` | **PORTED** — `OperatorAuthenticatorTests.cs` (5 cases) |
| `domain/orders/issued-order-window.spec.ts` | **PORTED** — `IssuedOrderWindowTests.cs` (9 cases, incl. the two constructor-guard cases #7's own suite has) |
| `domain/problem/rpc-error-mapping.spec.ts` | **PORTED** — `RpcErrorClassifierTests.cs` (18 cases: all 12 generic codes individually, both transient codes distinguished, 3 detail-code overrides, the shape-based override, an unrelated-code no-op, a no-signal fallback, an unrecognised-code fallback) |
| `domain/projection/order-read-model-mapper.spec.ts` | **PORTED** — `OrderReadModelMapperTests.cs` (7 cases). "preserves a non-null cancellationReason" is not a separate case: C# record `with`-expressions pass every field through by construction, so there is no hand-picked field list that could selectively drop one the way a hand-written object-literal mapper could — the risk #7's case guards against does not exist in this shape. |
| `infrastructure/auth/jwt-token.adapter.spec.ts` | **PORTED, +2 cases** — `JwtTokenServiceTests.cs` (7: round-trip, expired, tampered signature [new], wrong secret, wrong issuer, 4× malformed shapes [#7 has 1]) |
| `infrastructure/auth/login-throttle.config.spec.ts` | **PARTIALLY PORTED** — `LoginThrottleOptionsTests.cs` (6 cases: default, valid override, 4× malformed→fallback). The **structured-logging half of #7's own review finding F1** ("logs a warning naming the rejected raw value") is a deliberate, recorded gap: `LoginThrottleOptions.FromEnvironment()` follows this repository's OWN established, narrower precedent for static `*Options.FromEnvironment()` loaders (e.g. `ProjectorMongoOptions.FromEnvironment` — no `ILogger`, no logging on a malformed value), rather than introducing the first exception to it. Recorded here, not silently closed. |
| `infrastructure/messaging/nats-rpc-client.adapter.spec.ts` | **PORTED, over a REAL broker** (not a mocked NATS client) — `NatsRpcClientIntegrationTests.cs` (5 cases: success decode + header verification, no-responder→`RpcTransportError`, silent-responder→`RpcTimeoutError`, the two distinguished, business-error detail normalisation). "honours a per-call timeout override" **not ported** — deliberate: no Gateway command in this feature needs a different timeout per call, so `IRpcClient.CallAsync` has no override parameter; adding one with no caller would be untested surface. OTel trace-context injection **N/A** — feature 27's scope. |
| `infrastructure/persistence/mongo-order-read-model.adapter.spec.ts` | **PORTED, more strongly** — #7 mocks the MongoDB collection object directly (Jest convention); `MongoOrderReadModelIntegrationTests.cs` (4 cases) runs the SAME assertions against a real `mongo:8.3.8` container, per CLAUDE.md's own testing convention ("never mocks of infrastructure"). `MongoOrderReadModelMappingTests.cs` (3 cases, pure BSON, no server) additionally proves the field-by-field mapping against hand-built documents shaped exactly as the projector's own `PlaceholderDocument`/`DeltaToPipeline` write them. |
| `presentation/guards/jwt-auth.guard.spec.ts` | **PORTED at a different (stronger) level** — #8 has no NestJS Guard class (`BearerAuthenticationMiddleware` is plain ASP.NET Core middleware, not a separately-instantiable/unit-testable guard object); the 4 cases are covered end-to-end over real HTTP in `AuthAndRateLimitHttpTests.cs` (`Me_WithoutABearerToken_Returns401`, `Me_WithAValidBearerToken_ReturnsTheOperatorsIdentity`) and `DocsAndAnonymousRouteHttpTests.cs` (the F8 all-routes sweep, below) rather than a fast in-process unit test. |
| `presentation/pagination.spec.ts` | **PORTED** — `RequestParsingTests.cs` (8 cases) |
| `presentation/problem-json.filter.spec.ts` | **PORTED** — `ProblemJsonMiddlewareClassificationTests.cs` (11 direct unit cases, no `HttpContext`, no host) + `OrdersHttpTests.cs`/`AuthAndRateLimitHttpTests.cs` (the same mappings proved again end-to-end). The correlationId-reuse and traceId cases are **N/A** — R57/OTel is feature 27's scope; correlationId reuse (not a fresh id per error) IS ported: `ProblemJsonMiddleware.WriteProblemAsync` reads `CorrelationIdMiddleware.ItemKey` from `HttpContext.Items` rather than minting a new one. |
| `application/commands/*.spec.ts` (5 files), `application/queries/*.spec.ts` (6 files) | **PORTED** — one `*CommandHandlerTests.cs`/`*QueryHandlerTests.cs` per #8 handler: `PlaceOrderCommandHandlerTests.cs`, `CancelOrderCommandHandlerTests.cs`, `LoginAndCurrentUserHandlerTests.cs`, `RegisterPaymentCommandHandlerTests.cs` (7 cases, incl. #7's own F4 two-way scan-exhaustion split), `GetOrderQueryHandlerTests.cs`, `ListOrdersQueryHandlerTests.cs`, `ReadOnlyQueryHandlerTests.cs` (the five thin translation handlers — `ListStock`/`ListCredits`/`ListInvoices`/`ListCatalog`/`ReplenishStock` — one case each). |
| `auth.integration.spec.ts` | **PORTED** — `AuthAndRateLimitHttpTests.cs` + `DocsAndAnonymousRouteHttpTests.cs` › `EveryRegisteredRouteExceptTheTwoDocumentedPublicOnes_RejectsAnAnonymousRequestWith401` (#7's own finding F8, adapted to #8's own two public routes this feature builds — `/auth/login`, `/docs`; `/health/*` are phase 14's). |
| `auth-rate-limit.integration.spec.ts` | **PORTED, in full** — `AuthAndRateLimitHttpTests.cs` (3 cases: 429+Retry-After+problem+json+no-token, same-429-for-valid-and-invalid, scoped-to-login-only) — armed, see below. |
| `no-write-database-client.spec.ts` | **PORTED, adapted to .NET's own write-DB packages** — `WriteDatabaseAbsenceTests.cs` (4 cases: NetArchTest × 2, `.csproj` text scan, source-file `MSSQL_` scan) — armed, see below. |
| `contract-drift.integration.spec.ts` | **PORTED, +1 case** — `OpenApiContractTests.cs` (4 cases: the bijection, the excluded-set double-check, the 17-paths/18-operations count) + `DocsAndAnonymousRouteHttpTests.cs` (`GET /docs` content-type). F9 (route-registration order) is **N/A** — see ledger row 6. |
| `money-representation.integration.spec.ts` | **NOT PORTED, structurally prevented instead** — see ledger row 5. |
| `billing-fulfillment.integration.spec.ts` | **PARTIALLY PORTED** — `FulfillmentStockEndToEndTests.cs` proves the identical class of risk against Fulfillment's **real, live** `StockRpcResponder` (stronger than #7's own stand-in-fixture-based test). Billing's own real-responder equivalent was **not built** (time/scope) — the generic wire-level risk is still closed (`NatsRpcClientIntegrationTests.cs` + every Billing payload record in `GatewayRpcPayloads.cs` transcribed field-for-field from `src/Billing/Infrastructure/Messaging/Rpc/*.cs`), but a genuine real-Billing-responder end-to-end test is a fair follow-up recommendation, not a silent gap. |
| `black-box-api.integration.spec.ts` scenario 4 (auth/error shapes) | **PORTED** — `OrdersHttpTests.cs` › `GetOrder_ForAMalformedId_Answers400` (new, closes a gap this enumeration found), › `GetOrder_ForAnIdThisGatewayNeverIssued_Answers404`; `AuthAndRateLimitHttpTests.cs` › `Me_WithoutABearerToken_Returns401`. |
| `black-box-api.integration.spec.ts` (full happy/compensation/idempotency paths), `saga-e2e-verification.integration.spec.ts`, `spawn-real-service-smoke.integration.spec.ts` | **N/A to this feature** — whole-fleet saga e2e proofs belong to `order_saga_orchestrator` (already closed) or later phases; this feature's own real-process proof is `FulfillmentStockEndToEndTests.cs`. |
| `stream*.spec.ts`, `domain/sse/*.spec.ts` (3 files) | **N/A** — feature 26 (`gateway_sse_push`), not built here. |
| `health-probes.integration.spec.ts`, `infrastructure/health/health-checks.spec.ts` | **N/A** — phase 14 (`observability_reliability`), not built here. |
| `infrastructure/observability/http-instrumentation.spec.ts`, `presentation/request-latency.interceptor.spec.ts` | **N/A** — feature 27's scope (OTel/metrics). |

**Recommendation for a follow-up round** (not a gap in this feature's own
acceptance): a real-Billing-responder end-to-end test mirroring
`FulfillmentStockEndToEndTests.cs`, and threading structured-logging into
`LoginThrottleOptions.FromEnvironment` if this repository's static-loader
convention is ever revisited.

## Acceptance bullets — how each is proved

**1. "contract test asserts no drift from openapi.yaml."** Derived from the
app's REAL endpoint metadata on both sides, never a hand-typed list on
either: `tests/Gateway.UnitTests/OpenApiContractTests.cs` parses the
`paths:` section straight out of the EMBEDDED `openapi.yaml` bytes (the same
bytes `GET /docs` serves) and reads the "actual" side off
`((IEndpointRouteBuilder)app).DataSources` after `GatewayHost.Configure` has
mapped every endpoint — no Kestrel start required. `MappedEndpoints_MatchOpenApiYamlExactly_ForEveryPathThisFeatureBuilds`
asserts the symmetric difference is empty; `TheDeliberatelyExcludedPaths_AreDeclaredInOpenApiYaml_ButNotMappedByThisFeature`
proves the 3-path exclusion list is itself correct on both sides;
`OpenApiYaml_Declares17PathsAnd18Operations` is the same count check #7's
own `contract-drift.integration.spec.ts` makes. Armed — see below.

**2. "RPC timeouts mapped to HTTP status codes."** `RpcErrorClassifier`
(`src/Gateway/Domain/Problem/RpcErrorClassification.cs`) maps all 12
generic `RpcError.code` values individually
(`RpcErrorClassifierTests.cs`, one `[InlineData]` row per code, plus a
dedicated case proving `TIMEOUT`/`UNAVAILABLE` — the two codes that both
answer 503 — carry DIFFERENT `Problem.code`s). The two transient failures
are proved as genuinely DIFFERENT `RpcCallError` subtypes over a REAL NATS
broker in `NatsRpcClientIntegrationTests.cs`
(`CallAsync_Throws_RpcTransportError_WhenNoResponderIsSubscribed`,
`CallAsync_Throws_RpcTimeoutError_WhenAResponderIsSubscribedButNeverReplies`,
`CallAsync_NoResponderAndSilentResponder_ThrowDifferentErrorTypes`) — a real
broker is what makes `NatsNoRespondersException` (immediate) genuinely
distinguishable from a subscribed-but-silent responder's `NatsNoReplyException`
(deadline-paced), which no mock could prove honestly. Armed — see below.

**3. "list/detail served from MongoDB, never from a write DB."** An
ABSENCE claim, enumerated rather than asserted in prose
(`tests/Gateway.UnitTests/WriteDatabaseAbsenceTests.cs`, 4 cases — 2
NetArchTest scans of the compiled assembly, a `.csproj` text scan, a
source-file `MSSQL_` text scan) plus a positive proof that list/detail
genuinely comes from Mongo (`MongoOrderReadModelIntegrationTests.cs`, real
`mongo:8.3.8`) and that `GET /stock` — openapi.yaml's own documented
exception to R54 — genuinely comes from Fulfillment's live write model via
RPC instead (`FulfillmentStockEndToEndTests.cs`, real MS-SQL). The exact
enumeration commands and their complete output are recorded under
"Absence enumeration, run directly" below. Armed — see below.

## The known risk — how it was closed

Two independent proofs, at two different levels, both against REAL
infrastructure:

1. **`NatsRpcClientIntegrationTests.cs`** — a real `nats:2.14.5-alpine`
   broker, a generic stand-in responder (`StandInResponder.cs`, the same
   shape `StandInFulfillmentStockCheckResponder` already established in
   `Orders.IntegrationTests`) proving the Gateway's `NatsRpcClient` sends
   the correlation headers correctly, decodes a success reply correctly,
   and distinguishes every failure class a real broker can actually produce.
2. **`FulfillmentStockEndToEndTests.cs`** — the REAL `StockRpcResponder`
   (`OrderToCash.Fulfillment.FulfillmentHost.CreateBuilder`, verbatim, real
   MS-SQL, real NATS, real Kafka) driven through the Gateway's own real HTTP
   pipeline (`GatewayTestHost`, real Kestrel on an ephemeral port). Two
   tests: `GET /stock` reads what was actually seeded in Fulfillment's own
   database through the real RPC round trip, and `POST /stock/replenish`
   actually mutates it — read back independently through a second EF Core
   context to prove the mutation is real, not merely a 200 status code.

Every request/reply payload record in
`src/Gateway/Application/Rpc/GatewayRpcPayloads.cs` was cross-checked
property-by-property against the REAL production record it must be
wire-compatible with (cited in that file's own header comment) — the
concrete way this feature avoided "a stub written to match its own
assumption" for the four subjects (`billing.credit.list`,
`billing.invoice.list`, `billing.payment.register`, `catalog.reference.list`)
that do not yet have a dedicated real-process end-to-end test of their own.

## Absence enumeration, run directly

```
$ grep -rn "MSSQL_" src/Gateway --include=*.cs
(no hits)
$ grep -in "EntityFrameworkCore\|SqlClient" src/Gateway/Gateway.csproj
(no hits)
$ grep -rn "EntityFrameworkCore\|Microsoft.Data.SqlClient" src/Gateway --include=*.cs
(no hits)
```

One classification line per command: all three enumerate the complete
candidate set (every `.cs` file under `src/Gateway`, and the `.csproj`
itself) and all three return zero hits — the absence claim bullet 3 makes.

## Arming table

Every mutation below was: (1) applied with `Edit`, (2) built with
`dotnet build --no-incremental` (or a `touch` + normal build for the C#
cases, to defeat MSBuild's incremental up-to-date check), (3) the named
test run and its FAIL captured verbatim, (4) restored from a `cp` backup
taken before mutation (never `git checkout --`), (5) verified byte-identical
with `cmp` against the backup, (6) rebuilt, (7) the named test re-run and
confirmed green.

| # | File mutated | Mutation | Named test | Verbatim failure (abridged) | Restored + green |
|---|---|---|---|---|---|
| 1 | `GatewayHost.cs` | Commented out `app.MapCreditsEndpoints();` | `OpenApiContractTests.MappedEndpoints_MatchOpenApiYamlExactly_ForEveryPathThisFeatureBuilds` | `Drift from openapi.yaml. Declared in openapi.yaml but NOT mapped: GET /credits` | Yes — `cmp` identical, rebuilt, 1/1 green |
| 2 | `RpcErrorClassification.cs` | Removed the `TIMEOUT` entry from `_genericClassification` | `RpcErrorClassifierTests.Classify_MapsEveryGenericRpcErrorCode_...` (TIMEOUT row) + `Classify_TimeoutAndUnavailable_MapToTheSameStatusButDifferentProblemCodes` | `Assert.Equal() Failure ... Expected: 503 Actual: 500` (both cases) | Yes — `cmp` identical, rebuilt, 20/20 green |
| 3a | `Gateway.csproj` | Added an XML comment mentioning `Microsoft.EntityFrameworkCore` | `WriteDatabaseAbsenceTests.GatewayCsproj_NeverReferencesEitherWriteDatabasePackage` | `Assert.DoesNotContain() Failure: Sub-string found ... "Microsoft.EntityFrameworkCore"` | Yes — `cmp` identical, 1/1 green |
| 3b | `GatewayOptions.cs` | Added a comment mentioning `Environment.GetEnvironmentVariable("MSSQL_HOST")` | `WriteDatabaseAbsenceTests.NoGatewaySourceFile_ReadsAWriteDatabaseConnectionStringEnvironmentVariable` | `Write-DB connection-string reads found: .../GatewayOptions.cs:9` | Yes — `cmp` identical, 1/1 green |
| 4 | `CancelOrderCommand.cs` | `RpcCallMeta(command.OrderId, ...)` → `RpcCallMeta(Guid.NewGuid(), ...)` | `CancelOrderCommandHandlerTests.HandleAsync_UsesTheOrderIdAsTheRpcCorrelationId_NeverAFreshRequestId` | `Assert.Equal() Failure ... Expected: faee41f4-... Actual: e6f69ba2-...` (different GUIDs) | Yes — `cmp` identical, rebuilt, 4/4 green |
| 5 | `AuthEndpoints.cs` | Removed `.RequireRateLimiting(LoginRateLimiterExtensions.PolicyName)` | `AuthAndRateLimitHttpTests.Login_ExceedingTheRateLimit_Returns429WithRetryAfterAndIssuesNoToken` | `Assert.Equal() Failure ... Expected: TooManyRequests Actual: OK` | Yes — `cmp` identical, rebuilt, 7/7 green |
| 6 | `PlaceOrderCommand.cs` | `issuedOrders.Record(reply.OrderId)` → `issuedOrders.Record(Guid.NewGuid())` | `PlaceOrderCommandHandlerTests.HandleAsync_RecordsTheReplysOrderId_InTheIssuedOrderWindow` | `Assert.True() Failure Expected: True Actual: False` | Yes — `cmp` identical, rebuilt, 3/3 green |
| 7 | `GetOrderQuery.cs` | `if (issuedOrders.IsRecentlyIssued(...))` → `if (false && issuedOrders.IsRecentlyIssued(...))` | `GetOrderQueryHandlerTests.HandleAsync_ReturnsPending_WhenTheIdWasRecentlyIssuedButNotYetProjected` | `Assert.Equal() Failure ... Expected: Pending Actual: Unknown` | Yes — `cmp` identical, rebuilt, 3/3 green |

Guards 1-2, 4, 6-7 required a forced rebuild (`--no-incremental` or `touch`)
before both the arm and the restore confirmation, per the arming protocol's
own warning about MSBuild's incremental up-to-date check masking a still-
armed binary. Guards 3a/3b are read directly as text by their own tests
(`File.ReadAllText`), so no build step applies to them — `cmp` against the
backup is the whole restore proof.

## Final verification run

Two full `./quality.sh` passes were run. The first ran CONCURRENTLY with a
batch of gap-closing test additions (found while enumerating #7's own
Gateway test suite) and its Gateway.UnitTests count is therefore a stale
mid-edit snapshot — informational only, not cited as evidence. The SECOND
pass ran with the tree quiescent (no concurrent edits) and is the number
below.

```
$ ./quality.sh
── 1. Format check ── OK, clean
── 2. Build ── OK, 0 warnings, 0 errors
── 3. Test + coverage ── OK, all tests passed
── 4. Coverage summary ── per-project line coverage 0.0%–97.2% (18 reports)
```

Per-project totals from that run (`Failed: N, Passed: N, Skipped: N, Total: N`
lines, one per project), summed directly off the log:

```
$ grep -oE "Failed: *[0-9]+, Passed: *[0-9]+, Skipped: *[0-9]+, Total: *[0-9]+" quality_run_final.log \
    | awk -F'Total: *' '{sum+=$2; count++} END {print "projects:", count, "total tests:", sum}'
projects: 18 total tests: 1425

$ grep -oE "Failed: *[0-9]+" quality_run_final.log | awk -F': *' '{sum+=$2} END {print "total failed:", sum}'
total failed: 0
```

**18 projects (16 pre-existing + `Gateway.UnitTests` + `Gateway.IntegrationTests`),
1425 tests, 0 failed, 0 skipped** — up from the pre-feature baseline of
1284 passed / 0 failed / 0 skipped across 16 projects (a net +141 tests: 115
in `Gateway.UnitTests`, 26 in `Gateway.IntegrationTests`). Every
`Gateway.IntegrationTests` test ran against REAL infrastructure — a real
`nats:2.14.5-alpine`, a real `mongo:8.3.8`, and (for
`FulfillmentStockEndToEndTests`) real `mcr.microsoft.com/mssql/server` and
`apache/kafka:4.3.1` containers, driving Fulfillment's actual production
host — never a mock.

`dotnet format OrderToCash.sln --verify-no-changes` — clean, both before and
after the arming pass (`git diff --stat -- src/Gateway` after the pass shows
only the legitimate `Gateway.csproj` edit and the three deleted placeholder
files — every arming mutation left zero residue).

`./init.sh` — exits 0.

## What was NOT done, and why

- **`GET /orders/stream` and `GET /health/live`/`GET /health/ready`** —
  deliberately out of scope (features 26 and phase 14, per the brief).
- **A real Billing-responder end-to-end test** mirroring
  `FulfillmentStockEndToEndTests.cs` — time/scope; the generic wire-level
  risk is still closed (see "The known risk" above). Recommended follow-up.
- **Structured logging in `LoginThrottleOptions.FromEnvironment`** — #7's
  own review finding F1 has a logging half this repository's own
  `*Options.FromEnvironment()` convention does not carry elsewhere either;
  recorded as a deliberate, narrower port rather than silently closed (see
  the enumeration table).
- **Per-call RPC timeout overrides** — `IRpcClient.CallAsync` has no
  per-call timeout parameter; no Gateway command in this feature needs a
  different timeout than the shared default, so the parameter was not
  added with no caller to exercise it.
- **backlog id 56 (env reads)** — explicitly named in the brief as NOT
  riding this feature; `GatewayOptions`/`Program.cs` follow the same
  env-var-with-a-documented-default shape every other service's own
  composition root already uses, and no new guard mechanism was built here.

## Surprises

- **A real, live bug was found by `OrdersHttpTests.GetOrder_ForAnIdThisGatewayNeverIssued_Answers404`**
  during writing, not during arming: `ProblemJsonMiddleware.Classify` had no
  `case` for `GatewayNotFoundError` at all, so the genuine-404 path fell
  through to the `default` case and answered `500 INTERNAL_ERROR` instead of
  `404`. Fixed by adding the missing case (`ProblemJsonMiddleware.cs`); this
  is exactly the value the acceptance-bullet-driven integration tests are
  for — a unit test of `GetOrderQueryHandler` alone would have shown the
  right domain-level classification (`GetOrderResultKind.Unknown`) and never
  caught the missing wire-level translation.
- **ASP.NET Core's `EndpointDataSource` singleton is not reliably populated
  before the app has processed a request** — the contract test originally
  read `app.Services.GetRequiredService<EndpointDataSource>()` and found
  ZERO endpoints even after `GatewayHost.Configure` had mapped all 14.
  Reading `((IEndpointRouteBuilder)app).DataSources` directly (what every
  `Map*` call actually appends to) is the reliable seam, and is what both
  `OpenApiContractTests` and the F8 all-routes sweep in
  `DocsAndAnonymousRouteHttpTests` use.
- **Microsoft.AspNetCore.RateLimiting's built-in fixed-window limiter maps
  almost mechanically onto `@nestjs/throttler`'s `ThrottlerGuard`** — same
  "route-scoped policy, `OnRejected`/exception-filter callback writes the
  429 body" shape, and needed no new package, unlike the JWT case.
- **`IHost` returned by `HostApplicationBuilder.Build()`/`FulfillmentHost.CreateBuilder`
  does not implement `IAsyncDisposable`** at the interface level (only the
  concrete `Host` class does) — `await using` on an `IHost`-typed variable
  does not compile; every sibling `*.IntegrationTests` project in this
  repository calls `await host.StopAsync()` explicitly instead, which
  `FulfillmentStockEndToEndTests.cs` now follows too.

---

## Fix round — response to `progress/review_gateway_rest_auth.md`

Feature set back to `in_progress` by the reviewer (D1–D3 blocking, D4–D9
non-blocking but required by the leader's brief). This section documents
the fix round only; everything above is the original implementation report
and is otherwise unchanged. Scope: `src/Gateway/`, `tests/Gateway.UnitTests/`,
`tests/Gateway.IntegrationTests/`, `.env.example`, `docker-compose.infra.yml`
— no other service touched.

### D1 — `GatewaySubjects`/payload guard, ported from #7's rejection shape

**New:** `tests/Gateway.UnitTests/AsyncApiSchema.cs`, `RepositoryPaths.cs`
(the established per-project duplicated pair — same content as
`tests/Billing.UnitTests/`'s own copies, `namespace` retargeted),
`GatewaySubjectsTests.cs` (all 8 subjects, in the exact shape of
`tests/Fulfillment.UnitTests/StockSubjectsTests.cs` — reads
`specs/shared/asyncapi.yaml` as text, extracts each channel's own
`address:` line), `GatewayRpcPayloadTests.cs` (26 request/reply/nested
schemas via reflection over the record's own properties — the
`tests/Billing.UnitTests/CreditRpcPayloadTests.cs:29-35` shape the review
named, not `StockRpcPayloadTests`'s hand-typed-list shape, precisely so a
transcription slip cannot be "confirmed" against a second hand-typed copy
— backlog id 64's own point). `InvoiceViewPayload` is asserted as a
documented SUPERSET of `asyncapi.yaml`'s `InvoiceView` (D8's own finding —
the extra `lines` member is legal per `openapi.yaml`, so a strict-equality
assertion there would have been a false failure, not a stronger guard).

**Armed** (`GatewaySubjectsTests`): `GatewaySubjects.cs` — `OrdersCancel`
`"orders.cancel"` → `"orders.cancelled"`. `dotnet test --filter
GatewaySubjectsTests` FAILED verbatim:
```
Assert.Equal() Failure: Strings differ
                     ↓ (pos 13)
Expected: "orders.cancelled"
Actual:   "orders.cancel"
```
Restored from `cp` backup, `cmp`-verified identical, `--no-incremental`
rebuild, re-ran green (8/8).

**Armed** (`GatewayRpcPayloadTests`): `GatewayRpcPayloads.cs` —
`PaymentRegisterReplyPayload`'s `OrderReference` → `OrderReferenceRenamed`.
FAILED on BOTH the main theory case and the `G5` arming case verbatim:
```
Assert.Equal() Failure: HashSets differ
Expected: ["outcome", "paymentReference", "invoiceReference", "orderReference", "invoiceStatus", ···]
Actual:   ["outcome", "paymentReference", "invoiceReference", "orderReferenceRenamed", "invoiceStatus", ···]
```
```
Assert.NotEqual() Failure: HashSets are equal
```
Restored from `cp` backup, `cmp`-verified identical, `--no-incremental`
rebuild, re-ran green (151/151 in `Gateway.UnitTests` at that point).

### D2 — anonymous-route sweep no longer filters by the property it tests

**Changed:** `tests/Gateway.IntegrationTests/DocsAndAnonymousRouteHttpTests.cs`.
The public set is now a literal (`{"POST /auth/login", "GET /docs"}`); the
protected set is derived by SUBTRACTING that literal from every mapped
operation, never by re-reading `IAllowAnonymous`; a separate assertion
pins the `IAllowAnonymous` set itself to exactly the same literal pair —
so a route wrongly made public fails on two independent checks, not one.

**Armed:** re-ran the reviewer's own probe P2 (`.AllowAnonymous()` added
to `GET /orders` in `OrdersEndpoints.cs`). FAILED verbatim:
```
Assert.Equal() Failure: HashSets differ
Expected: ["POST /auth/login", "GET /docs"]
Actual:   ["POST /auth/login", "GET /orders", "GET /docs"]
```
(the loop assertion would also have failed on the same run — `GET /orders`
stayed in the protected loop and answered 200 where 401 was asserted, but
xUnit stopped at the first failed assertion). Restored from `cp` backup,
`cmp`-verified identical, `--no-incremental` rebuild, re-ran green (2/2).

### D3 — `JwtOptions.FromEnvironment()`

**Changed:** `src/Gateway/Infrastructure/Auth/JwtOptions.cs` (added
`FromEnvironment()`, reading `JWT_SECRET`/`JWT_ISSUER`/`JWT_EXPIRES_IN`,
same defaults as before — `otc_dev_jwt_secret_change_me` /
`order-to-cash` / 3600), `src/Gateway/Infrastructure/GatewayOptions.cs`
(`Jwt` property `get;` → `get; set;`, matching `LoginThrottle`'s own
shape), `src/Gateway/Program.cs` (`options.Jwt =
JwtOptions.FromEnvironment();`). `JWT_EXPIRES_IN` is read as an integer
count of seconds (matching `ExpiresInSeconds`'s existing type), not #7's
duration-string ("1h") — #7's own default already equals 3600 seconds, so
no behaviour is lost, only a string-duration parser #8 has no other use
for. **New:** `tests/Gateway.UnitTests/JwtOptionsTests.cs` (5 cases, in
the shape of `LoginThrottleOptionsTests`: defaults, all-three-set,
4×malformed-lifetime→fallback theory, empty-string secret/issuer→fallback).
`.env.example`: a new `JWT_SECRET`/`JWT_EXPIRES_IN`/`JWT_ISSUER` block
after the `GATEWAY_OPERATOR_*` lines. `docker-compose.infra.yml`: a
comment (deliberately NOT an `environment:` entry on the `n8n` service —
n8n calls only `POST /auth/login` over REST and never needs the signing
secret; adding it there would have been exactly the "declared but read by
nothing" defect class `progress/history.md`'s D2 warns against) noting the
Gateway process itself reads these three variables directly, and that no
`gateway:` service block exists yet in this compose file (no
`docker-compose.apps.yml` in #8, unlike #7's — the Gateway is not
containerized here).

No corruption arming applies to this one directly (it is not a
fact-emitting branch); `JwtOptionsTests` differentiates real values by
distinct literals, so a broken read fails the "reads all three" case
intrinsically.

### D4 — #7 test enumeration re-run, corrected, and the four `orders.integration.spec.ts` guards ported

**Enumeration re-run** (search result, not prose):
```
$ find ../order-to-cash-nestjs/apps/gateway/src -iname "*.spec.ts" | wc -l
44
```
44 files, classified individually (no grouping this time — the prior
round's two group-count errors, "6 files"/"3 files" when the true counts
were 7/5, are exactly what grouping hid):

| # | #7 file | Classification |
|---|---|---|
| 1 | `application/commands/cancel-order.command.spec.ts` | PORTED — `CancelOrderCommandHandlerTests.cs` |
| 2 | `application/commands/login.command.spec.ts` | PORTED — `LoginAndCurrentUserHandlerTests.cs` |
| 3 | `application/commands/place-order.command.spec.ts` | PORTED — `PlaceOrderCommandHandlerTests.cs` |
| 4 | `application/commands/register-payment.command.spec.ts` | PORTED — `RegisterPaymentCommandHandlerTests.cs` |
| 5 | `application/commands/replenish-stock.command.spec.ts` | PORTED — `ReadOnlyQueryHandlerTests.cs` |
| 6 | `application/queries/get-current-user.query.spec.ts` | PORTED — `LoginAndCurrentUserHandlerTests.cs` |
| 7 | `application/queries/get-order.query.spec.ts` | PORTED — `GetOrderQueryHandlerTests.cs` |
| 8 | `application/queries/list-catalog.query.spec.ts` | PORTED — `ReadOnlyQueryHandlerTests.cs` |
| 9 | `application/queries/list-credits.query.spec.ts` | PORTED — `ReadOnlyQueryHandlerTests.cs` |
| 10 | `application/queries/list-invoices.query.spec.ts` | PORTED — `ReadOnlyQueryHandlerTests.cs` |
| 11 | `application/queries/list-orders.query.spec.ts` | PORTED — `ListOrdersQueryHandlerTests.cs` |
| 12 | `application/queries/list-stock.query.spec.ts` | PORTED — `ReadOnlyQueryHandlerTests.cs` |
| 13 | `application/stream-hub.spec.ts` | N/A — feature 26 (`gateway_sse_push`) |
| 14 | `auth.integration.spec.ts` | PORTED — `AuthAndRateLimitHttpTests.cs` + `DocsAndAnonymousRouteHttpTests.cs` |
| 15 | `auth-rate-limit.integration.spec.ts` | PORTED — `AuthAndRateLimitHttpTests.cs` |
| 16 | `billing-fulfillment.integration.spec.ts` | PARTIALLY PORTED — `FulfillmentStockEndToEndTests.cs`; Billing's own real-responder e2e not built (recommended follow-up, unchanged this round) |
| 17 | `black-box-api.integration.spec.ts` | PARTIALLY PORTED — scenario 4 only (`OrdersHttpTests.cs` + `AuthAndRateLimitHttpTests.cs`); full happy/compensation/idempotency paths N/A (belongs to `order_saga_orchestrator`, already closed) |
| 18 | `contract-drift.integration.spec.ts` | PORTED, +1 case — `OpenApiContractTests.cs` + `DocsAndAnonymousRouteHttpTests.cs`; F9 N/A (ledger row 6, now probed — see D7) |
| 19 | `domain/auth/operator-credentials.spec.ts` | PORTED — `OperatorAuthenticatorTests.cs` |
| 20 | `domain/orders/issued-order-window.spec.ts` | PORTED — `IssuedOrderWindowTests.cs` |
| 21 | `domain/problem/rpc-error-mapping.spec.ts` | PORTED — `RpcErrorClassifierTests.cs` |
| 22 | `domain/projection/order-read-model-mapper.spec.ts` | PORTED — `OrderReadModelMapperTests.cs` |
| 23 | `domain/sse/cursor.spec.ts` | N/A — feature 26 |
| 24 | `domain/sse/replay-buffer.spec.ts` | N/A — feature 26 |
| 25 | `health-probes.integration.spec.ts` | N/A — phase 14 (`observability_reliability`) |
| 26 | `infrastructure/auth/jwt-token.adapter.spec.ts` | PORTED, +4 cases this round — `JwtTokenServiceTests.cs` (11: +alg:none/empty-signature, +tampered-payload — D9) |
| 27 | `infrastructure/auth/login-throttle.config.spec.ts` | PARTIALLY PORTED — `LoginThrottleOptionsTests.cs`; structured-logging half deliberately not ported (unchanged, this repository's own `*Options.FromEnvironment()` convention) |
| 28 | `infrastructure/health/health-checks.spec.ts` | N/A — phase 14 |
| 29 | `infrastructure/messaging/nats-rpc-client.adapter.spec.ts` | PORTED, over a real broker — `NatsRpcClientIntegrationTests.cs` |
| 30 | `infrastructure/messaging/nats-stream-signal.adapter.spec.ts` | N/A — feature 26 |
| 31 | `infrastructure/messaging/nats-stream-signal-log-trace-id.spec.ts` | N/A — feature 26 (previously unaccounted; now classified) |
| 32 | `infrastructure/observability/http-instrumentation.spec.ts` | N/A — feature 27 (OTel) |
| 33 | `infrastructure/persistence/mongo-order-read-model.adapter.spec.ts` | PORTED, more strongly — `MongoOrderReadModelIntegrationTests.cs` + `MongoOrderReadModelMappingTests.cs` |
| 34 | `money-representation.integration.spec.ts` | NOT PORTED, structurally prevented instead — ledger row 5 |
| 35 | `no-write-database-client.spec.ts` | PORTED, adapted — `WriteDatabaseAbsenceTests.cs` |
| 36 | `orders.integration.spec.ts` | **PORTED at HTTP level this round** — see below, the material miss last round |
| 37 | `presentation/guards/jwt-auth.guard.spec.ts` | PORTED at a stronger level — `AuthAndRateLimitHttpTests.cs` + `DocsAndAnonymousRouteHttpTests.cs` |
| 38 | `presentation/pagination.spec.ts` | PORTED — `RequestParsingTests.cs` |
| 39 | `presentation/problem-json.filter.spec.ts` | PORTED — `ProblemJsonMiddlewareClassificationTests.cs` + HTTP echoes, now including `InvoicesHttpTests.cs` (D6) |
| 40 | `presentation/request-latency.interceptor.spec.ts` | N/A — feature 27 |
| 41 | `saga-e2e-verification.integration.spec.ts` | N/A — `order_saga_orchestrator`, already closed |
| 42 | `spawn-real-service-smoke.integration.spec.ts` | N/A — `order_saga_orchestrator` / later phases |
| 43 | `stream.integration.spec.ts` | N/A — feature 26 |
| 44 | `stream-projector-e2e.integration.spec.ts` | N/A — feature 26 |

All 44 accounted, one line each. The two group-count errors are gone
(nothing is grouped this time); the two previously-"unaccounted"
`infrastructure/messaging/nats-stream-signal*` files are now rows 30/31,
both N/A to this feature (feature 26's scope, confirmed by content, not
merely by name).

**Row 36, `orders.integration.spec.ts` — the four guards, ported, not
declined:**

| #7 case | #8 guard added this round |
|---|---|
| `:211` `POST /orders/{id}/cancel` → 202 + `compensationPlanned` | `OrdersHttpTests.CancelOrder_TranslatesToOrdersCancel_AndReturns202WithCompensationPlanned` |
| `:200` malformed body → 400 `VALIDATION_FAILED` before any RPC call | `OrdersHttpTests.PlaceOrder_WithNoLines_Returns400ValidationFailed_BeforeAnyRpcCall` |
| `:129` `GET /orders` excludes a placeholder | `OrdersHttpTests.ListOrders_ExcludesAPlaceholderDocumentWithNoOrderReferenceYet` |
| `:117` `GET /orders/{id}` returns the projected document | `OrdersHttpTests.GetOrder_ReturnsTheProjectedDocument_OnceOneExistsInTheReadModel` |

All four run over real Kestrel with `RecordingRpcClient`/
`InMemoryOrderReadModel` substituted after `CreateBuilder` (`OrdersHttpTests`'s
own established seam — extended with `LastMeta`/`CallCount` on
`RecordingRpcClient` and a configurable `ListItems` on
`InMemoryOrderReadModel`). **Armed, all four:**

1. `CancelOrderCommand.cs`: `reply.CompensationPlanned ?? []` → `[]`.
   FAILED: `Assert.Equal() Failure ... Expected: ["stock_release"] Actual: []`.
2. `OrdersEndpoints.cs`: `Count: > 0` → `Count: >= 0` (defeats the
   guard without breaking nullable analysis, since `is not { Count: >= 0 }`
   still excludes `null`). FAILED: `Expected: BadRequest Actual:
   InternalServerError` (the RPC handler's deliberate throw fired,
   confirming the call really would have reached `orders.create`).
3. `ListOrdersQuery.cs`: substituted a real, `doc.OrderId`-carrying
   `OrderSummaryView` for a `null` mapper result instead of dropping it.
   FAILED: `Assert.DoesNotContain() Failure: Sub-string found ...
   "items":[{"orderId":"cc7c86cd-..."`.
4. `OrdersEndpoints.cs`: `Results.Json(result.Detail)` →
   `Results.Json(new { })`. FAILED:
   `KeyNotFoundException: The given key 'orderReference' was not present`.

All four restored from `cp` backups, `cmp`-verified identical,
`--no-incremental` rebuilds, re-ran green (30/30 in
`Gateway.IntegrationTests` at that point).

### D5 — payload-corruption survivor on `initialAmount`/`initialDiscount`

**Changed:** `tests/Gateway.UnitTests/OrderReadModelMapperTests.cs` —
`CompleteDocument()`'s `Totals` fixture `(124250, 0, 124250)` →
`(124950, 700, 124250)` (distinct, non-zero on all three fields), and
`ToOrderSummary_ReturnsARow_ForAFullyProjectedDocument` now asserts
`InitialAmount`/`InitialDiscount`/`TotalAmount` individually, not just
`TotalAmount`.

**Armed:** re-ran the reviewer's own probe P3
(`OrderReadModelMapper.cs`'s `ToOrderSummary` — transposed
`InitialAmount`/`InitialDiscount`). FAILED verbatim:
```
Assert.Equal() Failure: Values differ
Expected: 124950
Actual:   700
```
Restored from `cp` backup, `cmp`-verified identical, `--no-incremental`
rebuild, re-ran green (158/158 in `Gateway.UnitTests` at that point).
(`ListOrdersQueryHandlerTests.cs`'s own `(100, 0, 100)` fixture was left
unchanged — that file asserts only item COUNTS, never totals content, so
distinct values there would add no guard.)

### D6 — `POST /invoices/{id}/payments` had no HTTP-level test; a tampered token; `UnknownOperatorError`

**New:** `tests/Gateway.IntegrationTests/InvoicesHttpTests.cs` (4 cases:
404 on a genuinely-exhausted scan, 503 `SCAN_BUDGET_EXCEEDED` on a
budget-exceeded scan, 503 `UPSTREAM_UNAVAILABLE` when the invoice's order
is not yet projected, and the 201 happy path — all over real Kestrel, a
local `QueuedRpcClient`/`InMemoryOrderReadModel`, the same substitution
seam `OrdersHttpTests` establishes). **Armed:** flipped
`ProblemJsonMiddleware.cs`'s `InvoiceScanBudgetExceededError` case from
503/`SCAN_BUDGET_EXCEEDED` to 404/`NOT_FOUND` — the exact confusion D6
exists to prevent. FAILED verbatim:
```
Assert.Equal() Failure: Values differ
Expected: ServiceUnavailable
Actual:   NotFound
```
Restored from `cp` backup, `cmp`-verified identical, `--no-incremental`
rebuild, re-ran green (4/4).

**New:** `AuthAndRateLimitHttpTests.Me_WithATamperedBearerToken_Returns401`
— flips one character in the MIDDLE of a genuinely-issued token (not the
last character of the signature: the first attempt flipped the token's
final base64url character and still verified successfully, because that
character's bits fall in the padding-affected tail of a 32-byte HMACSHA256
signature's base64url encoding and did not change the decoded bytes —
recorded here as the reason the test flips a middle character instead).
Self-arming by construction: the test IS a probe of the real `Verify` path
against a genuinely different token, not a corrupt/restore cycle.

**New:** `ProblemJsonMiddlewareClassificationTests.Classify_UnknownOperatorError_MapsTo500InternalError`
— the one built branch (of ten) that had no test at any level. No arming
is meaningful here beyond running it: the review itself notes this branch
is behaviourally identical to `default`, so no mutation could distinguish
"case removed" from "case present" — the ask was only that it be tested at
all, which it now is.

### D7 — ledger row 6 (route precedence), probed and direction stated

**New:** `tests/Gateway.IntegrationTests/RoutePrecedenceTests.cs` — a
standalone `WebApplication` (not `GatewayHost`) with `/probe/{id}` mapped
BEFORE `/probe/literal` (registration order REVERSED relative to how
`OrdersEndpoints.cs` would register `/orders/stream` after `/orders/{id}`
— the ordering under which Express, #7's own F9 finding, would swallow the
literal segment). Real Kestrel round trip (`app.StartAsync()`, a real
`HttpClient`), because precedence is a match-time behaviour, not something
visible in the registered route table. `GET /probe/literal` answers
`"literal"`, not `"parameter:literal"` — the claim holds under the
REVERSED direction, and this file records which direction was probed, per
CLAUDE.md's amended ledger rule. The ledger's row 6 in the ORIGINAL
implementation report above is otherwise unchanged; this test is its
guard.

### D9 — `alg: none` / tampered-payload cases; ledger row 3

**Changed:** `tests/Gateway.UnitTests/JwtTokenServiceTests.cs` — two new
cases:
- `Verify_Throws_ForAnAlgNoneForgedToken_WithAnEmptySignatureSegment` — a
  hand-built `{"alg":"none",...}` header with a genuinely empty signature
  segment. `JwtTokenService.Verify` never parses the header at all (`Sign`
  is unconditionally `HMACSHA256`), so the empty provided signature simply
  fails the length check against the real 32-byte signature before
  `FixedTimeEquals` ever runs.
- `Verify_Throws_WhenThePayloadSubjectIsTamperedWith_EvenIfTheSignatureBytesAreUntouched`
  — `sub` substituted to `"admin"` in an otherwise-genuine issued token,
  signature segment left untouched. Both self-arm by construction (each is
  a direct probe of production code against a hand-crafted forged input;
  a vulnerable `Verify` would pass either).

**Ledger row 3 correction** (the original implementation report's own
table, row 3, "Guard" column): the row previously cited only
`JwtTokenServiceTests.cs`'s 7 pre-existing cases without naming what
`jsonwebtoken` supplied that this class had to re-derive by hand. Named
now, per D9: **expiry enforcement** (`exp` checked against `clock.UtcNow`
— `Verify_Throws_WhenTheTokenHasExpired`), **issuer enforcement** (`iss`
compared to `options.Value.Issuer` — `Verify_Throws_WhenTheIssuerDiffers`),
**structural rejection** (malformed segment counts, non-base64url,
non-JSON — the `Verify_Throws_ForAStructurallyMalformedToken` theory), and
**algorithm pinning** (`Sign`/`Verify` are unconditionally HMACSHA256, the
header's own `alg` claim is never read — `Verify_Throws_ForAnAlgNoneForgedToken_WithAnEmptySignatureSegment`,
new this round). All four are now guarded, closing D9's "the row does not
enumerate what the library supplied" finding.

### `R<n>` → test mapping — unchanged this round

No `R<n>` moves from TODO to DONE this round: R53/R54/R55's gateway halves
were already marked DONE by the original implementation and the
reviewer's own `R<n>` walk verified them satisfied. This round's tests
strengthen those rows' proof (an HTTP-level path now exists where only a
unit/adapter-level one did before) without changing their Status column,
so `specs/shared/test-matrix.md` is untouched this round — consistent with
the leader's brief: "update it if this round adds tests that satisfy an
R<n>", and none of D1–D9 does.

### Final verification

```
$ dotnet test tests/Gateway.UnitTests/Gateway.UnitTests.csproj --no-build
Passed! - Failed: 0, Passed: 161, Skipped: 0, Total: 161

$ dotnet test tests/Gateway.IntegrationTests/Gateway.IntegrationTests.csproj --no-build
Passed! - Failed: 0, Passed: 36, Skipped: 0, Total: 36
```

`./quality.sh` — full run, tree quiescent:
```
── 1. Format check ── dotnet format --verify-no-changes: clean
── 2. Build ── dotnet build: succeeded
── 3. Test + coverage ── dotnet test: all tests passed
── 4. Coverage summary ── per-project line coverage 0.0%–97.2% (18 reports)

$ grep -oE "Failed: *[0-9]+, Passed: *[0-9]+, Skipped: *[0-9]+, Total: *[0-9]+" quality_run_fix.log \
    | awk -F'Total: *' '{sum+=$2; count++} END {print "projects:", count, "total tests:", sum}'
projects: 18 total tests: 1481

$ grep -oE "Failed: *[0-9]+" quality_run_fix.log | awk -F': *' '{sum+=$2} END {print "total failed:", sum}'
total failed: 0
```

**1481 tests across 18 projects, 0 failed, 0 skipped** — up from this
feature's own prior full run of 1425 (56 new tests this round: 8
`GatewaySubjectsTests` + 28 `GatewayRpcPayloadTests` + 7
`JwtOptionsTests` + 4 `OrdersHttpTests` + 4 `InvoicesHttpTests` + 1
`AuthAndRateLimitHttpTests` + 1 `RoutePrecedenceTests` + 1
`ProblemJsonMiddlewareClassificationTests` + 2 `JwtTokenServiceTests` =
56, matching the delta exactly).

`./init.sh` — exits 0 (`init.sh: environment and state are coherent`).

### What was NOT done this round, and why

- **`specs/shared/test-matrix.md`** — deliberately untouched; no `R<n>`
  changes Status this round (see above).
- **A real Billing-responder end-to-end test** mirroring
  `FulfillmentStockEndToEndTests.cs` — still the original report's own
  recommended follow-up, not part of D1–D9, not attempted this round.
- **The structured-logging half of `login-throttle.config.spec.ts`** —
  unchanged; D1–D9 did not ask for it, and it remains a deliberate,
  recorded gap against this repository's own `*Options.FromEnvironment()`
  convention.
- **An HTTP-level test for `ProblemJsonMiddleware`'s `default` fallback
  branch** — D6 named only the `Invoice*`/`OrderNotYetProjected` branches,
  the tampered-token case and `UnknownOperatorError`; the bare
  `default`/`InvalidOperationException` fallback was not in D6's list and
  was not added.

### Surprises this round

- **Flipping a JWT's LAST base64url character does not always change the
  decoded signature bytes.** The first `Me_WithATamperedBearerToken_Returns401`
  attempt flipped `token[^1]` and the (deliberately) tampered token still
  verified successfully — HMACSHA256 produces a 32-byte (256-bit)
  signature, and base64url's last character in a length-not-a-multiple-of-3
  group can encode bits that decoding discards. Flipping a character in
  the MIDDLE of the token (this test lands in the payload segment, which
  works just as well — a modified payload no longer matches its own
  signature either) is the reliable mutation; recorded here so the next
  feature that hand-tampers a JWT for a test does not lose an hour to the
  same false negative.
- **A validation guard rewritten as `Count: >= 0` instead of `false &&
  ...`** was needed to arm D4's "no RPC call" case without breaking
  nullable-reference analysis: disabling the guard outright
  (`if (false && body.Lines is not { Count: > 0 })`) makes the compiler
  lose the null-exclusion the original `is not {...}` pattern provided for
  `body.Lines` downstream, and `TreatWarningsAsErrors` turns that into a
  build failure rather than a runtime symptom — a cheaper diagnostic than
  it might have been, but one worth naming for the next arming pass that
  disables a null-checking pattern match.
