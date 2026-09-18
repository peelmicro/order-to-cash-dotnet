# Order To Cash — .NET

> 🚧 **Under construction.** This repository is being built phase by phase; the table at the bottom tracks exactly how far it has got. Everything described as done is done and tested — nothing here is aspirational.

An **order-to-cash lifecycle backbone** for a B2B EDI / e-invoicing platform, built as event-driven microservices. It models the classic EDI exchange as a distributed workflow:

**Order (ORDERS) → Stock reservation → Credit check → Order confirmation (ORDRSP) → Despatch advice (DESADV) → Invoice (INVOIC) → Payment (remittance)**

— with an orchestrated **saga** coordinating the flow across services and **compensating** when a step fails. Deliberately B2B in shape: the retailer never pays at order time; a credit check gates despatch, and payment arrives at the end of the cycle, within payment terms.

## Demo

![Placing a `.99` order and watching the saga compensate live](docs/screenshots/demo-compensation.gif)

*A total ending in `.99` makes the credit check reject the hold. The saga releases the stock it had already reserved, then cancels the order — both steps visible in the timeline as they happen, including the `caused by stock.released.v1` causal link on the cancellation entry.*

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

**Back-port status: all five landed in #7**, confirmed by commit, not assumed from the table above — `6dafee0` (SA-5), `65f1c5d`/`63f130e` (SA-4), `5723874` (SA-3), `bf45af0` (SA-2), `015b97a` (SA-1). `specs/shared/openapi.yaml` is byte-identical between the two repositories as of this phase (`cmp`-checked). None is outstanding.

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

## Architecture

Six .NET services talking over **two brokers with different jobs**. Four own an MS-SQL database each (`otc_orders`, `otc_fulfillment`, `otc_billing`, `otc_notifications`); the Projector owns the MongoDB read model; the Gateway owns no store of its own. Clean Architecture inside every service — `Presentation → Application → Domain`, with `Infrastructure` implementing the ports `Application` declares, and `Domain/` holding zero framework references (enforced by NetArchTest, not by convention).

```text
        Web (Next.js)                     n8n  ("the external world")
             |                                     |
             |  REST + SSE                         |  REST
             v                                     v
      +--------------------------------------------------+
      |              Gateway / BFF  (REST, JWT)           |
      +--------------------------------------------------+
         |   |   |                                  |
         |   |   |  NATS RPC (request-reply)        |  direct read-only query
         |   |   |                                  v
         |   |   |                          [ MongoDB read model ]
         |   |   |                                  ^
         |   |   |                                  | writes
         |   |   +---------------------------+      |
         v   v                               v      |
   +-----------+     NATS RPC        +-------------+|   +---------------+
   |  Orders   |-------------------->| Fulfillment ||   | Notifications |
   | (saga     |  stock.reserve      +-------------+|   +---------------+
   |  orchestr)|  despatch.create           |       |          |
   +-----------+                            |       |          | SMTP
         |            NATS RPC        +-----------+ |          v
         |--------------------------->|  Billing  | |      [ Mailpit ]
         |            credit.hold     +-----------+ |
         |            invoice.issue         |       |
         |                                  |    +-----------+
         |   facts                facts     |    | Projector |
         v          v                       v    +-----------+
   ==================================================^==========
             Kafka  -  3 fact topics + 3 DLQ topics  |
   ===================================================
             ^                                   consume
             +---- consumed by Orders, Projector, Notifications

   Write models:  otc_orders   otc_fulfillment   otc_billing   otc_notifications
                  (MS-SQL, one database per service)
```

**The same one relationship #7 documents breaks the database-per-service boundary here too, deliberately.** The Gateway does not call the Projector over RPC — the Projector answers no NATS subject at all. The Gateway reads the read model's MongoDB collection **directly, read-only** (`src/Gateway/Infrastructure/Persistence/MongoOrderReadModel.cs`), because the Projector is the only writer and a query hop that adds nothing but latency is hard to justify. It is the one place two services share a datastore; the "database per service" boundary holds for the four write models and not for the read model.

Every service writes facts through a **transactional outbox** — the fact row and the state change commit in one EF Core transaction, and a relay publishes them afterwards. No service ever writes to Kafka and its database in the same breath.

### Kafka carries facts, NATS carries RPC

The single most-used rule in this codebase, reused verbatim from `specs/shared/` because it is stack-agnostic. Every inter-service *messaging* interaction must be justifiable by one row of this table — the Gateway's direct read of the read model, above, is the one deliberate exception, and it is not messaging:

| | **NATS (core, request-reply)** | **Kafka (fact topics)** |
|---|---|---|
| **Carries** | A *request* — "please do this" | A *fact* — "this happened" |
| **Tense** | Imperative: `stock.reserve`, `credit.hold`, `invoice.issue` | Past: `order.placed.v1`, `credit.rejected.v1` |
| **Caller wants** | An answer, now, or a timeout | Nothing — it has already committed |
| **If nobody listens** | A legitimate error the caller handles | A bug; facts must always be consumable |
| **Durability** | None, deliberately — no JetStream | Durable, replayable, partitioned by `correlationId` |
| **Retried by** | The caller, against a durable `saga_commands` row | The consumer, then a `.dlq` topic after 3 attempts |
| **Who may consume** | Exactly one responder | Anyone — Orders, Projector and Notifications all consume the same fact |

**The rule that falls out of it:** a command's response *never* advances the saga. The orchestrator uses the NATS reply only to decide whether to retry. The saga moves only when the corresponding **fact** arrives over Kafka — because only the fact is durable, replayable, and seen by the Projector and Notifications too. So `credit.hold` returning "approved" over NATS changes nothing on its own; `credit.approved.v1` arriving over Kafka is what moves the order. That separation is why a Billing crash between the two loses nothing.

### The saga

Orchestrated, not choreographed — Orders owns the flow, so there is exactly one place to read it and one place to put compensation. Full step tables and sequence diagrams are in [`specs/shared/saga.md`](specs/shared/saga.md); this is the shape:

```text
  HAPPY PATH
  placed --stock.reserved--> stock_reserved --credit.approved--> credit_approved
      --> confirmed --order.despatched--> despatched --invoice.issued--> invoiced
      --payment.received--> paid --credit.released--> completed

  COMPENSATION (credit rejected)
  placed --stock.rejected--> cancelled                (nothing to undo)

  stock_reserved --credit.rejected--> [release the stock] --stock.released--> cancelled

  COMPENSATION (operator cancels a confirmed order, SA-4)
  credit_approved / confirmed --operator cancel-->
      [release the CONTESTED stock first, under Fulfillment's own lock]
      --> a despatch already in flight wins (order proceeds, cancellation overtaken)
      --> otherwise: stock released, credit released --> cancelled (operator_cancelled)
```

At `invoiced` the saga **stops and waits for the outside world** — no internal timer, no polling. A remittance arrives through the Gateway (the operator's button, an API test, or the n8n payment robot), and only then does it continue.

Compensation is ordered and separately visible: on `credit.rejected.v1` the reserved stock is **released first**, and the order is cancelled only once `stock.released.v1` confirms it. Both steps appear as their own timeline entries rather than collapsing into one "failed" — see the demo below.

**SA-4, the one place the specification itself had a race, is worth naming here rather than only in the amendment table.** Both #7 and #8 found that an operator cancellation of a `credit_approved` or `confirmed` order could race a despatch already in flight, in a way neither implementation could fix inside the original contract — releasing credit before the contested stock decision let a despatch land with its hold already returned. Ruled at a human gate and applied to both repositories identically: release the *contested* resource first, and let Fulfillment's existing lock decide which side wins.

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

## How this is being built — the AI process

The development **process is a deliverable here**, not just the software. This repository carries a spec-driven agent harness, copied from #7 before any application code:

| Artifact | Role |
|---|---|
| `AGENTS.md` | Entry map — what to read, when, and the hard rules |
| `CLAUDE.md` | Leader role, project conventions, and — since phase 16 — the cost-discipline rules below |
| `docs/lessons.md` | The incident record behind every `CLAUDE.md` rule, split out so `CLAUDE.md` itself stays short (81 KB → 14 KB) |
| `feature_list.json` | Backlog state machine — 108 entries as of this phase, max one `in_progress` |
| `init.sh` | State-coherence check, run at the start of every session |
| `progress/` | External memory: session state, and per-feature **effort records** |
| `.claude/agents/` | leader, spec_author, implementer, reviewer, test_maintainer, suite_runner |

Large features go through the full loop with a **human approval gate**:

```
pending → [spec_author] → spec_ready → ⏸ HUMAN → in_progress
        → [implementer] → in_review → [reviewer] → done
```

Small features skip the spec ceremony but still traverse the state machine. `progress/history.md` records per-feature effort — the benchmark section below is built from it, and from #7's own copy of the same file.

**Two gates are load-bearing, and they are where the quality actually comes from**, exactly as #7's own README says: the human approval gate between specification and implementation, and the fact that the assistant never commits — every phase stops, reports what was built and how to test it by hand, and a human tests it before anything enters the history. The reviewer is adversarial by design and read-only. Several features here were approved only on a second or third pass, and several full-process features (ids 47, 31, 32) were approved on the **first** pass this session, which is itself part of the story below.

### The cost-discipline correction, mid-project

Phase 16 spent **24% of a week's usage allowance in under a day**, mostly running the full implementer-plus-reviewer cycle, with premise checks and defeat-list walks, for changes that turned out to be small — a stack-name label, a port number the harness already read correctly elsewhere. `CLAUDE.md` itself grew to 81 KB over the first 16 phases as each incident earned a paragraph, and that whole file rides along in every agent's context on every turn.

The maintainer's ruling, applied from phase 18 onward: **size the process to the change, not to the project's average caution.**
- A **light** classification (config, docs, infra, formatting, test-only fixes) gets one implementer; the leader reads the diff and re-runs the affected checks directly — no separate reviewer round.
- A **full** classification (saga, money domain, wire contract, persistence, security) keeps the implementer-plus-Opus-reviewer loop with arming and the defeat list.
- After a second rejection, the leader stops and asks rather than spending a third round unprompted.
- `CLAUDE.md` was cut to 14 KB the same day, with every incident narrative moved to `docs/lessons.md`, read only when an agent needs the *why* behind a rule.

**What changed after the correction, counted from `progress/history.md`, not estimated:** phases 18–23 closed **eight backlog entries** (ids 104, 31, 105, 32, 33, 47, 35, 36) with **zero rejection rounds** across all of them. Three earned the full implementer-plus-Opus-review loop (ids 31, 32, 47 — API tests, Playwright end-to-end, and the persistence/concurrency fix), each approved on its first round; the other five were classified light and closed by the leader reading the diff directly, no separate reviewer. Several of those light phases turned out to need almost no code at all once checked — phase 20 (n8n) and phase 22 (observability) both discovered their infrastructure already existed and needed live verification, not construction, which the light classification correctly priced at one implementer and no review.

**The correction was not perfectly applied even the same day it was adopted.** Two process misses happened within hours of the ruling, both caught by the leader before being reported as done rather than after: phase 21 was first scoped from a single backlog entry, and a second, pre-existing entry assigned to the same phase number was found only while closing it; the same day, updating this README's own planning document surfaced a *third* phase-21 item that had never reached the backlog at all — a deferred `.editorconfig` rule mentioned only in prose. Neither miss was expensive once found. Both are recorded as findings in their own right, because a process correction that is not itself checked is exactly the "guard-that-does-not-guard" failure `CLAUDE.md` names repeatedly.

**What the discipline actually rests on**, unchanged by the cost correction and the reason arming and adversarial review exist at all: a claim is not evidence until someone checks it. A guard is only real if you *arm its deletion* and watch a named test fail. A number is only true if you *re-derive* it, not compare it to the last version of itself — this repository's own commit-message hook exists because a completeness claim was written wrong in a subject line twice. A citation is only useful if you *open it* — several of this session's own review rounds found a ledger row or a README line that had drifted from the file it cited.

The full failure ledger — roughly forty entries, what caught each one and what it cost — is in `docs/PROCESS.md` and `docs/lessons.md`. They are worth reading before adopting a process like this one, because the interesting entries are not the bugs; they are the checks that looked like they were working and were not.

## Benchmark: #8 against #7, and what did not speed up

Every figure below is read from a `progress/history.md` — this repository's own, or #7's, both committed and both cited by phase or feature id so the number can be re-derived rather than trusted. **The honest shape of this data is two eras, not one number.** Early- and mid-project phases (6–14) were consistently *slower* here than in #7, by ratios that cluster around 1.2×–3.3×. Late phases (16 onward, once the web app, the API-test harness and the e2e suite existed to reuse) were consistently *faster*, several with no #7 counterpart to compare against at all because #8's own review process found and fixed defects #7 still carries.

### Where reuse cost more, not less

| Phase / feature | #8 | #7 | Ratio | Why, in one line |
|---|---|---|---|---|
| Phase 6 (EF Core models) | ≈65 min impl. | ≈20 min impl. | ≈3.3× | The largest gap in the build: EF Core migration authoring against MS-SQL had no shortcut #7's Drizzle-against-MySQL experience transferred |
| Phase 8 (Orders + saga) | ≈1 h 27 min impl., ≈19 min spec | ≈1 h 07 min, ≈3 min spec | ≈1.3× impl., ≈6.3× spec | The hand-rolled CQRS dispatcher and the saga orchestrator both needed .NET-specific design work the spec's prose does not carry |
| Phase 9 (Fulfillment) | ≈56 min impl., ≈32 min review | ≈48 min, ≈13 min review | ≈1.17× impl., ≈2.5× review | Closest to parity of the early phases — the pattern was already established by phase 8 |
| Phase 10 (Billing) | ≈2 h 20 impl. (derived), ≈50–60 min review | 1 h 39 impl., 29 min review | ≈1.4× impl., ≈1.9× review | The `.99` simulator and the invoice aggregate, both genuinely new domain logic, not a port |
| Phase 12 (Projector) | ≈5 h 04 min total | ≈3 h 41 min (comparable slice) | ≈1.4× | **The one entry where the ratio understates the win.** #8 folded #7's causal-edge amendment (`bf59af9`, 32 files, a human gate, rejected twice) into its *first draft*, because backlog id 57 had already closed the underlying Billing edge the day before. #7's own ≈2 h 55 min of eight-phases-late rework, plus its gate, never had to happen here — work that appears in no #8 session count because it was avoided, not performed faster |
| Phase 14 (guard-hardening audit) | 6 implementer sessions, 4 reviews, 3 rejections, ≈4 h 10 min impl. + ≈2 h 30 min review | 14 implementer passes, 8 reviews (2 rejected), spread over two days | Fewer passes, worse rejection rate | Not like-for-like: #7 built the app incrementally across many small passes; #8 built it in one pass and then spent 3 of 4 review rounds on a single bullet |

### Where reuse paid off — the era after phase 16

| Feature | #8 | #7's own history | The saving, honestly attributed |
|---|---|---|---|
| id 31 `api_tests` | 1 session, 1 review, 0 rejections, ≈2 h agent work | 8 sessions: 3 implementer passes, 3 reviews (2 rejected), a spec amendment, a human gate | #8's projector already shipped #7's post-rejection causal-depth sort, so the black-box suite found nothing new to fix. **The saving is reused findings, not a faster port of the same work** |
| id 32 `e2e_playwright` | 1 session, 1 review, 0 rejections, ≈1 h 23 min | 4 sessions, one REJECTED (a stale-DOM defect, D1), ≈1 h 52 min, closed with 7 open findings | #8's web app had already ported #7's post-rejection backstop-poll fix as part of building the page itself, so the defect that cost #7 two of its four sessions was never present to find |
| id 97 (SA-5, #7 money alignment) | 2 sessions, 2 reviews, 1 rejection, ≈32 min | no #7 counterpart | Not free: the rejection came from an unguarded input the implementer classified as display-only without checking |
| id 100 (timeline money scaling) | 2 sessions, 2 reviews, 1 rejection, ≈2 h | #7 never fixed this; it still renders unscaled | The rejection was a ported premise nobody checked — that `Intl`'s digits equal the ISO 4217 exponent |
| id 31/105 (causal-order guard) | see id 31 above; id 105 1 session, LIGHT | #7's own equivalent guard (`AssertCausalOrder`) had the same vacuous-pass gap, found by #7's own review as its N7 | #8 inherited the fix's shape but not the defect — filed and closed as id 105 the same phase it was found |
| id 47 (order-number scan cost) | 1 session, 1 review, 0 rejections, ≈1 h 14 min | #7 never fixed this; its allocator still scans unconditionally on every call | **Pure #8 cost with no #7 counterpart to be faster or slower than** — #8 now does strictly less work per order placement than #7 |
| ids 33, 35, 36 (n8n, observability, full compose) | 1 session each, 0 reviews (LIGHT), 0 rejections | not directly comparable — #7's own versions of these phases built content #8 inherited already built | Each phase discovered its own real bug while verifying already-built infrastructure (a `.99`-math-adjacent env-var check, a metric-naming mismatch, a Compose merge footgun), at the cost of one implementer session, not a construction pass |

### What was NOT faster, stated plainly

- **Nothing about writing .NET domain code against a Kafka/NATS/MS-SQL stack was faster than #7's TypeScript equivalent**, in the phases where that was the actual work (6, 8, 10). The specification and harness reuse did not translate into faster *typing* — the ratios above are 1.2×–3.3× slower, consistently, for as long as new domain logic was being written for the first time in this stack.
- **The spec-authoring step was consistently the widest gap** (phase 8: ≈6.3×), because a EARS requirement written against #7's own vocabulary still needed re-deriving in .NET terms before it was actionable.
- **The audit-style phase (14) took more total sessions here**, not fewer, once the maintainer overruled an attempt to close it by disposition rather than by actually fixing what was found.
- **What DID transfer, and transferred completely, was #7's own defect discovery** — not the code, the *findings*. Every late-phase win above is a defect #7's review process found and fixed, ported into #8 before #8's own equivalent review could rediscover it at the same cost. The dividend is real, but it is a dividend on #7's review effort, not on #7's implementation effort — and it only started paying out once enough of the trilogy's shared surface (the web app, the e2e harness, the API-test fleet) existed for a defect found once to be avoided twice.

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
| 22 | Prometheus, Grafana, Jaeger verification | ✅ **complete, 1 of 1.** All five panels of the reused Grafana dashboard (saga duration, per-service latency, consumer lag, outbox lag, DLQ depth) verified against real orders placed through the real stack; 4 of 5 were empty until a real bug was found and fixed — the dashboard queried Prometheus metric names with a `_milliseconds`/`_ratio` suffix .NET's OTel SDK never produces, unlike #7's JS SDK. One distributed trace captured spanning the whole saga: 42 spans, 6 services, depth 26, with continuity confirmed at the database level, not just Jaeger's UI grouping (#7: 22 spans, depth 11 — #8 spans three extra layers per hop) |
| 23 | Full Docker Compose | ✅ **complete, 2 of 2.** `docker-compose.apps.yml`, layered on `docker-compose.infra.yml` (never duplicating it), brings all 18 containers to healthy in 91.8s with images already built (a true from-scratch build+start is ~167s, disclosed as its own number rather than folded in). One shared `infra/docker/service/Dockerfile`, parameterised by a build arg, builds all six .NET services; a web Dockerfile builds `apps/web` as its own standalone pnpm project. Closed a real gap with zero `src/` changes: all four MS-SQL-backed services now get their own migrate-then-run container pair, where before only three were migrated automatically. Found and fixed a genuine Docker Compose footgun (`external: true` redeclared across a multi-file merge fails a first-ever cold start outright) while proving the timing claim. A real order placed through the composed Gateway reached `completed` and survived a full container restart |
| 24 | Documentation, demo recording, **#7 vs #8 benchmark** | ✅ **complete, 1 of 1.** README rewritten with an Architecture section (service diagram, the Kafka-carries-facts/NATS-carries-RPC decision matrix, three saga state diagrams including the SA-4 compensation path), a Trade-offs table (#7's ten rows plus six .NET-specific ones), an Assumptions/what-I'd-do-differently section citing real incidents from this repository, and a Production-extensions section with file/line-cited evidence (the outbox relay's row-lock measured at 117ms against a 3s bound). The AI-process section documents the phase-16 cost-discipline correction by name. The demo GIF (`docs/screenshots/demo-compensation.gif`, ported `scripts/capture-demo.mjs`, adapted for #8's direct `accepted-order-link` navigation) was captured live against a freshly-seeded stack on the first attempt and frame-confirmed to show the `.99` compensation saga end to end, including the `caused by stock.released.v1` causal link. The benchmark section reads both repositories' `progress/history.md` honestly: a "two eras" framing showing early phases (6, 8, 9, 10, 12, 14) genuinely slower than #7 and late phases genuinely faster, attributed to reused *findings* from #7's review process rather than faster raw implementation. Spec amendments' back-port status to #7 was confirmed live by commit (`git log`, `cmp` on `openapi.yaml`), not assumed: all five landed |
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

**All-Docker alternative** (phase 23): `pnpm dc:up:apps` builds and brings up all 18 containers, no host `dotnet`/`node` process needed — cold start reaches every container healthy in under two minutes with images already built. Seeding still runs from the CLI (`pnpm seed`) against the composed stack's host-published ports. `pnpm dc:down:apps` tears it down.

To work on one service in the foreground instead, run each in its own terminal: `pnpm seed`, then `pnpm dev:orders`, `dev:fulfillment`, `dev:billing`, `dev:notifications`, `dev:projector`, `dev:gateway` and `dev:web`. Each loads `.env.example`, then `.env`, then your shell (`scripts/dev-stack.sh env`).

| Group | Shortcuts |
|---|---|
| Harness and quality | `init`, `quality`, `quality:web`, `build`, `format`, `format:fix`, `test`, `test:unit`, `test:integration` (needs Docker) |
| Contracts | `contracts:generate`, `contracts:check` (the web app's OpenAPI types against `specs/shared/openapi.yaml`) |
| Database | `db:migrate:orders`, `db:migrate:fulfillment`, `db:migrate:billing`, `db:migrate:notifications`, `seed` (the seed also applies every migration) |
| Web app | `web:install`, `web:build`, `web:start`, `web:lint`, `web:typecheck`, `web:test`, `web:test:coverage`, `web:test:integration` |
| Infrastructure | `dc:up:infra`, `dc:up:infra:no-n8n`, `dc:down:infra`, `dc:ps:infra`, `dc:clean:infra`, `kafka:topics`, `dc:logs:infra`, `dc:logs:<service>`, `n8n:import`, `n8n:export`, `dc:up:sonar`, `dc:down:sonar`, `sonar:scan` |
| Full stack (Docker only, phase 23) | `dc:up:apps`, `dc:down:apps`, `dc:ps:apps`, `dc:logs:apps`, `dc:build:apps` — brings up all 18 containers (infra + six services + web); seeding still runs from the CLI (`pnpm seed`), by design — #8 has no seed container |
| Demo | `saga:watch` (every order's status and the saga command table, read from MS-SQL) |

#7 shortcuts with no #8 counterpart:
- `dc:seed` — #8 runs the seed job from the CLI (`pnpm seed`) rather than as its own container, a deliberate choice recorded in `progress/impl_full_docker_compose.md`;
- `order:place` and `invoice:pay` are Node scripts built on NestJS's NATS client, so they do not carry over as they are.

## Trade-offs

Every row is a decision that could defensibly have gone the other way. The alternative is named, not waved at. The first ten are stack-agnostic — reused from #7's own table, because the decision was made once, in `specs/shared/`, for the whole trilogy — with the .NET-specific cost named where it differs from #7's.

| Decision | Why | What it costs |
|---|---|---|
| **Orchestrated saga**, not choreography | One place to read the flow, one place to put compensation, one place to debug. The `saga_commands` table makes in-flight state inspectable in SQL | Orders knows the whole flow — a coupling choreography avoids, at the price of an emergent process nobody can read end to end |
| **Two brokers** (Kafka facts + NATS RPC) | Each is used for what it is good at, and the distinction is the thing worth teaching | Two client libraries (`Confluent.Kafka`, `NATS.Net`), two OpenTelemetry propagation paths |
| **NATS core, no JetStream** | Nothing in the RPC path needs durability or replay — a timeout is a legitimate answer | Would blur the matrix above by duplicating Kafka's job |
| **Polling outbox**, not CDC/Debezium | No dual write, no extra infrastructure, and identical on all three stacks of the trilogy | ~500 ms publish latency and steady DB load. Debezium is the production answer |
| **Topic per service**, not per event | 3 topics + 3 DLQs instead of far more; new facts need no broker administration; per-order ordering preserved by partition key | Consumers receive facts they filter out |
| **Database per service** on one MS-SQL instance | Real logical isolation — no cross-database joins, no shared FKs, each service independently extractable | One instance is a single point of failure; production separates them |
| **MongoDB read model**, not a relational replica | A denormalised document is the natural shape for "what happened to order X", and it proves the repository port abstracts the engine | Eventual consistency the UI must surface honestly — `GET /orders/{id}` answers `202` with a *projection pending* body rather than `404` while the write has committed but the read model has not caught up (`R55`), plus a second database technology to operate |
| **Credit simulator + `.99` rule**, not a real PSP | Compensation must be demoable deterministically in five seconds | Demonstrates saga design rather than payment integration. Labelled an affordance in the spec, not a credit policy |
| **n8n as the external world** | Payments and replenishment arrive from outside, as in reality — no hidden in-service timers faking demand. It speaks only the public REST API, so the same JSON serves #8 and #9 | One more container, demo-only |
| **Gateway reads the read model directly**, rather than through an RPC hop | The Projector is the only writer; a query subject in front of a read-optimised document store would add a hop, a serialisation and a failure mode for no gain | It is the one place two services share a datastore, so "database per service" holds for the four write models and not for the read model |
| **NATS vs gRPC for RPC** | NATS core is a single client, no code generation, no `.proto` build step, and the same client library already brings the fact-publishing story; a saga command is a small JSON payload, not a high-throughput streaming call gRPC would earn its keep on | No compile-time contract check on the NATS side — the RPC payload schemas live in `asyncapi.yaml` and are honoured by convention, checked by golden-envelope tests, not by a generated stub |
| **Hand-rolled CQRS dispatcher**, not MediatR | MediatR v13 is commercially licensed; the trilogy's benchmark needs the same mechanism on all six services, matching #7's `@nestjs/cqrs` shape, and .NET has no free equivalent | `src/Cqrs`, one more project to maintain, with its own startup-validation pass so a missing handler fails at boot rather than at first use |
| **EF Core over Dapper/raw SQL** | Migrations, a `DbContext` per service matching the database-per-service boundary, and LINQ where the query is simple enough not to need a hand-written statement | The atomic, race-safe statements this system's own concurrency guarantees (order-number allocation, the saga seed) are hand-written `ExecuteSqlInterpolatedAsync`, not EF's own API — EF Core has no first-class "insert if not exists under a lock hint" primitive |
| **NetArchTest over an ESLint rule** | Enforces the same `Domain/` purity rule #7 enforces at lint time, but as a real test that fails the build in the normal `dotnet test` pass, not a separate lint step | One more test project (`Architecture.Tests`), and the rule is checked at namespace granularity, not file granularity |
| **coverlet.collector, then a hand-rolled merge-by-line-union gate**, not coverlet.msbuild | `coverlet.collector`'s `--collect:"XPlat Code Coverage"` is the VSTest-native path every project already used; adding `coverlet.msbuild` alongside it for its `/p:Threshold=` property would be a second coverage mechanism to keep in sync | The threshold check (`quality.sh` §5) is project-specific Python merging every `coverage.cobertura.xml` by line-hit union, not a one-line MSBuild property — more code to maintain, but it is what makes a sibling test project's coverage count correctly rather than being averaged away |
| **A shared, `SERVICE`-parameterised Dockerfile**, not one per service | All six services' `.csproj` files share the identical shape (SDK build, ASP.NET Core runtime, `FrameworkReference Microsoft.AspNetCore.App`) — six near-duplicate Dockerfiles would be six places to forget the same fix | One Dockerfile carries more build-arg plumbing (`SERVICE`, `PORT_ENV_VAR`, `DEFAULT_PORT`) than a single-purpose file would need |
| **SonarQube behind an opt-in profile** | It costs ~1.5–2.3 GB of RAM, and the coverage gates run in `./quality.sh` regardless | Quality never depends on it running |
| **pnpm monorepo for `apps/web` only** | The backend has no Node dependency at all — `apps/web` keeps its own `package.json` and lockfile rather than joining a workspace with nothing else to share | Unlike #7's single pnpm workspace spanning every service, #8's root `package.json` is command shortcuts only, with no packages to hoist |

## Assumptions, and what I would do differently

**Assumptions made explicit**, because each one would be wrong in some real deployment — reused from #7's list, because they are properties of the shared domain model, not of either stack:

- **One currency per order.** `Money` is `long` minor units and never crosses currencies; a multi-currency order would need a rate at capture time and a policy for which rate.
- **Business references are globally sequential** (`ORD-000001`). Real EDI often needs per-retailer or per-year sequences, and the counter row is a write bottleneck at volume — id 47 (phase 21) cut its per-allocation cost, but did not remove the single-row serialisation itself.
- **A credit hold is a simple ledger sum**, not a scoring model, and the `.99` rule stands in for a bureau call.
- **Stock is a single logical warehouse.** No locations, no allocation strategy, no partial despatch.
- **The operator is a single trusted role.** One JWT, no per-retailer authorisation — a real system scopes every query by the caller's own trading relationships.
- **Facts are never schema-migrated.** Every event is `v1`; a real system needs an upcasting story before the first `v2`.

**What I would do differently**, drawing on both #7's own list and the incidents this repository's own process record actually caught:

- **Make anything the system records on failure visible by default.** Dead letters are recorded here, completely and with their reasons, and are not surfaced anywhere an operator would look. A queue nobody watches is a queue nobody knows is filling.
- **Write the black-box assertion before the feature, not after.** id 31's causal-order guard (`AssertCausalOrder`) started out able to pass vacuously if it checked zero edges — found by its own review, then closed properly as id 105. A general invariant that counts what it actually checked is worth writing once, not discovering it was silent after the fact.
- **Check a ported claim against the other repository's checkout, every time, not against what the framework "probably" does.** This repository's own history has several instances of a false premise entering through a brief or an implementer's assumption and costing a full review round to catch — id 97's `step` attribute, id 100's `Intl`-is-ISO-4217 assumption, id 103's parity guard parsing source text instead of the compiled table. The ledger rule (`CLAUDE.md`, "the ported-idiom ledger") exists because of exactly this pattern, and it still recurred after being written down.
- **Size the review process to the size of the change, from the start, not after a usage scare.** Phase 16 spent 24% of a week's usage allowance in under a day, mostly on full implementer-plus-reviewer cycles for changes that turned out to be small (a label, a port number). The "Cost discipline" rules adopted afterwards — a light path for config/infra work, a full path reserved for saga/money/persistence/security — should have been the starting shape, not a correction.
- **Check a phase's whole population before declaring it closed, every time, not just the entry a prior brief named.** Phase 21 was first scoped as one backlog entry; a second, pre-existing entry assigned to the same phase number was found only while closing it. The same day, closing phase 23 found a third phase-21 item that had never even reached the backlog, sitting only in an external planning document. Neither miss was expensive to fix once found — both were expensive because they were not looked for until the phase was almost declared done.
- **Name the transport on every message pattern**, #7's own lesson, and it holds here too: a bare pattern that would register on every connected transport is exactly the ambiguity a `BackgroundService`-per-transport avoids only by construction, not by a check that would catch a regression.

## Production extensions

This is an assessment, and it runs as one instance per service. That is a deliberate scope, not an oversight — but "we did not build it" is a weak claim, so this section separates **what the system already supports and can prove** from **what a production deployment would add**.

### What it already supports

- **The write path is safe to run at N instances.** Every outbox relay claims its batch under a row lock (`WITH (UPDLOCK, READPAST, ROWLOCK)`), so two relays polling the same table take disjoint batches — measured, not assumed: a second relay's whole run returned in 117 ms against a 3 s bound while the first held a row's claim open (phase-1 close, `progress/history.md`).
- **Business references stay unique under concurrency.** `ORD-`/`DES-`/`INV-`/`CR-` numbers come from a counter row allocated under a row lock (`EfCoreOrderNumberAllocator`); the second caller blocks rather than racing, proven by feature 45's own two-session guard, re-armed and still passing after id 47's fast-path optimisation.
- **Fact consumption scales with partitions, not with instances.** Every consumer joins a named Kafka consumer group, so adding an instance redistributes partitions instead of duplicating delivery. Per-order ordering is preserved because the partition key is the envelope's `correlationId`, fixed equal to the order id by `specs/shared/saga.md`.
- **Database per service** for the four write models. No cross-database joins and no foreign keys across service boundaries, so each is independently extractable onto its own instance. The one documented exception is the read model — see Architecture above.
- **The DLQ carries a full diagnostic header set**, including `traceparent`, so a dead letter can be traced back to the order that produced it instead of being an orphan.

### What production would add

- **A load balancer / horizontal replicas.** Not application code, which is why it is absent here; the properties above are the evidence the services would tolerate it. The MS-SQL connection pool is the one thing to size deliberately before replicating.
- **TLS termination.** Everything is plain HTTP locally. Production terminates TLS at the edge.
- **A secret store.** Configuration is a `.env` file with dev-only default credentials. Production reads from a managed secret store instead.
- **CDC instead of a polling outbox.** Documented as a trade-off above rather than implemented: Debezium removes the ~500 ms poll latency and the DB load, at the cost of another piece of infrastructure. The port boundary is already in the right place for the swap.
- **Alerting on the DLQ.** Both this system's DLQ and the corresponding gap in #7 are complete and queryable, and neither is surfaced anywhere an operator would look without being told to check. Replay tooling and an alert on the queue growing is the highest-value thing this system does not have.

### What is deliberately *not* here

- **A cache.** The MongoDB read model already is one — a denormalised projection maintained so queries never touch the write model. Adding a cache in front of it would introduce a third consistency story to reason about.
- **JetStream.** Nothing in the RPC path needs durability or replay; a timeout is a legitimate answer. Adding it would duplicate Kafka's job and blur the distinction this project exists to demonstrate.

## Licence

MIT.
