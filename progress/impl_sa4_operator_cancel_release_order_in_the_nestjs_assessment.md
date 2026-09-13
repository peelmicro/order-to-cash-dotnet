# impl — SA-4's operator-cancel release order, implemented in the NestJS assessment (#7)

**Verdict: PASS.** Backlog id 79. All four behaviours are addressed: **B1** and **B2** needed code (and got it), **B3** and **B4** were already satisfied in #7 and got guards instead, each armed. The code changed lives in `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` (#7); nothing in #8's `src/`, `tests/`, either repository's `specs/shared/`, either `feature_list.json`, `package.json` or any config was touched. No git command that writes the index or working tree was run; no commit, no push.

## Suite counts, and the reconciliation

| Suite | Command | Before | After | Δ |
|---|---|---:|---:|---:|
| Orders unit | `pnpm --filter @otc/orders test` | 519 passed / 53 files | **539 passed / 54 files** | +20, +1 file |
| Fulfillment unit | `pnpm --filter @otc/fulfillment test` | 89 passed / 19 files | **92 passed / 20 files** | +3, +1 file |

Every added test is accounted for; nothing was deleted without a replacement, and the two removed cases are named:

| File | Added | Removed | Net |
|---|---:|---:|---:|
| `apps/orders/src/application/saga-steps.spec.ts` | +3 (`reason` read from the fact ×2 statuses, `mapCreditReleaseReason`) | 0 (the two multi-variant cases were rewritten in place, not removed) | +3 |
| `apps/orders/src/application/commands/saga-fact.handlers.spec.ts` | +3 (late-approval event choice; `HandleStockReleasedFactHandler` publishes / publishes nothing) | −1 (the two `HandleCreditReleasedFactHandler` cases collapsed into one: it now owes no command at all) | +2 |
| `apps/orders/src/application/sagas/order.sagas.spec.ts` | +2 (two `credit.release` sources) | −1 (the single old `CreditReleasedForCancellationRecorded` case) | +1 |
| `apps/orders/src/application/saga-fact-handler.spec.ts` | +6 (B2's describe block) | 0 | +6 |
| `apps/orders/src/application/operator-cancel-envelope.spec.ts` *(new)* | +7 | 0 | +7 |
| `apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts` | +1 (B3's guard) | 0 | +1 |
| `apps/orders/src/application/cancel-order.handler.spec.ts` | 0 (rewritten in place) | 0 | 0 |
| `apps/orders/src/presentation/orders-cancel.controller.spec.ts` | 0 (rewritten in place) | 0 | 0 |
| **Orders total** | | | **+20** → 519 + 20 = **539** ✓ |
| `apps/fulfillment/src/application/one-lock-arbitration.spec.ts` *(new)* | +3 | 0 | **+3** → 89 + 3 = **92** ✓ |

**Integration suites (Testcontainers, Docker was available so they were actually run):**

| Spec | Result |
|---|---|
| `apps/orders/src/orders-cancel.integration.spec.ts` | **4 passed** — including the transposed real-wire proof `issuedOrder === ['stock.release', 'credit.release']` over real NATS + Kafka + MySQL |
| `apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts` | 3 passed (R27/R28 untouched) |
| `apps/orders/src/saga-happy-path.integration.spec.ts` + `saga-preconditions.integration.spec.ts` | 4 passed |
| `apps/fulfillment/src/despatch-create.integration.spec.ts` | 5 passed (the existing despatch-vs-release race) |

Not run: the remaining integration specs in either app (unrelated to this change, each spins its own container trio and the suite is serialised). `pnpm typecheck` passes for the **whole workspace** (8 packages, including `apps/web`); `npx eslint apps/orders/src apps/fulfillment/src` is clean; `pnpm contracts:check` is OK. `pnpm format` (prettier) fails repo-wide on **740 files**, 186 of them under `apps/orders/src`/`apps/fulfillment/src` — a **pre-existing** condition, not a gate (`pnpm quality` = lint + typecheck + test:coverage). Demonstrated rather than assumed: `git show HEAD:apps/orders/src/application/saga-steps.ts` piped to a scratch file (a read-only git command; `git stash list` is empty and no index/worktree-writing git command was run at any point) is itself rejected by `prettier --check`.

## The enumeration (acceptance bullet 1) — done FIRST, before any fix

Command, run from `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` **before any edit** (so every line number below is against the pre-change tree), excluding by path at the source rather than post-filtering grep's own output:

```
find apps/orders/src apps/fulfillment/src -name '*.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' -print0 | xargs -0 grep -n 'credit_release\|credit\.release\|stock_release\|stock\.release\|credit\.approved'
```

**275 hits across 48 files.** Complete output:

```
apps/orders/src/saga-happy-path.integration.spec.ts:4:// stub payment.received.v1 + credit.released.v1 -> paid -> completed.
apps/orders/src/saga-happy-path.integration.spec.ts:75:  it('reaches invoiced against stubbed responders, then paid -> completed on stub payment.received.v1 + credit.released.v1, with exactly one order.confirmed.v1 and one order.completed.v1 in the outbox', async () => {
apps/orders/src/saga-happy-path.integration.spec.ts:83:    // R21 — credit.approved.v1 performs both edges (approveCredit + confirm) in one save, owes despatch.create.
apps/orders/src/saga-happy-path.integration.spec.ts:102:    // then credit.released.v1, same causal order the real Billing context
apps/orders/src/saga-happy-path.integration.spec.ts:115:    await publishFact(harness.billingFactPublisher, 'credit.released.v1', order.id.value, {
apps/orders/src/saga-happy-path.integration.spec.ts:124:    // R24 — credit.released.v1 closes the saga: completed, exactly one order.completed.v1.
apps/orders/src/saga-preconditions.integration.spec.ts:57:  'credit.approved.v1',
apps/orders/src/saga-preconditions.integration.spec.ts:59:  'stock.released.v1',
apps/orders/src/saga-preconditions.integration.spec.ts:63:  'credit.released.v1',
apps/orders/src/saga-preconditions.integration.spec.ts:66:/** A minimal-but-valid payload per fact type — content does not matter for a precondition-mismatch redelivery (it is ignored before the aggregate ever reads it), except `stock.released.v1`'s `reason`, which `SAGA_STEPS.reason()` reads unconditionally. */
apps/orders/src/saga-preconditions.integration.spec.ts:68:  if (eventType === 'stock.released.v1') {
apps/orders/src/saga-preconditions.integration.spec.ts:106:    await publishFact(harness.billingFactPublisher, 'credit.released.v1', order.id.value, {
apps/orders/src/saga-preconditions.integration.spec.ts:131:    // redelivered stock.rejected.v1 must never trigger a stock.release.
apps/orders/src/saga-command-retry.integration.spec.ts:42:  command: 'stock.reserve' | 'stock.release' | 'despatch.create' | 'credit.hold' | 'invoice.issue',
apps/orders/src/saga-compensation-stock-rejected.integration.spec.ts:3:// stock.release subject is asserted to observe ZERO requests, including
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:2:// order in stock_reserved and owes stock.release; stock.released.v1 then
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:3:// cancels with one stock_released compensation step built from the
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:27:// credit.rejected.v1 (Kafka) -> stock.release (NATS RPC) ->
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:28:// stock.released.v1 (Kafka) -> cancelled — 4 Kafka round trips + 3 NATS
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:72:    // credit.rejected -> stock.release -> cancelled round trip can complete
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:111:  it('release-then-cancel in causal order, with one stock_released compensation step built from the observed fact (R27, R28, SO7)', async () => {
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:114:    // credit.rejected.v1 owes stock.release — R27's "status unchanged"
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:117:    // strictly on the observed stock.released.v1 fact (not on the reply).
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:131:    expect(payload.compensationSteps[0]).toMatchObject({ step: 'stock_released', eventType: 'stock.released.v1' });
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:140:// credit.rejected.v1 redelivered mid-compensation, while stock.release sits
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:144:// harness/containers: this scenario needs stock.release to have NO responder,
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:153:      // Fast, bounded retry/park budget so a `stock.release` with no
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:183:      .where(and(eq(ordersSchema.sagaCommands.orderId, orderId), eq(ordersSchema.sagaCommands.command, 'stock.release')));
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:192:  it('a distinct-eventId duplicate of credit.rejected.v1 mid-compensation neither crashes the consumer nor creates a second stock.release row, and the existing row is re-dispatched', async () => {
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:195:    // stock.release stays parked — no responder is subscribed
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:211:    // the first delivery's compensation never completed (stock.release
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:226:    // D1's whole point: still exactly one stock.release row, same id — not
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:233:        and(eq(ordersSchema.sagaCommands.orderId, order.id.value), eq(ordersSchema.sagaCommands.command, 'stock.release')),
apps/orders/src/orders-cancel.integration.spec.ts:10://   - `stock_reserved`                  -> `stock.release` (reason
apps/orders/src/orders-cancel.integration.spec.ts:13://                                          `stock.released.v1` fact over REAL
apps/orders/src/orders-cancel.integration.spec.ts:16://   - `credit_approved`/`confirmed`     -> `credit.release` over REAL NATS
apps/orders/src/orders-cancel.integration.spec.ts:19://                                          `stock.release`, proven strictly
apps/orders/src/orders-cancel.integration.spec.ts:72: * `stock.reserve`/`stock.release` — `credit.hold` has NO responder at all
apps/orders/src/orders-cancel.integration.spec.ts:116:      await publishFact(harness.fulfillmentFactPublisher, 'stock.released.v1', orderId, {
apps/orders/src/orders-cancel.integration.spec.ts:143: * automatic `stock.reserved.v1` -> `credit.hold` -> `credit.approved.v1`
apps/orders/src/orders-cancel.integration.spec.ts:144: * chain) — plus `credit.release` and `stock.release`, both required to
apps/orders/src/orders-cancel.integration.spec.ts:147: * issue automatically the instant `credit.approved.v1` lands — so the
apps/orders/src/orders-cancel.integration.spec.ts:152: * acquisition proof this test needs: `credit.release` strictly BEFORE
apps/orders/src/orders-cancel.integration.spec.ts:153: * `stock.release`, not merely "both eventually happen".
apps/orders/src/orders-cancel.integration.spec.ts:190:      await publishFact(harness.billingFactPublisher, 'credit.approved.v1', orderId, {
apps/orders/src/orders-cancel.integration.spec.ts:216:      issuedOrder.push('credit.release');
apps/orders/src/orders-cancel.integration.spec.ts:218:      await publishFact(harness.billingFactPublisher, 'credit.released.v1', orderId, {
apps/orders/src/orders-cancel.integration.spec.ts:244:      issuedOrder.push('stock.release');
apps/orders/src/orders-cancel.integration.spec.ts:246:      await publishFact(harness.fulfillmentFactPublisher, 'stock.released.v1', orderId, {
apps/orders/src/orders-cancel.integration.spec.ts:319:  it('OCR-stock_reserved — issues stock.release over real NATS; a real stock.released.v1 over real Kafka completes the cancellation', async () => {
apps/orders/src/orders-cancel.integration.spec.ts:337:    expect(success.compensationPlanned).toEqual(['stock_release']);
apps/orders/src/orders-cancel.integration.spec.ts:345:    // The stub fulfillment responder answers stock.release with reason
apps/orders/src/orders-cancel.integration.spec.ts:346:    // 'order_cancelled' echoed back, then publishes a REAL stock.released.v1
apps/orders/src/orders-cancel.integration.spec.ts:348:    // because saga-steps.ts's stock.released.v1 step was already
apps/orders/src/orders-cancel.integration.spec.ts:360:  it('OCR-credit-release — issues credit.release strictly BEFORE stock.release (reverse order of acquisition, saga.md §4.3); real credit.released.v1 + stock.released.v1 facts over real Kafka complete the cancellation', async () => {
apps/orders/src/orders-cancel.integration.spec.ts:368:    // stock.reserved.v1 -> credit.hold -> credit.approved.v1, which
apps/orders/src/orders-cancel.integration.spec.ts:371:    // `confirmed`, per saga-steps.ts's own credit.approved.v1 step).
apps/orders/src/orders-cancel.integration.spec.ts:379:    expect(success.compensationPlanned).toEqual(['credit_release', 'stock_release']);
apps/orders/src/orders-cancel.integration.spec.ts:389:    // complete, then assert credit.release was issued STRICTLY BEFORE
apps/orders/src/orders-cancel.integration.spec.ts:390:    // stock.release — the exact sequence, not merely that both eventually
apps/orders/src/orders-cancel.integration.spec.ts:393:    expect(billingApproved.issuedOrder).toEqual(['credit.release', 'stock.release']);
apps/fulfillment/src/stock-release-idempotency.integration.spec.ts:26:describe('stock.release — R34, FS9, FS10 (Testcontainers: mysql:8.4.11 + nats:2.14.5-alpine + apache/kafka:4.3.1)', () => {
apps/fulfillment/src/stock-release-idempotency.integration.spec.ts:37:  it('happy path: released reply, rows released, counter down, exactly one stock.released.v1 with the request\'s reason', async () => {
apps/fulfillment/src/stock-release-idempotency.integration.spec.ts:71:    expect(outboxRows[0]).toMatchObject({ eventType: 'stock.released.v1', causationId: releaseRequestId.value });
apps/fulfillment/src/despatch-create.integration.spec.ts:235:  it('concurrency against a simultaneous stock.release: exactly one of despatch.create/stock.release wins, and the final state is consistent either way', async () => {
apps/fulfillment/src/stock-wire.integration.spec.ts:75:  it('answers a bare-JSON request with a bare-JSON reply on stock.release', async () => {
apps/orders/src/application/saga-fact-handler.ts:70:    // types in the first place. `credit.released.v1` now has THREE
apps/orders/src/application/saga-fact-handler.ts:79:    // (`credit.released.v1`), where no single "the expected status" exists
apps/orders/src/application/cancel-order.handler.spec.ts:6://   OCR-stock_reserved   — `stock.release` (reason `order_cancelled`) enqueued
apps/orders/src/application/cancel-order.handler.spec.ts:12://   OCR-credit-release   — `credit_approved`/`confirmed` -> `credit.release`
apps/orders/src/application/cancel-order.handler.spec.ts:151:  it('OCR-stock_reserved — enqueues stock.release (reason order_cancelled) and dispatches the fast-path command, leaving the order stock_reserved', async () => {
apps/orders/src/application/cancel-order.handler.spec.ts:160:    expect(result.compensationPlanned).toEqual(['stock_release']);
apps/orders/src/application/cancel-order.handler.spec.ts:169:    expect(input.command).toBe('stock.release');
apps/orders/src/application/cancel-order.handler.spec.ts:181:    // fact-driven `stock.release` branch (`CreditRejectionRecorded`) uses,
apps/orders/src/application/cancel-order.handler.spec.ts:190:    'OCR-credit-release — enqueues credit.release and dispatches the fast-path command for status %s, leaving the order unchanged (reverse order of acquisition, saga.md §4.3)',
apps/orders/src/application/cancel-order.handler.spec.ts:200:      // credit_release FIRST, stock_release SECOND — reverse order of
apps/orders/src/application/cancel-order.handler.spec.ts:201:      // acquisition; only credit_release is actually issued by this call,
apps/orders/src/application/cancel-order.handler.spec.ts:202:      // stock_release follows once credit.released.v1 arrives.
apps/orders/src/application/cancel-order.handler.spec.ts:203:      expect(result.compensationPlanned).toEqual(['credit_release', 'stock_release']);
apps/orders/src/application/cancel-order.handler.spec.ts:206:      // once the fact chain (credit.released.v1 -> stock.release ->
apps/orders/src/application/cancel-order.handler.spec.ts:207:      // stock.released.v1) completes, mirroring the stock_reserved
apps/orders/src/application/cancel-order.handler.spec.ts:213:      expect(input.command).toBe('credit.release');
apps/orders/src/application/saga-steps.spec.ts:89:// `credit.released.v1` and `stock.released.v1` (feature 41's follow-up
apps/orders/src/application/saga-steps.spec.ts:101:const MULTI_VARIANT_FACT_TYPES = new Set(['credit.released.v1', 'stock.released.v1']);
apps/orders/src/application/saga-steps.spec.ts:116:      // `stock.released.v1` is excluded from this generic loop (multi-variant
apps/orders/src/application/saga-steps.spec.ts:195:describe('credit.approved.v1 — R21: performs both edges, exactly one order.confirmed.v1', () => {
apps/orders/src/application/saga-steps.spec.ts:197:    const step = stepFor('credit.approved.v1');
apps/orders/src/application/saga-steps.spec.ts:203:    apply(step, order, fact({ eventType: 'credit.approved.v1', correlationId: order.id.value }));
apps/orders/src/application/saga-steps.spec.ts:212:describe('credit.rejected.v1 — R27: status unchanged, owes stock.release', () => {
apps/orders/src/application/saga-steps.spec.ts:213:  it('leaves stock_reserved unchanged and owes stock.release', () => {
apps/orders/src/application/saga-steps.spec.ts:217:    expect(step.commandAfter).toBe('stock.release');
apps/orders/src/application/saga-steps.spec.ts:225:describe('stock.released.v1 — R28, SO7: compensation path B, one stock_released step from the observed fact', () => {
apps/orders/src/application/saga-steps.spec.ts:226:  it('stepFor("stock.released.v1") is ambiguous — three variants exist (feature 41\'s follow-up pass), so the status-less lookup refuses', () => {
apps/orders/src/application/saga-steps.spec.ts:227:    expect(() => stepFor('stock.released.v1')).toThrow(/ambiguous/);
apps/orders/src/application/saga-steps.spec.ts:231:    const step = stepForStatus('stock.released.v1', 'stock_reserved');
apps/orders/src/application/saga-steps.spec.ts:236:      eventType: 'stock.released.v1',
apps/orders/src/application/saga-steps.spec.ts:245:      step: 'stock_released',
apps/orders/src/application/saga-steps.spec.ts:247:      eventType: 'stock.released.v1',
apps/orders/src/application/saga-steps.spec.ts:267:  it('stepsFrom builds exactly one stock_released compensation step carrying the observed fact identity', () => {
apps/orders/src/application/saga-steps.spec.ts:268:    const envelope = fact({ eventType: 'stock.released.v1', payload: { reason: 'order_cancelled' } });
apps/orders/src/application/saga-steps.spec.ts:272:        step: 'stock_released',
apps/orders/src/application/saga-steps.spec.ts:274:        eventType: 'stock.released.v1',
apps/orders/src/application/saga-steps.spec.ts:281:    'the follow-up pass — %s: cancels with reason operator_cancelled (always — this is the ONLY trigger for stock.release from this status) and compensationSteps names BOTH credit_released and stock_released',
apps/orders/src/application/saga-steps.spec.ts:283:      const step = stepForStatus('stock.released.v1', status);
apps/orders/src/application/saga-steps.spec.ts:286:      const envelope = fact({ eventType: 'stock.released.v1', payload: { reason: 'order_cancelled' } });
apps/orders/src/application/saga-steps.spec.ts:291:      expect(steps[0]).toMatchObject({ step: 'credit_released', eventType: 'credit.released.v1' });
apps/orders/src/application/saga-steps.spec.ts:293:      expect(steps[1]).toMatchObject({ step: 'stock_released', eventId: envelope.eventId, eventType: 'stock.released.v1' });
apps/orders/src/application/saga-steps.spec.ts:306:      expect(stepForStatus('stock.released.v1', status)).toBeUndefined();
apps/orders/src/application/saga-steps.spec.ts:347:describe('credit.released.v1 — three variants (feature 41\'s follow-up pass): R24\'s paid happy path, and the credit_approved/confirmed compensation variants', () => {
apps/orders/src/application/saga-steps.spec.ts:348:  it('stepFor("credit.released.v1") is ambiguous — three variants exist, so the status-less lookup refuses rather than silently picking one', () => {
apps/orders/src/application/saga-steps.spec.ts:349:    expect(() => stepFor('credit.released.v1')).toThrow(/ambiguous/);
apps/orders/src/application/saga-steps.spec.ts:353:    const step = stepForStatus('credit.released.v1', 'paid');
apps/orders/src/application/saga-steps.spec.ts:358:    apply(step, order, fact({ eventType: 'credit.released.v1', correlationId: order.id.value, payload: { reason: 'invoice_paid' } }));
apps/orders/src/application/saga-steps.spec.ts:365:    'the follow-up pass — %s: status unchanged (no-op apply, mirrors credit.rejected.v1\'s own R27 no-op), owes stock.release',
apps/orders/src/application/saga-steps.spec.ts:367:      const step = stepForStatus('credit.released.v1', status);
apps/orders/src/application/saga-steps.spec.ts:369:      expect(step.commandAfter).toBe('stock.release');
apps/orders/src/application/saga-steps.spec.ts:372:      apply(step, order, fact({ eventType: 'credit.released.v1', correlationId: order.id.value, payload: { reason: 'order_cancelled' } }));
apps/orders/src/application/saga-steps.spec.ts:381:      expect(stepForStatus('credit.released.v1', status)).toBeUndefined();
apps/orders/src/application/saga-steps.spec.ts:406:        'credit.approved.v1',
apps/orders/src/application/saga-steps.spec.ts:408:        'stock.released.v1',
apps/orders/src/application/saga-steps.spec.ts:412:        'credit.released.v1',
apps/orders/src/application/saga-steps.ts:14: * `credit.release`, which `commandAfter` never names (no step-table row
apps/orders/src/application/saga-steps.ts:15: * owes it — see `credit.release`'s own comment below) but which
apps/orders/src/application/saga-steps.ts:17: * table, same durable mechanism" shape `stock.release`'s operator-cancel
apps/orders/src/application/saga-steps.ts:22:  'stock.release',
apps/orders/src/application/saga-steps.ts:26:  'credit.release',
apps/orders/src/application/saga-steps.ts:67:      throw new Error(`saga-steps: mapReason: unmapped stock.released.v1 reason "${String(exhaustive)}"`);
apps/orders/src/application/saga-steps.ts:74: * `stock.released.v1` below: TWO acquisitions are unwound in this branch
apps/orders/src/application/saga-steps.ts:79: * EARLIER `credit.released.v1` fact's own `eventId`/`occurredAt` — this
apps/orders/src/application/saga-steps.ts:82: * function hands it") — so the synthesised `credit_released` entry below
apps/orders/src/application/saga-steps.ts:92:      step: 'credit_released',
apps/orders/src/application/saga-steps.ts:93:      eventType: 'credit.released.v1',
apps/orders/src/application/saga-steps.ts:101:/** Builds the one-element `compensationSteps` array from the OBSERVED `stock.released.v1` fact (SO7) — the aggregate never sees the fact itself, only what this function hands it. */
apps/orders/src/application/saga-steps.ts:106:      step: 'stock_released',
apps/orders/src/application/saga-steps.ts:117: * precondition (one `SagaStep`); `credit.released.v1` and
apps/orders/src/application/saga-steps.ts:118: * `stock.released.v1` (feature 41's follow-up pass, closing the
apps/orders/src/application/saga-steps.ts:154:  'credit.approved.v1': {
apps/orders/src/application/saga-steps.ts:167:    // R27 — status unchanged; the order stays in the safe, resumable stock_reserved state until stock.released.v1 completes the compensation (design.md §4.3).
apps/orders/src/application/saga-steps.ts:171:    commandAfter: 'stock.release',
apps/orders/src/application/saga-steps.ts:174:  // three, symmetric with `credit.released.v1`'s own extension above):
apps/orders/src/application/saga-steps.ts:182:  //     `credit.released.v1`'s own comment above), so THIS fact is what
apps/orders/src/application/saga-steps.ts:184:  //     here (the only trigger for `stock.release` while `confirmed`/
apps/orders/src/application/saga-steps.ts:188:  'stock.released.v1': [
apps/orders/src/application/saga-steps.ts:231:  //     `CancelOrderHandler` issues `billing.credit.release` BEFORE
apps/orders/src/application/saga-steps.ts:232:  //     `stock.release` (reverse order of acquisition, saga.md §4.3); this
apps/orders/src/application/saga-steps.ts:235:  //     `confirmed` until `stock.released.v1` completes the cancellation via
apps/orders/src/application/saga-steps.ts:239:  //     the ONLY way `credit.released.v1` carries `invoice_paid` is
apps/orders/src/application/saga-steps.ts:244:  'credit.released.v1': [
apps/orders/src/application/saga-steps.ts:255:        /* no-op — status unchanged until stock.released.v1 arrives */
apps/orders/src/application/saga-steps.ts:257:      commandAfter: 'stock.release',
apps/orders/src/application/saga-steps.ts:263:        /* no-op — status unchanged until stock.released.v1 arrives */
apps/orders/src/application/saga-steps.ts:265:      commandAfter: 'stock.release',
apps/orders/src/application/saga-steps.ts:275:/** Every variant declared for `eventType` — `[]` when the table has no entry at all, one-element for every single-variant fact type, three for `credit.released.v1`/`stock.released.v1`. */
apps/orders/src/application/saga-steps.ts:299: * `credit.released.v1`) — returns "the" step unchanged. Throws if
apps/orders/src/application/cancel-order.handler.ts:9://   | Operator cancels while `stock_reserved`                | stock reservation            | stock reservation (`stock.release`)       | cancel `operator_cancelled` |
apps/orders/src/application/cancel-order.handler.ts:16:// original section) issues `billing.credit.release` FIRST — the newly
apps/orders/src/application/cancel-order.handler.ts:21:// stays `credit_approved`/`confirmed` (unchanged) until `credit.released.v1`
apps/orders/src/application/cancel-order.handler.ts:22:// arrives; `saga-steps.ts`'s `credit.released.v1` step now has a SECOND and
apps/orders/src/application/cancel-order.handler.ts:24:// `stock.release` next — the exact reverse-order-of-acquisition chain
apps/orders/src/application/cancel-order.handler.ts:27:// class for the second step): `credit.released.v1` (credit_approved/
apps/orders/src/application/cancel-order.handler.ts:28:// confirmed variant) -> owes `stock.release` -> `stock.released.v1`
apps/orders/src/application/cancel-order.handler.ts:32:// `stock.release` -> `stock.released.v1` already completes R27/R28's
apps/orders/src/application/cancel-order.handler.ts:64:      readonly compensationPlanned: readonly ('credit_release' | 'stock_release')[];
apps/orders/src/application/cancel-order.handler.ts:144:   * R8's `stock_reserved` branch (saga.md §4.3) — issues `stock.release`
apps/orders/src/application/cancel-order.handler.ts:151:   * order stays `stock_reserved` (unchanged) until `stock.released.v1`
apps/orders/src/application/cancel-order.handler.ts:152:   * arrives; `saga-steps.ts`'s EXISTING `stock.released.v1` step (precondition
apps/orders/src/application/cancel-order.handler.ts:172:      command: 'stock.release',
apps/orders/src/application/cancel-order.handler.ts:190:      compensationPlanned: ['stock_release'],
apps/orders/src/application/cancel-order.handler.ts:196:   * own follow-up pass) — issues `billing.credit.release` FIRST (reverse
apps/orders/src/application/cancel-order.handler.ts:203:   * — Billing's `billing.credit.release` responder always releases with
apps/orders/src/application/cancel-order.handler.ts:206:   * `credit.released.v1` arrives; `saga-steps.ts`'s SECOND/THIRD variant
apps/orders/src/application/cancel-order.handler.ts:208:   * `stock.release` — completing the reverse-order chain entirely through
apps/orders/src/application/cancel-order.handler.ts:231:      command: 'credit.release',
apps/orders/src/application/cancel-order.handler.ts:249:      // Reverse order of acquisition (saga.md §4.3) — credit_release is
apps/orders/src/application/cancel-order.handler.ts:250:      // issued NOW; stock_release follows once credit.released.v1 arrives
apps/orders/src/application/cancel-order.handler.ts:253:      compensationPlanned: ['credit_release', 'stock_release'],
apps/orders/src/application/saga-fact-handler.spec.ts:203:    // The order is `placed`; `credit.approved.v1` expects `stock_reserved`.
apps/orders/src/application/saga-fact-handler.spec.ts:204:    const envelope = fact({ eventType: 'credit.approved.v1', correlationId: order.id.value });
apps/orders/src/application/saga-fact-handler.spec.ts:211:      eventType: 'credit.approved.v1',
apps/orders/src/application/saga-command-payloads.ts:51: * `stock.release` is now owed by TWO different step-table rows (design.md
apps/orders/src/application/saga-command-payloads.ts:53: * (reason always `credit_rejected`) and `credit.released.v1`'s
apps/orders/src/application/saga-command-payloads.ts:55: * asserted here, not assumed: see `saga-steps.ts`'s `credit.released.v1`
apps/orders/src/application/saga-command-payloads.ts:64:    case 'credit.released.v1': {
apps/orders/src/application/saga-command-payloads.ts:68:          `saga-command-payloads: stock.release owed by credit.released.v1 with unexpected reason "${reason}" — expected order_cancelled (the paid variant owes no commandAfter and should never reach here)`,
apps/orders/src/application/saga-command-payloads.ts:74:      throw new Error(`saga-command-payloads: stock.release owed by unexpected fact type "${fact.eventType}"`);
apps/orders/src/application/saga-command-payloads.ts:83: * required for `stock.release` (its `reason` depends on which fact owed
apps/orders/src/application/saga-command-payloads.ts:97:    case 'stock.release': {
apps/orders/src/application/saga-command-payloads.ts:99:        throw new Error('saga-command-payloads: stock.release requires the triggering fact envelope to determine its reason');
apps/orders/src/application/saga-command-payloads.ts:132:    case 'credit.release':
apps/orders/src/application/saga-command-payloads.ts:133:      // No step-table row ever names `credit.release` as its
apps/orders/src/application/saga-command-payloads.ts:135:      // "outside the fact-driven table" shape `stock.release`'s
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:165:  it('R24 — the fact that completes the saga (credit.released.v1, precondition paid) records EXACTLY ONE completion, measured from order.orderDate (the real order.placed.v1 timestamp) to THIS fact\'s own occurredAt', async () => {
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:180:    const envelope = fact({ eventType: 'credit.released.v1', correlationId: order.id.value, occurredAt: closingOccurredAt });
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:208:  it('R27/R28 — the compensation-completing cancel (stock.released.v1, precondition stock_reserved) records EXACTLY ONE cancellation, not two, even though credit.rejected.v1 already advanced the saga once', async () => {
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:225:    // Then: stock.released.v1 — the CANCEL step that actually closes it.
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:229:        eventType: 'stock.released.v1',
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:265:    // `credit.approved.v1` expects `stock_reserved`; the order is `placed`.
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:267:      fact({ eventType: 'credit.approved.v1', correlationId: order.id.value }),
apps/orders/src/presentation/orders-cancel.controller.spec.ts:8:// ['credit_release', 'stock_release']` — the follow-up pass that closed the
apps/orders/src/presentation/orders-cancel.controller.spec.ts:9:// `credit_release_unavailable`/`UNAVAILABLE` gap this file used to
apps/orders/src/presentation/orders-cancel.controller.spec.ts:77:  it('maps compensation_pending from the credit_approved/confirmed branch to a success reply carrying compensationPlanned: [credit_release, stock_release]', async () => {
apps/orders/src/presentation/orders-cancel.controller.spec.ts:84:        compensationPlanned: ['credit_release', 'stock_release'],
apps/orders/src/presentation/orders-cancel.controller.spec.ts:94:      compensationPlanned: ['credit_release', 'stock_release'],
apps/orders/src/presentation/orders-cancel.controller.spec.ts:106:        compensationPlanned: ['stock_release'],
apps/orders/src/presentation/orders-cancel.controller.spec.ts:116:      compensationPlanned: ['stock_release'],
apps/orders/src/test-support/saga-integration-harness.ts:348:        // compensation on `stock.released.v1` — one app instance, exactly
apps/orders/src/domain/order.ts:395:   * observes `stock.released.v1` itself (design.md §4.5).
apps/orders/src/domain/order-transitions.ts:41:    trigger: 'credit.approved.v1 observed by the orchestrator',
apps/orders/src/domain/order-transitions.ts:71:    trigger: 'credit.released.v1 observed by the orchestrator — the saga closes',
apps/orders/src/domain/order-transitions.ts:85:      'stock.released.v1 completing the credit-rejection compensation (reason credit_rejected) or operator cancellation',
apps/fulfillment/src/application/despatch-application-errors.ts:22: * no-op, because (unlike `stock.release`'s F5) there is no sensible empty
apps/fulfillment/src/application/stock-application-errors.ts:39:/** `stock.release` — the non-locking pre-read (design.md §4.4 step 0) named a stock id that the subsequent locking read (step 1/2) did not confirm still belongs to the order. Defensive: a reservation inserted for this order between step 0 and step 1 is impossible in practice (reservations are created once, under the stock locks, by `stock.reserve`). The handler aborts rather than release under a lock it does not hold, and lets the orchestrator retry. */
apps/fulfillment/src/application/stock-application-errors.ts:44:    super(`stock.release: order ${orderReference}'s reservations changed between the pre-read and the locking read`);
apps/fulfillment/src/application/despatch-creation.handler.ts:5:// stock-rows-first FOR UPDATE ordering `stock.release` established) rather
apps/fulfillment/src/application/despatch-creation.handler.ts:48:   * then the same stock-rows-first lock protocol `stock.release` uses.
apps/fulfillment/src/presentation/stock.controller.ts:39:export const STOCK_RELEASE_SUBJECT = 'fulfillment.stock.release';
apps/fulfillment/src/presentation/stock.controller.spec.ts:83:  it('replies VALIDATION_FAILED and dispatches nothing when headers are missing on stock.release', async () => {
apps/fulfillment/src/domain/despatch-events.ts:7:// domain-model.md §7.1 — unlike `stock.reserved.v1`/`stock.released.v1`,
apps/fulfillment/src/domain/order-stock-reservation.spec.ts:58:  it('releases the reservations, decreases reservedUnits and emits exactly one stock.released.v1', () => {
apps/fulfillment/src/domain/order-stock-reservation.spec.ts:76:    expect(events[0]?.eventType).toBe('stock.released.v1');
apps/fulfillment/src/domain/order-stock-reservation.spec.ts:127:  it('stamps aggregateId with the first known line\'s stock item on stock.reserved.v1 and stock.rejected.v1, and the first released reservation\'s item on stock.released.v1', () => {
apps/fulfillment/src/domain/order-stock-reservation.ts:142: * reservations is non-empty, appends ONE `stock.released.v1` (reason from
apps/fulfillment/src/domain/stock-events.ts:99:    eventType: 'stock.released.v1',
apps/orders/src/infrastructure/persistence/migrations.integration.spec.ts:230:      eventType: 'credit.approved.v1',
apps/orders/src/infrastructure/persistence/migrations.integration.spec.ts:243:      eventType: 'credit.approved.v1',
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:199:// live bug's exact scenario: `stock.release` on an already-`consumed`
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:227:  it('the exact reproduced bug: stock.release against an already-consumed reservation (PRECONDITION_FAILED) is terminal, carries the subject and the responder code', async () => {
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:277:  it('releaseStock calls fulfillment.stock.release', async () => {
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:345:  it('releaseCredit calls billing.credit.release', async () => {
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.ts:34:export const STOCK_RELEASE_SUBJECT = 'fulfillment.stock.release';
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.ts:39:export const CREDIT_RELEASE_SUBJECT = 'billing.credit.release';
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:271:    const row = pendingRow({ command: 'stock.release' });
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:279:      .mockRejectedValue(new SagaCommandBusinessRejectionError('fulfillment.stock.release', 'PRECONDITION_FAILED', 'reservation already consumed'));
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:282:    const outcome = await dispatcher.dispatch(row.orderId, 'stock.release');
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:296:    const row = pendingRow({ command: 'stock.release', status: 'parked', attempts: 4 });
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:300:      .mockRejectedValue(new SagaCommandBusinessRejectionError('fulfillment.stock.release', 'PRECONDITION_FAILED', 'reservation already consumed'));
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:303:    const outcome = await dispatcher.dispatch(row.orderId, 'stock.release');
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:311:    const row = pendingRow({ command: 'stock.release' });
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:315:      .mockRejectedValue(new SagaCommandTransportError('fulfillment.stock.release', 'responder returned INTERNAL_ERROR: boom'));
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:318:    const outcome = await dispatcher.dispatch(row.orderId, 'stock.release');
apps/orders/src/infrastructure/saga/saga-command-dispatcher.ts:102:    case 'stock.release':
apps/orders/src/infrastructure/saga/saga-command-dispatcher.ts:110:    case 'credit.release':
apps/orders/src/application/commands/saga-dispatch.handlers.spec.ts:47:  it('IssueStockReleaseHandler dispatches stock.release (compensation path B)', async () => {
apps/orders/src/application/commands/saga-dispatch.handlers.spec.ts:54:    expect(dispatcher.dispatch).toHaveBeenCalledWith(orderId, 'stock.release');
apps/orders/src/application/commands/saga-fact.handlers.ts:180:   * `credit.released.v1` now has THREE step-table variants (feature 41's
apps/orders/src/application/commands/saga-fact.handlers.ts:184:   * `credit_approved`/`confirmed` variants owe `stock.release` — THIS is
apps/orders/src/application/commands/saga-fact.handlers.spec.ts:107:    const inner = fakeHandler({ outcome: 'processed', enqueued: 'stock.release' });
apps/orders/src/application/commands/saga-fact.handlers.spec.ts:136:  it('HandleCreditReleasedFactHandler publishes CreditReleasedForCancellationRecorded on processed+enqueued (feature 41 follow-up — the credit_approved/confirmed variant owing stock.release)', async () => {
apps/orders/src/application/commands/saga-fact.handlers.spec.ts:137:    const inner = fakeHandler({ outcome: 'processed', enqueued: 'stock.release' });
apps/orders/src/application/commands/saga-fact.commands.ts:107:  'credit.approved.v1': HandleCreditApprovedFactCommand,
apps/orders/src/application/commands/saga-fact.commands.ts:109:  'stock.released.v1': HandleStockReleasedFactCommand,
apps/orders/src/application/commands/saga-fact.commands.ts:113:  'credit.released.v1': HandleCreditReleasedFactCommand,
apps/orders/src/application/commands/saga-dispatch.handlers.ts:41:    await this.dispatcher.dispatch(UniqueId.from(command.orderId), 'stock.release');
apps/orders/src/application/commands/saga-dispatch.handlers.ts:69:    await this.dispatcher.dispatch(UniqueId.from(command.orderId), 'credit.release');
apps/orders/src/application/ports/saga-commands.port.ts:37:  /** `billing.credit.release` — feature 41's follow-up pass, closing the `credit_approved`/`confirmed` cancel gap. */
apps/orders/src/application/events/saga-dispatch.events.ts:26:/** `credit.rejected.v1` processed — owes `stock.release` (compensation path B, R27). */
apps/orders/src/application/events/saga-dispatch.events.ts:34:/** `credit.approved.v1` processed through to `confirmed` — owes `despatch.create`. */
apps/orders/src/application/events/saga-dispatch.events.ts:51: * `credit.released.v1` processed while `credit_approved`/`confirmed` — owes
apps/orders/src/application/events/saga-dispatch.events.ts:52: * `stock.release` (feature 41's follow-up pass: the compensation release,
apps/orders/src/application/events/saga-dispatch.events.ts:57: * actually owed the command; `credit.released.v1`'s OTHER variant
apps/orders/src/application/sagas/order.sagas.ts:4:// follow-up pass added the sixth — `credit.released.v1`'s mid-cancellation
apps/orders/src/application/sagas/order.sagas.ts:109:      'stock.release (credit.rejected.v1)',
apps/orders/src/application/sagas/order.sagas.ts:114:    // owed by a DIFFERENT fact (`credit.released.v1`'s `credit_approved`/
apps/orders/src/application/sagas/order.sagas.ts:123:      'stock.release (credit.released.v1 cancel compensation)',
apps/fulfillment/src/infrastructure/persistence/migrations.integration.spec.ts:387:      eventType: 'stock.released.v1',
apps/fulfillment/src/infrastructure/persistence/migrations.integration.spec.ts:401:        eventType: 'stock.released.v1',
apps/fulfillment/src/application/ports/consumer-name.ts:6:// `stock.release`/`despatch.create` are all command-driven), and the shared
apps/fulfillment/src/application/ports/stock-item-repository.port.ts:1:// The write-model port for `stock.reserve`/`stock.release`/`stock.replenish`
apps/orders/src/infrastructure/persistence/schema/saga-commands.schema.ts:16:  'stock.release',
apps/orders/src/infrastructure/persistence/schema/saga-commands.schema.ts:22:  // same "outside the fact-driven step table" shape `stock.release`'s
apps/orders/src/infrastructure/persistence/schema/saga-commands.schema.ts:24:  'credit.release',
apps/orders/src/infrastructure/messaging/test-support/stub-saga-responders.ts:124:  /** Set to `false` to leave `stock.release` with NO subscriber at all — used by the FS1/D1 crash-loop reproduction, which needs a `stock.release` row that stays `parked` (NATS `NoResponders`) long enough to redeliver a duplicate `credit.rejected.v1` against it. Defaults to `true` (a responder answers, as every other test in this codebase expects). */
apps/orders/src/infrastructure/messaging/test-support/stub-saga-responders.ts:131: * `stock.reserve`/`stock.release` -> fulfillment, `credit.hold` ->
apps/orders/src/infrastructure/messaging/test-support/stub-saga-responders.ts:183:            await publishFact(factPublishers.fulfillment, 'stock.released.v1', orderId, {
apps/orders/src/infrastructure/messaging/test-support/stub-saga-responders.ts:240:      await publishFact(factPublishers.billing, 'credit.approved.v1', orderId, {
```

### One classification line per hit

Codes: **CHANGED** — edited by this feature. **CORRECT** — a site about these commands that was already right and needed no change. **GUARD-NEW** — a file where the only change is an added guard. **NA-PROSE** — prose/round-trip fixture mentioning a command, not a site. A hit with no rule would print `UNCLASSIFIED`; **none did**.

```
apps/orders/src/saga-happy-path.integration.spec.ts:4: CORRECT — R24 paid -> credit.released.v1 -> completed; that variant is unchanged; re-run green against containers
apps/orders/src/saga-happy-path.integration.spec.ts:75: CORRECT — R24 paid -> credit.released.v1 -> completed; that variant is unchanged; re-run green against containers
apps/orders/src/saga-happy-path.integration.spec.ts:83: CORRECT — R24 paid -> credit.released.v1 -> completed; that variant is unchanged; re-run green against containers
apps/orders/src/saga-happy-path.integration.spec.ts:102: CORRECT — R24 paid -> credit.released.v1 -> completed; that variant is unchanged; re-run green against containers
apps/orders/src/saga-happy-path.integration.spec.ts:115: CORRECT — R24 paid -> credit.released.v1 -> completed; that variant is unchanged; re-run green against containers
apps/orders/src/saga-happy-path.integration.spec.ts:124: CORRECT — R24 paid -> credit.released.v1 -> completed; that variant is unchanged; re-run green against containers
apps/orders/src/saga-preconditions.integration.spec.ts:57: CORRECT — redelivery sweep against a COMPLETED order: with B2 the late-approval branch does not fire (status is completed), so every fact is still precondition_unmet; re-run green against containers
apps/orders/src/saga-preconditions.integration.spec.ts:59: CORRECT — redelivery sweep against a COMPLETED order: with B2 the late-approval branch does not fire (status is completed), so every fact is still precondition_unmet; re-run green against containers
apps/orders/src/saga-preconditions.integration.spec.ts:63: CORRECT — redelivery sweep against a COMPLETED order: with B2 the late-approval branch does not fire (status is completed), so every fact is still precondition_unmet; re-run green against containers
apps/orders/src/saga-preconditions.integration.spec.ts:66: CORRECT — redelivery sweep against a COMPLETED order: with B2 the late-approval branch does not fire (status is completed), so every fact is still precondition_unmet; re-run green against containers
apps/orders/src/saga-preconditions.integration.spec.ts:68: CORRECT — redelivery sweep against a COMPLETED order: with B2 the late-approval branch does not fire (status is completed), so every fact is still precondition_unmet; re-run green against containers
apps/orders/src/saga-preconditions.integration.spec.ts:106: CORRECT — redelivery sweep against a COMPLETED order: with B2 the late-approval branch does not fire (status is completed), so every fact is still precondition_unmet; re-run green against containers
apps/orders/src/saga-preconditions.integration.spec.ts:131: CORRECT — redelivery sweep against a COMPLETED order: with B2 the late-approval branch does not fire (status is completed), so every fact is still precondition_unmet; re-run green against containers
apps/orders/src/saga-command-retry.integration.spec.ts:42: CORRECT — a union type of command names in a helper signature; no ordering
apps/orders/src/saga-compensation-stock-rejected.integration.spec.ts:3: CORRECT — asserts ZERO stock.release requests on the stock-rejected path; unchanged
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:2: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:3: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:27: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:28: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:72: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:111: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:114: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:117: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:131: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:140: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:144: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:153: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:183: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:192: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:195: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:211: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:226: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/saga-compensation-credit-rejected.integration.spec.ts:233: CORRECT — R27/R28 automatic compensation: credit.rejected.v1 -> stock.release -> cancel. SA-4 does not touch that path; re-run green against containers
apps/orders/src/orders-cancel.integration.spec.ts:10: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:13: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:16: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:19: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:72: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:116: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:143: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:144: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:147: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:152: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:153: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:190: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:216: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:218: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:244: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:246: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:319: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:337: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:345: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:346: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:348: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:360: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:368: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:371: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:379: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:389: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:390: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/orders/src/orders-cancel.integration.spec.ts:393: CHANGED — real-wire proof transposed: issuedOrder is now [stock.release, credit.release] and the reply plan is [stock_release, credit_release]; stub responders unchanged (they answer both subjects either way)
apps/fulfillment/src/stock-release-idempotency.integration.spec.ts:26: CORRECT — R34/FS9/FS10 including the PRECONDITION_FAILED reply on consumed reservations — the Fulfillment half of B3, unchanged
apps/fulfillment/src/stock-release-idempotency.integration.spec.ts:37: CORRECT — R34/FS9/FS10 including the PRECONDITION_FAILED reply on consumed reservations — the Fulfillment half of B3, unchanged
apps/fulfillment/src/stock-release-idempotency.integration.spec.ts:71: CORRECT — R34/FS9/FS10 including the PRECONDITION_FAILED reply on consumed reservations — the Fulfillment half of B3, unchanged
apps/fulfillment/src/despatch-create.integration.spec.ts:235: CORRECT — R36 refusals and the real despatch-vs-release race — the existing B4 evidence; unchanged, re-run green against containers
apps/fulfillment/src/stock-wire.integration.spec.ts:75: CORRECT — bare-JSON wire shape on stock.release; no ordering
apps/orders/src/application/saga-fact-handler.ts:70: CHANGED — B2 added: the late credit.approved.v1 check before the generic dispatch; enqueues credit.release only, no transition, no save
apps/orders/src/application/saga-fact-handler.ts:79: CHANGED — B2 added: the late credit.approved.v1 check before the generic dispatch; enqueues credit.release only, no transition, no save
apps/orders/src/application/cancel-order.handler.spec.ts:6: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:12: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:151: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:160: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:169: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:181: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:190: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:200: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:201: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:202: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:203: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:206: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:207: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/cancel-order.handler.spec.ts:213: CHANGED — OCR-credit-release became OCR-stock-first: asserts stock.release enqueued, IssueStockReleaseCommand dispatched, plan ordered [stock_release, credit_release] by index
apps/orders/src/application/saga-steps.spec.ts:89: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:101: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:116: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:195: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:197: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:203: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:212: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:213: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:217: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:225: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:226: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:227: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:231: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:236: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:245: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:247: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:267: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:268: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:272: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:274: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:281: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:283: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:286: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:291: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:293: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:306: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:347: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:348: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:349: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:353: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:358: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:365: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:367: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:369: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:372: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:381: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:406: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:408: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.spec.ts:412: CHANGED — the step-table guards — both multi-variant blocks transposed and tightened (commandAfter, no-transition, compensation-step ORDER by index, reason read from the fact)
apps/orders/src/application/saga-steps.ts:14: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:15: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:17: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:22: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:26: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:67: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:74: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:79: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:82: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:92: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:93: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:101: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:106: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:117: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:118: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:154: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:167: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:171: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:174: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:182: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:184: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:188: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:231: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:232: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:235: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:239: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:244: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:255: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:257: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:263: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:265: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:275: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/saga-steps.ts:299: CHANGED — the step table — stock.released.v1 credit_approved/confirmed became advance+commandAfter credit.release; credit.released.v1 credit_approved/confirmed became the terminal cancel with both compensation steps; block comments rewritten
apps/orders/src/application/cancel-order.handler.ts:9: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:16: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:21: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:22: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:24: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:27: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:28: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:32: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:64: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:144: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:151: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:152: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:172: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:190: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:196: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:203: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:206: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:208: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:231: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:249: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:250: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/cancel-order.handler.ts:253: CHANGED — the operator-cancel enqueue site — all three compensating statuses now enqueue stock.release first; header table, branch dispatch, the two collapsed methods and compensationPlanned rewritten
apps/orders/src/application/saga-fact-handler.spec.ts:203: CHANGED — B2 guards added (six cases) and the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-fact-handler.spec.ts:204: CHANGED — B2 guards added (six cases) and the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-fact-handler.spec.ts:211: CHANGED — B2 guards added (six cases) and the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-command-payloads.ts:51: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:53: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:55: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:64: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:68: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:74: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:83: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:97: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:99: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:132: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:133: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-command-payloads.ts:135: CHANGED — stockReleaseReasonFor lost its credit.released.v1 case (that variant owes no command now); the credit.release case is now a LIVE fact-driven case and says so
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:165: CORRECT — metrics cases use stock.released.v1 at stock_reserved (R28, untouched) and credit.released.v1 at paid (R24, untouched); the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:180: CORRECT — metrics cases use stock.released.v1 at stock_reserved (R28, untouched) and credit.released.v1 at paid (R24, untouched); the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:208: CORRECT — metrics cases use stock.released.v1 at stock_reserved (R28, untouched) and credit.released.v1 at paid (R24, untouched); the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:225: CORRECT — metrics cases use stock.released.v1 at stock_reserved (R28, untouched) and credit.released.v1 at paid (R24, untouched); the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:229: CORRECT — metrics cases use stock.released.v1 at stock_reserved (R28, untouched) and credit.released.v1 at paid (R24, untouched); the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:265: CORRECT — metrics cases use stock.released.v1 at stock_reserved (R28, untouched) and credit.released.v1 at paid (R24, untouched); the store fake gained hasAcceptedOperatorCancel
apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts:267: CORRECT — metrics cases use stock.released.v1 at stock_reserved (R28, untouched) and credit.released.v1 at paid (R24, untouched); the store fake gained hasAcceptedOperatorCancel
apps/orders/src/presentation/orders-cancel.controller.spec.ts:8: CHANGED — the pass-through reply literals transposed, plus an index assertion so a controller that reordered would fail
apps/orders/src/presentation/orders-cancel.controller.spec.ts:9: CHANGED — the pass-through reply literals transposed, plus an index assertion so a controller that reordered would fail
apps/orders/src/presentation/orders-cancel.controller.spec.ts:77: CHANGED — the pass-through reply literals transposed, plus an index assertion so a controller that reordered would fail
apps/orders/src/presentation/orders-cancel.controller.spec.ts:84: CHANGED — the pass-through reply literals transposed, plus an index assertion so a controller that reordered would fail
apps/orders/src/presentation/orders-cancel.controller.spec.ts:94: CHANGED — the pass-through reply literals transposed, plus an index assertion so a controller that reordered would fail
apps/orders/src/presentation/orders-cancel.controller.spec.ts:106: CHANGED — the pass-through reply literals transposed, plus an index assertion so a controller that reordered would fail
apps/orders/src/presentation/orders-cancel.controller.spec.ts:116: CHANGED — the pass-through reply literals transposed, plus an index assertion so a controller that reordered would fail
apps/orders/src/test-support/saga-integration-harness.ts:348: CORRECT — prose about one app instance consuming stock.released.v1; harness unchanged
apps/orders/src/domain/order.ts:395: CORRECT — prose in the aggregate about observing stock.released.v1; the domain edge set is unchanged by SA-4 (cancel from credit_approved/confirmed with operator_cancelled already existed)
apps/orders/src/domain/order-transitions.ts:41: CORRECT — the edge table triggers are described per fact; both facts still legally reach their edges, no edge added or removed
apps/orders/src/domain/order-transitions.ts:71: CORRECT — the edge table triggers are described per fact; both facts still legally reach their edges, no edge added or removed
apps/orders/src/domain/order-transitions.ts:85: CORRECT — the edge table triggers are described per fact; both facts still legally reach their edges, no edge added or removed
apps/fulfillment/src/application/despatch-application-errors.ts:22: CORRECT — error text for the despatch flow; no ordering
apps/fulfillment/src/application/stock-application-errors.ts:39: CORRECT — error text for the release flow; no ordering
apps/fulfillment/src/application/stock-application-errors.ts:44: CORRECT — error text for the release flow; no ordering
apps/fulfillment/src/application/despatch-creation.handler.ts:5: CORRECT — B4 already met: despatch.create reuses the identical lock protocol. Source untouched; a guard was added instead
apps/fulfillment/src/application/despatch-creation.handler.ts:48: CORRECT — B4 already met: despatch.create reuses the identical lock protocol. Source untouched; a guard was added instead
apps/fulfillment/src/presentation/stock.controller.ts:39: CORRECT — the stock.release subject constant
apps/fulfillment/src/presentation/stock.controller.spec.ts:83: CORRECT — header validation on stock.release; no ordering
apps/fulfillment/src/domain/despatch-events.ts:7: NA-PROSE — prose contrasting despatch events with stock events
apps/fulfillment/src/domain/order-stock-reservation.spec.ts:58: CORRECT — domain guards for that emission; unchanged
apps/fulfillment/src/domain/order-stock-reservation.spec.ts:76: CORRECT — domain guards for that emission; unchanged
apps/fulfillment/src/domain/order-stock-reservation.spec.ts:127: CORRECT — domain guards for that emission; unchanged
apps/fulfillment/src/domain/order-stock-reservation.ts:142: CORRECT — emits exactly one stock.released.v1 with the requested reason; unchanged
apps/fulfillment/src/domain/stock-events.ts:99: CORRECT — the stock.released.v1 event type name
apps/orders/src/infrastructure/persistence/migrations.integration.spec.ts:230: NA-PROSE — round-trips a credit.approved.v1 envelope through the schema; no saga ordering
apps/orders/src/infrastructure/persistence/migrations.integration.spec.ts:243: NA-PROSE — round-trips a credit.approved.v1 envelope through the schema; no saga ordering
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:199: CORRECT — subject/terminality assertions per command; unaffected by which release goes first
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:227: CORRECT — subject/terminality assertions per command; unaffected by which release goes first
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:277: CORRECT — subject/terminality assertions per command; unaffected by which release goes first
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.spec.ts:345: CORRECT — subject/terminality assertions per command; unaffected by which release goes first
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.ts:34: CORRECT — subject constants; no ordering
apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.ts:39: CORRECT — subject constants; no ordering
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:271: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:279: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:282: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:296: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:300: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:303: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:311: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:315: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts:318: GUARD-NEW — B3 guard added for despatch.create + PRECONDITION_FAILED (rejected, no park, no claimDeadLetter, no onFirstPark); pre-existing cases untouched, plus the store fake gained hasAcceptedOperatorCancel
apps/orders/src/infrastructure/saga/saga-command-dispatcher.ts:102: CORRECT — B3 already satisfied here: a terminal business rejection short-circuits to markRejected, never park/dead-letter. Unchanged; a guard was added instead
apps/orders/src/infrastructure/saga/saga-command-dispatcher.ts:110: CORRECT — B3 already satisfied here: a terminal business rejection short-circuits to markRejected, never park/dead-letter. Unchanged; a guard was added instead
apps/orders/src/application/commands/saga-dispatch.handlers.spec.ts:47: CORRECT — asserts IssueStockReleaseHandler dispatches stock.release — true before and after
apps/orders/src/application/commands/saga-dispatch.handlers.spec.ts:54: CORRECT — asserts IssueStockReleaseHandler dispatches stock.release — true before and after
apps/orders/src/application/commands/saga-fact.handlers.ts:180: CHANGED — HandleStockReleasedFactHandler now publishes on enqueue; HandleCreditApprovedFactHandler chooses its event by result.enqueued; HandleCreditReleasedFactHandler lost its EventBus
apps/orders/src/application/commands/saga-fact.handlers.ts:184: CHANGED — HandleStockReleasedFactHandler now publishes on enqueue; HandleCreditApprovedFactHandler chooses its event by result.enqueued; HandleCreditReleasedFactHandler lost its EventBus
apps/orders/src/application/commands/saga-fact.handlers.spec.ts:107: CHANGED — guards for the three wrapper changes above, including "LateCreditApprovalRecorded, NOT OrderConfirmed"
apps/orders/src/application/commands/saga-fact.handlers.spec.ts:136: CHANGED — guards for the three wrapper changes above, including "LateCreditApprovalRecorded, NOT OrderConfirmed"
apps/orders/src/application/commands/saga-fact.handlers.spec.ts:137: CHANGED — guards for the three wrapper changes above, including "LateCreditApprovalRecorded, NOT OrderConfirmed"
apps/orders/src/application/commands/saga-fact.commands.ts:107: CORRECT — the eventType -> command-class map; no ordering, every fact type still consumed
apps/orders/src/application/commands/saga-fact.commands.ts:109: CORRECT — the eventType -> command-class map; no ordering, every fact type still consumed
apps/orders/src/application/commands/saga-fact.commands.ts:113: CORRECT — the eventType -> command-class map; no ordering, every fact type still consumed
apps/orders/src/application/commands/saga-dispatch.handlers.ts:41: CORRECT — the two Issue... handlers dispatch by command name and are indifferent to ordering; only the doc-comment naming their SOURCE was refreshed
apps/orders/src/application/commands/saga-dispatch.handlers.ts:69: CORRECT — the two Issue... handlers dispatch by command name and are indifferent to ordering; only the doc-comment naming their SOURCE was refreshed
apps/orders/src/application/ports/saga-commands.port.ts:37: CORRECT — the RPC port surface; only the releaseCredit doc-comment was refreshed to name its new source
apps/orders/src/application/events/saga-dispatch.events.ts:26: CHANGED — CreditReleasedForCancellationRecorded -> StockReleasedForCancellationRecorded (now owing credit.release) plus the new LateCreditApprovalRecorded
apps/orders/src/application/events/saga-dispatch.events.ts:34: CHANGED — CreditReleasedForCancellationRecorded -> StockReleasedForCancellationRecorded (now owing credit.release) plus the new LateCreditApprovalRecorded
apps/orders/src/application/events/saga-dispatch.events.ts:51: CHANGED — CreditReleasedForCancellationRecorded -> StockReleasedForCancellationRecorded (now owing credit.release) plus the new LateCreditApprovalRecorded
apps/orders/src/application/events/saga-dispatch.events.ts:52: CHANGED — CreditReleasedForCancellationRecorded -> StockReleasedForCancellationRecorded (now owing credit.release) plus the new LateCreditApprovalRecorded
apps/orders/src/application/events/saga-dispatch.events.ts:57: CHANGED — CreditReleasedForCancellationRecorded -> StockReleasedForCancellationRecorded (now owing credit.release) plus the new LateCreditApprovalRecorded
apps/orders/src/application/sagas/order.sagas.ts:4: CHANGED — the fast-path branches: credit.release now has two sources; seven merged streams
apps/orders/src/application/sagas/order.sagas.ts:109: CHANGED — the fast-path branches: credit.release now has two sources; seven merged streams
apps/orders/src/application/sagas/order.sagas.ts:114: CHANGED — the fast-path branches: credit.release now has two sources; seven merged streams
apps/orders/src/application/sagas/order.sagas.ts:123: CHANGED — the fast-path branches: credit.release now has two sources; seven merged streams
apps/fulfillment/src/infrastructure/persistence/migrations.integration.spec.ts:387: NA-PROSE — round-trips a stock.released.v1 outbox row; no ordering
apps/fulfillment/src/infrastructure/persistence/migrations.integration.spec.ts:401: NA-PROSE — round-trips a stock.released.v1 outbox row; no ordering
apps/fulfillment/src/application/ports/consumer-name.ts:6: NA-PROSE — prose listing command-driven flows
apps/fulfillment/src/application/ports/stock-item-repository.port.ts:1: CORRECT — the lock surface B4 depends on; unchanged
apps/orders/src/infrastructure/persistence/schema/saga-commands.schema.ts:16: CORRECT — the command enum already contains both credit.release and stock.release
apps/orders/src/infrastructure/persistence/schema/saga-commands.schema.ts:22: CORRECT — the command enum already contains both credit.release and stock.release
apps/orders/src/infrastructure/persistence/schema/saga-commands.schema.ts:24: CORRECT — the command enum already contains both credit.release and stock.release
apps/orders/src/infrastructure/messaging/test-support/stub-saga-responders.ts:124: CORRECT — test stubs answer both release subjects and publish the matching facts either way
apps/orders/src/infrastructure/messaging/test-support/stub-saga-responders.ts:131: CORRECT — test stubs answer both release subjects and publish the matching facts either way
apps/orders/src/infrastructure/messaging/test-support/stub-saga-responders.ts:183: CORRECT — test stubs answer both release subjects and publish the matching facts either way
apps/orders/src/infrastructure/messaging/test-support/stub-saga-responders.ts:240: CORRECT — test stubs answer both release subjects and publish the matching facts either way

TOTALS: {'CORRECT': 86, 'CHANGED': 174, 'NA-PROSE': 6, 'GUARD-NEW': 9} sum = 275
```

**Totals: 174 CHANGED, 86 CORRECT, 9 GUARD-NEW, 6 NA-PROSE, 0 UNCLASSIFIED = 275** (= the hit count, so every hit is dispositioned).

### The prescribed pattern was not sufficient — a supplementary sweep, and what it found

The brief's pattern matches the command *strings*. It cannot match prose that names the **class** `IssueCreditReleaseCommand`, and two such doc-comments asserted the superseded design in words:

```
find apps/orders/src apps/fulfillment/src -name '*.ts' -not -path '*/dist/*' -print0 | xargs -0 grep -n -i 'reverse order\|released before stock\|credit.release.*FIRST\|FIRST.*credit hold\|IssueCreditReleaseCommand\|CreditReleasedForCancellation'
```

| Hit | Verdict |
|---|---|
| `apps/orders/src/application/commands/saga-dispatch.commands.ts:36` — *"`CancelOrderHandler`'s fast-path hop for the `credit_approved`/`confirmed` branch"* | **CHANGED.** False after SA-4: `CancelOrderHandler` no longer issues `credit.release` at all. Rewritten to name the real sources. |
| `apps/orders/src/application/commands/saga-dispatch.handlers.ts:63` — same sentence | **CHANGED**, same reason. |
| `apps/orders/src/application/ports/saga-commands.port.ts:37` — *"feature 41's follow-up pass, closing the … gap"* | **CHANGED** (refreshed): still historically true but silent about the new ordering and the new late-approval source. |
| `apps/orders/src/application/saga-steps.ts:150` | **CHANGED** (refreshed) — the multi-variant note now describes the branch, not the superseded pass. |
| `apps/orders/src/infrastructure/messaging/nats-saga-commands.adapter.ts:38`, `.spec.ts:70`, `orders-cancel.integration.spec.ts:6` | **CORRECT** — provenance statements about when the *subject* was added; still true, no ordering claim. |
| every remaining hit | occurrences inside the files already rewritten above. |

This is the *"enumerate on the wording of the claim being retired"* rule paying for itself: the retired claim's wording (`IssueCreditReleaseCommand` … *"CancelOrderHandler's fast-path hop"*) shares no token with the pattern the brief prescribed.

## B1 — the release order transposed (CODE)

`stock.release` first, `credit.release` second, and the inverse of what #7 did before. Every hop:

| Hop | Before (superseded) | After (SA-4) | File |
|---|---|---|---|
| The operator-cancel enqueue | `credit_approved`/`confirmed` → enqueue `credit.release`; `stock_reserved` → enqueue `stock.release` (two methods) | **all three statuses → enqueue `stock.release`** (ONE method, mirroring #8's single direct enqueue site) | `application/cancel-order.handler.ts` |
| The reply's plan | `['credit_release', 'stock_release']` | **`['stock_release', 'credit_release']`** | same |
| `stock.released.v1` @ `credit_approved`/`confirmed` | `cancel`, reason `operator_cancelled`, both compensation steps | **`advance`, no-op apply, `commandAfter: 'credit.release'`** | `application/saga-steps.ts` |
| `credit.released.v1` @ `credit_approved`/`confirmed` | `advance`, no-op apply, `commandAfter: 'stock.release'` | **`cancel`, reason from the fact, BOTH compensation steps in release order** | same |
| The two-release synthesis | `stepsFromCreditCompensation` — synthesised the **credit** step, then the observed stock fact | **`stepsFromStockThenCreditRelease`** — synthesises the **stock** step (no `eventId`, disclosed), then the observed credit fact | same |
| `stock.release`'s payload reason | owed by `credit.rejected.v1` **and** `credit.released.v1` | owed by `credit.rejected.v1` **only**; the operator-cancel row is enqueued directly with `order_cancelled` | `application/saga-command-payloads.ts` |
| `credit.release`'s payload | *"no step-table row ever names this"* | a **live** fact-driven case, reached two ways | same |
| The fast-path event | `CreditReleasedForCancellationRecorded` → `IssueStockReleaseCommand` | **`StockReleasedForCancellationRecorded` → `IssueCreditReleaseCommand`** | `application/events/saga-dispatch.events.ts`, `application/sagas/order.sagas.ts` |
| The publishing wrapper | `HandleCreditReleasedFactHandler` published on enqueue | **`HandleStockReleasedFactHandler`** publishes on enqueue; `HandleCreditReleasedFactHandler` now takes **no `EventBus` at all** (none of its three variants owes a command) | `application/commands/saga-fact.handlers.ts` |

Two deliberate strengthenings over a literal transposition, both of which #8 also has:
- **`mapCreditReleaseReason`** (new, exported): the terminal cancel reads `reason` **from the fact** and refuses anything but `order_cancelled`, instead of returning the constant `'operator_cancelled'` the old `stock.released.v1` variants used. A constant would make the terminal reason unfalsifiable by anything on the wire (defeat-list attack #2).
- **`compensationPlanned` is passed in at the call site** rather than derived inside the enqueue method, so the ORDER is decided where the status is known, in one place.

## B2 — the late credit approval (CODE)

`credit.approved.v1` for an order whose operator cancellation was already accepted issues `credit.release` and **nothing else**. Implemented exactly as the brief decided, mirroring #8:

- **No domain flag.** Confirmed on disk first: `cancel-order.handler.ts`'s compensating branch leaves the order's status and reason untouched, so the aggregate carries no marker — the enqueued row is the only durable evidence.
- **`SagaCommandStore.hasAcceptedOperatorCancel(tx, orderId)`** (new port method + Drizzle implementation): an `EXISTS`-shaped read over the order's `credit.release` **and** `stock.release` rows, narrowed by envelope **CONTENT**, no status filter. It takes the caller's `tx` so the answer and the status it is paired with come from one snapshot (guard A8 below).
- **The content test is written ONCE** — `application/operator-cancel-envelope.ts` exports `OPERATOR_CANCEL_EVENT_TYPE` and `isOperatorCancelEnvelope`, imported by the **writer** (`cancel-order.handler.ts`'s `buildTriggeringEnvelope`) and the **reader** (`drizzle-saga-command-store.ts`). #7 has only one reader today, so the module is what stops the pair becoming two literals that can drift; a test feeds the envelope the real handler enqueues to the real predicate.
- **The check runs before the generic status dispatch** in `SagaFactHandler`, for the two shapes the spec enumerates (`stock_reserved` + accepted cancel; `cancelled` + `operator_cancelled`). It enqueues, sets `enqueued = 'credit.release'`, and **returns without saving** — no transition, no `order.confirmed.v1`, no `despatch.create`.
- **The fast path had to change too**, and this is the one place #7's shape forced a difference from #8: #8 signals the fast path with a generic `SagaCommandRef(orderId, kind)`, while #7 maps **per-fact event classes** to fixed `Issue…Command`s. `HandleCreditApprovedFactHandler` therefore now chooses its event **by `result.enqueued`** — `LateCreditApprovalRecorded` (→ `IssueCreditReleaseCommand`) for the late case, `OrderConfirmed` (→ `IssueDespatchCreateCommand`) otherwise. Keying off the fact type would have issued a `despatch.create` for an order being cancelled; armed as A7.

One deliberate divergence from #8, recorded rather than smoothed over: #8 signals only on `EnqueueOutcome.Enqueued`; **#7 reports the command as owed on either outcome**, because that is #7's own documented D1 reasoning at the generic enqueue site three lines away (*"`already_owed` means the row exists, and the fast path re-dispatches the row that actually exists"*). Following #8 here would have contradicted #7's established idiom.

## B3 — the lost despatch race: ALREADY CORRECT, guard added

Checked before changing anything, as instructed. #7 already treats a `PRECONDITION_FAILED` reply as a **terminal** business rejection, for every command, and has since feature 42:

- `infrastructure/messaging/nats-saga-commands.adapter.ts:84,182` maps the `PRECONDITION_FAILED` `RpcError` to `SagaCommandBusinessRejectionError` (terminal), not `SagaCommandTransportError`.
- `infrastructure/saga/saga-command-dispatcher.ts:180-193` short-circuits the retry loop on it: no further attempt, no backoff, `markRejected` → `'rejected'`, and it returns **before** `park()`, `claimDeadLetter()` and the `onFirstPark` hook — the ONE hook that publishes the DLQ record and `order.saga_failed.v1`.
- `saga-command-store.port.ts:92` + the `rejected` status: `claimDue`'s predicate matches only `pending`/`parked`, so the row is structurally excluded from every future sweep.

**No production code was changed for B3.** What did not exist was a guard for *this* command and for the *absence* of dead-lettering: the existing feature-42 cases all use `stock.release` and none asserts `claimDeadLetterCalls` or `onFirstPark`. Added one test (armed as A3) that pins `despatch.create` + `PRECONDITION_FAILED` → `rejected`, one call, zero delays, **zero** park calls, **zero** dead-letter claims, **zero** `onFirstPark` invocations.

The "despatch wins" half of B3 — *"no `credit.release` is issued"* — needs no code either, and now follows structurally from B1: `credit.release` is owed **only** by `stock.released.v1`'s `credit_approved`/`confirmed` variants, and if the despatch won, Fulfillment releases nothing and emits no `stock.released.v1` (`apps/fulfillment/src/application/stock-reservation.handler.ts:128-130`, the `already_released` outcome, which returns before `saveAll` and therefore before any outbox row). That is asserted end-to-end by `apps/fulfillment/src/stock-release-idempotency.integration.spec.ts:127-148`.

## B4 — Fulfillment's one lock: ALREADY MET, guard added

Confirmed on disk, unchanged: `stock-reservation.handler.ts:103,109` and `despatch-creation.handler.ts:64,70` both take the order's stock rows via `stockIdsOfOrder` + `lockByIdsForOrder`, inside the unit of work. **No Fulfillment source was refactored.**

Searching for a test that fails if either path stops taking that lock:

```
find apps/fulfillment/src -name '*.ts' -not -path '*/node_modules/*' -not -path '*/dist/*' -print0 | xargs -0 grep -n 'lockByIdsForOrder\|stockIdsOfOrder'
```

returns **42 hits today, 9 of which are inside the new guard file**. Of the rest: 4 are the two production call sites (`stock-reservation.handler.ts:103,109`, `despatch-creation.handler.ts:64,70`), 6 are the port/repository declarations and their doc-comments, and **every single spec hit is a fake-repository member** (`async lockByIdsForOrder() { … }` / `async stockIdsOfOrder() { … }`) — a stub, never an assertion about the lock. The nearest existing evidence, `despatch-create.integration.spec.ts:235-290`, observes the outcome of a real race — which is evidence, not a guard: two transactions can serialise by luck, it needs Docker, and it would still pass if a handler took a *different* lock that happened to be exclusive.

So a new pure-unit guard, `apps/fulfillment/src/application/one-lock-arbitration.spec.ts`, asserts the mechanism for **both** paths: the method is `lockByIdsForOrder` (not either of the two real sibling locks the repository also offers), over the ids `stockIdsOfOrder` returned, scoped to the same order reference, **inside that handler's own transaction** (the fake unit of work hands out a distinct `tx` object and the recorded one must be identical), and — the arbitration claim itself — that the two paths' lock shapes are **equal to each other**. Armed twice, once per handler, by substituting a valid sibling lock (A4a/A4b).

## The ledger — this port runs #8 → #7, so the row's history half is a claim about #8

The ledger rule exists for translating a mechanism between stacks; here the direction is reversed (#8's id 62 is the reference implementation), so each row reads *"#8 relied on X; in #7 that property is supplied by Y"*, cited into #8's checkout.

| Property | #8 relied on | In #7 it is supplied by | Guard |
|---|---|---|---|
| The "has an operator cancel been accepted?" read sees the same state as the order it is paired with | The ambient `OrdersDbContext` under `READ_COMMITTED_SNAPSHOT ON`, executed after the caller's `UPDLOCK` read of the order row — documented at `src/Orders/Infrastructure/Saga/EfCoreSagaCommandStore.cs:366-373` | An **explicit `tx` parameter** on the port (`hasAcceptedOperatorCancel(tx, orderId)`), threaded from the same `runOnce` transaction that loaded the order. #7's Drizzle store has no ambient-context equivalent: `findByOrderAndCommand` runs on `this.db`, outside any transaction, so an ambient read here would have been a *different* snapshot | `saga-fact-handler.spec.ts` — *"still stock_reserved with the cancellation accepted…"* asserts `commandStore.txOfCall[0]` is identically the tx `runOnce` handed out (**armed A8**: passing `{}` instead of `tx` fails it) |
| One content test, not two copies | A `private static IsOperatorCancelEnvelope` shared by `ExtractOperatorCancelNote` and `HasAcceptedOperatorCancelAsync` inside one class — `EfCoreSagaCommandStore.cs:332-353`, whose own comment says *"so both read the SAME content test, never two independently-maintained ones"* | A shared module, `application/operator-cancel-envelope.ts`: #7's writer and reader are in **different layers** (application handler, infrastructure store), so a private member could not span them. The `eventType` literal now exists once in the whole app | `operator-cancel-envelope.spec.ts` (**armed A5**: dropping the content test; **armed A6**: changing the shared literal on both sides at once still fails, because `cancel-order.handler.spec.ts` pins the wire literal) |
| A late `credit.approved.v1` does not trigger the ordinary confirm/despatch fast path | A **generic** signal carrying the kind: `SagaCommandRef(order.Id.Value, SagaCommandKind.CreditRelease)` — `src/Orders/Application/Sagas/SagaFactHandler.cs:117` — so the fast path cannot be told by the fact type | A **new event class** plus a `result.enqueued`-keyed choice in `HandleCreditApprovedFactHandler`; #7's `@Saga()` maps event class → fixed command, so the fact type alone would have selected `IssueDespatchCreateCommand` | `saga-fact.handlers.spec.ts` — *"publishes LateCreditApprovalRecorded, NOT OrderConfirmed"* (**armed A7**) |
| The step table can express two legal preconditions per fact | `PairVariants` + `ForStatus` — `src/Orders/Application/Sagas/SagaStepTable.cs:78-82, 220-226, 255-264` | Already present in #7 (`stepVariantsFor`/`stepForStatus`, feature 41): **nothing to build**, only the variants' contents to transpose | the two rewritten `saga-steps.spec.ts` blocks (**armed A1b, A1c**) |
| The synthesised compensation step is honest about what it cannot know | `CompensationStepsFromStockThenCreditRelease` carries `EventId: null` and reuses the current fact's `OccurredAt` — `SagaStepTable.cs:159-173`, with the trade-off written out | The same shape, same disclosure, in `stepsFromStockThenCreditRelease` — and #7 already carried the identical trade-off in the opposite direction | `saga-steps.spec.ts` asserts `steps[0]` has **no** `eventId` |
| The reply's `compensationPlanned` order is a wire claim | `["stock_release", "credit_release"]` built at the branch in `CancelOrderCommandHandler.cs:152-154` | The same, passed into the one enqueue method | `cancel-order.handler.spec.ts` + `orders-cancel.controller.spec.ts` assert by index, not membership (**armed A1a**) |

**Divergence, stated rather than hidden:** #8 signals the fast path only when the enqueue actually inserted; #7 reports the command as owed on `already_owed` too (its own D1 rule). Same durability, different signalling convention.

## Arming table — every mutation, the ONE named spec, the verbatim failure

Protocol for each: `cp` backup → mutate → run the named spec → record verbatim → restore from the backup → `cmp` against the backup (all eight printed `RESTORED_IDENTICAL`) → re-read the restored line. TypeScript/Vitest transform from source on every run, so there is no compiled artefact to go stale; the confirming full runs afterwards are **539/539** and **92/92**. **No `git checkout`/`stash`/`restore`/`reset`/`clean` was used at any point.** No two test or build processes ran at once.

| # | Mutation (family) | Named spec | FAILED with |
|---|---|---|---|
| A1a | `compensationPlanned` for `credit_approved`/`confirmed` → `['credit_release','stock_release']` (**sibling transposition**) | `cancel-order.handler.spec.ts` › *OCR-stock-first (SA-4) — for status credit_approved …* (and `confirmed`) | `AssertionError: expected [ 'credit_release', 'stock_release' ] to deeply equal [ 'stock_release', 'credit_release' ]` — 2 failed \| 8 passed |
| A1b | `stock.released.v1`'s variants `commandAfter: 'credit.release'` → `'stock.release'` (**sibling substitution**) | `saga-steps.spec.ts` › *SA-4 — credit_approved: the winning stock release is an ADVANCE that owes credit.release …* | `AssertionError: expected 'stock.release' to be 'credit.release' // Object.is equality` — 2 failed \| 99 passed |
| A1c | `stepsFromStockThenCreditRelease` returns credit first, stock second (**ordering**) | `saga-steps.spec.ts` › *SA-4 — credit_approved: this is now the TERMINAL step …* | `AssertionError: expected { step: 'credit_released', …(4) } to match object { step: 'stock_released', …(1) }` — 2 failed \| 99 passed |
| A2 | the late-approval `credit.release` enqueue made unreachable (**deletion**) | `saga-fact-handler.spec.ts` › *still stock_reserved with the cancellation accepted …* | `AssertionError: expected { outcome: 'processed', …(1) } to deeply equal { … }` / `- "enqueued": "credit.release"` `+ "enqueued": "despatch.create"` — and the second case fell to `outcome: 'ignored'`. 2 failed \| 13 passed. **The received value is the defect itself**: without the branch the order is confirmed and a despatch is ordered for a cancelled order |
| A3 | the terminal-rejection short-circuit made unreachable, so a refusal falls through to retry → park → dead-letter (**routing**) | `saga-command-dispatcher.spec.ts` › *SA-4 — a despatch.create refused with PRECONDITION_FAILED after a winning release is TERMINAL …* | `AssertionError: expected 'parked' to be 'rejected' // Object.is equality` — 3 failed \| 12 passed, with the dispatcher's own log line `"exhausted attempts, command parked" … "command":"despatch.create"` |
| A4a | `StockReservationHandler.release` takes `lockByProductCodes` instead (**sibling substitution**) | `one-lock-arbitration.spec.ts` › *stock.release takes the order's stock rows through lockByIdsForOrder …* | `AssertionError: expected 'lockByProductCodes' to be 'lockByIdsForOrder' // Object.is equality` — 2 failed \| 1 passed (the cross-path equality case failed too) |
| A4b | `DespatchCreationHandler.create` takes `lockByProductCodes` instead (**sibling substitution**) | `one-lock-arbitration.spec.ts` › *despatch.create takes the SAME lock, the same way …* | `AssertionError: expected 'lockByProductCodes' to be 'lockByIdsForOrder' // Object.is equality` — 2 failed \| 1 passed |
| A5 | `isOperatorCancelEnvelope` drops the `eventType` test — *any* row of those two commands counts (**the R27 confusion this guard exists to prevent**) | `operator-cancel-envelope.spec.ts` › *does NOT recognise the real fact credit.rejected.v1 …* | `AssertionError: expected true to be false // Object.is equality` — 5 failed \| 2 passed |
| A6 | `OPERATOR_CANCEL_EVENT_TYPE` changed to `'orders.cancel.accepted'` on **both sides at once** (writer and reader still agree — the mutation the shared-constant design would otherwise hide) | `cancel-order.handler.spec.ts` › *OCR-stock_reserved …* (+2) | `AssertionError: expected 'orders.cancel.accepted' to be 'orders.cancel.requested' // Object.is equality` — 3 failed \| 7 passed |
| A7 | `HandleCreditApprovedFactHandler` publishes `OrderConfirmed` unconditionally again (**keying off the fact type**) | `saga-fact.handlers.spec.ts` › *SA-4 — HandleCreditApprovedFactHandler publishes LateCreditApprovalRecorded, NOT OrderConfirmed …* | `AssertionError: expected OrderConfirmed{ …(2) } to be an instance of LateCreditApprovalRecorded` — 1 failed \| 11 passed |
| A8 | `hasAcceptedOperatorCancel` called with `{}` instead of the caller's `tx` (**the snapshot-consistency premise**) | `saga-fact-handler.spec.ts` › *still stock_reserved with the cancellation accepted …* | `AssertionError: expected {} to be { id: +0 } // Object.is equality` — 1 failed \| 14 passed |

### The defeat list, run against these guards before submitting

| # | Attack | Ran? |
|---|---|---|
| 1 | Delete the behaviour | **Yes** — A2 (the late-approval enqueue), A3 (the terminal-rejection branch) |
| 2 | Corrupt a payload field the test supplied | **Yes** — `mapCreditReleaseReason` exists precisely so the terminal reason is read from the fact; `saga-steps.spec.ts` feeds `reason: 'invoice_paid'` and requires a throw. `stock.release`'s `reason: 'order_cancelled'` is asserted at the enqueue site |
| 3 | Substitute a valid sibling identifier | **Yes, four times** — A1b (`credit.release` ↔ `stock.release`), A4a/A4b (`lockByIdsForOrder` ↔ `lockByProductCodes`), A6 (the synthetic `eventType`). Sibling families here: the six `SAGA_COMMAND_KINDS`, the four lock methods on `StockItemRepository`, the two `compensationPlanned` members |
| 4 | Shadow the pattern from a comment or string literal | **N/A** — every guard here executes code; none is a text scanner. (It *did* apply to the enumeration, which is why the supplementary sweep above was run and found two prose sites.) |
| 5 | Hide the real thing in a dead region | **N/A** — same reason; there is no conditional compilation in TypeScript, and `if (false && …)` was used as a mutation, not defended against |
| 6 | Hide it in a raw/verbatim string | **N/A** — no scanner |
| 7 | Drop an OPTIONAL element entirely | **Yes** — `steps[0]` is asserted to have **no** `eventId` (`not.toHaveProperty`), so adding a fabricated one fails; `expect('commandAfter' in step).toBe(false)` pins the terminal step's absence of a command |
| 8 | Compare a literal to a literal | **Considered, and one such site was strengthened**: `orders-cancel.controller.spec.ts` is a pass-through test whose input it supplies itself, so an index assertion was added — it now fails if the controller reorders or rebuilds the array. It still cannot prove the *handler's* order; `cancel-order.handler.spec.ts` and the integration spec do that |
| 9 | Satisfy the closer half and leave the premise half stale | **Yes** — this is exactly what the supplementary sweep found (two doc-comments still asserting "CancelOrderHandler's fast-path hop"), plus `saga-command-payloads.ts`'s dead `credit.released.v1` branch, which was **removed** rather than left as a stale premise |
| 10 | Let a build-output copy join the population | **Yes** — `dist/` and `node_modules/` excluded by `-not -path`, at the source, never by post-filtering grep's output |

### One arming ran into its own false-negative check

A8's first attempt asserted on a string that did not exist in the file (`… && await this.commandStore.hasAcceptedOperatorCancel(tx, order.id));` with a leading `&&` that the wrapped line does not carry). The script raised `AssertionError` and the spec then ran **green — 15 passed**. That green run is NOT evidence of anything: the mutation never landed. It was re-run against the actual text and failed as recorded. Noting it because a green run after a failed mutation is exactly the shape that gets mistaken for "the guard is weak".

## What I could not do, and other disclosures

- **`hasAcceptedOperatorCancel`'s SQL is not directly unit-tested.** The predicate it applies is (`operator-cancel-envelope.spec.ts`, armed twice), and the call site's use of it is (`saga-fact-handler.spec.ts`, armed twice, including the transaction identity) — but the Drizzle query itself (the `WHERE order_id = ? AND command IN (…)` and the `.some(...)` over the JSON column) has no test, because #7's store adapter is only ever exercised through Testcontainers integration specs. #8's equivalent is covered by container tests there. **Adding one here would mean a new integration spec and a container trio**, which is more than this backlog entry's four behaviours asked for; I have flagged it rather than written it. The risk it leaves: a wrong column or a wrong command set would be caught only by an integration run.
- **The `credit.release` row's own uniqueness across the two B2/B1 sources.** `saga_commands` is unique on `(order_id, command)`, so if a late `credit.approved.v1` and a winning `stock.released.v1` both owe `credit.release` for one order, the second enqueue resolves to `already_owed` and the fast path re-dispatches the existing row — correct by #7's D1 design, and not newly asserted here.
- **Prettier**: not run as a fix; the repository fails `pnpm format` on 740 files at HEAD and correcting that is out of scope. New files were written in the surrounding style and `eslint` is clean.
- **No `feature_list.json` transition** was made in either repository, and no `specs/` file in either repository was read-modified. The record you are reading is the only file written outside #7's `apps/`.
- **Integration coverage of B2 end-to-end** (a real late `credit.approved.v1` racing a real accepted cancellation) does not exist in either repository; #8 covers it at unit level too. Worth an entry if the reviewer wants parity there.

## Files touched (all in `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`)

**New (3):**
- `apps/orders/src/application/operator-cancel-envelope.ts`
- `apps/orders/src/application/operator-cancel-envelope.spec.ts`
- `apps/fulfillment/src/application/one-lock-arbitration.spec.ts`

**Modified — production (10):**
- `apps/orders/src/application/cancel-order.handler.ts`
- `apps/orders/src/application/saga-steps.ts`
- `apps/orders/src/application/saga-command-payloads.ts`
- `apps/orders/src/application/saga-fact-handler.ts`
- `apps/orders/src/application/ports/saga-command-store.port.ts`
- `apps/orders/src/application/ports/saga-commands.port.ts` *(doc only)*
- `apps/orders/src/application/events/saga-dispatch.events.ts`
- `apps/orders/src/application/commands/saga-fact.handlers.ts`
- `apps/orders/src/application/commands/saga-dispatch.commands.ts` *(doc only)*
- `apps/orders/src/application/commands/saga-dispatch.handlers.ts` *(doc only)*
- `apps/orders/src/application/sagas/order.sagas.ts`
- `apps/orders/src/infrastructure/saga/drizzle-saga-command-store.ts`

**Modified — tests (10):**
- `apps/orders/src/application/cancel-order.handler.spec.ts`
- `apps/orders/src/application/saga-steps.spec.ts`
- `apps/orders/src/application/saga-fact-handler.spec.ts`
- `apps/orders/src/application/saga-fact-handler-saga-completion-metrics.spec.ts` *(fake store member)*
- `apps/orders/src/application/commands/saga-fact.handlers.spec.ts`
- `apps/orders/src/application/sagas/order.sagas.spec.ts`
- `apps/orders/src/presentation/orders-cancel.controller.spec.ts`
- `apps/orders/src/orders-cancel.integration.spec.ts`
- `apps/orders/src/infrastructure/saga/saga-command-dispatcher.spec.ts`
- `apps/orders/src/infrastructure/saga/saga-command-dispatcher-log-trace-id.spec.ts`, `saga-command-sweeper.spec.ts`, `saga-command-sweeper-log-trace-id.spec.ts` *(fake store member only)*

**Deliberately NOT touched:** `apps/fulfillment/src/application/stock-reservation.handler.ts` and `despatch-creation.handler.ts` (B4 was already met — guards, not refactors), and every `specs/` file in both repositories.
