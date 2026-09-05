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
- **Never forbid in a brief what the approved `tasks.md` mandates.** A gate-approved spec outranks the brief that dispatched the work — that is already the ruling here — so a brief that contradicts it does not constrain the subagent, it just manufactures a false finding for the reviewer to spend a round on. Found in feature 16, where the brief said "you MUST NOT edit `feature_list.json`" while task M5 of the approved task list said "set `order_saga_orchestrator` to `in_review` in `feature_list.json` and stop". The implementer correctly followed the spec and the review had to adjudicate a conflict that should never have existed. **Before writing a scope bound, read the whole task list** — not just its bookkeeping tasks — and phrase every bound around what it already mandates: "make no change to `feature_list.json` beyond the transition `tasks.md` itself asks for", "touch no service other than the ones `tasks.md` names".

  **This rule was then broken again, one phase later, by the leader who wrote it** — a brief for `fulfillment_stock` said "do not touch `src/Orders/`" while task group A of the approved spec required exactly three files there, for a cross-service change the feature genuinely needed. The first version of this rule said *read the task list's own bookkeeping tasks*, so it was read for bookkeeping and not for source. **The bound must be derived from the task list, never written from an assumption about which directories a feature ought to need** — a feature that composes with another service will say so in its tasks, and a coordinator who has not read them is guessing.
- **Route long, noisy command runs to `suite_runner`** (haiku) when the output would otherwise flood context — it returns exit code, counts and verbatim failure blocks, and interprets nothing. Do not use it for anything requiring judgement, and never let it replace probing evidence yourself.
- **`reviewer`: probe the claims, do not re-run the world.** Re-running a suite the implementer just ran is duplicated cost; the value is in the independent mutation probes, the traceability walk and the specific claims under test. Re-run in full only when the claim *is* about the full suite.

### `feature_list.json` is a single-writer file

Never run two subagents concurrently when both may write the backlog. They will not conflict on source — different features touch different directories — but a `reviewer` closing one feature and anything else transitioning another are both read-modify-write on the same JSON, and the later write silently reverts the earlier one.

The reason this is worth a rule rather than care: **`init.sh` cannot catch it.** A status reverted from `spec_ready` to `pending` is still a *valid* status, still has at most one `in_progress`, and still satisfies SDD coherence — so the coherence check passes while the state is wrong. That is the guard-that-does-not-guard shape once more, and the only defence is not to create the race. Found in Phase 8, where a review and a spec revision were launched in parallel and a `spec_ready` transition was lost.

**And no agent may run `git checkout --` on `feature_list.json`, ever.** Found in feature 16, where a reviewer reformatted the file, thought better of it, and reverted with `git checkout --` — which restored the *last committed* version and silently destroyed the leader's uncommitted backlog entry for a defect found in already-closed work. The file is almost always dirty: it carries the current feature's transitions and anything anyone has added since the last commit, so reverting it to HEAD discards other writers' work by construction, not by accident.

**It happened a second time, one feature after the rule was written, and the second occurrence names the real trigger.** An implementer rewrote the file with a JSON round-trip that lacked `ensure_ascii=False`, re-escaping every non-ASCII character in the file, and reached for `git checkout --` to undo the mess. Both incidents began the same way: **a whole-file rewrite went wrong, and reverting looked like the only way back.** So the rule as written — *to undo an edit, re-edit it* — is sound advice that arrives too late, because by then there is a whole mangled file to re-edit rather than one line.

The actionable form is therefore upstream of the revert: **do not rewrite this file to change one value.** Edit the single line. If you do parse and re-serialise it, `json.dumps(..., indent=2, ensure_ascii=False)` reproduces this file's formatting exactly, and `git diff` showing **only the lines you meant to change** is the check that it did — run that check *before* moving on, while the mistake is still one command from being fixed by hand. Not a line count: a legitimate edit that adds a backlog entry is a dozen lines or more, so counting insertions would cry wolf. Read the diff.

The reason this needs saying separately from the arming protocol's own no-`git checkout` rule is that the arming rule is justified by files being **untracked** — and `feature_list.json` is tracked, so a reader who has internalised that rule will conclude it does not apply. It applies more. And **`init.sh` cannot catch it**: a backlog with a feature missing is still a valid backlog, still has at most one `in_progress`, still satisfies SDD coherence. That is now the third disguise of the guard-that-does-not-guard in this file — a check that fires on nothing, a check run against the wrong artefact, and a check whose invariants are all satisfied by an incorrect state. To undo an edit to this file, re-edit it.

Parallelism across subagents is still worth having — just never with the backlog in two writers' hands at once. Sequence the one that writes it, or have only one of them own it.

### The injected copy of this file is a cache — check the disk

This repository amends its own conventions at human gates, mid-project, on purpose: the wire-shape non-negotiable changed in Phase 5, and the arming protocol gained two clauses in Phases 5 and 6. Any copy of this file injected into an agent's context was taken when that session started and **is expected to go stale**.

So: before enforcing or quoting a rule from here — in a brief, in a review, in a report — `grep` the file on disk. A reviewer that rejects work against a superseded rule is a guard firing on something no longer true, which is the guard-that-does-not-guard inverted and just as expensive. Found in Phase 7, where it produced one spurious advisory against correct code.

### The ported-idiom ledger — the one defect class nothing else here can see

**Every `design.md` for a feature that ports a #7 mechanism carries a short section listing, one line per ported idiom: *"#7 relied on X; in #8 that property is supplied by Y."* Where the property was supplied by #7's engine, language or library and must be hand-built here, a guard test is required and named in `tasks.md`.** Adopted at the human gate closing Phase 8.

The evidence is three defects, and what makes them one class is not the mechanism but the way they hid:

| Property | #7 got it from | #8's rendering | How it surfaced |
|---|---|---|---|
| Payload key order on the wire | MySQL's `json` column normalisation, leaking through the relay | Treated as a byte-exact parity requirement | Captured twelve real envelopes and looked |
| Money never truncates | JavaScript numbers have no narrowing conversion | `int` columns with a narrowing cast, justified as "spec parity" | The human asked whether it was a mistake |
| The counter row seeds atomically | `INSERT … ON DUPLICATE KEY UPDATE`, unconditional | `IF NOT EXISTS (SELECT …) INSERT` — check-then-act | A review of a *later* feature read the SQL |

**All three satisfied their requirement text exactly.** So `R<n>` → test traceability cannot see them: the requirement was met. And **arming cannot see them either**, because the behaviour was present and correct on the path the test took — the lost property only shows under a condition the test never created (a second writer, a value above `int.MaxValue`, a different storage engine). Two of this repository's three strongest guards are structurally blind to this class, which is why it needs its own line rather than more of either.

None of the three was found by the process. One was found by capturing real bytes, one by the human asking a question, one by a reviewer reading SQL for an unrelated feature. That is a 0-for-3 detection record on a class that has cost real rework every time, and phases 9–13 port five more services from the same source.

**Writing the line is most of the value.** The failure in all three cases was not analytical difficulty — it was that nobody asked *"what made this correct over there, and does that thing exist here?"* at the moment of translating. A one-line ledger forces the question at spec time, when the translation is being thought about anyway and the answer is nearly free.

### Never hand the human an open question you could have closed

Before anything reaches the human gate, ask **"did #7 face this, and what did it do?"** #7's checkout is on disk; the answer is in its committed code or its `progress/history.md`, and fetching it is cheaper and far more reliable than a gate round-trip. Only what #7 **could not** face — because the engine or the language differs — is genuinely a decision.

This applies to the leader at least as much as to any subagent, and the leader is the one who keeps failing it. A subagent that applies the test and reports *"#7 deferred this, here is the citation"* has done its job; relaying that to the human as an open question undoes the work and wastes the gate. **Twice now the human has had to ask "is that something that was not already decided?"** — the second time about a finding the leader had personally verified two messages earlier.

So: when a subagent raises an open point, resolve it before passing it on, and if it genuinely must go to the gate, **go with a recommendation and the evidence behind it** — never a menu. A gate exists for judgement the human alone can supply, not for questions the repository already answers.

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
- **The hand-rolled dispatcher is binding** (human gate ruling, Phase 8 — ratified across all six services, matching #7's own gate ruling at its feature 16)**.** Application layers use `ICommandHandler<T>` / `IQueryHandler<T,R>` / `IEventHandler<T>` resolved from the DI container in every service — no MediatR (v13 is commercially licensed). Registration is by assembly scan, and **startup validation fails fast if a command has no handler or more than one**. Durability never depends on the in-process bus: the `outbox` and `saga_commands` tables remain the guarantee, the in-process hop is only the fast path.

  **Why all six, when it does not fit all six equally.** In Orders, Fulfillment and Billing the fit is obvious. In the Gateway the "commands" are NATS RPC calls *outward*, so the dispatcher sits in front of an outward client; in Notifications and Projector — pure consumers with roughly one handler per fact type — it adds a hop that a direct call would not need. That indirection is accepted deliberately, for one reason: **#7 used `@nestjs/cqrs` in all six, and a #8 that used its dispatcher in three would stop the benchmark comparing like with like.** The per-feature effort numbers for Notifications and Projector would then reflect a different architecture rather than a different language, which is the one thing this repository exists to measure. Recorded as a parity trade-off in the README, not as a claim that the layer earns its keep everywhere.
- **Explicit DI registration, and a startup validation pass.** #7's equivalent rule existed because NestJS could infer a token from `emitDecoratorMetadata` and silently resolve to `undefined` under a compiler that did not emit it — a failure invisible until first use. .NET has no such inference, so the *rule* changes shape but the *defence* does not: every port is registered explicitly in `Program.cs`, and the startup validation pass is what turns "a handler is missing" from a runtime surprise into a boot failure. The lesson #7 paid for is that DI failures must be loud at boot; keep it that way.
- **One `BackgroundService` per transport.** #7's services were hybrid NestJS apps where a bare `@MessagePattern` registered on *every* connected transport and crashed the boot — a bug that needed its own ESLint rule. In .NET a NATS responder and a Kafka consumer are different classes subscribing to different things, so the ambiguity does not exist. Do not reintroduce it by multiplexing transports through one service class.
- **Database per service.** No cross-database joins, no foreign keys across service boundaries. Fulfillment and Billing reference `CompanyCode`, `RetailerCode`, `ProductCode`, `OrderReference` — business identifiers carried in messages, never FKs into the Orders database.
- **The only shared runtime code** is `src/SharedKernel` (zero `PackageReference`), `src/Contracts` (generated types) and `src/Cqrs` (the in-process dispatcher). Nothing else is shared.

  **`src/Cqrs` is a #8-only third project, added at the human gate in Phase 8, and it exists because of an earlier ruling rather than a new preference.** The dispatcher is binding across all six services; #7 got that capability from `@nestjs/cqrs`, a package, so it never needed a home for it. #8 hand-rolls it (MediatR v13 is commercially licensed), and it needs `Microsoft.Extensions.DependencyInjection.Abstractions` — which `SharedKernel` may not have, because an architecture test asserts `SharedKernel` carries **zero** package references and that rule is worth more than the convenience. `Contracts` is the wire contract, versioned by `asyncapi.yaml`; an in-process bus is not a wire concern. So the third project is the consequence of a decision already taken, not a widening of what may be shared.

  **It does not widen what the domain may reach for.** `src/Cqrs` is an **Application-layer** concern: handlers live in `Application/`, and no `Domain/` namespace may reference `OrderToCash.Cqrs`. An architecture test enforces that, because nothing else would.
- **The JSON wire shape must match #7 — envelope byte-exact, payload semantically equal.** `camelCase`, nulls omitted, no `$type` discriminator, no PascalCase envelope, set once in a shared `JsonSerializerOptions` in `Contracts` so no service can drift. This is what makes the n8n workflows and the API test script portable, and it is a parity claim the benchmark depends on.

  The rule is split deliberately, and the reason is evidence rather than preference. Twelve real #7 envelopes were captured from its retained Kafka topics in Phase 5 and are committed under `tests/Contracts.UnitTests/GoldenEnvelopes/`. They show the **envelope**'s seven fields in the order `asyncapi.yaml` declares them — `eventId`, `eventType`, `aggregateId`, `correlationId`, `causationId`, `occurredAt`, `payload` — which #8 matches exactly, and the golden files prove it.

  They also show the **payload**'s keys ordered by key length then alphabetically, which is **MySQL's `json` column normalisation**, not a serializer decision: #7's outbox relay reads the payload back out of that column and republishes it, so a storage artifact reached its wire. Verified on a single `eventId` present in both stores. #8 keeps payloads in `nvarchar(max)`, which preserves insertion order, so byte-equality of the payload would mean deliberately emulating another engine's storage quirk forever — and #9 on PostgreSQL could not do it either. JSON object key order carries no meaning and nothing downstream reads it: n8n parses by key, the projector reads fields, the API tests assert values. So the payload is asserted **semantically** — same keys, same values, same types, same casing — and key order is not a parity claim.

  This is not a spec amendment: `specs/shared/` is silent on key ordering (its "byte-for-byte" language concerns DLQ redrive, which is a different guarantee). It is a #8 convention, gated by the human, recorded here.
- **Kafka carries facts, NATS carries RPC.** Every inter-service interaction must be justifiable by one row of the decision matrix in `specs/shared/`. Never use Kafka as a request bus; never use RPC for facts.

## Coding conventions

| Topic | Rule |
|---|---|
| Language | C# 14 / `net10.0`, `Nullable` enabled, `ImplicitUsings` enabled, async all the way down |
| Money | **`long` minor units (cents) only**, in the domain **and in the column** (`bigint`). Never a float, never `decimal` in domain arithmetic. Use the `Money` value object. A narrowing cast on a money value is a defect, not something to make loud — `specs/shared/` requires "integer minor units" and never a width, so a storage type narrower than the domain type buys nothing and costs a boundary that can truncate |
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
- **Arming protocol — how a guard is proven, and the one way it silently lies.** To arm a guard: introduce the violation, run the specific named test, confirm it FAILS and record the message verbatim, then restore. **After restoring, force the rebuild** (`touch` the restored file, or `dotnet build --no-incremental`) **before the confirming green run.** **Restore from a backup copy you took, never with `git checkout --`** — most files are untracked while a feature is in flight, and `git checkout` on an untracked path fails with `pathspec did not match any file(s) known to git`, restoring nothing and leaving the file **still armed** while its own error scrolls past. Confirm the restore by re-reading the changed line. A byte-for-byte `cmp` against your backup is a source-level check only: if the restore preserved the backup's timestamp, MSBuild's incremental check sees the source as older than its output, skips the compile, and the confirming run executes the **previously armed binary**. Found live in feature 7, where it produced a false red; the same mechanism produces a false green — a stale-but-correct binary vouching for source that is still armed. An arming table produced without a forced rebuild proves nothing about the code on disk.
- **Deleting the emission is one mutation family, not the whole of arming. Corrupt the payload too.** The protocol above says *delete the behaviour and watch the test fail*, and a guard can pass that perfectly while never reading what the fact contains. Found in feature 17: a task said *"exactly one `stock.released.v1` carrying the request's `reason`"*, the test counted the row and never opened it, and corrupting that `reason` **and** another fact's `retailerCode` on the wire left the whole suite green — 79/79 and 48/48. The reviewer missed it in its own first pass for the same reason, and said so: all six of its probes attacked emission deletion.

  **And a corruption probe only bites on a field whose expected value the test supplied.** For fields the test does not control — ids, clocks, generated references — inject the source (a delegate, a fake clock) or bracket the value, or the field is unguarded however many probes you run. Found in feature 18: a test named for two ids being *the delegate's returned values* asserted only that they were non-default and distinct, which any two GUIDs satisfy; substituting the source left the suite green. `Assert.NotEqual` proves non-collision and can never prove provenance.

  So a fact-emitting branch needs both questions asked of it: **does the guard fail when the row is absent, and does it fail when a field is wrong?** They find different defects, and a suite that only ever answers the first will ship payload defects indefinitely — with a wire contract, a saga that branches on `reason`, and five services still to build, the second question is the more expensive one to leave unasked.

- **Every branch that emits — or deliberately suppresses — a domain fact must be guarded by a test that fails when the emission is deleted.** Before submitting, the implementer arms that deletion itself and records in `progress/impl_<feature>.md` which named test failed and with what message. A fact-emitting branch whose emission survives its own deletion on a green suite is **not done** — with double force where the branch has no live caller yet, because integration harnesses cannot reach it. #7 learned this twice, on two different features, both correct code with no guard. Inheriting the lesson is free; rediscovering it is not.
- **A task that makes a countable claim must be armed, whether or not it carries the arming flag — and `tasks.md` must flag every such task.** Found twice, identically. The saga orchestrator's committed-offset task said *"read the group's committed offset from the broker; do not infer it from the redelivery alone"*; it was ticked, it inferred, and the offset contract shipped unguarded. Fulfillment's reservation tasks said *"exactly one `stock.reserved.v1`"* and *"exactly one … and one `stock.rejected.v1`"*; both ticked, and deleting the rejection fact's persistence left **both** suites fully green.

  Both features armed their **flagged** tasks perfectly — 11 of 11 and 12 of 12. The defect is not carelessness, it is that the arming discipline attaches to the flag rather than to the claim, so a task whose prose says *exactly one row* gets written, ticked and never mutated because nobody marked it. **A tick is not evidence the assertion exists.** If a task asserts a count, an identity, an ordering or an absence, it is a guard, and a guard is not done until it has been seen to fail.

- **A negative claim about the repository is a search result, not a reading.** *"No test does X"*, *"no instrument does Y"*, *"nothing else has this shape"* — a claim of absence is reportable only as **(a)** the exact command that enumerates the candidate set, **(b)** its complete output, and **(c)** one classification line per hit. Prose sweeps have been reported clear and disproved within minutes **three times** (feature 17, then feature 46 twice), each time by someone who ran a command instead of re-reading. A missed hit must be visible as an **unclassified line**, not invisible as a sentence.

  The decisive evidence is that the third miss was already written down: the instance the sweep failed to mention was sitting in `progress/history.md`'s own Phase-9 note, committed eleven minutes before that feature started. **Recording something in prose does not stop a prose sweep from missing it** — only enumeration does. And the corollary that makes this cheap rather than bureaucratic: the enumerating command is usually one `grep`, and it is the same artefact whether the answer is "clear" or "three hits".

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
