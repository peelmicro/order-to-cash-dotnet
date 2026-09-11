# Spec pass — `observability_reliability` (id 27, phase 14)

## Status

**Where we are.** The triple-doc is written: `specs/observability_reliability/{requirements,design,tasks}.md`. `feature_list.json` id 27 is set to **`spec_ready`**. No code, no test and no `specs/shared/` byte was touched.

**What needs approval.** Five rows in §3 below. **Only one of them is a genuine choice** (`G2`, widening an architecture guard that a previous human gate approved); the other four are decisions taken here with a stated reason and are listed so they are seen, not so they are debated. **Six of #7's seven open points were inherited rather than re-decided** — §2 cites the #8 artefact that carries each.

**Recommendation.** Approve as written and dispatch in the five task groups `design.md` §1.3 sequences (**B → A1 → A2 → A3 → A4**). #7's counterpart needed six implementer passes; the group boundaries here are chosen so a pass may end at any of them with a green suite. Attack `design.md` §10.4's three rows first at review — each is correct on the path a deletion probe takes and wrong only under a condition the happy path never creates.

---

## 1. Scope, as inherited

Two halves in one feature, folded together by an explicit human decision in #7 on 2026-08-26. **#8 inherits the widened form; that is not a gate question** (`feature_list.json` id 27's `notes` carries the provenance).

- **Half A — reliability and observability.** Shared `R56`–`R60`, plus two ratified deferrals this feature owns that live in other shared sections: `R16` (§2) and `R29`'s dead-letter clause (§3).
- **Half B — `orders.create` `requestId` idempotent replay.** Shared `R62` (`specs/shared/requirements.md:524`), minted by #7 and inherited verbatim.

Local ids added by this pass: `OR1`–`OR7` and `RI1`–`RI5`, all in `specs/observability_reliability/requirements.md` §3–§4, each stating which shared clause it refines and in which direction.

---

## 2. #7's seven open points — inherited, not re-decided

Each row below was verified against the #8 artefact named, before this pass began writing.

| #7's open point | Status in #8 | The #8 artefact that carries it |
|---|---|---|
| **1** — mint `R62` for `requestId` replay | **Inherited.** Not re-opened | `specs/shared/requirements.md:524`, with its "Id ordering / provenance" note at lines 506-522 |
| **2** — the concurrent-`requestId` race resolves to the winner's order, never an error | **Inherited via `R62`'s own wording**, confirmed by reading it: *"SHALL ensure that exactly one order is created for that `requestId` and SHALL resolve the other request to that same order's reply, never to a second order or to an error"* | `specs/shared/requirements.md:524-537`. The **MS-SQL realisation** of it is new and is `design.md` §2.4 |
| **3** — a 14th fact versus an Orders-side channel (#7's largest) | **Inherited, Option 1 chosen.** `order.saga_failed.v1` is registered on the `ordersFacts` channel and #8 already carries the whole of it | `specs/shared/asyncapi.yaml:117-118` (channel registration), `:1525-1545` (message), `:2632-2665` (payload); and in #8's source already: `src/Contracts/Facts/Payloads/OrderSagaFailedPayload.cs`, `FactCatalog.cs:33`, `Projector/Domain/Summaries.cs:105`, `SagaStepTable.cs:246`. **#7's `A1a`/`A1b` "thirteen → fourteen" sweep is therefore not owed in #8** — `tasks.md` A2g makes that an enumerated absence rather than a claim |
| **4** — `R56` split into mechanism versus composed-stack observation | **Inherited.** The split is already written into the shared matrix's own `R56` row and defers the composed-stack half to feature 28 | `specs/shared/test-matrix.md` §8, `R56` row |
| **5** — metrics by OTLP-push to the collector, stale Prometheus block removed | **Inherited, and already executed.** The removal and its reasoning are in the file's own header | `infra/prometheus/prometheus.yml:12-21`. This feature edits **no** file under `infra/` |
| **6** — the Gateway minting a fresh `correlationId` per exception | **Does not exist in #8 — verified.** `ProblemJsonMiddleware.cs:37-39` reads the request-scoped id out of `HttpContext.Items` and mints a Guid only as a fallback, and `GatewayHost.cs:87-88` registers `CorrelationIdMiddleware` **before** it, so on a composed-pipeline request the fallback is unreachable. **See finding F1** — the real gap is the ordering, and it is now a task |
| **7** — task-group sequencing | **Genuinely #8's to choose.** Decided: one `tasks.md`, five groups, **B → A1 → A2 → A3 → A4**. See `G3` | `design.md` §1.3 |

**The reuse dividend, for the benchmark record:** six of seven of #7's largest feature's open points cost nothing here. What did not come free is the *stack realisation* — `design.md` is 13 sections and a 28-row ledger, none of which #7's could supply.

---

## 3. Gate rows

| # | Row | Recommendation | Needs a decision? |
|---|---|---|---|
| **G1** | **Five services gain an HTTP listener.** `openapi.yaml` publishes `/health/live` and `/health/ready`, `R60` says "per service", and #7 gave each of its six services an HTTP port for exactly this (`apps/orders/src/presentation/health.controller.ts`'s own banner: *"this service's HTTP port exists purely for health/metrics"*). #8's five non-Gateway services are console generic hosts. | **Do it as an `IHostedService` hosting a minimal `WebApplication`**, not by converting `*Host.CreateBuilder` to `WebApplication.CreateBuilder` — the second changes five composition-root signatures and every test that calls them, for two endpoints. Adds `FrameworkReference Microsoft.AspNetCore.App` (no NuGet package). Ports `3002`–`3006`, #7's own allocation. `design.md` §8.1 | **No** — *that* it happens is inherited from #7; *how* is a design decision with a stated reason. Listed for awareness because it adds five listeners and five env vars |
| **G2** | **Widening an architecture guard a previous human gate approved.** `FactPublisherConfinementTests` confines `Confluent.Kafka`'s producer types to `*.Infrastructure.Outbox` (amended and ratified at the `order_saga_orchestrator` gate, 2026-09-04). `Projector` and `Notifications` need a dead-letter **producer** and own no outbox. | **Widen the pattern to `\.Infrastructure\.(Outbox|Messaging\.DeadLetter)(\.|$)`** and put the copies in `Infrastructure/Messaging/DeadLetter/`. The alternative — an `Infrastructure/Outbox/` folder in a service with no outbox — is a lie the next reader has to disprove. The widening is justified by what `R14` actually protects (*"no command handler, aggregate or domain service **publishes** directly"*; a dead-letter republication is none of those), and `tasks.md` A1e **re-arms** the rule after widening. | **Yes.** It is the only row here that changes something a human gate already ratified |
| **G3** | **Sequencing** — #7's open point 7, the one it left to us. | One `tasks.md`, five groups, **B → A1 → A2 → A3 → A4**, each a valid stopping point. A1 precedes A2 because A2's park hook publishes through the port A1 defines (#7 had to forward-reference it). A3 keeps trace, logs and metrics **together** because all three read the same `Activity` plumbing — #7 split them and its `A6`/`A7` sat blocked on `A5` across two passes. A4 last because it is the only group that changes the host surface. `design.md` §1.3 | **No** — a recommendation with its reason; say so if you want one PR per group instead |
| **G4** | **`RI5` changes already-shipped Orders behaviour.** #7 seeds `order.placed.v1`'s `causationId` from `requestId` when supplied (`apps/orders/src/application/place-order.handler.ts:143`, `:205-211`); #8 mints a fresh id unconditionally (`PlaceOrderCommandHandler.cs:88`). | **Adopt #7's behaviour.** `asyncapi.yaml` defines `causationId` as *"the eventId of the fact — **or the id of the command** — that caused this one"*, and a client-supplied idempotency key **is** the command's id. Given its own local requirement (`RI5`) and its own task rather than smuggled into `RI1`, precisely because it touches approved code | **No** — inherited from #7 with a citation, not an open question. Flagged because it changes a wire value |
| **G5** | **Every service's console log format becomes single-line JSON.** `R58`/`OR7` need structured records; #7's were already JSON. | Adopt. `AddJsonConsole` + `IncludeScopes` + `ActivityTrackingOptions`, in every host. Visible in `dc:logs` and in any script that greps service output | **No** — awareness only |

**No `SA-n` amendment is proposed, and that is a checked conclusion rather than an omission.** #7 raised one promotion candidate here — that `saga.md` should state the dead-letter publication is once per parked row, not once per sweep — and it was **not** promoted. Re-checked against the shared spec itself rather than against #7's behaviour: `R29` says *"route the triggering fact to the dead-letter topic and record a saga-failure entry"* and says nothing about cadence, so the at-most-once discipline is satisfiable **within** the requirement as written, as `OR3` does. The shared spec does not prescribe the thing an amendment would change, so there is nothing to amend.

---

## 4. Findings from reading #8's disk that the brief did not anticipate

**F1 — #7's open point 6 is genuinely absent here; the unguarded property is the middleware ORDER.** Verified as the brief asked. `ProblemJsonMiddleware.cs:37-39` reuses the request-scoped id; `GatewayHost.cs:87-88` puts `CorrelationIdMiddleware` first, so the `Guid.NewGuid()` fallback cannot be reached on a real request. **But swapping those two lines compiles, passes the whole existing suite, and silently gives every error-path log line a different id from the rest of its request** — exactly `R58`'s "every line" guarantee, broken invisibly. `tasks.md` A3g adds the guard and arms it by the swap. The fallback itself is retained as a defensive default, not deleted.

**F2 — #7's poison-message incident cannot occur in #8, and porting its test literally would produce a vacuous test.** #7's incident was a syntactically valid envelope whose `correlationId` was not a UUID. `Envelope.CorrelationId` is a `Guid` in #8, so that message is rejected by the envelope guard and already logged-and-acknowledged. **The failure mode is nevertheless live and unguarded in all three consumers, in a different shape**: payload deserialisation sits *outside* the envelope `try`, so a structurally valid envelope with a payload that does not fit its catalogued type — or any downstream throw — propagates past `KafkaFactStreamSubscriber`'s `StoreOffset` and is redelivered forever on that partition. `requirements.md` §2 states the #8 shape and every `OR1` test uses it. Ledger row **L9**.

**F3 — the highest-consequence row in the feature: MS-SQL's unique index does not treat `NULL`s as distinct.** #7's schema comment says in so many words that it depends on MySQL's opposite behaviour. A literal translation makes the **second** order that omits `requestId` fail — which is nearly every order — while any test that places exactly one such order stays green. The fix is a filtered unique index, which EF Core's SQL Server provider is *expected* to emit by convention; `tasks.md` B1 requires the generated migration to be read and the `filter:` argument quoted, rather than trusting the convention. Ledger row **L1**, guard: two orders with no `requestId`.

**F4 — an `RI3` trap #7 did not have.** `EfCoreOrderRepository.SaveChangesAsync` writes the `order.placed.v1` **outbox row before** the aggregate rows (`EfCoreOrderRepository.cs:64-112`), so at collision time an outbox row for an order that will never exist is already in the transaction. #7 caught its collision *inside* the transaction and committed, harmlessly, because its outbox write came after the failing insert. Doing the same here publishes a fact for a non-existent order — **with `RI3`'s headline assertion still green**. Hence the catch sits outside the unit of work and the guard counts `outbox` rows, not orders. Ledger row **L2**.

**F5 — the ported-guard enumeration is complete and two of #7's guards are deliberately not ported.** `design.md` §11 classifies all **145** of #7's assertions for these mechanisms (a search result over the two commits that built it, `95e883a` and `8635b66`). The two not ported — its event-type-pattern widening and its thirteen→fourteen sweep — are **not applicable**, because #8 minted the fourteenth fact into `Contracts`, `FactCatalog` and the projector in earlier features. Two guards #8 adds that #7 never had: F1's ordering guard, and the write-model transaction span (`L23`) that closes a hop #7's traces never contained.

---

## 5. What was deliberately NOT done in this pass

- No file under `src/`, `tests/` or `apps/web/` touched. No test written.
- **No byte of `specs/shared/` changed.** `git status --porcelain specs/shared` is empty; `./init.sh` §5d's `cmp` against #7 passes.
- No file under `infra/` touched — `prometheus.yml` and the collector config are already in their post-decision state, inherited.
- `feature_list.json`: the **single status line only**, `pending` → `spec_ready`, edited in place. No `git checkout --` was run on it at any point.
- `progress/current.md` untouched.

## 6. Grep check (the `spec_author` standing instruction) — as a search result, not a sentence

This pass changed nothing under `specs/shared/` (`git status --porcelain specs/shared` is empty), so the hits below are pre-existing and are classified rather than introduced.

```
$ grep -incE "nest|drizzle|nuxt|mysql|typescript|dotnet|efcore|mssql|csharp" specs/shared/*
specs/shared/asyncapi.yaml:0
specs/shared/domain-model.md:0
specs/shared/n8n-workflows.md:3
specs/shared/openapi.yaml:5
specs/shared/requirements.md:2
specs/shared/saga.md:0
specs/shared/test-matrix.md:7
```

All seventeen hits, classified one line per hit:

| File:line | Matched | Classification |
|---|---|---|
| `n8n-workflows.md:30`, `:149`, `:397` | `nest` inside *honest* / *honesty* | false positive |
| `requirements.md:470`, `:477` | `nest` inside *honestly* / *honest* | false positive |
| `openapi.yaml:34`, `:436` | `nest` inside *honestly* / *honest* | false positive |
| `openapi.yaml:427`, `:499`, `:1606` | `neSt` inside `TimelineStreamEntry` | false positive |
| `test-matrix.md:22`, `:167`, `:168`, `:221` | `nest` inside *honest* / *honesty* | false positive |
| `test-matrix.md:135`, `:137` | `neSt` inside `TimelineStreamEntry` | false positive |
| `test-matrix.md:139` | `MsSql`, in the `R36` row's **Status** cell | legitimate — column 5 is each assessment's own realisation record, per that file's own rule; every other column is stack-neutral |

Every stack term introduced by this pass lives in `specs/observability_reliability/design.md` and `tasks.md`, which is where `CLAUDE.md` requires it.
