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

**Every feature that ports a #7 mechanism carries a ledger: a short section listing, one line per ported idiom, *"#7 relied on X; in #8 that property is supplied by Y."* Where the property was supplied by #7's engine, language or library and must be hand-built here, a guard test is required and named.** Adopted at the human gate closing Phase 8; **bound to the port rather than to the document at the human gate opening Phase 13.**

**Where the ledger lives depends on the feature, and this is the part that was wrong for four phases.** For an `sdd: true` feature it belongs in `design.md`, with its guards named in `tasks.md`. For an **`sdd: false` feature it belongs in `progress/impl_<feature>.md`**, beside the arming table the implementer already writes there, with its guards named in the same place — no new document and no extra ceremony.

**The original rule said `design.md`, and an `sdd: false` feature has none — so the rule silently exempted exactly the features most likely to need it.** Phase 10 found the consequence and recommended the fix: feature 22 ported #7's payment handler wholesale, had no `design.md`, carried no ledger, and its one finding of substance was a textbook ledger miss — *#7 relied on a `markPaid` that returns its event id; in #8 that property is supplied by nothing.* Traceability could not see it (the requirement was satisfied), arming could not see it (nine mutations, eight kills, none nearby), and it took a reviewer reading #7's source for an unrelated question.

**The recommendation then sat unactioned for two phases while three ported services shipped**, which is its own finding: a recommendation inside a closing assessment is read once, by whoever writes the next brief, and then only if they go looking. Phase 13 is where it finally bit hard enough to act on — every one of its six features is `sdd: false`, and its central feature ports #7's gateway wholesale. **#7's own record is the argument**: its `gateway_rest_auth` was rejected twice, and the first rejection was, in its historian's words, *the first defect no test in the repository could have caught, because both sides of the seam were tested only against the wire each preferred.* A NATS wire mismatch between a client and the responders it calls, each side green against its own assumption. That is a ported-idiom defect in its purest form, and under the old rule the #8 feature that inherits it would have owed no ledger at all.

The evidence is three defects, and what makes them one class is not the mechanism but the way they hid:

| Property | #7 got it from | #8's rendering | How it surfaced |
|---|---|---|---|
| Payload key order on the wire | MySQL's `json` column normalisation, leaking through the relay | Treated as a byte-exact parity requirement | Captured twelve real envelopes and looked |
| Money never truncates | JavaScript numbers have no narrowing conversion | `int` columns with a narrowing cast, justified as "spec parity" | The human asked whether it was a mistake |
| The counter row seeds atomically | `INSERT … ON DUPLICATE KEY UPDATE`, unconditional | `IF NOT EXISTS (SELECT …) INSERT` — check-then-act | A review of a *later* feature read the SQL |

**All three satisfied their requirement text exactly.** So `R<n>` → test traceability cannot see them: the requirement was met. And **arming cannot see them either**, because the behaviour was present and correct on the path the test took — the lost property only shows under a condition the test never created (a second writer, a value above `int.MaxValue`, a different storage engine). Two of this repository's three strongest guards are structurally blind to this class, which is why it needs its own line rather than more of either.

None of the three was found by the process. One was found by capturing real bytes, one by the human asking a question, one by a reviewer reading SQL for an unrelated feature. That is a 0-for-3 detection record on a class that has cost real rework every time, and phases 9–13 port five more services from the same source.

**A ledger row's Guard column is itself a countable claim, and a countable claim is not done until it has been seen to fail.** Adopted after feature 19, where a row correctly identified a property #7 got from its database driver, the code that supplied it was correct, and the row's named guard **could not fail**: the test re-implemented the conversion instead of reading through the mapper, so the task list's own prescribed mutation left the suite green. The enumeration worked and the guard was decorative — which is the ledger's own version of the failure it exists to catch, one level up. Naming a guard in `tasks.md` creates the obligation to arm it; it does not discharge it.

**An engine claim probed in one direction is a claim about that direction only.** A ledger row asserting how a database, driver or broker behaves is a **countable claim about a symmetric situation**, and running the probe one way answers half of it. Found twice on the same row, in consecutive rounds of feature 24: the row first claimed two index filters would conflict (they do not), and its correction then explained the right answer with the wrong mechanism — that the server normalises a type alias *when storing* it. It does not; it normalises when *comparing*. Creating with the alias first stores the alias, which the reverse-order probe showed immediately and the forward-order probe could never have shown.

**Where a row's claim involves two parties — two writers, two orderings, two creation sequences — the probe runs both ways or the row states which way it was run.** The second occurrence is the one that makes this a rule rather than a note: the wrong mechanism had reached a **test name**, and a test name is how the next assessment reads a question as settled. A ledger exists to carry properties across stacks; a confidently wrong row in it is worse than an absent one, because it will be inherited rather than re-derived.

**A ledger row has two halves, and the *"#7 relied on X"* half is a claim about #7's source code — so it is read out of #7's checkout, with a file and line, never inferred from what the framework would plausibly have done.** Found on the very first feature to carry a ledger under the port-bound rule, in phase 13. The row said #7 got one-instance reuse from *"module-scoped provider singletons … declaratively, with nothing to configure wrong."* #7's `app.module.ts:123-137` does the opposite: an explicit `useExisting` alias between two distinct tokens, carrying its own warning comment about why it is not a second factory. The row's **guard** half was real and had teeth; its **history** half was written from an assumption about NestJS.

That asymmetry is why this needs its own rule rather than more care. A wrong guard fails loudly the moment it is armed. **A wrong history half cannot fail at all** — nothing executes it, the guard beside it still passes, and it is precisely the half #9 inherits and has no reason to re-derive. The ledger exists to carry *what made the original correct*; a row that misdescribes that has kept the ceremony and thrown away the cargo.

It also costs almost nothing to get right: #7's checkout is on disk, and the difference between *"NestJS singletons make this automatic"* and *"#7 aliased two tokens with `useExisting` and left a comment explaining why"* is one `grep`. Cite the file and line in the row, the way any other claim about another repository is cited here.

**Both halves of a row are claims, and tightening one displaces attention onto it.** Two consecutive phase-13 features were rejected with **the citation half correct and the guard half hollow** — the second one naming a guard whose own *"in #8"* column already explained that the test does not call the code the row is about. The rule requiring a file-and-line citation into #7 was added between those two features, and it worked: the citations became accurate. It also moved where the care went.

That is worth stating because it is the general shape of every fix in this file: **a rule that hardens one half of a two-part claim does not harden the other, and will quietly borrow attention from it.** When you write a row, the last thing to check is not the sentence you just tightened — it is the one you did not. For a ledger row that means reading the named guard and asking *does this test execute the code this row is about?*, which is a different question from *does this test pass?* and from *does deleting the behaviour break it?*

**Writing the line is most of the value.** The failure in all three cases was not analytical difficulty — it was that nobody asked *"what made this correct over there, and does that thing exist here?"* at the moment of translating. A one-line ledger forces the question at spec time, when the translation is being thought about anyway and the answer is nearly free.

### When you port a mechanism, port its guards — enumerate #7's tests, not only its source

**Both phase-13 rejections were the same shape: a guard #7 wrote, dropped in translation, found by review rather than by us.** Feature 40 lost #7's `includeDisabled` controller assertion, so forcing that flag to a constant left the whole suite green. Feature 41 lost #7's compensation-reason assertion, so transposing the two arms of the reason branch left 337 unit and 6 container tests green — and that one mattered, because a mislabelled reason makes `Order.Cancel` throw and strands the order mid-compensation.

Neither was hard to prevent. **Both guards existed, in a checkout on this machine, in files named after the thing being ported.** The ledger already forces reading #7's *source* to answer *"what supplied this property there"*; nothing forced reading #7's *tests* to answer **"what did they check about it, and does an equivalent exist here?"**

So, when porting: **enumerate #7's test files for the mechanism and classify each assertion — ported, deliberately not ported (with the reason), or not applicable.** It is a search result, not a reading: the command, its complete output, one line per hit.

This is narrower than it sounds, and that is the point. It does not ask for #7's tests to be reproduced — #8's suites are structured differently and frequently assert more. It asks that a guard which existed and stopped existing be **noticed**, because the alternative is what happened twice in one phase: shipping a branch whose behaviour nothing checks, in code whose predecessor checked it.

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
- **Arming protocol — how a guard is proven, and the one way it silently lies.** To arm a guard: introduce the violation, run the specific named test, confirm it FAILS and record the message verbatim, then restore. **After restoring, force the rebuild** (`touch` the restored file, or `dotnet build --no-incremental`) **before the confirming green run.** **Restore from a backup copy you took, never with `git checkout --`** — most files are untracked while a feature is in flight, and `git checkout` on an untracked path fails with `pathspec did not match any file(s) known to git`, restoring nothing and leaving the file **still armed** while its own error scrolls past. Confirm the restore by re-reading the changed line. **And do not offer `git diff` or `git diff --stat` on the mutated file as proof that the restore was clean** — the same untrackedness that makes `git checkout` fail makes `git diff` print nothing, so on the files this protocol usually touches that check **cannot fail**. It is the guard-that-does-not-guard appearing inside the restore step of the very protocol built to prevent it. Found in feature 21, where the restore was genuinely correct and the evidence offered for it was worthless. Use `cmp` against your backup, or read the line. A byte-for-byte `cmp` against your backup is a source-level check only: if the restore preserved the backup's timestamp, MSBuild's incremental check sees the source as older than its output, skips the compile, and the confirming run executes the **previously armed binary**. Found live in feature 7, where it produced a false red; the same mechanism produces a false green — a stale-but-correct binary vouching for source that is still armed. An arming table produced without a forced rebuild proves nothing about the code on disk.
- **Deleting the emission is one mutation family, not the whole of arming. Corrupt the payload too.** The protocol above says *delete the behaviour and watch the test fail*, and a guard can pass that perfectly while never reading what the fact contains. Found in feature 17: a task said *"exactly one `stock.released.v1` carrying the request's `reason`"*, the test counted the row and never opened it, and corrupting that `reason` **and** another fact's `retailerCode` on the wire left the whole suite green — 79/79 and 48/48. The reviewer missed it in its own first pass for the same reason, and said so: all six of its probes attacked emission deletion.

  **And a corruption probe only bites on a field whose expected value the test supplied.** For fields the test does not control — ids, clocks, generated references — inject the source (a delegate, a fake clock) or bracket the value, or the field is unguarded however many probes you run. Found in feature 18: a test named for two ids being *the delegate's returned values* asserted only that they were non-default and distinct, which any two GUIDs satisfy; substituting the source left the suite green. `Assert.NotEqual` proves non-collision and can never prove provenance.

  So a fact-emitting branch needs both questions asked of it: **does the guard fail when the row is absent, and does it fail when a field is wrong?** They find different defects, and a suite that only ever answers the first will ship payload defects indefinitely — with a wire contract, a saga that branches on `reason`, and five services still to build, the second question is the more expensive one to leave unasked.

- **Every branch that emits — or deliberately suppresses — a domain fact must be guarded by a test that fails when the emission is deleted.** Before submitting, the implementer arms that deletion itself and records in `progress/impl_<feature>.md` which named test failed and with what message. A fact-emitting branch whose emission survives its own deletion on a green suite is **not done** — with double force where the branch has no live caller yet, because integration harnesses cannot reach it. #7 learned this twice, on two different features, both correct code with no guard. Inheriting the lesson is free; rediscovering it is not.
- **A task that makes a countable claim must be armed, whether or not it carries the arming flag — and `tasks.md` must flag every such task.** Found twice, identically. The saga orchestrator's committed-offset task said *"read the group's committed offset from the broker; do not infer it from the redelivery alone"*; it was ticked, it inferred, and the offset contract shipped unguarded. Fulfillment's reservation tasks said *"exactly one `stock.reserved.v1`"* and *"exactly one … and one `stock.rejected.v1`"*; both ticked, and deleting the rejection fact's persistence left **both** suites fully green.

  Both features armed their **flagged** tasks perfectly — 11 of 11 and 12 of 12. The defect is not carelessness, it is that the arming discipline attaches to the flag rather than to the claim, so a task whose prose says *exactly one row* gets written, ticked and never mutated because nobody marked it. **A tick is not evidence the assertion exists.** If a task asserts a count, an identity, an ordering or an absence, it is a guard, and a guard is not done until it has been seen to fail.

- **A negative claim about the repository is a search result, not a reading.** *"No test does X"*, *"no instrument does Y"*, *"nothing else has this shape"* — a claim of absence is reportable only as **(a)** the exact command that enumerates the candidate set, **(b)** its complete output, and **(c)** one classification line per hit. Prose sweeps have been reported clear and disproved within minutes **three times** (feature 17, then feature 46 twice), each time by someone who ran a command instead of re-reading. A missed hit must be visible as an **unclassified line**, not invisible as a sentence.

  **A sweep must not filter by the property it is testing.** Three instances in phase 13 alone, and it is the sharpest form of the guard-that-does-not-guard because the filter looks like scoping rather than like an assumption. A route sweep asserting *"every endpoint except the public ones requires auth"* selected its candidate set **by the `IAllowAnonymous` metadata under test** — so marking `GET /orders` anonymous removed it from the sweep instead of failing it, and 141 tests stayed green. A disclosure of six unpaced retry loops enumerated only four, because two hits were excluded on a *readiness* ground while the claim being made was about *pacing*. And `grep -rn <pat> | grep -v '/bin/'` excludes by matching the output line's **content**, which includes the matched text, not only its path.

  **Its commonest disguise is filtering by filename when the claim is about content.** The guard-enumeration rule above — *enumerate #7's tests for the ported mechanism* — was applied twice before its own enumeration was caught doing exactly this: `find … | grep -iE "sse|stream"` over **filenames**, when the property being enumerated is which assertions *mention* the mechanism. A content-based command found a guard in a file whose name says nothing about streams, and the file-granular classification silently swallowed a second one inside a file that was listed. **Classify the unit the claim is about** — if the claim is about assertions, classify assertions, not the files containing them; a per-file line hides everything the file contains.

  The shape is always the same: **the predicate that decides membership is derived from the thing under test**, so a violation removes itself from the population rather than showing up in it. The fix is equally consistent — **make the expected set a literal and derive the rest by subtraction.** #7's version of that same route sweep hard-codes its public set and derives the protected set by subtraction, which is why #7's would have caught what #8's could not. When you write any sweep, ask what a violation would do to the candidate list: if the answer is *leave it*, the sweep cannot fail.

  **A list of places a correction must reach is the same prose sweep, wearing the clothes of a fix.** Found in feature 24, on a ledger row already corrected twice. Round 2 closed with *"the fix reached five places"*; round 3 found the disproved claim alive in three more, one of them the production source file the row exists to justify. What makes this its own trap rather than a repeat of the rule above is **which word everybody searched for**: the fix was about a *mechanism*, so implementer and reviewer both grepped the mechanism word — and the residue was carrying the **first** wrong version, whose wording shares no term with the correction. A grep for the new claim structurally cannot find text asserting the old one.

  So: **enumerate on the wording of the claim being retired, not the claim being written**, and where a thing has been wrong twice, enumerate for both. The command that found all seven hits was one `grep` for a five-word phrase from the original text, and it ran in under a second — against three rounds of prose sweeps that each missed them.

  **And the enumerating command must exclude by path, not by post-filtering its own output.** `grep -rn <pat> --include='*.cs' . | grep -v '/bin/\|/obj/'` reads as a path exclusion and is not one: `grep -rn` emits `path:lineno:content`, so the filter matches **content** too, and silently drops any hit whose matched line happens to mention a build directory. Found in feature 24, where that form returned **16** hits and the equivalent path-excluding form returned **19**, both perfectly stable across eight runs each — a discrepancy first misdiagnosed as a nondeterministic `grep`.

  The three suppressed lines are what make this worth a rule: **all three were quotations of the enumeration command itself**, dropped because the command contains `/bin/`. The lines most likely to quote the command are the records *of* the sweep, so this filter preferentially deletes the evidence that the sweep happened, from the artefact whose whole purpose is to prove it was complete — and a reviewer re-running the recorded command reproduces the same 16 and confirms nothing. Exclude at the source: `find . \( -name '*.cs' \) -not -path '*/bin/*' -print0 | xargs -0 grep -n <pat>`, or anchor the filter to the path as `init.sh` already does (`grep -vE '^\./(…|bin/|obj/)'` against a **file list**), which is why the harness has never carried this defect.

  The decisive evidence is that the third miss was already written down: the instance the sweep failed to mention was sitting in `progress/history.md`'s own Phase-9 note, committed eleven minutes before that feature started. **Recording something in prose does not stop a prose sweep from missing it** — only enumeration does. And the corollary that makes this cheap rather than bureaucratic: the enumerating command is usually one `grep`, and it is the same artefact whether the answer is "clear" or "three hits".

- **A retry budget counted in attempts assumes each attempt costs time.** Found in phase 13, while arming the fix for a real flake. A readiness loop retried 100 times with no delay, pacing itself on the request timeout — but the error it retries on, NATS's *no responders*, is returned by the server almost instantly instead of timing out, so the whole budget can expire in about a millisecond and report *not ready* while the subscription was 1 ms away. The same unpaced shape sits in four other fixtures, latent because their callers happen to warm up first (backlog id 63).

  Two things generalise. **Pace a retry loop explicitly**, rather than assuming the failure path is slow — the fast-failing error is the one that breaks the assumption, and it is invisible while every failure happens to be a timeout. And **prove the pacing with a change of kind, not of probability**: delay the dependency by a controlled interval and show the unpaced loop loses *every* time and the paced loop wins *every* time. "The flakes stopped" is not evidence, for the same reason a green run is not evidence about a red one.

- Coverage gates: **≥80% domain layer, ≥60% overall**, enforced by coverlet in `./quality.sh` regardless of SonarQube — and **verified to fail when breached**. #7 found its gate had been inert for twenty phases.
- Every EARS requirement `R<n>` maps to at least one named test in `specs/shared/test-matrix.md`. The ids are #7's: reusing one is a claim that the same requirement is satisfied here.

## Commit discipline

> **Claude never runs `git commit` or `git push`.** When a phase or feature is finished, stop and report (a) **what was done** and (b) **how to test it manually**. The human tests it, then commits. You may draft the message. The single exception: when the human says **"full wrap-up"**, that is the authorisation — then commit and push, update the plan document, refresh `README.md`, update `docs/PROCESS.md`, update the private stack-comparison document, and brief the next phase.

**Rule for the stack-comparison document:** only what a **committed file** proves gets marked confirmed. Anything learned from a probe, a spike or a deleted scratch directory goes in as pre-resolved, naming the phase that will promote it. A tick that stops anyone re-checking is the guard-that-does-not-guard pattern, which is the exact failure class this harness exists to catch.

**A claim of completeness is a count, and a count is a reading.** Neither *"all"*, *"every"*, *"both"*, *"the last"* nor *"complete"* belongs in a commit subject unless the thing counted was enumerated first — and a bare number belongs there only if it was read off a run in the same session. **This is now enforced by `scripts/git-hooks/commit-msg`**, installed by `init.sh` and checked by it on every run, because the coordinator wrote a false count into a subject **twice** and the rule that came out of the first occurrence did not prevent the second. Both were written at the same point in the workflow — a session-closing summary — about the same kind of quantity, with the true figure one command away. **A rule whose only enforcement is the author's memory fires exactly when attention is elsewhere.**

To satisfy the hook, put the enumeration in the body: the command and its output, or a `counted: …` line. **The hook checks the subject only** — the first of the two errors was a figure in the body, whose failure was provenance rather than presence, and no pattern can see that. Half the problem is mechanical now; the other half is still discipline, and a green hook does not mean the numbers in the message are true.

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
