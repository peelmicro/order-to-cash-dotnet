# Current session

**Feature:** none active
**Status:** **Phase 13's four features are all `done` — the Gateway is complete and all six services exist.** 43 of 64 features done. Six backlog entries remain filed against phase 13 (**56, 60, 61, 63, 64, 65**), so the phase is open and **no closing assessment is due**. `SA-2` is still with the human. **Nothing has been committed since `4f9746d`** — four features, a whole new service and several `CLAUDE.md` amendments are uncommitted

## Goal

**Phase 13 — the Gateway / BFF, and the two Orders responders it needs.** The sixth and last service. REST per the copied `openapi.yaml` with Swagger UI, JWT auth plus login rate limiting, NATS RPC clients, MongoDB read-model queries, the remittance endpoint, and SSE push with heartbeat and `Last-Event-ID` reconnect.

Six features, **all `sdd: false`** — so no spec phase and no gate on any of them, and `feature_list.json`'s acceptance bullets plus `specs/shared/openapi.yaml` are the specification of record.

## The gate item — APPROVED, and now in force: the ledger binds to the PORT, not to the document

**This is not a new idea; it is an unactioned recommendation from the phase-10 closing assessment, and phase 13 is the case it was written for.** The ledger lives in `design.md`, so an `sdd: false` feature has none. Phase 10 found the consequence: feature 22 ported #7's payment handler wholesale, had no `design.md`, carried no ledger, and its one finding of substance was exactly a ledger-class miss.

**Every feature in phase 13 is `sdd: false`, and id 25 ports #7's gateway wholesale.**

**And #7's own record is the evidence.** Its `gateway_rest_auth` was **rejected twice**, and its history entry says the first rejection was *"the first rejected on a defect that no test in the repository could have caught, because both sides of the seam were tested only against the wire each preferred"* — a NATS wire mismatch between the gateway's client and the responders it calls, each side tested against its own assumption. That is a ported-idiom defect in its purest form: a property #7's framework supplied implicitly on one side of a seam and not the other.

**Recommendation: an `sdd: false` feature that ports a #7 mechanism owes its ledger in `progress/impl_<feature>.md`, where its arming table already lives.** No new document, no new ceremony — one section, in a file the implementer already writes.

## Backlog attachment map — written BEFORE the first implementer is dispatched

Phase 10 wrote its map up front and closed five entries inside one feature, against phase 9's one. Phase 12 did the same and closed three. This is the phase-13 map.

| Entry | Rides | Why |
|---|---|---|
| **56** — every env read in every `Program.cs` is deletable with the suite green | **~~id 25~~ → its own feature, immediately after id 25** | **Revised, and the reason is the discriminator ids 58/59 supplied: ride when entries share a CAUSE, not merely a phase or a directory.** Id 56's cause — no test project compiles a composition root — is not id 25's cause, and its blast radius is six `Program.cs` files across six services while id 25 touches one new one. Bundling them would put a cross-cutting guard mechanism inside the phase's largest feature. It still waits for id 25, so the mechanism is chosen once with the last composition root in hand. Its stale count is corrected in the acceptance: **49 reads across six files**, not 34 across three |
| **60, 61, 63, 64, 65** | **one guard-hardening loop, after id 26** | **Revised, applying the same discriminator that moved id 56: ride on a shared CAUSE.** All five are the same cause — *a guard whose assertion cannot detect the defect it names*: fixtures whose colliding values defeat a provenance assertion (60), an `.OrderBy` nothing checks (61), a retry budget that expires in 1 ms (63), a wire-key theory compared against itself (64), a mapper transposition that survives (65). They also share arming work and largely share test projects. One loop, one review, one instrument-shaped fix — not five rounds |
| **52** — retroactive boundary ledger for pre-ledger services | **standalone, and now overdue** | Its own acceptance forbids fixing anything in place, so it cannot ride. It has been `pending` since phase 10 and phase 13 adds the last decode boundaries it would have to walk — **the cost of deferring it again is that it grows** |

**The map declining to attach is the harder half of the judgement**, and entry 52 is that half here.

## With the human — `SA-2`, and it blocks nothing

`specs/shared/` contradicts itself, identically in #7. `openapi.yaml:1344-1347` describes `CancelOrderRequest.note` as *"Free-text operator note **recorded on the timeline entry**"*; `asyncapi.yaml:2602-2627`'s `OrderCancelledPayload` has **no** field to carry it, and that payload is the only thing the projector builds an `order.cancelled` timeline entry from. So id 41's acceptance bullet 4 was never satisfiable as written.

#7 shipped the same gap, disclosed; its reviewer ruled that **the next feature to touch `OrderCancelledPayload` must close it** — which was id 41. Recommendation put to the human: raise `SA-2` (add optional `note` to `OrderCancelledPayload`), back-port to #7 in the same session per `SA-1`'s convention. **Nothing in phase 13 waits on it.**

## The guard-hardening loop — brief it with this, it is the reviewer's own framing

Ids **60, 61, 63, 64, 65** share one cause: *a guard whose assertion cannot detect the defect it names.* Feature 26 added two more exhibits, and the reviewer placed all three on one axis rather than treating them as a list:

- **D1** is one step **upstream**: there was no assertion at all, and what hid it was a **missing ledger row**, not a weak one.
- **R2-1** is one step **sideways**: the row exists, correctly names no guard — and then offers evidence that **cannot fire**.
- **60/61/63/64/65** are the middle: a guard exists, names the right thing, and cannot detect it.

**Ruling on folding D1 in: fold the *question*, leave the *defect* closed.** D1 is finished and armed three ways; reopening it would be re-proving a closed thing.

**The instrument that loop builds must therefore ask of every ledger row, not only every test:** *"if this row's stated evidence were false, what would turn red, and has anyone seen it?"* — because `"no guard is owed here"` is a claim like any other, and its honest justification is never *"the framework handles it"* but **"here is what I did to make it fail, and why nothing could."*

## Settled this session — do not re-open

- **The n8n workflows and the black-box API script against the .NET Gateway** are now runnable for the first time, and the round-2 review flagged them as needing scheduling. **They are already scheduled**: id **31** `api_tests` (phase 18) and id **33** `n8n_workflows` (phase 20), both `pending`. No action, no new entry.
- **Ledger row 6's route-precedence claim** (feature 25) was corrected **by me** after I first wrote it too strongly. `RoutePrecedenceTests` probes **one** direction — parameter route mapped *before* the literal, the order under which Express would swallow the literal segment — and the row now says so. The forward order is deliberately unprobed because it would pass under an order-sensitive router too and therefore distinguishes nothing. **Feature 26 registers `/orders/stream` and will read this row**: it licenses *literal-beats-parameter at the same position*, not route precedence in general.

## What the phase's features cost, and the one pattern behind the rejections

| | #7 | #8 |
|---|---|---|
| id 40 `orders_catalog_responder` | 1 pass, approved first time, ≈56 min | 4 passes, ≈1 h 45 min — **≈1.9×** |
| id 41 `orders_cancel_responder` | 2 passes + a leader spec amendment, ≈2 h 45 min | 5 passes, ≈3 h 20 min — **≈1.2×** |

**Both rejections were the same shape: a guard #7 wrote, dropped in translation.** That is now a `CLAUDE.md` convention — when porting a mechanism, enumerate #7's **tests** for it, not only its source. Against that, #8 needed **no** follow-up pass for id 41's fourth branch, because it inherited the `billing.credit.release` amendment #7 needed a gate for.

And fix round 3 found a defect **#7 half-knows and still carries**: its stub responders `await connection.flush()` with a comment naming *"no responders"*, while its saga harness blocks only on Kafka readiness — so #7's NATS half is unclosed to this day.

## Carried forward into id 25's brief — settled, not open

- **No NATS responder in this solution uses a queue group** — four subscription sites across Orders, Fulfillment and Billing, enumerated. **#7 has none either**, so this is parity rather than a #8 regression, and both run one instance per service in compose. It is a latent scaling property common to both assessments, not a defect, and it is recorded here so id 25's brief states it rather than rediscovering it.
- **`billing.credit.release` already exists in #8** — in `specs/shared/asyncapi.yaml:440-455` **and** implemented in `BillingRpcResponder`. #7 could not build id 41's fourth cancellation branch at all: the RPC existed nowhere, its implementer correctly refused to fabricate the contract, and a leader interlude had to add the channels before a follow-up pass could finish it. **#8 inherits that amendment already applied** — another dividend that will show up as absence.

## Order of work, and the one thing #7's record says to do differently

1. **id 40 `orders_catalog_responder`** and **id 41 `orders_cancel_responder`** — the two NATS responders the Gateway calls. #7 built these *after* discovering the Gateway needed them. Building them first means id 25 has real responders to talk to rather than stubs, which is where #7's F1 seam defect lived.
2. **id 25 `gateway_rest_auth`** — with **id 56** riding.
3. **id 26 `gateway_sse_push`** — with **id 60** riding.

**The seam is the risk, and it is named in advance.** #7's blocking defect was that the gateway and the responders were each tested against the wire *each preferred*, so both suites were green and the pair did not work. #8 has one advantage #7 did not: NATS.Net request-reply was verified against the deployed stack outside Testcontainers in phase 8, feature 15. That does not make the class impossible — it makes it *checkable*, and the check is an end-to-end call through a real broker, not two green suites.

## Blockers

None. The gate item above is a recommendation awaiting the human, not a blocker on writing briefs.

## Notes

**This file is the leader's and no subagent may write to it.**

**Rewrite this whole body at every transition, and treat a reviewer's approval as the trigger** — this file went stale at four consecutive features, always at the moment a reviewer performed a transition the leader was not present for. `init.sh` catches the `**Feature:**` line only and says so; it cannot see a stale body.

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
