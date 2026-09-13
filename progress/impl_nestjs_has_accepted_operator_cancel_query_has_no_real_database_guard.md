# impl — backlog id 91: `hasAcceptedOperatorCancel` gets a real-database guard in #7

**Verdict: PASS.** One new integration spec in `order-to-cash-nestjs`, 8/8 green; #7's Orders unit suite unchanged at **539/539 across 54 files**; no production code changed (`git diff --numstat` on the store is `29 0`, which is id 79's own pre-existing uncommitted SA-4 addition, zero deletions, no mutation residue).

## What was built

One file, in #7 (`/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`):

- `apps/orders/src/infrastructure/saga/saga-command-store-operator-cancel.integration.spec.ts` — new, 8 cases, real MySQL (`mysql:8.4.11`, Testcontainers).

Nothing else was touched in either repository. `feature_list.json`, `specs/shared/`, #8's `src/`/`tests/`, and all config are untouched.

### Why the MySQL-only fixture

`startOrdersTestFixture()` (`apps/orders/src/infrastructure/persistence/test-support/orders-test-fixture.ts`) starts ONE `MySqlContainer`, migrated from empty. `hasAcceptedOperatorCancel` is a pure database read, so the saga trio's Kafka and NATS containers would buy nothing; the whole file runs in ~11s against a container the fixture starts and stops itself. No order row is needed — `saga_commands.order_id` is `char(36) notNull` with no foreign key (`saga-commands.schema.ts:44`) — so each case generates its own order id, which also keeps `uq_saga_commands_order_command` out of the way. The fixture worked exactly as briefed; no escalation to the trio was needed.

The spec instantiates the REAL `DrizzleSagaCommandStore` and the REAL `DrizzleUnitOfWork`, and both the write (`enqueue`) and the read (`hasAcceptedOperatorCancel`) go through a genuine open MySQL transaction, i.e. the same `(tx, orderId)` signature `SagaFactHandler` uses at `saga-fact-handler.ts:140`. `OPERATOR_CANCEL_EVENT_TYPE` is imported from `application/operator-cancel-envelope.ts`; the literal `'orders.cancel.requested'` appears nowhere in the spec.

### The eight cases, against the five required assertions

| # | Case (test name, abbreviated) | Required bullet |
|---|---|---|
| 1 | `stock.release` + REAL fact envelope (`credit.rejected.v1`, R27) → **false** | required 1 — mirrors #8 `SagaCommandStoreTests.cs:387` |
| 2 | `stock.release` + SYNTHETIC operator-cancel envelope → **true** | required 2 — mirrors #8 `SagaCommandStoreTests.cs:415` |
| 3 | `credit.release` + REAL fact envelope (`stock.released.v1`) → **false** | required 3 |
| 4 | `credit.release` + SYNTHETIC envelope → **true** | required 3 |
| 5 | `stock.reserve` AND `despatch.create` rows, both carrying the SYNTHETIC envelope → **false** | required 4 (the narrowing) |
| 6 | `credit.release` whose **payload** column holds the operator-cancel envelope while its **envelope** column holds a real fact → **false** | the column (S2's name-reason discharge) |
| 7 | the production interleave: fact-driven `credit.release` (real fact) **plus** the operator's own `stock.release` (synthetic), operator row physically second → **true** | content, not row position — #8's own point at `SagaCommandStoreTests.cs:342` |
| 8 | no rows at all → **false** | required 5 |

Four cases expect `false` and four expect `true`, deliberately: a mutation that merely empties the result set can only break `true` cases, and "no row matched" is indistinguishable from "the row was never inserted". Every arming below therefore has at least one **false → true** flip to point at, which no absence can produce. That is the acceptance bullet (c) mechanism, designed into the case list rather than bolted on afterwards.

## Arming table

Protocol per mutation: `cp` backup → mutate → `tsc --noEmit` (to prove the mutation compiles and type-checks, which is the whole premise of the entry) → run the ONE spec → record verbatim → restore from backup → `cmp` against backup → confirming green run. No `git checkout`/`stash`/`reset`/`restore`/`clean` was run at any point; the only git commands used were `diff`, `status`, `log` (read-only). Vitest transforms TypeScript per run, so there is no compiled-artefact staleness hazard here; `tsc` was nonetheless re-run clean after the final restore.

| Arm | Mutation (in `drizzle-saga-command-store.ts`) | `tsc` | Result | Named test that failed for the NAME reason |
|---|---|---|---|---|
| S1a | `:103` `'stock.release'` → `'despatch.create'` | exit 0 | 3 failed / 5 passed | case 5 (narrowing), **false → true** |
| S1b | `:103` `'credit.release'` → `'despatch.create'` | exit 0 | 2 failed / 6 passed | case 5 (narrowing), **false → true** |
| S2 | `:98` `sagaCommands.triggeringEventEnvelope` → `sagaCommands.payload` | exit 0 | 4 failed / 4 passed | case 6 (payload column), **false → true** |
| S3 | the `or(...)` command filter removed entirely (`.where(eq(orderId))`) | exit 0 | 1 failed / 7 passed | case 5 (narrowing), **false → true** |
| S4 | `isOperatorCancelEnvelope(...)` → `(... ) !== null` (content check dropped, "any release row counts") | exit 0 | 3 failed / 5 passed | cases 1, 3, 6, **false → true** |

All five mutations type-check, which is the point: none of them is visible to the compiler, and before this spec existed all five left every test in the repository green.

### Verbatim failure messages

**S1a** — `'stock.release'` → `'despatch.create'`; 3 failed (cases 2, 5, 7). The name-reason failure:

```
 FAIL  src/infrastructure/saga/saga-command-store-operator-cancel.integration.spec.ts > DrizzleSagaCommandStore.hasAcceptedOperatorCancel — SA-4, real MySQL (mysql:8.4.11, Testcontainers) > answers false for rows of OTHER commands carrying the SYNTHETIC envelope — stock.reserve and despatch.create are outside the two release commands the query scans
AssertionError: expected true to be false // Object.is equality

- Expected
+ Received

- false
+ true

 ❯ src/infrastructure/saga/saga-command-store-operator-cancel.integration.spec.ts:240:32
    238|     await enqueueRow(orderId, 'despatch.create', envelope, ORDERS_FACT…
    239|
    240|     expect(await ask(orderId)).toBe(false);
       |                                ^
```

The other two S1a failures (cases 2 and 7) read `AssertionError: expected false to be true` — those are absence-shaped and are NOT offered as evidence.

**S1b** — `'credit.release'` → `'despatch.create'`; 2 failed (cases 4, 5). The name-reason failure is the same case 5 block, verbatim:

```
 FAIL  … > answers false for rows of OTHER commands carrying the SYNTHETIC envelope — stock.reserve and despatch.create are outside the two release commands the query scans
AssertionError: expected true to be false // Object.is equality

- Expected
+ Received

- false
+ true

 ❯ src/infrastructure/saga/saga-command-store-operator-cancel.integration.spec.ts:240:32
```

**S2** — the sibling `json` column; 4 failed (cases 2, 4, 6, 7). The name-reason failure:

```
 FAIL  … > answers false when the operator-cancel envelope is in the PAYLOAD column and the envelope column holds a real fact — the answer comes from triggering_event_envelope, not from payload
AssertionError: expected true to be false // Object.is equality

- Expected
+ Received

- false
+ true

 ❯ src/infrastructure/saga/saga-command-store-operator-cancel.integration.spec.ts:264:32
    262|     );
    263|
    264|     expect(await ask(orderId)).toBe(false);
       |                                ^
```

**S3** — narrowing dropped; 1 failed (case 5 only, which is exactly right: without the `or(...)` every other case's expectation is unaffected):

```
 FAIL  … > answers false for rows of OTHER commands carrying the SYNTHETIC envelope — stock.reserve and despatch.create are outside the two release commands the query scans
AssertionError: expected true to be false // Object.is equality

- Expected
+ Received

- false
+ true

 ❯ src/infrastructure/saga/saga-command-store-operator-cancel.integration.spec.ts:240:32
```

**S4** — content check dropped; 3 failed (cases 1, 3, 6):

```
 FAIL  … > answers false for a stock.release row carrying a REAL fact envelope — R27 automatic compensation is not an operator cancellation
AssertionError: expected true to be false // Object.is equality

- Expected
+ Received

- false
+ true

 ❯ src/infrastructure/saga/saga-command-store-operator-cancel.integration.spec.ts:186:32
    184|     );
    185|
    186|     expect(await ask(orderId)).toBe(false);
       |                                ^
```

### The substitution family's false negative (acceptance bullet c), checked per arm

The trap: a substituted sibling that matches no row makes the query return the empty set, the answer is `false`, and the `true` cases fail for the ABSENCE reason — which is not evidence about the NAME. Checked explicitly:

- **S1a / S1b.** The substituted sibling `despatch.create` **is present** for the order in case 5, carrying the synthetic envelope, so the mutated query reads a real row and answers `true` where `false` is expected. The failure is `expected true to be false` — a flip only reachable by counting a row that the unmutated narrowing excludes. Had case 5 not existed, the only failures would have been `expected false to be true`, and the arm would have proved nothing about the token. This is why the decoy rows were added, not an afterthought.
- **S2.** The substituted sibling column `payload` is `notNull`, so every row has one, and case 6's payload deliberately holds a copy of the operator-cancel envelope (a shape production never writes) while its envelope column holds a real fact. The mutated read answers `true`; failure `expected true to be false`. No absence is possible.
- **S3.** Dropping the filter can only ever widen the result set, so absence is structurally impossible; the flip is `expected true to be false` on case 5.
- **S4.** Every row's envelope column is non-null in cases 1, 3 and 6, so the mutated predicate answers `true` where `false` is expected. Failure is `expected true to be false`.

In every arm the recorded evidence is a `false → true` flip. No arm is recorded on the strength of an `expected false to be true` message alone.

## Defeat list (CLAUDE.md's ten attacks) run against this guard

| # | Attack | Ran? |
|---|---|---|
| 1 | Delete the behaviour | **Ran** — S3 deletes the narrowing; S4 deletes the content check. A degenerate `return false` would be caught by cases 2, 4 and 7, though only absence-shaped, which is why S1–S4 are the arms recorded. |
| 2 | Corrupt a field the test supplied | **Ran** — S4 is exactly this on the field that decides (`eventType`, supplied by the test through the imported constant): dropping the read of it flips three cases. Corrupting the constant itself is id 79's A5/A6 in `operator-cancel-envelope.spec.ts` and is not re-armed here. |
| 3 | Substitute a valid sibling identifier | **Ran** — S1a and S1b (two of six `SagaCommandKind` members) and S2 (the sibling `json` column). Both sibling families are real and enumerable: `SAGA_COMMAND_KINDS` (six members) and the two `json` columns on `saga_commands`. |
| 4 | Shadow the pattern from a comment or string literal | **Not applicable** — this guard executes production code against a real database; it is not a text scanner, and there is no pattern to shadow. |
| 5 | Hide the real thing in a dead region (`#if false`) | **Not applicable** — TypeScript has no conditional compilation, and the spec executes the shipped module through the normal import graph, so no region can be live for the compiler and dead for the test. |
| 6 | Hide it in a raw or verbatim string | **Not applicable** — same reason as 4; nothing here parses source text. |
| 7 | Drop an OPTIONAL element entirely | **Ran, by construction** — the absence case IS asserted (case 8, no rows → `false`), and the optional `note` on the synthetic envelope is not read by the predicate, so its presence cannot carry the assertion. No assertion in this spec compares presence only; all eight compare a boolean both ways. |
| 8 | Compare a literal to a literal | **Not applicable** — the actual side of every assertion is a value returned by the real store from a real MySQL round-trip; only the expected side is a literal. The one string literal that matters (`orders.cancel.requested`) is imported, never retyped, so the spec cannot agree with itself while disagreeing with production. |
| 9 | Satisfy the closer half and leave the premise half stale | **Ran** — the entry's premise ("27 hits, none executing the SQL") was re-enumerated after the change rather than assumed; see the enumeration below. It also found the entry's bullet-4 premise to be half wrong (see "The optional criterion"). |
| 10 | Let a build-output copy join the population | **Ran** — the enumeration excludes `node_modules/`, `dist/` and `coverage/` **by path** in `find`, never by post-filtering `grep` output. |

## Enumeration (the negative claim, as a search result)

Command (path-based exclusions, run in `order-to-cash-nestjs`):

```
find . -name '*.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' -not -path '*/coverage/*' -print0 \
  | xargs -0 grep -n "hasAcceptedOperatorCancel" | sed 's/:.*hasAcceptedOperatorCancel.*/ :: HIT/' | sort | uniq -c | sort -rn
```

Complete output after this change, classified file by file (31 hits; 27 before, +4 in the new spec):

| Hits | File | Classification |
|---|---|---|
| 7 | `apps/orders/src/application/saga-fact-handler.spec.ts` | fake store + assertions against the fake |
| 6 | `apps/orders/src/infrastructure/saga/saga-command-sweeper.spec.ts` | fake store member |
| 4 | `apps/orders/src/infrastructure/saga/saga-command-store-operator-cancel.integration.spec.ts` | **new — the only execution of the real SQL** |
| 2 | `apps/orders/src/infrastructure/saga/saga-command-sweeper-log-trace-id.spec.ts` | fake store member |
| 2 | `apps/orders/src/application/saga-fact-handler.ts` | production call site + doc-comment |
| 2 | `apps/orders/src/application/operator-cancel-envelope.spec.ts` | doc-comment + `vi.fn()` stub |
| 1 | `apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts` | fake store member |
| 1 | `apps/orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts` | fake store member |
| 1 | `apps/orders/src/infrastructure/saga/drizzle-saga-command-store.ts` | the implementation |
| 1 | `apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts` | fake store member |
| 1 | `apps/orders/src/application/ports/saga-command-store.port.ts` | the port declaration |
| 1 | `apps/orders/src/application/operator-cancel-envelope.ts` | doc-comment |
| 1 | `apps/orders/src/application/cancel-order.handler.ts` | doc-comment |
| 1 | `apps/orders/src/application/cancel-order.handler.spec.ts` | `vi.fn()` stub |

The entry's premise is confirmed exactly: before this spec, every test-side hit was a fake, and the SQL between the guarded predicate and the guarded call site was executed by nothing.

## Porting #8's guards (the "port its guards" rule, run #8 → #7)

Command, in `order-to-cash-dotnet`:

```
find tests src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -n "HasAcceptedOperatorCancel"
```

Classification of the #8 **test-side** hits that assert about this method (the rest are fake-store members or doc-comments):

| #8 test | Assertion | Ported? |
|---|---|---|
| `SagaCommandStoreTests.cs:387` `..._ARealFactEnvelopeOnStockRelease_ReturnsFalse` | real fact envelope on `stock.release` → false | **ported** — case 1 |
| `SagaCommandStoreTests.cs:415` `..._TheSyntheticOperatorEnvelopeOnStockRelease_ReturnsTrue` | synthetic envelope on `stock.release` → true | **ported** — case 2 |
| `SagaFactHandlerTests.cs:553,577,658` | call-site behaviour against a fake (`HasAcceptedOperatorCancelCalls`) | not applicable — #7's equivalent already exists (`saga-fact-handler.spec.ts:374,396,409`), armed by id 79's A2/A8 |
| `OperatorCancelRacesSagaForwardProgressTests.cs` (`StockReserved_LateApproval_BeforeStockReleased` and the `cancelled`-status ordering) | end-to-end late-approval path | **NOT ported** — see below |
| `SagaConsumptionTests.cs:304`, `SagaCommandRetryTests.cs:309` | delegating decorators, not assertions | not applicable |

Cases 3–8 of the new spec are additions beyond #8's two: #8 guards only the `stock.release` side and neither the narrowing nor the column, so the substitutions this entry is about would have been armable in #7 but **not** in #8 by #8's own two tests. That is worth carrying to #9: the property "the query scans exactly these two commands, and reads exactly the envelope column" needs the `false`-expecting decoy cases to be armable at all, in either stack.

## Ported-idiom ledger (reverse direction, #8 → #7)

This is an `sdd: false` feature, so the ledger lives here. The port runs backwards for once — a guard written for #8 landing in #7 — so the rows read "#8 relied on X; in #7 that property is supplied by Y".

| # | Property | #8 relied on | In #7 supplied by | Guard |
|---|---|---|---|---|
| L1 | Per-case database isolation | a fresh database per test — `mssql.CreateFreshDatabaseAsync($"otc_orders_sagastore_{Guid.NewGuid():N}")` then `MigrateAsync()`, at `SagaCommandStoreTests.cs:389-393` | one shared container for the file, plus a freshly generated `order_id` per case and the `(order_id, command)` unique key (`saga-commands.schema.ts:77`) | `enqueueRow`'s `expect(outcome).toBe('enqueued')`, executed on all 9 inserts: any cross-case collision would return `already_owed` and fail loudly rather than silently reusing a row |
| L2 | The envelope comes back as a parsed object, so the predicate's `typeof envelope === 'object'` holds | explicit deserialisation at the boundary — the envelope is stored as bytes/`nvarchar(max)` and parsed by the store (`IsOperatorCancelEnvelope(envelope, out _)`) | **mysql2's automatic `json` column parsing**: nothing in `hasAcceptedOperatorCancel` calls `JSON.parse`; if the driver returned the column as a string, `isOperatorCancelEnvelope` would answer `false` for every order and `hasAcceptedOperatorCancel` would silently be a constant `false` | cases 2, 4 and 7 — they can only pass if the driver hands the predicate an object. This is a genuine engine-supplied property and until now it was asserted nowhere in #7 |
| L3 | The synthetic `eventType` is one value shared by writer and reader | a single C# constant used by both sides | `OPERATOR_CANCEL_EVENT_TYPE` exported from `application/operator-cancel-envelope.ts` | the spec **imports** the constant; the literal appears nowhere in it, so a rename cannot leave the test agreeing with a stale copy |
| L4 | The read happens inside the caller's transaction | the ambient `OrdersDbContext` under `READ_COMMITTED_SNAPSHOT`, after the caller's `UPDLOCK` read of the order row (`EfCoreSagaCommandStore.cs:366-372`) | an explicit `TransactionContext` parameter unwrapped by `asDrizzleTx`, opened by `DrizzleUnitOfWork.execute` | `ask()` calls through `unitOfWork.execute`, so the real signature is exercised; the guard does **not** assert the snapshot semantics themselves, which remain untested in both repositories (recorded, not claimed) |

L2 is the row worth keeping. The others document how the guard was made equivalent; L2 names a correctness property that #7 gets from its driver and that nothing in #7 checked before this spec.

## The optional criterion (acceptance bullet 4) — NOT closed, and its premise is corrected

The bullet says "neither repository has end-to-end integration coverage of the late-approval path". **That is wrong about #8.** `tests/Orders.IntegrationTests/OperatorCancelRacesSagaForwardProgressTests.cs` covers it, committed in `b453931`, in both orderings — `StockReserved_LateApproval_BeforeStockReleased` (documented at `:450-459` as "the one reachable production caller" of `HasAcceptedOperatorCancelAsync`) and the already-`cancelled` ordering, which asserts exactly one `credit.release` row, zero `despatch.create` rows, one `order.cancelled.v1` and no `order.confirmed.v1`. So the gap is **one-sided: #7 only**.

Checked for #7 and confirmed absent: `LATE_CREDIT_APPROVAL_EVENT_TYPE` appears only in `apps/orders/src/application/saga-fact-handler.ts`, and the four `it(...)` cases in `orders-cancel.integration.spec.ts` are OCR-placed, OCR-stock_reserved, OCR-stock-first and OCR-terminal — none of them delivers a `credit.approved.v1` after an accepted operator cancellation.

**Not closed here, because it is not cheap.** Closing it in #7 requires `startSagaIntegrationHarness`, whose `prepareSagaFixtures` starts **three** containers (`startOrdersTestFixture()`, `startKafkaTestFixture()`, `startNatsTestFixture()`, `saga-integration-harness.ts:178-182`), creates six topics, brings up the Nest app and its Kafka consumer group, and needs a stub responder set plus an order driven to `stock_reserved` before the late fact can be published. That is a new spec of the size of `orders-cancel.integration.spec.ts`, not an addition to this one, and it would have expanded this feature well past the "one spec file" scope the entry's own recommendation rests on. Recorded as an open parity gap in #7 rather than done: if it is wanted, it belongs in its own backlog entry, with #8's `OperatorCancelRacesSagaForwardProgressTests` as the specification of what to assert.

Acceptance bullet (e), the re-open trigger, is unchanged and still applies to the entry: the TypeScript-side predicate is sufficient only while the query is bounded to at most two rows by `(order_id, command)`, so a third caller of `hasAcceptedOperatorCancel` re-opens the question.

## Counts, and the reconciliation

| Run | Result |
|---|---|
| New spec, first green run (before any arming) | **8 passed (8)**, 1 file |
| S1a | 3 failed \| 5 passed (8) |
| green after S1a restore | **8 passed (8)** |
| S1b | 2 failed \| 6 passed (8) |
| S2 | 4 failed \| 4 passed (8) |
| S3 | 1 failed \| 7 passed (8) |
| S4 | 3 failed \| 5 passed (8) |
| New spec, final green run (after the last restore + `cmp`) | **8 passed (8)**, 1 file |
| Orders unit suite (`vitest run`, `vitest.config.mts`) — run twice, after S3's restore and after S4's | **539 passed (539) across 54 files**, both times |
| `tsc -p tsconfig.json --noEmit` after the final restore | exit 0 |
| `prettier --check` on the new spec | clean |
| `eslint` on the new spec | clean |

The unit count is **exactly the 539/539 across 54 files baseline** — nothing moved, and nothing should have: the new file is `*.integration.spec.ts`, which `vitest.config.mts` excludes explicitly (`exclude: ['**/node_modules/**', 'src/**/*.integration.spec.ts']`). The new file adds **8 tests to the integration suite**, which is a separate config and a separate gate. No number moved the wrong way and there is nothing left to reconcile.

## Surprises, and what I could not do

- **The entry's own bullet 4 was wrong about #8.** The late-approval path is covered end-to-end there, and in both orderings; only #7 lacks it. Worth noting because the bullet was written as a symmetric parity observation and would have been inherited as one.
- **#8's two tests would not, by themselves, have caught the defect this entry is about.** They are both `stock.release` cases and neither asserts a `false` where the substituted sibling is present, so a token or column substitution in #8 flips them absence-shaped at best and leaves the narrowing entirely unguarded. Cases 3–8 are #7 additions, and #9 should start from the eight rather than from #8's two.
- **The store file was already dirty in #7** — 29 uncommitted insertions, id 79's SA-4 addition, present before this work started. Every restore was therefore verified with `cmp` against a working-tree backup, and the final `git diff --numstat` (`29 0`, zero deletions) confirms the tree is back exactly where it was.
- I could not make any arming fail for a reason other than a `false → true` flip without designing the decoy cases in up front; the four `false`-expecting cases are load-bearing for the entry's bullet (c) and should not be removed by a later refactor without replacing the flip they provide.
