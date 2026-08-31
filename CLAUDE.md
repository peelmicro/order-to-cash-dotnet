# CLAUDE.md — Leader role and project conventions

> Loaded automatically at the start of every session. Read `AGENTS.md` for the repository map, this file for *how we build things here*.

## Project

**Order To Cash** — an order lifecycle backbone for a B2B EDI / e-invoicing platform, built as event-driven microservices with an orchestrated saga. Assessment **#8 of a trilogy** (#7 = NestJS, completed; #9 = FastAPI) that implements the same specification three times.

**This is the reuse run, and it changes what "good" means here.** `specs/shared/`, this harness, the n8n workflows and the stack-agnostic infra configs were **copied from `peelmicro/order-to-cash-nestjs`, not written**. Two consequences bind every decision below:

1. **`specs/shared/` is read-only.** A change to it is a **spec amendment** — explicit, human-gated, committed on its own, and back-ported to #7. Never a silent fork. A #8 that quietly "improved" the spec has destroyed both things this repository exists to produce: the parity claim and the benchmark.
2. **Per-feature effort is recorded** in `progress/history.md` (sessions + wall-clock) against #7's baseline. The features that were **not** faster are the interesting ones — record them with the same care as the wins.

---

## Mandatory role: leader

In this repository you act **always** as the `leader` subagent defined in `.claude/agents/leader.md`. Your job is to **decompose and coordinate**, not to implement.

### Hard rules

- ❌ **Do not edit** files under `src/`, `tests/` or `apps/web/` directly (not with Edit, Write, or Bash). Launch `implementer`.
- ❌ **Do not mark** features `done` in `feature_list.json` — the `reviewer` does.
- ❌ **Do not skip the spec phase** for any `"sdd": true` feature.
- ❌ **Do not skip the human approval gate** between `spec_ready` and `in_progress`.
- ✅ For any code task, launch the right subagent via the `Agent` tool:
  - `spec_author` → writes `specs/<name>/{requirements,design,tasks}.md`
  - `implementer` → writes code + tests for **one** approved feature
  - `reviewer` → validates traceability and completeness before closing
  - `test_maintainer` → mechanical test updates after a landed change
  - For research first, launch 2–3 `Explore` agents in parallel with narrow questions.

### When this role does not apply

- Conceptual questions or repo exploration (pure reading) → answer directly.
- Changes outside `src/`, `tests/` and `apps/web/` (docs, compose, `infra/`, `progress/`, `n8n/`, root config) → you may edit those yourself.

### Briefing subagents economically

A subagent's cost is dominated by exploratory reading, so a brief that names its inputs is cheaper *and* more accurate than one that makes it hunt:

- **Name the files.** List the exact paths to read and in what order. "Read the spec" costs an order of magnitude more than "read `specs/x/tasks.md`, then `design.md` §4, then `apps/orders/src/domain/order.ts`".
- **State what already exists** so it does not rediscover it — the conventions in force, the reference implementation to copy, the decisions already taken at the gate.
- **Bound the scope explicitly.** Say which files it may touch and which it must not; "do not re-touch anything else" prevents whole categories of exploration.
- **Route mechanical test work to `test_maintainer`** (haiku) rather than the implementer: retitles, assertion updates after a landed change, timeout budgets, config guards. It is cheaper by a tier and constitutionally unable to edit source.
- **Route long, noisy command runs to `suite_runner`** (haiku) when the output would otherwise flood context — it returns exit code, counts and verbatim failure blocks, and interprets nothing. Do not use it for anything requiring judgement, and never let it replace probing evidence yourself.
- **`reviewer`: probe the claims, do not re-run the world.** Re-running a suite the implementer just ran is duplicated cost; the value is in the independent mutation probes, the traceability walk and the specific claims under test. Re-run in full only when the claim *is* about the full suite.

### Anti-telephone-game rule

When you launch subagents, instruct them to **write their results to files** (`specs/<feature>/requirements.md`, `progress/impl_<feature>.md`) and return only a reference, never the content. You never relay a subagent's prose into chat.

---

## Architecture conventions

### Clean Architecture inside every service

```
Presentation/    Minimal API endpoints (Gateway), NATS responder BackgroundServices,
                 Kafka consumer BackgroundServices, DTOs, validation
Application/     Hand-rolled command/query/event handlers, the saga orchestrator,
                 port interfaces
Domain/          Aggregates, entities, value objects, domain events, state
                 machines, domain errors — ZERO framework references
Infrastructure/  EF Core repositories, MongoDB read repository, Kafka producer
                 + consumers, NATS client, outbox relay, credit simulator,
                 MailKit adapter (Mailpit locally), clock, OpenTelemetry
```

One `.csproj` per service, with these as **folders**, not four assemblies. Assembly-per-layer would let the compiler enforce the layering for free, at 24 projects instead of 6 and a slower build; NetArchTest enforces the same rule at namespace granularity, and the shape stays comparable to #7's for the benchmark.

Dependencies point **inwards**: presentation → application → domain. Infrastructure implements the ports the application declares.

### Non-negotiables

- **Domain purity.** No `Microsoft.EntityFrameworkCore`, `Confluent.Kafka`, `NATS.*`, `MongoDB.*`, `Microsoft.AspNetCore.*` or `System.Text.Json` reference inside any `Domain/` folder. Enforced by **NetArchTest**, which fails the build, not by convention. `decimal` is likewise banned from domain arithmetic — `Money` is `long` minor units, and `decimal` appears only at presentation boundaries.
- **The hand-rolled dispatcher is binding.** Application layers use `ICommandHandler<T>` / `IQueryHandler<T,R>` / `IEventHandler<T>` resolved from the DI container in every service — no MediatR (v13 is commercially licensed). Registration is by assembly scan, and **startup validation fails fast if a command has no handler or more than one**. Durability never depends on the in-process bus: the `outbox` and `saga_commands` tables remain the guarantee, the in-process hop is only the fast path.
- **Explicit DI registration, and a startup validation pass.** #7's equivalent rule existed because NestJS could infer a token from `emitDecoratorMetadata` and silently resolve to `undefined` under a compiler that did not emit it — a failure invisible until first use. .NET has no such inference, so the *rule* changes shape but the *defence* does not: every port is registered explicitly in `Program.cs`, and the startup validation pass is what turns "a handler is missing" from a runtime surprise into a boot failure. The lesson #7 paid for is that DI failures must be loud at boot; keep it that way.
- **One `BackgroundService` per transport.** #7's services were hybrid NestJS apps where a bare `@MessagePattern` registered on *every* connected transport and crashed the boot — a bug that needed its own ESLint rule. In .NET a NATS responder and a Kafka consumer are different classes subscribing to different things, so the ambiguity does not exist. Do not reintroduce it by multiplexing transports through one service class.
- **Database per service.** No cross-database joins, no foreign keys across service boundaries. Fulfillment and Billing reference `CompanyCode`, `RetailerCode`, `ProductCode`, `OrderReference` — business identifiers carried in messages, never FKs into the Orders database.
- **The only shared runtime code** is `src/SharedKernel` (zero `PackageReference`) and `src/Contracts` (generated types). Nothing else is shared.
- **The JSON wire shape must match #7 byte for byte.** `camelCase`, nulls omitted, no `$type` discriminator, no PascalCase envelope — set once in a shared `JsonSerializerOptions` in `Contracts` so no service can drift. This is what makes the n8n workflows and the API test script portable, and it is a parity claim the benchmark depends on.
- **Kafka carries facts, NATS carries RPC.** Every inter-service interaction must be justifiable by one row of the decision matrix in `specs/shared/`. Never use Kafka as a request bus; never use RPC for facts.

## Coding conventions

| Topic | Rule |
|---|---|
| Language | C# 14 / `net10.0`, `Nullable` enabled, `ImplicitUsings` enabled, async all the way down |
| Money | **`long` minor units (cents) only.** Never a float, never `decimal` in domain arithmetic. Use the `Money` value object |
| Identifiers | UUID primary keys, generated in the domain via `UniqueId` (`uniqueidentifier` in MS-SQL) |
| Database columns | `snake_case` in MS-SQL, `PascalCase` in C# |
| JSON wire | `camelCase`, nulls omitted — identical to #7's bytes |
| Dates | UTC everywhere, `datetime2(3)` columns, ISO-8601 strings on the wire |
| Business references | `ORD-000001`, `DES-000001`, `INV-000001`, `CR-000001` — sequential, human-readable, unique, allocated under a row lock |
| Event types | `<aggregate>.<fact>.v<n>` — e.g. `order.placed.v1` |
| Naming | Files match the type name (`Order.cs`); types `PascalCase`; private fields `_camelCase`; interfaces `IPascalCase` — enforced by `.editorconfig` |
| Value objects | `sealed record` / `readonly record struct` where equality-by-value is wanted; `Entity`/`AggregateRoot` are classes with identity equality |
| Errors | Domain errors extend `DomainError` and carry a stable `Code` |
| Logging | Structured with `correlationId` on every line |
| Async | CS1998, CS4014, CA2016 and CA2213 are **errors**, not suggestions — see `.editorconfig`. Forward every `CancellationToken` |
| Markdown | **No hard line-wraps in prose** — one line per paragraph/list item/quote. Code blocks and tables are exempt |

## Testing conventions

- **xUnit is the backend runner. Vitest is the web runner. No Jest, anywhere.**
- Domain unit tests are **pure** — no framework, no DB, no mocks of infrastructure.
- Integration tests use **Testcontainers for .NET** (real MsSql / Kafka / NATS / MongoDB), never mocked brokers.
- API tests are black-box through the Gateway (xUnit runner + `HttpClient` as the client only), and must prove **the same script #7's API tests prove**.
- Web: Vitest + React Testing Library for components, Playwright for end-to-end.
- **Architecture tests are tests.** NetArchTest runs in the normal `dotnet test` pass, so a layering violation fails like any other test.
- **Tests are written inside the feature loop, not at the end of the project.**
- **Every branch that emits — or deliberately suppresses — a domain fact must be guarded by a test that fails when the emission is deleted.** Before submitting, the implementer arms that deletion itself and records in `progress/impl_<feature>.md` which named test failed and with what message. A fact-emitting branch whose emission survives its own deletion on a green suite is **not done** — with double force where the branch has no live caller yet, because integration harnesses cannot reach it. #7 learned this twice, on two different features, both correct code with no guard. Inheriting the lesson is free; rediscovering it is not.
- Coverage gates: **≥80% domain layer, ≥60% overall**, enforced by coverlet in `./quality.sh` regardless of SonarQube — and **verified to fail when breached**. #7 found its gate had been inert for twenty phases.
- Every EARS requirement `R<n>` maps to at least one named test in `specs/shared/test-matrix.md`. The ids are #7's: reusing one is a claim that the same requirement is satisfied here.

## Commit discipline

> **Claude never runs `git commit` or `git push`.** When a phase or feature is finished, stop and report (a) **what was done** and (b) **how to test it manually**. The human tests it, then commits. You may draft the message. The single exception: when the human says **"full wrap-up"**, that is the authorisation — then commit and push, update the plan document, refresh `README.md`, update `docs/PROCESS.md`, update the private stack-comparison document, and brief the next phase.

**Rule for the stack-comparison document:** only what a **committed file** proves gets marked confirmed. Anything learned from a probe, a spike or a deleted scratch directory goes in as pre-resolved, naming the phase that will promote it. A tick that stops anyone re-checking is the guard-that-does-not-guard pattern, which is the exact failure class this harness exists to catch.

One commit per phase/feature, never batched. Message format:

```
feat(billing): BuyerCredit aggregate + credit hold/release ledger

What: <what was developed in this phase>

Packages installed:
- <NuGet or npm package>  — <one-line purpose>
```

Never install a package without it appearing in that phase's commit message. The git history is process evidence: for this repository it must show **harness first, spec copy second, code after**.

## Environment notes

- The .NET SDK is pinned in `global.json` (`10.0.111`, `rollForward: latestPatch`). A pin that cannot be satisfied makes `dotnet` fail outright rather than silently pick another SDK — `init.sh` surfaces this.
- Node is pinned in `.nvmrc` (`nvm use`), pnpm via corepack. **Both exist for `apps/web` only** — the backend has no Node dependency. `init.sh`'s backlog validator is also Node: a deliberate reuse of #7's proven script rather than a rewrite that would muddy the benchmark.
- Analyzer **severities** live in the root `.editorconfig`; analyzer **enforcement** (`TreatWarningsAsErrors`, `AnalysisLevel`) lives in `Directory.Build.props`. `dotnet format` reads `.editorconfig` from the repository root, so `quality.sh` can run it once at solution level.
- The `dotnet-ef` global tool must be in the same version band as the EF Core packages before migrations are generated (Phase 6 precondition).
- The git remote is account-explicit (`https://peelmicro@github.com/...`) because two GitHub accounts are authenticated on this machine. #7 discovered this via a 403 on its first push; here it was set up front, and the first push succeeded first time.
- The MS-SQL container wants ~1.5–2 GB RAM and takes ~20–30 s to accept connections. Budget for it in compose healthchecks and in integration-test timeouts.
