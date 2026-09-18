import { expect, test } from '@playwright/test';

/**
 * Scenario 1 (happy path to `completed`) + Scenario 3 (payment flips the
 * invoice to `paid`), deliberately combined into one order/one test — per
 * #7's own precedent (apps/web/e2e/happy-path.spec.ts), these overlap, so
 * this places exactly one order rather than two where one will do, while
 * still asserting each fact explicitly.
 *
 * Synchronisation rule (binding, CLAUDE.md / feature 16's review in #7,
 * carried over here): every wait below is for a TERMINAL or MONOTONIC
 * fact — `invoiced` (the saga's own resting point until a human registers a
 * payment), `paid` on the invoice (never reverts — openapi.yaml: issued ->
 * paid only), `completed` on the order (terminal). Nothing here ever polls
 * for a transient mid-saga status (`stock_reserved`/`credit_approved`/
 * `confirmed`/`despatched`), which the correct saga leaves within a single
 * poll interval by construction.
 */
test('an order with a non-.99 total reaches completed, and the invoice explicitly flips to paid', async ({ page }) => {
  await page.goto('/orders/place');

  // Retailer: CarrefourEs. Company: ALBIONFOODS. Neither pairing is enforced
  // server-side — confirmed live against the real Gateway before writing
  // this test, same as #7 found. ALBIONFOODS is used specifically because it
  // is NOT one of the six companies src/Seed/Domain/Sagas/SagaFixtures.cs
  // seeds a saga against (IBERFOODS, FRESHFR, TOOLIBERIA, GERMANFOODS,
  // UKDISTRIB), so src/Seed/Domain/Data/StockSeed.cs gives it the FULL
  // baseline: 500 units of every seeded product, PRD-0006 included — a
  // company with zero stock for the chosen product would fail the
  // acceptance-time availability check (409/STOCK_UNAVAILABLE), an unrelated
  // failure this test does not exist to cover.
  // getByRole('combobox', ...), not getByLabel: until the catalogue query
  // resolves, place-order-form.tsx renders a plain fallback `<Input>` under
  // the SAME label/id (`retailersUsable` is false while `retailers.data` is
  // still `undefined`, not only on a real catalogue failure) — a
  // `getByLabel` locator matches that Input immediately and `selectOption`
  // then fails outright (not an actionability wait Playwright retries: found
  // live, the very first version of this test). Scoping by role="combobox"
  // (only the real `<select>` carries it) makes Playwright's own auto-wait
  // hold for the real control to mount.
  await page.getByRole('combobox', { name: 'Retailer' }).selectOption('CarrefourEs');
  await page.getByRole('combobox', { name: 'Company' }).selectOption('ALBIONFOODS');

  // Still NOT getByRole('combobox', { name: 'Product' }) / getByLabel('Product'),
  // though id 106 (see progress/impl_place_order_line_key_is_module_scope_mutable_state.md)
  // has since fixed the underlying defect this locator was written to route
  // around: place-order-form.tsx's product <select> `id` and its <label for>
  // used to derive BOTH from a module-scope mutable `nextLineKey` counter
  // that free-ran across every SSR request a long-lived `next start` process
  // ever served, so the server's count and the browser's own fresh count
  // could diverge (`for="product-7"` vs `id="product-2"`, reproduced across
  // repeated loads) and React's hydration did not always repair the stale
  // `for` attribute once adopted. The pair now derives from React's own
  // `useId()` instead, proven by a hydration-repetition guard in
  // place-order-form.test.tsx — this structural locator was kept rather than
  // reverted to getByLabel/getByRole('combobox', { name: 'Product' }) as a
  // LIGHT-change judgment call (CLAUDE.md cost discipline): reverting it
  // would need a real e2e run against the full docker stack to confirm,
  // which is disproportionate to a component-local id-generation fix already
  // proven at the unit level. Scoping through `order-line` (data-testid, one
  // row = one product per the brief) and taking its one <select> continues to
  // work regardless.
  await page.getByTestId('order-line').locator('select').selectOption('PRD-0006');
  await page.getByTestId('quantity-input').fill('2');

  const submit = page.getByRole('button', { name: 'Place order', exact: true });
  await expect(submit).toBeEnabled();
  await submit.click();

  const success = page.getByTestId('place-order-success');
  await expect(success).toBeVisible();
  const successText = await success.innerText();
  const orderReference = successText.match(/Order (ORD-\d+) accepted/)?.[1];
  expect(orderReference, `expected the success banner to name an ORD-nnnnnn reference, got: "${successText}"`).toBeTruthy();

  // Real navigation through the success banner's OWN link — #8's
  // place-order-form.tsx renders `accepted-order-link` straight to
  // `/orders/{orderId}` (no intermediate "order list" link the way #7's
  // banner does — a real structural difference, recorded in
  // progress/impl_e2e_playwright.md), so this proves that link works rather
  // than a bare page.goto.
  const orderLink = page.getByTestId('accepted-order-link');
  await expect(orderLink).toBeVisible();
  await orderLink.click();
  await expect(page).toHaveURL(/\/orders\/[0-9a-f-]+$/);

  // Terminal/monotonic wait #1: `invoiced` is where the automatic part of
  // the saga rests until a human registers a payment — not a state the
  // correct saga leaves on its own within a poll interval.
  await expect(page.getByTestId('order-detail-status')).toHaveText('invoiced', { timeout: 30_000 });

  await page.goto('/billing');

  const invoiceRow = page.locator('[data-testid="invoice-row"]').filter({ hasText: orderReference! });
  await expect(invoiceRow).toBeVisible({ timeout: 30_000 });

  await invoiceRow.getByTestId('register-payment-button').click();

  // #8's PaymentForm (billing-view.tsx) renders above the invoices table,
  // not nested inside a row (structural difference), but the locator is
  // identical to #7's: page-level getByTestId, no adaptation needed.
  const paymentForm = page.getByTestId('payment-form');
  await expect(paymentForm).toBeVisible();

  // Pre-filled by the page itself (paymentReference from suggestPaymentReference,
  // amount from the invoice's own totalAmount) — submitted as-is, exactly the
  // "register payment through the billing UI button" step the brief names.
  const submitPayment = paymentForm.getByTestId('submit-payment-button');
  await expect(submitPayment).toBeEnabled();
  await submitPayment.click();

  await expect(paymentForm.getByTestId('payment-outcome-accepted')).toBeVisible({ timeout: 30_000 });

  // Scenario 3, explicit: the invoice ROW's own status badge (not merely
  // inferred from the order later reaching `completed`) flips to `paid`.
  // `paid` is itself terminal for an invoice (openapi.yaml: invoices only
  // ever go issued -> paid, never back), so this is monotonic evidence too.
  await expect(invoiceRow.getByTestId('invoice-status')).toHaveText('paid', { timeout: 30_000 });

  const viewOrderLink = paymentForm.getByTestId('view-order-link');
  await expect(viewOrderLink).toBeVisible({ timeout: 30_000 });
  await viewOrderLink.click();

  // Terminal/monotonic wait #2: `completed`, the saga's genuine terminal
  // state on the happy path — never caught mid-flight at `paid` on the order
  // itself (which the saga leaves immediately for `completed`).
  await expect(page.getByTestId('order-detail-status')).toHaveText('completed', { timeout: 30_000 });
});
