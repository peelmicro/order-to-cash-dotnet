import { expect, test } from '@playwright/test';

/**
 * Scenario 2: the `.99` compensation path. `fillCompensationDemo()`
 * (apps/web/src/features/orders/place-order-form.tsx) constructs exactly
 * 1 x a product with an explicit `unitPrice` of 24999 minor units (falls
 * back to PRD-0001 if the catalogue has not answered yet, but a product
 * covered by this order's unit-price override either way) — a total ending
 * in `.99`, which the credit simulator deliberately refuses
 * (`simulated_cents_rule`), triggering the saga's compensation path: stock
 * released, then the order cancelled.
 *
 * Deliberately NOT asserting which exact retailer/company/product the demo
 * fill chose (confirmed live against the real Gateway before writing this
 * test: `GET /catalog/retailers|companies|products` sort ALPHABETICALLY by
 * code, so with the catalogue loaded the demo fill picks AldiDe / ALBIONFOODS
 * / PRD-0001 — the first row of each seeded list, not #7's CarrefourEs /
 * IBERFOODS pairing, since #8's catalogue sort order differs from #7's) —
 * the running total is the behaviour this test actually depends on, and
 * #8's currencies (EUR/GBP/USD) are all exponent-2, so "249.99" holds
 * regardless of which retailer's currency was picked.
 *
 * Synchronisation rule: waits only for `cancelled` — the saga's genuine
 * terminal state here — never for a mid-compensation state.
 *
 * **D4, on the ordering assertion below** (ported verbatim from #7's own
 * comment, apps/web/e2e/compensation.spec.ts in the #7 checkout). The credit
 * simulator's `.99` refusal is deterministic, but amendment A1's original
 * defect (`bf59af9` in #7) was a *random* `eventId` tie-break — so this e2e
 * assertion is a probabilistic guard against that defect class (it catches a
 * full, deterministic inversion, but is not guaranteed to catch every
 * instance of a random tie-break). The deterministic guard for that class is
 * feature 31's black-box `assertCausalOrder` (every entry whose
 * `causationId` names another entry, cause precedes effect, checked for ALL
 * entries, not just this one pair) — treat this test as a real-browser
 * confirmation on top of that guard, not a replacement for it.
 */
test('a .99 order is cancelled with credit_rejected, and the timeline shows both compensation steps in order with the causal link rendered', async ({ page }) => {
  await page.goto('/orders/place');

  await page.getByRole('button', { name: 'Fill demo order (.99 → compensation)' }).click();

  // The demo fill sets retailer/company directly (not through the <select>
  // components), so the running total should already reflect 24999 minor
  // units (24.999 -> "249.99") before submit — checked as a sanity guard,
  // not the test's main assertion.
  await expect(page.getByTestId('running-total')).toContainText('249.99');

  const submit = page.getByRole('button', { name: 'Place order', exact: true });
  await expect(submit).toBeEnabled();
  await submit.click();

  const success = page.getByTestId('place-order-success');
  await expect(success).toBeVisible();
  const successText = await success.innerText();
  const orderReference = successText.match(/Order (ORD-\d+) accepted/)?.[1];
  expect(orderReference, `expected the success banner to name an ORD-nnnnnn reference, got: "${successText}"`).toBeTruthy();

  // Real navigation through the success banner's OWN link (see
  // happy-path.spec.ts's comment on the same structural difference from #7).
  const orderLink = page.getByTestId('accepted-order-link');
  await expect(orderLink).toBeVisible();
  await orderLink.click();
  await expect(page).toHaveURL(/\/orders\/[0-9a-f-]+$/);

  // Terminal/monotonic wait: `cancelled` is where this saga rests forever
  // once the compensation completes — never caught mid-flight at
  // `stock_reserved`/`credit_approved` (both transient here) or at the
  // intermediate `stock.released.v1` fact alone.
  await expect(page.getByTestId('order-detail-status')).toHaveText('cancelled', { timeout: 30_000 });
  await expect(page.getByText('credit_rejected', { exact: true })).toBeVisible();

  const timelineEntries = page.getByTestId('timeline-entry');
  const readEventTypes = () =>
    timelineEntries.evaluateAll((nodes) =>
      nodes.map((node) => {
        const paragraphs = Array.from(node.querySelectorAll('p'));
        // The event-type line is the second <p> inside the entry's text block
        // (first is the human summary) — see order-detail-view.tsx's <li>.
        // NOTE: deliberately not `hasText`-filtered locators here — the
        // `order.cancelled.v1` entry's OWN causal-link text also contains
        // the literal string "stock.released.v1" ("caused by
        // stock.released.v1"), so a text-filtered locator for that string
        // resolves to both entries (the same real strict-mode collision #7
        // found live against its container). Reading each entry's own
        // second <p> avoids the ambiguity entirely.
        return paragraphs[1]?.textContent?.trim() ?? '';
      }),
    );

  // D3 (ported from #7): assert the RELATIONSHIP this test actually cares
  // about — both compensation facts present in the rendered timeline —
  // rather than an exact entry count. `toHaveCount(5)` would break the day a
  // legitimate new fact joins this path (a notification fact, say) for a
  // reason entirely unrelated to what this test verifies.
  await expect
    .poll(readEventTypes, { timeout: 30_000, message: 'expected the timeline to include both stock.released.v1 and order.cancelled.v1' })
    .toEqual(expect.arrayContaining(['stock.released.v1', 'order.cancelled.v1']));

  const eventTypesByEntry = await readEventTypes();

  const stockReleasedIndex = eventTypesByEntry.indexOf('stock.released.v1');
  const orderCancelledIndex = eventTypesByEntry.indexOf('order.cancelled.v1');
  expect(stockReleasedIndex, `stock.released.v1 not found among rendered timeline entries: ${JSON.stringify(eventTypesByEntry)}`).toBeGreaterThanOrEqual(0);
  expect(orderCancelledIndex, `order.cancelled.v1 not found among rendered timeline entries: ${JSON.stringify(eventTypesByEntry)}`).toBeGreaterThanOrEqual(0);
  expect(stockReleasedIndex, 'stock.released.v1 must appear BEFORE order.cancelled.v1 in the rendered timeline').toBeLessThan(orderCancelledIndex);

  // Amendment A1's causal edge, rendered: the cancellation entry names
  // "caused by stock.released.v1" — the single end-to-end proof of the
  // defect class fixed in #7's bf59af9.
  const cancelledEntry = timelineEntries.nth(orderCancelledIndex);
  const causation = cancelledEntry.getByTestId('timeline-causation');
  await expect(causation).toBeVisible();
  await expect(causation).toContainText('caused by');
  await expect(cancelledEntry.getByTestId('timeline-causation-link')).toHaveText('stock.released.v1');
});
