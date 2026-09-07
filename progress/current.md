# Current session

**Feature:** none active — `projector_read_model` (id 24, phase 12) is **`spec_ready` and WAITING AT THE HUMAN GATE**; it stays there until the gate is answered
**Status:** idle, blocked on one gate decision
**Session started:** —

## Goal

**Phase 12 — the Projector.** The spec is written and waiting. Backlog id 57 closed first so the projector inherits a completion pair with a real causal edge.

## Decisions taken this session

Id 57 closed and approved. The projector spec pass was interrupted by an API rate limit after writing `requirements.md`, then resumed and completed `design.md`, `tasks.md` and the gate record without redoing the first file.

## Blockers

**ONE OPEN GATE DECISION — feature 24 cannot start until the human answers it.** Recorded in `progress/spec_projector_read_model.md`, open-point row 1, with a recommendation and its evidence:

> `NatsConnection` connects **lazily**. #7's `main.ts` awaited `connect()` at boot, so a wrong `NATS_URL` failed the boot loudly. #8's other three NATS services never noticed, because they are **responders** — `SubscribeAsync` materialises the connection at startup. **The projector only publishes**, and `PR19` requires publication failures to be logged and swallowed (correctly, for a *transient* failure). Composed, a misconfigured broker gives a projector that projects perfectly and **signals nothing, forever**, with every test on a good URL passing.
>
> **Recommendation: connect eagerly** — `await connection.ConnectAsync()` as the third step of `ReadModelBootstrap.StartAsync`. Cost: one `await` and one test. If approved, `requirements.md` gains `PR45` (verbatim text in the gate row) and task `H8`'s guard. If declined, `H8` is skipped and the implementer records the consequence — and it must **not** be worked around by making `PR19` rethrow, which would block the partition forever.

**Also awaiting the same signature: open-point row 2, a RATIFY rather than a question.** Matrix rule 3(b) requires `R54`/`R55`'s scoped rows to be ratified by somebody other than their author before they may close. Approving the spec supplies that; no shared file changes.

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
