# review_notifications_service — feature 23, phase 11

**Verdict: REJECTED.**

Two blocking defects, both in the **guards**, neither in the code. Everything the feature claims to *do*, it does — I verified acceptance bullets 1 and 3 myself against the live stack, published my own seven facts and my own suppression control, and watched Mailpit go 7 → 14 and stay at 14 through a full byte-identical redelivery of all seven. The durable-ledger inheritance is real, the `AutoOffsetReset.Latest` argument is sound and I saw it work on live partitions, and the OI12 parity fix is correct, narrow and genuinely load-bearing — I proved that with two mutations of my own, one of which the implementer had not tried.

The rejection is that **the correctness is not defended**. Two independent mutation probes, run by me, leave both suites fully green while the service silently stops emailing five of its seven facts, or emails two of them to the wrong address:

| Probe | Mutation | Suites after mutation |
|---|---|---|
| **D1** | Five of the seven entries deleted from `NotificationFactsConsumer._notifiedFactTypes` | **43/43 and 12/12 green** |
| **D2** | 68 single-field payload mutations across the seven templates | **45 of 68 survive** the 43-test unit suite, including the recipient address of `order.completed.v1` and `order.cancelled.v1` |

That is the feature-17 class exactly — the one CLAUDE.md widened the arming rule for, the one that is invisible to traceability because the requirement text is satisfied, and the one that is invisible to arming when arming only ever attacks deletion of the *handler* and never the *routing* or the *payload*. This service's only externally visible effect is an email; both probes change who gets one and what is in it, and nothing turns red.

---

## Method — what I ran, and what I deliberately did not

`./quality.sh` was exit 0 at 1039 minutes before this review. **I did not re-run it in full** — the claim under test is not "the suite passes", it is "the suite would fail if the behaviour regressed", and that is answered by probes, not by repetition. What I ran instead, all figures read off my own runs:

| # | Probe | Result |
|---|---|---|
| P1 | `dotnet test tests/Notifications.UnitTests` | **43/43**, 0 failed, 0 skipped — confirms the reported figure |
| P2 | `dotnet test tests/Notifications.IntegrationTests` (real Kafka `apache/kafka` + real MS-SQL Testcontainers) | **12/12**, 1 m 30 s |
| P3 | `dotnet test tests/Orders.UnitTests` | **280/280**; the four `IdempotentConsumerParityTests` **4/4** |
| P4 | `dotnet test tests/Architecture.Tests` (NetArchTest, run — not eyeballed) | **16/16** |
| P5 | **OI12 semantic-gutting probe** — the duplicate `throw` removed from Notifications' `IdempotentConsumer.cs` copy, so `work` runs on a duplicate | **FAILS**, names the file (verbatim below) |
| P6 | **OI12 narrowness probe** (untried by the implementer) — Notifications' own-service `using` changed to a **non-whitelisted own-service suffix** (`.Application.Templates`) | **FAILS** — the normalisation really is suffix-bounded, not "any own-service `using`" |
| P7 | **68-mutation template payload sweep**, automated, one field at a time, restore + `utime` + rebuild between each | **45 SURVIVED**, 23 caught, 0 build errors |
| P8 | **Notified-fact routing-table probe** — five of seven fact types removed from the consumer's filter | unit **43/43** and integration **12/12** both green |
| P9 | **Live Mailpit verification, my own run** — real `src/Notifications` host, `SenderKind = Smtp`, real compose stack; my own 7 notified facts + 1 `stock.reserved.v1` control published with `kafka-console-producer` | Mailpit **7 → 14**, ledger **7 → 14**, control produced **no** email and **no** ledger row |
| P10 | **Live idempotency, both halves** — all seven envelopes republished byte-identically | Mailpit **stays 14**, ledger **stays 14**, host still alive, 28 duplicate-key events handled internally |
| P11 | **Live `AutoOffsetReset.Latest` observation** — 10 of 18 partitions held **no** committed offset for the `notifications` group and carried ~30 historical facts | Mailpit stayed at 7 through assignment — `Latest` genuinely suppressed the backlog |
| P12 | `./init.sh` | exit 0, 56 features, id 23 `in_review` |
| P13 | C7 fidelity: `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared`; same for `n8n/workflows` | only `test-matrix.md` differs; **n8n byte-identical** |
| P14 | Canonical-copy comparison by hand (`diff`, banner-stripped), `Directory.Packages.props` diff, `decimal`/`float` sweep, `TODO`/`Console.WriteLine` sweep, Jest sweep | all clean |

**Every mutated file was restored from a pre-mutation backup (never `git checkout --`), confirmed byte-identical with `cmp`, `touch`ed, rebuilt with `dotnet build --no-incremental`, and re-run green.** `git status --porcelain` at the end of this review is byte-identical to the submitted state. The live host was stopped; no throwaway project was created (I published with the broker's own console producer rather than adding a .NET project).

---

## Acceptance bullets — each on its own terms

### Bullet 1 — "real email verified in the Mailpit inbox for each notified fact"

**Behaviourally MET, verified by me from Mailpit's own API — but not defended (see D1, D2).**

I did not take the implementer's seven messages as evidence; I published my own. Baseline `GET /api/v1/messages` → `"total": 7` (the implementer's run). After my eight publishes:

```
TOTAL: 14
2ef4729b-…@order-to-cash | ord-rev23@retailer.order-to-cash.example  | Payment received for invoice INV-REV23 (correlationId: 96f59565-…)
6a267f6c-…@order-to-cash | revretail@retailer.order-to-cash.example  | Invoice INV-REV23 issued (correlationId: 96f59565-…)
44c618ba-…@order-to-cash | revretail@retailer.order-to-cash.example  | Order ORD-REV23 despatched (DES-REV23) (correlationId: …)
688b0c18-…@order-to-cash | revretail@retailer.order-to-cash.example  | Order ORD-REV23 cancelled (correlationId: …)
35d59ce1-…@order-to-cash | revretail@retailer.order-to-cash.example  | Order ORD-REV23 completed (correlationId: …)
509f885f-…@order-to-cash | revretail@retailer.order-to-cash.example  | Order ORD-REV23 confirmed (correlationId: …)
a85fe4fa-…@order-to-cash | revretail@retailer.order-to-cash.example  | Order ORD-REV23 placed (correlationId: …)
```

Seven new messages for seven notified facts, each `Message-Id` equal to `<eventId>@order-to-cash` for the exact `eventId` I generated. `payment.received.v1`'s recipient is correctly derived from `orderReference` (the payload carries no `retailerCode`). `GET /api/v1/message/{id}` on the invoice message returned real rendered HTML and text — `<li>Total: 248.50 USD</li>` from `24850` minor units, integer arithmetic, no `decimal` anywhere in `src/Notifications`. The `stock.reserved.v1` control produced **no** email and **no** ledger row: 8 facts in, 7 emails out, 7 ledger rows.

### Bullet 2 — "console adapter used in tests via the same port"

**MET.** `ConsoleNotificationSenderTests.cs:14` constructs the adapter and assigns it to `INotificationSender` before calling it, so the call goes through the port, not around it. `NotificationsOptions.SenderKind` defaults to `Console` and only `Program.cs` sets `Smtp`; `NotificationsDispatcherRegistrationTests` builds the **real** host composition at that default and proves `Build()` fails when the `INotificationSender` registration is removed. The integration suite swaps the same port with `Replace(ServiceDescriptor.Singleton<INotificationSender>(fake))` — a stronger substitution than binding console and not asserting. No test can construct `MailKitNotificationSender`: it is bound only under `SenderKind.Smtp`, and `MailKitNotificationSenderTests` drives it through the injected `Func<ISmtpTransport>` seam, never a socket.

### Bullet 3 — "idempotent by eventId", including its absence half

**MET, verified live and in the suites.** After the seven emails landed, I republished all seven envelopes byte-identically. Mailpit stayed at **14** and `otc_notifications.processed_events` stayed at **14** — no second email, no second ledger row. The host stayed alive; the ledger's unique-index violations were caught internally (28 duplicate-key events in the log, zero unhandled). In the suites the absence half is proven positively rather than by non-observation: `DispatchAsync_RedeliveredEventId_SendsNoSecondEmailAndNeverBuildsTheMessage` counts `buildMessage` invocations and asserts **1**, and the integration suite's cross-restart case starts a genuinely fresh `IHost` over the same durable MS-SQL and the same consumer group and asserts `sender2.CallCount == 0`.

**#7's round-1 defect is genuinely inherited as prevention, not merely claimed.** #7 shipped a heap `Set`, was rejected on three emails for one `eventId`, and needed a whole second round to add a fourth database mid-feature. Here `otc_notifications.processed_events` already existed from phase 6, the acceptance bullet already said "durable ledger", and the equivalent probe produces one email on the first delivery and zero on every redelivery. #7's N6 (insert-first, delete-on-throw) and N13 (never let a failing compensation mask the original send error) are both present in `NotificationDispatchService.cs:60-96` **from the first submission**, each with a named unit guard that I confirmed exists and asserts the right thing.

---

## The harness gap — judged, not accepted

**The fix is correct, the scope call was right, and the fix is armed. This is the strongest part of the feature.**

*Was the guard vacuous before?* **Yes, structurally.** `DiscoverCopyServices` ranges over services carrying **both** a `ProcessedEventConfiguration.cs` **and** an `IdempotentConsumer.cs`. Until this feature that set was `{Orders}` — a set of one, in which the canonical is compared to itself and the comparison cannot fail. Fulfillment and Billing have the configuration but no consumer copy; Projector has neither. Notifications is the first genuine second member, and the set is now `{Orders, Notifications}`, which I verified from the filesystem.

*Is the fix correct?* **Yes.** `NormalizeCanonical` now neutralises exactly one line: a `using` whose namespace is `OrderToCash.<this file's own declared namespace service>` followed by one of the two suffixes `design.md` §6.4 already names as legitimately service-specific. I checked the two copies by hand, banner-stripped: `IdempotentConsumer.cs` differs from Orders' only in the `using` and the `namespace`; `ProcessedEventLedger.cs` likewise. Nothing else is normalised away.

*Is the fix armed?* **By me, twice, in two families.**

```
P5 (deletion of semantics — the duplicate `throw` replaced by `await work(ct); return Processed;`)
  Notifications's IdempotentConsumer.cs (at src/Notifications/Infrastructure/Messaging/IdempotentConsumer.cs)
  diverges from the canonical src/Orders/Infrastructure/Messaging/IdempotentConsumer.cs outside the banner
  and the namespace line.
  Failed! - Failed: 1, Passed: 3, Skipped: 0, Total: 4

P6 (narrowness — own-service `using` with a NON-whitelisted suffix `.Application.Templates`)
  Notifications's IdempotentConsumer.cs (…) diverges from the canonical (…)
  Failed! - Failed: 1, Passed: 3, Skipped: 0, Total: 4
```

P5 is precisely the mutation #7's guard **blessed** (its round-1 review: "Four green"). Here it fails and names the file. P6 is mine and was not in the implementer's table: it proves the acceptance is narrower than "any own-service `using`", which is the property the fix's own doc comment claims.

*Was editing `tests/Orders.UnitTests/IdempotentConsumerParityTests.cs` scope creep?* **No — it was the right call, and the alternatives were worse.** The red test was caused by this feature's own file, and CLAUDE.md's "a red test anywhere in your suite is inside your scope" is on point. The only ways to avoid it were to ship a red `quality.sh` or to invent a fourth, non-canonical dedup pattern to dodge the discriminator — which is exactly the "opt out by not creating a file" failure #7's own review closed with. The diff touches one helper and adds three private members; `AssertAdoptable` and the other three cases are untouched; `src/Orders/` production code is untouched. **Report it, do not repeat it as a precedent for wider edits.**

---

## The `AutoOffsetReset.Latest` decision

**The argument is sound. The property is real and I watched it hold. It is guarded by nothing.**

The argument, judged on its merits: Orders' saga orchestrator owns a state machine, so an order placed before its group existed must still reach a terminal state — `Earliest` is right there. Notifications owns no aggregate and no state machine (`domain-model.md` §6), so a fact its group never received is an omission, not an inconsistency; and the durable ledger cannot help, because it only suppresses a **duplicate** of a fact already recorded and can never un-see a fact never received. Those are two different failure modes needing two different defences. **That reasoning is correct**, and it is a better answer than #7's, which reached the same setting only as a round-2 patch after its reviewer asked directly.

I also observed it working: at assignment my host held 10 partitions with **no** committed offset for the `notifications` group, carrying roughly thirty historical facts between them. Mailpit stayed at 7. Under `Earliest` that would have been a mail run.

But `grep -rn "AutoOffsetReset" tests/` returns nothing for Notifications except **three comment lines**. No test asserts `Latest`. Flipping the single token to `Earliest` restores the exact exposure #7 suffered live, and the whole 1039-test solution stays green — `Earliest` would if anything make the integration suite *more* reliable, since the elaborate `WarmUpAsync` machinery in `NotificationConsumptionTestSupport.cs` exists only to work around `Latest`. This is a countable claim the implementer explicitly nominates as the ported-idiom ledger entry it owns, and CLAUDE.md's ledger rule requires a hand-built property to carry a named guard test. Recorded as **D3** below, medium, not on its own a blocker.

---

## Defects

### D1 — BLOCKING — five of the seven notified facts can be silently and permanently suppressed with both suites green

**File:** `src/Notifications/Presentation/NotificationFactsConsumer.cs:33-42` (`_notifiedFactTypes`) and `:116-126` (the routing switch).

I deleted `"order.confirmed.v1"`, `"invoice.issued.v1"`, `"payment.received.v1"`, `"order.completed.v1"` and `"order.cancelled.v1"` from the filter set, leaving only `order.placed.v1` and `order.despatched.v1`. Those five facts then take the early-return "acknowledged, no dispatch, no ledger row" path — the service stops emailing five of the seven business events it exists to email, forever, silently. Result:

```
Passed!  - Failed: 0, Passed: 43, Skipped: 0, Total: 43   (Notifications.UnitTests)
Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12   (Notifications.IntegrationTests, real Kafka + real MS-SQL)
```

**Why it matters.** This is a branch that *deliberately suppresses*, and CLAUDE.md's arming rule names that case explicitly. The implementer armed one direction of this filter (row 6, `stock.reserved.v1` must **not** dispatch) and never armed the other (each of the seven **must** dispatch). The seven handler guards in `NotifyFactCommandHandlersTests` do not cover it: they construct the handler directly and never traverse the filter or the routing switch, so the code path that decides *whether a fact becomes an email at all* is proven for two fact types out of seven. It defeats acceptance bullet 1 — "for **each** notified fact" — at the only place where "each" is decided. `progress/impl_notifications_service.md`'s NS-trace maps NS1–NS7 to template and handler tests; no row maps any of them to the consumer's routing, and the trace does not claim to.

**Confound — would #7's standard have caught it?** **No.** #7's `notification-facts.controller.spec.ts:74` tested the exclusion side only, and its reviewer's Probe 5 armed the seven **handlers**, exactly as here. This is a finding #8's own widened arming rule makes visible and #7's did not.

### D2 — BLOCKING — 45 of 68 payload-field mutations survive the unit suite, including the recipient address on two of the seven templates

**Files:** all seven of `src/Notifications/Application/Templates/*Template.cs`; the two worst are `OrderCompletedTemplate.cs:38` and `OrderCancelledTemplate.cs:42`.

Automated sweep: for each string-typed `payload.<Field>` reference in a live (non-comment) line of the seven templates, replace that one reference with a literal, rebuild, run the 43-test unit suite, restore. **68 mutations, 23 caught, 45 survived, 0 build errors.** Two families of survivor matter most:

1. **The recipient.** `OrderCompletedTemplateTests` and `OrderCancelledTemplateTests` are the only two of the seven with **no** `message.To` assertion. I changed both templates' `RecipientFor(payload.RetailerCode)` to `RecipientFor(payload.CompanyCode)` — the completed and cancelled emails then go to `comp01@retailer.order-to-cash.example` instead of the retailer — and the suite reported `Passed! - Failed: 0, Passed: 43`. Sending a counterparty's order-cancelled notice to the wrong address is the single most consequential thing this service can get wrong, and two of its seven paths have no assertion that it does not.

2. **The body.** Every `Retailer:`, `Company:`, `Despatch reference:` and `Source:` line in every template is unasserted in both the text and the HTML rendering. `OrderCompletedTemplate` and `OrderCancelledTemplate` are unasserted on **every** string field except the subject: I separately swapped `Retailer: {payload.RetailerCode}` for `{payload.CompanyCode}` in `OrderCompletedTemplate`'s text **and** HTML and the suite stayed at 43/43.

**Why it matters.** CLAUDE.md is explicit that a fact-emitting branch needs **both** questions asked of **it**: does the guard fail when the emission is absent, and does it fail when a field is wrong. `progress/impl_notifications_service.md:95` reads "at least one branch has been seen to fail on payload corruption, satisfying both mutation families" — that is one exemplar (row 2, `OrderPlacedTemplate`'s subject), not both questions asked of each of the seven. It is the same reading that let feature 17's `reason` field ship corrupt on a 79/79 green suite.

I am not asking for 68 assertions. The proportionate fix is named under "What must change".

**Confound — would #7's standard have caught it?** **No.** #7's handler specs asserted `toHaveBeenCalledWith(ENVELOPE, buildXMessage)` — builder *identity*, which catches a wrong-template copy-paste but not a wrong field inside the right template. #7's reviewer explicitly signed that off as sufficient. This finding exists only because CLAUDE.md's second mutation family was added after #7 and I applied it.

### D3 — Medium, not blocking — the ledger entry this feature owns has no guard

**File:** `src/Notifications/Infrastructure/Messaging/Consumers/KafkaFactStreamSubscriber.cs:99`.

`AutoOffsetReset.Latest` is nominated in the implementation record as this feature's ported-idiom ledger entry and is argued at length in the class's own remarks. No test in the repository asserts it; the only occurrences in `tests/` are three comments. Changing that one token to `Earliest` reinstates #7's live mail-storm exposure and leaves every suite green. CLAUDE.md's ledger rule requires a named guard for a hand-built property. A one-line reflection or config-shape assertion over `BuildConsumerConfig` closes it, and the same test can pin `GroupId`/`ClientId`, which are likewise unasserted.

### D4 — Low, owner: leader, not blocking on id 23 — `progress/current.md` carries Phase 10 leftovers

Its header names `notifications_service` (id 23, phase 11) and says `Status: in_progress`, but the backlog says `in_review`, and **Goal**, **Decisions taken this session** and **Notes** still describe Phase 10 — *"Phase 10 continues: `billing_credit_simulator` (id 20 …), then invoicing (id 21 …), then remittance intake (id 22)"*, plus the phase-10 backlog attachment map. C2's fourth box requires the active session or the bare template, never leftovers. Note `init.sh` reports "in lockstep with the backlog" — it compares the header line only, so it cannot see this. Same finding, same owner, as #7's N11; it must be reset before the wrap-up commit.

---

## CHECKPOINTS walk

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`).
- [x] Every agent definition declares its model or states it inherits.
- [x] `./init.sh` exits 0 — re-run by me.

### C2 — state is coherent
- [x] At most one feature `in_progress` — zero at review time; id 23 `in_review`.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [ ] **`progress/current.md` describes the active session** — header only; body is Phase 10. **D4.**
- [x] Every `blocked` feature records why — none blocked.

### C3 — architecture is respected
- [x] No forbidden framework reference in any `Domain/` folder — `Architecture.Tests` **16/16 run by me**, not eyeballed. `src/Notifications/Domain/` holds only the placeholder; this context owns no aggregate (`domain-model.md` §6).
- [x] No cross-service database access — `otc_notifications` is its own database, one table, no FK across a boundary; `NotificationsDbContext` reads nothing else.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs` — `Notifications.csproj` references exactly those three.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — `grep` across all `src/*/Domain/` returns nothing; `CqrsDomainPurityTests` green.
- [x] `src/SharedKernel` still has zero `PackageReference` entries.
- [x] No `decimal` in domain arithmetic — `grep` for `decimal|double|float` across `src/Notifications` returns only three doc-comment mentions; `NotificationFormat.FormatMoney` is integer division and modulo throughout.
- [x] Every interaction is Kafka-fact or NATS-RPC — three Kafka fact consumptions, zero RPC, zero producer, no outbox. Correct per the matrix: this service consumes facts and emits none.
- [x] No stray debug logging, no context-free TODOs — sweep clean; the console adapter's line is the deliberate structured adapter and logs `to`/`subject`/`messageId` only, never the body.

### C4 — verification is real
- [x] `./quality.sh` passes *(implementer's record, exit 0 at 1039; not re-run in full — see Method. I ran the four suites whose claims I was testing: 43, 12, 280, 16.)*
- [x] Domain tests are pure — N/A for an aggregate-less context; the application-layer tests are framework-free hand-rolled fakes with real call records.
- [x] Integration tests use Testcontainers against real MS-SQL and real Kafka — **independently verified by running them**, 12/12 in 1 m 30 s against real containers, never a mocked broker.
- [x] Coverage thresholds — not enforced anywhere in this repository until feature 34/phase 21; the implementer reports rather than claims a gate, which is the honest call.
- [x] **No Jest anywhere** — sweep clean.
- [ ] **Tests would fail if the behaviour regressed.** **No.** D1 and D2, each proven by a probe: five of seven notified facts suppressible on a green suite; 45 of 68 payload mutations survive, including the recipient on two templates. The parity guard, the idempotency guards and the seven handler guards *are* real — this box fails on the routing branch and on the payload family, not on the whole feature.

### C5 — the session closed cleanly
- [x] No suspicious untracked files — `git status --porcelain` matches the submitted state exactly after my restores; no `*.tmp`, no probe artefacts, no throwaway project.
- [ ] **`progress/history.md` has an entry including the effort record** — correctly absent; the feature is not approved.
- [x] `feature_list.json` reflects the true state — set back to `in_progress` by this review.
- [x] The human has been told what was done and how to test it — `progress/impl_notifications_service.md` is thorough and, on the two points I checked hardest, accurate.
- [x] Claude did not commit — no commit or push by me.

### C6 — spec-driven development
Not applicable: id 23 is `sdd: false`. The implementer correctly declined to invent `R<n>` ids, used local `NS1`–`NS8`, and correctly left `specs/shared/test-matrix.md` untouched — R17/R18's row is owned by feature 14 and cites Orders' own tests, and no cited test was renamed. That matches #7's call on the identical question.

### C7 — spec-reuse fidelity and benchmark honesty
- [x] **`specs/shared/` byte-identical to #7's except `test-matrix.md`** — verified with a real `diff -rq` against the #7 checkout: exactly one differing file.
- [x] Every deviation is a recorded amendment — this feature amends nothing in `specs/shared/`.
- [x] The `R<n>` ids are #7's — none invented; R17/R18 are claimed and genuinely satisfied (verified live, P10).
- [x] `n8n/workflows/*.json` unchanged from #7 — `diff -rq` returns nothing at all.
- [x] The black-box API script proves the same saga steps — untouched by this feature.
- [ ] **`progress/history.md` effort records complete and honest** — pending this feature's entry, which cannot be written until approval.
- [x] The README's benchmark section — untouched by this feature.

---

## Requirement → test mapping verified

| Claim | Test(s) | Verified? |
|---|---|---|
| **R17** — record the `(eventId, consumer)` pair | canonical `IdempotentConsumer` + `ProcessedEventLedger` copies; `NotificationConsumptionTests.ConsumesARealOrderPlacedFact_SendsExactlyOnceAndRecordsTheLedgerRow` (real MS-SQL) | ✅ and confirmed live: ledger 7 → 14 for 7 facts, and the `stock.reserved.v1` control wrote none |
| **R18** — redelivery acknowledged with no second effect | `DispatchAsync_RedeliveredEventId_SendsNoSecondEmailAndNeverBuildsTheMessage`; `ARedeliveredEventId_InTheSameRunningProcess_SendsNoSecondEmail`; `ARedeliveredEventId_AfterARestart_IsNotResentByAFreshlyCompiledHostSharingTheSameDurableLedger` | ✅ and confirmed live: all seven republished, Mailpit and ledger both unchanged |
| §7.3 — exactly the seven notified facts, `stock.*`/`credit.*` excluded | exclusion: `AStockReservedFact_IsAcknowledgedButNeverDispatched`; inclusion: **nothing for five of seven** | ❌ **D1** — the seven match `domain-model.md:488` exactly in source, but the branch that enforces it is guarded for two |
| §7.1 envelope — seven fields, `correlationId` in the subject | `ValidateEnvelope` + all seven `*TemplateTests` subject assertions | ✅ |
| NS1–NS7 — one templated email per fact, correct template | `NotifyFactCommandHandlersTests` (subject substring per fact) + seven `*TemplateTests` | ⚠️ template *identity* guarded; template *content* is not — **D2** |
| NS8 — idempotent by `eventId`, both halves, incl. cross-restart | `NotificationDispatchServiceTests` (6 cases) + `NotificationConsumptionTests` (5 cases) | ✅ |
| Console adapter through the same port | `ConsoleNotificationSenderTests`; `NotificationsDispatcherRegistrationTests`; integration `Replace(...)` on the same port | ✅ |
| OI12 byte-identity for a real second copy | `HoldsEveryWriteModelsCopyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine` | ✅ and armed by me in two families (P5, P6) |
| Ledger entry — `AutoOffsetReset.Latest` | none | ❌ **D3** |

---

## What must change before re-review

1. **D1 — guard the notified-fact routing, all seven.** The cheapest sufficient form is a **unit** test over `NotificationFactsConsumer`'s classification: for each of the seven `eventType` literals, the message is dispatched and to the command type that fact maps to; for `stock.*`, `credit.*` and `order.saga_failed.v1`, it is not. Arm it by deleting one entry from `_notifiedFactTypes` and by swapping two routing arms — both must go red. (An integration test per fact would also work and is far slower; the existing `stock.reserved.v1` integration case should stay either way.)
2. **D2 — ask the corruption question of each of the seven templates, not of one.** Minimum: (a) assert `message.To` in **all seven** `*TemplateTests` — `OrderCompletedTemplateTests` and `OrderCancelledTemplateTests` have none today; (b) for each template assert its identifying business references and its distinguishing payload fields in **both** `Text` and `Html`, so a wrong-field interpolation fails. Then re-run the sweep and report the survivor count: it does not have to be zero, but the recipient and the fact-identifying references must all be caught, and the record must state the residual.
3. **D3 — one guard for the ledger entry.** Assert `Latest`, `GroupId = "notifications"` and `ClientId = "otc-notifications"` from `BuildConsumerConfig`, and name it in the implementation record's ledger line. Arm it by flipping the token to `Earliest`.
4. **D4 (leader) — reset `progress/current.md`** to the active session or the bare template before the wrap-up commit.

Not required, but worth saying: **do not touch anything else.** The parity fix, the dispatch service, the ledger composition, the seven templates' logic, the host wiring and the live behaviour are all correct as submitted, and this rejection asks for assertions, not redesign.

`feature_list.json` id 23 set back to **`in_progress`**.

---

## Effort note, held for the approval round

Not written to `progress/history.md` — that entry belongs to the approving round, and the round-count is not final. Recorded here so it is not lost:

- **#7's baseline:** 2 implementation sessions + 2 review passes, **rejected once then approved**, with a fourth MySQL database added mid-feature under rejection.
- **#8 so far:** 1 implementation session + 1 review pass (this one), rejected.
- **The reuse dividend, which shows up as an absence.** #7's five round-1 blockers were N1–N5. Four of them **could not occur here**: the acceptance bullet already said *durable ledger* (written into `feature_list.json` from #7's outcome), `otc_notifications.processed_events` already existed from phase 6, `outbox_and_idempotency/design.md` §6.3 had already named the record-before-or-after-send question **at design time**, and #7's N6 and N13 fixes were both present in the first submission. #7's N5 — the parity guard that blessed a gutted `runOnce` — is the one that recurred in a different shape, and #8 found and fixed it **inside the feature** rather than in a follow-up briefing. Concretely, that is **one whole implementation round and one whole review round of #7's cost that did not happen here**, and it is invisible in a session count unless stated. What the reuse did **not** prevent is this round's D1 and D2 — both are defect classes #7's standard did not have, so neither the spec nor the harness could carry them across.

---

# ROUND 2 — re-review after the fix round

> Round 1 above is untouched and is not reopened. Everything below is this round's own work, run by me, this session.

**Verdict: APPROVED.**

D1, D2 and D3 are closed, and closed in the shape round 1 asked for rather than in the smallest shape that would pass. I re-armed all three myself in both mutation families, wrote and ran my **own** 89-site payload sweep with its machinery proved by sentinels before I trusted a single one of its verdicts, and reproduced the implementer's `TOTAL=70 SURVIVED=0` independently — same 70 string-typed sites, enumerated by my script, not read off theirs.

Three residuals are recorded below (**R2-D5**, **R2-D6**, **R2-D7**). None blocks: two are unguarded properties that I verified are *correct today* (one of them by round 1 against real bytes), and the third is bookkeeping owned by the leader. R2-D5 is worth a backlog entry and one line of test.

---

## Method — what I ran, and what I deliberately did not

`./quality.sh` is reported exit 0 at **1054**. **I did not re-run it in full.** The fix round's blast radius is one production file (`NotificationFactsConsumer.cs`), one production file's single token (`KafkaFactStreamSubscriber.cs`, D3 arming only — restored), two new test files and seven edited test files, all inside `Notifications.UnitTests`. So I re-ran the suites whose claims were actually at risk and spent the rest of the budget on probes. Every figure below is off my own run.

| # | Probe | Result |
|---|---|---|
| R2-P1 | `dotnet test tests/Notifications.UnitTests` | **58/58**, 0 failed, 0 skipped — confirms the reported figure (was 43) |
| R2-P2 | **D1 deletion family** — the `["order.confirmed.v1"]` entry deleted from `_routes` | **1 failed / 57 passed**: `EachOfTheSevenNotifiedFacts_ReachesItsOwnCommand` at that `InlineData` |
| R2-P3 | **D1 corruption family** — the `order.completed.v1` and `order.cancelled.v1` closures swapped | **2 failed / 56 passed** |
| R2-P4 | **D1 payload family, mine, untried by the implementer** — `ToEnvelope` copies `envelope.EventId` into the typed envelope's `correlationId` | **SURVIVES** unit **58/58** *and* integration **12/12** (real Kafka + real MS-SQL) → **R2-D5** |
| R2-P5 | **D3** — `AutoOffsetReset.Latest` → `Earliest` on `KafkaFactStreamSubscriber.cs:99` | **FAILS by name**, verbatim below |
| R2-P5b | **My own first attempt at R2-P5 mutated the doc comment on line 76 instead**, and the suite stayed green | Recorded, because it is exactly the machinery failure I was sent to look for, found in my own probe rather than the implementer's |
| R2-P6 | **D3 second field** — `GroupId` `"notifications"` → `"notifications-2"` | **1 failed / 57 passed** |
| R2-P7 | **My own 89-site template mutation sweep**, machinery sentinel-verified first (below) | **75 CAUGHT / 14 SURVIVED / 0 not-applied / 0 build failures**; the 70 **string-typed** sites are **70/70 CAUGHT** — the implementer's figure reproduced independently. The 14 survivors are all `DateTimeOffset` fields → **R2-D6** |
| R2-P8 | `dotnet test` on `Notifications.IntegrationTests`, `Orders.UnitTests`, `Architecture.Tests` after restore + `--no-incremental` | **12/12** (1 m 24 s, real containers), **280/280**, **16/16** |
| R2-P9 | `./init.sh` | exit 0; 34/56 done; id 23 `in_review`; the lockstep line now states what it reads |
| R2-P10 | C7 fidelity re-run: `diff -rq specs/shared ../order-to-cash-nestjs/specs/shared`, same for `n8n/workflows` | only `test-matrix.md` differs; **n8n byte-identical** |
| R2-P11 | Subscription-set claim (`NotificationFactTopics.All`) — read, **not** mutated | Guarded structurally: `NotificationConsumptionTestSupport` warms up on **each** of the three topics and `throw new TimeoutException(...)` when a topic is never observed, so dropping one fails every integration test. Stated as a reading, not as a run |
| R2-P12 | `git status --porcelain` before and after this review | **identical**, 29 entries, same set; no `.bak`, no scratch file, no throwaway project inside the repository |

**Every mutated file was restored from a pre-mutation backup (never `git checkout --`), confirmed byte-identical by SHA-256 *and* `cmp`, `utime`d, rebuilt with `dotnet build --no-incremental`, and re-run green.** All seven templates were `cmp`-verified against their backups after the sweep.

### The D3 failure, verbatim

```
Failed OrderToCash.Notifications.UnitTests.KafkaFactStreamSubscriberConfigTests.BuildConsumerConfig_SetsLatestNotEarliest_SoAFreshConsumerGroupDoesNotMailTheHistoricalBacklog [31 ms]
  Assert.Equal() Failure: Values differ
  Expected: Latest
  Actual:   Earliest
```

---

## The sweep's own machinery — the thing I was asked to distrust

A sweep reporting every mutation caught is exactly as trustworthy as a green suite, so I did not audit the implementer's script: **I wrote my own, proved it could report each outcome before believing any of them, and enumerated the sites myself.**

Three sentinels, run before the sweep proper:

| Sentinel | What it plants | Reported | Why it matters |
|---|---|---|---|
| **A** | a mutation whose search text does not exist in the file | `file_changed=False` | a mutation that silently fails to apply is **detected**, not counted as CAUGHT. Every one of the 89 real sites then carried `applied=True` — verified by SHA-256 before/after, not by the `replace()` call's return |
| **B** | a doc-comment-only edit | **SURVIVED** | the sweep **can** report a survivor. A sweep that cannot has not proved there are none |
| **C** | `RecipientFor(payload.RetailerCode)` → `RecipientFor(payload.CompanyCode)` in `OrderCompletedTemplate` — round 1's own worst survivor | **CAUGHT** | the exact mutation that reported green in round 1 now dies |

Rebuild forcing: every mutation writes the file and `utime`s it, and each run is a full `dotnet test` (build included); the run is rejected unless the reported total is exactly **58**, so a stale or partially-built binary cannot be scored. The final restored state was rebuilt `--no-incremental` and re-run green.

**Sentinel B is not hypothetical here.** My own first D3 probe (R2-P5b) replaced the first textual occurrence of `AutoOffsetReset.Latest` in `KafkaFactStreamSubscriber.cs` — which is a `<see cref="..."/>` in the class remarks on line 76, not the config assignment on line 99 — and the suite stayed at 58/58. A less careful reviewer records "D3's guard does not fire" and rejects; a sweep with the same bug records 70 caught and approves. Both directions of that error live in the same missing check, which is why the sentinels come first.

**Independent enumeration.** My script found the string-typed `payload.<Field>` interpolation sites by regex over the seven templates, ignoring comment lines, without reading the implementer's site list — and found **exactly 70**, matching their per-file counts file by file. All 70 CAUGHT. The 5 money-field sites (`TotalAmount`, `Amount`) are also caught. That is the D2 claim reproduced, not accepted.

---

## The three round-1 defects

### D1 — CLOSED, and closed with the better fix

`NotificationFactsConsumer.cs:47-71` now carries one `IReadOnlyDictionary<string, Func<Envelope<JsonElement>, IDispatcher, CancellationToken, Task>> _routes`. `HandleMessageAsync:117` is a single `TryGetValue`; a miss is the exclusion path. **`Keys` is genuinely the only source** — I checked, and there is no second enumeration anywhere: `grep` for the seven `eventType` literals across all of `src/Notifications/` returns the seven dictionary keys and **nothing else** except one doc-comment mention per template file. The `HashSet` and the `switch` are both gone.

Guarded by `NotificationFactsConsumerTests`, which drives `ExecuteAsync` itself through a fake subscriber and a recording dispatcher — the real filter and the real routing table are traversed, which is precisely what the seven handler tests never did. Both mutation families kill it (R2-P2, R2-P3). Deleting an entry now costs a named theory case; swapping two arms costs two.

### D2 — CLOSED, with the residual measured rather than asserted

All seven `*TemplateTests` now assert `message.To` — including `OrderCompletedTemplateTests:31` and `OrderCancelledTemplateTests:36`, the two round 1 named — and each template's identifying references in **both** `Text` and `Html`. My independent sweep: **70/70 string-typed sites caught**, including all seven recipients and every `Retailer:` / `Company:` / `Despatch reference:` / `Source:` / `Reason:` line in both renderings. Round 1's 45 survivors are 0.

The record's claim *"the residual is zero"* is true **of the population the sweep enumerated** — string-typed fields — and the report says so in those words. My sweep widened the population and found the residual is not zero: see R2-D6.

### D3 — CLOSED

`KafkaFactStreamSubscriberConfigTests` reaches the real private `BuildConsumerConfig` by reflection rather than re-implementing it, and pins `AutoOffsetReset.Latest`, `GroupId`, `ClientId`, `EnableAutoCommit` and `EnableAutoOffsetStore`. Two of those five I flipped myself (R2-P5, R2-P6) and both died. The ledger entry this feature owns now has the named guard `CLAUDE.md` requires, and the guard is named in the implementation record.

### D4 — CLOSED (leader)

`progress/current.md`'s body is Phase 11 and feature 23. More useful than the rewrite: `init.sh`'s success line now reads *"…this check reads that line ONLY — a stale Goal/Decisions/Notes body below it is invisible here"*. A guard that overstated its coverage now states it, which is the one repair that generalises. See R2-D7 for the small piece still stale.

---

## Defects found this round

### R2-D5 — Medium, not blocking — the consumer's envelope copy is unguarded, and a corrupted `correlationId` reaches every email with both suites green

**File:** `src/Notifications/Presentation/NotificationFactsConsumer.cs:144-152` (`ToEnvelope<TPayload>`).

`ToEnvelope` copies seven envelope fields by hand into the typed envelope the templates then read. I changed line 151 so the typed envelope's `correlationId` receives `envelope.EventId` instead. Every one of the seven emails then carries a wrong `correlationId` in its subject — the one field `specs/shared` §7.1 and this feature's own acceptance put there deliberately, and the field every downstream trace joins on. Result:

```
Passed!  - Failed: 0, Passed: 58, Skipped: 0, Total: 58   (Notifications.UnitTests)
Passed!  - Failed: 0, Passed: 12, Skipped: 0, Total: 12   (Notifications.IntegrationTests, real Kafka + real MS-SQL, 1 m 41 s)
```

**Why it is not blocking.** The property is *verified correct today*, and not by inference: round 1 read seven real Mailpit messages whose subjects carried the exact `correlationId` published, and the implementer's own live run did the same. It is also exactly the guard round 1 did **not** ask for — round 1 prescribed "the command type that fact maps to", the implementer built that and armed it in two families, and rejecting for a surface the previous round specified out would be moving the goalposts, which this repository has explicitly ruled against.

**Why it is still a defect.** It is a hand-written field-by-field copy — the shape that has produced three ported-idiom defects in this build — sitting on the one code path all seven emails traverse, and no test in the repository would notice if it were wrong. **Cheapest sufficient fix, one line inside the existing theory:** assert the dispatched command's envelope carries the source `correlationId` (and, while there, `eventId`), so the copy is checked once for all seven. Recommend a backlog entry; the leader owns that file.

### R2-D6 — Low, not blocking — 14 date-field interpolation sites are unasserted in all seven templates

**Files:** each `*Template.cs`, the `X date:` / `X at:` line in both the text and the HTML rendering (e.g. `OrderPlacedTemplate.cs:24` and `:34`).

My sweep replaced each `payload.<DateField>` with `DateTimeOffset.UnixEpoch`: **14 sites, 14 survivors**, one text and one HTML per template. No test asserts any date in any rendering. This is not a contradiction of the implementer's `TOTAL=70 SURVIVED=0` — their sweep was scoped to string-typed fields and says so — it is the residual that scoping left, now measured. Consequence is cosmetic (a wrong timestamp in an email body, no routing or identity effect), which is why it is Low; it is recorded so the next reader knows the number rather than inferring zero.

### R2-D7 — Low, owner: leader, not blocking — `progress/current.md`'s **Status:** line is stale again

`progress/current.md:4` reads `**Status:** in_progress — rejected on two blocking defects, fix round in flight` while `feature_list.json` says `in_review` and the fix round landed 26 minutes before this review began. `init.sh` cannot see it — by its own newly honest admission, it reads the `**Feature:**` line only. Reset it with the wrap-up, along with the `done` transition this review has just made.

---

## CHECKPOINTS walk — round 2

### C1 — the harness is complete
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` all exist.
- [x] `progress/current.md` and `progress/history.md` exist.
- [x] `.claude/agents/` holds leader, spec_author, implementer, reviewer, test_maintainer (plus `suite_runner`).
- [x] Every agent definition declares its model or states it inherits.
- [x] `./init.sh` exits 0 — re-run by me this round (R2-P9).

### C2 — state is coherent
- [x] At most one feature `in_progress` — zero; id 23 was `in_review` at review time and is `done` after it.
- [x] Every status is in `rules.valid_status`.
- [x] Every `done` feature has passing tests associated with it.
- [x] **`progress/current.md` describes the active session** — round 1's D4 is fixed: the body is Phase 11 / feature 23. The `**Status:**` line lags one transition (**R2-D7**, low, leader) — the box is met, the nit is recorded.
- [x] Every `blocked` feature records why — none blocked.

### C3 — architecture is respected
- [x] No forbidden framework reference in any `Domain/` folder — `Architecture.Tests` **16/16, run by me this round**, not eyeballed.
- [x] No cross-service database access — unchanged by the fix round; `otc_notifications` remains one table, no FK across a boundary.
- [x] No shared runtime code beyond `src/SharedKernel`, `src/Contracts`, `src/Cqrs`.
- [x] No `Domain/` namespace references `OrderToCash.Cqrs` — `CqrsDomainPurityTests` green inside the 16.
- [x] `src/SharedKernel` still has zero `PackageReference` entries.
- [x] No `decimal` in domain arithmetic — re-swept `src/Notifications` for `decimal|double |float `: **no hit outside doc comments**; `NotificationFormat.FormatMoney` is integer division and modulo.
- [x] Every interaction is Kafka-fact or NATS-RPC — the fix round changed the routing table's *shape*, not the classification: three Kafka fact topics consumed, zero RPC, zero producer.
- [x] No stray debug logging, no context-free TODOs.

### C4 — verification is real
- [x] `./quality.sh` passes *(implementer's record, exit 0 at 1054; deliberately not re-run in full — see Method. I ran the four suites whose claims were at risk: 58, 12, 280, 16.)*
- [x] Domain tests are pure — N/A for an aggregate-less context; the new `NotificationFactsConsumerTests` uses hand-rolled fakes and a real `ServiceCollection`, no mocking framework.
- [x] Integration tests use Testcontainers against real MS-SQL and real Kafka — re-run by me, **12/12** in 1 m 24 s, and again under mutation (1 m 41 s) which is itself evidence the containers were real.
- [x] Coverage thresholds — not enforced anywhere in this repository until feature 34/phase 21; reported, not claimed.
- [x] **No Jest anywhere** — swept again, the only hit is a feature title in `feature_list.json` that forbids it.
- [x] **Tests would fail if the behaviour regressed** — the round-1 blocker on this box is lifted. Six mutations by me this round: five red where they must be (route deletion, route swap, `Latest`, `GroupId`, recipient), 70/70 on the payload sweep. Two named residuals stand — **R2-D5** and **R2-D6** — both recorded, neither on a routing or identity path, and R2-D5's property independently verified correct against real bytes in round 1.

### C5 — the session closed cleanly
- [x] No suspicious untracked files — `git status --porcelain` byte-identical before and after this review (R2-P12).
- [x] **`progress/history.md` has an entry including the effort record** — appended by this review, with the Phase 11 closing assessment.
- [x] `feature_list.json` reflects the true state — id 23 set to `done` by this review, as a single-line edit; `git diff` shows exactly that one line.
- [x] The human has been told what was done and how to test it — `progress/impl_notifications_service.md`'s fix round is specific and, on the three claims I tested hardest, accurate.
- [x] Claude did not commit — no commit or push by me.

### C6 — spec-driven development
Not applicable: id 23 is `sdd: false`. `specs/shared/test-matrix.md` remains untouched, correctly — I re-checked that none of the three new/edited test classes is cited by any matrix row.

### C7 — spec-reuse fidelity and benchmark honesty
- [x] **`specs/shared/` byte-identical to #7's except `test-matrix.md`** — re-verified this round with a real `diff -rq`: exactly one differing file.
- [x] Every deviation is a recorded amendment — this feature amends nothing in `specs/shared/`.
- [x] The `R<n>` ids are #7's — none invented; local `NS1`–`NS8` used, as #7 did for this feature.
- [x] `n8n/workflows/*.json` unchanged from #7 — `diff -rq` returns nothing at all.
- [x] The black-box API script proves the same saga steps — untouched by this feature.
- [x] **`progress/history.md` effort records complete and honest** — this feature's entry is appended, including the round it lost and why.
- [x] The README's benchmark section — untouched by this feature; the leader updates it at wrap-up.

---

## Requirement → test mapping, re-verified

| Claim | Test(s) | Round 1 | Round 2 |
|---|---|---|---|
| **R17** — record the `(eventId, consumer)` pair | canonical `IdempotentConsumer`/`ProcessedEventLedger` copies; `ConsumesARealOrderPlacedFact_SendsExactlyOnceAndRecordsTheLedgerRow` | ✅ live | ✅ 12/12 re-run |
| **R18** — redelivery acknowledged with no second effect | `DispatchAsync_RedeliveredEventId_SendsNoSecondEmailAndNeverBuildsTheMessage`; the in-process and cross-restart integration cases | ✅ live | ✅ re-run |
| §7.3 — exactly the seven notified facts, `stock.*`/`credit.*`/`order.saga_failed.v1` excluded | `NotificationFactsConsumerTests.EachOfTheSevenNotifiedFacts_ReachesItsOwnCommand` (7 cases) + `AnExcludedFact_ReachesNoDispatchAndOpensNoScope` (3 cases) + the real-Kafka `AStockReservedFact_...` | ❌ **D1** | ✅ **armed by me, both families** |
| §7.1 envelope — seven fields, `correlationId` in the subject | `ValidateEnvelope` + all seven `*TemplateTests` subject assertions | ✅ | ⚠️ the templates' half is guarded; the consumer's copy of `correlationId` is not — **R2-D5** |
| NS1–NS7 — one templated email per fact, correct template, correct content | seven `*TemplateTests` (recipient + both renderings) + `NotifyFactCommandHandlersTests` + the routing theory | ⚠️ **D2** | ✅ 70/70 string-field sites caught in my own sweep; 14 date sites survive — **R2-D6** |
| NS8 — idempotent by `eventId`, both halves, incl. cross-restart | `NotificationDispatchServiceTests` + `NotificationConsumptionTests` | ✅ | ✅ unchanged by the fix round |
| Console adapter through the same port | `ConsoleNotificationSenderTests`; `NotificationsDispatcherRegistrationTests`; integration `Replace(...)` | ✅ | ✅ unchanged |
| OI12 byte-identity for a real second copy | `HoldsEveryWriteModelsCopyByteIdenticalToTheCanonicalAfterTheBannerAndTheNamespaceLine` | ✅ armed twice | ✅ file untouched this round (mtime 15:56, pre-fix-round); `Orders.UnitTests` 280/280 |
| Ledger entry — `AutoOffsetReset.Latest` | `KafkaFactStreamSubscriberConfigTests` (3 cases) | ❌ **D3** | ✅ **flipped by me, dies by name** |

---

## Bookkeeping

`feature_list.json` id 23 → **`done`** (single-line edit; `git diff` shows that line only). Effort record and the Phase 11 closing assessment appended to `progress/history.md`.

Two items for the leader, neither blocking the close:

1. **Backlog entry for R2-D5** — the consumer's envelope copy, one assertion inside the existing theory.
2. **`progress/current.md`** — R2-D7, reset at wrap-up.

### Post-transition state — read this before anything else advances

Closing id 23 makes `progress/current.md` incoherent, and `init.sh` **now fails** on it:

```
[FAIL]  progress/current.md claims a feature while none is active: "**Feature:** `notifications_service` (id 23, phase 11)"
```

This is **R2-D7 escalated by the very transition this review just made**: before the close it was a stale `**Status:**` line that `init.sh` could not see; after the close the `**Feature:**` line names a feature that is no longer active, which `init.sh` does see. It is not a defect in the feature and it does not affect the verdict — `init.sh` ran **exit 0** on the submitted state (R2-P9), and the failure is a consequence of the approval, not of the work.

**I have deliberately not fixed it.** `progress/current.md` is the leader's file, round 1 assigned it to the leader as D4, and a reviewer's bookkeeping mandate is the feature under review — `feature_list.json`'s single status line and `progress/history.md` — and nothing else. **Required immediate action, leader:** reset `progress/current.md` to the bare template (or to Phase 12's opening session) and re-run `./init.sh` to green before the wrap-up commit.

C1's fifth box is therefore marked honestly: `./init.sh` exits 0 **on the state I reviewed**, and exits 1 **on the state my own approval created**, for this one reason and no other. Marking it `[x]` without saying so would be the overstatement this repository keeps paying for.
