# Order To Cash — .NET

> 🚧 **Under construction.** This repository is being built phase by phase; the table at the bottom tracks exactly how far it has got. Everything described as done is done and tested — nothing here is aspirational.

An **order-to-cash lifecycle backbone** for a B2B EDI / e-invoicing platform, built as event-driven microservices. It models the classic EDI exchange as a distributed workflow:

**Order (ORDERS) → Stock reservation → Credit check → Order confirmation (ORDRSP) → Despatch advice (DESADV) → Invoice (INVOIC) → Payment (remittance)**

— with an orchestrated **saga** coordinating the flow across services and **compensating** when a step fails. Deliberately B2B in shape: the retailer never pays at order time; a credit check gates despatch, and payment arrives at the end of the cycle, within payment terms.

## The trilogy, and what makes this repository different

This is **assessment #8 of three**, all implementing the *same* specification on different stacks:

| # | Backend | Frontend | Write DB | Repository |
|---|---------|----------|----------|------------|
| 7 | NestJS 11 | Nuxt 4 + shadcn-vue | MySQL 8 | [`peelmicro/order-to-cash-nestjs`](https://github.com/peelmicro/order-to-cash-nestjs) — complete |
| **8** | **.NET 10** | **Next.js + shadcn/ui** | **MS-SQL Server** | **this repository** |
| 9 | Python (FastAPI) | Angular + spartan/ui | PostgreSQL | not started |

#7 wrote the stack-agnostic specification and the AI agent harness. **#8 does not start from scratch, and that is the point.** The specification (`specs/shared/`), the harness, the four n8n demo workflows and the stack-agnostic infrastructure configuration are **copied from #7, not rewritten** — so this repository is the trilogy's first empirical answer to a question the process literature mostly asserts rather than measures:

> When a specification and an agent harness are genuinely mature, how much does re-implementing the same system on a new stack actually accelerate — and which parts do not speed up at all?

Per-feature effort (sessions, wall-clock) is recorded in `progress/history.md` against #7's baseline, and the README will close with the comparison table and an honest reading of it, including what was **not** faster.

Any place where the .NET implementation proves the shared specification wrong or incomplete is a **spec amendment**: an explicit commit here, and a back-port to #7. Never a silent fork.

## Spec amendments

Where the .NET implementation proves the shared specification wrong or incomplete, the change is an **amendment**: explicit, committed on its own, and applied to every repository of the trilogy in the same session — never a silent fork. Each carries a stable `SA-n` id and an entry in the `progress/history.md` of every repository it touches.

| Id | Raised | Touches | What was wrong |
|---|---|---|---|
| `SA-1` | #8, Phase 3 | `specs/shared/test-matrix.md` — the reset-recipe paragraph only | The recipe told a new assessment which prose to delete by listing the specific paragraphs *in that copy*. Correct when read, false once followed — the next assessment would inherit an inventory of content already gone. Reworded from an inventory of the copy into a description of the class, so the instruction stays true in every copy. Applied to #7 and #8 identically. |

## Tech stack

| Layer | Technology |
|-------|-----------|
| Backend runtime | .NET 10 (LTS) — ASP.NET Core for services with an HTTP/RPC surface, Worker Services for pure consumers |
| CQRS (in-process) | Hand-rolled command/query dispatcher — no MediatR (commercial licence; trade-off documented) |
| Inter-service transport | Confluent.Kafka (domain facts) + NATS.Net (request-reply RPC, core only — no JetStream) |
| Write databases | MS-SQL Server — `otc_orders`, `otc_fulfillment`, `otc_billing`, `otc_notifications` |
| ORM | Entity Framework Core + SQL Server provider, one `DbContext` and migration set per service |
| Read model | MongoDB — the denormalised `order_timeline` collection (CQRS query side) |
| Saga orchestrator | Hand-rolled, in the Orders service — no MassTransit (spec parity is the point of the trilogy) |
| Observability | OpenTelemetry .NET → OTel Collector → Jaeger (traces) + Prometheus → Grafana |
| Email | MailKit → Mailpit container; console adapter behind the same port for tests |
| Frontend | Next.js (App Router) + shadcn/ui + TanStack Query + Tailwind CSS v4, SSE with reconnect |
| Backend testing | xUnit + Testcontainers for .NET (real MsSql / Kafka / NATS / MongoDB — brokers are never mocked) |
| Web testing | Vitest + React Testing Library (components), Playwright (end-to-end) — no Jest anywhere |
| Architecture enforcement | NetArchTest.Rules — `Domain` may not reference EF Core, Kafka, NATS or ASP.NET Core |
| Demo automation | n8n — the same four workflow JSONs as #7, Gateway REST API only |
| Infrastructure | Docker Compose |

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | **10.0.111** | Pinned in `global.json` (`rollForward: latestPatch`) |
| Node.js | **24.19.0** (LTS) | Pinned in `.nvmrc` — `nvm use`. **Web app only**; the backend has no Node dependency |
| pnpm | **11.22.0** | Via corepack. Used only inside `apps/web` |
| Docker | 29.x + Compose | The MS-SQL container alone wants ~2 GB RAM |

## Repository layout

```
OrderToCash.sln          the six services + SharedKernel + Contracts + Cqrs + Seed
src/                     one project per service, Clean Architecture folders inside each
tests/                   architecture, unit, integration, API and end-to-end tests
apps/web/                Next.js app (the only place pnpm lives)
specs/shared/            the stack-agnostic specification — copied verbatim from #7
specs/<feature>/         per-feature triple-doc (EARS requirements, design, tasks)
progress/                the agent harness's external memory, including the effort records
infra/, n8n/             compose infrastructure and the reused demo workflows
docs/PROCESS.md          how this project is built — the process guide
```

## How this is being built

The development **process is a deliverable**, not a footnote: Spec-Driven Development plus an agent harness with a backlog state machine (`feature_list.json`, max one feature in progress), external memory (`progress/`), a specification written before the code, and separate leader / spec-author / implementer / reviewer subagents each pinned to an explicit model. Every feature passes a human approval gate at its specification and again before its commit. `docs/PROCESS.md` explains all of it; the git history is the evidence, and for this repository it must show **harness first, specification copy second, code after**.

## Build progress

| Phase | What | Status |
|-------|------|--------|
| 1 | Environment & repository | ✅ SDK/Node pins verified adversarially, account-explicit remote, `.gitignore` proven not to swallow source |
| 2 | Harness layer, copied from #7 and re-pointed | ✅ 42-feature backlog reset, `init.sh` verified to exit 1 on all eight break cases, C7 inverted to spec-reuse fidelity |
| 3 | Shared specification, copied verbatim from #7 | ✅ six of seven files byte-identical (`cmp`-proven); `test-matrix.md` reset by #7's own recipe; zero stack leaks found; `SA-1` raised and applied to both repos |
| 4 | Infrastructure compose + Kafka topics & NATS subjects | ✅ 15 services, 36s cold to all-healthy, MS-SQL bootstrap written from scratch (the image has no init hook), topology derived from the spec |
| 5 | Solution scaffold, SharedKernel, Contracts, architecture tests | ✅ 65 tests, 12 armed architecture rules, and a wire-parity oracle of 12 real #7 envelopes |
| 6 | EF Core models + migrations for the four write databases | ✅ 20 tables, 60 integration tests against real MS-SQL, cross-context reliability-table parity asserted from the live schema |
| 7 | Deterministic seed job | ✅ identifiers provably byte-identical to #7's, derived by the same SHA-256 scheme; 3 currencies, 12 products, 7 retailers, 22 companies, 215 stock rows, 6 sample orders and their read-model documents |
| 8 | Orders service + saga orchestrator | ✅ aggregate, hand-rolled dispatcher, transactional outbox, `orders.create` acceptance, the saga with both compensation paths, and terminal-vs-retryable command classification — 7 features, 3 of them defects found in already-closed work; 16 armed architecture rules |
| 9 | Fulfillment service | 🚧 `StockItem` aggregate, reservation lifecycle and the `fulfillment.stock.*` RPC responder done — and starting it resumed four saga commands that had been parked since Phase 8, unattended |
| 10 | Billing service | ⬜ |
| 11 | Notifications service | ⬜ |
| 12 | Projector service + MongoDB read model | ⬜ |
| 13 | Gateway / BFF | ⬜ |
| 14 | Health checks, OTel propagation, retry + DLQ | ⬜ |
| 15 | End-to-end saga verification | ⬜ |
| 16 | Next.js web app | ⬜ |
| 17 | Web component tests | ⬜ |
| 18 | API tests through the Gateway | ⬜ |
| 19 | Playwright end-to-end tests | ⬜ |
| 20 | n8n demo workflows, reused unchanged | ⬜ |
| 21 | Quality gates (analyzers, format, coverage) | ⬜ |
| 22 | Prometheus, Grafana, Jaeger verification | ⬜ |
| 23 | Full Docker Compose | ⬜ |
| 24 | Documentation, demo recording, **#7 vs #8 benchmark** | ⬜ |
| 25 | Final checkpoint | ⬜ |

## Running what exists so far

The Orders service is the first one that runs. It has no HTTP surface yet — the Gateway is Phase 13 — so it is driven over NATS, and it needs a stand-in for the Fulfillment service that Phase 9 will build. The commands below were run exactly as written; the outputs are the real ones.

Since the saga orchestrator landed, the service also **consumes** the fact stream, so placing an order does more than reply. The `order.placed.v1` fact comes back in through the Kafka consumer, the saga issues the owed `stock.reserve` command over NATS, and — with no Fulfillment service in existence until Phase 9 — that command parks:

```sql
SELECT order_reference, command, status, attempts, last_error FROM dbo.saga_commands;
```

```
ORD-000010 | stock.reserve | parked | attempts=6 | fulfillment.stock.reserve: transport failure: no responder i...
```

Parked rows plus structured logs are the **correct** steady state, not a stall: the sweeper keeps retrying on capped backoff, and they resume unattended the moment a responder exists. The stand-in above answers `fulfillment.stock.check` — the synchronous check the acceptance path makes — and deliberately not `fulfillment.stock.reserve`, which is the saga's own command.

**That last claim is now demonstrated rather than designed.** Four commands sat parked for a day, at six attempts each. Starting the real Fulfillment service resumed all four with no operator action:

```bash
dotnet run --project src/Fulfillment    # in place of the stand-in
```

so the stand-in above is only needed until you run the real thing.

```bash
docker compose -f docker-compose.infra.yml up -d
export $(grep -E '^(MSSQL_APP_PASSWORD|MSSQL_APP_USER|MSSQL_DB_ORDERS|MSSQL_HOST|MSSQL_HOST_PORT|KAFKA_HOST_PORT|NATS_CLIENT_HOST_PORT|MONGO_.*)=' .env | xargs)

# migrations + deterministic master data (idempotent; the seed applies the migrations itself)
dotnet run --project src/Seed

# a stand-in Fulfillment, in its own terminal — Phase 9 replaces it with the real service
docker run --rm --network otcnet-net natsio/nats-box \
  nats --server nats://otcnet-nats:4222 reply fulfillment.stock.check \
  '{"available":true,"lines":[{"productCode":"PRD-0001","requested":2,"available":500,"sufficient":true}]}'

# the service, in another
dotnet run --project src/Orders

# place an order
docker run --rm --network otcnet-net natsio/nats-box \
  nats --server nats://otcnet-nats:4222 request orders.create \
  '{"retailerCode":"CarrefourEs","companyCode":"IBERFOODS","currency":"EUR","lines":[{"productCode":"PRD-0001","quantity":2}]}'
```

```json
{"orderId":"8b0670d1-...","orderReference":"ORD-000007","status":"placed","currency":"EUR","initialAmount":49998,"initialDiscount":0,"totalAmount":49998,"orderDate":"2026-09-03T18:04:27.376Z"}
```

`ORD-000007` because the seed's six sample orders already hold `ORD-000001`–`ORD-000006`; amounts are minor units, always. An `order.placed.v1` fact appears on the `otc.orders.facts.v1` Kafka topic moments later, published by the outbox relay running inside the same host — envelope fields in the order the shared contract declares, `eventId` first and `payload` last.

Two negative probes, both real output:

| Probe | Reply |
|---|---|
| Omit `lines` from the request | `{"code":"VALIDATION_FAILED","message":"orders.create request is missing or has an empty required field: lines."}` |
| Stop the stand-in, then request again | `{"code":"UNAVAILABLE","message":"fulfillment.stock.check: transport failure ... no responder is subscribed"}` — deliberately not `TIMEOUT`, a distinction Phase 8's terminal-rejection classification depends on |

One thing that does **not** work yet, on purpose: a repeated `requestId` currently creates a second order. Idempotent replay of `orders.create` is its own requirement, still `TODO` in the traceability matrix and owned by `observability_reliability` in Phase 14 — the same feature that owns it in #7; the field is carried through the command and explicitly ignored until then.

## Licence

MIT.
