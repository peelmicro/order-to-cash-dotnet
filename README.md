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
OrderToCash.sln          the six services + SharedKernel + Contracts + Seed
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
| 2 | Harness layer, copied from #7 and re-pointed | ⬜ |
| 3 | Shared specification, copied verbatim from #7 | ⬜ |
| 4 | Infrastructure compose + Kafka topics & NATS subjects | ⬜ |
| 5 | Solution scaffold, SharedKernel, Contracts, architecture tests | ⬜ |
| 6 | EF Core models + migrations for the four write databases | ⬜ |
| 7 | Deterministic seed job | ⬜ |
| 8 | Orders service + saga orchestrator | ⬜ |
| 9 | Fulfillment service | ⬜ |
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

## Licence

MIT.
