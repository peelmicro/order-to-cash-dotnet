# CHECKPOINTS — session-close criteria

> In multi-agent systems you do not evaluate the path, you evaluate the destination. These are objective checkpoints a judge — human or AI — can walk to decide whether the project is healthy. The `reviewer` agent walks C1–C7 and refuses to close a session while any box in an applicable section is empty.

## C1 — The harness is complete

- [ ] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [ ] `progress/current.md` and `progress/history.md` exist.
- [ ] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer.
- [ ] **Every agent definition declares its model** — either `model:` in the frontmatter, or a description stating it deliberately inherits the session model.
- [ ] `./init.sh` exits 0.

## C2 — State is coherent

- [ ] At most **one** feature `in_progress` in `feature_list.json`.
- [ ] Every status is in `rules.valid_status`.
- [ ] Every `done` feature has passing tests associated with it.
- [ ] `progress/current.md` describes the active session or holds only the template — never leftovers from a previous session.
- [ ] Every `blocked` feature records *why* it is blocked.

## C3 — Architecture is respected

- [ ] No `Microsoft.EntityFrameworkCore`, `Confluent.Kafka`, `NATS.*`, `MongoDB.*` or `Microsoft.AspNetCore.*` reference inside any `Domain/` folder — verified by running the NetArchTest suite, not by eye.
- [ ] No cross-service database access: no service reads another service's schema, and no foreign key crosses a service boundary.
- [ ] No shared runtime code beyond `src/SharedKernel`, `src/Contracts` and `src/Cqrs` (the third added at the Phase 8 human gate — see `CLAUDE.md`; #7 needed no equivalent because its dispatcher came from a package).
- [ ] No `Domain/` namespace references `OrderToCash.Cqrs` — the dispatcher is an Application-layer concern, and permitting a third shared project must not widen what the domain may reach for.
- [ ] `src/SharedKernel` still has zero `PackageReference` entries.
- [ ] No `decimal` in domain arithmetic — `Money` is `long` minor units; `decimal` only at presentation boundaries.
- [ ] Every inter-service interaction is classifiable as Kafka-fact or NATS-RPC per the decision matrix — no Kafka-as-request-bus, no RPC-for-facts.
- [ ] No stray debug logging, no context-free TODOs.

## C4 — Verification is real

- [ ] `./quality.sh` (format check + build + test + coverage) passes.
- [ ] Domain tests are pure — no framework references, no DB, no broker.
- [ ] Integration tests use Testcontainers for .NET against real MsSql / Kafka / NATS / MongoDB — not mocked brokers.
- [ ] Coverage thresholds met: **≥80% domain layer, ≥60% overall**.
- [ ] **No Jest anywhere.** xUnit is the backend runner, Vitest the web one.

## C5 — The session closed cleanly

- [ ] No suspicious untracked files (`*.tmp`, build output outside `.gitignore`).
- [ ] `progress/history.md` has an entry for the feature just finished, **including its effort record** (sessions, wall-clock).
- [ ] `feature_list.json` reflects the true state of every feature touched.
- [ ] The human has been told **what was done** and **how to test it manually**.
- [ ] **Claude did not commit.** The commit is the human's, after testing.

## C6 — Spec-Driven Development

- [ ] Every `"sdd": true` feature in `spec_ready`, `in_progress`, `in_review` or `done` has `specs/<name>/` with all three of `requirements.md`, `design.md`, `tasks.md`.
- [ ] `requirements.md` uses strict EARS notation, every requirement carrying an `R<n>` id.
- [ ] Every `done` sdd feature has all its tasks ticked `[x]` in `tasks.md`.
- [ ] Every `R<n>` is covered by at least one concrete named test, recorded in `specs/shared/test-matrix.md`.
- [ ] The spec commit **precedes** the implementation commit in git history.

## C7 — Spec-reuse fidelity and benchmark honesty (assessment #8 only)

This is #7's C7 turned around. #7's job was to *produce* something reusable; #8's job is to *prove it reused it*, and to say honestly what that was worth. These boxes are the evaluation criteria the Task names as specific to this assessment.

- [ ] **`specs/shared/` is still byte-identical to #7's**, except `test-matrix.md`'s Status column — which records *this* assessment's realisation and is expected to name xUnit classes and C# paths. Verified with a real `diff` against the #7 checkout, not from memory.
- [ ] **Every deviation is a recorded amendment, in both repositories.** No silent fork. Each amendment is its own commit here, named in the README, and either back-ported to #7 or listed with an explicit reason for deferring.
- [ ] **The `R<n>` ids are #7's.** Where a requirement is claimed, the .NET realisation genuinely satisfies the same requirement — a reused id is a claim about behaviour, not a convenient label.
- [ ] `n8n/workflows/*.json` are **unchanged** from #7 apart from the base-URL environment variable, and all four fire green against the .NET Gateway. This is the sharpest available parity test: if a workflow needs editing, the OpenAPI contract was approximated rather than re-implemented.
- [ ] The black-box API script proves the **same** saga steps, the same facts and the same compensation as #7's.
- [ ] `progress/history.md` effort records are complete and honest — **including the features that were not faster.** An all-green benchmark is not a result, it is a lack of measurement.
- [ ] The README's benchmark section gives what the reuse saved, what it did not, and what it cost, in comparable detail.

---

**How to use this file:** the `reviewer` agent walks each box, marks `[x]` or `[ ]`, and rejects the close if any applicable box is empty. Sections C3–C4 only apply once application code exists (phase 5 onwards); C6 only once a `sdd: true` feature has started (phase 8 onwards).
