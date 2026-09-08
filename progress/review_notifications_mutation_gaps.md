# review_notifications_mutation_gaps — backlog ids 59 (drives) and 58 (rides)

**Verdict: REJECTED.**

Closing both entries closes phase 12, and I cannot sign that close. Two reasons, in order of weight:

1. **A payload interpolation site in a shipped email still survives mutation, found by this review.** `src/Notifications/Application/Templates/OrderCancelledTemplate.cs:16` renders `payload.CompensationSteps.Select(step => step.Step)`. Replacing `step.Step` with `step.EventType` leaves the whole suite green — **65/65, my own run** — because the fixture's step is `("credit.release", "credit.released.v1", …)` and `Assert.Contains("Compensation steps: credit.release", …)` matches `Compensation steps: credit.released.v1` as a prefix. The entry being closed is titled *"payload sites survive mutation"*; a site that survives mutation, found by its own closing review, is not a close.
2. **Two of id 59's three acceptance bullets were dropped in the implementation, and the stated justification for dropping them is disproved by (1).** The record argues the population is "closed and enumerated by hand" so a sweep would only "re-derive what is already a closed, verified list". The hand enumeration was of *payload date fields*; the population that mattered is *interpolation sites*, and it contains three nested collection-element sites nobody has ever enumerated, one of which is the survivor above. That is exactly the false-zero-from-a-narrowed-population failure bullets 2 and 3 exist to prevent — the same failure that created this entry in the first place.

Everything the bullets *do* name is in good shape, and much of it is excellent work. This is a short fix round, not a rebuild. The measurements below are all off my own runs and are handed over so the round does not have to repeat them.

---

## Method — what I ran, and what I did not

The implementer reports `./quality.sh` at 16 projects / **1201 passed, 0 failed, 0 skipped**. **I did not re-run it in full.** The blast radius is eight test files in one project and zero files under `src/`; re-running fifteen unrelated projects would duplicate cost and prove nothing about the claims under test. I spent the budget on mutation probes instead.

What I ran, all this session, all figures mine:

| Run | Result |
|---|---|
| `dotnet test tests/Notifications.UnitTests` (baseline) | **65/65**, 0 failed, 0 skipped |
| `dotnet test tests/Architecture.Tests` | **16/16** — NetArchTest suite executed, not eyeballed |
| `dotnet build --no-incremental src/Notifications` after every restore | 0 errors |
| `./init.sh` | exit 0; 37/58 done; 1 WARN for 11 uncommitted changes (expected mid-session) |
| `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` | only `test-matrix.md` differs — C7 fidelity intact |
| **14 mutation runs** across five families (below) | 26 mutations applied, 25 caught, **1 survivor** |

Every mutated file was restored from a `cp` backup taken before the first probe (never `git checkout --`), `cmp`'d byte-identical, `touch`ed, rebuilt with `dotnet build --no-incremental`, and re-run green. `NotificationFactsConsumer.cs` SHA-256 before the first probe and after the last restore: `2e63770b555ac391661f08fc874eec22295d1f6fe54c4535416236d50ba9b291`, equal. `git status --porcelain` before and after this review: **identical, 11 entries, same set** — no `.bak`, no scratch file, no throwaway project inside the repository.

---

## The site population, enumerated — 95 sites, 94 caught, 1 survivor

The predecessor's sweep counted **89**. My enumeration reconciles it exactly and then widens it. Commands and complete output:

```
$ grep -rnoE 'payload\.[A-Za-z]+' src/Notifications/Application/Templates --include=*.cs | wc -l
92

$ grep -rnoE '\b(line|step)\.[A-Za-z]+' src/Notifications/Application/Templates --include=*.cs
src/Notifications/Application/Templates/OrderCancelledTemplate.cs:16:step.EventType
src/Notifications/Application/Templates/OrderDespatchedTemplate.cs:14:line.ProductCode
src/Notifications/Application/Templates/OrderDespatchedTemplate.cs:14:line.Units
```

(the `step.EventType` line is the mutated form captured mid-probe; on restored source it reads `step.Step`.)

92 `payload.<Field>` references + 3 nested collection-element references = **95 sites**. The predecessor's 89 = 92 − 3, the three excluded being the collection references themselves (`payload.CompensationSteps` twice, `payload.Lines` once), which a string-substitution sweep cannot type-check. Every one of the 95 is classified below; **zero unclassified lines**.

| Partition | Count | Verdict | Evidence |
|---|---|---|---|
| String `payload.<Field>` | 70 | CAUGHT 70/70 | predecessor's sweep, SHA-256-verified applied per site (`review_notifications_service.md` R2-P7), independently reproducing the feature-23 implementer's own figure. **Not re-run by me** — two independent prior measurements agree and the population is untouched by this feature |
| **Money `payload.<Field>`** | **5** | **CAUGHT 5/5, my run** | see below |
| Date `payload.<Field>` | 14 | CAUGHT 14/14, my two runs | see below |
| Collection `payload.<Field>` | 3 | CAUGHT 3/3, my run | `Take(0)` on `payload.Lines` and `Count >= 0` on `payload.CompensationSteps` each fail a named test |
| Nested element `line.*` | 2 | CAUGHT 2/2, my two runs | `line.Units + 1` and a literal for `line.ProductCode` each fail `OrderDespatchedTemplateTests` on `Not found: "Lines: SKU-1 x10"` |
| **Nested element `step.Step`** | **1** | **SURVIVES — defect D1** | see below |

### The money sites — the population the acceptance names and the record does not measure

```
$ grep -rnE 'payload\.(TotalAmount|Amount)\b' src/Notifications/Application/Templates --include=*.cs
src/Notifications/Application/Templates/OrderPlacedTemplate.cs:14:        var total = FormatMoney(payload.TotalAmount, payload.Currency);
src/Notifications/Application/Templates/InvoiceIssuedTemplate.cs:14:        var total = FormatMoney(payload.TotalAmount, payload.Currency);
src/Notifications/Application/Templates/OrderCompletedTemplate.cs:14:        var total = FormatMoney(payload.TotalAmount, payload.Currency);
src/Notifications/Application/Templates/OrderConfirmedTemplate.cs:14:        var total = FormatMoney(payload.TotalAmount, payload.Currency);
src/Notifications/Application/Templates/PaymentReceivedTemplate.cs:14:        var amount = FormatMoney(payload.Amount, payload.Currency);
```

Five sites, one per money-bearing template, each `long` minor units per the payload records (`OrderPlacedPayload.cs:18`, `OrderConfirmedPayload.cs:12`, `InvoiceIssuedPayload.cs:17`, `OrderCompletedPayload.cs:12`, `PaymentReceivedPayload.cs:12`). Mutation `payload.<field>` → `0L`, all five at once (each lands in a different test class, so the verdicts are independent):

```
Failed … OrderConfirmedTemplateTests.Build_ProducesASubjectCarryingTheOrderReferenceAndTheCorrelationId
Failed … PaymentReceivedTemplateTests.Build_ProducesASubjectCarryingTheInvoiceReferenceAndDerivesTheRecipientFromTheOrderReference
Failed … OrderPlacedTemplateTests.Build_ProducesASubjectCarryingTheOrderReferenceAndTheCorrelationId
Failed … OrderCompletedTemplateTests.Build_ProducesASubjectCarryingTheOrderReferenceAndTheCorrelationId
Failed … InvoiceIssuedTemplateTests.Build_ProducesASubjectCarryingTheInvoiceReferenceAndTheCorrelationId
Failed!  - Failed: 5, Passed: 60, Skipped: 0, Total: 65
```

**5/5 CAUGHT.** So the money population is guarded — the acceptance's named gap turns out to carry no unguarded code. It was still unmeasured by the implementer, and it is measured here.

---

## Defects

### D1 — Blocking — `OrderCancelledTemplate.cs:16` renders `step.Step` and the assertion cannot tell it from `step.EventType`

**File:** `src/Notifications/Application/Templates/OrderCancelledTemplate.cs:16`
**Test:** `tests/Notifications.UnitTests/OrderCancelledTemplateTests.cs:45` / `:52`

```
mutation: payload.CompensationSteps.Select(step => step.Step)
       →  payload.CompensationSteps.Select(step => step.EventType)
result:   Passed!  - Failed: 0, Passed: 65, Skipped: 0, Total: 65
```

The fixture step is `new CompensationStep("credit.release", "credit.released.v1", …)` (`OrderCancelledTemplateTests.cs:33`) and the assertion is `Assert.Contains("Compensation steps: credit.release", …)`. `"credit.released.v1"` **contains** `"credit.release"`, so the wrong field renders and the substring assertion is satisfied. Both renderings — text `:26` and HTML `:37` — are affected; the whole cancellation email would list event types where it should list compensation step names.

**Why it matters beyond the cosmetics.** It is a fixture-value collision of exactly the class this feature was created to remove, one level in: not two *fields* holding the same value, but one field's value being a **prefix** of another's, which defeats `Assert.Contains` just as thoroughly as equality defeats `Assert.Equal`. The feature found and fixed two collisions of the equality shape and did not look for the prefix shape.

**Cheapest sufficient fix:** assert the full rendered line rather than a prefix of it (`"Compensation steps: credit.release\n"` in the text body, `"Compensation steps: credit.release</li>"` in the HTML), or give the fixture a `Step` that is not a prefix of its `EventType`. Then arm it: the `step.Step` → `step.EventType` substitution above must go red.

### D2 — Blocking — id 59's bullets 2 and 3 were dropped in the implementation, against the leader's own written plan, and the justification does not survive D1

Id 59's acceptance, verbatim from `feature_list.json`:

- bullet 1 — *"each of the 14 date-typed sites either fails a named test under mutation, or is classified as a field no requirement constrains, with the reason given per site"* → **MET**, and verified independently below.
- bullet 2 — *"the sweep that measures this covers date and money sites as well as string sites"* → **NOT MET by the implementation.** No sweep exists, and the record contains **no measurement of the money population at all** — neither a count nor a classification. The five money sites are measured for the first time in this document.
- bullet 3 — *"the sweep is proved with sentinels before its result is believed: a mutation that must be caught, one that must survive, and one that must report not-applied"* → **NOT MET as written**, and only partly met in substance. The record's substitute argument is that with all 14 mutations red, "caught" and "applied" are the same evidence, which is sound; but its "must survive" sentinel is *the previous review's pre-feature finding*, i.e. a survivor of code that no longer exists, not a control run in this round.

**Ruling on the deviation the leader asked me to adjudicate — plainly: it is a narrowing, not a superset, and it should have gone back to the leader rather than been decided in the implementation.**

- It is **not a superset**. Twenty hand-armed guards over date sites and envelope fields do not touch the five money sites; the bullet names money explicitly and the record is silent on it.
- The narrowing was **disclosed**, which is to its credit and is why this round is cheap — but `progress/current.md:8`, written by the leader before the work started, states *"id 59's sweep is the instrument that proves id 58 — its acceptance already requires the sweep to cover string, date and money sites"*. Replacing the instrument the session plan named is a scope decision, and the standing convention here is that a gate-approved or leader-planned task list outranks the implementer's judgement about it. The right move was one sentence back to the leader, not a section titled *"What was not done, and why"* in the submission.
- And the substantive argument for the narrowing — *"the population is closed and fully enumerated by hand, a script would only re-derive it"* — **is disproved by D1**. The hand enumeration was of *payload date fields* (a closed set of seven), not of *interpolation sites* (95, of which 3 were never in anyone's population and 1 is unguarded). A wider instrument is exactly what finds the sites a narrower enumeration cannot name.

I record for balance that a faithful rebuild of the predecessor's own 89-site sweep would **also** have missed `step.Step`, since it enumerated `payload.<Field>` only. So D1 is not proof that the specific sweep would have caught it — it is proof that the *reasoning* used to skip the sweep (the population is closed) was wrong about which population was at risk.

**The standing instruction settles the rest.** *Stop leaving issues to the next phase, fix them.* D1 is a two-line fix and one armed run. Deferring it to a new backlog entry so that phase 12 can close on schedule is precisely the trade this repository has ruled against, and the disclosure of D2 makes the shortfall cheap to fix, not discharged.

### D3 — Low, non-blocking — `progress/current.md:4` disagrees with the backlog again

`progress/current.md:4` reads `**Status:** in_progress — driving one loop…` while `feature_list.json` has id 59 at `in_review`. This is the third consecutive appearance of the same nit (feature 23's R2-D7, feature 24's A9). Leader-owned bookkeeping; `init.sh` cannot see it, by its own honest admission that it reads the `**Feature:**` line only.

### D4 — Low, non-blocking, out of scope — the same fixture collision is latent in two other services

Enumerated, not swept in prose:

```
$ grep -rn "BuildMessage(" --include=*.cs tests/ src/ | grep -v "/bin/\|/obj/"
```
(complete output read; the 27 hits split into six file-private helpers). The two that share the fixed shape:

- `tests/Orders.UnitTests/SagaFactsConsumerTests.cs:106` — `new Envelope<object>(Guid.NewGuid(), eventType, correlationId, correlationId, …)`
- `tests/Projector.UnitTests/ProjectorFactsConsumerTests.cs:130` — identical line

Both carry the `AggregateId == CorrelationId` collision this feature removed from Notifications. It is **latent rather than live**: the hazard needs a hand-written envelope copy to bite, and there is exactly one in the solution —

```
$ grep -rn "new Envelope<TPayload>(\|new Envelope<T>(" --include=*.cs src/ | grep -v "/bin/\|/obj/"
src/Notifications/Presentation/NotificationFactsConsumer.cs:147:        return new Envelope<TPayload>(
```

— so neither consumer can currently transpose the two fields. The trap is for the next assertion written against those fixtures. Recommend a backlog entry; not this feature's to fix.

### D5 — Informational — one fixture in the changed set is still unbracketed

`tests/Notifications.UnitTests/OrderCancelledTemplateTests.cs:64-65` — the second test (`Build_RendersNoCompensationStepsAsNone`) still sets payload `CancelledAt` and envelope `OccurredAt` to the same literal `2026-09-01T15:00:00Z`. Harmless today, because the date site is guarded by the *first* test whose fixture was bracketed. Worth bracketing for consistency while the file is open, since it is the exact collision the feature exists to remove.

---

## What I verified that the record claims — the arming table, re-run rather than accepted

**I re-armed all 20 guards the record tables, plus 6 more it does not.** 14 mutation runs, 26 mutations.

### Id 58 — all six envelope fields, my own mutations

| Field | My mutation | Result | Verbatim |
|---|---|---|---|
| `AggregateId` + `CorrelationId` | **transposed** — the positional four-`Guid` constructor argument swap the leader asked for | **7/7 theory cases FAIL** | `Assert.Equal() Failure: Values differ / Expected: 8f41450e-… / Actual: 4870bc7c-…` |
| `EventId` | → `Guid.Empty` | FAIL | `Expected: 8a235462-… / Actual: 00000000-0000-0000-0000-000000000000` |
| `EventType` | → `"corrupted.event.type"` | FAIL | `Expected: order.despatched.v1 / Actual: corrupted.event.type` |
| `CausationId` | → `Guid.Empty` | FAIL | `Expected: 30f01187-… / Actual: 00000000-…` |
| `OccurredAt` | → `.AddDays(1)` | FAIL | `Expected: 2026-01-02T03:04:05.6780000+00:00 / Actual: 2026-01-03T03:04:05.6780000+00:00` |

The failing test is `NotificationFactsConsumerTests.EachOfTheSevenNotifiedFacts_DispatchedEnvelopeCopiesEveryFieldFromTheSource` in every case. Id 58's bullet 1 (corrupt the `correlationId` copy) and bullet 2 (every field the copy touches) are **met**; bullet 3 (the report names each field, its mutation, its verbatim failure) is **met** by the record and reproduced here.

**The `Payload` exclusion is justified, and I checked it rather than accepting it.** `ToEnvelope`'s seventh argument is a real `JsonSerializer.Deserialize`, not a positional copy, and it is guarded on the real path: `tests/Notifications.IntegrationTests/NotificationConsumptionTests.cs:44` asserts the sent subject contains `ORD-CONSUME-1`, a value that can only reach the subject through that deserialisation, against real Kafka and real MS-SQL.

### The fixture-collision finding — real, and load-bearing in both directions

The record claims two pre-existing collisions made provenance unprovable. Both claims are true, and I proved the fixes are load-bearing rather than cosmetic by **re-introducing each collision while the production mutation was still in place**:

| Claim | Old fixture | Probe | Result |
|---|---|---|---|
| `AggregateId == CorrelationId` in `BuildMessage` | `new Envelope<object>(Guid.NewGuid(), eventType, correlationId, correlationId, …)` (old `:105`) | production transposition **left in place**, test's `aggregateId` set back to `= correlationId` | **Passed! 65/65** — the transposition becomes invisible |
| `payload.<Date> == envelope.OccurredAt` in all seven template fixtures | e.g. `PaymentReceivedTemplateTests` had both at `2026-09-01T13:00:00Z` | all seven text sites mutated `payload.<Date>` → `envelope.OccurredAt`, then `_confirmedAt` set back to the envelope's `2026-09-01T10:05:00Z` in one file | **6 failed, not 7** — `OrderConfirmedTemplateTests` goes green under a live wrong-field mutation |

Without the fixture change the new assertions would have been decorative. That is a genuine find of the class `CLAUDE.md` names, and it is the best work in this submission.

**No other assertion was weakened.** This is a search, not a reading: `BuildMessage` in `NotificationFactsConsumerTests` is file-private with four callers, all in that file (enumeration in D4); the seven template `_<date>` constants are file-private with one caller each; and every old literal is still accounted for (`2026-09-01T1[0-5]:00:00Z` now appears only as the envelope `OccurredAt` it always was, plus the doc comments that name it). Suite green at 65/65 confirms the three `BuildMessage` callers that use the defaults are unaffected.

### Id 59 — the 14 date sites, enumerated then re-armed

The count is a **run, not an assertion**:

```
$ grep -rnE 'payload\.(OrderDate|ConfirmedAt|DespatchDate|InvoiceDate|ValueDate|CompletedAt|CancelledAt)\b' src/Notifications/Application/Templates --include=*.cs
```
14 hits, exactly the file:line pairs the record tables — 7 text (`{payload.X:O}`) and 7 HTML (`EscapeHtml(payload.X.ToString("O"))`). Seven payloads × one `DateTimeOffset` each × two renderings.

Two runs, one per rendering, so each of the 14 is separately answered:

- **7 text sites** → `DateTimeOffset.UnixEpoch`: **7/7 FAIL**, e.g. `Not found: "Despatch date: 2026-08-27T13:25:50.000000"`, `Not found: "Order date: 2026-08-25T09:15:30.0000000+0"` — matching the record's fragments exactly.
- **7 HTML sites** → `DateTimeOffset.UnixEpoch`: **7/7 FAIL**, with the text assertion passing in the same run, which is what proves the two sites are independently guarded rather than jointly.
- **7 text sites** → `envelope.OccurredAt` (the wrong-field family, which only bites because of the bracketing): **7/7 FAIL**.

**14/14 caught, by me, in three runs.** Bullet 1 of id 59 is met.

### Bullet 3's sentinels, supplied by this review

Since the round did not run them, I did:

- **Must be caught** — 25 of 26 mutations red, tabulated above.
- **Must survive** — the `OrderConfirmedTemplateTests` case in the collision-restored run: mutation demonstrably applied (the same script pass turned the other six files red), test green. A control that can report a survivor.
- **Must report not-applied** — every mutation I applied was a Python `str.replace` guarded by `assert <pattern> in source` (and `count(...) == 1` where ambiguity was possible), so a pattern that no longer matches raises rather than silently producing a green "survivor". Each run also printed the files it rewrote.

---

## `CHECKPOINTS.md` walk

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`).
- [x] Every agent definition declares its model or states it inherits.
- [x] `./init.sh` exits 0 — run by me this session.

### C2 — state is coherent
- [x] At most one feature `in_progress` — zero at review time (id 59 was `in_review`).
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [ ] **`progress/current.md` describes the active session** — the body does, the `**Status:**` line does not (**D3**). Third recurrence; left unticked this time rather than ticked-with-a-nit, because ticking it twice has not caused it to be fixed.
- [x] Every `blocked` feature records why — none blocked.

### C3 — architecture is respected
- [x] No forbidden framework reference in any `Domain/` folder — `Architecture.Tests` **16/16, run by me**, not eyeballed.
- [x] No cross-service database access — no file under `src/` changed by this feature.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs`.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — inside the green 16.
- [x] `src/SharedKernel` still has zero `PackageReference` entries.
- [x] No `decimal` in domain arithmetic — the five money sites all route through `NotificationFormat.FormatMoney(long, string)`, integer division and modulo; my `0L` probe confirms the parameter type.
- [x] Every interaction Kafka-fact or NATS-RPC — unchanged; three Kafka fact topics consumed, zero RPC, zero producer.
- [x] No stray debug logging, no context-free TODOs.

### C4 — verification is real
- [x] `./quality.sh` passes — implementer's run, 16 projects, 1201/0/0. **Deliberately not re-run in full**; I ran `Notifications.UnitTests` 65/65 and `Architecture.Tests` 16/16, the two suites whose claims were at risk.
- [x] Domain tests are pure.
- [x] Integration tests use Testcontainers — untouched by this feature.
- [ ] Coverage thresholds — the gate is inert (finding A7, feature 34's, disclosed and open). Unticked, as at the last three closes.
- [x] No Jest anywhere.

### C5 — the session closed cleanly
- [x] No suspicious untracked files — `git status --porcelain` identical before and after my probes, 11 entries.
- [ ] `progress/history.md` entry with effort record — **not written**, correctly: rejection, so no close.
- [ ] `feature_list.json` reflects true state — id 59 returned to `in_progress` by this review; id 58 stays `pending`.
- [x] The human has been told what was done and how to test it — this file.
- [x] Claude did not commit.

### C6 — spec-driven development
Not applicable. Both entries are `"sdd": false`; the specification of record is `feature_list.json`'s acceptance bullets, read verbatim before anything else.

### C7 — spec-reuse fidelity
- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — real `diff -rq` against the #7 checkout, run by me.
- [x] No silent fork — nothing under `specs/` touched by this feature.
- [x] `R<n>` ids are #7's — not touched.
- [ ] n8n workflows fire green — not exercised this session, and not at risk (no service behaviour changed).
- [ ] Black-box API script — same.
- [x] `progress/history.md` effort records complete and honest — no entry due for a rejection.
- [x] README benchmark section — untouched.

---

## Acceptance-bullet ledger

| Entry | Bullet | Verdict |
|---|---|---|
| **58** | corrupting the `correlationId` copy makes a named test fail | **MET** — reproduced by me via the positional transposition |
| **58** | the same holds for every field the copy touches | **MET** — all six re-armed by me; `Payload` correctly excluded and its guard verified on the integration path |
| **58** | armed: report names each field, mutation, verbatim failure | **MET** — record's table checked against my own runs |
| **59** | each of the 14 date sites fails a named test under mutation, or is classified | **MET** — 14/14, my own three runs, enumeration by command |
| **59** | the sweep covers date **and money** sites as well as string sites | **NOT MET** — no sweep; money population unmeasured by the implementation (**D2**). Measured here at 5/5 |
| **59** | the sweep is proved with sentinels before its result is believed | **NOT MET as written**; substance partly met, "must survive" inherited from a previous round rather than run (**D2**). Supplied here |

---

## What must change before re-review

1. **Close D1.** Guard `OrderCancelledTemplate.cs:16`. The mutation `step.Step` → `step.EventType` must turn `OrderCancelledTemplateTests` red; arm it and record the verbatim failure. A prefix-safe assertion or a fixture whose `Step` is not a prefix of its `EventType` both work — the second also removes the collision rather than working around it, which is the shape this feature has been using.
2. **Widen the measured population and report it as a search result.** Every interpolation site in the seven templates, **including nested collection-element fields**, with the enumerating command, its complete output, and one classification line per hit. My enumeration is 95 sites / 94 caught / 1 survivor and is handed over above — reproduce it or correct it, do not re-derive it from scratch. The money partition is 5/5 and needs no re-arming; say so and move on.
3. **Run bullet 3's three sentinels in this round**, not by citation of a previous one: one mutation that must be caught, one control that must survive (an unconstrained field — `CompensationStep.OccurredAt` is never rendered and is the obvious candidate), and one applied-check that would report not-applied.
4. **Close D5** while the file is open — one literal.
5. **If, having read the above, the implementer still judges a committed sweep script unnecessary, that is a question for the leader before the round starts, with this document as the evidence.** It is a defensible position — the argument that all-red implies all-applied is sound, and 95 hand-armed sites is genuinely stronger than a tool nobody has to trust. It is not a position an implementation may adopt on its own against the acceptance and the session plan.

Not required: any change under `src/`. The production code was correct before this feature and is correct now; D1 is a test defect, not a behaviour defect. `NotificationFactsConsumer.cs` and all seven templates are byte-identical to their pre-review state.

## Bookkeeping done by this review

- `feature_list.json` — id 59 `in_review` → `in_progress` (one line). Id 58 unchanged at `pending`.
- No `progress/history.md` entry, no effort record, no phase-12 closing assessment — phase 12 stays open.

---

# Round 2 — backlog ids 59 (drives) and 58 (rides)

**Verdict: APPROVED.** Both entries closed `done`; phase 12 closes with them.

Round 1 above is closed and is not amended, retracted or reopened by anything below. Its two blocking defects are both closed, verified by my own runs rather than by reading the record: **D1** is closed at the root (the fixture collision was removed, not worked around) and **D2** is closed by a committed instrument whose enumeration I reproduced verbatim and whose three verdict branches I exercised myself, including the one the round could not exercise. Every figure below is off my own run this session.

## Method — what I ran, and what I did not

| Run | Result |
|---|---|
| `dotnet test tests/Notifications.UnitTests` (baseline, then after every restore) | **65/65**, 0 failed, 0 skipped, four times |
| `./quality.sh` — **re-run in full this round, unlike round 1** | exit 0, **16 projects, 1201 passed, 0 failed, 0 skipped** — identical to the implementer's figure. Run in full because approving closes a phase, so `CHECKPOINTS.md` C4's box is a claim about the whole suite |
| `dotnet test tests/Architecture.Tests` | **16/16** — NetArchTest executed, not eyeballed |
| `./init.sh` before the close | exit 0, 37/58 done, one expected WARN |
| `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared` | only `test-matrix.md` differs — C7 fidelity intact |
| `scripts/notification-template-payload-sweep.sh --enumerate` | 95 sites, `string=70 money=5 date=14 collection=3 nested=3` |
| **6 probes through the delivered tool + 2 by hand + 1 fixture-containment enumeration** | 1 CAUGHT-by-hand, 4 CAUGHT-by-tool, **1 SURVIVED-by-tool**, 2 NOT-APPLIED-by-tool |

Five slow container-backed suites inside `quality.sh` were not otherwise re-armed; round 1's mutation table stands and nothing under `src/` moved. Where I did not re-run, I say so on the line.

## D1 — closed, at the root, verified by me

The fix changed the **fixture**, not the production line: `OrderCancelledTemplateTests.cs:39` now reads `new CompensationStep("warehouse.release", "credit.released.v1", …)`. That removes the collision rather than working around it, which is what round 1 asked for and the shape the rest of the feature used.

My own arming, protocol in full (backup, `sed`, `touch`, `dotnet build --no-incremental`, run, restore from backup, `cmp`, `touch`, forced rebuild, confirm green):

```
$ sed -i '16s/step\.Step/step.EventType/' src/Notifications/Application/Templates/OrderCancelledTemplate.cs
    OrderToCash.Notifications.UnitTests.OrderCancelledTemplateTests.Build_ProducesASubjectCarryingTheOrderReferenceAndTheReason [FAIL]
      Assert.Contains() Failure: Sub-string not found
      Not found: "Compensation steps: warehouse.release"
```

Restored, `cmp` IDENTICAL, line 16 read back as `: string.Join(", ", payload.CompensationSteps.Select(step => step.Step));`, forced rebuild, **65/65**. `OrderCancelledTemplate.cs` SHA-256 before the first probe and after the last restore: `a68a3b207b09f32df62ea368dad2bcdcc829d63cf31637295baff13c67ddd0cc`, equal.

**And the prefix relationship has not moved elsewhere — this is a search, not a reading.** Round 1's defect was a *fixture-value containment*: one field's value being a substring of another's, which defeats `Assert.Contains` exactly as equality defeats `Assert.Equal`. I enumerated every such pair across all seven template fixtures, comparing only constructor-argument literals (assertion strings excluded, since a fixture value being contained in its own expected line is the normal case and would drown the signal):

```
$ python3 - <<'PY'   # all "..." literals on non-Assert lines, comments stripped, matching ^[A-Za-z0-9._-]+$
   ... pairwise containment over each file's literal set ...
PY
== InvoiceIssuedTemplateTests.cs    ['2222…225','COMP01','CarrefourEs','INV-000001','ORD-000001','SKU-1','USD','invoice.issued.v1']      no fixture-value containment
== OrderCancelledTemplateTests.cs   ['2222…228','COMP01','CarrefourEs','ORD-000001','credit.released.v1','operator_cancelled','order.cancelled.v1','stock_rejected','warehouse.release']   no fixture-value containment
== OrderCompletedTemplateTests.cs   ['2222…227','COMP01','CarrefourEs','ORD-000001','USD','order.completed.v1']                          no fixture-value containment
== OrderConfirmedTemplateTests.cs   ['2222…223','COMP01','CarrefourEs','ORD-000001','USD','order.confirmed.v1']                          no fixture-value containment
== OrderDespatchedTemplateTests.cs  ['2222…224','COMP01','CarrefourEs','DES-000001','ORD-000001','SKU-1','order.despatched.v1']          no fixture-value containment
== OrderPlacedTemplateTests.cs      ['1234567890123','2222…222','9876543210987','COMP01','CarrefourEs','ORD-000001','SKU-1','USD','Widget','order.placed.v1']   no fixture-value containment
== PaymentReceivedTemplateTests.cs  ['2222…226','CR-000001','INV-000001','ORD-000001','USD','bank_transfer','payment.received.v1']       no fixture-value containment
```

**Seven files, zero containment pairs, zero unclassified lines.** `"warehouse.release"` is not a prefix of `"credit.released.v1"` in either direction, and no other pair anywhere in the seven fixtures has the relationship either. The D1 *class* is now closed population-wide, not just at the one site — which is more than round 1 asked for and is the check I would want #9 to inherit.

The one repeated-literal hit the same enumeration turned up is recorded below as **A14**; it is latent, not live.

## The tool, probed adversarially — six probes, and the falsifying case the round could not supply

### The enumeration is genuinely live, and reproduces mine exactly

My own commands, independent of the script:

```
$ find src/Notifications/Application/Templates -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -noE 'payload\.[A-Za-z]+' | wc -l
92
$ find src/Notifications/Application/Templates -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -noE '\b(line|step)\.[A-Za-z]+'
src/Notifications/Application/Templates/OrderDespatchedTemplate.cs:14:line.ProductCode
src/Notifications/Application/Templates/OrderDespatchedTemplate.cs:14:line.Units
src/Notifications/Application/Templates/OrderCancelledTemplate.cs:16:step.Step
```

92 + 3 = **95**. And `diff` of the tool's `--enumerate` output against the 95 classification lines pasted in `progress/impl_notifications_mutation_gaps.md` is **empty but for the closing markdown fence** — so the record's output was pasted, not retyped, which is the discipline `CLAUDE.md` names after feature 24's A12. **Same 95 sites as round 1, not the same total by coincidence:** same three nested-element hits, same file:line pairs, same partition split.

I also enumerated one class round 1 did not name: `{envelope.<Field>}` interpolations inside the templates (seven `envelope.CorrelationId` subject/footer sites). They are outside the *payload*-site population by definition and are guarded by id 58's own consumer test plus the seven `Assert.Contains("(correlationId: …)")` subject assertions. Classified, not left as an unclassified line.

### Path exclusion is at the source, in the code

`enumerate()` (`scripts/notification-template-payload-sweep.sh:111-116`) is `find … -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -noE …`. `grep -n "grep -v\|grep -rn\|--include" scripts/notification-template-payload-sweep.sh` returns **exactly one hit, line 109, inside a comment explaining why the post-filter form is forbidden**. No post-filtering anywhere in the executable path. Checked in the code, as asked, not in the report.

### `--probe` genuinely restores

```
$ sha256sum src/Notifications/Application/Templates/OrderDespatchedTemplate.cs
ec284d9e…  (before)
$ scripts/notification-template-payload-sweep.sh --probe 94
site 94: OrderDespatchedTemplate.cs:14  line.ProductCode
CAUGHT                                            (1m40s)
$ sha256sum …/OrderDespatchedTemplate.cs
ec284d9e…  (after)   — and `cmp` against my own pre-review backup: byte-identical
```

At the end of this review, `sha256sum -c` over **all eight** files in `Templates/` returns `OK` on every one, and `NotificationFactsConsumer.cs` hashes to `2e63770b…`, the same value round 1 recorded. `git status --porcelain` before and after this review: identical, 13 entries, same set. No `.bak`, no scratch file, no throwaway project inside the repository.

### Would it report a false CAUGHT if the mutation silently failed to apply? No — and the reason is structural, plus one empirical probe

Three independent barriers stand between "the mutation did not take" and a `CAUGHT`, read off the code:

1. `probe_site:189-193` — the mutation text must occur **exactly once on the named line** (`sed -n "${line}p" | grep -oF | wc -l`), else `NOT-APPLIED` before anything is touched.
2. `probe_site:210-214` — after `sed`, `cmp -s "$bak" "$file"`; if the file did not change, `NOT-APPLIED`. This is the barrier that actually catches a silently-failed substitution.
3. `probe_site:228-233` — the **restored** suite must produce a `Passed!` line, or the probe aborts with `*** RESTORE DID NOT TAKE ***` and returns non-zero. `CAUGHT` is emitted **after** that check, so a suite that is red for any reason other than the mutation cannot yield a false `CAUGHT` — it yields the abort instead. That makes the restore check double as a baseline check, which is better than it was asked to be.

I exercised barrier 1 empirically rather than trusting the reading, and without editing the script (the implementer's own demonstration required corrupting the `OVERRIDES` table, which is a probe of the script rather than through it). I made `line.Units` occur twice on the enumerated line and re-ran:

```
$ sed -i '14s/x{line.Units}/x{line.Units}{line.Units}/' src/Notifications/Application/Templates/OrderDespatchedTemplate.cs
$ scripts/notification-template-payload-sweep.sh --enumerate | tail -1
string=70 money=5 date=14 collection=3 nested=4 total=96          <-- the population moved, live, with the source
$ scripts/notification-template-payload-sweep.sh --probe 96
site 96: OrderDespatchedTemplate.cs:14  line.Units
NOT-APPLIED  (expected exactly 1 occurrence of 'line.Units' on OrderDespatchedTemplate.cs:14, found 2)
```

Restored from backup, `cmp` IDENTICAL, line 14 read back. This also settles a second question in one run: the enumeration is **not a cached list** — it re-derived 96 from a source change I had just made.

Out-of-range probes (`--probe 999`, `--probe 0`) both report `NOT-APPLIED` rather than indexing off the end.

### The must-SURVIVE sentinel — the round's weakest link, and I closed it

The leader's question was the right one. `CompensationStep.OccurredAt` **has no site in the population**, so the implementer's hand demonstration (append `{step.OccurredAt:O}` to line 16 and watch 65/65 stay green) proves that *the suite* does not constrain that field — it does **not** prove that *the tool* can report a survivor. The round therefore shipped an instrument with a clean sheet and no falsifying case, which is precisely the shape round 1's bullet 3 exists to prevent. That the candidate field was named by round 1, by me, and that the implementer disclosed the limitation accurately rather than papering over it, is why this is an advisory and not a third rejection.

I supplied the falsifying case, in-population, through the tool's own machinery. I restored round 1's D1 collision in the fixture (`"warehouse.release"` → `"credit.release"` in the `CompensationStep`, both assertions moved back) and re-probed the site the tool was built to reach:

```
$ scripts/notification-template-payload-sweep.sh --probe 93
site 93: OrderCancelledTemplate.cs:16  step.Step
SURVIVED
```

Test fixture restored from backup, `cmp` IDENTICAL, and the same probe re-run on the delivered fixture:

```
$ scripts/notification-template-payload-sweep.sh --probe 93
site 93: OrderCancelledTemplate.cs:16  step.Step
CAUGHT
```

**So the instrument has now been seen to fail, on a real site in its own population, against the exact defect that caused round 1's rejection — and to pass on the fix.** That is the strongest statement available about this tool, and it is the one the record was missing. All three verdict branches (`CAUGHT`, `SURVIVED`, `NOT-APPLIED`) are now exercised by someone other than their author.

### One more site, spot-checked because the reading suggested it was the weakest

Site 14 (`InvoiceIssuedTemplate.cs:38`, `payload.RetailerCode`) is the third occurrence of the same field in one template and feeds `RecipientFor(...)` rather than a body line — structurally the most plausible place for a `Contains` assertion elsewhere in the file to mask a mutation. `--probe 14` → **CAUGHT**. Reading the assertions confirms why: all seven recipient sites are guarded by `Assert.Equal(…, message.To)`, not `Assert.Contains`. The 70-site string partition remains **inherited** from two independent prior measurements, as in round 1; no template source changed, so re-measuring it would be re-running the world.

## Whether the fix round disturbed anything

**It did not, and this is a search rather than a reading.** `progress/review_notifications_mutation_gaps.md` was written at `14:47:47`. Of the eight changed files under `tests/Notifications.UnitTests/`, seven carry mtimes at or before `14:38:17` — i.e. round-1-era — and exactly one, `OrderCancelledTemplateTests.cs`, was rewritten afterwards. `git status --porcelain -- src/` is empty and every file under `src/Notifications/` hashes to its pre-review value. So the fix round's blast radius is **one file**, and no round-1 guard in the other seven could have been weakened.

I re-armed id 58's flagship guard anyway, because I am closing id 58 on it:

```
transposition: envelope.AggregateId / envelope.CorrelationId swapped in ToEnvelope (NotificationFactsConsumer.cs:150-151)
    NotificationFactsConsumerTests.EachOfTheSevenNotifiedFacts_DispatchedEnvelopeCopiesEveryFieldFromTheSource
      (eventType: "order.despatched.v1" … and six more)  [FAIL]
      Assert.Equal() Failure: Values differ
```

All seven theory cases fail; restored, `cmp` IDENTICAL, hash `2e63770b…` unchanged, forced rebuild, **65/65**.

**D5 is closed.** `OrderCancelledTemplateTests.cs:75` now feeds the payload `_cancelledAt` (`2026-08-31T21:50:35Z`) against the envelope's `2026-09-01T15:00:00Z`. Read on disk.

## Acceptance-bullet ledger, final

| Entry | Bullet | Round 1 | Round 2 |
|---|---|---|---|
| **58** | corrupting the `correlationId` copy makes a named test fail | MET | **MET** — re-armed by me this round via the positional transposition |
| **58** | the same holds for every field the copy touches | MET | **MET** — six fields, round 1; file byte-identical since |
| **58** | armed: report names each field, mutation, verbatim failure | MET | **MET** |
| **59** | each of the 14 date sites fails a named test under mutation, or is classified | MET | **MET** — 14/14, round 1, three runs |
| **59** | the sweep covers date **and money** sites as well as string sites | NOT MET | **MET** — `scripts/notification-template-payload-sweep.sh`, one type-safe generic rule per family (`"MUTATED-…"` / `DateTimeOffset.UnixEpoch` / `0L`) plus six hand-specified `OVERRIDES` for the sites a token swap cannot reach; enumeration reproduced verbatim by me; sites from four of the six families probed through it this round |
| **59** | the sweep is proved with sentinels before its result is believed | NOT MET | **MET** — CAUGHT and NOT-APPLIED through the tool by the round and re-armed independently by me; SURVIVED supplied in-population by this review (**A13**) |

## Findings at approval — none blocking

- **A13 — the round's must-SURVIVE sentinel was outside the tool's own population.** `progress/impl_notifications_mutation_gaps.md` §3 demonstrates it by hand on `CompensationStep.OccurredAt`, correctly noting the field "has no site in the population by construction" — which is exactly why it cannot falsify the tool. Closed by this review's in-population `--probe 93` → `SURVIVED`. Recorded because the *lesson* generalises past this feature: **a must-survive control that is not in the instrument's population is not a control for the instrument.** Pick a real site and break its guard, not a field the instrument was never going to reach.
- **A14 — one residual fixture collision, latent.** `tests/Notifications.UnitTests/OrderCancelledTemplateTests.cs:26` and `:39` both use the literal `2026-09-01T15:00:00Z`, so the envelope's `OccurredAt` and the `CompensationStep`'s `OccurredAt` are indistinguishable. Harmless today — nothing renders `step.OccurredAt`, which is precisely why the round chose it as its control — but if that field is ever rendered, the fixture cannot prove provenance, and the guard written for it would be decorative on arrival. One literal. Found by the repeated-literal arm of my containment enumeration.
- **A15 — `--all` has never been run end to end, and it does not stop on a failed restore.** `cmd_all:262` captures the probe's verdict with `local result="$(probe_site …)"`; `local` masks the exit status, so `probe_site`'s `return 1` on `*** RESTORE DID NOT TAKE ***` does not abort the sweep. Not a correctness hazard — `CAUGHT` is emitted downstream of the restore check, so subsequent probes on a broken tree report `NOT-APPLIED`, never a false `CAUGHT` — but the sweep would burn ~100 s per remaining site to say nothing. At ~1 m 40 s per probe measured here, a full `--all` is ≈2 h 40 m; that is a deliberate, disclosed cost and I am not requiring it, but whoever first runs it should add `|| true` handling that fails fast.
- **A16 — `enumerate()` depends on `grep` printing a filename prefix.** It does so only because the directory holds more than one `.cs` file; reduced to one, `grep -n` emits `lineno:match` and the `IFS=: read -r file line token` parse at `cmd_enumerate:121` would silently mis-assign every field. `grep -H` removes the dependency. Latent, one flag.
- **A17 — `progress/current.md:4` disagrees with the backlog, for the fourth consecutive feature** (feature 23 R2-D7, feature 24 A9, this feature's round-1 D3, now again). It is leader-owned and outside a reviewer's remit to fix, but this time it is not cosmetic: with ids 58 and 59 closed, **`./init.sh` now FAILS** — `progress/current.md claims a feature while none is active`. The leader must reset the file before the session advances. Ticking this box twice and unticking it twice has not changed the outcome; the durable fix is for `current.md`'s `**Feature:**` line to be part of the same edit that transitions the backlog, not a separate step at session close.
- **A7 carried forward** — the coverage gate remains inert (per-report `line-rate`, no aggregate threshold), feature 34's finding, correctly disclosed. `CHECKPOINTS.md` C4's coverage box stays unticked.

## `CHECKPOINTS.md` walk — round 2

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`).
- [x] Every agent definition declares its model or states it inherits.
- [x] `./init.sh` exits 0 — run by me **before** the close. After the close it FAILS on §4 only; see **A17**.

### C2 — state is coherent
- [x] At most one feature `in_progress` — zero; id 59 was `in_review`, both are now `done`.
- [x] Every status is in `rules.valid_status` — `init.sh` §3 OK after the close.
- [x] Every `done` feature has passing tests associated with it — `quality.sh` 1201/0/0, my run.
- [ ] **`progress/current.md` describes the active session** — **A17**, now a hard `init.sh` FAIL. Left unticked for the second consecutive round.
- [x] Every `blocked` feature records why — none blocked.

### C3 — architecture is respected
- [x] No forbidden framework reference in any `Domain/` folder — `Architecture.Tests` **16/16, run by me**.
- [x] No cross-service database access — nothing under `src/` changed by either round.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs`.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — inside the green 16.
- [x] `src/SharedKernel` still has zero `PackageReference` entries.
- [x] No `decimal` in domain arithmetic — the five money sites route through `NotificationFormat.FormatMoney(long, string)`; round 1's `0L` probe confirms the parameter type and the tool's money rule reuses it.
- [x] Every interaction Kafka-fact or NATS-RPC — unchanged; three Kafka fact topics consumed, zero RPC, zero producer.
- [x] No stray debug logging, no context-free TODOs — the new script's only comments are load-bearing explanations, including the `|| true`.

### C4 — verification is real
- [x] `./quality.sh` passes — **my own run this round**: exit 0, 16 projects, **1201 passed, 0 failed, 0 skipped**.
- [x] Domain tests are pure.
- [x] Integration tests use Testcontainers — `Notifications.IntegrationTests` inside the green 16; the `Payload` field's guard runs against real Kafka and real MS-SQL (round 1 §id 58).
- [ ] Coverage thresholds — **A7**, the gate is inert. Unticked, as at the last four closes.
- [x] No Jest anywhere.

### C5 — the session closed cleanly
- [x] No suspicious untracked files — `git status --porcelain` identical before and after my probes, 13 entries; the only new tracked-worthy artefact is `scripts/notification-template-payload-sweep.sh`, which is the deliverable.
- [x] `progress/history.md` entry with effort record — written by this review, one entry for the joint loop, carrying the phase-12 closing assessment.
- [x] `feature_list.json` reflects true state — ids 58 and 59 → `done`, **two single-line `sed` edits**; `git diff` shows exactly those two lines plus the leader's own pre-existing id-60 addition, and nothing else. No `git checkout --`, at any point, on any file.
- [x] The human has been told what was done and how to test it — this file. Manual check: `scripts/notification-template-payload-sweep.sh --enumerate` (expect 95), then `--probe 93` (expect `CAUGHT`), then `dotnet test tests/Notifications.UnitTests` (expect 65/65).
- [x] Claude did not commit.

### C6 — spec-driven development
Not applicable. Both entries are `"sdd": false`; the specification of record is `feature_list.json`'s acceptance bullets, read verbatim in both rounds.

### C7 — spec-reuse fidelity
- [x] `specs/shared/` byte-identical to #7's except `test-matrix.md` — real `diff -rq` against the #7 checkout, run by me this round.
- [x] No silent fork — nothing under `specs/` touched by either round.
- [x] `R<n>` ids are #7's — not touched.
- [ ] n8n workflows fire green — not exercised, and not at risk: no service behaviour changed in either round.
- [ ] Black-box API script — same.
- [x] `progress/history.md` effort records complete and honest — one joint entry appended, with the phase-12 closing assessment.
- [x] README benchmark section — untouched; the phase-12 numbers land in `history.md` and are the leader's to promote at wrap-up.

## Bookkeeping done by this review

- `feature_list.json` — id 58 `pending` → `done` (line 798), id 59 `in_review` → `done` (line 812). Two `sed` edits on two lines; diff read before moving on.
- `progress/history.md` — one effort record for the joint loop, carrying the phase-12 closing assessment.
- **Owed by the leader before the session advances:** reset `progress/current.md` (**A17**) — `./init.sh` fails until it is done.
