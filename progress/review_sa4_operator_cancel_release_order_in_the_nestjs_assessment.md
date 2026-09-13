# review — backlog id 79 `sa4_operator_cancel_release_order_in_the_nestjs_assessment`

**Verdict: APPROVED.** One gap is routed as a numbered backlog entry rather than narrated (proposed below); it does not block the close. No defect blocks. Zero rejections.

**Scope of this review.** Scoped, by the leader's brief. The suite counts (Orders 539/539, Fulfillment 92/92), the 275-hit enumeration and its reconciliation, the transposition at every site, the four specs asserting order rather than membership, the rewritten doc-comments, the single-site content test and B4's sibling-substitution resistance were verified independently by the leader before this review and were **not** re-run here. What this review did instead: five arming probes re-armed from scratch against the tree on disk, two independent sweeps on the *retired* wording, and a disk verification of every "already correct" claim. The Orders suite was nonetheless run six times end to end (once per probe plus a confirming green), because vitest has no per-file scoping through the pnpm filter used, so the counts below are first-hand.

---

## 1. B3's "ALREADY CORRECT" claim — verified on disk, and the guard verified by re-arming

The claim is **true**, at all three links of the chain, and none of them is command-specific:

- `apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.ts:169-199` — `isTerminalRpcErrorCode` lists `PRECONDITION_FAILED` among the terminal-business codes, and `:180-184` throws `SagaCommandBusinessRejectionError` (not `SagaCommandTransportError`) for it. The switch is exhaustive over `RpcError['code']` with a `never` default, so a new code cannot be silently added to the transient side.
- `apps/orders/src/infrastructure/saga/saga-command-dispatcher.ts:180-193` — the `catch` short-circuits on that error class: `markRejected`, one log line, `return 'rejected'`. The `park()` call is at `:210`, the `claimDeadLetter()` + `onFirstPark()` pair at `:231-236` — **both after** the return, so the DLQ record and `order.saga_failed.v1` are structurally unreachable on this path.
- `apps/orders/src/infrastructure/saga/drizzle-saga-command-store.ts:124-125` — `claimDue`'s predicate is `status = 'pending' AND created_at < cutoff` OR `status = 'parked' AND next_attempt_at <= now`. A `rejected` row matches neither, so it is excluded from every future sweep. `markRejected` is at `:159-164`.

The "despatch wins" half needs no code either and now follows structurally from B1: `credit.release` is owed **only** by `stock.released.v1`'s `credit_approved`/`confirmed` variants (`saga-steps.ts:235,243`), and a despatch that won means Fulfillment released nothing and emitted no `stock.released.v1`.

**Guard re-armed independently (probe A3).** Mutation: `saga-command-dispatcher.ts:180` → `if (false && error instanceof SagaCommandBusinessRejectionError) {`, which routes the refusal exactly as the acceptance bullet forbids — through the retry budget, into `park`, into `claimDeadLetter`, into `onFirstPark`.

```
FAIL  src/infrastructure/saga/saga-command-dispatcher.spec.ts > SagaCommandDispatcher — feature 42 (terminal business rejection short-circuits SO4 retry) > SA-4 — a despatch.create refused with PRECONDITION_FAILED after a winning release is TERMINAL: rejected, no retry, no park, no dead-letter claim, and onFirstPark (order.saga_failed.v1) is never reached
AssertionError: expected 'parked' to be 'rejected' // Object.is equality
  ❯ src/infrastructure/saga/saga-command-dispatcher.spec.ts:346:21
Tests  3 failed | 536 passed (539)
```

Matches the record's A3 verbatim. The guard at `saga-command-dispatcher.spec.ts:322-354` asserts the **absences** the bullet names and not merely the outcome: `parkCalls` 0, `claimDeadLetterCalls` 0, `firstPark.calls` 0, `delays` 0, `createDespatch` called once. An "already correct" verdict backed by a guard that pins four absences is the right disposition; restored file `cmp`-identical.

## 2. The arming table — five of eleven rows re-armed independently, all reproduce

Protocol on every probe: `cp` to a backup outside the repository, mutate with an asserted single-occurrence replacement, run the suite, record verbatim, `cp` back, `cmp` against the backup, re-read the changed line. TypeScript transforms from source per run, so there is no stale binary hazard; the confirming run after the last restore is **539 passed (54 files)**. **No `git checkout`, `stash`, `reset`, `restore` or `clean` was run in either repository** — both repositories' `git stash list` are empty and neither reflog carries a `reset: moving to HEAD`.

| Row | Mutation I applied | Result |
|---|---|---|
| **A1b** | both `commandAfter: 'credit.release'` (`saga-steps.ts:235,243`) → `'stock.release'` — sibling substitution within the six-member `SAGA_COMMAND_KINDS` | **FAILS.** `AssertionError: expected 'stock.release' to be 'credit.release' // Object.is equality` at `saga-steps.spec.ts:301`, two cases (`credit_approved`, `confirmed`). 2 failed / 537 passed |
| **A5** | `isOperatorCancelEnvelope` drops the `eventType` test, returning true for any non-null object | **FAILS.** `AssertionError: expected true to be false` at `operator-cancel-envelope.spec.ts:46` and `:52`. **5** failed / 534 passed — and the four failing cases are the R27 discriminator itself: `credit.rejected.v1`, `stock.released.v1`, `credit.approved.v1`, `order.placed.v1`, i.e. exactly the real facts whose rows sit on the same two commands |
| **A7** | `HandleCreditApprovedFactHandler` publishes `OrderConfirmed` unconditionally (`saga-fact.handlers.ts:102-106`) | **FAILS.** `AssertionError: expected OrderConfirmed{ …(2) } to be an instance of LateCreditApprovalRecorded` at `saga-fact.handlers.spec.ts:140`. 1 failed / 538 passed |
| **A3** | as §1 above | **FAILS**, 3 failed / 536 passed |
| **A2** | `saga-fact-handler.ts:142` → `if (false && lateForAnAcceptedOperatorCancel) {` — the late-approval enqueue made unreachable | **FAILS on BOTH shapes** — see §4 |

All five reproduce the record's message verbatim. On A7 the negative half matters and is present: `saga-fact.handlers.spec.ts:141` also asserts `.not.toBeInstanceOf(OrderConfirmed)`, so the guard cannot be satisfied by publishing both.

A5 deserves one extra note in its favour. Its own premise — that the synthetic `OPERATOR_CANCEL_EVENT_TYPE = 'orders.cancel.requested'` is not a real wire fact type — was checked rather than assumed: `grep -rn "orders.cancel.requested" specs/shared/asyncapi.yaml packages/contracts/src` returns **no hits**, so a real fact's envelope can never be misread as an operator-cancel row.

## 3. The one disclosed gap — routed as a backlog entry, not a blocker

**The gap is real and correctly disclosed** (record §743). `DrizzleSagaCommandStore.hasAcceptedOperatorCancel` (`drizzle-saga-command-store.ts:95-108`) has **no test of any kind**. Enumerated, not asserted:

```
find apps/orders/src -name '*.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' -print0 | xargs -0 grep -n 'hasAcceptedOperatorCancel'
```

27 hits. One production implementation (`drizzle-saga-command-store.ts:95`), one production call site (`saga-fact-handler.ts:140`), one port declaration, three doc-comments — and **every remaining hit is a fake-store member or an assertion against a fake**. Nothing executes the Drizzle query. #8's equivalent *is* covered against a real database at `tests/Orders.IntegrationTests/SagaCommandStoreTests.cs:387` and `:415`, so this is the `CLAUDE.md` rule *"when you port a mechanism, port its guards"* running in reverse, #8 → #7, and the guard was dropped.

**What is unguarded, precisely.** Two of the query's three decisions are substitution-family hazards in the sense `CLAUDE.md` defines — a literal naming a member of a set whose other members also exist here:

- the command tokens `'credit.release'` / `'stock.release'`, two of six `SagaCommandKind` members;
- the column `sagaCommands.triggeringEventEnvelope`, which has a sibling `json` column (`payload`) that type-checks identically.

Either substitution compiles, passes lint, passes typecheck and leaves all 539 Orders tests green, while `hasAcceptedOperatorCancel` answers *false for every order* — which is exactly the defect A2's probe renders visible: the late `credit.approved.v1` then takes the ordinary path and returns `enqueued: 'despatch.create'`, ordering a despatch for an order being cancelled.

**Why it does not block.** Four reasons, in order of weight:

1. **Every acceptance bullet of id 79 is met**, including bullet 6's arming requirement, verified first-hand above. No bullet asks for a real-database test of the store.
2. The **predicate** is guarded and armed twice (A5, A6) and the **call site** is guarded and armed twice (A2, A8 — the latter pinning transaction identity). Only the two-line query between them is naked, and it is naked in a typed schema, so nothing but a valid-sibling substitution can reach it.
3. #7 is a **completed** assessment with no open backlog of its own; closing this needs a new Testcontainers spec and a container trio, which is a budgeted decision rather than a correction.
4. Phase 14's own stopping rule applies: *"is this defect real?"* and *"is this defect worth the budget?"* are different questions, and this review is answering the second deliberately.

**Routing, which is the part that must not be prose.** The disclosure may **not** be discharged by "the next feature that touches the saga command store will cover it" — that exact sentence has failed twice in this trilogy. I therefore propose a numbered backlog entry for the leader to file (I am not the writer of new backlog entries):

> **Proposed entry — `#7's hasAcceptedOperatorCancel query has no real-database guard`** (phase 14 or later, `sdd: false`).
> Acceptance: (a) a spec in `apps/orders/src` instantiating the real `DrizzleSagaCommandStore` against the existing MySQL Testcontainers fixture — the harness already exists at `saga-command-retry.integration.spec.ts:104` and `saga-command-dead-letter.integration.spec.ts:119` — asserts that a row carrying a **real fact's** envelope answers `false` and a row carrying the **synthetic operator-cancel** envelope answers `true`, mirroring #8's `SagaCommandStoreTests.cs:387`/`:415`; (b) the guard is armed by **substitution**, not deletion: repoint one of the two command tokens at a valid sibling (`despatch.create`) and repoint `triggeringEventEnvelope` at `payload`, and record both verbatim failures; (c) if the false negative appears — the substituted sibling produces no row, so the test fails for the wrong reason — the failure message must name what was intended to break.
> Re-open trigger if dispositioned rather than fixed: the moment any third caller of `hasAcceptedOperatorCancel` is added, since the bounded two-row assumption in its doc-comment is what makes the TypeScript-side predicate sufficient.

The record's second disclosure (§747 — no end-to-end integration coverage of the late-approval path in **either** repository) belongs in the same entry as an optional second criterion; it is a parity observation, not a #7 defect.

## 4. Acceptance bullet 3's completeness — both shapes implemented, both guarded, both armed

`specs/shared/saga.md:251-259` names two shapes. Both are in the code, in one expression at `saga-fact-handler.ts:137-140`:

```ts
const lateForAnAcceptedOperatorCancel =
  order.status === 'cancelled'
    ? order.cancellationReason === 'operator_cancelled'
    : order.status === 'stock_reserved' && (await this.commandStore.hasAcceptedOperatorCancel(tx, order.id));
```

Both are guarded by their own named case — `saga-fact-handler.spec.ts:349` (still `stock_reserved`) and `:382` (already `cancelled` with `operator_cancelled`) — and the block also pins the two ways it must **not** widen: `:399` (`stock_reserved` with no accepted cancel takes the ordinary R21 path) and `:413` (`cancelled` for any other reason falls through to R25).

**Both shapes armed, by my own A2 probe**, and they fail for different reasons — which is what proves a single-shape guard is not standing in for two:

```
FAIL … > still stock_reserved with the cancellation accepted: enqueues credit.release ONLY …
-   "enqueued": "credit.release"      +   "enqueued": "despatch.create"      (saga-fact-handler.spec.ts:356)
FAIL … > already cancelled with reason operator_cancelled: enqueues credit.release ONLY …
AssertionError: expected { outcome: 'ignored' } to deeply equal { outcome: 'processed', …(1) }   (:392)
Tests  2 failed | 537 passed (539)
```

The spec's parenthetical *"(reason `order_cancelled`)"* is satisfied without a payload field: `saga-command-payloads.ts:124-139` builds `credit.release` with no `reason`, because `billing.credit.release` always releases with `order_cancelled` — verified in Billing's own source at `apps/billing/src/application/credit-release.handler.ts:52`, not assumed.

## 5. The deliberate divergence from #8 (record §661) — the justification holds

#8 signals the fast path only on `EnqueueOutcome.Enqueued`; #7's late-approval branch sets `enqueued = 'credit.release'` on either outcome. Verified against the idiom it cites: the **generic** enqueue site in the same file, `saga-fact-handler.ts:191-209`, does exactly the same thing (`enqueued = step.commandAfter`, unconditional) and carries the D1 comment explaining why — *"Either outcome reports the SAME command as owed, so the fast path always re-dispatches the row that actually exists — a `sent` row is a silent no-op there, a `pending`/`parked` one is (re-)dispatched."* Fifty lines apart, in one file: following #8 here would have produced two different conventions for the same question. The divergence is correct, is disclosed in the record rather than smoothed over, and its one consequence (the `(order_id, command)` unique key making a second `credit.release` resolve to `already_owed`) is itself disclosed at §744. **Accepted.**

## 6. Scope and process

- **#7's working tree** holds exactly 24 modified + 3 untracked files, all under `apps/orders/src` and `apps/fulfillment/src`, and the set matches the record's file list item for item (12 production + 12 test + 3 new). No `specs/`, no `feature_list.json`, no `package.json`, no config.
- **#8's working tree** holds exactly two entries: `M feature_list.json` (the leader's transition) and the untracked impl record. **Nothing under `src/`, `tests/` or `specs/` in #8 was touched.**
- **Both repositories' `git stash list` are empty** and neither reflog contains a `reset: moving to HEAD`, so the phase-14 stash incident did not recur.
- **The retired wording, swept independently by me** — not by re-reading the record. `'credit_release', 'stock_release'` in that order: **zero** hits across `apps/`. `credit hold.*first` / `CreditReleasedForCancellation` / `reverse order of acquisition`: three hits, all three post-SA-4 and correct (`cancel-order.handler.ts:10` now reads *"stock reservation FIRST, THEN credit hold"*; `:27` explains what reverse-order-of-acquisition alone *would* have suggested and why it is wrong here; `saga-dispatch.events.ts:63` documents the rename). `released before stock` / `before the stock` / `credit before`: one hit, in `apps/billing/src/invoice-issue.integration.spec.ts:67`, about a credit *balance*, unrelated. **No residue of the retired claim.**
- **The defeat list** (record §722) is stated per attack with a reason for each N/A, and the three N/A claims are sound: attacks 4, 5 and 6 target text scanners, and every guard here executes code.

---

## CHECKPOINTS.md — walked

Sections applicable to a feature whose code landed in #7 and which touched no #8 source.

**C1 — harness complete**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` exist
- [x] `progress/current.md` and `progress/history.md` exist
- [x] `.claude/agents/` holds the five agents (unchanged by this feature)
- [x] Every agent definition declares its model (unchanged by this feature)
- [~] `./init.sh` exits 0 — not re-run in this scoped review; no harness file was touched by this feature and #8's tree is clean apart from `feature_list.json` and the record

**C2 — state coherent**
- [x] At most one feature `in_progress` — id 79 was the only one, and this review moves it to `done`
- [x] Every status in `rules.valid_status`
- [x] Every `done` feature has passing tests — id 79's land in #7: Orders 539/539, Fulfillment 92/92, the former re-confirmed first-hand here
- [x] `progress/current.md` describes the active session
- [x] No `blocked` feature introduced

**C3 — architecture** — **not applicable**: no `src/` in #8 was touched; #7's change adds no dependency, no cross-service DB access, and no new interaction. The one new interaction pattern (`stock.released.v1` → `credit.release`) is a Kafka fact driving a NATS RPC, correctly classified, reusing the existing `billing.credit.release` subject.

**C4 — verification is real**
- [x] Tests are real and fail when the behaviour regresses — five mutations re-armed here, five suites went red, message-for-message
- [x] No Jest — the runner is vitest in #7, xUnit in #8
- [~] `./quality.sh` / coverage gates — #8's suites were untouched and not re-run; #7's `pnpm lint` and `pnpm typecheck` are the implementer's, and #7's pre-existing repo-wide `prettier` failure (740 files at HEAD, reproduced by the implementer against a `git show HEAD:` copy) is disclosed and is not a gate in `pnpm quality`

**C5 — session closed cleanly**
- [x] No suspicious untracked files — #7 has three, all named source/spec files; #8 has one, the record
- [x] `progress/history.md` entry appended, **with its effort record**
- [x] `feature_list.json` reflects true state — id 79 set `done` by this review, single-line edit
- [x] Claude did not commit and did not push, in either repository

**C6 — SDD** — **not applicable**: id 79 is `"sdd": false`, so no `specs/<name>/` is owed. The ported-idiom ledger obligation therefore falls on `progress/impl_<feature>.md`, where it is present (record §689-702), reversed to *"#8 relied on X; in #7 that property is supplied by Y"* because the port direction is #8 → #7, with each history half cited to a file and line in #8's checkout and each guard named. Checked the two most likely to be assumed: the transaction-snapshot row (its guard A8 asserts the `tx` object is identically the one `runOnce` handed out, so it executes the code the row is about) and the *"nothing to build"* row for `stepVariantsFor`/`stepForStatus`, which is a claim of pre-existence and is true on disk at `saga-steps.ts:275-305`.

**C7 — spec-reuse fidelity**
- [x] `specs/shared/` untouched in both repositories by this feature — verified by `git status` in each, not from memory
- [x] No silent fork: SA-4 was the human-gated amendment; this entry is its #7 code alignment, which is precisely the routing `CLAUDE.md` prescribes
- [x] `progress/history.md` effort record present and honest

---

## Effort record (appended to `progress/history.md`)

**Sessions:** 1 implementer session, 0 review rounds before this one, 1 review session (this one).
**Wall-clock:** implementer ≈ **57 minutes** — bounded by the filesystem rather than by self-report: the leader's last pre-dispatch write to `progress/current.md` at 08:51, the earliest last-write among the 27 changed #7 files at 09:20, the implementer's own arming backups at 09:32, the record completed at 09:48. Review ≈ **35 minutes**, six full Orders suite runs (five armed, one confirming green).

## Defects

**None blocking.** One routed gap (§3), proposed as a numbered backlog entry for the leader to file. One parity observation (record §747) folded into the same entry.
