# impl — SA-5 money alignment (id 97, bullet 5) and the stack label (id 99, #7 half), in #7

Repository worked in: `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` (#7). Every change is under `apps/web/`. I did not touch `specs/shared/` or any `feature_list.json`, and ran no git command that writes. Date: 2026-09-17.

**Result: PASS.**

## What was built

### Part 1 — SA-5: money formatting uses the ISO 4217 exponent

`apps/web/app/lib/money.ts`:
- New `currencyExponent(currency)` reads the exponent from `Intl.NumberFormat('en', { style: 'currency', currency }).resolvedOptions().maximumFractionDigits`. Results are cached per upper-cased code. If `Intl` rejects the code (a malformed code, or a half-typed `"EU"` in the place-order Currency field), it falls back to 2.
- New `currencyInputStep(currency)` gives the HTML `step` for one minor unit (`"0.01"` / `"1"` / `"0.001"`).
- `formatMoney(minorUnits, currency)` now divides by `10 ** exponent` instead of `100`. This division only affects display.
- `currencyInputStep(currency)` also drives each amount `<input type="number">`'s native constraint validation (`validity.stepMismatch`), not merely its display: a fixed `step="0.01"` on a BHD field rejects a valid 3-decimal value such as `0.005` at submit time. See the correction below (Fix round 1).
- `decimalStringToMinorUnits(value, currency)` takes a **new `currency` parameter**. The regex allows `{1,exponent}` fractional digits, and a 0-exponent currency allows no fraction at all. Parsing is still digit-wise: the whole and fractional digits are joined as text, with the fraction padded to the exponent, and converted to a number once. The result must pass `Number.isSafeInteger`.
- `minorUnitsToDecimalString(minorUnits, currency)` takes a **new `currency` parameter**. It now builds the string digit by digit instead of using `(x / 100).toFixed(2)`.
- The old "noted as a simplification" comment is gone. The new comments say what is true: SA-5 names the ISO 4217 exponent as the source, no response carries it, and it is read from `Intl`. The float-trap comment was kept and extended: while writing the tests I found that even `Math.round(parseFloat(x) * 100)` is off by one for large safe amounts such as `"82718514212381.37"`.

Callers updated: `app/pages/billing/index.vue` and `app/pages/orders/place.vue`. The billing payment form uses the invoice's own currency; the place-order form uses `form.currency`. In the same two pages, the amount inputs' hard-coded `step="0.01"` became `:step="currencyInputStep(...)"`, and the line-discount placeholder `"0.00"` became `minorUnitsToDecimalString(0, form.currency)`. Without those changes a JPY or BHD input would still have stepped in cents.

### Part 2 — the stack label `#7 · NestJS / Nuxt`

- `apps/web/app/lib/stack-label.ts` is the only definition. It exports `STACK_LABEL = '#7 · NestJS / Nuxt'` and `DOCUMENT_TITLE = 'Order-To-Cash · ' + STACK_LABEL`.
- `app/layouts/default.vue`: a small muted bordered `<span data-testid="stack-label">` next to the "Order-To-Cash" link in the signed-in header.
- `app/pages/login.vue`: a small muted `<p data-testid="stack-label">` under the card title. The layout hides its header on `/login`, so the login page needs its own label.
- `app/app.vue`: `useHead({ title: DOCUMENT_TITLE })`. No page sets its own title (`grep -rn "useHead\|useSeoMeta" app` returned no hits before this change), so the tab title is the same everywhere.
- Enumeration: `grep -rn "NestJS / Nuxt" app server --include=*.vue --include=*.ts` returns `lib/stack-label.ts:10` plus the spec's own assertions. There is no second copy.

## Files touched (all under #7's `apps/web/`)

Modified: `app/lib/money.ts`, `app/pages/billing/index.vue`, `app/pages/orders/place.vue`, `app/app.vue`, `app/layouts/default.vue`, `app/pages/login.vue`.
New: `app/lib/money.spec.ts` (49 cases), `app/pages/currency-exponent.spec.ts` (2), `app/lib/stack-label.ts`, `app/stack-label.spec.ts` (4).

## Call-site list

The brief's command, `grep -rn "formatMoney\|decimalStringToMinorUnits\|minorUnitsToDecimalString" app server`, run on the unmodified tree, returned **22** lines outside `money.ts`, not 21. Those 22 lines are 4 imports, 2 comments and **16 calls**. Every line is listed below with its line number before the change. Lines marked "changed" now pass the currency.

| # | Site (pre-change line) | Kind | Action |
|---|---|---|---|
| 1 | `pages/orders/[id].vue:10` | import | unchanged |
| 2 | `pages/orders/[id].vue:185` | `formatMoney(totalAmount, detail.currency)` | unchanged (already passed currency) |
| 3 | `pages/billing/index.vue:13` | import | adds `currencyInputStep` |
| 4 | `pages/billing/index.vue:103` | `minorUnitsToDecimalString(invoice.totalAmount)` | **changed**: `, invoice.currency` |
| 5 | `pages/billing/index.vue:128` | `decimalStringToMinorUnits(amountInput)` | **changed**: `, invoice.currency` |
| 6 | `pages/billing/index.vue:227` | `formatMoney(credit.creditLimit, credit.currency)` | unchanged |
| 7 | `pages/billing/index.vue:230` | `formatMoney(credit.activeHolds, credit.currency)` | unchanged |
| 8 | `pages/billing/index.vue:233` | `formatMoney(credit.openExposure, credit.currency)` | unchanged |
| 9 | `pages/billing/index.vue:236` | `formatMoney(credit.availableCredit, credit.currency)` | unchanged |
| 10 | `pages/billing/index.vue:335` | `formatMoney(invoice.totalAmount, invoice.currency)` | unchanged |
| 11 | `pages/billing/index.vue:378` | comment | unchanged |
| 12 | `pages/orders/place.vue:11` | import | adds `currencyInputStep`, `minorUnitsToDecimalString` |
| 13 | `pages/orders/place.vue:51` | comment | unchanged |
| 14 | `pages/orders/place.vue:96` | `formatMoney(selected.price, selected.currency)` | unchanged |
| 15 | `pages/orders/place.vue:156` | `decimalStringToMinorUnits(l.unitPriceInput)` (running total) | **changed**: `, form.currency` |
| 16 | `pages/orders/place.vue:157` | `decimalStringToMinorUnits(l.lineDiscountInput)` (running total) | **changed**: `, form.currency` |
| 17 | `pages/orders/place.vue:216` | `decimalStringToMinorUnits(l.unitPriceInput)` (submit) | **changed**: `, form.currency` |
| 18 | `pages/orders/place.vue:217` | `decimalStringToMinorUnits(l.lineDiscountInput)` (submit) | **changed**: `, form.currency` |
| 19 | `pages/orders/place.vue:355` | `formatMoney(product.price, product.currency)` | unchanged |
| 20 | `pages/orders/place.vue:420` | `formatMoney(runningTotal, form.currency)` | unchanged |
| 21 | `pages/orders/index.vue:10` | import | unchanged |
| 22 | `pages/orders/index.vue:164` | `formatMoney(order.totals.totalAmount, order.currency)` | unchanged |

New call sites added: `billing/index.vue:386` and `place.vue:383,397` (`:step`), plus `place.vue:398` (`:placeholder`). `server/` has no hits.

## Arming table

Harness: `scratchpad/arm.sh`. Each arm backs the file up with `cp -p`, applies one replacement, runs the named spec, restores with `cp`, runs `touch`, and confirms with `cmp`. Every arm printed `RESTORED … (cmp identical)`. Vitest transforms source on every run, so no stale build output was involved; the files were touched anyway. Before arming, the call-site spec ran green on its own (2/2).

| Arm | Mutation | Named test(s) that failed (verbatim message) |
|---|---|---|
| A1 | `formatMoney` back to `format(minorUnits / 100)` | `formatMoney … > JPY (0): 24999 minor units is 24,999 yen, not 249.99`: `expected 'JP¥250' to be 'JP¥24,999'`; `… > BHD (3)…`: `expected 'BHD 249.990' to be 'BHD 24.999'` (2 failed / 46 passed) |
| A2 | `decimalStringToMinorUnits` exponent hard-coded to 2 (this reproduces the old `{1,2}` regex and `× 100`) | 10 failed, e.g. `"1.005" in BHD is 1005`: `expected undefined to be 1005`; `"24999" in JPY is 24999`: `expected 2499900 to be 24999`; `"1.5" in JPY is rejected`: `expected 150 to be undefined` |
| A3 | `minorUnitsToDecimalString` back to `(minorUnits / 100).toFixed(2)` | 7 failed, e.g. `24999 in JPY is "24999"`: `expected '249.99' to be '24999'`; `5 in BHD is "0.005"`: `expected '0.05' to be '0.005'` |
| A4 | parse via `Math.trunc(Number(trimmed) * 10 ** exponent)` | 6 failed, e.g. `"0.29" in EUR is 29`: `expected 28 to be 29`; `"19.99" in EUR is 1999`: `expected 1998 to be 1999`; `"1.005" in BHD is 1005`: `expected 1004 to be 1005` |
| A4b | parse via `Math.round(Number(trimmed) * 10 ** exponent)` | **First run: survived, 48/48.** Rounding is correct for small amounts. A random search found large safe-integer counterexamples, so I added the case `is exact where even Math.round(parseFloat(x) * 100) is not`. **Re-armed: killed.** Message: `expected 8271851421238138 to be 8271851421238137` |
| A5 / A5r | `currencyExponent` always returns 2 (re-run after the lint fix below) | 20 failed, e.g. `JPY has exponent 0`: `expected 2 to be +0`; `BHD has exponent 3`: `expected 2 to be 3` |
| A5x | `currencyExponent` reads `minimumIntegerDigits` instead of `maximumFractionDigits` (substitutes a sibling `Intl` option) | 34 failed |
| C1 | billing submit: `decimalStringToMinorUnits(…, 'EUR')` | `billing/index.vue — a 0-exponent invoice (JPY) > renders, pre-fills and submits…`: `expected { amount: 2499900, currency: 'JPY' } to deeply equal { amount: 24999, currency: 'JPY' }` |
| C2 | billing prefill: `minorUnitsToDecimalString(…, 'EUR')` | same test: `expected '249.99' to be '24999'` |
| C3 | billing step: `currencyInputStep('EUR')` | same test: `expected '0.01' to be '1'` |
| C4 | billing invoice total: `formatMoney(…, 'EUR')` | same test: `expected '€249.99' not to contain '249.99'` |
| C5 | place submit unitPrice: `'EUR'` | `orders/place.vue — a 3-exponent order currency (BHD) > parses …`: `expected { productCode: 'PRD-0001', …(2) } to match object { unitPrice: 1005, lineDiscount: 500 }` |
| C6 | place submit lineDiscount: `'EUR'` | same test: `expected { productCode: 'PRD-0001', …(3) } to match object { unitPrice: 1005, lineDiscount: 500 }` |
| C7 | place running-total unitPrice: `'EUR'` | same test: `expected '-BHD 0.500' to be 'BHD 0.505'` |
| C8 | place running-total lineDiscount: `'EUR'` | same test: `expected 'BHD 0.955' to be 'BHD 0.505'` |
| C9 | place unit-price step: `currencyInputStep('EUR')` | same test: `expected '0.01' to be '0.001'` |
| S1 | remove `{{ STACK_LABEL }}` from `layouts/default.vue` | `stack label (id 99) > is shown in the header of a signed-in page`: `Expected element to have text content: … Received:` (1 failed / 3 passed) |
| S2 | remove `{{ STACK_LABEL }}` from `pages/login.vue` | `… > is shown on the login page`: `Expected element to have text content: … Received:` |
| S3 | remove `useHead({ title: DOCUMENT_TITLE })` from `app.vue` | `… > names the stack in the browser-tab title`: `expected '' to be 'Order-To-Cash · #7 · NestJS / Nuxt'` |
| S4 | change the single definition to `'#8 · .NET / Next.js'` (swaps in the sibling app's label) | all 4 failed, e.g. `expected 'Order-To-Cash · #8 · .NET / Next.js' to be 'Order-To-Cash · #7 · NestJS / Nuxt'` |

Order of events: arms A1–A5, A4b and C1–C9 ran before a lint fix to `currencyExponent`. That fix changed `let exponent = DEFAULT_EXPONENT;` to `let exponent: number;` and did not change behaviour. I re-ran A5 (as A5r) and A5x after the fix, and both killed. The final full suite ran after all arms and was green.

### Defeat list

- Rows 1–3 (delete / corrupt / substitute) were run: A1–A5x and S1–S3 delete or corrupt, and C1–C9 and S4 substitute.
- Rows 4–6 (comment, dead region, raw string) do not apply. These guards run the code rather than scan its text.
- Row 7 (drop an optional element): S1–S3 remove the label from each place.
- Row 8 (literal compared with a literal): the stack-label tests render the real layout, the real login page and the real `app.vue`. The first test does compare the constant with a literal, but S1–S3 show that the three render tests fail on their own.
- Row 9 (two-part claim) does not apply.
- Row 10 (build output joining the population) does not apply: Vitest's include is `app/**/*.spec.ts` and `server/**/*.spec.ts`, and nothing is compiled into `app/`.

### Not guarded by a substitution arm, with reasons

These call sites already passed a currency before this change, and their call expressions were not edited:
- the four credit `formatMoney` calls;
- `orders/index.vue:164`;
- `orders/[id].vue:185`;
- `place.vue:96` and `place.vue:355`.

Their exponent handling is covered by A1/A5 through `formatMoney` itself.

One new call site in `place.vue` is not armed: the line-discount `:placeholder` (398). It only affects the input's display — there is no `stepMismatch`-shaped consequence for a placeholder.

**Correction (Fix round 1):** the line-discount `:step` (397) was originally listed here as unarmed alongside the placeholder, on the claim that both "only affect the input's display". That claim was wrong for `:step`: on a `type="number"` input with no `novalidate` on its `<form>` (`place.vue:263`), `step` feeds native constraint validation (`validity.stepMismatch`), so a fixed `step="0.01"` would silently block submission of a valid BHD line discount such as `0.005`. This was caught in review (id 97, D1) and is now armed as M5 below.

## Counts (before → after, same commands)

| Command (in #7) | Before | After | Reconciliation |
|---|---|---|---|
| `apps/web: pnpm test` | 15 files / 74 tests, exit 0 | **18 files / 129 tests**, exit 0 | +3 files (`money.spec.ts`, `currency-exponent.spec.ts`, `stack-label.spec.ts`); +55 tests = 49 + 2 + 4; 74 + 55 = 129 |
| `apps/web: pnpm lint` | exit 0 | exit 0 (after fixing one `no-useless-assignment` I introduced) | — |
| `apps/web: pnpm typecheck` | exit 0 | exit 0 | — |
| `packages/contracts: pnpm test` | 5 files / 22 tests, exit 0 | 5 / 22, exit 0 | nothing in `packages/` changed |
| `packages/contracts: pnpm check` | not run before | `contracts:check OK` | — |

I did not run Playwright e2e (`test:e2e`). It needs the live stack, and the brief's verification list did not include it.

## Ported-idiom ledger

- **Stack label:** no row owed, because nothing was ported. This is new work in both repositories: `grep -rn "NestJS / Nuxt" app server` in #7 returned no hits before the change.
- **SA-5:** this is #7 aligning with #8, not a port from #7 to #8. Before this change, #7 got a correct 2-decimal result from `apps/web/app/lib/money.ts:10-11` (`/ 100`). That was correct only because the seeded currencies are EUR, GBP and USD. It now gets the exponent from `Intl`'s ISO 4217 data, and `money.spec.ts` guards it (A1–A5x).

## Surprises

- The brief counted 21 call-site lines; the grep returns 22 (see the table).
- The brief's claim that digit-wise parsing is needed held up more strongly than its own example showed. The "classic" traps (`0.29`, `19.99`, `1.005`) only defeat truncation. `Math.round` after multiplying gets them right. It fails only for large amounts, and before this change no test anywhere in #7 could tell the two approaches apart (A4b's first run).
- The amount inputs' hard-coded `step="0.01"` was a fourth place that assumed an exponent of 2, and the brief's list did not name it. I fixed it in the same pages.
- In `@nuxt/test-utils`, `useHead` in `app.vue` does update `document.title` under `renderSuspended(App)`, so the tab title can be tested without a browser.

## Fix round 1 (id 97, review defect D1)

Review rejected id 97 for one defect: `place.vue:397`'s line-discount `:step` was unguarded, and the record's stated reason — that `:step` "only affects display" — was wrong. `step` drives HTML5 native constraint validation (`validity.stepMismatch`) on a `type="number"` input; the place-order `<form>` has no `novalidate`, so a fixed `step="0.01"` would block submitting a valid BHD line discount such as `0.005`.

### Change made

`apps/web/app/pages/currency-exponent.spec.ts`, BHD case: added `expect(screen.getByTestId('line-discount-input').getAttribute('step')).toBe('0.001')`, immediately after the existing unit-price `step` assertion. The `line-discount-input` test id already existed (`place.vue:399`), so no template change was needed. The JPY case renders `BillingPage`, which has no line-discount input, so no case was added there.

### Arm (M5)

Backup taken with `cp` to a scratch path. Mutated `place.vue:397` from `:step="currencyInputStep(form.currency)"` to `step="0.01"` (line 383, the unit-price step, left untouched — confirmed by re-reading both lines after the edit).

Ran `npx vitest run app/pages/currency-exponent.spec.ts`:

```
FAIL  app/pages/currency-exponent.spec.ts > orders/place.vue — a 3-exponent order currency (BHD) > parses the unit price and line discount with the form's own currency: "1.005" BHD is 1005, "0.5" BHD is 500
AssertionError: expected '0.01' to be '0.001' // Object.is equality

Expected: "0.001"
Received: "0.01"

 ❯ app/pages/currency-exponent.spec.ts:99:76

Test Files  1 failed (1)
     Tests  1 failed | 1 passed (2)
```

Restored with `cp` from the backup; `cmp` reported identical (`RESTORED`); `touch`ed the file; re-read line 397 to confirm `:step="currencyInputStep(form.currency)"` was back.

Re-ran `npx vitest run app/pages/currency-exponent.spec.ts`: `Test Files 1 passed (1)` / `Tests 2 passed (2)`.

| # | Mutation | Named test | Result (verbatim) |
|---|---|---|---|
| M5 | `place.vue:397` `:step` → fixed `step="0.01"` | `currency-exponent.spec.ts` (BHD case) | 1 failed: `expected '0.01' to be '0.001'` (line 99, the new assertion) |

### Full-suite re-run

`pnpm test` (from `apps/web`): **18 files / 129 tests passed**, exit 0. Reconciles exactly against the pre-fix-round baseline of 18 files / 129 tests — this round added one assertion inside an existing `it`, not a new test case, so the count is unchanged.

`pnpm lint`: exit 0, no output.

`pnpm typecheck` (`nuxi typecheck`): exit 0.

### Record correction

The two lines claiming `:step` "only affects display" were corrected above: the "Not guarded by a substitution arm, with reasons" line was edited in place, and a new correction line was added to "What was built" (line 15). Both are marked there rather than only here — see the inline "Correction (Fix round 1)" notes. `:placeholder` (`place.vue:398`) remains genuinely unguarded and its reason stands: it is cosmetic, with no constraint-validation consequence.

**Result: PASS.**
