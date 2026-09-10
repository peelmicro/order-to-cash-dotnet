# Current session

**Feature:** none active
**Status:** **PHASE 13 IS CLOSED** — all four features and all six backlog entries `done`. **50 of 70 features done**, all six services exist and the Gateway is complete. Owed: **`SA-2`'s own commit** (applied byte-identically in both repositories), and a wrap-up — nothing committed since `f70b6a1`. Next phase: **14**, reliability and observability, holding backlog ids **62, 67, 68, 69, 70, 71**

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

## The guard-hardening loop — brief it with this

Ids **60, 61, 63, 64, 65** share one cause: *a guard whose assertion cannot detect the defect it names.* Two features have since placed more exhibits on the same axis:

- **upstream** — no assertion at all, hidden by a **missing ledger row** (feature 26's D1);
- **the middle** — 60/61/63/64/65: a guard exists, names the right thing, cannot detect it;
- **sideways** — a row that correctly claims **no guard is owed** and then offers evidence that **cannot fire** (feature 26's R2-1).

**The instrument must interrogate ledger rows, not only tests:** *if this row's stated evidence were false, what would turn red, and has anyone seen it?* Because *"no guard is owed here"* is a claim like any other, and its honest justification is never *"the framework handles it"* but **"here is what I did to make it fail, and why nothing could."**

### The three mutation families, and the reviewer's ruling on when each applies

Feature 56 produced a **third** family. The ruling, which the loop's brief should carry verbatim in substance:

**Run all three — and "is the identifier a parameter?" is the wrong discriminator.** At feature 56's D1, deletion could not apply (removing the body is a compile error) and corruption could not (the value is a source constant the test never supplied). **The right predicate is a property of the literal: does it name a member of a set whose other members also exist in this repository?** `MSSQL_DB_ORDERS` has three siblings; `"TrustServerCertificate=True;"` has none, and swapping it degenerates into corruption. Enumerable families here: `MSSQL_DB_*`, `MONGO_DB_*`, the `otc-*` client and consumer-group ids, the `*.v1` event types, the NATS subjects, the Mongo collections.

**Why substitution earns its place: it is the only family whose green suite hides correct behaviour aimed at the wrong target.** Deletion gives missing behaviour, corruption gives wrong data, substitution gives a working system pointed at another service's database — the failure that crosses a service boundary. Both phase-13 instances were exactly that.

**Its false negative, which the instrument must handle:** if the substituted sibling is unset, the read falls back to a default and fails for the *default* reason rather than the *name* reason. **A swap that fails is not evidence until the failure message names what you intended to break.**

### Also fold in, cheaply

`tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs:77` cites a spec **line number** (`` `:1752` ``) beside the schema name. The line is correct today; the schema name is the stable reference and is already there. **Drop the number, keep the name** — one edit, and it removes the only line-citation in the solution.

Ids **60, 61, 63, 64, 65** share one cause: *a guard whose assertion cannot detect the defect it names.* Feature 26 added two more exhibits, and the reviewer placed all three on one axis rather than treating them as a list:

- **D1** is one step **upstream**: there was no assertion at all, and what hid it was a **missing ledger row**, not a weak one.
- **R2-1** is one step **sideways**: the row exists, correctly names no guard — and then offers evidence that **cannot fire**.
- **60/61/63/64/65** are the middle: a guard exists, names the right thing, and cannot detect it.

**Ruling on folding D1 in: fold the *question*, leave the *defect* closed.** D1 is finished and armed three ways; reopening it would be re-proving a closed thing.

**The instrument that loop builds must therefore ask of every ledger row, not only every test:** *"if this row's stated evidence were false, what would turn red, and has anyone seen it?"* — because `"no guard is owed here"` is a claim like any other, and its honest justification is never *"the framework handles it"* but **"here is what I did to make it fail, and why nothing could."*

## `SA-2`, and the three decisions that came out of it

**The amendment.** An optional `note` on `OrderCancelledPayload` in `asyncapi.yaml`, byte-identical in both repositories (`sha256` equal, checked by me), history entry in each, registry row in #8's README. **It is uncommitted and must be committed on its own** — an amendment is never bundled.

**Decision 1 — `init.sh` now checks shared-spec parity, and I built it because the spec author caught me asserting it already existed.** It did not. `init.sh` had exactly one mention of `specs/` — the per-feature triple-doc existence check — and **no** reference to a sibling checkout, hash or `cmp`. So the invariant this entire repository rests on, *the shared spec stays copied rather than quietly forked*, was unguarded through two amendments that happened to be applied correctly by hand.

Section **5d** now `cmp`s each shared file against `../order-to-cash-nestjs` (override with `OTC_SIBLING_REPO`). **`test-matrix.md` is exempt by design, not convenience**: it carries each assessment's own Status column, so it is the one file that *must* diverge — and exempting it is what makes the other six checkable, since a check that hashed all seven would fail daily and be switched off within a week. **Armed three ways**: a divergence in a guarded file fails (exit 1, naming the file); a divergence in the exempt file is correctly ignored (exit 0); a missing sibling warns rather than fails (exit 0). Restored `cmp`-clean after each.

**Decision 2 — a reviewer convention, in `CLAUDE.md` and `.claude/agents/reviewer.md`.** A disclosure whose root cause is `specs/shared/` must leave a numbered backlog entry or an `SA-n` proposal; approval prose may **not** discharge it with *"the next feature that touches X."* The evidence is that #7's own closing commit for that feature added **111 lines to `asyncapi.yaml`** and not the three that would have closed the gap — the file was open — and then **39 commits followed without touching it again.** *"The next feature"* named nobody, so nobody was named. Detection was never the failure; routing was.

**Decision 3 — NO cross-document consistency test.** The spec author costed it rather than guessing: 99 asyncapi schemas against 57 openapi, 23 shared names, **zero** property/`required` drift today — and it would have missed **both** real divergences these repositories have, including `SA-2`'s, because `CancelOrderRequest` and `OrderCancelledPayload` share no name with anything opposite. Green on the day it is written and 0-for-2 against the known defect population: that is another guard-that-does-not-guard, and it is rejected on those grounds rather than deferred.

**And a fourth, small one.** The related worry — comments citing spec **line numbers**, which amendments move — was enumerated rather than assumed. The whole solution contains **one** such citation, `tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs:77`'s `` `:1752` ``, and it is **correct** (that line is `lines:` under `Invoice`). No feature is warranted for a population of one. The proportionate fix rides the guard-hardening loop: **drop the line number and keep the schema name**, which is the stable reference and already present.

*Method note, recorded because it is the fourth instance this phase: my first two attempts to enumerate those citations both missed the only one that exists, because I searched for `openapi.yaml:1752` and for the filename adjacent to digits, while the text reads ``openapi.yaml`'s `Invoice` (`:1752`)``. The predicate was wrong twice before it was right.*

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
