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

SDD costs real ceremony, and for a 50-line feature the ceremony is decorative paperwork. That is why only 8 of this project's 53 features carry `"sdd": true` — the aggregates and state machines, the saga and its compensation, the outbox and idempotency, the read-model projection, and the observability wiring. Everything else skips the triple-doc but still travels the backlog state machine. The spec-becomes-infrastructure moments (Kafka topics derived from the AsyncAPI file, TypeScript types generated from both API documents) are where the spec pays for itself even on small features.

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
| `CLAUDE.md` | "How do we do things here?" — binding conventions | Leader role, architecture non-negotiables, coding/testing conventions, commit discipline. **Amended at human gates as the build goes**, which is why nothing may quote the copy injected into its context | Copied from #7, **substantially adapted** — three rules translated, one added; six amendments since, each recorded with its superseded phrasing | Phase 2 | Phase 8 |
| `feature_list.json` | "What is happening right now?" — the backlog state machine | 53 features, 8 `sdd: true`. Max one `in_progress`, enforced by `init.sh`. Only the reviewer sets `done` | #7's ids, names and phases **reset to `pending`**; one new feature (`cqrs_dispatcher`) | Phase 2 | every feature transition |
| `init.sh` | "Is the world sane?" — the session circuit breaker | Exit ≠ 0 ⇒ do not advance. Checks env, harness files, agent model declarations, backlog and SDD coherence, **plus three checks written here**: no superseded rule phrasing survives anywhere, the session file names the active feature, and a **backlog tripwire** that fails if a feature id disappears or a `done` reverts — the corruption every other check is blind to, since a backlog missing a feature is still perfectly shaped | Copied from #7; environment section rewritten for the .NET SDK, backlog validator kept as-is; three sections added in Phase 8 | Phase 2 | Phase 8 |
| `CHECKPOINTS.md` | "Am I actually done?" — objective close criteria | C1–C7; the reviewer walks them | Copied from #7; **C7 inverted** — from "is this reusable?" to "did it actually reuse it, and is the benchmark honest?" | Phase 2 | Phase 2 |
| `.superseded-rules` | "Did the amendment actually finish?" — one line per amended rule, carrying the phrasing it replaced | `init.sh` fails if any of them still appears outside the history files. Written because the sweep had been a habit, and a habit failed twice in two rounds | **Written here** | Phase 8 | per amendment |
| `scripts/arm-probe.sh` | "Prove the guard, without leaving the tree armed" — backup first, mutate, force the rebuild, run, restore from its own backup, force again, re-run | Never uses version control to restore, because most files are untracked while a feature is in flight. Refuses to start when the target does not exist — a lesson it taught itself | **Written here** | Phase 8 | Phase 8 |
| `.claude/agents/*.md` (×6) | Role separation with different powers and cost tiers | Each declares model + tools; reviewer deliberately read-only; test_maintainer deliberately bash-less | Copied from #7 with **model pinning unchanged**; `spec_author` gained a section on how reuse changes its job | Phase 2 | Phase 2 |
| `progress/current.md` | Working memory of the active session | Updated at every status transition, in lockstep with the backlog — the reviewer checks this (C2) | Template from #7, content fresh | Phase 2 | every session |
| `progress/history.md` | Append-only log + **per-feature effort records** | Carries a `#7 baseline` field per entry; this is the benchmark | Template from #7, **deliberately empty** — #8's numbers must be #8's | Phase 2 | every feature close |
| `progress/impl_*.md` | The implementer's own report per feature | What was built, evidence, deviations, what the review later caught | Written here | — | every feature |
| `progress/review_*.md` | The reviewer's verdict per feature | Probes with real output, defects with file/line/why, CHECKPOINTS walk | Written here | — | every feature |
| `specs/shared/` (7 files) | The system's definition, before the code | **Read-only.** A change is a spec amendment: explicit, human-gated, applied to every repository in the same session | **Copied verbatim from #7** — six of seven files byte-identical, `cmp`-proven | Phase 3 | Phase 3 (`SA-1`) |
| `specs/shared/test-matrix.md` | Requirement → test traceability | Columns 1–4 are #7's; only the Status column is this assessment's | Copied; reset by #7's own four-step recipe — 63 rows to `TODO`, columns 1–4 confirmed identical | Phase 3 | as features land |
| `specs/<feature>/` | Per-feature triple-doc for the large features | `requirements.md` mostly cites #7's `R<n>` ids; `design.md` is where nearly all the new work is | Written here | Phase 8 | per feature |
| `docs/PROCESS.md` | This document | Updated at the end of every phase — registry + status | Copied from #7, re-pointed; §11 reset | Phase 2 | every phase |
| `README.md` | Honest front door at every commit | Grows incrementally each phase; never describes software that does not exist yet | Written here | Phase 1 | every phase |
| `global.json` / `.editorconfig` | Toolchain and code-style pins | SDK pinned with `rollForward: latestPatch`; analyzer **severities** live in `.editorconfig`, **enforcement** in `Directory.Build.props` | Written here | Phase 1 | Phase 1 |
| `n8n/workflows/*.json` | The demo's "external world" | Gateway REST API only — which is why they port at all | **Reused unchanged** from #7, byte-identical (base URL env only) | Phase 3 | never |
| `infra/` (OTel, Prometheus, Grafana, Kafka topics, n8n import) | The stack-agnostic infrastructure | Nine files, `cmp`-verified byte-identical. The collector speaks OTLP and the topic script reads the shared spec — neither has anything stack-specific in it | **Reused from #7 unchanged** | Phase 4 | Phase 4 |
| `infra/mssql/` | Database bootstrap | The engine's image has no `/docker-entrypoint-initdb.d` and no init hook of any kind, so the entrypoint starts it, waits for it to answer, bootstraps, then hands the foreground back. The largest genuinely new piece of infrastructure in this assessment | **Written here** | Phase 4 | Phase 4 |
| `docker-compose.infra.yml` + `.env.example` | The runnable infrastructure | 15 services under its own Compose project namespace, so its containers and volumes cannot collide with the previous assessment's | Adapted from #7 | Phase 4 | Phase 4 |

---

## 10. Where the project is right now

> Maintained at the end of every phase. History of *how* each phase went lives in `progress/history.md`; this is only the current position.

**Position: Phase 9 complete — 24 of 53 features done.** Two services now run. An order can be placed, reserved against real stock, despatched, and every step of that is a fact on the wire rather than a call between components.

**The phase's own measurement, and it is the closest to parity this build has reached.** On the features with a counterpart in the previous assessment, roughly six hours against its four and three quarters — about 1.25×, where earlier phases ran at two and three times. The remaining hour and three quarters is unmatched work: a defect this build created in translation, and a seam it invented and then failed to keep.

**A confound appeared this phase and it will grow, so it is recorded before it distorts the conclusion.** Part of the per-feature gap is a **deliberately raised review bar**, not a slower language: two of this phase's rejections were for guards the previous assessment shipped without and disclosed. From here, each rejection records whether the predecessor's standard would have caught it — otherwise the final comparison will report harness maturity as a language penalty, which is the one conclusion the evidence cannot support.

**The ledger's first honest verdict.** All twelve of its claims survived line-by-line checking on their first outing against *extended* rather than authored code. But its value this phase was as a **review instrument, not a prevention record**: the gap it exposed was the row nobody wrote, and the phase's actual translation defect sat in code written before the ledger existed and therefore outside its reach. It also created debt rather than discharging any. The real experiment is the next phase — derive the rows from an enumerated boundary list, then count.

**The backlog grew by six during a three-feature phase, and that is discovery rather than production.** Four of the six are findings about *earlier* phases, and every one was found by a probe or an enumeration rather than by reading. That is a detector improving, and suppressing the number would be the worst available response. The real problem is scheduling: entries accumulate against a phase field, while the only closure mechanism this build has ever demonstrated is closing one **inside a feature that already has the file open** — which took one minute this phase. A phase field is not a schedule.

| Phase | What | State |
|---|---|---|
| 1 | Environment & repository — SDK/Node pins verified adversarially, account-explicit remote, `.gitignore` proven not to swallow source | ✅ |
| 2 | Harness layer — copied from #7 and re-pointed to .NET; backlog reset; C7 inverted | ✅ |
| 3 | Shared specification — copied **verbatim** from #7, `cmp`-proven per file; zero stack leaks found; `SA-1` raised and applied to both repositories | ✅ |
| 4 | Infrastructure compose (15 services, 36 s cold to all-healthy) + spec-derived Kafka topology. The database engine's bootstrap had to be written from scratch — its image has no initialisation hook at all | ✅ |
| 5 | Solution scaffold, SharedKernel, Contracts, NetArchTest architecture tests — 65 tests, twelve armed architecture rules, and a wire-parity oracle of twelve real messages captured from the previous assessment's own broker | ✅ |
| 6 | EF Core models + migrations for the four write databases — 20 tables, 60 integration tests against a real database engine, and a parity test asserting the reliability tables are identical across all four schemas | ✅ |
| 7 | Deterministic seed job — identifiers reproduced byte for byte from the previous assessment's own derivation scheme, and 413 master-data rows diffed row for row against its live database | ✅ |
| 8 | Orders service — aggregate, hand-rolled dispatcher, outbox/idempotency, acceptance, saga orchestrator, terminal-rejection classification | ✅ |
| 9 | Fulfillment — stock reservations and DESADV creation | ✅ |
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
| A test fixture whose money values were all zero, making the assertions on a reply's amounts look complete while proving nothing — the reply mapped the wrong money field onto the wire | The one blocking defect of its acceptance feature, plus the second implementation session and second review pass that found it | Never occurred: every fixture uses three pairwise-distinct, non-zero values with explicit inequality guards, and swapping the fields fails a real integration test |

### 11.2 Found here

**Phase 3 — an instruction invalidated by its own execution (`SA-1`).** The shared specification carried a four-step recipe telling a new assessment how to reset the traceability matrix. Step 4 named the prose to delete by listing the specific paragraphs present *in that copy*. Following the recipe therefore made the recipe false: the executed copy contained an inventory of content it no longer had, and the next assessment inheriting it could not tell a completed step from a missed one.

Two things make it worth keeping beyond the fix itself. First, the **previous assessment's final audit could not have found it** — that audit searched for stack-specific vocabulary, and this is a self-reference defect with no stack terms in it at all. Second, it was found by *doing* rather than by reading: the instruction had been reviewed carefully and was correct every time anyone read it. It only became wrong at the moment it was obeyed, which no amount of re-reading would have surfaced.

The generalisable form: **an instruction that describes its own current contents will go stale the first time it is followed.** Write the class, not the instance.

**Phase 4 — a verification command that could not fail.** Reporting that the message broker ran in its cut-down, correct mode, the agent offered a one-line command as proof: grep the broker's status page for the feature's name and expect zero hits. The status page names that feature whether it is on or off, so the command returned the same result either way. The claim was true — three later checks, including asking the feature to actually do something and being refused, confirmed it — but the evidence offered for it was worthless, and it was annotated with its expected output as though it were an assertion.

It was caught by the human reading the report, not by the agent that wrote it.

**Phase 8 — a verification tool that left a booby-trap.** The script written earlier this phase to arm guards correctly — because the protocol had been applied wrongly twice by hand — was itself run against a mis-typed path. Its cleanup trap then "restored" the file from a backup that had never been taken, writing a **zero-byte source file** at the wrong path, beside the real one, with a name that looked exactly right.

It compiled harmlessly, so nothing went red. It was found by a reviewer's forensics rather than by any test: the file's permissions and timestamp did not match anything the implementer had produced, which is how it was traced to the coordinator's own probe run.

Two things worth keeping. **A tool written to make a discipline safe is itself code, and inherits every hazard code has** — the fix was one line, refusing to proceed when the target does not exist, before the cleanup trap is armed. And more uncomfortably: **the artefact that survived was invisible to every automated check in the project.** Fourteen architecture rules, a full test suite and a state-coherence script all passed with a phantom file shadowing a real one. What caught it was somebody looking at the working tree and asking why a file's mode was unusual.

**Phase 8 — the same question asked twice by the human, about the same failure.** Early in the phase the coordinator brought three decisions to the human gate. The human asked whether they had already been decided — by the previous assessment — and two of the three had been, in its committed code and its own progress log. That produced a standing test: *did the previous assessment face this, and what did it do?* Only what it **could not** face, because the language or engine differs, is genuinely a decision.

The test worked. Given to the specification author, it closed every open point on the next feature and realigned four further decisions onto evidenced answers.

Then the coordinator failed it again, one layer up. The specification author applied the test correctly, found that the previous assessment had deferred a requirement, and cited the file. The coordinator relayed that finding to the human **as an open question** — having personally verified the evidence two messages earlier. The human asked the same question a second time.

The diagnosis is not that the rule was wrong but that it was aimed one level too low: it told subagents to check before raising, and said nothing about the coordinator resolving before escalating. **A gate exists for judgement the human alone can supply, not for questions the repository already answers** — and an open point that arrives at the gate with a citation attached is not an open point, it is a finding waiting to be relayed as one.

The rule now reads: resolve before passing on, and where something genuinely must go to the gate, go with a recommendation and the evidence — never a menu.

**Phase 8 — a rule kept current everywhere except in the file that tells reviewers what to enforce.** An earlier finding this phase produced a rule: never quote a convention from the copy injected into your context; read the file on disk, because the project amends its conventions at human gates and the injected copy goes stale. Sound, and it has a precondition nobody stated — **the disk has to be maintained too.**

When a non-negotiable was amended at a gate, the coordinator updated the conventions file, the checkpoint list and the entry map, and missed the reviewer agent's own definition, which still carried the superseded text. That is worse than a stale cache: the next reviewer would have read it from disk, exactly as instructed, and rejected correct code with confirmation that it had checked. A guard firing on something no longer true, with the checking ritual performed correctly.

Three things follow. **Agent definitions are rule-bearing files** and belong in the sweep whenever a rule changes — they are instructions, not documentation. And an amendment is not finished when the canonical file is edited; it is finished when nothing anywhere still asserts the old rule.

The third was the reviewer's, raised when asked to judge the first two, and it is the one with teeth: **the sweep as written was a habit, and this project's whole argument is that a habit is not a guard.** The first stale rule was caught by a grep; the second was caught by a grep *one round later, after the first finding had already put everyone on notice*. A discipline that fails twice in two rounds under maximum attention will not hold twenty phases later under normal attention.

So the sweep is now a check that fails. The superseded phrasing of every amended rule is recorded, one line per amendment, in the same change that supersedes it; the session-coherence script fails if any of them appears anywhere outside the history files. It cost one line per amendment and one clause in the script, and it converts *remember to sweep* into something that cannot be forgotten. Written, it immediately failed on two false positives of its own — which is the argument for writing it as a runnable check rather than a paragraph.

**Phase 8 — a specification outranking the instruction that dispatched it.** An implementer was given a brief whose constraint list forbade touching a directory that the feature's own approved task list named as touchable, for one specific item. It did neither thing silently: it left both files untouched, un-ticked the two affected boxes with an inline note explaining the conflict, and reported it for resolution.

Both alternatives were worse and both were silent. Obeying the brief would have shipped an incomplete feature against an approved specification. Obeying the specification would have violated a stated constraint with no record that it had been violated. The cost of stopping was one round-trip.

The ruling, which is the part worth keeping: **a gate-approved specification outranks the brief that dispatched the work**, because the specification passed a human gate and the brief's constraint list did not. A coordinator writing a brief is summarising a plan; where the summary and the plan disagree, the plan is the authority. The corollary matters more than the rule: **a subagent that flags a conflict is doing its job, not failing to follow instructions** — and a harness that punishes the flag will get silence instead, which is the failure it can least afford.

**Phase 8 — a lost update the coherence check could not see.** Two agents were launched in parallel on the reasoning that they touched different parts of the tree, which was true of the source and false of the backlog file: both were read-modify-write on it, and the later write silently reverted a state transition the earlier one had made.

The interesting part is not the race — races are ordinary — but that **the project's own state-coherence check passed throughout**. A status reverted from *specification ready* back to *pending* is still a valid status, still leaves at most one feature in progress, and still satisfies the rule that a specified feature has its documents on disk. Every invariant held. The state was simply wrong.

This is the third distinct disguise of the same failure in this build: a check that fires on nothing, a check run against the wrong artefact, and now a check whose invariants are all satisfied by an incorrect state. The generalisable form: **a coherence check validates shape, not history.** It can tell you the state is *legal*; it cannot tell you the state is the one you left. Where a transition matters, the defence is to avoid the race rather than to detect it afterwards.

**Phase 9 — a guard killed by the wrong mutation has not been armed.** A test was named for two identifiers being *the values a delegate returned*. Its assertions were that they were non-default and different from each other — true of any two identifiers, however obtained. It built the two known values it was going to check against, and never checked against them. Substituting the source of both left the whole suite green.

It had been cited as armed, and it was: deleting the emission killed it. **But deletion kills that test for an unrelated reason** — it proves a fact is emitted, and says nothing whatever about which identifier the fact carries. So the arming table was accurate and the property was unguarded, at the same time.

The implementer's own diagnosis is the transferable part, and it arrived without being asked for: *an assertion of non-collision can never prove provenance*. Its assertions had been generated to match the test's **shape** rather than transcribed from the test's own **name**, which already claimed the stronger thing.

The correction was deliberately *not* a new rule. A style rule produces no artefact and does not fire mechanically, which is the opposite of what this project's amendments that worked have had in common. The generalisable content is also different from that first phrasing, and better placed: this is a third face of the corruption family — substituting the **source** of a value with one that produces an equally valid-looking value. Deletion probes cannot see it because the field is present; corruption probes see it only if the test supplied the expected value. **So it is a rule about a test's inputs, not its assertions**, and stated that way it also covers a timestamp, where the right fix was a bracketing window rather than an equality. One clause, appended where the reader already stands, and the convention count stayed at four.

The uncomfortable detail worth keeping: **this was the same property, in the same feature, twice** — once in production code, as the backlog item that feature closed, and once in the test written specifically to guard that code.

**Phase 9 — a claim of absence is a search result, not a reading.** Three times in three features, a sweep was reported clear and disproved within minutes — and every time the disproof came from someone running a command instead of re-reading. A test the sweep had not mentioned; then, after that was fixed, another in the same file; then, after *that*, three more found by a single `grep` over every typed deserialiser call site, the worst of them asserting nothing whatever.

The evidence that settles it is the third miss, and it is uncomfortable: **the instance the sweep failed to mention was already written down**, in this project's own history file, committed eleven minutes before that feature began. Recording something in prose does not stop a prose sweep from missing it. Only enumeration does.

So a negative claim about the repository — *no test does X*, *nothing else has this shape* — is now reportable only as the exact command that enumerates the candidate set, its complete output, and one classification line per hit. **A missed hit must be visible as an unclassified line, not invisible as a sentence.** What makes this cheap rather than bureaucratic is that the enumerating command is usually one `grep`, and it is the same artefact whether the answer turns out to be "clear" or "three hits" — the cost is identical, only the honesty differs.

**Phase 9 — the fourth instance of the translation class, and the first in code written before the guard against it existed.** A reply decoder here accepted any reply into its success type, so an error reply produced an all-defaults object and the next line threw. The previous assessment does not have that bug: its decoder reads the reply as either shape and guards on which it got. The property was dropped in translation, in a feature that shipped weeks before the ledger convention was adopted.

That is the finding, and it generalises past this defect: **a convention adopted mid-project protects what comes after it and cannot reach what came before.** Four services were ported before this one existed. The response is deliberately narrow — not a re-read of those services, which is open-ended, but a sweep of the *boundaries*, because every instance of this class so far sits where a value crosses into or out of the process: a decode, a serialise, a column write, an upsert. Those sites are enumerable, on the order of eight, each a short comparison answering one question. Filed as backlog work with one binding constraint: **fix nothing in place**, because a fix inside a sweep is a change nobody reviewed against a spec.

The deciding evidence for doing it at all came from the feature itself rather than from a prior. It was dispatched specifically to port one missing guard from a thirty-line method; the implementer read that method; and a *second* missing guard from the same method was still absent afterwards. Two unported guards in one method, one surviving a dedicated repair pass, is not a base rate that can be argued away.

**Phase 9 — a ticked task is not evidence the assertion exists, and the flag was doing the work the claim should have done.** Three times now, a task has explicitly ordered an assertion, been ticked, and the assertion has not been written. Once it was *"read the committed offset from the broker; do not infer it from the redelivery alone"* — ticked, inferred. Twice more it was *"exactly one such fact, carrying this field"* — ticked, and the fact's persistence could be deleted, or its field corrupted, with the entire suite staying green.

The diagnosis came from the implementer rather than from review, and it is better than "be more careful". The task list marks tasks that must be armed, and **both features armed every marked task perfectly** — eleven of eleven, then twelve of twelve. Both defects were in **unmarked** tasks whose prose nonetheless made exactly the same kind of countable claim. The discipline had attached itself to the mark rather than to the claim, so a sentence saying *exactly one row* got written, ticked, and never mutated because nobody had marked it.

The correction places the responsibility where the decision is: the specification author marks every task whose prose asserts a count, an identity, an ordering or an absence, because that is a guard whether or not anyone labelled it one. And the implementer follows the marks, which is what it should do.

**Then the third instance showed the correction's limit, which is worth as much as the correction.** An amendment cannot retro-mark task lists that are already approved — and the offending task was sitting in one. So the fix for work already specified is not a rule but a sweep: read the whole list, find every countable claim, check each is actually asserted. The sweep found no fourth instance, and was spot-checked rather than believed, because a sweep that missed one is worse than no sweep — it gets cited as clearance.

**Phase 9 — deleting the emission is one mutation family, and a whole review pass can attack only that one.** A guard said *"exactly one release fact carrying the request's reason"*. It counted the row and never opened it. Corrupting that field, and another fact's retailer code, on the wire left one hundred and twenty-seven tests green.

What makes this worth recording is not the defect but the reviewer's account of why it had missed it a round earlier: **all six of its probes had attacked emission deletion, and none had attacked payload corruption.** It marked its own miss rather than reporting only the finding, and that is the more valuable half.

The protocol as written says *delete the behaviour and watch the test fail*, which is one question. A fact-emitting branch needs two: **does the guard fail when the row is absent, and does it fail when a field is wrong?** They find different defects. A suite that only ever answers the first will ship payload defects indefinitely — and with a wire contract, a saga that branches on a field's value, and four services still to build, the unasked question is the expensive one.

**Phase 9 — a design that made a false claim about this repository's own code, and nothing checks that kind of claim.** Three documents — a design section, a task, and a test's own comment — all state that a test reads the shared contract file as text and compares against it. It never has; it compares against hand-retyped lists. No drift exists today, and the retyped lists do catch the code changing alone, so it is not a defect. What is missing is the correlated error: a schema and a test changed together, wrongly.

The observation underneath is a blind spot distinct from the others recorded here. The ported-idiom ledger asks what supplied a property in the *other* repository. This asks something narrower and, it turns out, unguarded: **a design's factual claims about the code in this repository are as checkable as its claims about behaviour, and nothing in the process checks them.** Recorded as a finding and filed as backlog work rather than made a fourth convention in two days — a rule nobody can hold in their head is its own failure mode, and this one deserves a phase of evidence first.

**Phase 8 — the defect class two of the project's three strongest guards cannot see.** Three defects in this build were the same shape: a property the previous assessment's engine, language or library supplied for free, dropped in a rendering that read as faithful. The wire's key ordering, which turned out to be one database's storage normalisation leaking through a relay and had been written into the conventions as a byte-exact parity requirement. Money columns narrower than the domain type, justified as specification parity when the specification names no width — safe in a language whose numbers do not narrow. And a counter row seeded by an unconditional insert-or-ignore, rendered here as check-then-act, which two concurrent first-ever callers can both pass.

What makes them one class is not the mechanism but the way they hid. **All three satisfied their requirement text exactly.** So requirement-to-test traceability cannot see them — the requirement was met. And **arming cannot see them either**, because the behaviour was present and correct on the path the test took; the lost property only shows under a condition the test never created, a second writer or a larger value or a different storage engine. Those two mechanisms are this repository's strongest guards and both are structurally blind here.

The detection record says so. **Nought for three.** One was found by going and capturing the real bytes rather than reasoning about them, one by the human asking whether a decision had been a mistake, one by a reviewer reading SQL for an unrelated feature. Not one was found by the process.

The amendment adopted at the gate is deliberately small: every design that ports a mechanism records one line per idiom — what made it correct in the original, and who supplies that property here — with a guard test required wherever the honest answer used to be "the engine did". **Writing the line is most of the value.** The failure was never analytical difficulty; it was that nobody asked *what made this correct over there, and does that thing exist here?* at the moment of translating, when the answer is nearly free.

The generalisable form, and it is uncomfortable for any reuse project: **a specification tells you what must be true, not what was quietly making it true.** Requirements describe outcomes; correctness often rests on properties nobody wrote down because, in the original stack, nobody had to.

**Phase 8 — explaining a red run instead of reproducing it, three times, by three different agents.** A whole-suite run showed one failing test. It was attributed to memory pressure — swap was genuinely exhausted at the time — reproduced clean in isolation, and the next whole-suite run passed. Every one of those observations was true, and the conclusion was wrong: the failure was a real race in the test's own missing synchronisation, reproducible on an idle machine by slowing one component. The reviewer reproduced it deterministically on the first try.

Then it happened again, one level down. The fix round that corrected the attribution **closed a different red run the same way** — explained by attribution, with a subsequent green run offered as confirmation. And in the round that named the habit, the reviewer wrote *"exit 0"* for a check it had not yet run, then ran it, found exit 1, and corrected its own line rather than leaving it.

Three instances, three different agents, one shape: **a green run is not evidence about a red one.** The red run is the observation; the green run only says the conditions changed, and you chose which conditions. What makes it seductive is that the explanation is usually *partly* true — the machine really was under load, and load really did widen the window — so the wrong conclusion arrives wearing evidence.

The rule that came out of it, in the implementer's own words and better than any paraphrase: **a red test inside this feature's own suite is inside this feature's own scope, full stop.** A brief's "change nothing else" bounds which *other* files may be touched; it was never licence to leave a red run in your own requirement's test unexamined.

**Phase 8 — a reproduction vehicle that went quiet, caught by running the control.** Having found the race above, the reviewer had a mutation that reproduced it, and the fix round then used the same mutation as its arming evidence. In the final round the reviewer ran the leg nobody had asked for: the **unfixed** code against that same vehicle. It passed 4 out of 4 — where it had failed 2-of-4 for the reviewer and 3-of-4 for the implementer hours earlier.

So the arming everyone had been citing proved nothing, and would have gone into the record as proof. What replaced it is the useful part. Instead of a pass/fail ratio, the reviewer built a probe with a *structural* signal: feed the wait an unmatchable value and the run stretches from 16 seconds to 3 minutes 37, because every iteration now blocks on its own budget. One run, any machine, any load, no statistics. And the confirming green after restore ran in 23 seconds, which is itself proof the restore reached the compiled binary.

The generalisable form: **a probabilistic guard is not a guard, and the fix is usually not more runs but a different signal.** When a mutation's effect is a change in *likelihood*, look for one whose effect is a change in *kind* — a duration, a count, an exception type — something a single run can read.

**Phase 8 — undoing an edit with version control destroyed another agent's uncommitted work.** A reviewer reformatted the backlog file, thought better of it, and reverted with the version-control checkout command. That restored the last *committed* version — silently discarding a backlog entry the coordinator had added between rounds for a defect found in already-closed code.

The project already had a rule against that command, written for the arming protocol. It did not fire in anyone's head, and the reason is instructive: **that rule is justified by files being untracked**, and this file is tracked, so a reader who has correctly internalised the rule concludes it does not apply. It applies more. The backlog is almost always dirty — it carries the current feature's transitions plus anything anyone has added since the last commit — so reverting it to the last commit discards other writers' work by construction rather than by accident.

And once more the coherence check could not see it: a backlog with a feature missing is still a valid backlog, still has at most one feature in progress, still satisfies every invariant. That is the third disguise of the same failure in this build — a check that fires on nothing, a check run against the wrong artefact, and a check whose invariants are all satisfied by an incorrect state.

The generalisable form: **a rule's stated justification determines where readers will apply it, so the justification is part of the rule.** The fix was not only "do not run that command on this file" but explaining *why the untracked-file reasoning misleads here*.

**Phase 8 — a brief that forbade what the approved plan required.** The coordinator's instructions to an implementer said it must not touch the backlog file. The gate-approved task list's own final task said to set the feature's status in that file and stop.

The implementer followed the specification, correctly — this project had already ruled that a gate-approved specification outranks the brief that dispatched the work — and the coordinator then reported the edit to the reviewer as a scope violation, which cost a round trip to adjudicate. The bound had never constrained anything; it only manufactured a finding.

The generalisable form is about where coordination errors surface: **an instruction that contradicts the plan does not create a conflict for the worker, it creates one for the reviewer.** The worker resolves it correctly and moves on; the cost lands one stage later, on whoever has to work out which of two authorities was right. Before writing a constraint, read what the plan already tells the worker to do, and phrase the constraint around it.

**Phase 8 — three rounds of fixes that were correct and unguarded.** A feature was rejected three times. Not one of the seven defects across those rounds was a wrong line of code; every one was the same shape — **the behaviour is right, and removing it would go unnoticed**. A distinction the design makes load-bearing for a later feature had been discovered live by the implementer, fixed in production code, and shipped with no test; collapsing it left the full suite green.

The second round is the finding. All of the first round's fixes had landed correctly, and **three of the four were themselves unguarded** — including one where reverting the exact symptom the first round existed to eliminate left three hundred tests passing. The correction is not more review. It is that a fix is a change like any other, and the project's own arming rule applies to it: **the question to put to somebody who has just fixed something is not "does your fix work?" but "what fails if your fix is reverted?"** Round three was launched with that question in the brief and closed on the first pass, with seven reviewer mutations fired, seven failures, zero survivors — two of them on things nobody had thought to name.

The generalisable form: **a fix inherits the defect class of the code it fixes.** A round of review that only asks whether the reported symptom is gone will keep finding the same shape forever, because the shape is invisible to exactly the test that was never written.

**Phase 8 — the same lesson, one audience wider: a README written from the code rather than from running it.** The front page gained a short "run what exists so far" recipe, drafted by someone who had just read every file involved. Running it before publishing found three errors in nine lines. It named a command-line tool that is not installed on this machine. It predicted the first order's human-readable reference as number one, when the seeded database already holds six and the correct answer is seven. And — the bad one — it asserted that repeating a request with the same client-supplied id returns the original order, which is **a capability the code documents, in a comment, as deliberately not implemented yet**: that requirement is still `TODO` in the traceability matrix and belongs to a later feature.

The third is worth dwelling on because of *how* it happened. It was not guessed from nowhere; the field exists on the request, it is carried through the command, the specification requires the behaviour, and the surrounding feature is literally named for idempotency. Everything pointed at the claim being true except the one thing that decides it, which is what the code does when you run it.

This project already has the rule in another costume — *the expected output written beside a command is a claim, and a reader who trusts it inherits the error* — learned when instructions handed to the human annotated a step with a result nobody had observed. The generalisable form now covers documentation as well as reports: **a runnable instruction is an assertion, and publishing one you have not executed is shipping an unarmed test to the widest audience the project has.** The recipe in the README was run, top to bottom, and the outputs printed there are the ones it produced.

**Phase 8 — where the blocking line sits is a measurement, not a detail.** The same feature is the build's first honest loss against the benchmark: slower than the previous assessment, one more implementation session, one more review pass. The tempting explanation is that the new code was worse. It was not — and the previous assessment's own record for the same feature says so, because **two of the three defects that blocked here are written down there as advisories it disclosed and shipped.**

So the two builds produced the same defect class and drew the line in different places. That is the actual measurement, and it means a per-feature effort comparison is only honest when it says what was *enforced*, not just how many rounds each took. Recorded as a loss with the reason attached, because a benchmark that quietly credits stricter gate-keeping as slower work is measuring the policy and reporting it as the language.

**Phase 8 — a convention written at a severity that cannot enforce it.** The code-style configuration states a preference for explicit types over inferred ones, with a comment explaining why it matters for domain code. The codebase ignores it about **1,900 times**, across every service and every test project.

Nothing was broken, and nothing failed, because the rule sits at *suggestion* severity: it never becomes a warning, so the build's warnings-as-errors setting never sees it, and the formatter — which acts from warning level upwards — skips it too. The editor is the only thing that reports it, which is how it surfaced at all: somebody opened a file and asked whether the squiggles mattered.

Two things came out of measuring it rather than reasoning about it. Raising that one line to *warning* turns all ~1,900 into **build errors**, because code-style enforcement is on in the build — a one-character edit away from a red solution, now known rather than discovered later. And arming the rule against a single file showed the diagnostic falls exactly where the configuration says it should: an inferred type is accepted where the line already names the type and rejected where it does not, confirmed in both directions.

The generalisable form is the familiar one in an unfamiliar register: **a rule is only as real as the severity it is written at.** This project has a mechanism for a rule that was superseded and a protocol for a guard that was never armed; it had nothing for a rule that was never enforceable in the first place, which reads exactly like a convention until someone checks. Deferred to the quality-gates phase as a decision with two honest exits — adjust the rule to what the code does, or enforce it where the argument for it actually holds — and explicitly *not* as a three-line fix in the one file where it was noticed, which would have left the same rule unenforced everywhere else.

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
