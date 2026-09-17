# Review — backlog ids 100 (`timeline_money_reads_as_minor_units`) and 101 (`stock_page_repeats_the_product_code`), #8 and #7

Reviewer session 2026-09-17, 05:21:51Z → about 05:40Z. Brief: `brief_review_100_101.md`. I checked `CLAUDE.md` on disk (lines 109, 243-247, 318-335) before applying any rule from it.

## Verdicts

- **Id 100: REJECTED.** Leave it at `in_review`/`in_progress`. The defects are listed below.
- **Id 101: APPROVED.** I tried to set the status to `done` myself, but the permission classifier denied the edit to `feature_list.json` ("Modify Shared Resources"). The file is unchanged: line 1519 still reads `"status": "in_review"`. **The leader must make that single-line transition** and append the effort record in the last section of this file to `progress/history.md`.

## What I ran, and what I did not

- I did not re-run any full suite. `./quality.sh` was not run, per the brief.
- **#8 targeted tests, all green, counts match the record:**
  - SharedKernel.UnitTests 79/79
  - Seed.UnitTests 47/47
  - Projector.UnitTests 120/120
  - Notifications.UnitTests 113/113
  - Architecture.Tests 50/50
  - Projector.IntegrationTests `SeededOracleParityTests` 6/6 (Docker)
- **#7 package suites, all green, counts match the record:**
  - `@otc/shared-kernel` 84
  - `projector` 184
  - `notifications` 121
  - `seed` 149
- **Web, all green:**
  - #8 `stock-view.test.tsx` 14/14
  - #8 `error-text-sweep.test.tsx` 56/56
  - #7 `app/pages/stock/index.spec.ts` 13/13
- **Scope:** `specs/shared/*` and `CLAUDE.md` in both repositories have mtimes earlier than both implementer sessions (04:47Z dispatch). The #7 `git status` lists only the files the id 100 and id 101 records name, plus the web-port and SA-5 files from earlier work this session.

## Id 100 — probes

**The #8 algorithm is correct.**
- `MoneyText.cs` produces whole strings for EUR, JPY and BHD, for negative values, for zero, and for `long.MinValue`/`long.MaxValue`. It uses string slicing only, with no floating point and no `decimal`.
- `SharedKernel.csproj` still has zero `PackageReference` entries. The only usings are `System.Globalization` and `System.Text`. Architecture.Tests passes 50/50.

**Arms I ran myself.** Each followed the same protocol: `cp` a backup, mutate, run, restore with `cp`, `cmp`, then re-run green. .NET builds used `--no-incremental`. For #7, the shared-kernel `dist/` was rebuilt after each mutate and after each restore, because apps resolve `@otc/shared-kernel` through `dist/index.js`.

| # | Repo | Mutation | Result |
|---|---|---|---|
| R1 | #8 | `Projector.Domain.MoneyFormat.Of` → `$"{minorUnits} {currency}"` (the projector disagrees with the seed) | **KILLED.** `SeededOracleParityTests` 6/6 failed, e.g. `events[2].summary: expected Credit hold of 161.30 EUR approved, was Credit hold of 16130 EUR approved`. Restored; 6/6 green. |
| R2 | #8 | `CurrencyExponent` table: `JOD` 3→2 and `ISK` 0→2 | **SURVIVED.** SharedKernel.UnitTests stayed 79/79 green. Restored; 79/79. |
| R3 | #7 | `currencyExponent` forced to `return 2` | **KILLED** in 3 of 4 packages. Examples: shared-kernel `expected 2 to be +0`, `expected '50.00 JPY' to be '5 000 JPY'`; projector `expected 'Credit hold of 50.00 JPY approved' to be 'Credit hold of 5 000 JPY approved'`; notifications 2 failed. Seed stayed 149 green; that is expected, because the seed has only EUR and GBP. Restored and rebuilt; 84/184/121 green. |
| R4 | #7 | `formatMoney` split replaced by `(Math.abs(minorUnits) / 10 ** exponent).toFixed(exponent)` (floating-point division) | **SURVIVED.** All four packages stayed green: 84 + 184 + 121 + 149 = 538 tests. Restored and rebuilt; green. |
| R5 | #7 | seed `credit.approved.v1` summary reverted to `${totalAmount} ${currency.code}` | **KILLED.** 2 failed, `expected 'Credit hold of 16130 EUR approved' to be 'Credit hold of 161.30 EUR approved'`. Restored; 149 green. |
| R6 | #7 | seed uses its own formatter, `(totalAmount / 100).toFixed(2)`, ungrouped | **SURVIVED**, 149 green. The seeded amounts are all EUR/GBP below 1 000, so this formatter agrees with `formatMoney` on every seeded value. #8 would pass the same variant. I record this as an advisory, not a defect: the seed/projector agreement holds because both call the same function, and #7 has no PR44-style oracle. |

**How the #7 exponent source compares with #8's table.** I ran this probe on Node v24.19.0:

```
new Intl.NumberFormat("en",{style:"currency",currency:c}).resolvedOptions().maximumFractionDigits
BIF=0 CLP=0 DJF=0 GNF=0 ISK=0 JPY=0 KMF=0 KRW=0 PYG=0 RWF=0 UGX=0 VND=0 VUV=0 XAF=0 XOF=0 XPF=0 BHD=3 IQD=0 JOD=3 KWD=3 LYD=3 OMR=3 TND=3 CLF=4 UYW=4 UYI=0 EUR=2 USD=2 GBP=2 AFN=0 ALL=0 IRR=0 KPW=0 LAK=0 LBP=0 MGA=0 MMK=0 RSD=2 SOS=0 SYP=0 YER=0 HUF=0 TWD=2 COP=0 IDR=0 MRU=2 XAU=2 XXX=2
```

**Checking #8's table against ISO 4217:**
- Every non-2 code it lists is correct: the zero-, three- and four-decimal sets.
- It **misses `UYI`**, which is exponent 0 (a fund code).
- No commonly traded currency is missing.

**The two repositories disagree** on `IQD`, `UYI`, and every code where ICU/CLDR's display digits differ from ISO 4217. On this runtime those are: `AFN ALL IRR KPW LAK LBP MGA MMK SOS SYP YER HUF COP IDR`. ISO 4217 gives them 2; #7 now renders them with 0.

## Id 100 — defects

**D1 (blocking): #7's exponent is not the ISO 4217 exponent.**
- Location: `order-to-cash-nestjs/packages/shared-kernel/src/domain/currency-exponent.ts:21`.
- It reads `Intl`'s `maximumFractionDigits`. That value is CLDR's *display* digits, not ISO 4217's minor unit. This is the same objection the #8 ledger raises against `NumberFormatInfo.CurrencyDecimalDigits`.
- Acceptance bullet 1 requires the ISO 4217 exponent, and bullet 3 requires the same change in #7.
- As shipped, one stored amount renders differently on the two stacks:
  - `12345 HUF`: #8 `"123.45 HUF"`, #7 `"12 345 HUF"`.
  - `12345 IQD`: #8 `"12.345 IQD"`, #7 `"12 345 IQD"`.
- That is a 100× or 1000× misread, the defect this entry exists to remove. HUF, IDR and COP are plausible B2B currencies.
- PR16 in #8's `specs/projector_read_model/requirements.md` claims "#7's `formatMinorUnits` byte for byte". That claim is false while D1 stands.
- Ledger row 1 of the impl record says `Intl` "answers correctly". That is true only for the two codes probed (JPY, BHD), so the row's history half misdescribes #7.

**D2 (blocking): #7's floating-point arm survives.**
- Acceptance bullet 4 requires arming by "replacing the integer split with a floating-point division". The record declined to arm #7 because it "is the same algorithm". R4 shows that argument is not evidence: the mutation leaves 538/538 green.
- #7's large-value case (`Number.MAX_SAFE_INTEGER`, EUR) happens to render correctly through `toFixed`.
- Values that do kill the mutation, computed on this Node:
  - `formatMoney(9007199254740990, 'EUR')`: correct `'90 071 992 547 409.90 EUR'`, float gives `…409.91`.
  - `formatMoney(9007199254740991, 'BHD')`: correct `'9 007 199 254 740.991 BHD'`, float gives `…740.990`.
- Required: add such a case in `money-text.spec.ts` (and in the projector spec if it keeps its own large-value case), and arm it. The arm table must record the failure message verbatim.

**D3 (must fix): 20 of the 25 non-default rows in #8's exponent table are unguarded.**
- Location: `src/SharedKernel/CurrencyExponent.cs:56-86`. R2 survived.
- The table is a countable claim, so it needs a whole-table literal guard: every non-2 row plus `UYI`.
- #7's table, once D1 is fixed, should carry the identical literal set, so a divergence between the two repositories fails in each.

**D4 (record correction): a false negative claim.**
- `progress/impl_timeline_money_reads_as_minor_units.md:123` says #7 "has no separate per-feature EARS requirements document".
- It does: `order-to-cash-nestjs/specs/projector_read_model/requirements.md`, with PR16 at `:109`.
- #7's PR16 wording ("never converted") stays true, so no edit is owed there. The record must still state that it checked the file and why no edit is owed.
- Related advisory: #7 `specs/projector_read_model/tasks.md:39` (C5, "no `.` floating artefact") is now narrower than the spec it describes. `summaries.spec.ts` was scoped accordingly.

**D5 (must fix): a wrong citation in a source comment's ledger text.**
- Location: `src/SharedKernel/CurrencyExponent.cs:17`.
- It cites `apps/projector/src/domain/currency-exponent.ts` in #7. That file does not exist (`ls`: "No such file or directory").
- The port is at `packages/shared-kernel/src/domain/currency-exponent.ts`. After D1, this comment must also describe #7's source correctly.

**D6 (routing, not blocking this entry): a class member was misclassified.**
- The record classifies every `DomainError` amount message as "developer diagnostic text". At least one reaches a human:
  1. `InvoicePaymentAmountMismatchError` ("Payment amount {received} does not equal the invoice's totalAmount {expected}.", `src/Billing/Domain/Errors/InvoicePaymentAmountMismatchError.cs:7`)
  2. becomes the RPC error message at `src/Billing/Presentation/Rpc/BillingErrorMapper.cs:95-98`,
  3. then problem+json `detail` at `src/Gateway/Presentation/Problem/ProblemJsonMiddleware.cs:94`,
  4. which the web shows (`apps/web/src/lib/problem.ts:23`).
- #7 has the twin at `apps/billing/src/domain/invoice-errors.ts:71`.
- This is outside id 100's acceptance, which names the projector, the seed and notifications. **It must become a numbered backlog entry**, filed by the leader: *"problem-details `detail` renders money in raw minor units (INVOICE_PAYMENT_AMOUNT_MISMATCH and any sibling reaching the Gateway), both repositories"*. The record's classification line must be corrected to say so.

**D7 (routing): both web apps take the exponent from `Intl`, so the D1 divergence also exists inside each repository.**
- Locations: #8 `apps/web/src/lib/money.ts:30`; #7 `apps/web/app/lib/money.ts:22`. This predates id 100 (SA-5 / id 97).
- Consequences:
  - #8's web and #8's projector now disagree for HUF, IQD and the other codes listed above.
  - #8's place-order form would parse a HUF amount with exponent 0.
- The root cause is not `specs/shared/`. SA-5 correctly says ISO 4217. The fault is in the implementation.
- **The leader must file a numbered backlog entry**: *"web `currencyExponent` uses CLDR display digits via Intl, not ISO 4217 — diverges from the backend table for IQD/HUF/IDR/COP/…, both repositories"*. It should share the D3 literal table.

**Advisory:** the record says n8n has "three workflow files". There are four (`1-order-generator`, `2-payment-robot`, `3-stock-replenishment`, `4-burst`). I read all four money mentions. Every one is structured request/response data, with no human-facing text, so the conclusion holds.

**Enumeration re-run.** I re-ran the recorded #8 command. Its output matches the record's table line for line, including `Money.cs:85`, the Billing and Orders `DomainError`s, `Summaries.cs:50,54`, `SagaFixtures.cs:289,469`, and `PaymentReceivedTemplate.cs:23,36`. Every `FormatMoney`/`MoneyText`/`MoneyFormat.Of` call site in `src/` goes through `MoneyText.Format`.

## Id 100 — what must change before re-review

1. **D1.** Replace #7's `Intl` exponent with an ISO 4217 literal table identical to #8's, with `UYI` = 0 added to both.
   - Update the #7 docs/comments and ledger row 1: #7 previously relied on CLDR display digits, which are not ISO 4217.
   - Re-run and re-state the cross-repository comparison.
2. **D2.** Add a large-value case that kills the float mutation in #7, and arm it.
3. **D3.** Add a whole-table literal guard in both repositories and arm it with a single-row corruption.
4. **D4, D5, D6.** Correct the record and the comment as described.
5. **D6, D7.** The leader files the two backlog entries.

## Id 101 — probes and verdict

**Traceability to the five acceptance bullets:**

| Bullet | #8 test (`apps/web/src/features/stock/stock-view.test.tsx`) | #7 test (`apps/web/app/pages/stock/index.spec.ts`) |
|---|---|---|
| name from catalog, code beside it | "the name comes from the catalog…" | same |
| code once, never twice | "the code appears only once…" (counts occurrences inside `stock-product`) | same |
| `productName` wins | "productName … takes precedence" | same |
| catalog failure keeps the table and the sweep | "a catalog failure does not hide the stock table…" (asserts `stock-row` present, `stock-products-error` present); `error-text-sweep.test.tsx` `EXPECTED_LOAD['/stock']` now includes `GET /api/catalog/products` | "a catalog failure does not hide the stock table…" |
| same change in #7, armed | implementer arms 1–4 | implementer arms 1–4 |

**My probes:**

| # | Mutation | Result |
|---|---|---|
| P1 | #8 `stock-view.tsx:101` `stock.isError` → `stock.isError \|\| products.isError` (catalog failure hides the table) | **KILLED.** `Unable to find an element by: [data-testid="stock-row"]`, 1 failed / 13. Restored (`cmp` OK); 14/14. |
| P2 | #8 `stock-view.tsx:84` banner replaced with `{null}` (failure silently swallowed, defeat-list row 12) | **KILLED by the sweep.** `"/stock \| load \| GET /api/catalog/products \| 503 detail \| … shown instead: (no new text at all)"`, the same for the title variant, plus `LABELS no failing run produced — stale: [ 'Product names unavailable:' ]`. 3 failed / 56. Restored; 56/56. |
| P3 | #7 `index.vue:168` `v-if="isError"` → `v-if="isError \|\| productsFailed"` | **KILLED.** `Unable to find an element by: [data-testid="stock-row"]`, 1 failed / 13. Restored (`cmp` OK); 13/13. |

**Company scoping.** Products are not company-scoped in either repository, as the implementer concluded:
- #8: `src/Orders/Infrastructure/Persistence/Configurations/ProductConfiguration.cs:18` has `HasIndex(p => p.Code).IsUnique()`.
- #7: `apps/orders/src/infrastructure/persistence/schema/products.schema.ts:9` has `code … .unique()`.

Two companies cannot hold one code under different names, so a `Map<code, name>` is sound. A disabled product falls back to its code once, as the record discloses.

**Scope.** The implementer touched only the files it lists. `specs/shared/` and `CLAUDE.md` are untouched.

**Advisory.** `progress/impl_stock_page_repeats_the_product_code.md:13` says the status was left at `pending`. The backlog shows `in_review`, set by the leader. This is stale prose, not a defect.

**Checkpoints walked (applicable subset):**
- [x] traceability, every bullet mapped to named tests
- [x] tests real and armed (the implementer's 4 arms plus my 3)
- [x] behavioural sweep extended; defeat-list row 12 probed
- [x] no Jest (Vitest in both)
- [x] no `specs/shared/` change
- [x] #7 parity

**Id 101 effort record, for the leader to append to `progress/history.md`:** 1 implementer session covering both repositories, 04:47:34Z → 05:09:33Z (transcript `agent-aa4ca396f73018f90`). It ran in parallel with id 100's implementer. 1 review, 05:21:51Z → about 05:40Z, shared with id 100. 0 rejections. Wall-clock from dispatch to close: about 53 minutes. Against #7's baseline, there is no #7 counterpart: the defect was present in both repositories and was fixed in both by this entry.

---

# Re-review: ids 100 (fix round 1), 101, 102 and 103. 2026-09-17, about 06:32Z → 06:46Z

I checked `CLAUDE.md` on disk before applying any rule from it.

## Verdicts

| Id | Verdict | Status set |
|---|---|---|
| 100 | **APPROVED** | `done` (line 1503) |
| 101 | **APPROVED** (from round 1) | `done` (line 1519) |
| 102 | **REJECTED** | left `in_review` (see note) |
| 103 | **REJECTED** | left `in_review` (see note) |

**Backlog edits.** Both edits were one line each and neither was refused.
- `diff` against my pre-edit copy shows only lines 1503 and 1519.
- The file parses. Statuses read: `(100, done), (101, done), (102, in_review), (103, in_review)`.
- I did not move 102 and 103 to `in_progress`: two features in `in_progress` at once would break the backlog's at-most-one rule. The leader should choose which one goes first.

## What I ran

I did not run `./quality.sh`. These are the runs I did make:

**#8, all counts match the records:**
- SharedKernel.UnitTests 81
- Billing.UnitTests 270
- Orders.UnitTests 500
- Gateway.UnitTests 248
- Architecture.Tests 50
- `apps/web` full Vitest suite, 22 files / 289 tests. This includes `error-text-sweep.test.tsx` and the parity test.

**#7, all counts match the records:**
- `@otc/shared-kernel` 89
- projector 186
- notifications 121
- seed 149
- billing 159
- orders 542
- gateway 141
- `@otc/web` 148

## Scope

**Files changed since my first review** (`find -newer` on the first review file, 07:34:27 local; build output excluded):
- **#8, id 100:** `src/SharedKernel/CurrencyExponent.cs`, `tests/SharedKernel.UnitTests/CurrencyExponentTests.cs`.
- **#8, id 102:**
  - Billing domain: `src/Billing/Domain/{BuyerCredit,CreditLedgerEntry,Invoice}.cs` and `src/Billing/Domain/Errors/{CreditLimitExceeded,CreditRefusalMismatch,CreditReleaseUnderflow,InvoicePaymentAmountMismatch,NegativeInvoiceTotal}Error.cs`.
  - Orders: `src/Orders/Application/Commands/{PlaceOrderCommandHandler,PlaceOrderErrors}.cs` and `src/Orders/Domain/Errors/OrderTotalMustNotBeNegativeError.cs`.
  - Tests: `tests/Billing.UnitTests/{BillingErrorMapperTests,DomainErrorMoneyTextTests}.cs`, `tests/Gateway.UnitTests/ProblemDetailMoneyTextTests.cs`, `tests/Orders.UnitTests/{OrdersCreateErrorMapperTests,OrderTotalsTests,PlaceOrderCommandHandlerTests}.cs`.
  - `src/Gateway/Presentation/Problem/ProblemJsonMiddleware.cs` has a new mtime only because it was restored after an arm. `git diff` against HEAD is empty.
- **#8, id 103:** `apps/web/src/lib/{currency-exponent.ts,currency-exponent.parity.test.ts,money.ts,money.test.ts}`, `apps/web/src/features/billing/billing-view.test.tsx`, `apps/web/src/test/fixtures/gateway/payment-amount-mismatch-422.json`.
- **#8, other:**
  - Git-ignored artefacts: `apps/web/tsconfig.tsbuildinfo`, `.backlog-snapshot` (written by `init.sh`), and `logs/dev-stack/*` from the live recapture.
  - Records: `progress/{current.md,impl_*.md}`.
  - `feature_list.json`: the leader's transitions to `in_review`.
- **#7, id 100:** `packages/shared-kernel/src/domain/{currency-exponent.ts,currency-exponent.spec.ts,money-text.spec.ts}`, `apps/projector/src/domain/money-format.spec.ts`.
  - `money-text.ts` has a new mtime only because it was restored after an arm. It is byte-identical (`cmp`) to my round-1 backup.
- **#7, id 102:**
  - Billing: `apps/billing/src/domain/{buyer-credit,credit-errors,invoice-errors,invoice}.ts`, `domain-error-money-text.spec.ts`, `apps/billing/src/presentation/rpc-error-mapper.spec.ts`.
  - Orders: `apps/orders/src/domain/{order-errors.ts,domain-error-money-text.spec.ts}`, `apps/orders/src/application/{place-order.errors,place-order.handler,place-order.handler.spec}.ts`, `apps/orders/src/presentation/rpc-error-mapper.spec.ts`.
  - Gateway: `apps/gateway/src/presentation/problem-detail-money-text.spec.ts`.
  - `apps/gateway/src/presentation/problem-json.filter.ts` was restored after an arm. `git diff` against HEAD is empty.
- **#7, id 103:** `apps/web/app/lib/{money.ts,money.spec.ts}`, `apps/web/app/pages/billing/index.spec.ts`, `apps/web/package.json`, `pnpm-lock.yaml` (+3 lines, the workspace link only).
- **`specs/shared/*` and `CLAUDE.md`:** untouched in both repositories. Every mtime predates both fix-round sessions: the latest is `openapi.yaml` at 04:48Z, the SA-5 work.

## Id 100: verification of D1–D5

**D1 is closed.** I parsed both tables programmatically:
- 26 rows each;
- `a == b` is True;
- the symmetric difference is empty;
- `UYI = 0` is in both;
- #7 has no remaining call to `Intl`.

**D2 is closed.** I armed it myself: #7 `money-text.ts` split replaced by `(Math.abs(n)/10**e).toFixed(e)`, with `dist/` rebuilt.
- Killed in shared-kernel, 2 failed / 12: `expected '90 071 992 547 409.91 EUR' to be '90 071 992 547 409.90 EUR'` and `expected '9 007 199 254 740.990 BHD' to be '9 007 199 254 740.991 BHD'`.
- Killed in projector, 2 failed / 15, same messages.
- Restored (`cmp` OK), `dist/` rebuilt with no `ARM` marker, green.

**D3 is closed.** I armed a single-row corruption on each side myself, using rows other than the implementer's `JOD`:
- **#8, `OMR` 3→2** (`--no-incremental`): `Of_MatchesTheWholeIso4217NonDefaultExponentTable` FAILED with `Expected: 3 / Actual: 2`. Restored (`cmp` OK) and rebuilt; 81/81.
- **#7, `IQD` 3→2** (`dist/` rebuilt):
  - shared-kernel failed: `currencyExponent(IQD): expected 2 to be 3`.
  - #7 web `money.spec.ts` also failed (`expected 'IQD 123.45' to be 'IQD 12.345'`, and others). That shows the web really resolves the shared table.
  - Restored and rebuilt; green.

**D4 and D5 are made.**
- The record now acknowledges #7's `requirements.md`.
- `CurrencyExponent.cs:18-19` cites the right #7 path.

**Advisories, carried into id 103's fix round.** That round has to reopen `CurrencyExponent.cs` anyway (see id 103, R1).
- (a) The D4 correction cites #7's PR16 at `:98`, and says the line "shifted". It did not shift: #7's PR16 is still at `:109`. `:98` is #8's line.
- (b) `CurrencyExponent.cs:106` still says the fallback "mirrors #7's `Intl`-rejects-it fallback". #7 no longer uses `Intl`.
- (c) The whole-table test's failure prints `Expected: 3 / Actual: 2` without naming the currency. CLAUDE.md's failure-message rule applies; add the code to the message. #7's twin already prints `currencyExponent(IQD): …`.

## Id 102: REJECTED

**Enumeration re-run.** I re-ran the recorded #8 command. It reproduces the record's table line for line; the fixed sites now call `MoneyText.Format`. I ran a wider sweep (`discount|total|price|money|minor|exposure|limit`, and `string.Format` / `+` concatenation) and it found no further human-facing money text; the notification `Total:` lines already go through `FormatMoney`. My #7 content sweep (template literals with money-ish names, excluding `apps/web`, specs, `dist` and `.output`) matches the record's #7 table. The remaining hits are:
- SQL template tags;
- test-support helpers;
- `Money.toString()` (`money.ts:146`), which has no human-facing caller;
- the currency-code-only messages.

**The "wire-shape validation echo" design call is accepted.**
- `amount.amount must be a non-negative integer; got X` (`PaymentRegisterRequestValidator.cs:53`) cannot be produced from #8's web UI. `parseDecimalToMinorUnits` (`apps/web/src/lib/money.ts:41`) only accepts `^(\d*)(?:\.(\d{1,e}))?$`, so there are no negatives, and the currency always comes from the invoice.
- Only a direct API client can trigger it, and that client sent integer minor units, so echoing them back is correct.
- The `InvoiceRequestValidator` echoes sit on `billing.invoice.issue`. Only the saga calls that subject; `openapi.yaml` has no `POST /invoices`. So they reach a human only through a saga-failure `lastError`, and only if Orders itself sends a malformed invoice.

**Arms I ran myself:**

| # | Repo | Mutation | Result |
|---|---|---|---|
| Q1 | #8 | `CreditLimitExceededError`: requested amount rendered raw (`{requestedMinorUnits} {currency}`) | **KILLED.** `DomainErrorMoneyTextTests.CreditLimitExceededError_…`: `Expected: "Requested amount 12.345 BHD …" / Actual: "Requested amount 12345 BHD …"`. Restored; 270/270. |
| Q2 | #8 | `BillingErrorMapper.cs:97`, the hop that puts the message on the wire: `e.Message` → `$"Payment amount {e.ReceivedMinorUnits} does not equal the invoice's totalAmount {e.ExpectedMinorUnits}."` | **SURVIVED.** `Billing.UnitTests` 270/270 green. Restored (`cmp` OK, rebuilt `--no-incremental`). |
| Q3 | #7 | `invoice-errors.ts:92`: received amount rendered raw | **KILLED.** `expected 'payment amount (2001) does not match …' to be 'payment amount (20.01 EUR) does not m…'`. Restored; 159/159. |
| Q4 | #7 | `apps/billing/src/presentation/rpc-error-mapper.ts:116`: `message: error.message` → raw `payment amount (${error.received}) … (${error.expected}) …` for `InvoicePaymentAmountMismatchError` | **SURVIVED.** billing 159/159 green, typecheck clean. Restored (`cmp` OK; `git diff` against HEAD empty). |

**Defect E1 (blocking): the service's error-mapper hop is unguarded in both repositories.**
- The user's path has four steps:
  1. the domain error;
  2. the service's `*ErrorMapper` / `rpc-error-mapper.ts`, which builds the wire `message`;
  3. the Gateway's `detail`;
  4. the screen.
- Acceptance bullet 4 asks for "a test per fixed site [that] asserts the whole detail string through the path a user sees". The tests cover step 1 (domain tests) and step 3, but the step-3 tests are Gateway tests that **build the message themselves** with `MoneyText.Format` / `formatMoney`. Neither kind runs step 2.
- As a result, a mapper that re-emits raw minor units, on the exact path this entry was filed for, leaves every suite green in both repositories (Q2, Q4).
- This is the shape the ledger rule describes from feature 19: "the test re-implemented the conversion instead of reading through the mapper".
- The mapper tests (`BillingErrorMapperTests.InvoicePaymentAmountMismatchError_MapsToPreconditionFailed_CarryingItsCode`, and #7 `rpc-error-mapper.spec.ts:92`) check `code` and `details` but never `message`.

**What must change for id 102:**
1. For every fixed site whose error goes through a mapper, pass a **real** domain error through the **real** mapper and assert the whole wire `message`. That means the #8 Billing and Orders mappers, and the #7 billing and orders `rpc-error-mapper.ts`.
   - Better, where the harness allows: drive it through the responder, so the Gateway receives the real bytes.
   - The Gateway tests may stay as the step-3 proof. The record must stop claiming they prove "the WHOLE path" (`ProblemDetailMoneyTextTests.cs` doc comment; #7 `problem-detail-money-text.spec.ts` header).
2. Arm Q2 and Q4, and record them in the arm table.

## Id 103: REJECTED

**Verified correct:**
- **#8 web:**
  - `currencyExponent` reads the literal table;
  - `formatMinorUnits` pins the fraction digits;
  - the parser for HUF, IQD, JPY, BHD and EUR is tested and armed.
- **#7 web:**
  - it imports `@otc/shared-kernel`;
  - `formatMoney` pins the fraction digits;
  - the lockfile diff is only the workspace link;
  - 148 tests pass;
  - my D3 arm shows the web really runs the shared table.
- **The #7 production build resolves the dependency**, checked in `apps/web/.output/public/_nuxt/CrdS2GfQ.js`: the whole shared-kernel barrel is bundled into a 13 KB client chunk. Its `require("node:crypto")` becomes an empty-object shim (`i=t(((e,t)=>{t.exports={}}))`), so loading the chunk is safe. See advisory A2.
- **The fixture is genuine.** `payment-amount-mismatch-422.json` has:
  - `capturedAt` 06:24:34.848Z;
  - `correlationId` `3e26385c-e39d-4424-aff3-2316262110ee`, which is unique among the 16 fixtures and appears in `logs/dev-stack/Gateway.log:58` with the same `detail`;
  - `detail` "Payment amount 92.46 EUR does not equal the invoice's totalAmount 92.45 EUR.";
  - the same top-level keys as its 15 siblings (`body, capturedAt, capturedFrom, headers, status`), and the same body keys as the other problem fixtures.
  - Only this fixture's mtime changed.
- **#8's error sweep passes** (part of the 22/289 run).

**Parser attacks on `currency-exponent.parity.test.ts`.** I edited the real `src/SharedKernel/CurrencyExponent.cs` and ran the parity file each time:

| Attack | Backend effect | Parity test |
|---|---|---|
| `["OMR"] = 3,` → `// ["OMR"] = 3,` (line comment) | OMR becomes 2 | **KILLED**, 2 failed / 5 |
| `["OMR"] = 3,` → `/* ["OMR"] = 3, */` (block comment) | OMR becomes 2. Proven with a `--no-incremental` build: the .NET whole-table test failed `Expected: 3 / Actual: 2` | **SURVIVED**, 5/5 green |
| `["OMR"] = 3,` wrapped in `#if false … #endif` | OMR becomes 2 | **SURVIVED**, 5/5 green |
| `["TND"] = 3, ["MGA"] = 1,` (a second row on one line, backend only) | MGA becomes 1 | **SURVIVED**, 5/5 green. `exec` takes only the first match per line, and the 26-row check still counts 26 |

I restored after each attack (`cmp` OK, rebuilt `--no-incremental`, SharedKernel 81/81).

**Defect R1 (blocking): the parity guard can be beaten by three of the shapes on CLAUDE.md's defeat list** (rows 4, 5 and 11).
- Acceptance bullet 2 requires a test that "reads the backend table and fails on any difference". In three of the attacks above, the backend's live table differs from the web's and the test passes.
- The record's row-5 reasoning ("TypeScript has no preprocessor") is wrong: the file being parsed is C#, which has `#if`. It also dismissed row 6.
- This is the id 68 pattern: a hand-rolled text scanner losing to one new shape after another.
- **Required: test the table's behaviour, not its text.** For example:
  - a .NET test that serialises the live `CurrencyExponent` table (by reflection over `NonDefaultExponents`, or by probing every code) and asserts it equals a committed JSON file, plus a web test that asserts `NON_DEFAULT_EXPONENTS` equals the same JSON;
  - or any equivalent that reads what the compiler built.
- If the parser stays, it must fail loudly on `/*`, on `#if`, and on more than one match per line. Every defeat-list row must be answered honestly.

**Defect R2 (blocking): the #7 web Docker image no longer builds its new dependency.**
- `infra/docker/web/Dockerfile:42-46` copies only `packages/contracts` and runs only `pnpm --filter "@otc/contracts" run build` before `nuxt build`.
- `packages/shared-kernel` is not copied: only its `package.json` arrives, in the deps stage, `:27`. Its `dist/` is git-ignored.
- So `@otc/shared-kernel` resolves to a package whose `main` (`dist/index.js`) does not exist in the image.
- The file's own comment (`:12-14`) still says web does "NOT" depend on `@otc/shared-kernel`.
- The implementer's local `pnpm build` succeeded only because a local `dist/` exists.
- I established this **by reading**; I did not run a `docker build`.
- **Required:**
  - copy `packages/shared-kernel` and build it before `nuxt build`, as `infra/docker/seed/Dockerfile:53,59` already does;
  - correct the comment;
  - prove the image builds, or state why it cannot be run here.

**Advisories:**
- **A1: #7's clean-checkout order.** #7 has no CI and no `quality.sh`. The root `quality` script (`lint && typecheck && test:coverage`) never builds `packages/shared-kernel`, and `pnpm -r run typecheck` runs shared-kernel with `--noEmit`. So on a clean checkout, every consumer of the `dist/` output fails. This **predates id 103**: every backend app has the same precondition. The web app now joins it. Worth a numbered entry only if #7 ever gains CI.
- **A2: the whole barrel ships to the browser.** #7's web now bundles all of shared-kernel into the client, including `UniqueId.generate`, whose `node:crypto` import is an empty shim in the browser. Loading is safe. Calling `generate` there would throw. Importing a narrower path would avoid shipping it.
- **A3: `currencyInputStep` is unused in #8.** The #8 web module exports it, but #8's forms are text inputs. It is disclosed, and acceptance bullet 3's "input step" is satisfied in #7 only.
- **A4: #7's `formatMoney` still divides.** It computes `minorUnits / 10 ** exponent` before `Intl` formats the result. This predates id 103, is display-only, and loses precision only above 2^53 / 10^e. It is noted, not owed here.

## Routing

No finding in this re-review has its root cause in `specs/shared/`. SA-5 correctly says "ISO 4217 minor-unit exponent". R1, R2 and E1 are implementation defects inside open entries 102 and 103, so they need no new backlog id. Advisory A1 is recorded as a candidate entry for the leader to decide on.

---

# Re-review 2: ids 102 and 103. 2026-09-17, about 07:04Z → 07:12Z

This round checks only last round's blocking defects. I checked `CLAUDE.md` on disk before applying any rule from it.

## Verdicts

| Id | Verdict | Status set |
|---|---|---|
| 102 | **APPROVED** | `done` (line 1535) |
| 103 | **APPROVED** | `done` (line 1551) |

Both edits were one line each and neither was refused. `diff` against my pre-edit copy shows only lines 1535 and 1551. The file parses, and ids 100–103 all read `done`.

## Id 102: E1 is closed

**Q2, re-applied myself.** `BillingErrorMapper.cs:97`: `e.Message` → raw `$"Payment amount {e.ReceivedMinorUnits} … {e.ExpectedMinorUnits}."`, built `--no-incremental`.
- **KILLED.** `BillingErrorMapperMoneyTextTests.InvoicePaymentAmountMismatchError_ReachesTheWireMessage_ScaledByTheCurrencysExponent`: `Expected: "Payment amount 92.45 EUR does not equal t"··· / Actual: "Payment amount 9245 does not equal the in"···`, 1 failed / 278.
- Restored: `cmp` OK, `git diff` against HEAD empty, rebuilt; 278/278.

**Q4, re-applied myself.** `apps/billing/src/presentation/rpc-error-mapper.ts:116`: raw `payment amount (${error.received}) …` for `InvoicePaymentAmountMismatchError`.
- **KILLED.** `rpc-error-mapper — money text reaches the wire message (id 102 fix round 1) > InvoicePaymentAmountMismatchError (Invoice.markPaid) …`: `expected 'payment amount (2001) does not match …' to be 'payment amount (20.01 EUR) does not m…'`, 1 failed / 164.
- Restored: `cmp` OK, `git diff` against HEAD empty; 164/164.

## Id 103: R1 and R2 are closed, and the id 100 leftovers are fixed

**R1: the new instrument.** `CurrencyExponentWebParityTests` compares the compiled `CurrencyExponent.NonDefaultExponents` with `apps/web/src/lib/currency-exponents.json`. `apps/web/src/lib/currency-exponent.ts:1,50` imports that JSON directly. My attacks, each followed by a restore and a `cmp`:

| Attack | Result |
|---|---|
| JSON only: `"OMR": 3` → `2` | **KILLED**: `Value mismatches: [OMR: backend=3, web=2]` |
| JSON only: a duplicate `"OMR": 2` key appended (both JSON parsers keep the last value) | **KILLED**: same message. Both sides read the same last value, so the web would also see 2; the guard reports it. |
| C# only: `["IQD"] = 3` → `2` | **KILLED**: `Value mismatches: [IQD: backend=2, web=3]` |
| C#: `/* ["OMR"] = 3, */` (block comment) | **KILLED**: `In web only: [OMR]` |
| C#: `#if false` around the OMR row | **KILLED**: `In web only: [OMR]` |
| C#: `["TND"] = 3, ["MGA"] = 1,` (two rows on one line) | **KILLED**: `In backend only: [MGA]` |
| Web behaviour: JSON `"IQD": 2`, then run `money.test.ts` | **KILLED**: `expected 2 to be 3`, `expected 'IQD 123.45' to be 'IQD 12.345'`. This proves the web really runs the JSON. |

- Every C# attack was built `--no-incremental`, one at a time.
- After the final restore and rebuild: SharedKernel.UnitTests 82/82; #8 web 22 files / 286 tests. The 286 reconciles with the record: 289 − 5 deleted + 2 new.

**R2: the #7 Dockerfile.** The diff now copies `packages/shared-kernel` and runs `pnpm --filter "@otc/shared-kernel" run build` before `@otc/contracts` and `nuxt build`. The header comment is corrected.
- I did not re-run the build.
- The implementer's image, `otc-web:local` with ID `0e822371a1b9`, exists locally, created 06:53Z. Its ID matches the `sha256:0e822371…` in the recorded build log.

**Id 100 leftovers:**
- `CurrencyExponent.cs:115` now reads "matches #7's `currencyExponent`, which falls back the same way".
- The whole-table test (`CurrencyExponentTests.cs:92`) uses `Assert.True(…, $"CurrencyExponent.Of(\"{code}\"): expected {expected}, was {actual}")`, so a failure names the currency.
- The record's #7 PR16 citation now reads `:109`.

## Advisory

- **A5:** `CurrencyExponent.NonDefaultExponents` is now `public` and typed `IReadOnlyDictionary`, but the object behind it is a mutable `Dictionary`. A caller could cast it and change it at runtime. Wrapping it in `.AsReadOnly()` or using a `FrozenDictionary` would close that. Not blocking.
