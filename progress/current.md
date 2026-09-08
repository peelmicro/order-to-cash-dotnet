# Current session

**Feature:** none active
**Status:** phase 12 in progress — feature 24 `projector_read_model` closed `done` after four review rounds; backlog ids 58 and 59 remain `pending` under this phase, so **no phase-12 closing assessment is due**

## Goal

**Phase 12 — the Projector and the MongoDB read model.** Backlog id 57 was closed first so the projector inherited a completion pair with a real causal edge, rather than amending its own spec later as #7 had to.

Remaining in phase 12: **id 58** (the envelope copy is by hand and its `correlationId` is unguarded) and **id 59** (14 date-typed payload sites survive mutation). Both are Notifications-file work, both are the same class, and the files are open.

## Decisions taken this session

Id 57 closed and committed. The projector spec was written across two passes (the first cut off by an API rate limit after `requirements.md`, resumed without redoing it) and approved at the gate, adding `PR45` — the NATS connection is established at boot, not on first publish.

Feature 24 took **four review rounds and three fix rounds, none of them on production code.** Every defect was a false or unguarded *claim*: D1 and D4 on ledger row `L12`, D3 a false statement in a record, D5 the retired hazard surviving in three places the fix's own list did not name. The production behaviour was correct from the first submission and never changed.

Three conventions came out of it, all now in `CLAUDE.md`:

- **An engine claim probed in one direction is a claim about that direction only** (from D4 — the row explained the right answer with a false mechanism, and only reversing creation order exposed it).
- **Enumerate on the wording of the claim being retired, not the claim being written** (from D5 — everyone grepped the *mechanism* word, and the residue carried the *hazard* wording, which shares no term with it).
- **Exclude by path at the source, never post-filter the output** (from the round-4 finding — `grep -rn … | grep -v '/bin/'` filters on content as well as path, and preferentially deletes the lines that quote the enumeration command).

## Blockers

None. Nothing is committed since `766f21f`: feature 24's whole tree, the `CLAUDE.md` amendments and the `progress/` records are all uncommitted and awaiting the human's `full wrap-up`.

## Notes

**This file is the leader's and no subagent may write to it.** Its body was once overwritten by an implementer with a claim its own task list contradicted — review defect D3 of this feature.

**`init.sh` cannot see a stale body**, for the reason its own success message states: the lockstep check reads the `**Feature:**` line only, and everything below it is invisible. That limitation is documented rather than fixed, because a check over free prose would be a guard nobody could trust — so **this body is maintained by reading, not by the script.** Rewrite the whole body at every transition, not just the header.

### Backlog attachment map

| Entry | Rides | Why |
|---|---|---|
| **52** — retroactive boundary ledger for pre-ledger services | **standalone** | Its own acceptance forbids fixing anything in place |
| **56** — every env read in every `Program.cs` is deletable with the suite green | **phase 13** | Five composition roots now; choose the mechanism once and inherit it |
| **58** — the envelope copy is by hand and its `correlationId` is unguarded | **phase 12** | The projector reads envelopes; this is one assertion inside an existing theory |
| **59** — 14 date-typed payload sites survive mutation | **phase 12** | Same class as 58 and the same files are open |

---

## Template (reset to this on session close)

```markdown
# Current session

**Feature:** <name or "none active">
**Status:** <status>
**Session started:** <date>

## Goal

## Decisions taken this session

## Blockers

## Notes
```
