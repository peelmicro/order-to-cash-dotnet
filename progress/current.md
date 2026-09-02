# Current session

**Feature:** `orders_aggregate` (id 13, phase 8) — `sdd: true`
**Status:** in_progress — spec APPROVED at the human gate
**Session started:** 2026-09-02

## Goal

The `Order` aggregate, its state machine and its invariants, implemented against an approved triple-doc. `src/Orders/Domain/` and `tests/Orders.UnitTests/` only; the repository adapter is designed here but built by feature 15.

## Decisions taken this session

- **The hand-rolled dispatcher is binding across all six services** (human gate). It does not fit all six equally — the Gateway's commands are outward RPC, and Notifications and Projector are pure consumers — but #7 used `@nestjs/cqrs` in all six, and a #8 that used its dispatcher in three would turn an architecture difference into what looks like a language difference in the benchmark.
- **Money columns corrected `int` → `bigint`** (human gate, feature 44). The plan had justified `int` as "spec parity"; `specs/shared/` specifies *"integer minor units"* and never a width. Every narrowing cast on money is deleted, not made checked.
- **The Phase 6 migrations were amended in place** rather than superseded by a widening migration: they never left this machine, nothing depends on them, and an `ALTER` scar documenting a three-day-old mistake is worse than a clean schema plus an honest record.

## Blockers

None.

## Notes

- **The most valuable thing this phase has produced is a test for the gate itself.** Prompted by the human asking "is that something that was not already decided?", the rule is now: before a question goes to the gate, ask *"did #7 face this, and what did it do?"* — the answer is in its code or its `progress/history.md`. Only what #7 **could not** face, because the language or engine differs, is genuinely a decision. Two of my three gate items failed that test, and applying it to the whole spec realigned **four more** points onto #7's evidenced answers, each now cited by file and line.
- **`feature_list.json` is a single-writer file.** Two agents were launched in parallel on the reasoning that they touched different directories — true of the source, false of the backlog. A `spec_ready` transition was silently reverted, and `init.sh` passed throughout, because a reverted status is still a *valid* status. A coherence check validates shape, not history.
- Still due in this phase: the wire-format parity check against the golden envelopes — Phase 5 proved the serializer, Phase 8 must prove the producer.

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
