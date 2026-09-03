# Current session

**Feature:** none — `orders_acceptance` (id 15) closed and committed, awaiting feature 16
**Status:** idle
**Session started:** —

## Goal

Next: `order_saga_orchestrator` (id 16, phase 8, `"sdd": true`). It opens with a spec pass and a **human approval gate** before any code — no exceptions.

## Decisions taken this session

Feature 15 wrapped up: two commits pushed (`4c6ed34` code, `fb10544` docs), the four external assessment documents updated, and the service verified against the deployed stack rather than only Testcontainers.

## Blockers

None.

## Notes

**Brief for feature 16, with #7's evidence attached so the gate is not asked to re-derive it.**

#7's baseline for this exact feature: 1 spec session + 1 gate revision + 1 implementation session + **1 review pass, approved first time** — ≈1 h 45 min implementation, ≈35 min review. Its own history calls it *"the cheapest `sdd: true` feature of phase 8 by a wide margin, on the most complex surface"*, and names the cause: a spec with 35 tasks and **a step table the implementer could transcribe**. That is the single most transferable fact about this feature, and it says where the spec pass should spend its effort.

Two things #7 paid for here that #8 will not:

- Its live-stack walkthrough found a **transport-binding crash** — the hybrid-app bug where one decorator registered on every connected transport. .NET has no such ambiguity, and `CLAUDE.md` forbids reintroducing it by multiplexing transports through one service class.
- `saga_commands` pending/sent/parked plus the sweeper, and `saga_ignored_facts`, are already in the Orders schema since phase 6 — the tables exist and are migrated.

Carried into this feature from feature 43's review, and still open: the no-MediatR guard reads **three hardcoded project paths of twenty-one**. Closed for the central-package-management route and open to a hand-written `VersionOverride`. **Glob** the project files when the solution-wide equivalent lands in `tests/Architecture.Tests` — a glob is what makes it closed, a list is what made it a whitelist.

And the rule that ended feature 15, which belongs in every brief from here: the question to put to a subagent that has just fixed something is not *"does your fix work?"* but **"what fails if your fix is reverted?"**

---

## Template (reset to this on session close)

```markdown
# Current session

**Feature:** `<name>` (id <n>, phase <n>)
**Status:** <status>
**Session started:** <date>

## Goal

## Decisions taken this session

## Blockers

## Notes
```
