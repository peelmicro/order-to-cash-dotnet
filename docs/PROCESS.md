# How this project is built — the process guide

> **What this is:** the complete explanation of the development process used in this repository — the concepts, the cast of agents, the workflow, and a registry of every process artifact. If you cloned this repo and want to understand *how* it was built (or replicate the pattern), start here.
>
> **Maintenance rule:** this document is updated at the end of every phase — the artifact registry (§9) and "Where the project is right now" (§10) must always reflect reality. A stale process guide is a defect.
>
> **About this repository specifically:** this is assessment **#8** of a three-stack trilogy. The process described here was *built* in [#7 (`order-to-cash-nestjs`)](https://github.com/peelmicro/order-to-cash-nestjs) and is **reused here rather than reinvented** — the specification, the harness, the agent definitions and the demo workflows were copied across. That makes this repository the trilogy's first measurement of what a mature spec plus a mature harness is actually worth on a re-implementation, which is why §10 tracks effort against a baseline and why the entries in §11 are deliberately sparse rather than inherited.

---

## 1. The premise

This repository is built almost entirely by AI agents (Claude Code), with a human making the judgment calls, testing every phase, and owning every commit. **The development process is itself a deliverable**: the assessment behind this project scores not only the software but whether the process artifacts show real use and whether a stranger could replicate the pattern.

For this repository there is a second question on top: **does reusing a mature process actually help?** The honest answer requires recording effort per feature against #7's numbers — including the features where reuse saved nothing — which is what `progress/history.md` exists for here.

The process combines two ideas that are often confused. They stack — the harness is the foundation, SDD sits on top.

---

## 2. Layer 1 — The harness

### The problem it solves

An AI agent has two structural weaknesses: **it has no memory between sessions**, and **it will do the wrong thing very fast and very confidently**. Left alone, agent-built projects rot in predictable ways — three features each 70% finished, tests that assert nothing, state that silently contradicts itself, a README describing software that does not exist.

The harness is a set of plain files that give the agent an external brain and a set of rails. None of it is magic; all of it is discipline made mechanical.

### The parts, and why each exists

**External memory** (`progress/`). Between sessions the agent remembers nothing, so everything worth remembering is written to disk *while working, not at the end*: what is in flight (`current.md`), what was finished and what it cost (`history.md`), and each agent's own report of what it did (`impl_*.md`, `review_*.md`, `spec_*.md`). A new session reads these and continues as if it had never stopped. This was proven mid-build: a session died between a review rejection and the fix; the next session resumed exactly where the loop stopped, from the files alone.

**A backlog with a state machine** (`feature_list.json`). Work is decomposed into features, each with a status:

```
pending → spec_ready → in_progress → in_review → done
                                          ↓
                                       blocked
```

Two rules carry most of the value. **Max one feature `in_progress`** — because parallel half-finished work is how agent projects rot; one-at-a-time makes "finished" mean something. And **only the reviewer sets `done`** — the agent that wrote the code never gets to declare it correct.

**A circuit breaker** (`init.sh`). Run at the start of every session. It checks that the environment is sane (the pinned .NET SDK resolves, Node and pnpm for the web app, Docker), the harness files exist, every agent declares its model, the backlog parses and obeys its own rules, and — crucially — that any spec-required feature past `pending` actually has its spec on disk. **If it exits non-zero, the session must not advance.** Its checks were adversarially verified: the state was deliberately broken four different ways and each was caught. A check that has never been seen failing is a convention, not a gate.

**Conventions that are enforced, not requested** (`CLAUDE.md` + tooling). The rules that matter are backed by machinery: domain purity is an ESLint rule that fails the build, not a paragraph; money as integer minor units is a value object that throws, not a guideline; "no Jest" is greppable. When an agent violates house style, the fix is usually to make the rule more explicit or more mechanical — not to correct the agent by hand and hope.

**Objective completion criteria** (`CHECKPOINTS.md`). "Am I done?" is a feeling; the checkpoints are yes/no questions a reviewer walks: harness complete? state coherent? architecture respected? verification real? session closed cleanly? SDD followed? artifacts reusable? A session does not close with an applicable box unchecked.

**Specialised agents** (`.claude/agents/`) — see §3.

### What the harness is *not*

It is not specific to this project, this stack, or even to SDD. The harness layer alone is worth adopting in any AI-assisted repository. It is also not tooling-heavy: every artifact is markdown, JSON or bash, readable in an editor, diffable in git.

---

## 3. Layer 2 — SDD (Spec-Driven Development)

### The problem it solves

For large features, the expensive mistakes are made **before any code is written** — a wrong invariant, a missing compensation path, an ambiguous contract between services. Code review catches coding mistakes; nothing catches *specification* mistakes unless the specification exists as an artifact someone can review.

SDD inverts the usual order: write the specification first, in a notation precise enough to be testable, get a human to approve it, and only then implement. The spec — not the code — is the source of truth. When code and spec disagree, the code is wrong (or the spec gets amended *first*, visibly).

### How it works here

- **`specs/shared/`** holds the system-wide specification, written in Phase 3 before any application code: the domain model and its invariants, the saga with both compensation paths, 63 EARS requirements, an AsyncAPI document (every event and RPC message), an OpenAPI document (the REST contract), a test matrix mapping every requirement to the test that proves it, and the functional spec of the demo workflows. It is deliberately **stack-agnostic** because two sibling assessments (#8 .NET, #9 FastAPI) reuse it verbatim.
- **`specs/<feature>/`** (from Phase 8 onward) holds a per-feature triple-doc for the 8 *large* features only — `requirements.md` (EARS), `design.md` (the stack-specific how), `tasks.md` (an ordered checklist the implementer ticks). These features carry `"sdd": true` in the backlog.
- **The human approval gate**: a spec-required feature stops at `spec_ready` until the human has reviewed the spec's *decisions* (see §6) and approved. No code before approval — and the git history proves the ordering, because the spec commit precedes the implementation commit.

### The honesty clause

SDD costs real ceremony, and for a 50-line feature the ceremony is decorative paperwork. That is why only 8 of this project's 42 features carry `"sdd": true` — the aggregates and state machines, the saga and its compensation, the outbox and idempotency, the read-model projection, and the observability wiring. Everything else skips the triple-doc but still travels the backlog state machine. The spec-becomes-infrastructure moments (Kafka topics derived from the AsyncAPI file, TypeScript types generated from both API documents) are where the spec pays for itself even on small features.

---

## 4. The cast — who does what

Six roles: five agents defined in `.claude/agents/`, plus the human. Each agent definition declares which Claude model it runs on (or documents that it deliberately inherits the session's model) and which tools it may use — both are design decisions, not defaults.

| Role | Model | Tools (the deliberate part) | Job |
|---|---|---|---|
| **The human** | — | everything, including the only `git commit` | Approves specs, adjudicates judgment calls, tests every phase, owns the git history |
| `leader` | unpinned — inherits the session model | has the **Agent** tool; never edits `apps/` or `packages/` | Decomposes work, launches the other agents, maintains the backlog and session state, stops at every human gate |
| `spec_author` | unpinned | Read/Write, **no code execution focus** | Writes `specs/` — EARS requirements, designs, task lists. Never writes application code or tests |
| `implementer` | `sonnet` | full edit + bash | Implements **one** feature against its approved spec, writes its tests, self-verifies |
| `reviewer` | unpinned | **read-only — no Write, no Edit** | Approves or rejects the implementer's work; the only role that sets `done` |
| `test_maintainer` | `haiku` | edit but **no bash** | Mechanical test updates after landed changes — retitles, flips assertions, fixes flaky timeouts. Never touches source |
| `suite_runner` | `haiku` | Bash, Read — **no edit** | Runs one long, noisy command and returns exit code, counts and verbatim failure blocks. Interprets nothing — which is why delegating to it does not weaken the do-not-trust-reports rule |

### The reasoning behind the model pinning

- `leader`, `spec_author`, `reviewer` are unpinned so they get the strongest available tier: decomposition, specification and adversarial review are the highest-judgment work, and the spec is inherited by two more assessments.
- `implementer` runs on a mid-tier model *because the thinking has already been done* — the spec or the acceptance list is the decision; implementation is faithful execution, and it happens ~30 times across the build.
- `test_maintainer` runs on the cheapest tier because its work is bounded and pattern-following by construction.

### Two design choices that are easy to miss

**The reviewer cannot write.** It has no Edit/Write tool *by design*. A reviewer that can fix what it finds becomes a second implementer — and nobody reviews the reviewer. Its only outputs are a verdict file and status changes. This has teeth: in this build the reviewer has rejected features the implementer reported as fully verified, by probing the running system and finding the report wrong (see `progress/review_infra_compose.md` for the clearest example — a data-loss bug behind a confident "verified" claim).

**Agents write to files, not to chat** (the anti-telephone-game rule). A subagent's deliverable is a file (`specs/<feature>/`, `progress/impl_*.md`); what returns to the leader is only a reference. Every hop through a chat summary loses detail; a file does not degrade, survives the session, and becomes the audit trail the process is scored on.

---

## 5. The loop — a feature's life, concretely

What actually happens when a large (`"sdd": true`) feature is built:

```
 1. leader: ./init.sh green? read current.md + feature_list.json
    │
 2. leader launches spec_author
    │    writes specs/<feature>/{requirements,design,tasks}.md
    │    sets status: spec_ready
    │    returns only: "spec_ready → specs/<feature>/"
    │
 3. ⏸ HUMAN GATE — the human reviews the spec's DECISIONS (§6) and
    │  approves or asks for changes. Nothing proceeds without this.
    │
 4. leader sets in_progress, launches implementer
    │    implements from the spec (not from its own idea of the feature)
    │    writes the tests INSIDE the feature — green before handover
    │    writes progress/impl_<feature>.md
    │    sets status: in_review
    │
 5. leader launches reviewer
    │    probes the running system — never trusts the report
    │    walks CHECKPOINTS.md, verifies requirement→test traceability
    │    writes progress/review_<feature>.md
    │    APPROVED → done + effort record in history.md
    │    REJECTED → in_progress, back to step 4 with a precise defect list
    │
 6. ⏸ HUMAN GATE — the leader reports what was done and how to test it
    │  manually. The human tests. Only then is the phase closed and committed.
```

Small features (`"sdd": false`) skip steps 2–3 and implement directly from their acceptance list — but never skip the review or either human gate.

The rejection path is not theoretical. As of Phase 6, the reviewer has rejected 2 of 8 reviewed features on first pass, with defects the implementer's own verification missed (a Kafka volume mounted where the broker never writes; a healthcheck reporting healthy 90 seconds early; verification logic that passed silently on drift). The loop's value *is* those catches.

---

## 6. EARS — the requirements notation

EARS (Easy Approach to Requirements Syntax) constrains every requirement to one of five shapes, which makes vagueness structurally difficult:

| Pattern | Shape | Used for |
|---|---|---|
| Ubiquitous | THE SYSTEM SHALL … | invariants, always true |
| Event-driven | WHEN ‹trigger›, THE SYSTEM SHALL … | reactions to facts/commands |
| State-driven | WHILE ‹state›, THE SYSTEM SHALL … | behaviour during a condition |
| Unwanted | IF ‹condition›, THEN THE SYSTEM SHALL … | error and edge cases |
| Optional | WHERE ‹feature present›, THE SYSTEM SHALL … | configuration-dependent behaviour |

A real one from this project's spec:

> **R27.** WHEN a `credit.rejected.v1` fact is received for an order in status `stock_reserved`, THE SYSTEM SHALL issue a stock release command, and SHALL NOT set the order to `cancelled` until `stock.released.v1` has been observed.

What makes it good: a named trigger, a named precondition, an explicit prohibition with ordering — every clause is something a test can fail on. Contrast: *"the system shall handle credit rejection gracefully"* — nothing can fail that; it is a wish, not a requirement.

Every requirement carries a stable id (`R1`…`R61`), and `specs/shared/test-matrix.md` maps each id to the named test that proves it. A feature is not `done` while its matrix rows are red or missing. This is the traceability chain: requirement → test → green.

---

## 7. What "reviewing a spec" actually means

The most misunderstood human task in the whole process, so it gets its own section.

When an agent writes a specification from a task document, it repeatedly hits places where the source is **ambiguous**, and it must *decide*. Those decisions then bind every downstream phase. Reviewing the spec means **reviewing those decisions — not proof-reading the prose**. If the decisions are right, the prose follows.

The mechanism: the spec author records every ambiguity it resolved in a table (what was unclear → what was decided → why → where it is recorded). In this project, Phase 3's spec pass surfaced **13 such decisions** (see `progress/spec_shared_passA.md` §4) — which fact drives `paid` vs `completed`, whether compensation releases stock before or after cancelling, whether an RPC reply may ever advance the saga (it may not — only facts do). The human read a 13-row table, not 7,500 lines, and pushed back where it mattered.

A useful instinct for the human: pay most attention to decisions that **add** something the source document never mentioned — that is where an agent has invented policy. (Here: what happens when an operator cancels an order after stock is reserved and credit is held. The task document was silent; the spec author designed the unwind rule; the human approved it knowingly.)

---

## 8. The rhythm of a phase, and common confusions

### The rhythm

Every phase runs the same shape:

1. `./init.sh` — refuse to start from a broken state.
2. Do the work through the loop (§5), one feature at a time.
3. **Stop.** The leader reports *what was done* and *how to test it manually* — exact commands, expected output.
4. The human runs the commands and verifies.
5. The human authorises the close. Only then:
6. **The phase-close ritual**: commit (one commit per phase/feature, message naming every package installed and why) → update the private build-plan document → refresh `README.md` → update this document (§9 registry + §10 status) → brief the next phase.

The agents never run `git commit` or `git push` of their own accord. The commit history is reviewed process evidence; every commit is something the human personally verified. That is also why the history reads spec-first: the ordering is the proof.

### Common confusions

**"Why is there a spec *and* a plan?"** The plan (kept outside this repository) is the build order — phases, sequencing, decisions log. The spec (`specs/shared/`) is the system's definition — what the software must do, independent of schedule. The plan changes as the build learns; the spec changes only when requirements change, and visibly.

**"Why can't the agent just commit?"** Because a commit is a claim that something works, and only the person who tested it can make that claim — on a public portfolio repository, under their own name.

**"Why max one feature in progress?"** An agent will cheerfully leave three features 70% done. One at a time makes "finished" meaningful and keeps the effort records honest.

**"Does the human read everything the agents produce?"** No. The human reads the *decision tables* and the *verdicts*, spot-tests the system, and trusts the adversarial loop for the rest. The full artifacts exist for when they are needed — and for the assessor.

**"What happens when an agent is wrong?"** The reviewer rejects with a precise defect list and the loop repeats. If the same class of mistake recurs, the fix goes into `CLAUDE.md` or the agent's own definition — the process is corrected, not just the instance.

**"Is any of this specific to Claude?"** The file formats assume Claude Code's subagent mechanism (`.claude/agents/`), but the pattern — external memory, backlog state machine, circuit breaker, spec gate, adversarial review, human commit gate — is tool-agnostic.

---

## 9. The artifact registry

Every process artifact in this repository: what it is for, and where it came from. The **Origin** column is specific to this assessment — it records whether an artifact was copied from #7, copied and re-pointed, or genuinely written here, because that distinction is the measurement. ("Updated" means meaningful content change, not status ticks.)

| Artifact | The problem it solves | Useful to know | Origin | Created | Last updated |
|---|---|---|---|---|---|
| `AGENTS.md` | "Where does an agent start?" — the entry map | Read order, hard rules, the SDD flow, session-close procedure | Copied from #7, re-pointed (4 edits) | Phase 2 | Phase 2 |
| `CLAUDE.md` | "How do we do things here?" — binding conventions | Leader role, architecture non-negotiables, coding/testing conventions, commit discipline | Copied from #7, **substantially adapted** — three rules translated, one added | Phase 2 | Phase 2 |
| `feature_list.json` | "What is happening right now?" — the backlog state machine | 42 features, 8 `sdd: true`. Max one `in_progress`, enforced by `init.sh`. Only the reviewer sets `done` | #7's ids, names and phases **reset to `pending`**; one new feature (`cqrs_dispatcher`) | Phase 2 | every feature transition |
| `init.sh` | "Is the world sane?" — the session circuit breaker | Exit ≠ 0 ⇒ do not advance. Checks env, harness files, agent model declarations, backlog and SDD coherence | Copied from #7; environment section rewritten for the .NET SDK, backlog validator kept as-is | Phase 2 | Phase 2 |
| `CHECKPOINTS.md` | "Am I actually done?" — objective close criteria | C1–C7; the reviewer walks them | Copied from #7; **C7 inverted** — from "is this reusable?" to "did it actually reuse it, and is the benchmark honest?" | Phase 2 | Phase 2 |
| `.claude/agents/*.md` (×6) | Role separation with different powers and cost tiers | Each declares model + tools; reviewer deliberately read-only; test_maintainer deliberately bash-less | Copied from #7 with **model pinning unchanged**; `spec_author` gained a section on how reuse changes its job | Phase 2 | Phase 2 |
| `progress/current.md` | Working memory of the active session | Updated at every status transition, in lockstep with the backlog — the reviewer checks this (C2) | Template from #7, content fresh | Phase 2 | every session |
| `progress/history.md` | Append-only log + **per-feature effort records** | Carries a `#7 baseline` field per entry; this is the benchmark | Template from #7, **deliberately empty** — #8's numbers must be #8's | Phase 2 | every feature close |
| `progress/impl_*.md` | The implementer's own report per feature | What was built, evidence, deviations, what the review later caught | Written here | — | every feature |
| `progress/review_*.md` | The reviewer's verdict per feature | Probes with real output, defects with file/line/why, CHECKPOINTS walk | Written here | — | every feature |
| `specs/shared/` (7 files) | The system's definition, before the code | **Read-only.** A change is a spec amendment: explicit, human-gated, applied to every repository in the same session | **Copied verbatim from #7** — six of seven files byte-identical, `cmp`-proven | Phase 3 | Phase 3 (`SA-1`) |
| `specs/shared/test-matrix.md` | Requirement → test traceability | Columns 1–4 are #7's; only the Status column is this assessment's | Copied; reset by #7's own four-step recipe — 63 rows to `TODO`, columns 1–4 confirmed identical | Phase 3 | as features land |
| `specs/<feature>/` | Per-feature triple-doc for the large features | `requirements.md` mostly cites #7's `R<n>` ids; `design.md` is where nearly all the new work is | Written here | Phase 8 | per feature |
| `docs/PROCESS.md` | This document | Updated at the end of every phase — registry + status | Copied from #7, re-pointed; §11 reset | Phase 2 | Phase 2 |
| `README.md` | Honest front door at every commit | Grows incrementally each phase; never describes software that does not exist yet | Written here | Phase 1 | every phase |
| `global.json` / `.editorconfig` | Toolchain and code-style pins | SDK pinned with `rollForward: latestPatch`; analyzer **severities** live in `.editorconfig`, **enforcement** in `Directory.Build.props` | Written here | Phase 1 | Phase 1 |
| `n8n/workflows/*.json` | The demo's "external world" | Gateway REST API only — which is why they port at all | **Reused unchanged** from #7, byte-identical (base URL env only) | Phase 3 | never |
| `infra/` (OTel, Prometheus, Grafana, Kafka topics, n8n import) | The stack-agnostic infrastructure | Nine files, `cmp`-verified byte-identical. The collector speaks OTLP and the topic script reads the shared spec — neither has anything stack-specific in it | **Reused from #7 unchanged** | Phase 4 | Phase 4 |
| `infra/mssql/` | Database bootstrap | The engine's image has no `/docker-entrypoint-initdb.d` and no init hook of any kind, so the entrypoint starts it, waits for it to answer, bootstraps, then hands the foreground back. The largest genuinely new piece of infrastructure in this assessment | **Written here** | Phase 4 | Phase 4 |
| `docker-compose.infra.yml` + `.env.example` | The runnable infrastructure | 15 services under its own Compose project namespace, so its containers and volumes cannot collide with the previous assessment's | Adapted from #7 | Phase 4 | Phase 4 |

---

## 10. Where the project is right now

> Maintained at the end of every phase. History of *how* each phase went lives in `progress/history.md`; this is only the current position.

**Position: Phase 6 complete — 11 of 42 features done.** Four write schemas exist, with their migrations, and sixty integration tests that interrogate a real database engine rather than the object-relational mapper's opinion of itself.

Phase 6 produced the clearest benchmark result so far, and it is not the expected one. The phase cost **more** than the assessment it is reusing from — marginally, but on a phase where the entire schema arrived in a document, with the previous assessment's committed SQL available for corroboration. One feature was rejected for shipping seven of eight foreign keys, undisclosed and untested; the two features that followed were briefed on that failure and came in clean, one of them more than twice as fast as its predecessor. Separating those two effects is the finding:

> **The specification reused; the verification did not.** Copying a specification tells the next implementation *what to build*. Only a review tells it *how to prove it built that*. The first is free and transfers between runs; the second is a cost each run pays again.

A second, less comfortable pattern sits underneath it: the reuse dividend was largest where the baseline was still learning, and fell to zero where the baseline had already learned. Reuse does not compound — it front-loads.

| Phase | What | State |
|---|---|---|
| 1 | Environment & repository — SDK/Node pins verified adversarially, account-explicit remote, `.gitignore` proven not to swallow source | ✅ |
| 2 | Harness layer — copied from #7 and re-pointed to .NET; backlog reset; C7 inverted | ✅ |
| 3 | Shared specification — copied **verbatim** from #7, `cmp`-proven per file; zero stack leaks found; `SA-1` raised and applied to both repositories | ✅ |
| 4 | Infrastructure compose (15 services, 36 s cold to all-healthy) + spec-derived Kafka topology. The database engine's bootstrap had to be written from scratch — its image has no initialisation hook at all | ✅ |
| 5 | Solution scaffold, SharedKernel, Contracts, NetArchTest architecture tests — 65 tests, twelve armed architecture rules, and a wire-parity oracle of twelve real messages captured from the previous assessment's own broker | ✅ |
| 6 | EF Core models + migrations for the four write databases — 20 tables, 60 integration tests against a real database engine, and a parity test asserting the reliability tables are identical across all four schemas | ✅ |
| 7 | Deterministic seed job, checksum-compared against #7's dataset | ⬜ |
| 8 | Orders service — aggregate, hand-rolled dispatcher, outbox/idempotency, acceptance, saga orchestrator | ⬜ |
| 9 | Fulfillment — stock reservations and DESADV creation | ⬜ |
| 10 | Billing — buyer credit, the `.99` simulator, invoicing, remittance intake | ⬜ |
| 11 | Notifications — MailKit into Mailpit, durable idempotency ledger | ⬜ |
| 12 | Projector — the MongoDB read model | ⬜ |
| 13 | Gateway / BFF — REST, JWT, login rate limiting, SSE | ⬜ |
| 14 | Reliability + observability — retry, DLQ, OTel propagation, health checks | ⬜ |
| 15 | End-to-end saga verification | ⬜ |
| 16 | Web app (Next.js App Router) | ⬜ |
| 17 | Web component tests (Vitest + React Testing Library) | ⬜ |
| 18 | API tests — the same black-box script #7's prove | ⬜ |
| 19 | Playwright end-to-end tests | ⬜ |
| 20 | n8n demo workflows, reused unchanged | ⬜ |
| 21 | Quality gates — analyzers, format, coverage proven to bite | ⬜ |
| 22 | Prometheus, Grafana, Jaeger verification | ⬜ |
| 23 | Full Docker Compose | ⬜ |
| 24 | Documentation, demo, and the **#7 vs #8 benchmark** | ⬜ |
| 25 | Final checkpoint | ⬜ |

---

## 11. What this process actually caught

> In #7 this section grew into a long catalogue of real findings — guards that guarded nothing, claims that did not survive being checked, defects only the real system could reveal. **That catalogue is #7's, and it stays in #7.** Reprinting it here would be claiming another build's evidence, which is the precise failure this section exists to guard against.
>
> This section fills up as #8 accumulates its own. It is expected to be shorter, and if it is *not* shorter, that is a finding in itself: it would mean the inherited specification and harness prevented less than they were supposed to.

### 11.1 Inherited as prevention, not rediscovered

The interesting category for a reuse run. Each of these cost #7 real debugging time; here they cost a line of configuration, because they were written down.

| What | Cost in #7 | Cost in #8 |
|---|---|---|
| Two GitHub accounts on one machine, `credential.helper=store` serving the wrong token for a `peelmicro` repo | A failed first push (HTTP 403) and the investigation behind it | One `git remote set-url` before the first push, which then succeeded first time |
| A bare `data/` in `.gitignore` silently matching a source directory | 11 source files untracked, undetected until Phase 8 | `/data/` anchored from the start, and `git check-ignore` run in both directions to prove it |

### 11.2 Found here

**Phase 3 — an instruction invalidated by its own execution (`SA-1`).** The shared specification carried a four-step recipe telling a new assessment how to reset the traceability matrix. Step 4 named the prose to delete by listing the specific paragraphs present *in that copy*. Following the recipe therefore made the recipe false: the executed copy contained an inventory of content it no longer had, and the next assessment inheriting it could not tell a completed step from a missed one.

Two things make it worth keeping beyond the fix itself. First, the **previous assessment's final audit could not have found it** — that audit searched for stack-specific vocabulary, and this is a self-reference defect with no stack terms in it at all. Second, it was found by *doing* rather than by reading: the instruction had been reviewed carefully and was correct every time anyone read it. It only became wrong at the moment it was obeyed, which no amount of re-reading would have surfaced.

The generalisable form: **an instruction that describes its own current contents will go stale the first time it is followed.** Write the class, not the instance.

**Phase 4 — a verification command that could not fail.** Reporting that the message broker ran in its cut-down, correct mode, the agent offered a one-line command as proof: grep the broker's status page for the feature's name and expect zero hits. The status page names that feature whether it is on or off, so the command returned the same result either way. The claim was true — three later checks, including asking the feature to actually do something and being refused, confirmed it — but the evidence offered for it was worthless, and it was annotated with its expected output as though it were an assertion.

It was caught by the human reading the report, not by the agent that wrote it.

**Phase 7 — a reviewer enforcing a rule that had already been changed.** A review rejected a detail of the work for breaching a convention, quoting the conventions file to justify it. The quotation was real; the rule was not current. The project amends its own conventions at human gates as it goes — this particular one had been changed two phases earlier, at a gate, with the reasoning recorded inline — and the reviewer had quoted the copy injected into its context when its session began rather than the file on disk.

Its own diagnosis was the valuable part, and it went further than the correction required: an injected snapshot is a **cache**, a project that deliberately amends its conventions mid-flight *guarantees* that cache goes stale, and so the rule has to be to read the file rather than the copy. It also named the shape: this is the guard-that-does-not-guard **inverted** — not a check that fires on nothing, but a check that fires correctly on something that is no longer true. Here it cost one spurious advisory against correct code. On a rule with teeth it would have cost a spurious rejection and a round of pointless rework.

The generalisable form: **a convention you did not just read is a convention you are guessing at.** Currency of a rule is part of the rule.

**Phase 5 — a parity target that was measuring the wrong thing.** A rule said the new implementation's message format must match the previous one *byte for byte*. Going to fetch the previous assessment's real bytes — rather than reasoning about what they should be — showed the target was wrong twice over. The messages stored in its outbox table were not what went on the wire, because the database's JSON column type reorders keys; and the messages that *did* go on the wire carried that same reordering, because the relay read them back out of the column before publishing. A storage artifact had become part of the "contract".

Holding the rule literally would have meant emulating one database engine's key ordering in a different engine, permanently, in a project whose entire premise is that the *specification* is what ports — and the third assessment, on a third engine, could not have complied at all.

The rule now splits: the envelope is byte-exact (its field order comes from the shared contract, and twelve captured real messages prove the match), and the payload is asserted semantically — same keys, values, types and casing, order unasserted, because JSON key order carries no meaning and no consumer reads it.

The generalisable form: **before enforcing a parity claim, go and look at what you are claiming parity with.** A target adopted from a document rather than from the artefact can encode an accident as a requirement, and the stricter it sounds the less likely anyone is to question it.

**Phase 5 — a check run against the wrong artefact.** Proving a guard works means breaking the thing it guards and watching it fail, then restoring and watching it pass. During one such run the restore preserved the backup file's timestamp; the build system's incremental check saw the source as older than its compiled output, skipped the rebuild, and the confirming run executed the *previously broken* binary. Here it produced a false failure, which is harmless and is how it was noticed. The same mechanism produces a false *pass*: restore a file, fail to actually apply it, and a stale-but-correct binary vouches for source that is still broken.

The fix is one line in the protocol — force a rebuild after restoring, before the confirming run — and it is now a rule rather than a habit. The generalisable form is the same as the entry above wearing a different disguise: **a check is only worth what the artefact it ran against is worth.** Comparing the source to your backup proves the source; it says nothing about what the test actually executed.

**The same protocol then failed in its *restore* step, in instructions handed to the human.** They used the version-control system's checkout command to undo a deliberate break — but the file was untracked, as most files are while a feature is still in flight, so the command restored nothing and failed with an error that scrolled past between two blocks of test output. The expected result was written beside the step: the suite would go green. It did not, and the file was left broken on disk. The human noticed the second run still failing and said so.

Two lessons, and the second is the one that generalises. Narrowly: restore from a backup copy you took, never from version control, and confirm the restore by re-reading the changed line rather than assuming the command worked. Broadly: **the expected output written beside a command is a claim, and a reader who trusts it inherits the error.** Annotating a step with its result is useful when the step has been run in exactly that form, and is a guess in the costume of a result when it has not.

The generalisable form: **a check that cannot fail is worse than no check, because it looks like verification.** This project already demands that an implementer arm a test by deleting the behaviour and watching it fail. The same discipline applies to the commands offered to a reviewer as proof: before writing one down, establish that it would fail if the property did not hold. An unarmed command in a report is an unarmed test with a wider audience.
