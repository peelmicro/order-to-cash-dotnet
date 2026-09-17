# Review — backlog id 97 (SA-5 + #7 money alignment) and id 99 (stack label in both web apps)

Reviewer, 2026-09-17. Brief: `scratchpad/brief_review_97_99.md`. Records reviewed: `progress/impl_web_apps_name_their_stack.md` (#8 half of 99) and `progress/impl_sa5_and_stack_label_in_nestjs.md` (#7 halves of 97 and 99). I read the on-disk `CLAUDE.md` before enforcing any rule. I cite the arming protocol, the rule that a countable claim needs an arm, and the defeat list from that copy.

## Verdicts

- **Id 97 — REJECTED.** One defect: the line-discount input's `step` is not guarded. Details under D1. The status stays `in_progress`, which it already was.
- **Id 99 — APPROVED.** Status set to `done`. The effort record is appended to `progress/history.md`.

## What I ran, and what I did not

I did not re-run either full suite that the leader had already verified (#8 Vitest 21/278, #7 `pnpm test` 18/129). I made one exception: after my own restores in #7, I ran `pnpm test` once to confirm it was green again (**18 files / 129 tests passed**). In #8, my confirming run covered only the two files my claims depend on: `stack-label.test.tsx` and `error-text-sweep.test.tsx`, **2 files / 67 tests passed**. Every mutation below was run against one named spec, restored with `cp` + `touch`, and checked with `cmp` (`RESTORED` / `ALL-RESTORED` printed). Vitest transforms from source on each run, so there is no stale binary to worry about.

## Id 97 — #7 money alignment

### Reading
`apps/web/app/lib/money.ts` in #7 reads the exponent from `currencyExponent(currency)`, which uses `Intl…resolvedOptions().maximumFractionDigits` and falls back to 2 for codes that `Intl` rejects. `formatMoney`, `decimalStringToMinorUnits` and `minorUnitsToDecimalString` each take the exponent from that function. Parsing works on the digit strings (`Number(whole + fraction.padEnd(exponent))`), and the result must be a safe integer.

### Call sites, enumerated by content
Command, run from #7 `apps/web`: `grep -rn "decimalStringToMinorUnits\|minorUnitsToDecimalString\|formatMoney\|currencyInputStep\|step=" app server --exclude-dir=node_modules | grep -v "^app/lib/money"`. The `grep -v` excludes by path prefix: each output line begins with its path, so no content line can match it. Complete output, classified:

| Hit | Class |
|---|---|
| `orders/index.vue:10` | import |
| `orders/index.vue:164` `formatMoney(…, order.currency)` | call, passes currency |
| `orders/place.vue:11` | import |
| `orders/place.vue:51` | comment |
| `orders/place.vue:96` `formatMoney(selected.price, selected.currency)` | call, passes currency |
| `orders/place.vue:156,157,216,217` `decimalStringToMinorUnits(…, form.currency)` | 4 calls, pass currency |
| `orders/place.vue:355` `formatMoney(product.price, product.currency)` | call, passes currency |
| `orders/place.vue:383` `:step="currencyInputStep(form.currency)"` (unit price) | step follows the currency; guarded (M4) |
| `orders/place.vue:397` `:step="currencyInputStep(form.currency)"` (line discount) | step follows the currency; **not guarded (D1)** |
| `orders/place.vue:398` `:placeholder="minorUnitsToDecimalString(0, form.currency)"` | call, passes currency; cosmetic, unguarded (advisory A1) |
| `orders/place.vue:420` `formatMoney(runningTotal, form.currency)` | call, passes currency |
| `orders/[id].vue:10` | import |
| `orders/[id].vue:185` `formatMoney(…, data.detail.currency)` | call, passes currency |
| `billing/index.vue:13` | import |
| `billing/index.vue:103,128` `…(…, invoice.currency)` | 2 calls, pass currency |
| `billing/index.vue:227,230,233,236` `formatMoney(credit.*, credit.currency)` | 4 calls, pass currency |
| `billing/index.vue:335` `formatMoney(invoice.totalAmount, invoice.currency)` | call, passes currency |
| `billing/index.vue:378` | comment |
| `billing/index.vue:386` `:step="currencyInputStep(invoice.currency)"` | step follows the currency; guarded (M3) |
| `stock/index.vue:230` `step="1"` | the replenish **units** input, not money; not applicable |

**The 22 lines against the brief's 21.** The implementer's table classifies the 22 pre-change lines as 4 imports, 2 comments and 16 calls. My post-change enumeration contains all 22 of those lines at their new or unchanged positions, and I classified each line again above. The extra line is `place.vue:51`, a comment. Every call site passes the currency. `grep -rn "0\.01\|/ *100\b\|toFixed\|\* *100\b" app` finds no remaining 2-decimal assumption outside comments and spec premises.

### Arms (mine)

| # | Mutation | Spec run | Result (verbatim) |
|---|---|---|---|
| M1 | `currencyExponent` returns `2` at entry | `app/lib/money.spec.ts` | 20 failed, including `JPY has exponent 0` → `expected 2 to be +0`; `BHD has exponent 3` → `expected 2 to be 3`; `formatMoney … > JPY (0): 24999 minor units is 24,999 yen, not 249.99` → `expected 'JP¥250' to be 'JP¥24,999'`; `… > BHD (3): 24999 minor units is 24.999` → `expected 'BHD 249.990' to be 'BHD 24.999'`; `"24999" in JPY is 24999 minor units`; `"1.005" in BHD is 1005 minor units` → `expected undefined to be 1005`. The test names carry the currency, as the brief requires. |
| M2 | parse via `Math.round(parseFloat(trimmed) * 10 ** exponent)` | `app/lib/money.spec.ts` | 1 failed / 48 passed: `is exact where even Math.round(parseFloat(x) * 100) is not: large safe-integer amounts` → `expected 8271851421238138 to be 8271851421238137` |
| M3 | billing `:step` → fixed `step="0.01"` | `app/pages/currency-exponent.spec.ts` | 1 failed: `renders, pre-fills and submits the amount in whole yen…` → `expected '0.01' to be '1'` |
| M4 | place unit-price `:step` → fixed `step="0.01"` | same | 1 failed: `parses the unit price and line discount with the form's own currency…` → `expected '0.01' to be '0.001'` |
| **M5** | place **line-discount** `:step` (line 397) → fixed `step="0.01"` | same | **Survived: `Tests 2 passed (2)`** |

Enumeration showing that no other spec could kill M5: `grep -rln "step" app --include=*.spec.ts` returns only `app/pages/currency-exponent.spec.ts` and `app/lib/money.spec.ts`. `money.spec.ts` tests `currencyInputStep` directly and never reads a page. `currency-exponent.spec.ts` reads `getAttribute('step')` only from `unit-price-input` and `payment-amount-input`.

### Defects

**D1 — the line-discount `step` is not guarded, and the record gives a false reason for leaving it.** File: #7 `apps/web/app/pages/orders/place.vue:397`. The record (`impl_sa5_and_stack_label_in_nestjs.md`, section "Not guarded by a substitution arm") says that `:step` and `:placeholder` "only affect the input's display". That is true of the placeholder but not of `step`. On `type="number"`, `step` feeds native constraint validation, and the form (`place.vue:263`, `<form … @submit.prevent="submit">`) has no `novalidate`. I measured this with happy-dom from #7's own `node_modules`: `<input type=number min=0 step=0.01 value=0.005>` → `validity.stepMismatch true`, `checkValidity() false`; the same input with `step=0.001` → `false` / `true`. In a browser, an invalid field blocks the submit. So if line 397 went back to `step="0.01"`, a BHD line discount such as `0.005` could not be submitted. That browser-blocking step is HTML specification behaviour; I did not measure it in a real browser here. This is exactly the regression the brief asked me to check ("a test would fail if it went back to fixed"). For this site, M5 shows no test fails. The implementer armed the other two step sites (C3, C9) and left this one unarmed on a wrong premise.

### What must change before re-review (id 97)
1. In `app/pages/currency-exponent.spec.ts`, in the BHD case, assert `screen.getByTestId('line-discount-input').getAttribute('step')` is `'0.001'`. Then arm it with M5 (line 397 → `step="0.01"`) and record the failure verbatim.
2. Correct the record's justification: `step` constrains submission and is not display-only. Keep the placeholder as the only unguarded site, with its real reason.
3. Re-run `app/pages/currency-exponent.spec.ts`, then `pnpm test`, and reconcile the count (129 plus any case you add).

No other change is required. The rest of id 97 checks out: bullets 1–3 (the SA-5 text, byte-identity with md5 `fa261d4cf12aa3b373cb777aaa79b2e1`, and the regeneration/drift check), bullet 4 (#8's guard is `apps/web/src/lib/money.test.ts:21-33,79`, with JPY 0, BHD/KWD 3 and the `toDecimalString` JPY case, armed in phase 16 as A13/A14 in `progress/impl_web_app.md`), and M1/M2 on bullet 5.

### Advisory (does not block)
- **A1.** `place.vue:398` `:placeholder` is unguarded. It really is cosmetic.
- **A2.** `currency-exponent.spec.ts`'s billing invoice-total check, `not.toContain('249.99')`, is a negative assertion. C4 shows that it kills the `'EUR'` substitution, but an exact expected string would name the claim more clearly.

## Id 99 — stack label

| Bullet | #8 | #7 |
|---|---|---|
| 1/2 Header, login page, tab title | `(app)/layout.tsx:22` `<StackLabel />`; `login/page.tsx:11`; `layout.tsx:8` `title: APP_TITLE` | `layouts/default.vue:24-25`; `pages/login.vue:56-57`; `app.vue:5` `useHead({ title: DOCUMENT_TITLE })` |
| 3 One definition | `grep -rn "StackLabel\|STACK_LABEL\|APP_TITLE" src` (non-test): the only literal is `src/lib/stack-label.ts:10`; the component and both pages import it. This is also guarded by the content scan `has one definition…` | `grep -rn "NestJS\|#7 ·" app server`: the only literal is `app/lib/stack-label.ts:10` (line 2 is a doc comment); the other hits are in the spec |
| 4 Armed test | `apps/web/src/app/stack-label.test.tsx` | `apps/web/app/stack-label.spec.ts` |
| 5 (#8) Error sweep | See below | n/a |

### Arms (mine)

| # | App | Mutation | Result (verbatim) |
|---|---|---|---|
| L1 | #8 | `STACK_LABEL` → `'#8 · .NET / React'` | 9 failed: `is the reviewed text` → `src/lib/stack-label.ts: the label is not the reviewed one: expected '#8 · .NET / React' to be '#8 · .NET / Next.js'`; the title case; all 5 signed-in header cases (`signed-in header of /billing ((app)/billing/page.tsx): the stack label is missing: expected [ 'header: #8 · .NET / React' ] to include 'header: #8 · .NET / Next.js'`); login; one-definition |
| L2 | #8 | delete `<StackLabel />` from `(app)/layout.tsx:22` | 5 failed: `signed-in header of /billing ((app)/billing/page.tsx): the stack label is missing: expected [] to include 'header: #8 · .NET / Next.js'`, and the same for `/orders/[id]`, `/orders`, `/orders/place`, `/stock` |
| L3 | #7 | `STACK_LABEL` → `'#7 · NestJS / Vue'` | 4 failed: `is the literal #7 label` → `expected '#7 · NestJS / Vue' to be '#7 · NestJS / Nuxt'`; header and login → `toHaveTextContent()`; title → `expected 'Order-To-Cash · #7 · NestJS / Vue' to be 'Order-To-Cash · #7 · NestJS / Nuxt'` |
| L4 | #7 | delete `{{ STACK_LABEL }}` from `layouts/default.vue:25` | 1 failed / 3 passed: `is shown in the header of a signed-in page` → `expect(element).toHaveTextContent()` … `Received:` (empty) |

L4's message is the jest-dom matcher's own diff. The test name carries the claim; the message alone does not. This is acceptable because the named case is the only failure.

**Bullet 5 (#8).** In `src/app/error-text-sweep.test.tsx`, `unlabelled()` (lines 257–265) fails any text that a failing run adds and that is neither the problem's own words nor in `LABELS`. The label is static, so it appears in the all-success baseline and in every failing run, and so it is never added text. The cases that cover this are the per-page `shows each load request's failure as that problem's own %s` (lines 370–378), the per-action sweeps (line 411), and `every reviewed label is produced by some failing run…` (line 553), which would fail if someone added the label to `LABELS` without need. My run of the file passed (it is part of the 67 above). No file that renders an error was edited: the #8 record lists only the three layout/page files and the new files.

**Ledger (id 99).** Neither record owes a ledger row, and each cites its evidence. #8 ran `git grep … HEAD -- apps/web` in #7, which found nothing to port. #7 ran `grep -rn "NestJS / Nuxt"`, which found nothing before the change. I accept both: this is new work on both sides.

## Spec amendment bookkeeping (SA-5)
- #8 `README.md:39` has the registry row. ✔
- #8 `progress/history.md:2920` and #7 `progress/history.md:1521` each have an SA-5 history section. ✔
- #7 `README.md` has **no SA registry at all**: `grep -n -i "amendment\|SA-" README.md` returns nothing, so SA-1 to SA-4 are absent too. SA-5's absence matches #7's existing practice and is not a defect of this work. I note it for the leader: #8's README is the only registry.
- `cmp` of every `specs/shared/` file against #7: all are identical except `test-matrix.md`, whose Status column is expected to differ under C7.

## CHECKPOINTS walked (applicable boxes)
- C1 `./init.sh` exits 0 — [x] (run this session; `environment and state are coherent`)
- C2 at most one `in_progress` (id 97) — [x]; every status valid — [x] (init.sh); every `done` has passing tests (id 99) — [x]
- C3 — [x] not applicable: web-only and prose-only change; no `src/` or `Domain/` touched
- C4 No Jest — [x] (the new specs import from `vitest`); `./quality.sh` — [x] for `QUALITY_ONLY=web` per the #8 record; not re-run by me
- C5 history entry with effort record for id 99 — [x]; `feature_list.json` true state — [x] (99 `done`, 97 left `in_progress`); Claude did not commit — [x]
- C6 — [x] not applicable (`sdd: false`)
- C7 `specs/shared/` identical except `test-matrix.md` — [x] (cmp); deviation recorded in both repositories — [x] (history in both; registry in #8 only, and #7 has none); effort record honest — [x]

## Bookkeeping performed
- `feature_list.json`: I changed one line, 1487 (`id 99`), `"pending"` → `"done"`. `diff` against my pre-edit copy shows only that line. `node -e JSON.parse` → `parses`. Id 97 is untouched.
- `progress/history.md`: I appended the id 99 section with its effort record. Timings come from the subagent transcripts: #8 half 02:50:36Z→03:00:15Z, #7 half 02:50:29Z→03:00:52Z, review from 03:03:48Z. No id 97 entry is written, because 97 is not closed.
- No git command that writes, in either repository. No source edited. All mutations were restored and verified with `cmp`.

## Re-review — id 97, fix round 1 (2026-09-17)

**Verdict: APPROVED.** Id 97 is set to `done`.

1. **Re-armed M5 myself.** Backup taken, then `place.vue:397` changed to `step="0.01"`. I read back lines 383 and 397: line 383 still reads `:step="currencyInputStep(form.currency)"`. Ran `pnpm exec vitest run app/pages/currency-exponent.spec.ts`: **1 failed / 1 passed**. Failing test: `orders/place.vue — a 3-exponent order currency (BHD) > parses the unit price and line discount with the form's own currency…`, message `AssertionError: expected '0.01' to be '0.001'`, at `currency-exponent.spec.ts:99:76`, which is the new assertion. Restored with `cp`, ran `touch`, and `cmp` printed `RESTORED`. Line 397 reads `:step="currencyInputStep(form.currency)"` again. The confirming run gave **2 passed (2)**.
2. **Retired wording, enumerated.** Command: `grep -rn -i "only affect\|display-only\|display only\|affect the input's display"` over the #7 record and #7 `apps/web/app` (excluding `node_modules`). The output has 6 hits:
   - record `:14`: about the `formatMoney` division, which really is display-only. Correct as written.
   - record `:114`: the `:placeholder`, which really is cosmetic. Correct.
   - record `:116`, `:144`, `:187`: each quotes the retired claim in order to correct it. Correct.
   - #7 `money.ts:42`: the `formatMoney` doc comment. Correct.

   No hit still asserts that `step` is display-only. The new line `:15` states the corrected premise.
3. **Nothing else changed.** `find` across #7 (excluding `node_modules`, `.nuxt`, `coverage`, `test-results` and `.git`) for files newer than my first review lists only `apps/web/app/pages/currency-exponent.spec.ts` and `apps/web/app/pages/orders/place.vue`. `place.vue` is byte-identical (`cmp`) to my pre-fix-round backup, so its newer mtime comes from the implementer's restore `touch`, not from a content change. The spec change is the one assertion at line 99. `money.ts`, `billing/index.vue`, `stack-label.ts` and `layouts/default.vue` are all `cmp`-identical to my first-round backups. `git status` in #7 shows the same file set as before. In #8, no file under `apps/web/src` is newer than my first review.

I accept the implementer's full-suite figure of 18 files / 129 tests: an assertion added inside an existing `it` leaves the count unchanged, and I did not re-run the full suite. I re-ran only the spec that holds the claim under test.

**Advisory.** Record `:187` says "the two lines claiming `:step` only affects display (in *What was built* and …) were corrected in place". The *What was built* section never held that claim about `step`: line 14 is about `formatMoney` and was left as it was, correctly. The correction there was an added line (`:15`), not an edited one. This does not affect the verdict.

**Bookkeeping.** In `feature_list.json` I changed one line, 1455, from `"in_progress"` to `"done"`. A `diff` against my pre-edit copy shows only that line. The file parses, ids 97 and 99 are both `done`, and no feature is `in_progress`. I appended id 97's effort record to `progress/history.md`. I ran no git command that writes and edited no source.
