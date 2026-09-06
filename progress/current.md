# Current session

**Feature:** none — **Phase 11 complete**, 35 of 58 features done
**Status:** idle
**Session started:** —

## Goal

**Phase 12 — the Projector and the MongoDB read model** (`projector_read_model`, id 24). Phase 11 had one feature and it is closed.

## Decisions taken this session

Phase 10 closed and committed; Phase 11 closed. A `commit-msg` hook now refuses a subject making an unverified quantity claim, and `init.sh` §5c verifies it is installed. `init.sh`'s lockstep check now states what it actually reads, after its success message overstated it for a whole feature.

## Blockers

None.

## Notes

**Rewrite this whole body at every transition, not just the header.** It was stale for a feature and `init.sh` could not see it — the check reads the `**Feature:**` line only, and now says so.

### Backlog attachment map

| Entry | Rides | Why |
|---|---|---|
| **52** — retroactive boundary ledger for pre-ledger services | **standalone** | Its own acceptance forbids fixing anything in place |
| **56** — every env read in every `Program.cs` is deletable with the suite green | **phase 13** | Five composition roots now; choose the mechanism once and inherit it |
| **57** — the completion pair has no causal edge | **standalone, Billing — and it is now urgent** | Filed to land *before* the projector reads causal chains. Phase 12 is that feature. If it is not closed first, the projector inherits an unordered pair |
| **58** — the envelope copy is by hand and its `correlationId` is unguarded | **phase 12** | The projector reads envelopes; this is one assertion inside an existing theory |
| **59** — 14 date-typed payload sites survive mutation | **phase 12** | Same class as 58 and the same files are open |

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
