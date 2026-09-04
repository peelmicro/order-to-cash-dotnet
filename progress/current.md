# Current session

**Feature:** none — `order_saga_orchestrator` (id 16) closed, awaiting feature 42 or 45
**Status:** idle
**Session started:** —

## Goal

Next: `orders_saga_terminal_rejection_classification` (id 42, `sdd: false`) or `order_number_allocator_seed_race` (id 45, `sdd: false`) — both phase 8, neither needs a spec gate.

## Decisions taken this session

Feature 16 approved on the fourth review round. Two `CLAUDE.md` amendments landed: never forbid in a brief what the approved `tasks.md` mandates, and no agent runs `git checkout --` on `feature_list.json`.

## Blockers

None.

## Notes

Nothing committed since `850f32c` — feature 16's whole tree, the two `CLAUDE.md` amendments, the `docs/PROCESS.md` entry from feature 15's wrap-up, and backlog id 45 are all uncommitted.

Carried for whoever takes id 45: the race test asserts the race **reproduces**, so it must be **inverted, not deleted**, when the allocator is fixed.

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
