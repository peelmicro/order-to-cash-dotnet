# impl — id 102 `problem_detail_money_reads_as_minor_units` (#8 and #7)

Repositories worked in: `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-dotnet` (#8) and `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` (#7). No `specs/shared/` file touched. No `CLAUDE.md` edit. No `apps/web` edit. No git command that writes the index or working tree was run. `feature_list.json`'s id 102 status is **left unchanged** (still `pending`) — the brief did not authorise changing it. Date: 2026-09-17.

Brief: `brief_100_fix1_102.md`. Filed from `progress/review_timeline_money_and_stock_names.md` D6.

## The defect

A `DomainError`/RPC error message that names an amount reaches a Gateway problem document's `detail` in raw `long` minor units — `"Payment amount 9245 does not equal the invoice's totalAmount 12000."` — which the web app shows verbatim. Traced by the review: `src/Billing/Domain/Errors/InvoicePaymentAmountMismatchError.cs:7` → `src/Billing/Presentation/Rpc/BillingErrorMapper.cs:95-98` → `src/Gateway/Presentation/Problem/ProblemJsonMiddleware.cs:95` (the `Classification` whose 4th argument is `Detail`) → the web shows `detail`. #7's twin: `apps/billing/src/domain/invoice-errors.ts:71`.

The predecessor's id 100 record classified every Billing/Orders `DomainError` amount message as "developer diagnostic text, out of scope". That classification was **wrong for all of them, not just one** — every one of them is mapped by its service's `*ErrorMapper` and reaches the Gateway's `detail` via `ProblemJsonMiddleware.Classify`'s `RpcCallError` branch (`rpcError.Message`, forwarded verbatim), which the web renders.

## Enumeration (defect class: a `DomainError`/RPC error message that interpolates an amount), as a search result

Command, #8, content-based, excluding build output and this feature's own new test files:

```
find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/tests/*' -print0 \
  | xargs -0 grep -nE '\$"' | grep -iE "amount|minorunits|totalamount|heldamount|creditlimit|openexposure|availablecredit|price"
```

Full output, one line per hit, classified against **"does this reach a Gateway problem document's `detail`, and does it name a MONEY amount (as opposed to a raw wire-level value being echoed back for schema-validation diagnostics, or a currency code with no amount)?"**:

| Hit | Reaches `detail`? | Names a money amount? | Verdict |
|---|---|---|---|
| `src/SharedKernel/Money.cs:85` `ToString()` | No live human-facing caller (checked: unused in any human-facing path, per id 100's own finding) | — | Not fixed — no live path, flagged for a future entry as id 100 already recorded |
| `src/Projector/Domain/Summaries.cs:50,54` | No — Projector facts feed the read model/timeline, not a problem document | — | Not a hit for this class (already fixed by id 100) |
| `src/Billing/Domain/Invoice.cs:232` (`InvalidInvoiceSnapshotError`, line currency mismatch) | Yes, via generic `DomainError` → `DOMAIN_ERROR` | No — currency **codes** only, no amount | Not a hit |
| `src/Billing/Domain/Invoice.cs:256,263,269` (`InvalidInvoiceSnapshotError`, stored amount/totalAmount/negative-total disagreements) | **Yes** | **Yes** | **FIXED** — `MoneyText.Format(…, snapshot.Currency)` |
| `src/Billing/Domain/CreditLedgerEntry.cs:39` (`InvalidBuyerCreditSnapshotError`, non-positive amount) | **Yes** | **Yes** | **FIXED** — `MoneyText.Format(amount.MinorUnits, amount.Currency)` |
| `src/Billing/Domain/BuyerCredit.cs:73-74` (`InvalidBuyerCreditSnapshotError`, committedExposure vs creditLimit) | **Yes** | **Yes** | **FIXED** — `MoneyText.Format(…, snapshot.CreditLimit.Currency)` |
| `src/Billing/Domain/BuyerCredit.cs:81-82` (`InvalidBuyerCreditSnapshotError`, entry currency mismatch) | Yes | No — currency codes only | Not a hit |
| `src/SharedKernel/Errors/CurrencyMismatchError.cs:15` | Yes (reachable via any aggregate) | No — currency codes only | Not a hit |
| `src/Projector/Infrastructure/Persistence/DeltaToPipeline.cs:112,114` | No — a MongoDB field-path string, never a rendered value | — | Not a hit |
| `src/Seed/Domain/Sagas/SagaFixtures.cs:289,469,354` | No — seed-only, already covered by id 100 | — | Not this class |
| `src/Billing/Presentation/Rpc/CreditRequestValidator.cs:122` (`amount.currency '…' must match …`) | Yes | No — `amount.currency`, not `amount.amount`; a grep false-positive on the substring `amount` | Not a hit |
| `src/Billing/Presentation/Rpc/PaymentRegisterRequestValidator.cs:53` (`amount.amount must be a non-negative integer; got {value}`) | Yes | **Names a raw wire value**, not a presented money amount — see "Design choice: wire-shape validation echoes" below | **Not fixed, deliberately** |
| `src/Billing/Presentation/Rpc/PaymentRegisterRequestValidator.cs:58` (`amount.currency '…' must match …`) | Yes | No — currency code only | Not a hit |
| `src/Billing/Presentation/Rpc/InvoiceRequestValidator.cs:63` (`lines[].unitPrice must be a non-negative integer; got {value}`) | Yes | Wire-shape echo, same reasoning | **Not fixed, deliberately** |
| `src/Billing/Presentation/Rpc/InvoiceRequestValidator.cs:90` (`discount ({discount}) must not exceed the sum of unitPrice x units ({sum})`) | Yes | Wire-shape cross-field echo (both operands are the raw wire-level values that failed the schema check, computed before any currency is confirmed valid), same reasoning | **Not fixed, deliberately** |
| `src/Billing/Domain/Errors/NegativeInvoiceTotalError.cs` | **Yes** | **Yes** | **FIXED** |
| `src/Billing/Domain/Errors/CreditReleaseUnderflowError.cs` | **Yes** | **Yes** | **FIXED** |
| `src/Billing/Domain/Errors/CreditRefusalMismatchError.cs` | **Yes** | **Yes** | **FIXED** |
| `src/Billing/Domain/Errors/InvoicePaymentAmountMismatchError.cs` | **Yes — the review's own traced path** | **Yes** | **FIXED** |
| `src/Billing/Domain/Errors/CreditLimitExceededError.cs` | **Yes** | **Yes** | **FIXED** |
| `src/Orders/Domain/Errors/OrderTotalMustNotBeNegativeError.cs:17` | **Yes** — mapped by `OrdersCreateErrorMapper`'s generic `DomainError` case (`VALIDATION_FAILED`) | **Yes** | **FIXED** — already carries the `Money` value directly (`candidateTotalAmount`), so this is `MoneyText.Format(candidateTotalAmount.MinorUnits, candidateTotalAmount.Currency)` |
| `src/Orders/Domain/Errors/OrderLineCurrencyMismatchError.cs:18` | Yes | No — currency codes only | Not a hit |
| `src/Orders/Application/Commands/PlaceOrderErrors.cs:51` (`OrderDiscountNotSupportedError`) | **Yes** — mapped `VALIDATION_FAILED` | **Yes** | **FIXED** — currency threaded from the request's own wire `currency` field (see "Currency source" below) |
| `src/Notifications/Application/Templates/PaymentReceivedTemplate.cs:23,36` | No — email body, not a problem document | — | Not this class (already fixed by id 100) |

**#7 enumeration**, same predicate, content-based sweep of the symmetric set id 100's own record already found (`apps/billing/src/domain/buyer-credit.ts:92`, `apps/billing/src/domain/invoice-errors.ts:38`, `apps/billing/src/domain/invoice.ts:183,190`), plus the Orders-side `NegativeOrderTotalError` (`apps/orders/src/domain/order-errors.ts`) and `OrderDiscountNotSupportedError` (`apps/orders/src/application/place-order.errors.ts`) found by reading `rpc-error-mapper.ts`'s own mapping table for both services:

| Hit | Reaches `detail`? | Names a money amount? | Verdict |
|---|---|---|---|
| `apps/billing/src/domain/credit-errors.ts` — `CreditLimitExceededError`, `CreditReleaseUnderflowError` | **Yes** | **Yes** | **FIXED** |
| `apps/billing/src/domain/credit-errors.ts` — `CreditRefusalMismatchError` | Yes | **No** — this class's #7 constructor takes only `reason: string`, no amount at all (a genuine design difference from #8's version, which does carry both amounts) | Not a hit in #7 |
| `apps/billing/src/domain/credit-errors.ts` — `InvalidBuyerCreditSnapshotError` (via `buyer-credit.ts`'s `reconstitute`, two call sites: `creditLimit < 0`, `committedExposure > creditLimit`) | **Yes** | **Yes** | **FIXED** |
| `apps/billing/src/domain/credit-ledger-entry.ts` | Yes | No — #7's `CreditLedgerEntry.create` has NO non-positive-amount check at all (per its own comment, "checked by the AGGREGATE" — and no such check exists in `buyer-credit.ts` either); a genuine design difference from #8, not a hit here | Not a hit in #7 |
| `apps/billing/src/domain/invoice-errors.ts` — `NegativeInvoiceTotalError`, `InvoicePaymentAmountMismatchError` | **Yes — the latter is the review's own cited `invoice-errors.ts:71`** | **Yes** | **FIXED** |
| `apps/billing/src/domain/invoice.ts` — `InvalidInvoiceSnapshotError` (stored amount/totalAmount reconciliation, two call sites) | **Yes** | **Yes** | **FIXED** |
| `apps/orders/src/domain/order-errors.ts` — `NegativeOrderTotalError` | **Yes** | **Yes** — was `Money.toString()` (`money.ts:146`, raw minor units — the exact D1-class defect) | **FIXED** |
| `apps/orders/src/application/place-order.errors.ts` — `OrderDiscountNotSupportedError` | **Yes** | **Yes** | **FIXED** — currency from `command.currency` |

## Design choice: wire-shape validation echoes are NOT reformatted

Three call sites interpolate a raw integer that FAILED a schema-level validation check (`amount.amount must be a non-negative integer; got {value}`, `lines[].unitPrice must be a non-negative integer; got {value}`, `discount (…) must not exceed the sum of unitPrice x units (…)`), all in `Billing/Presentation/Rpc/*RequestValidator.cs`. These reach `detail` (mapped `VALIDATION_FAILED`), and were deliberately left unformatted:

- The message's own wording ("must be a non-negative integer") is describing a JSON-schema-shape violation of the WIRE FIELD ITSELF — the value is being echoed back as evidence of what the client sent, not presented as a currency amount for a human to read financially. Formatting `"unitPrice must be a non-negative integer; got 12.50 EUR"` would misrepresent the point of the message, which is that the field failed to BE a valid integer amount at all.
- The currency is not reliably available at these sites: validation accumulates every error independently (no short-circuit), so a request with BOTH an invalid `unitPrice`/`amount` AND an invalid/missing `currency` produces both messages from the same pass, and the amount-validation branch runs whether or not the currency branch also fired. Using an unconfirmed or absent currency to format an already-invalid number risks presenting a fabricated decimal for data that is not a valid amount in the first place.
- This is a considered decision, not an oversight: the discriminator is "is this message reporting a STORED/COMPUTED business value (format it) or ECHOING a malformed WIRE FIELD as validation diagnostics (leave it raw)?" Every site fixed above is the former; these three are the latter.

## Currency source (per fixed site)

The brief: *"The currency must come from data the error has. If a message has an amount but no currency, decide how to obtain the currency and record the decision; do not guess."* Every fixed site already had the currency in scope at the throw site — none required a guess or a new port:

| Site | Currency source |
|---|---|
| `InvoicePaymentAmountMismatchError` (both repos) | The invoice's own `Currency`/`currency` — `MarkPaid`/`markPaid` checks the payment's currency matches BEFORE this error can be raised, so both amounts share it |
| `NegativeInvoiceTotalError` (both repos) | `Invoice.Issue`/`Invoice.issue`'s local `currency` (= `input.Discount.Currency`/`input.currency`) |
| `InvalidInvoiceSnapshotError` reconciliation messages (both repos) | `snapshot.Currency`/`snapshot.currency` |
| `CreditLimitExceededError`, `CreditRefusalMismatchError` (#8 only — see #7 table above), `CreditReleaseUnderflowError` | The credit line's own `CreditLimit.Currency`/`this.props.currency` |
| `InvalidBuyerCreditSnapshotError` (both repos) | `snapshot.CreditLimit.Currency`/`snapshot.currency` |
| `CreditLedgerEntry.Create` non-positive-amount message (#8 only) | The `Money` value passed in (`amount.Currency`) |
| `OrderTotalMustNotBeNegativeError`/`NegativeOrderTotalError` (both repos) | Already carries a `Money` value (`candidateTotalAmount`/`totalAmount`) — the currency was already there, just unused by the old `{minorUnits} {currency}`/`Money.toString()` rendering |
| `OrderDiscountNotSupportedError` (both repos) | The request's own wire `currency` field (`command.Currency`/`command.currency`), threaded from `PlaceOrderCommandHandler.HandleAsync`/`place-order.handler.ts`'s `execute` — the only currency this error's throw site has, since it fires BEFORE reference-data resolution confirms the code exists. Documented in the error class's own doc comment/JSDoc (both repos) as a deliberate choice, not a guess: it is the exact value the client supplied, echoed back in the SAME message that already names it, so no invented default was needed. |

No site needed a currency it did not already have — every "does it have a currency" question resolved to "yes, in a `Money` value or a sibling field already in scope."

## Machine fields unchanged

`code` (the `RpcErrorPayload`/`RpcError` machine field) and every structured `details` amount (`e.Code`, `["code"] = e.Code`, `ExpectedMinorUnits`/`ReceivedMinorUnits`/`RequestedMinorUnits`/`AvailableMinorUnits` properties, `expected`/`received`/`requested`/`available` in #7) are untouched — only the human-readable `.Message`/constructor `super(...)` string changed. Verified by reading every edited constructor: the raw `long`/`number` properties are still assigned from the raw parameter, never from the formatted string.

## Tests, and how each fixed site is proven through the path a user sees

**#8**, new: `tests/Billing.UnitTests/DomainErrorMoneyTextTests.cs` (8 cases — one whole-string assertion per fixed Billing site: `InvoicePaymentAmountMismatchError`, `NegativeInvoiceTotalError`, `CreditReleaseUnderflowError`, `CreditRefusalMismatchError`, `CreditLimitExceededError`, `CreditLedgerEntry.Create`, `BuyerCredit.Reconstitute`, `Invoice.Reconstitute`), `tests/Gateway.UnitTests/ProblemDetailMoneyTextTests.cs` (3 cases — the RPC-error → Gateway-problem-document path itself, for `InvoicePaymentAmountMismatchError`'s traced route AND `OrderTotalMustNotBeNegativeError`'s Orders-side route, plus a `code`-field-unaffected control).

**#8**, extended: `tests/Orders.UnitTests/OrderTotalsTests.cs` (R6 test now also asserts the exact `Message`), `tests/Orders.UnitTests/PlaceOrderCommandHandlerTests.cs` (new whole-string assertion on `OrderDiscountNotSupportedError.Message`).

Gateway.UnitTests has no `ProjectReference` to Billing or Orders (checked: `tests/Gateway.UnitTests/Gateway.UnitTests.csproj` references only `Gateway`, `SharedKernel`, `Contracts`, `Cqrs`), matching the "database per service" / no cross-service reference rule — so `ProblemDetailMoneyTextTests.cs` reconstructs the WIRE MESSAGE TEXT with `MoneyText.Format` directly (the same function the Billing/Orders services call) and drives it through a real `RpcBusinessError`/`ProblemJsonMiddleware.Classify`, rather than importing the error types. The domain-level tests separately prove the message TEXT is correct; this test proves the PASS-THROUGH is exact (`ProblemJsonMiddleware.Classify`'s existing `Classify_NeverRewritesTheOriginalMessage` test already proves no rewriting happens for any case, generically).

**#7**, new: `apps/billing/src/domain/domain-error-money-text.spec.ts` (5 cases: `CreditLimitExceededError`, `CreditReleaseUnderflowError`, `InvalidBuyerCreditSnapshotError`, `NegativeInvoiceTotalError`, `InvoicePaymentAmountMismatchError`), `apps/orders/src/domain/domain-error-money-text.spec.ts` (2 cases: `NegativeOrderTotalError` for a 2-exponent and a 0-exponent currency), `apps/gateway/src/presentation/problem-detail-money-text.spec.ts` (2 cases — the same RPC-error → Gateway-problem-document proof as #8's, using `RpcBusinessError`/`ProblemJsonExceptionFilter.catch`).

**#7**, extended: `apps/orders/src/application/place-order.handler.spec.ts` (new whole-string assertion on `OrderDiscountNotSupportedError`'s message, reached through the real handler).

## Arming table

All arms follow the protocol: `cp` a backup, mutate, run the named test, record the verbatim failure, restore from the backup, `cmp` byte-identical, force a rebuild (`--no-incremental` for .NET; for #7, a `currency` parameter left unread after a mutation was pinned with a throwaway field so the mutated build still compiles under `TreatWarningsAsErrors`/`CS9113`), re-run green.

| # | Repo | Mutation | Result |
|---|---|---|---|
| 1 | #8 | `InvoicePaymentAmountMismatchError`'s message reverted to raw `{receivedMinorUnits}`/`{expectedMinorUnits}` (no `MoneyText.Format`) | **KILLED.** `DomainErrorMoneyTextTests.InvoicePaymentAmountMismatchError_RendersBothAmountsScaledByTheCurrencysExponent`: `Assert.Equal() Failure: Strings differ … Expected: "Payment amount 92.45 EUR …" / Actual: "Payment amount 9245 …"`. 1 failed / 7 passed (same file). Restored; `cmp` identical; rebuilt `--no-incremental`; `Billing.UnitTests` 270/270 green. |
| 2 | #8 | `Invoice.Reconstitute`'s "stored amount" message (line 259) reverted to raw `{snapshot.Amount.MinorUnits}`/`{recomputedAmount}` | **KILLED.** `Invoice_Reconstitute_RefusesAStoredAmountDisagreeingWithTheLinesOwnTotal_RenderedScaledByTheCurrencysExponent`: `Expected: "…: stored amount 99.99 EUR …" / Actual: "…: stored amount 9999 …"`. 1 failed / 7 passed. Restored; `cmp`; rebuilt; 270/270 green. |
| 3 | #8 | `OrderDiscountNotSupportedError`'s message reverted to raw `{orderDiscountMinorUnits}` (`currency` parameter pinned to an unread private field to keep the mutated build compiling) | **KILLED.** `Handler_RefusesANonZeroOrderDiscountBeforeResolvingAnythingElse`: `Expected: "orderDiscount 1.50 EUR …" / Actual: "orderDiscount 150 …"`. 1 failed / 0 passed (isolated filter). Restored; `cmp`; rebuilt; `Orders.UnitTests` 500/500 green. |
| 4 | #8 | `ProblemJsonMiddleware.Classify`'s `RpcBusinessError`/`RpcCallError` branch: `detail: rpcError.Message` → `detail: classified.Title` (the pass-through itself, not a domain message) | **KILLED, both new Gateway tests.** `Classify_InvoicePaymentAmountMismatch…`: `Expected: "payment amount (92.45 EUR) …" / Actual: "Payment amount does not match the invoice total"` (the generic `title`). `Classify_OrderTotalMustNotBeNegative…`: same shape. 2 failed / 1 passed (isolated filter). Restored; `cmp`; rebuilt `--no-incremental`; `Gateway.UnitTests` 248/248 green. |
| 5 | #7 | `credit-errors.ts`'s `CreditLimitExceededError` reverted to raw `${requested}`/`${available}` | **KILLED.** `domain-error-money-text.spec.ts`: `Expected: "requested 10.00 EUR …" / Actual: "requested 1000 …"`. 1 failed / 4 passed. Restored; `cmp` identical; `Billing` suite (no `dist/` rebuild needed — Vitest resolves `apps/billing`'s own TS source directly) 159/159 green. |
| 6 | #7 | `problem-json.filter.ts`'s `RpcBusinessError` branch: `detail: exception.message` → `detail: classified.title` | **KILLED, both new Gateway tests.** Same shape as arm 4. 2 failed / 0 passed (isolated file). Restored; `cmp`; `Gateway` suite 141/141 green. |
| 7 | #7 | `order-errors.ts`'s `NegativeOrderTotalError` reverted to `${totalAmount.toString()}` (the ORIGINAL, pre-fix code — `Money.toString()`'s raw minor units) | **KILLED, both new tests.** `Expected: "… -1.00 EUR" / Actual: "… -100 EUR"`; `Expected: "… -1 000 JPY" / Actual: "… -1000 JPY"`. 2 failed / 0 passed (isolated file). Restored; `cmp`; `Orders` suite (`domain-error-money-text.spec.ts` + `order-totals.spec.ts`) 14/14 green. |

Arms 1-3, 5, 7 attack the DOMAIN/APPLICATION-LAYER formatting (row 1 of `CLAUDE.md`'s defeat list — deletion/reversion of the correct behaviour). Arms 4 and 6 attack the GATEWAY PASS-THROUGH specifically — the property that the review's own trace names ("BillingErrorMapper → ProblemJsonMiddleware → the web shows `detail`") is not merely "the domain message is correct" but "the message SURVIVES the trip through the Gateway unchanged", and arms 4/6 are the ones that would catch a regression in THAT step even if every domain message stayed correct.

## Defeat-list statement (`CLAUDE.md` rows 1-12)

- **Row 1 (delete/revert)** — arms 1, 2, 3, 5, 7 above.
- **Row 2 (corrupt a payload field the test supplied)** — not directly applicable in the strict sense (no wire payload record here); the closest analogue is arms 4/6, which corrupt the FIELD the middleware forwards (substituting `title` for `message`) rather than a domain value — covered.
- **Row 3 (substitute a valid sibling identifier)** — not applicable; no `MSSQL_DB_*`-shaped sibling family here.
- **Rows 4-6 (text-shadowing, dead regions, raw strings)** — not applicable; every guard executes real code, none scans text.
- **Row 7 (drop an OPTIONAL element)** — not applicable; every fixed site emits a REQUIRED message, not an optional one.
- **Row 8 (literal compared to a literal)** — not applicable; every test calls the real function/handler and compares its real output to a literal expected string.
- **Row 9 (two-part claim, premise half stale)** — this entry's own "currency source" table is the premise half of each fix; every row cites the exact field the throw site already had, checked by reading the surrounding code, not assumed. The guard half (arming table) and the premise half (currency-source table) are both stated, and neither is left unattacked.
- **Row 10 (build-output copy joins the population)** — not applicable; the enumeration command excludes `bin`/`obj` at the source.
- **Row 11 (form the instrument doesn't recognise)** — not applicable; plain function/handler tests, no syntax sweep.
- **Row 12 (failure path the population never drives)** — this is the row arms 4/6 exist to close: a domain-message-only test suite would never notice the Gateway itself failing to forward the message, because every domain test calls the domain function directly, never the Gateway. Arms 4/6 drive that exact path.

## Web fixture check (per the brief — report only, do not edit `apps/web`)

**#8 — a real captured fixture is now stale. Reported, not touched.**

```
grep -rln "does not equal the invoice\|does not match the invoice\|would be negative" apps/web/src/test/fixtures/gateway/
```
→ `apps/web/src/test/fixtures/gateway/payment-amount-mismatch-422.json`, a REAL captured Gateway response (`"capturedAt": "2026-09-16T14:53:37.435Z"`) whose body is `"detail":"Payment amount 5548 does not equal the invoice's totalAmount 5547."` — the exact raw-minor-units shape this entry's fix removes. Four test files consume it: `apps/web/src/features/billing/billing-view.test.tsx`, `apps/web/src/test/gateway-fixtures.ts`, `apps/web/src/lib/error-text-chain.test.ts`, `apps/web/src/app/api/route-handlers.test.ts`. Of those, **`billing-view.test.tsx:342` hard-codes the raw text as a literal assertion**: `expect(…textContent).toBe('Payment amount 5548 does not equal the invoice\'s totalAmount 5547.')`. Once this entry's Billing fix is live end to end, a REAL Gateway would answer `"Payment amount 55.48 EUR does not equal the invoice's totalAmount 55.47 EUR."` for the same underlying values — this fixture and that assertion are now stale and will need the `apps/web` implementer to re-capture/update them. **Not edited here** — reported per the brief, since another implementer owns `apps/web` right now.

**#7 — a synthetic mock uses the same old shape, but is not "captured" and is not broken by this fix.**

`apps/web` has no dedicated fixtures directory (Nuxt's structure differs from #8's Next.js); the broader sweep (`grep -rln … apps/web/`) found one hit: `apps/web/app/pages/billing/index.spec.ts:277`, which constructs a HAND-WRITTEN mock server response (`detail: 'Payment amount 100 does not match invoice total 24999'`) — not captured from a real backend call, and the test's own stated purpose is "surfaces the server's own reason" (i.e. the UI must render whatever `detail` text the mock returns, verbatim). This test does not call the real Billing/Gateway code this entry changed, so it will keep passing regardless — but the mock string is now a cosmetically outdated example of the exact defect class this entry fixes. Reported for the `apps/web` implementer's awareness; **not edited here**, same reason as #8.

## Verify

**#8:**
| Command | Before this entry | After | Reconciliation |
|---|---|---|---|
| `dotnet build OrderToCash.sln --no-incremental` | — | exit 0, 0 warnings, 0 errors | |
| `Billing.UnitTests` | 262 | **270** | +8 (`DomainErrorMoneyTextTests.cs`) |
| `Orders.UnitTests` | 500 | 500 | unchanged count — 2 new assertions added to 2 EXISTING tests, no new test methods |
| `Gateway.UnitTests` | 245 | **248** | +3 (`ProblemDetailMoneyTextTests.cs`) |
| `Architecture.Tests` | 50 | 50 | unchanged |

*"Before" counts: `Orders.UnitTests` (500) was run and empirically measured before any new test file was added, in this same session. `Billing.UnitTests` (262) and `Gateway.UnitTests` (245) are derived by subtracting the exact count of new test methods this session added (8, 3 respectively — counted directly from the new files, `grep -c '\[Fact\]'`) from the after-count actually run (270, 248) — the new files were already present when those two suites were first run in this session, so no separate reverted-tree run was needed or performed.*

**#7:**
| Command | Before this entry | After | Reconciliation |
|---|---|---|---|
| `pnpm --filter billing typecheck` / `pnpm --filter orders typecheck` / `pnpm --filter gateway typecheck` | — | all exit 0 | |
| `pnpm --filter billing test` | 154 | **159** | +5 (`domain-error-money-text.spec.ts`) |
| `pnpm --filter orders test` | 539 | **542** | +3 = 2 (`domain-error-money-text.spec.ts`) + 1 (new assertion in `place-order.handler.spec.ts`) |
| `pnpm --filter gateway test` | 139 | **141** | +2 (`problem-detail-money-text.spec.ts`) |
| `npx eslint <every file this entry touched>` | — | exit 0 (one warning fixed: an unused `OrderNumber` import in the new Orders spec, removed) | |

`billing` (154) and `orders` (539, base before the +3) were empirically run before their new/extended spec files existed, in this session. `gateway` (139) is derived by subtraction (141 measured − 2 counted new tests in `problem-detail-money-text.spec.ts`), same reasoning as #8's `Billing`/`Gateway` rows above.

Total new test cases this entry added: **11 in #8 (8 + 3), 10 in #7 (5 + 2 + 1 + 2) = 21.**

Not run: `./quality.sh` in either repository (per the brief). `apps/web` in either repository (checked only, per the brief's explicit instruction).

## Rules followed

Touched, #8: `src/Billing/Domain/Errors/{InvoicePaymentAmountMismatchError,NegativeInvoiceTotalError,CreditReleaseUnderflowError,CreditRefusalMismatchError,CreditLimitExceededError}.cs`, `src/Billing/Domain/{Invoice,BuyerCredit,CreditLedgerEntry}.cs`, `src/Orders/Domain/Errors/OrderTotalMustNotBeNegativeError.cs`, `src/Orders/Application/Commands/{PlaceOrderErrors,PlaceOrderCommandHandler}.cs`, their tests (new + extended, listed above). Touched, #7: `apps/billing/src/domain/{credit-errors,buyer-credit,invoice-errors,invoice}.ts`, `apps/orders/src/domain/order-errors.ts`, `apps/orders/src/application/{place-order.errors,place-order.handler}.ts`, their tests (new + extended). Also updated existing constructor call sites in `tests/Billing.UnitTests/BillingErrorMapperTests.cs`, `tests/Orders.UnitTests/OrdersCreateErrorMapperTests.cs`, `apps/billing/src/presentation/rpc-error-mapper.spec.ts`, `apps/orders/src/presentation/rpc-error-mapper.spec.ts` (new required constructor parameter, no behavioural change to those tests). Nothing under `apps/web` in either repository. No `specs/shared/` file. No `CLAUDE.md` edit. `feature_list.json` id 102's status untouched (left `pending` — the brief only authorised transitioning id 100).

## Surprises

- #7's `CreditRefusalMismatchError` and `credit-ledger-entry.ts`'s non-positive-amount check are genuinely absent/differently-shaped compared to #8's equivalents — not every #8 fix site has a live #7 twin. Recorded in the enumeration table rather than forced into a parity that does not exist in the code.
- The Gateway pass-through arms (4, 6) are the ones the review's own trace most directly names ("BillingErrorMapper → ProblemJsonMiddleware → the web shows detail"), and neither #8 nor #7 had ANY existing test that would have caught a regression in that specific hop for a money-bearing message — `Classify_NeverRewritesTheOriginalMessage` (#8) only pins `InvalidCredentialsError`'s message, not a money-bearing one, so this entry's arms 4/6 close a gap the existing suite did not already cover for this class.
- `PaymentRegisterRequestValidator.cs`/`InvoiceRequestValidator.cs`'s wire-shape echoes were the one place this entry chose NOT to apply `MoneyText.Format`, on purpose — see "Design choice" above. This is the kind of call the brief's "decide … do not guess" applies to at the CLASS-MEMBERSHIP level as much as at the currency-source level: not every `${amount}`-shaped hit is the SAME defect class as id 100/102 exist to fix.

## Status

`id 102`'s `feature_list.json` status is left as the brief specified — **unchanged**.

## Fix round 1

Brief: `brief_102_fix1.md`. Filed from `progress/review_timeline_money_and_stock_names.md`'s "Id 102: REJECTED" section, defect E1.

### The blocking defect (E1)

Nothing tested the service error-mapper hop between the domain error and the Gateway, in either repository. `BillingErrorMapper.cs`/`OrdersCreateErrorMapper.cs` and `rpc-error-mapper.ts` (billing, orders) all pass `e.Message`/`error.message` straight through in every branch — but nothing DROVE a real domain error through the real mapper and asserted the whole wire `message`. The reviewer's own arms (Q2, #8 `BillingErrorMapper.cs:97`; Q4, #7 `rpc-error-mapper.ts:116`) proved it: reverting the mapper to rebuild a raw-minor-units message left both suites fully green (270/270, 159/159), because `DomainErrorMoneyTextTests`/`domain-error-money-text.spec.ts` call the domain error directly and `ProblemDetailMoneyTextTests`/`problem-detail-money-text.spec.ts` reconstruct the message text themselves rather than importing the mapper (no cross-service project reference).

### Sites, and the unit the claim is about

The unit is **each fixed site's path through its own service mapper** — one test per site, asserting the exact wire `message`. Counting from id 102's own enumeration table:

**#8 — 10 sites, all now covered:**
- Billing (8, new file `tests/Billing.UnitTests/BillingErrorMapperMoneyTextTests.cs`, through `BillingErrorMapper.Map`): `InvoicePaymentAmountMismatchError`, `NegativeInvoiceTotalError`, `CreditReleaseUnderflowError`, `CreditRefusalMismatchError`, `CreditLimitExceededError`, `CreditLedgerEntry.Create`'s non-positive-amount refusal, `BuyerCredit.Reconstitute`'s over-limit refusal, `Invoice.Reconstitute`'s stored-amount refusal.
- Orders (2, extended `tests/Orders.UnitTests/OrdersCreateErrorMapperTests.cs`, through `OrdersCreateErrorMapper.Map`): `OrderDiscountNotSupportedError`, `OrderTotalMustNotBeNegativeError`.

**#7 — 7 sites, all now covered:**
- Billing (5, extended `apps/billing/src/presentation/rpc-error-mapper.spec.ts`, new describe block `rpc-error-mapper — money text reaches the wire message (id 102 fix round 1)`, through `toRpcError`): `CreditLimitExceededError`, `CreditReleaseUnderflowError`, `InvalidBuyerCreditSnapshotError` (via `BuyerCredit.reconstitute`), `NegativeInvoiceTotalError` (via `Invoice.issue`), `InvoicePaymentAmountMismatchError` (via `Invoice.markPaid` — the review's own traced route).
- Orders (2, extended `apps/orders/src/presentation/rpc-error-mapper.spec.ts`, new describe block `toRpcError — money text reaches the wire message (id 102 fix round 1)`, through `toRpcError`): `NegativeOrderTotalError` (via `computeOrderTotals`), `OrderDiscountNotSupportedError`.

Total: **17 new mapper-boundary test cases** (10 + 7), one per fixed site, each driving a REAL domain error through the REAL mapper.

### Item 4 — checked whether any other mapper rebuilds a message

Read every branch of every mapper in both repositories (all pass `e.Message`/`error.message` straight through, none reformat, none of the money-bearing sites live outside Billing/Orders):
- `src/Billing/Presentation/Rpc/BillingErrorMapper.cs` — every branch `e.Message`.
- `src/Orders/Presentation/Rpc/OrdersCreateErrorMapper.cs` — every branch `e.Message`, including `MapStockCheckBusinessError`'s `e.ResponderMessage`.
- `src/Fulfillment/Presentation/Rpc/StockErrorMapper.cs` — every branch `e.Message` (no money-bearing site here at all, per id 102's own enumeration).
- `apps/billing/src/presentation/rpc-error-mapper.ts`, `apps/orders/src/presentation/rpc-error-mapper.ts`, `apps/fulfillment/src/presentation/rpc-error-mapper.ts` — every branch `message: error.message` (`grep -n "message" <each file>`, verified no other pattern appears).

No other mapper needs the same test; the two service pairs above are the whole set.

### Arming table

Protocol: `cp` a backup, mutate, force a rebuild (`--no-incremental` for .NET; Vitest resolves `apps/*`'s own TS source directly, no `dist/` rebuild needed for these unit suites), run the named test, record the verbatim failure, restore from the backup, `cmp` byte-identical, force another rebuild, re-run green.

| # | Repo | Mutation | Result |
|---|---|---|---|
| A1 (Q2 re-armed) | #8 | `BillingErrorMapper.cs`'s `InvoicePaymentAmountMismatchError` case: `e.Message` → rebuilt raw `$"Payment amount {e.ReceivedMinorUnits} does not equal the invoice's totalAmount {e.ExpectedMinorUnits}."` — the reviewer's own Q2 mutation | **KILLED.** `BillingErrorMapperMoneyTextTests.InvoicePaymentAmountMismatchError_ReachesTheWireMessage_ScaledByTheCurrencysExponent`: `Assert.Equal() Failure: Strings differ … Expected: "Payment amount 92.45 EUR does not equal t"··· / Actual: "Payment amount 9245 does not equal the in"···`. Restored; `cmp` identical; rebuilt `--no-incremental`; `Billing.UnitTests` 278/278 green. |
| A2 (field corruption) | #8 | `InvoicePaymentAmountMismatchError.cs`: the received amount's `MoneyText.Format(receivedMinorUnits, currency)` → `MoneyText.Format(receivedMinorUnits, "JPY")` (wrong currency passed to the formatter) | **KILLED.** Same test: `Expected: "Payment amount 92.45 EUR does not equal t"··· / Actual: "Payment amount 9 245 JPY does not equal t"···`. Restored; `cmp` identical; rebuilt; `Billing.UnitTests` 278/278 green. |
| A3 (field corruption, Orders) | #8 | `PlaceOrderErrors.cs`'s `OrderDiscountNotSupportedError`: `MoneyText.Format(orderDiscountMinorUnits, currency)` → `MoneyText.Format(orderDiscountMinorUnits, "JPY")` (`currency` parameter pinned to an unread private field, arm-only, to keep the mutated build compiling under `TreatWarningsAsErrors`/CS9113) | **KILLED.** `OrdersCreateErrorMapperTests.Map_OrderDiscountNotSupportedError_ReachesTheWireMessage_ScaledByTheCurrencysExponent`: `Expected: "orderDiscount 1.50 EUR was supplied, but "··· / Actual: "orderDiscount 150 JPY was supplied, but t"···`. Restored; `cmp` identical; rebuilt; `Orders.UnitTests` 502/502 green. |
| A4 (Q4 re-armed) | #7 | `rpc-error-mapper.ts`'s `InvoicePaymentAmountMismatchError`/`InvoiceAlreadyPaidError`/`InvoicePaymentCurrencyMismatchError` branch: `message: error.message` → `error instanceof InvoicePaymentAmountMismatchError ? raw rebuild using error.received/error.expected : error.message` — the reviewer's own Q4 mutation, narrowed to the one class it names | **KILLED.** `rpc-error-mapper — money text reaches the wire message` describe block, `InvoicePaymentAmountMismatchError (Invoice.markPaid) …`: `Expected: "payment amount (20.01 EUR) does not m…" / Received: "payment amount (2001) does not match …"`. Restored; `cmp` identical; `billing` suite 164/164 green. |
| A5 (field corruption) | #7 | `invoice-errors.ts`'s `InvoicePaymentAmountMismatchError`: the received amount's `formatMoney(received, currency)` → `formatMoney(received, 'JPY')` | **KILLED.** Same test: `Expected: "payment amount (20.01 EUR) does not m…" / Received: "payment amount (2 001 JPY) does not m…"`. Restored; `cmp` identical; `billing` suite 164/164 green. |
| A6 (field corruption, Orders) | #7 | `order-errors.ts`'s `NegativeOrderTotalError`: `formatMoney(totalAmount.amount, totalAmount.currency)` → `formatMoney(totalAmount.amount, 'JPY')` | **KILLED.** `toRpcError — money text reaches the wire message`, `NegativeOrderTotalError …`: `Expected: "total amount would be negative: -1.00 …" / Received: "total amount would be negative: -100 J…"`. Restored; `cmp` identical; `orders` suite 544/544 green. |

The brief's minimum ("the reviewer's two mutations; one field corruption per repo") is arms A1, A4 (the reviewer's own two) plus A2, A5 (one field corruption per repo). A3 and A6 are the same field-corruption family applied to each repo's Orders site too, for symmetry with the Billing arm and because the new Orders tests are themselves countable claims that must be seen to fail (CLAUDE.md's "a task that makes a countable claim must be armed" rule).

### Doc-comment correction (review's "What must change", bullet 1's second half)

The review named two doc comments that overstated what the Gateway-only tests prove ("proves the WHOLE path"). Both corrected — not by removing the claim, but by naming precisely what each of the three layers (domain message, service mapper, Gateway pass-through) now separately proves:
- `tests/Gateway.UnitTests/ProblemDetailMoneyTextTests.cs` — added a `<remarks>` block.
- `apps/gateway/src/presentation/problem-detail-money-text.spec.ts` — rewrote the header comment.

Both are comment-only edits; both suites' counts are unchanged (`Gateway.UnitTests` 248/248, #7 `gateway` 141/141), confirmed by re-running after the edit.

### Defeat-list statement (`CLAUDE.md` rows 1–12), for this round's new tests/arms

- **Row 1 (delete/revert)** — arm A1 (#8) and A4 (#7) are exactly this, using the reviewer's own mutations.
- **Row 2 (corrupt a payload field the test supplied)** — arms A2/A3 (#8) and A5/A6 (#7): the currency argument passed to the formatter, corrupted to a real sibling currency (`JPY`) rather than deleted.
- **Row 3 (substitute a valid sibling identifier)** — the currency corruption arms substitute one real ISO-4217 code (`JPY`) for another (`EUR`/`BHD`) — the sibling family is `CurrencyExponent`'s table (id 100). Both directions were already probed by id 100's own D3 arm on the shared table itself; this round's arms probe a different call site (the mapper-boundary tests), not the table.
- **Rows 4–6 (text-shadowing, dead regions, raw strings)** — not applicable; every new test calls real code, none scans text.
- **Row 7 (drop an OPTIONAL element)** — not applicable; every mapper branch tested is a REQUIRED case (the error type is always thrown with a message).
- **Row 8 (literal compared to a literal)** — not applicable; every new test calls the real mapper function and compares its real return value to a literal expected string.
- **Row 9 (two-part claim, premise half stale)** — this round's own two-part claim is "the mapper does not rebuild the message" (guard) + "the currency source is real" (premise, carried over unchanged from the original entry's own currency-source table, itself unaffected by this round). Both halves checked: the guard is armed (A1–A6), and the premise was re-read from the mapper source directly (item 4's sweep above) rather than assumed.
- **Row 10 (build-output copy joins the population)** — not applicable; item 4's sweep is source-only (`src/`, `apps/*/src/`), no `bin`/`obj`/`dist` path matched.
- **Row 11 (form the instrument doesn't recognise)** — not applicable; plain function calls, no syntax sweep.
- **Row 12 (failure path the population never drives)** — this is the row this whole round exists to close: the domain-message-only and Gateway-only suites never drove the mapper hop itself. Arms A1–A6 drive exactly that path, at the mapper function boundary, for both repositories.

### Verify

**#8** (all runs `--no-incremental` before the confirming test run, per the arming protocol; `dotnet build OrderToCash.sln --no-incremental` also run standalone, 0 warnings/0 errors):

| Suite | Before this round | After | Reconciliation |
|---|---|---|---|
| `Billing.UnitTests` | 270 | **278** | +8 (`BillingErrorMapperMoneyTextTests.cs`) |
| `Orders.UnitTests` | 500 | **502** | +2 (2 new `[Fact]`s in `OrdersCreateErrorMapperTests.cs`) |
| `Gateway.UnitTests` | 248 | 248 | unchanged — comment-only edit |

**#7** (typecheck run standalone per project, all exit 0; eslint run on every touched file, exit 0/no output):

| Suite | Before this round | After | Reconciliation |
|---|---|---|---|
| `pnpm --filter billing test` | 159 | **164** | +5 (new describe block, `rpc-error-mapper.spec.ts`) |
| `pnpm --filter orders test` | 542 | **544** | +2 (new describe block, `rpc-error-mapper.spec.ts`) |
| `pnpm --filter gateway test` | 141 | 141 | unchanged — comment-only edit |

Total new test cases this round added: **10 in #8 (8 + 2), 7 in #7 (5 + 2) = 17** — matching the 17-site count above exactly (one test per site).

Not run: `./quality.sh` in either repository (per the brief). `apps/web` and `src/SharedKernel`/`tests/SharedKernel.UnitTests` (owned by the other implementer working concurrently, per the coordinator's mid-task addition) — neither touched, neither built.

### Concurrency note

Mid-task, the coordinator flagged that another implementer is also building #8 and both builds compile `src/SharedKernel`. Checked `pgrep -af "dotnet (build|test|format)|MSBuild.dll(?!.*nodemode)|testhost"` before every build/test in this round; idle `/nodemode:1` reuse nodes were present throughout and ignored. One live `dotnet build` from another process was observed once, mid-round (after all #8 arming and verification in this round had already completed) — no build/test of mine was run while it was in flight; this report was drafted (file edits only) until it cleared.

### Rules followed

Touched, #8: `tests/Billing.UnitTests/BillingErrorMapperMoneyTextTests.cs` (new), `tests/Orders.UnitTests/OrdersCreateErrorMapperTests.cs` (extended), `tests/Gateway.UnitTests/ProblemDetailMoneyTextTests.cs` (doc comment only). Touched, #7: `apps/billing/src/presentation/rpc-error-mapper.spec.ts` (extended), `apps/orders/src/presentation/rpc-error-mapper.spec.ts` (extended), `apps/gateway/src/presentation/problem-detail-money-text.spec.ts` (header comment only). No production code left changed by this round (arms A1–A6 all restored and `cmp`-verified identical to their pre-arm state). No `apps/web`, no `src/SharedKernel`, no `tests/SharedKernel.UnitTests`, no #7 `infra/docker/web`. No `specs/shared/` file. No `CLAUDE.md` edit. No `feature_list.json` edit. No git command that writes the index or working tree.

### Status

`id 102`'s `feature_list.json` status remains **unchanged** (`pending`) — this round did not authorise a transition.
