import nonDefaultExponentsTable from './currency-exponents.json';

/**
 * ISO 4217's minor-unit exponent for a currency code — SA-5
 * (specs/shared/openapi.yaml's Money section): "Formatting for humans
 * happens ... from the currency's ISO 4217 minor-unit exponent (EUR, GBP
 * and USD are 2; JPY is 0; BHD is 3). No response carries that exponent: it
 * is a property of the currency code itself."
 *
 * Backlog id 103 (D1/D7 of review_timeline_money_and_stock_names.md): this
 * used to read `Intl.NumberFormat(...).resolvedOptions().maximumFractionDigits`,
 * which is ICU/CLDR's DISPLAY precision, not the ISO 4217 minor-unit
 * exponent — the two disagree for about fifteen codes (IQD: `Intl` says 0,
 * ISO says 3; HUF, IDR, COP and eleven others: `Intl` says 0, ISO says 2),
 * so amounts in those currencies were both mis-FORMATTED and mis-PARSED
 * (an HUF input accepted 0 decimals instead of 2). `Intl` only happened to
 * answer correctly for the two currencies this project's earlier tests
 * probed (JPY, BHD).
 *
 * Fixed by carrying the identical ISO 4217 non-default-exponent set
 * `src/SharedKernel/CurrencyExponent.cs` (backend) and #7's
 * `packages/shared-kernel/src/domain/currency-exponent.ts` also carry — the
 * zero-, three- and four-decimal currency sets the ISO 4217 maintenance
 * agency publishes (and that are commonly reproduced, e.g. Wikipedia's "ISO
 * 4217" article, Active codes table, "Minor unit" column). Every currency
 * not listed defaults to 2.
 *
 * Backlog id 103, fix round 1: this used to duplicate the table as a second
 * hand-written TS object literal, guarded by a test
 * (`currency-exponent.parity.test.ts`) that parsed the backend's C# source
 * as text with a regex. That parser was beaten three ways in review — a C#
 * block comment, an `#if false` region, and two rows written on one line
 * (CLAUDE.md's defeat-list rows 4, 5 and 11) — because none of those change
 * what the .NET compiler actually builds, and a test that never asks the
 * compiler could not see them. The instrument changed instead of hardening
 * the parser (CLAUDE.md defeat-list row 11: "when a syntax guard keeps
 * losing, test the behaviour instead"). `./currency-exponents.json` is now
 * the ONE committed table this repository keeps — this module imports it
 * directly, so there is no second copy here for a divergence to hide in —
 * and `tests/SharedKernel.UnitTests/CurrencyExponentWebParityTests.cs`
 * compares the backend's COMPILED `CurrencyExponent.NonDefaultExponents`
 * dictionary against this same JSON file, in both directions, naming every
 * differing code. Comparing compiled state instead of parsed text is why a
 * comment or a dead region can no longer fool the guard: the compiler
 * already discarded that text before the test ever runs.
 */
const DEFAULT_EXPONENT = 2;

/** Currencies whose ISO 4217 minor-unit exponent is NOT 2 — every other code defaults to {@link DEFAULT_EXPONENT}. Read from `./currency-exponents.json`, the single committed table this repository keeps; see `tests/SharedKernel.UnitTests/CurrencyExponentWebParityTests.cs` (backend) and `currency-exponent.test.ts` (this file does not silently diverge from the JSON it imports). */
export const NON_DEFAULT_EXPONENTS: Readonly<Record<string, number>> = nonDefaultExponentsTable;

/** The ISO 4217 minor-unit exponent of `currency` (EUR → 2, JPY → 0, BHD → 3). An unrecognised or malformed code defaults to 2 — the common case, and what every currency this system seeds uses — so a keystroke never throws out of a computed value. */
export function currencyExponent(currency: string): number {
  const code = currency.trim().toUpperCase();
  return NON_DEFAULT_EXPONENTS[code] ?? DEFAULT_EXPONENT;
}

/** The decimal input step for one minor unit of `currency` (`"0.01"` for EUR, `"1"` for JPY, `"0.001"` for BHD) — offered for parity with #7's `apps/web/app/lib/money.ts`'s `currencyInputStep`, which a native `<input type="number" step=...>` reads directly; #8's form fields are text inputs validated through {@link currencyExponent} instead (see `place-order-form.tsx`), so this is exercised here as a pure library function. */
export function currencyInputStep(currency: string): string {
  const exponent = currencyExponent(currency);
  return exponent === 0 ? '1' : `0.${'1'.padStart(exponent, '0')}`;
}
