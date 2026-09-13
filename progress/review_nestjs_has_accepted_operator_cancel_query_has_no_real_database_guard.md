# review — backlog id 91: `hasAcceptedOperatorCancel` gets a real-database guard in #7

**Verdict: APPROVED.**

Deliberately minimal review, as briefed. The leader had already verified first-hand, and this review did NOT repeat: the 8/8 green run of the new spec, the "no production change" claim (`git diff --numstat` = `29 0` on the store, 28 porcelain entries in #7), the imported-constant check, the MySQL-only-fixture check, the unmoved 539/539 Orders unit baseline, and the correction to acceptance bullet 4. **This review's own work was the independent re-arming**, from scratch, against the tree on disk, plus the traceability and "does the guard execute the code" judgement below.

## What I re-armed myself (3 of 5, the three the brief prioritised)

Protocol per arm: `cp` backup (`md5 0f26de0fef114340e48b3d085e553fe0`) → mutate `apps/orders/src/infrastructure/saga/drizzle-saga-command-store.ts` → `npx tsc -p tsconfig.json --noEmit` → run the ONE spec → restore from backup → `cmp` against backup. No `git checkout`/`stash`/`reset`/`restore`/`clean` was used at any point; the only git commands run were `diff`, `status`, `show`-class reads. Vitest transforms TypeScript per run, so there is no stale-binary hazard; `cmp` + `md5sum` is therefore sufficient restore evidence here, and the store file being already dirty (id 79's 29 uncommitted insertions) is why `git diff` was not used as restore evidence.

| Arm | Mutation | `tsc` | Result (mine) | Recorded | Name-reason failure |
|---|---|---|---|---|---|
| **S2** | `:98` `sagaCommands.triggeringEventEnvelope` → `sagaCommands.payload` | exit 0 | **4 failed \| 4 passed (8)** | 4 \| 4 | case 6, `spec.ts:264`, `AssertionError: expected true to be false` — **false → true** |
| **S1a** | `:103` `'stock.release'` → `'despatch.create'` | exit 0 | **3 failed \| 5 passed (8)** | 3 \| 5 | case 5, `spec.ts:240`, `AssertionError: expected true to be false` — **false → true** |
| **S3** | the `or(...)` command filter removed (`.where(eq(sagaCommands.orderId, orderId.value))`) | exit 0 | **1 failed \| 7 passed (8)** | 1 \| 7 | case 5, `spec.ts:240`, `AssertionError: expected true to be false` — **false → true**, and it is the ONLY failure |

All three reproduce the record's counts **exactly**, and all three reproduce the recorded messages verbatim. S1b and S4 were not re-armed (budget); their recorded shape is consistent with the three that were, and S4's recorded failure (case 1 at `:186`) is likewise a `false → true` flip.

### Acceptance bullet (c) — the substitution family's false negative — independently confirmed

This is the entry's whole point and it holds. In each of the three arms the failure I offer as evidence is a case that **expects `false` and received `true`**:

- **S2**: `payload` is `notNull`, so every row has one; case 6's payload deliberately holds a copy of the operator-cancel envelope while its envelope column holds a real fact. The mutated read counts a row it must not. No empty result set can produce this.
- **S1a**: the substituted sibling `despatch.create` **is present** for case 5's order, carrying the synthetic envelope, so the mutated narrowing reads a real row. Had case 5 not existed, S1a's only failures would have been the two absence-shaped `expected false to be true` ones — which prove nothing about the token. The decoy rows are load-bearing, exactly as the record says.
- **S3**: dropping a filter can only widen the result set, so absence is structurally impossible; the single failure is the flip.

I confirm none of the three arms rests on an `expected false to be true` message.

### Restore

`cmp` clean after every arm; final `md5sum` of the working file **identical** to the backup; `git diff --numstat` on the store back to **`29 0`** (id 79's pre-existing SA-4 insertions, zero deletions); `git status --porcelain` in #7 back to **28** entries; #8's `src`/`tests` at **0** modifications. Confirming green run after the final restore: **8 passed (8), 1 file, 11.88s**.

## Judgement question: does the guard execute the code the entry is about?

**Yes.** The spec constructs the real `DrizzleSagaCommandStore(fixture.db, …)` and the real `DrizzleUnitOfWork` (`spec:137-141`), and `ask()` (`spec:173-175`) calls `unitOfWork.execute((tx) => store.hasAcceptedOperatorCancel(tx, orderId))` — the production method, inside a genuine open MySQL transaction, with the same `(tx, orderId)` signature `saga-fact-handler.ts:140` uses. It does not re-implement the predicate. The decisive evidence is the arming itself rather than the reading: **all three mutations were made to the production file only, and each one changed the spec's result.** A test re-implementing the SQL would have stayed green under all three.

## Traceability — the entry's five acceptance bullets

| Bullet | Verified | How |
|---|---|---|
| 1 — real store against the MySQL fixture, real-fact → `false`, synthetic → `true`, mirroring #8 `SagaCommandStoreTests.cs:387,:415` | **[x]** | cases 1 and 2 (`spec:177`, `:189`); real store + real UoW, leader-verified 8/8 |
| 2 — armed by SUBSTITUTION (sibling `SagaCommandKind`, sibling `json` column), both verbatim | **[x]** | **re-armed by me**: S1a and S2 above, both `tsc` exit 0, both flips |
| 3 — the false-negative honoured; the message names what was intended to break | **[x]** | **re-verified by me** per arm, table above |
| 4 (optional, corrected) — #7-only late-approval gap | **[x] recorded, not closed** | record §228-234 gives the cost (`startSagaIntegrationHarness`, three containers) and confirms #7's absence by enumeration. Accepted per the brief; not re-litigated. **Routing note below.** |
| 5 — re-open trigger preserved | **[x]** | record §236 restates it unchanged; the bound it rests on (`(order_id, command)` unique key) is the one the store's doc-comment at `:89-93` relies on |

## Checkpoints walked

- **[x]** Tests are real — they execute production code against a real container (judgement above), and they have been seen to fail, by me, three ways.
- **[x]** Integration tests hit a real container, not mocks — `mysql:8.4.11` via Testcontainers.
- **[x]** No production change; no #8 source touched; `specs/shared/` untouched.
- **[x]** Mutation families: substitution (S1a — sibling command token; S2 — sibling `json` column) **and** deletion (S3 — filter removed). The corruption family is covered by the `false`-expecting decoy cases, which is what makes every recorded failure a flip rather than an absence.
- **[x]** Ported-idiom ledger — present in `progress/impl_…md` §215-226, correct for an `sdd: false` feature, and running in the **reverse** direction (#8 → #7), which is right for a back-port. I probed the row most likely to be assumed, **L2** (mysql2's automatic `json` parsing): it is a genuine engine-supplied property, and its named guard is real — under S2 the driver hands the predicate the `payload` object and cases 2/4/7 flip, which could not happen if the column came back as a string. L4 correctly records what it does **not** claim (snapshot semantics, untested in both repositories) rather than overstating it.
- **[x]** Effort record — appended to `progress/history.md`.
- **[ ]** NetArchTest / domain purity / money / wire shape — **not applicable**: no #8 code changed, no domain code anywhere changed.

## Defects

**None blocking.** Two observations, neither a defect:

1. S1b and S4 were re-armed by the implementer only, not by me. Recorded as a scope limit of this review, not as doubt — three of five reproduced exactly, counts and messages.
2. **Routing, not narration** — bullet 4's #7-only late-approval gap is a real, disclosed, open parity gap in a completed assessment. The record correctly declines to close it and says why (it needs the three-container harness, i.e. a new spec the size of `orders-cancel.integration.spec.ts`). Per `CLAUDE.md`, *"the next feature that touches X"* is not a disposition. It is **not** a `specs/shared/` gap, so no `SA-n` is owed — but it must leave an artefact: **the leader should file it as its own numbered backlog entry**, with #8's `tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs` named as the specification of what to assert, and a disposition (`fix` / `accept with evidence`) attached at filing. The reviewer does not write `feature_list.json` entries; this names the obligation rather than discharging it in prose.

## Close

Id 91 set to `done` in `feature_list.json` (single status line edited). No feature left `in_progress`.
