# Current session

**Feature:** none active
**Status:** **Phase 12 is CLOSED** — ids 24, 58 and 59 all `done`, 39 of 59 features done. Phase 13 (Gateway / BFF) is next and nothing in it has started. Uncommitted: the whole ids 58/59 loop, the sweep script, backlog id 60 and this file

## The recurrence this file keeps producing, and what to do about it

**A17 is the fourth consecutive feature whose review has recorded that this file disagreed with the backlog.** Round 2 of ids 58/59 recorded it as a **hard** `init.sh` failure rather than a cosmetic nit, because closing the last feature of a phase leaves `**Feature:**` naming something no longer active.

The pattern is now legible enough to state: **this file goes stale at exactly one moment — the transition a reviewer performs — and the leader is never the one performing it.** `init.sh` catches the header (it did, immediately, both times this session) and cannot catch a stale body, by its own honest admission: the lockstep check reads the `**Feature:**` line only. So the header is mechanically safe and the body is not.

**The standing instruction that follows: rewrite this whole body at every transition, not just the header, and treat a reviewer's approval as the trigger.** Do not wait for `init.sh` to fail — by then the failure is in a review document with a finding id attached.

## Goal

**Phase 13 — the Gateway / BFF.** Nothing started. Its features are REST per the copied `openapi.yaml` with Swagger UI, JWT auth plus login rate limiting (a #7 late finding, specified up front here), NATS RPC clients, MongoDB read-model queries, the remittance endpoint, and SSE push with heartbeat and reconnect.

## Decisions taken this session

Phase 12 ran as three features and closed with **five review rounds across them, in which executable production code changed exactly once** — one exception message. Every other defect was in a record, a test, a fixture or an instrument.

- **Id 24 `projector_read_model`** — rejected three times, all on records. Ledger row `L12` was answered backwards, then corrected with a false mechanism, then corrected in five places while the original wrong version survived in three more. Committed in `95f62e3` / `facbaea`.
- **Ids 58 and 59** — run as **one loop** on the leader's judgement, and the review confirmed that was right for a reason worth keeping: the two entries shared a *cause* (colliding fixture values), not merely a directory, so one bracketing fix served both. Rejected once, on a live survivor the review found itself — a **containment** collision (`"credit.release"` is a prefix of `"credit.released.v1"`), which defeats `Assert.Contains` exactly as equality defeats `Assert.Equal`. A new collision shape, in three sites no prior enumeration had included.
- **The sweep is now an instrument**, `scripts/notification-template-payload-sweep.sh`, committed at the leader's ruling over the implementer's sound objection. The deciding evidence was that the population had been counted three times by three parties and yielded **14, 89 and 95**.

## Blockers

None. Awaiting the human's `full wrap-up` to commit and push.

## Notes

**This file is the leader's and no subagent may write to it.** Its body was once overwritten by an implementer with a claim its own task list contradicted — defect D3 of feature 24.

### Backlog attachment map

| Entry | Rides | Why |
|---|---|---|
| **52** — retroactive boundary ledger for pre-ledger services | **standalone** | Its own acceptance forbids fixing anything in place |
| **56** — every env read in every `Program.cs` is deletable with the suite green | **phase 13** | Six composition roots now; choose the mechanism once and inherit it |
| **60** — envelope fixture collisions defeat provenance assertions | **phase 13** | Latent, not live: exactly one hand-written envelope copy exists. The Gateway adds the next envelope-reading surface |

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
