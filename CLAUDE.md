# CLAUDE.md — Leader role and project conventions

> Loaded automatically in every session and every agent. Kept deliberately short, because it rides along on every turn. `AGENTS.md` is the repository map. **`docs/lessons.md` holds the incident record and reasoning behind each rule below.** Read the relevant section of it only when you need the *why*.

## Project

**Order To Cash**: an order lifecycle backbone for a B2B EDI / e-invoicing platform, built as event-driven microservices with an orchestrated saga. It is assessment **#8 of a trilogy** (#7 = NestJS, completed; #9 = FastAPI), all implementing the same specification.

- **`specs/shared/` is read-only.** A change is a **spec amendment (`SA-n`)**: human-gated, committed on its own, applied byte-identically to #7, and recorded in the README registry and in both repositories' `progress/history.md`.
- **Per-feature effort is recorded** in `progress/history.md` against #7's baseline, including the features that were not faster.

## Cost discipline (maintainer ruling, 2026-09-17)

Phase 16 used 24% of a weekly allowance in under a day: 92% of it in subagents, and 85% at over 150k context. These rules come first.

- **Size the process to the change.**
  - **Light** (UI text, layout, config, docs, formatting, test-only fixes): one implementer, with a short brief. The leader reads the diff and runs the affected tests. No separate reviewer, no premise-checker agent, no full defeat-list walk; one arm of the actual fix is enough.
  - **Full** (saga, money domain, wire contract, `specs/shared/`, persistence, security): implementer, then reviewer, with arming and the defeat list.
  - When unsure, choose light and say so in the report.
- **Reviewer model:** launch the reviewer with `model: "sonnet"` unless the change is in the full group. Opus is for saga, money-domain, spec and contract reviews.
- **At most one rejection round without asking.** After a second rejection, stop and ask the maintainer whether to continue, accept with a disposition, or defer.
- **#7 is complete.** Mirror a change into #7 only when it is a spec amendment or the maintainer asks for it.
- **A full wrap-up request comes first.** When the maintainer asks for one, report new findings with a recommendation and ask whether to fix first or commit first. Never silently turn a wrap-up into fix rounds. Mechanical gate blockers (a format violation, a one-line test isolation fix) may be fixed directly.
- **One session per phase.** Start the next phase in a fresh session from `progress/current.md`.
- **Briefs name their inputs** (exact paths, what is already verified) and their bounds. Route mechanical test edits to `test_maintainer` (haiku) and long noisy runs to `suite_runner` (haiku).

## Mandatory role: leader

You always act as the `leader` defined in `.claude/agents/leader.md`: you decompose and coordinate; you do not implement.

- ❌ Do not edit `src/`, `tests/` or `apps/web/`. Launch `implementer`, or `test_maintainer` for mechanical test edits.
- ❌ Do not mark features `done`. The `reviewer` does, or, for light changes, the leader after reading the diff and running the tests, with that recorded.
- ❌ Do not skip the spec phase for `"sdd": true` features, or the human gate between `spec_ready` and `in_progress`.
- ✅ You may edit docs, compose, `infra/`, `progress/`, `n8n/`, `scripts/` and root config yourself.
- **Subagents write results to files** (`progress/impl_<feature>.md`, `progress/review_<feature>.md`) and return only a short summary. Never relay their prose.

### Briefs and recommendations

- **Did I produce this fact with a command in this session?** If yes, state it. If not, phrase it as a question for the subagent. Never supply the answer to a question a rule says must be researched (no "almost certainly X").
- For full-group work, run `premise_checker` over the brief before dispatching. For a recommendation the maintainer will act on (for example "#8 already complies"), verify it with a command first. An unchecked premise created backlog id 103.
- **Never forbid in a brief what the approved `tasks.md` mandates.** Derive the scope bounds from the task list.
- **Name the unit** (arm, case, row, site, ordering) in every acceptance criterion. A sample is never the population; give the command that counts the population.
- **Never hand the maintainer a question you could close.** First check what #7 did (its checkout is on disk) and whether `specs/shared/` really prescribes what you would amend. Go to the gate with a recommendation and evidence, never a menu.

### `feature_list.json`

- **Single writer.** Never run two agents that may both write it.
- **Edit only the line you mean to change.** If you re-serialise, use `json.dumps(..., indent=2, ensure_ascii=False)`, then read `git diff` before moving on.
- **No agent may run a git command that writes the index or working tree (`stash`, `reset`, `restore`, `clean`, `checkout`).** Use `git show HEAD:<path>` to read committed content. To undo an edit, re-edit.
- `init.sh` cannot detect a lost transition: a wrong state can still be a valid state.
- **Findings get a disposition:** fix, accept with evidence (`done` plus "ACCEPTED, NOT FIXED" and a re-open trigger), or re-open only if X. A finding rooted in `specs/shared/` becomes an `SA-n` proposal or a backlog entry, never a sentence in a review.
- **Audit-style work needs a stopping rule written before it starts.**

### This file is a cache

Before quoting or enforcing a rule, `grep` this file on disk; it is amended at human gates.

### Porting from #7

- **The ported-idiom ledger.** Every port carries one line per idiom: *"#7 relied on X; in #8 that property is supplied by Y"*, with a named, armed guard where #8 must hand-build the property.
  - The ledger lives in `design.md` for `sdd: true` features and in `progress/impl_<feature>.md` for `sdd: false` ones.
  - The "#7 relied on X" half is read from #7's checkout, with a file and line. "None owed" needs the same citation.
  - A two-party engine claim is probed both ways, or the row states which way was probed.
  - Check both halves: does the named guard execute the code the row is about?
- **Port the guards too.** Enumerate #7's tests for the mechanism by content, and classify each assertion as ported, deliberately not ported (with the reason), or not applicable.
- **Defect classes.** When the work targets a class, enumerate it repository-wide first, as a search result.

## Architecture conventions

- **Clean Architecture per service, as folders in one `.csproj`:** `Presentation/` (Minimal API, NATS responders, Kafka consumers), `Application/` (hand-rolled handlers, saga, ports), `Domain/` (zero framework references), `Infrastructure/` (EF Core, Mongo, Kafka, NATS, outbox, MailKit, OTel). Dependencies point inwards.
- **Domain purity:** no EF Core, Kafka, NATS, Mongo, ASP.NET Core or `System.Text.Json` in `Domain/`. NetArchTest enforces it.
- **The hand-rolled dispatcher is binding in all six services:** `ICommandHandler<T>`, `IQueryHandler<T,R>` and `IEventHandler<T>`, registered by assembly scan. Startup validation fails on a missing or duplicate handler. No MediatR. The outbox and `saga_commands` are the durability guarantee.
- **Explicit DI registration**, with loud failure at boot.
- **One `BackgroundService` per transport.**
- **Database per service:** no cross-database joins or foreign keys; business identifiers only.
- **The only shared runtime code** is `src/SharedKernel` (zero package references; pure functions such as `MoneyText` and `CurrencyExponent` are allowed), `src/Contracts` (wire types) and `src/Cqrs` (the Application-layer dispatcher; `Domain/` may not reference it).
- **JSON wire shape:** the envelope is byte-exact with #7 (field order as `asyncapi.yaml` declares it); the payload is semantically equal. camelCase, nulls omitted, no `$type`, one shared `JsonSerializerOptions` in `Contracts`.
- **Kafka carries facts, NATS carries RPC.** Every interaction must map to a row of the decision matrix in `specs/shared/`.

## Coding conventions

| Topic | Rule |
|---|---|
| Language | C# 14 / `net10.0`, nullable and implicit usings on, async throughout |
| Money | `long` minor units in the domain **and** in the `bigint` column; never float; `decimal` only at presentation boundaries; no narrowing casts. Human text uses `MoneyText` (ISO 4217 exponent, per SA-5) |
| Identifiers | UUID keys generated in the domain (`UniqueId`) |
| Columns | `snake_case` in MS-SQL, `PascalCase` in C# |
| Dates | UTC, `datetime2(3)`, ISO-8601 on the wire |
| References | `ORD-000001`, `DES-…`, `INV-…`, `CR-…`: sequential, allocated under a row lock |
| Event types | `<aggregate>.<fact>.v<n>` |
| Naming | file = type name; `_camelCase` private fields; `IPascalCase` interfaces (`.editorconfig`, checked by `dotnet format`) |
| Errors | domain errors extend `DomainError` with a stable `Code` |
| Logging | structured, with `correlationId` on every line |
| Async | CS1998, CS4014, CA2016 and CA2213 are errors; forward every `CancellationToken` |
| Markdown | no hard line-wraps in prose |

## Testing conventions

- **Runners:** xUnit for the backend, Vitest (plus React Testing Library) for web, Playwright for end-to-end. No Jest.
- **Domain tests are pure.** Integration tests use Testcontainers, never mocked brokers. **They must pass with the developer infrastructure down** (id 104 hid behind a developer Kafka on 9092).
- **API tests** are black-box through the Gateway and prove the same script as #7's.
- **Architecture tests** run in the normal `dotnet test` pass.
- **Coverage gates:** ≥80% domain, ≥60% overall, verified to fail when breached.
- **Every `R<n>`** maps to a named test in `specs/shared/test-matrix.md`.
- **Arming protocol:**
  1. `cp` a backup, introduce the violation, and run the ONE named test.
  2. Record the failure verbatim; its message must name the claim.
  3. Restore from the backup (never `git checkout`), confirm with `cmp`, force the rebuild (`--no-incremental` or `touch`), and re-run green.
  - Do not offer `git diff` on an untracked file as proof.
- **A countable claim** (a count, identity, ordering or absence) is a guard, and a guard is not done until it has been seen to fail, flagged or not. Arms go stale when a later change alters the path; re-run them, do not re-read them.
- **Mutation families:**
  - delete the behaviour;
  - corrupt a field the test supplied;
  - **substitute a valid sibling identifier** (`MSSQL_DB_*`, subjects, topics). A substitution's failure must name the intended break.
- **Every branch that emits or suppresses a fact** has a test that fails when the emission is deleted.
- **The defeat list.** For full-group work, run it against your own guard before submitting, and state which rows apply:
  1. delete the behaviour;
  2. corrupt a supplied field;
  3. substitute a sibling identifier;
  4. shadow the pattern in a comment or string;
  5. hide it in a dead region (`#if`);
  6. hide it in a raw or verbatim string;
  7. drop an optional element;
  8. compare a literal to a literal;
  9. satisfy the closer half and leave the premise stale;
  10. let build output join the population;
  11. write it in a form the instrument doesn't recognise (**when a syntax guard keeps losing, test the behaviour, or compare what the compiler built**);
  12. serve the failure through a path the population never drives.
- **Changing an instrument** swaps its premises. List the new instrument's assumptions and arm them in the same round.
- **A negative claim is a search result:** the command, its full output, and one classification per hit.
  - Never filter the population by the property under test; make the expected set a literal and derive the rest by subtraction.
  - Classify the unit the claim is about.
  - When retiring a claim, enumerate on the retired wording.
  - Exclude paths at the source, not by filtering `grep` output.
- **Retry loops:** pace them explicitly, and prove the pacing with a change of kind, not of probability.
- **Builds:**
  - Never run two builds or test runs against the same projects at once.
  - While your background build is alive, do read-only work only.
  - Wait on a PID, never on `pgrep -f`.
  - If a failure names a line the source cannot explain, clear `bin/` and `obj/` first.

## Commit discipline

> **Claude never runs `git commit` or `git push`**, except when the maintainer says **"full wrap-up"**. That authorises, in this order:
> 1. commit, one commit per feature and a spec amendment on its own;
> 2. push;
> 3. update `README.md`, `docs/PROCESS.md`, the three external documents (Plan, Solution Documents, Stack Comparison), and both DotNet quizzes (`embed-doc-in-quiz.py`);
> 4. brief the next phase in `progress/current.md`.
>
> Never ask whether to commit.

- **Stack comparison:** mark only what a committed file proves as confirmed; everything else is "pre-resolved", naming the phase that will confirm it.
- **Counts in subjects:** a completeness word or bare number in a subject needs a `counted:` line or command in the body; `scripts/git-hooks/commit-msg` enforces this. "ISO 4217" counts as a bare number. A number that does not reconcile with the last run is a finding, not a footnote.
- **Message format:** `type(scope): subject`, then `What:` and a `Packages installed:` list for every package added in that commit.

## Environment notes

- The .NET SDK is pinned in `global.json`. Node comes from `.nvmrc`, and pnpm through corepack; both serve `apps/web` only, plus the root `package.json` command shortcuts.
- Analyzer severities are set in `.editorconfig`; enforcement is set in `Directory.Build.props`.
- `dotnet-ef` must match the EF Core version band.
- The git remote is account-explicit: `https://peelmicro@github.com/...`.
- MS-SQL needs about 2 GB RAM and about 30 s to start.
- **The maintainer's `~/.docker/config.json`** names a missing `docker-credential-desktop`. For image builds, use a scratch `DOCKER_CONFIG` containing `{}` plus a symlink to `~/.docker/cli-plugins`. Never edit the maintainer's file.
- **Port 3000 is often taken on this machine**, so the web app defaults to `WEB_PORT=3010` (#7 too).
