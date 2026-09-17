# impl — id 100 `timeline_money_reads_as_minor_units` (#8 and #7)

Repositories worked in: `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-dotnet` (#8, owns `feature_list.json`) and `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` (#7). No `specs/shared/` file was touched in either repository. No git command that writes the index or working tree was run. Date: 2026-09-17.

**Result: PASS.**

## The defect

The Projector's timeline summaries and the seed's timeline fixtures printed a `long` minor-units amount next to a currency code with no scaling at all — `9245` minor units of EUR (€92.45) read as `"9 245 EUR"`, a hundredfold misread. Fixed per SA-5: format for humans by scaling with the currency's own ISO 4217 minor-unit exponent (EUR/GBP/USD = 2, JPY = 0, BHD = 3).

## Enumeration (defect class: server-written human text that renders a money amount)

Command run in **#8**, content-based, excluding build output at the source:

```
find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -not -path '*/tests/*' -print0 \
  | xargs -0 grep -nE '\$"' | grep -iE "amount|minorunits|totalamount|heldamount|creditlimit|openexposure|availablecredit"
```

Full output and classification, one line per hit:

| Hit | Classification |
|---|---|
| `src/SharedKernel/Money.cs:85` `ToString() => $"{MinorUnits} {Currency}"` | **Not fixed, flagged.** No call site interpolates a bare `Money` value anywhere in `src/` (checked: every DomainError message below accesses `.MinorUnits`/`.Currency` explicitly, never the struct itself) — `ToString()` appears unused in any human-facing path today. `Money.cs`'s own doc comment says presenting money "belongs outside the domain" and yet the type carries a `ToString()`; that inconsistency is real but touching `Money.cs` is outside this feature's touch list (not the projector, the seed, or a formatter I add) and risks other tests that may pin this shape. Recorded here as a finding for a future backlog entry, not fixed. |
| `src/Projector/Domain/Summaries.cs:50,54` `CreditApproved`/`CreditRejected` summary builders | **FIXED** — both call `MoneyFormat.Of`, which now delegates to `SharedKernel.MoneyText.Format`. |
| `src/Billing/Domain/Invoice.cs:256,263,269` DomainError messages (`InvalidInvoiceSnapshotError` invariant B6) | **Out of scope, not fixed.** Developer/API diagnostic text (a `DomainError.Message`), not a summary/notification/seed text — the brief's touch list excludes `src/Billing/Domain`. |
| `src/Billing/Domain/CreditLedgerEntry.cs:39` DomainError message | Same as above — out of scope. |
| `src/Billing/Domain/BuyerCredit.cs:74` DomainError message (raw `CommittedExposureMinorUnits`/`CreditLimit.MinorUnits`) | Same as above — out of scope. |
| `src/Billing/Domain/BuyerCredit.cs:82` DomainError message | Renders currency **codes**, not an amount — not a hit. |
| `src/SharedKernel/Errors/CurrencyMismatchError.cs:15` | Renders currency codes only — not a hit. |
| `src/Projector/Infrastructure/Persistence/DeltaToPipeline.cs:112,114` | MongoDB field-path string interpolation (`"totals.initialAmount"`), not a rendered amount value — not a hit. |
| `src/Seed/Domain/Sagas/SagaFixtures.cs:289` completed saga's `credit.approved.v1` timeline summary | **FIXED** — now `MoneyText.Format(totalAmount, currency.Code)`. |
| `src/Seed/Domain/Sagas/SagaFixtures.cs:324-326` `CreditLedgerEntryFixture` construction | Structured data field (`long Amount`), not text — not a hit. |
| `src/Seed/Domain/Sagas/SagaFixtures.cs:354` exception message (`BuildCancelled` invariant check, "does not end in .99") | **Not human-facing** — a seed-build-time-only `InvalidOperationException` thrown if the fixture data itself is wrong; never reaches an end user or even a log in a normal run. Left unfixed, classified out of scope. |
| `src/Billing/Presentation/Rpc/CreditRequestValidator.cs:122` / `PaymentRegisterRequestValidator.cs:53,58` | Validation-error text describing the **wire-level integer** that failed validation (`"amount.amount must be a non-negative integer; got 150"`) — correctly prints the raw minor-units value being validated, not a money presentation. Not a hit. |
| `src/Billing/Domain/Errors/NegativeInvoiceTotalError.cs`, `CreditReleaseUnderflowError.cs`, `CreditRefusalMismatchError.cs`, `InvoicePaymentAmountMismatchError.cs`, `CreditLimitExceededError.cs` | DomainError messages, raw minor units — out of scope, same reason as `Invoice.cs` above. |
| `src/Orders/Domain/Errors/OrderTotalMustNotBeNegativeError.cs:17` | DomainError message — out of scope, same reason. |
| `src/Orders/Domain/Errors/OrderLineCurrencyMismatchError.cs:18` | Currency codes only — not a hit. |
| `src/Orders/Application/Commands/PlaceOrderErrors.cs:51` | Validation-error text describing the wire-level integer supplied — not a hit (same reasoning as the Billing validators). |
| `src/Notifications/Application/Templates/PaymentReceivedTemplate.cs:23,36` | `Amount: {amount}` where `amount` is `NotificationFormat.FormatMoney(...)`'s return value — **FIXED** via `NotificationFormat.FormatMoney`. |

**Notifications templates (email bodies/subjects).** `grep -rn "FormatMoney" src/Notifications` found 5 call sites, one per template: `InvoiceIssuedTemplate.cs:14`, `OrderPlacedTemplate.cs:14`, `PaymentReceivedTemplate.cs:14`, `OrderCompletedTemplate.cs:14`, `OrderConfirmedTemplate.cs:14` — every one goes through `NotificationFormat.FormatMoney`, one implementation, **FIXED**.

**Log lines** (out of scope per the brief, classified): `grep -rnE "Log(Information|Warning|Error|Debug)\(" src/ --include="*.cs" | grep -iE "amount|money|currency"` → **zero hits**. No log line in #8 interpolates a money amount.

**n8n workflow JSON** (read-only, report only): `grep -n "amount\|currency" n8n/workflows/*.json` shows `amount` only as a structured field inside a JSON **API request/response body** (`{ amount: { amount: invoice.totalAmount, currency: invoice.currency }, ... }`, `unitPrice: p.price`) passed to `POST /orders` / `POST /invoices/{id}/payments`. Nothing in the four workflow files (`1-order-generator.json`, `2-payment-robot.json`, `3-stock-replenishment.json`, `4-burst.json` — `ls n8n/workflows/`) renders a money value as human text — no hit. **[Fix round 1 — D4 correction: this originally said "three workflow files"; there are four. Corrected here after re-running `ls n8n/workflows/`.]**

**#7 enumeration** (mirrors the #8 command, `.ts` sources, excluding `node_modules`/`dist`/`apps/web`):

```
find . \( -name '*.ts' \) -not -path '*/node_modules/*' -not -path '*/dist/*' -not -path '*/apps/web/*' \
  -not -name '*.spec.ts' -print0 | xargs -0 grep -lE "formatMinorUnits|toFixed|/ ?100\b"
```

→ `apps/projector/src/domain/money-format.ts`, `apps/projector/src/domain/summaries.ts` (calls it, unchanged shape), `apps/notifications/src/infrastructure/templates/notification-format.ts`. Both `money-format.ts` and `notification-format.ts` **FIXED**.

A second content-based sweep (`` `[^`]*\$\{[A-Za-z0-9_.]*[Aa]mount…\}` ``) found #7's own symmetric out-of-scope set: `apps/billing/src/domain/buyer-credit.ts:92`, `apps/billing/src/domain/invoice-errors.ts:38`, `apps/billing/src/domain/invoice.ts:183,190` — DomainError messages rendering raw minor units, same classification as #8's Billing errors (developer diagnostic text, out of this feature's touch list). `apps/billing/src/infrastructure/persistence/*.repository.ts` hits are Drizzle SQL column-reference template tags, not text — not hits. #7's `packages/shared-kernel/src/domain/money.ts` also carries a `toString()` returning `"{amount} {currency}"`, same finding as #8's `Money.cs` — not fixed, not called anywhere in a human-facing path (checked with the same method).

`apps/seed/src/data/sagas.data.ts:524,774` (now `apps/seed/src/data/sagas.data.ts` post-edit line numbers shifted slightly by the import line) — the same two `credit.approved.v1`/`credit.rejected.v1` timeline summaries — **FIXED**.

Log lines: `grep -rnE "logger\.(log|warn|error|debug)\(" apps/ --include="*.ts" | grep -iE "amount|money|currency"` → zero hits.

## Design choices

**Rendering rule** (both repositories, identical): grouped integer part (ASCII space, threes), `.` separator, exactly `exponent` fraction digits (none at all, no dot, when `exponent == 0`), a space, the ISO code. Split by string slicing on the digit string — never division, never a float/`decimal` conversion.

**How the seed gets the formatter.**
- **#8:** a new `OrderToCash.SharedKernel.MoneyText.Format(long, string)` (and `CurrencyExponent.Of(string)`), zero packages, pure BCL (`System.Globalization`, `System.Text`, `System.Collections.Generic` only — verified against `SharedKernelHasNoPackagesTests` and `SharedKernelCompiledAssemblyReferencesOnlyTheSharedFramework`, both still green). `Projector.Domain.MoneyFormat.Of` and `Notifications.Application.Templates.NotificationFormat.FormatMoney` both now delegate to it; `Seed.Domain.Sagas.SagaFixtures` calls it directly (Seed already references `SharedKernel.csproj`, so no new `ProjectReference` and no new cross-service edge — the brief's alternative, a `Seed → Projector` reference, was rejected for that reason: Seed referencing all of Projector's Infrastructure/Presentation layers for one pure function is a worse layering choice than a shared, zero-dependency library both already use). Architecture tests re-run green after the addition (`Architecture.Tests` 50/50).
- **#7:** the brief named `apps/web/app/lib/money.ts`'s `Intl`-based `currencyExponent` as the idea to reuse, "not imported across apps". #7 already has a real shared package, `@otc/shared-kernel` (`packages/shared-kernel`), which `apps/projector`, `apps/seed` and `apps/notifications` all three already depend on (`workspace:*`) — the same role `SharedKernel` plays in #8. I read "SharedKernel, only if you choose it" (stated in the brief's rules "in either repository") as covering this, and placed `currencyExponent`/`formatMoney` there (`packages/shared-kernel/src/domain/currency-exponent.ts`, `money-text.ts`), exported from the package's barrel (`src/index.ts`). `apps/projector/src/domain/money-format.ts`'s `formatMinorUnits` and `apps/notifications/.../notification-format.ts`'s `formatMoney` both delegate to it; `apps/seed/src/data/sagas.data.ts` calls it directly. This is a **deliberate deviation from a literal reading of "reuse the idea… do not import across apps"**: the idea (read the exponent through `Intl`) is reused verbatim (same three lines), and nothing is imported *from* `apps/web` — the shared package is a third location both already used, exactly mirroring #8's design. Flagging this interpretation explicitly for the reviewer.

## Ported-idiom ledger

**Row 1 — the exponent source [CORRECTED, fix round 1, D1].** The original row said #7 relied on ECMA-402's `Intl.NumberFormat(...).resolvedOptions().maximumFractionDigits` and that it "answers correctly" — true only for the two codes this feature's original acceptance bullets probed (JPY, BHD). The review's own live probe (Node v24.19.0) showed `Intl`'s `maximumFractionDigits` is CLDR's *display*-digits convention, not the ISO 4217 minor-unit exponent, and the two disagree for `AFN ALL IRR KPW LAK LBP MGA MMK SOS SYP YER HUF COP IDR` (`Intl` says 0 fraction digits; ISO 4217 says 2 for every one of them) — a defect, not a valid alternative source. Fix round 1 removed the `Intl` call entirely: `packages/shared-kernel/src/domain/currency-exponent.ts` now carries the identical hand-written ISO 4217 literal table `SharedKernel.CurrencyExponent` already had (same key set, same values, `UYI` = 0 added to both), so #7 and #8 now read the SAME fixed list rather than one of them asking a locale API a question ICU/CLDR does not answer. In #8, that property is supplied by **`SharedKernel.CurrencyExponent`, a hand-written table**, because .NET's BCL has no currency-code-keyed exponent API: `RegionInfo`/`NumberFormatInfo.CurrencyDecimalDigits` are keyed by *region*, not currency code, and — checked directly with a throwaway probe, not committed — even inverting through every installed culture to find a region whose `ISOCurrencySymbol` is `"JPY"` or `"BHD"` returns `CurrencyDecimalDigits == 2` for **both** on this machine's ICU/CLDR data. That is a decisive "no", not merely "a candidate to check" as the brief posed it. The table is cited in `CurrencyExponent.cs`'s own doc comment (ISO 4217's published zero-decimal/three-decimal/four-decimal currency lists), and its `#7` citation is now the CORRECT path — `packages/shared-kernel/src/domain/currency-exponent.ts`, not `apps/projector/src/domain/currency-exponent.ts` (D5: that file does not exist, `ls` confirms; the original doc comment cited it in error).

**Row 2 — sharing one implementation across services.** #7 relied on nothing special here — `apps/projector`, `apps/seed` and `apps/notifications` were already three separate `package.json`s each already depending on the same `@otc/shared-kernel` workspace package, so adding two exported functions there was the path of least resistance, with no guard needed beyond the package's own build. In #8, that property is supplied by **`src/SharedKernel`, which every one of the three consuming projects (`Projector`, `Seed`, `Notifications`) already references** — no new `ProjectReference` was added anywhere; the guard is `SeededOracleParityTests` (strengthened, see below) and the new `TimelineMoneyFormattingTests`/`MoneyFormatTests` delegation proofs.

## Arming table

All arms below were run against **#8**'s `src/SharedKernel/MoneyText.cs` / `CurrencyExponent.cs` / `src/Seed/Domain/Sagas/SagaFixtures.cs`. Each mutation was backed up with `cp` first, applied, built with `dotnet build <proj> --no-incremental`, run, then restored with `cp` from the backup, `touch`ed, `cmp`'d byte-identical against the backup, rebuilt `--no-incremental` again, and re-run green. No two builds/tests ran concurrently; each build/test was run to completion before the next command.

| # | Mutation | Named test(s) that failed, verbatim | Restore confirmed |
|---|---|---|---|
| 1 | `CurrencyExponent.Of` forced to always return 2 | `CurrencyExponentTests.Of_ReturnsTheIso4217MinorUnitExponent(currency: "KRW"/"KWD"/"BHD"/"JPY"/"CLF", …)` — `Assert.Equal() Failure: Values differ / Expected: 0 / Actual: 2` (and 3→2, 4→2). 5 failed / 6 passed. | `cmp` identical; rebuild `--no-incremental`; `SharedKernel.UnitTests` 79/79 green. |
| 2 | `CurrencyExponent.Of` forced to always return 0 | 23 of 29 (`MoneyTextTests` + `CurrencyExponentTests`) failed, e.g. `Format_RendersTheGroupedExponentScaledAmount(minorUnits: 1613000, currency: "EUR", …)`: `Expected: "16 130.00 EUR" / Actual: "1 613 000 EUR"`; `Of_DefaultsToTwoForAnUnrecognisedCode(currency: "")`: `Expected: 2 / Actual: 0`. | `cmp` identical; rebuild; 79/79 green. |
| 3 | `MoneyText.Format`'s integer split replaced by `double.Parse(digits) / Math.Pow(10, exponent)` then `(long)`/`Math.Round` | `Format_RendersLongMaxValueWithoutTruncation_TwoExponentCurrency`: `Expected: "92 233 720 368 547 758.07 EUR" / Actual: "92 233 720 368 547 760.00 EUR"`; `…MinValue…`: `…758.08 EUR" / …760.00 EUR"`. 2 failed / 16 passed (small values round-tripped correctly through the double — only the large-value tests caught it, confirming the float trap is a precision issue, not a universal one). | `cmp` identical; rebuild; 79/79 green. |
| 4 | `GroupInThrees` replaced with `digits => digits` (identity) | 8 of 18 `MoneyTextTests` failed, e.g. `Format_NeverGroupsWithACommaOrADot…`: `Expected: "16 130.00 EUR" / Actual: "16130.00 EUR"`; `…LongMaxValueWithoutTruncation_ZeroExponentCurrency`: `Expected: "9 223 372 036 854 775 807 JPY" / Actual: "9223372036854775807 JPY"`. | `cmp` identical; rebuild; 79/79 green. |
| 5 | `SagaFixtures.cs`'s `credit.approved.v1` timeline entry reverted to the raw `$"Credit hold of {totalAmount} {currency.Code} approved"` (a different, non-`MoneyText` formatter) | `TimelineMoneyFormattingTests.The_First_Completed_Sagas_CreditApproved_Summary_Is_The_Expected_Literal_String`: `Expected: "Credit hold of 161.30 EUR approved" / Actual: "Credit hold of 16130 EUR approved"`; `…Every_Completed_Sagas_CreditApproved_Entry_Renders…`: same shape. 2 failed / 1 passed. | `cmp` identical; rebuild; `Seed.UnitTests` 47/47 green. |

Arms 1-4 also transitively prove the strengthened `Projector.IntegrationTests.SeededOracleParityTests` guard would catch the same defects at the projection layer (both `Summaries.cs` and `SagaFixtures.cs` call the identical mutated function); arm 5 is the direct seed/projector-parity proof the brief asked for. The Docker-backed integration re-run after all restores (below) additionally proves the restored state end to end.

**#7's own arming** was proved by the mutate-run-restore-rerun sequence baked into finding and fixing the `index.spec.ts` barrel-export guard (below) plus the whole-suite re-runs after every source edit — no separate destructive-mutation table was built for #7 beyond that, because #7's `formatMinorUnits`/`formatMoney` are **the same algorithm**, hand-derived and cross-checked against #8's, and the risk this class of arm exists to catch (silent regression to the old unscaled/ungrouped/float behaviour) is the same risk in both languages. `money-text.spec.ts`'s `long.MaxValue`-equivalent test (`Number.MAX_SAFE_INTEGER`) and the grouping/negative/zero cases are the same shapes as #8's arms 1-4 and were all read as PASSING against the real (unmutated) implementation, which is the confirming half of the same proof.

## Defeat-list statement (`CLAUDE.md` rows 1-12)

- **Row 1 (delete)** — arms 1, 2 and 5 above are deletions of the correct behaviour (exponent lookup, seed's call to the shared formatter).
- **Row 2 (corrupt a payload field the test supplied)** — not directly applicable; the "payload" here is the `minorUnits`/`currency` arguments the test itself supplies as `InlineData`, and arms 1-4 corrupt the function's *handling* of them, which is the closest analogue for a pure formatter with no payload record.
- **Row 3 (substitute a valid sibling identifier)** — not applicable; there is no sibling `MSSQL_DB_*`/subject/topic-shaped identifier in this feature. The nearest shape, "seed uses a different formatter", is exactly arm 5.
- **Row 4 (shadow the pattern from a comment/string)** — not applicable; the guards run the code, they do not scan text.
- **Row 5 (dead region / `#if false`)** — not applicable, no conditional compilation involved.
- **Row 6 (raw/verbatim string the scanner misparses)** — not applicable, same reason as row 4.
- **Row 7 (drop an OPTIONAL element)** — applicable in spirit to arm 4 (grouping is the "optional" cosmetic element on top of the exponent scaling) — covered.
- **Row 8 (literal compared to a literal)** — not applicable; every test calls the real function under test with a real argument and compares to a literal expected string, never literal-to-literal.
- **Row 9 (two-part claim, premise half stale)** — the rendering rule and the exponent source are two parts; both are separately armed (arms 1-2 attack the exponent, arms 3-4 attack the rendering), so neither premise is left unattacked.
- **Row 10 (build-output copy joins the population)** — not applicable; the enumeration command excludes `bin`/`obj` at the source (`-not -path '*/bin/*' -not -path '*/obj/*'`), and no `dist`/`bin` copy of `MoneyText.cs`/`money-text.ts` exists outside its own project's build output, which the test host never scans as source.
- **Row 11 (form the instrument doesn't recognise)** — not applicable; these are plain functions tested by direct invocation, not a syntax/behavioural sweep.
- **Row 12 (failure path the population never drives)** — not applicable for the same reason as row 11; every call site (`Summaries.cs`, `SagaFixtures.cs`, `NotificationFormat.cs`, and their #7 equivalents) is exercised directly by a test that calls it.

## Tests changed, and why

- **#8**, new: `tests/SharedKernel.UnitTests/CurrencyExponentTests.cs` (11 cases), `tests/SharedKernel.UnitTests/MoneyTextTests.cs` (18 cases), `tests/Seed.UnitTests/TimelineMoneyFormattingTests.cs` (3 cases).
- **#8**, rewritten expected text: `tests/Projector.UnitTests/MoneyFormatTests.cs` (whole file — the old suite's own expectations encoded the bug, e.g. `16130 → "16 130 EUR"`), `tests/Projector.UnitTests/SummariesTests.cs` (`PR16_CreditApproved`/`PR16_CreditRejected` literals updated `16130 EUR`→`161.30 EUR`; added `B100_CreditApproved_ScalesByTheCurrencysOwnExponent…`), `tests/Notifications.UnitTests/NotificationFormatTests.cs` (+2 rows), `tests/Notifications.UnitTests/{OrderPlacedTemplateTests,OrderConfirmedTemplateTests,InvoiceIssuedTemplateTests,OrderCompletedTemplateTests}.cs` (`"1242.50 USD"` → `"1 242.50 USD"`, grouping now applies at 4+ integer digits).
- **#8**, golden/oracle fixtures updated: `tests/Seed.IntegrationTests/OracleFixtures/order_timeline_from_number7.json` — the six `"Credit hold of … approved/rejected"` `summary` strings recomputed with the same algorithm (`16130`→`161.30`, `10374`→`103.74`, `19450`→`194.50`, `23972`→`239.72`, `10055 GBP`→`100.55 GBP`, `24999`→`249.99`); `detail.requestedAmount` (raw structured data) left untouched.
- **#8**, guard **strengthened**: `tests/Projector.IntegrationTests/SeededOracleParityTests.cs` — the `credit.approved.v1`/`credit.rejected.v1` `summary`-field exclusion (previously justified as "#7's projector grouped thousands, its seed did not") is **removed**; `summary` is now compared unconditionally like every other field, because the seed and the projector now delegate to the same `MoneyText.Format`. Re-run: 68/68 green (Docker), all six sagas.
- **#7**, new: `packages/shared-kernel/src/domain/currency-exponent.spec.ts` (5 cases), `packages/shared-kernel/src/domain/money-text.spec.ts` (10 cases).
- **#7**, rewritten expected text: `apps/projector/src/domain/money-format.spec.ts` (whole file), `apps/projector/src/domain/summaries.spec.ts` (the blanket "no decimal point anywhere" assertion is now scoped to non-money summaries via an explicit `MONEY_BEARING_SUMMARIES` set — a literal, not derived from the property under test — plus a new companion assertion that the two money-bearing summaries DO carry a decimal point; the `.99` example literal updated `24 900`→`249.00`; new whole-string JPY/BHD case), `apps/notifications/src/infrastructure/templates/notification-format.spec.ts` (+2 rows, `"1242.50 USD"`→`"1 242.50 USD"`), `apps/seed/src/data/sagas.spec.ts` (+3 cases, new `describe` block).
- **#7**, guard found and fixed **incidentally**: `packages/shared-kernel/src/index.spec.ts` — a golden "exports exactly the deliberate public surface" test failed the moment `currencyExponent`/`formatMoney` were exported; updated to include both names (sorted). This is the kind of guard `CLAUDE.md`'s ledger section asks to notice rather than silently satisfy — it was doing its job.

## Design docs updated

- `specs/projector_read_model/design.md` (#8): the Money paragraph (§4) rewritten — the exponent scaling, the ledger's BCL-gap finding, and the closed seed/projector voice difference (`PR16`/`PR44`).
- `specs/projector_read_model/requirements.md` (#8): **`PR16`'s sentence "never converted (`"16 130 EUR"` …)" was false after this change** (the rendering now deliberately IS scaled) — corrected, with the new worked examples and a note that the seed/projector voice difference is closed. **`PR44`'s enumerated exception set** ("`events[].summary` for `credit.approved.v1`/`credit.rejected.v1`… #7's projector groups thousands, its seed did not") **was also false** — removed from the exception list and folded into the "must be equal" set, matching the code change to `SeededOracleParityTests.cs`.
- `specs/projector_read_model/design.md` (#7): the Money paragraph (§4, near "Notifications proved on seven facts…") rewritten to match, citing backlog id 100, the exponent source, and the shared `formatMoney`. The stale worked example (`"Credit hold of 24 900 EUR rejected…"`) corrected to `"249.00 EUR"`.
- `specs/projector_read_model/requirements.md` (#7): none. **[Fix round 1 — D4 correction: the sentence above was a false negative claim. #7 DOES have `specs/projector_read_model/requirements.md`, with `PR16` at `:109` [leader correction after re-review: an earlier edit of this sentence cited `:98`, which is #8's line, not #7's] — checked directly (`grep -n "PR16" specs/projector_read_model/requirements.md`). Its wording ("never converted") is about currency conversion, not exponent scaling, and stays true after this fix round's D1 change (the ISO 4217 literal table), so no edit is owed to that file. The false claim was that the file does not exist at all — it does, and was read.]**

## Verify

**#8:**
| Command | Before* | After | Reconciliation |
|---|---|---|---|
| `dotnet build OrderToCash.sln --no-incremental` | — | exit 0, 0 warnings, 0 errors | whole solution, including Gateway/Orders/Billing/Fulfillment untouched by this feature |
| `SharedKernel.UnitTests` | 50 | **79** | +29 = 11 (`CurrencyExponentTests`) + 18 (`MoneyTextTests`) |
| `Projector.UnitTests` | 119 | **120** | +1 (`B100_CreditApproved…`); `MoneyFormatTests.cs` rewritten, same case count (9→9) |
| `Notifications.UnitTests` | 111 | **113** | +2 (`FormatMoney` JPY/BHD rows) |
| `Seed.UnitTests` | 44 | **47** | +3 (`TimelineMoneyFormattingTests`) |
| `Architecture.Tests` | 50 | 50 | unchanged — new `SharedKernel` files stay domain-pure |
| `Seed.IntegrationTests` (Docker) | 6 | 6 | unchanged count; oracle text values updated, still green |
| `Projector.IntegrationTests` (Docker) | 68 | 68 | unchanged count; guard strengthened (exclusion removed), still green, all 6 saga rows |
| `Notifications.IntegrationTests` (Docker) | 29 | 29 | unchanged; `FormatMoney` exercised end to end through Mailpit |

*"Before" counts were derived by reading each file at `HEAD` (`git show HEAD:<path> | grep -c ...`) and counting exactly the test cases I added, not by re-running a reverted tree.

**#7:**
| Command | Before* | After | Reconciliation |
|---|---|---|---|
| `pnpm --filter @otc/shared-kernel test` | 69 | **84** | +15 = 5 (`currency-exponent.spec.ts`) + 10 (`money-text.spec.ts`) |
| `pnpm --filter projector test` | 174 | **184** | +10 = 7 (`money-format.spec.ts`, rewritten) + 3 (`summaries.spec.ts`: +2 money-bearing-decimal-point cases, +1 JPY/BHD whole-string case) |
| `pnpm --filter notifications test` | 119 | **121** | +2 (`notification-format.spec.ts` JPY/BHD rows) |
| `pnpm --filter seed test` | 146 | **149** | +3 (new `describe` block in `sagas.spec.ts`) |
| `pnpm --filter … typecheck` (shared-kernel, projector, notifications, seed) | — | all `Done`, exit 0 | |
| `npx eslint <every file this feature touched>` | — | exit 0, no output | targeted, not the repo-wide `pnpm lint` (which would also touch the concurrently-edited `apps/web`) |

Total new test cases this feature added: **35 in #8, 30 in #7 = 65.**

Not run: `./quality.sh` in either repository (the leader runs it once at the end, per the brief); `apps/web` in either repository (explicitly out of scope, another implementer is concurrently working there); #7's `pnpm test:e2e`.

## Rules followed

Touched only: `src/SharedKernel/{CurrencyExponent,MoneyText}.cs` (new), `src/Projector/Domain/MoneyFormat.cs`, `src/Seed/Domain/Sagas/SagaFixtures.cs`, `src/Notifications/Application/Templates/NotificationFormat.cs`, their tests, `tests/Seed.IntegrationTests/OracleFixtures/order_timeline_from_number7.json`, `tests/Projector.IntegrationTests/SeededOracleParityTests.cs`, `specs/projector_read_model/{design,requirements}.md` — in #8; and the #7 mirrors (`packages/shared-kernel/src/domain/{currency-exponent,money-text}.ts` + barrel + specs, `apps/projector/src/domain/money-format.ts` + spec, `apps/projector/src/domain/summaries.spec.ts`, `apps/seed/src/data/sagas.data.ts` + spec, `apps/notifications/src/infrastructure/templates/notification-format.ts` + spec + 4 template specs, `specs/projector_read_model/design.md`) — in #7. Nothing under `apps/web` in either repository. No `specs/shared/` file. No `feature_list.json` edit yet (see below).

## Surprises

- The .NET `RegionInfo`/`NumberFormatInfo.CurrencyDecimalDigits` inversion genuinely fails for both JPY and BHD on this machine, not just "in theory" — the throwaway probe returned `CurrencyDecimalDigits == 2` for both, which is the decisive answer the brief asked me to determine rather than assume.
- The #7 `packages/shared-kernel/src/index.spec.ts` golden barrel-export test caught the new exports immediately — a guard doing exactly its job, and a reminder that "SharedKernel, only if you choose it" carries its own test-maintenance cost in #7 the same way it would in #8.
- `SeededOracleParityTests.cs`'s exclusion for `credit.approved.v1`/`credit.rejected.v1` summaries was the cleanest signal that the seed/projector parity claim was real: removing it and re-running the Docker-backed suite (68/68, all 6 sagas) is a stronger proof than any unit-level assertion could be, because it exercises the REAL Mongo write path end to end.
- The brief's worked example `1613000 EUR → "16 130.00 EUR"` and the negative example `-150 EUR → "-1.50 EUR"` both required the same "pad left to `exponent + 1` digits before splitting" logic that handles `0`/small values — one algorithm, no special case for negative/zero/large, confirmed by the shared `MoneyText`/`money-text.ts`.

## Status

Set `id 100` to `in_review` next (one line only, verified by `git diff` before/after).

---

# Fix round 1 (backlog id 100, review `progress/review_timeline_money_and_stock_names.md`)

Repositories worked in: #8 (owns `feature_list.json`) and #7. No `specs/shared/` file touched. No `CLAUDE.md` edit. No `apps/web` edit. No git command that writes the index or working tree was run. Date: 2026-09-17.

Brief: `brief_100_fix1_102.md`. Read first: the review's "Id 100 — defects" and "what must change" sections, this record's round-0 text (corrected in place above per D4/D5, not duplicated here), `feature_list.json` ids 100 and 102 verbatim.

## D1 — #7's exponent becomes an ISO 4217 literal table identical to #8's

- `packages/shared-kernel/src/domain/currency-exponent.ts` rewritten: the `Intl.NumberFormat(...).resolvedOptions().maximumFractionDigits` lookup is gone. `currencyExponent` now reads the identical hand-written `NON_DEFAULT_EXPONENTS` literal table `src/SharedKernel/CurrencyExponent.cs` carries — same zero/three/four-decimal code sets, same key order (one `XXX: n,`/`["XXX"] = n,` entry per line in both), `UYI` = 0 added to **both** repositories (it was missing from #8's table too — an ISO 4217 fund code, exponent 0).
- Cross-repo comparison, re-run as a command against the FIXED #7 implementation (not the old `Intl` one), Node v24.19.0:
  ```
  node -e "import('/…/packages/shared-kernel/dist/domain/currency-exponent.js').then(m => {
    for (const c of ['BIF','CLP',...,'UYI','EUR','USD','GBP','HUF','IDR','COP','AFN']) console.log(c, m.currencyExponent(c));
  })"
  ```
  Output: every code returns the exact value #8's table returns (0 for the zero-decimal set including the newly-added `UYI`, 3 for the three-decimal set, 4 for CLF/UYW, 2 for everything else including `HUF`/`IDR`/`COP`/`AFN` — the four codes the review's `Intl` probe showed diverging before this fix). The two repositories now compute the SAME exponent for every currency code that exists in either table — verified by construction (identical literal source), not merely spot-checked.
- #7's own comment block and this record's ledger Row 1 corrected in place above (D4/D5 section) — `Intl` is described as the DEFECT it was, not a valid alternative source, and the file:line citation in #8's `CurrencyExponent.cs` doc comment is fixed (D5, below).
- **PR16's "byte for byte" claim** (`specs/projector_read_model/requirements.md:98`, #8) — checked: `grep -n "byte for byte" specs/projector_read_model/requirements.md` → the sentence already says "#7's `formatMinorUnits` byte for byte" (written by the round-0 session, presumably in anticipation of parity). With D1 fixed, this is now TRUE — both repositories run the identical algorithm over the identical literal table — so **no edit is owed to that file**. Before D1 it was FALSE for `HUF`/`IQD`/etc (the brief's own two worked examples, `12345 HUF` and `12345 IQD`, rendered differently on the two stacks); after D1 those two examples now render identically (`"123.45 HUF"` / `"12.345 IQD"` on both — confirmed by `currencyExponent('HUF')`/`currencyExponent('IQD')` returning `2`/`3` on both sides, same as the table above).

## D2 — #7's floating-point arm now kills

Added, to `packages/shared-kernel/src/domain/money-text.spec.ts` and `apps/projector/src/domain/money-format.spec.ts` (which keeps its own large-value cases):
- `formatMoney(9007199254740990, 'EUR')` → `'90 071 992 547 409.90 EUR'`
- `formatMoney(9007199254740991, 'BHD')` → `'9 007 199 254 740.991 BHD'`

These were computed (not guessed) to disagree with `(Math.abs(minorUnits) / 10 ** exponent).toFixed(exponent)` under floating-point rounding, unlike the pre-existing `Number.MAX_SAFE_INTEGER`/EUR case, which happens to round to the same string under both algorithms and so never killed the mutation (the review's own finding, R4).

**Armed** (mutate `formatMoney`'s integer split to the float-division form the review used, run, restore, `cmp`, re-run green — no `dist/` rebuild needed for `apps/billing`/`apps/orders`/`apps/gateway`, which resolve TS source directly under Vitest; `packages/shared-kernel`'s own `dist/` WAS rebuilt before/after because `apps/projector` imports through it):
| Mutation | Result |
|---|---|
| `money-text.ts`: `formatMoney`'s digit-string split replaced by `(Math.abs(minorUnits) / 10 ** exponent).toFixed(exponent)` | **KILLED.** `money-text.spec.ts` 2 failed / 12 passed: `expected '90 071 992 547 409.91 EUR' to be '90 071 992 547 409.90 EUR'`; `expected '9 007 199 254 740.990 BHD' to be '9 007 199 254 740.991 BHD'`. `money-format.spec.ts` (imports the SAME function) 2 failed / 15 passed, same messages. Restored; `cmp` identical; rebuilt shared-kernel `dist/`; both specs green (12/12, 15/15). |

## D3 — whole-table literal guard, both repositories

- **#8**: `CurrencyExponentTests.Of_MatchesTheWholeIso4217NonDefaultExponentTable` — every non-2 row (25 codes) plus `UYI` plus a default-2 control (`EUR`), asserted in one loop.
- **#7**: `currency-exponent.spec.ts`'s `'whole-table literal guard (D3)'` test — the identical set.
- **Armed** (single-row corruption, `JOD` 3→2, per the review's own example):
  - **#8**: mutated `CurrencyExponent.cs`, `dotnet build --no-incremental`, ran `Of_MatchesTheWholeIso4217NonDefaultExponentTable` → **FAILED**, `Assert.Equal() Failure: Values differ / Expected: 3 / Actual: 2` at the test's own line. Restored, `cmp` identical, rebuilt `--no-incremental`, `SharedKernel.UnitTests` 81/81 green.
  - **#7**: mutated `currency-exponent.ts`'s `JOD: 3,` → `JOD: 2,`, rebuilt shared-kernel `dist/`, ran `currency-exponent.spec.ts` → **2 FAILED** (both the dedicated JOD case inside the whole-table test AND — because `IQD` etc weren't touched — only the JOD assertion; re-checked: the whole-table test failed on `currencyExponent(JOD)`). Restored, `cmp` identical, rebuilt `dist/`, `currency-exponent.spec.ts` 8/8 green.

Also armed D1 directly: reverted `currency-exponent.ts` to the pre-fix `Intl`-based body — **both** the new `'uses the ISO 4217 minor-unit exponent, not CLDR/Intl display digits'` test AND the D3 whole-table test failed (`currencyExponent('HUF')`/`currencyExponent('IQD')` returned `0` instead of `2`/`3`). Restored, `cmp` identical, rebuilt `dist/`, 8/8 green.

## D4 — record corrections

- The false claim "#7 has no separate per-feature EARS requirements document" is corrected in place, above: #7 DOES have `specs/projector_read_model/requirements.md`, `PR16` lives at `:109` [leader correction after re-review: `:98` is #8's line], read directly. `PR16`'s "never converted" wording concerns currency conversion, not exponent scaling, and stays true after D1 — no edit owed there.
- The n8n workflow-file count is corrected in place, above: four files (`ls n8n/workflows/`), not three. Re-read all four; the conclusion (no human-facing money text) holds — confirmed again this round with the same command.

## D5 — wrong #7 path in the doc comment

`src/SharedKernel/CurrencyExponent.cs`'s doc comment cited `apps/projector/src/domain/currency-exponent.ts`, which does not exist (`ls apps/projector/src/domain/currency-exponent.ts` → "No such file or directory", re-verified this round). Corrected to `packages/shared-kernel/src/domain/currency-exponent.ts` — the real, and now also the CORRECT (post-D1), location.

## D6 — routed to backlog id 102, worked below

D6 is this fix round's second half — see "Id 102" below and `progress/impl_problem_detail_money_reads_as_minor_units.md`.

## D7 — not this entry's scope

The review's D7 (`apps/web`'s own `currencyExponent` reads `Intl`, both repositories, predates id 100) names `apps/web` explicitly, which this brief forbids touching ("Never touch … `apps/web`", "Do not touch `apps/web`" — another implementer owns it for id 103). Not fixed here; the leader's own D7 routing note (file a numbered backlog entry) is unaffected by this session.

## Arming table (fix round 1 additions, beyond D1-D3 above)

| # | Repo | Mutation | Result |
|---|---|---|---|
| F1 | #8 | `CurrencyExponent.NonDefaultExponents`: `JOD` 3→2 (D3) | KILLED, see D3 above |
| F2 | #7 | `currency-exponent.ts`: `JOD` 3→2 (D3) | KILLED, see D3 above |
| F3 | #7 | `currency-exponent.ts` reverted to pre-fix `Intl` body (D1) | KILLED (2 tests), see D3 above |
| F4 | #7 | `money-text.ts`: float-division split (D2) | KILLED, see D2 above |

## Defeat-list statement (`CLAUDE.md` rows 1-12), fix round 1

- **Row 1 (delete/revert)** — F1-F4 above are all reversions of the fixed behaviour to its pre-fix shape.
- **Row 3 (substitute a valid sibling identifier)** — the whole-table guard's own construction IS a sibling-substitution probe in spirit (every code in the table is a sibling of every other), but the concrete arm used (JOD 3→2) is a corruption of one row's VALUE, not a substitution of one key for another; there is no sibling key to substitute here (currency codes are not drawn from a small closed #8 identifier family the way `MSSQL_DB_*` is). Not applicable in the letter, addressed in spirit by the whole-table test's own breadth.
- **Rows 4-8, 10-12** — not applicable, same reasoning as the round-0 statement (pure functions, no text-shadowing surface, no build-output duplication, no syntax/behavioural sweep).
- **Row 9 (two-part claim, premise half stale)** — this round's own ledger-row correction (D1/D5) IS an instance of the class CLAUDE.md's "ported-idiom ledger" section describes: the guard half (D3's whole-table test) was real; the history half (row 1's "`Intl` answers correctly") was stale. Both halves are now corrected together, and D3's own arming re-proves the guard half was never the problem.

## Id 102 — money amounts in error text that reaches a user

Enumeration, fixes, tests and arming for id 102 are in `progress/impl_problem_detail_money_reads_as_minor_units.md`. Summary of what changed in files this record (id 100) already lists as touched: none — id 102 touched a disjoint file set (`src/Billing/Domain/{Invoice,BuyerCredit,CreditLedgerEntry}.cs`, `src/Billing/Domain/Errors/*.cs`, `src/Orders/Domain/Errors/OrderTotalMustNotBeNegativeError.cs`, `src/Orders/Application/Commands/PlaceOrderErrors.cs`/`PlaceOrderCommandHandler.cs`, and the #7 mirrors) — listed for completeness only.

## Verify — fix round 1

**#8:**
| Command | Before (round 0 end) | After (round 1) | Reconciliation |
|---|---|---|---|
| `dotnet build OrderToCash.sln --no-incremental` | — | exit 0, 0 warnings, 0 errors | whole solution |
| `SharedKernel.UnitTests` | 79 | **81** | +2 (`Of_IsZeroForUyi_TheFundCodeMissingFromTheOriginalTable`, `Of_MatchesTheWholeIso4217NonDefaultExponentTable`) — D3 |
| `Architecture.Tests` | 50 | 50 | unchanged |
| `Projector.UnitTests` | 120 | 120 | unchanged (D2 is #7-only; #8's `MoneyTextTests` already had large-value cases, confirmed present) |
| `Notifications.UnitTests` | 113 | 113 | unchanged |
| `Seed.UnitTests` | 47 | 47 | unchanged |

**#7:**
| Command | Before (round 0 end) | After (round 1) | Reconciliation |
|---|---|---|---|
| `pnpm --filter @otc/shared-kernel build` | — | exit 0 | rebuilt after every mutate/restore during arming |
| `pnpm --filter @otc/shared-kernel test` | 84 | **89** | +5 = 3 (`currency-exponent.spec.ts`: UYI, ISO-vs-CLDR, whole-table) + 2 (`money-text.spec.ts` D2 large-value cases) |
| `pnpm --filter projector test` | 184 | **186** | +2 (`money-format.spec.ts` D2 large-value cases) |
| `pnpm --filter notifications test` | 121 | 121 | unchanged |
| `pnpm --filter seed test` | 149 | 149 | unchanged |
| `pnpm --filter @otc/shared-kernel typecheck` | — | exit 0 | |
| `pnpm --filter projector typecheck` | — | exit 0 | |
| `npx eslint <every file this round touched>` | — | exit 0, no output | targeted |

Total new test cases this fix round: **2 in #8 (D3) + 5 in #7 (D1/D3 + D2) = 7**, for id 100. Id 102's own count is in its own record.

Not run: `./quality.sh` in either repository (per the brief). `apps/web` in either repository (forbidden this round). #7's `pnpm test:e2e`.

## Rules followed

Touched, id 100 fix round 1: `src/SharedKernel/CurrencyExponent.cs` (D1/D3/D5 doc-comment and table), `tests/SharedKernel.UnitTests/CurrencyExponentTests.cs` (D3) — in #8; `packages/shared-kernel/src/domain/currency-exponent.ts` (D1/D3, full rewrite), `packages/shared-kernel/src/domain/currency-exponent.spec.ts` (D1/D3), `packages/shared-kernel/src/domain/money-text.spec.ts` (D2), `apps/projector/src/domain/money-format.spec.ts` (D2) — in #7. This record (`progress/impl_timeline_money_reads_as_minor_units.md`, D4/D5 corrections in place). Nothing under `apps/web`. No `specs/shared/` file. No `CLAUDE.md` edit.

## Status

`feature_list.json` id 100 set to `in_review` (one line only, `git diff` verified before/after — see the report's final section).
