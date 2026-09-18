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
| `SA-2` | #8, Phase 13 | `specs/shared/asyncapi.yaml` — one optional property on `OrderCancelledPayload` | The two halves of the shared spec contradicted each other. `openapi.yaml` promised `CancelOrderRequest.note` would be *"recorded on the timeline entry"*; `asyncapi.yaml`'s `OrderCancelledPayload` — the only fact the projector builds an `order.cancelled` timeline entry from — had no field able to carry it, so feature 41's acceptance bullet 4 was never satisfiable in either stack. An optional `note: string` added, typed as the REST side already types it. **#7 disclosed this and shipped; #8 disclosed it again and was about to** — a defect both runs found and neither fixed. Applied to #7 and #8 identically. |
| `SA-3` | #8, Phase 14 | `specs/shared/asyncapi.yaml` — the `DeadLetterHeaders` description only | The dead-letter headers `x-first-failed-at` and `x-failed-at` were a bare `$ref` to `Instant` with no definition, while every neighbouring header said what it meant — and the two assessments filled the silence differently, both green: #7 rendered the dispatch **entry** instant in all three of its copies (`apps/orders/…/fact-retry-dispatcher.ts:136`, `apps/projector/…:126`, `apps/notifications/…:123`, each read before the retry loop), #8 the first **caught failure**. Defined at the human gate: `x-first-failed-at` is the instant the FIRST processing attempt failed, `x-failed-at` the instant the final attempt failed. Placed in the schema's own description, not beside the `$ref`: a description next to `$ref` made #7's contract generator emit `string` instead of `Instant`. Applied to #7 and #8 identically; #7's commit also regenerated the contract types that SA-2 had left stale. Aligning #7's code is backlog id 75. |
| `SA-4` | #8, Phase 14 | `specs/shared/saga.md` §4.3 and §5, `openapi.yaml` `cancelOrder` and `CancelOrderResponse`, `asyncapi.yaml` `orders.cancel` and `OrdersCancelReplyPayload` — prose and table text only | The specification made an operator cancellation of a `credit_approved` or `confirmed` order race a despatch it had already requested — step 3 issues `despatch.create` in the handler that confirms — while its compensation released the credit hold **first**. If the despatch then consumed the stock, the release returned nothing and emitted no fact, and the order shipped with its credit hold already returned after a `202`. Both assessments reproduced it (#7's review Finding 1, HIGH) and neither could fix it inside the contract. Ruled at the human gate: from those two statuses the contested stock reservation is released first, Fulfillment decides `stock.release` against `despatch.create` under one lock, a despatch that wins stands and the cancellation is documented as overtaken; and a `credit.approved.v1` arriving after an operator cancellation releases that hold and advances nothing. No new fact, channel or schema shape. Applied to #7 and #8 identically, with #7's contract types regenerated. |
| `SA-5` | #8, Phase 16 | `specs/shared/openapi.yaml` — one sentence of the `info.description` Money section, prose only | The contract said clients format money *"from `currency.decimalPoints`"*, but every currency on the REST wire is a bare ISO 4217 code and `decimalPoints` travels only on an internal NATS reply, so the specification named a source no client can reach. Both assessments hit it: #7 hard-coded a 2-decimal exponent and noted it only in a code comment; #8's web app read it from `Intl`, whose CLDR display digits differ from ISO 4217 for about 15 codes (corrected under backlog id 103). Ruled at the human gate: the sentence now names the currency's ISO 4217 minor-unit exponent as the source. No schema, path or wire shape changed; both repositories' generated contract types were re-checked and are unchanged. Applied to #7 and #8 identically, with #7's formatting code aligned under backlog id 97. |

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
apps/web/                Next.js app, with its own package.json and lockfile
package.json             command shortcuts only (pnpm run lists them), modelled on #7's
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
| 9 | Fulfillment service | ✅ `StockItem` aggregate, reservation lifecycle, the `fulfillment.stock.*` responder and DESADV creation — starting it resumed four saga commands parked since Phase 8, unattended |
| 10 | Billing service | ✅ `BuyerCredit` and its ledger, the `.99` simulator, the `Invoice` aggregate and remittance intake — **the order-to-cash cycle now runs end to end**, verified live: an order reaches `completed` unattended on a payment arriving from outside the system, with no internal timer anywhere |
| 11 | Notifications service | ✅ MailKit into Mailpit with a console adapter behind the same port, seven templated emails, and idempotency by `eventId` against a durable ledger — every notified fact verified in the real inbox |
| 12 | Projector service + MongoDB read model | ✅ every fact into `order_timeline`, idempotent by `eventId`, ordered by a **recorded causal edge** rather than by clock (#7's own amendment, folded in at first draft instead of eight phases late), with placeholders for facts that arrive before their order — plus the repository's first committed **mutation instrument**. Across the phase's five review rounds, executable production code changed exactly once |
| 13 | Gateway / BFF | ✅ **the sixth and last service.** REST per the copied `openapi.yaml` (14 paths), hand-rolled JWT, login rate limiting, NATS RPC clients, MongoDB-only reads, SSE with heartbeat and `Last-Event-ID` replay — **and not one new NuGet package**, where the predecessor needed three. Two Orders responders were built first so the Gateway had real responders to talk to, which is where its predecessor's seam defect lived; six backlog entries then closed the guards those features exposed |
| 14 | Health checks, OTel propagation, retry + DLQ — **and the guard-hardening audit** | ✅ **complete, 13 of 13.** The audit filed findings faster than it closed them, so the phase was first closed *by disposition*; the maintainer **overruled that** and eleven entries were re-opened and genuinely finished — all eleven approved, none rejected. Two were then accepted with evidence and a re-open trigger, neither being work developed incorrectly. What the finish found: a guard that passed under the very mutation it existed to catch; a flake whose real cause was a lazily-connecting client, not the timeout its entry blamed; a defect class that did not end at the sites its entry named; and **107** doc-comment defects that became visible only once the check could actually fire |
| 15 | End-to-end saga verification | ✅ **complete, 4 of 4.** Ported #7's proven fleet architecture (real Testcontainers infrastructure, no mocks) into a single shared-fleet suite covering all five criteria — happy path, `.99` compensation, redelivery, poisoned-message DLQ, and one trace id spanning the whole saga. The trace criterion found a real production gap: the id-80 fast-path dispatcher carried no trace context at all, so three services saw three different trace ids for one order — fixed in the same pass. Also closed: a cold-connection flake ported from id 81, a dead arm from phase 14 that OR1's later retry logic had silently defeated (fixed in Orders **and** its Projector sibling), and the last RPC-payload duplication unified into `src/Contracts/Rpc` |
| 16 | Next.js web app | ✅ **complete, 9 of 9.** Next.js App Router web app with a BFF: the session token stays in an httpOnly sealed cookie, and route handlers proxy to the Gateway, including the live SSE timeline with `Last-Event-ID` resume. A behavioural error sweep fails every request each page makes and asserts that the Gateway's own problem text reaches the screen; it replaced a syntax guard that review defeated twice, and it found five real silent failures. Also fixed: the Gateway ignored `GATEWAY_PORT`, and an expired session on a finished order showed "connection lost". Spec amendment **SA-5** (money is formatted from the currency's ISO 4217 exponent) was applied to both repositories, with #7's money code aligned to it; both web apps now show which stack they are (`#8 · .NET / Next.js`, `#7 · NestJS / Nuxt`). Four money and display defects were then fixed in both repositories: the timeline (id 100) and problem `detail` text (id 102) showed raw minor units, the stock page repeated the product code (id 101), and the web apps took the currency exponent from `Intl`'s CLDR digits rather than ISO 4217 (id 103). Each repository now has one ISO 4217 table, and #8's web app is checked against it |
| 17 | Web component tests | ✅ **complete, 1 of 1.** Delivered inside phase 16: the Vitest + React Testing Library suite (id 30) was built alongside the web app it tests, 22 files / 286 tests |
| 18 | API tests through the Gateway | ✅ **complete, 3 of 3.** `BlackBoxApiTests` ports #7's black-box script against a real Kestrel Gateway and the real service fleet, with the developer infrastructure down. It covers the happy path, with the completion triple checked structurally for causal order (R24); the compensation path; a duplicate `paymentReference` yielding one payment; and the R49 rejections, each checked against Billing's own database. Approved on the first review round, in one implementer session (#7 needed eight). Also: dead-letter producers now fall back to the service's own Kafka setting (id 104), and the causal-order check fails when it has checked no edges (id 105) |
| 19 | Playwright end-to-end tests | ✅ **complete, 1 of 1.** `apps/web/e2e/` ports #7's happy-path and `.99` compensation scenarios against a real Kestrel + Next.js stack (`scripts/dev-stack.sh`, since #8 has no full Docker Compose until phase 23). The compensation spec renders the timeline's causal link (`stock.released.v1` → `order.cancelled.v1`) in a real browser, on top of id 31/105's structural guard. Approved on Opus's first review round, with a ported-idiom ledger correction. Also found: a frontend accessibility defect (a per-line select's `id` can drift from its label's `htmlFor` under repeated server-side renders), filed as id 106 and not fixed in this phase |
| 20 | n8n demo workflows, reused unchanged | ✅ **complete, 1 of 1.** The four workflow JSONs were already copied byte-identically from #7 during the harness phase; this phase verified them live rather than porting anything. Auto-import proven idempotent from a cold `docker compose --profile n8n up`; the burst workflow fired through a real webhook, reached the Gateway on the host (via a `host.docker.internal`/`extra_hosts` bridge, since `scripts/dev-stack.sh` runs the .NET services outside Docker until phase 23's compose exists) and placed a real order, confirmed end to end and state restored afterward; removing the `n8n` profile left 12 other services healthy and an order still reaching `completed` with n8n never started. `n8n:import`/`n8n:export` root shortcuts ported from #7 |
| 21 | Quality gates (analyzers, format, coverage) | ✅ **complete, 2 of 2.** `quality.sh`'s coverage gate now fails the build below 80% domain / 60% overall, merging every coverlet report by line-hit union so a sibling test project's coverage isn't undercounted; proven to fail with two artifact-corruption arms and a real coverlet exclude-filter run. SonarQube's optional profile now runs a real scan to completion (`sonar-scan.properties`, renamed from the ported `sonar-project.properties` — that exact filename makes `dotnet-sonarscanner end` fail). A pre-existing backlog entry from feature 45's review (`EfCoreOrderNumberAllocator` scanning the whole `orders` table on every allocation, not just the first) was also closed here: a cheap fast-path check now skips that scan once the sequence row exists, with feature 45's own concurrency guarantee left untouched and re-armed to prove it |
| 22 | Prometheus, Grafana, Jaeger verification | ⬜ |
| 23 | Full Docker Compose | ⬜ |
| 24 | Documentation, demo recording, **#7 vs #8 benchmark** | ⬜ |
| 25 | Final checkpoint | ⬜ |

## Running it

The root `package.json` holds command shortcuts only, modelled on #7's scripts. The backend is .NET; `apps/web` keeps its own `package.json` and lockfile. Run `pnpm run` to list every shortcut. Prerequisites: Docker, the .NET SDK pinned in `global.json`, and Node from `.nvmrc` with pnpm via corepack.

```bash
cp .env.example .env            # optional: .env overrides .env.example
pnpm dc:up:infra                # MS-SQL, MongoDB, Kafka, NATS, Mailpit, observability, n8n
pnpm web:install                # once, for apps/web
pnpm stack:start                # build, seed + migrate, six services and the web app, in the background
# open http://localhost:3010 and sign in as operator / $GATEWAY_OPERATOR_PASSWORD
pnpm stack:status
pnpm stack:stop
```

The web app listens on **3010** (`WEB_PORT`), not Next.js's usual 3000, which is often taken by another app. #7 uses the same port, and the two stacks never run together because they share every other host port. A value set in your shell wins over both env files, e.g. `WEB_PORT=3020 pnpm stack:start`.

To work on one service in the foreground instead, run each in its own terminal: `pnpm seed`, then `pnpm dev:orders`, `dev:fulfillment`, `dev:billing`, `dev:notifications`, `dev:projector`, `dev:gateway` and `dev:web`. Each loads `.env.example`, then `.env`, then your shell (`scripts/dev-stack.sh env`).

| Group | Shortcuts |
|---|---|
| Harness and quality | `init`, `quality`, `quality:web`, `build`, `format`, `format:fix`, `test`, `test:unit`, `test:integration` (needs Docker) |
| Contracts | `contracts:generate`, `contracts:check` (the web app's OpenAPI types against `specs/shared/openapi.yaml`) |
| Database | `db:migrate:orders`, `db:migrate:fulfillment`, `db:migrate:billing`, `db:migrate:notifications`, `seed` (the seed also applies every migration) |
| Web app | `web:install`, `web:build`, `web:start`, `web:lint`, `web:typecheck`, `web:test`, `web:test:coverage`, `web:test:integration` |
| Infrastructure | `dc:up:infra`, `dc:up:infra:no-n8n`, `dc:down:infra`, `dc:ps:infra`, `dc:clean:infra`, `kafka:topics`, `dc:logs:infra`, `dc:logs:<service>`, `n8n:import`, `n8n:export`, `dc:up:sonar`, `dc:down:sonar` |
| Demo | `saga:watch` (every order's status and the saga command table, read from MS-SQL) |

#7 shortcuts with no #8 counterpart yet:
- `dc:*:apps` and `dc:seed` wait for the full Docker Compose (phase 23);
- `sonar:scan` waits for phase 21;
- `order:place` and `invoice:pay` are Node scripts built on NestJS's NATS client, so they do not carry over as they are.

## Licence

MIT.
