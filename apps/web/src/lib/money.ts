/**
 * Money at the human edge. Every amount on the wire is an integer count of
 * minor units (openapi.yaml "Money", R1); this module is the only place a
 * decimal string is turned into one, or one is turned into a decimal string —
 * and it does both DIGIT-WISE, never through floating point.
 *
 * Why not `Math.round(parseFloat(x) * 100)`: `0.29 * 100` is
 * `28.999999999999996` and `1.005 * 100` is `100.49999999999999` in IEEE 754,
 * so float arithmetic silently loses a cent on ordinary inputs (#7
 * review_web_app.md Pass 5 shipped exactly that bug). Here the whole and
 * fractional digits are concatenated as TEXT and converted to an integer once.
 *
 * The minor-unit exponent is never assumed to be 2. openapi.yaml says
 * formatting derives from `currency.decimalPoints`, but no REST operation in
 * the contract carries it (it lives only on the NATS `catalog.reference.list`
 * `CurrencyView`), so the exponent is read from an ISO 4217 literal table
 * (`./currency-exponent`, backlog id 103) — JPY is 0, BHD is 3, EUR/GBP/USD
 * are 2. Callers that do hold a `decimalPoints` value can pass it explicitly.
 * This module used to read the exponent from the platform's `Intl` data
 * (`maximumFractionDigits`), which is ICU/CLDR's DISPLAY precision and
 * disagrees with ISO 4217 for about fifteen codes — see `./currency-exponent`
 * for the full account and the parity guard against the backend's table.
 */

import { currencyExponent } from './currency-exponent';

export { currencyExponent, currencyInputStep } from './currency-exponent';

/**
 * Parses a human-typed decimal amount (`"19.99"`) into integer minor units
 * (`1999`) for a currency whose exponent is `exponent`.
 *
 * Returns `undefined` — never a wrong number — for: an empty/blank string, a
 * negative or signed value, scientific notation, thousands separators, more
 * fractional digits than the currency has (`"1.005"` in EUR), any fractional
 * part at all in a 0-exponent currency, and an in-progress `"20."`.
 */
export function parseDecimalToMinorUnits(input: string, exponent: number): number | undefined {
  const trimmed = input.trim();
  if (trimmed === '') return undefined;
  const pattern = exponent > 0 ? new RegExp(`^(\\d*)(?:\\.(\\d{1,${exponent}}))?$`) : /^(\d+)$/;
  const match = pattern.exec(trimmed);
  if (!match) return undefined;
  const whole = match[1] ?? '';
  const fraction = match[2] ?? '';
  if (whole === '' && fraction === '') return undefined;
  const digits = `${whole}${fraction.padEnd(exponent, '0')}`.replace(/^0+(?=\d)/, '');
  const value = Number(digits);
  return Number.isSafeInteger(value) ? value : undefined;
}

/** `parseDecimalToMinorUnits` with the exponent looked up from the currency code. */
export function parseAmount(input: string, currency: string): number | undefined {
  return parseDecimalToMinorUnits(input, currencyExponent(currency));
}

/** Integer minor units → the plain decimal string an editable input shows (`24999, 2 → "249.99"`). */
export function minorUnitsToDecimalString(minorUnits: number, exponent: number): string {
  const negative = minorUnits < 0;
  const digits = Math.abs(Math.trunc(minorUnits)).toString();
  if (exponent <= 0) return `${negative ? '-' : ''}${digits}`;
  const padded = digits.padStart(exponent + 1, '0');
  const whole = padded.slice(0, padded.length - exponent);
  const fraction = padded.slice(padded.length - exponent);
  return `${negative ? '-' : ''}${whole}.${fraction}`;
}

/** Integer minor units → the decimal string for `currency` (`24999, "EUR" → "249.99"`). */
export function toDecimalString(minorUnits: number, currency: string): string {
  return minorUnitsToDecimalString(minorUnits, currencyExponent(currency));
}

/**
 * Formats integer minor units for humans (`24999, "EUR"` → `"€249.99"`).
 * `Intl.NumberFormat.format` is given the exact decimal STRING, which the
 * platform formats without converting it to a binary float first.
 */
export function formatMinorUnits(minorUnits: number, currency: string, locale = 'en-GB'): string {
  const exponent = currencyExponent(currency);
  const decimal = minorUnitsToDecimalString(minorUnits, exponent);
  try {
    const formatter = new Intl.NumberFormat(locale, { style: 'currency', currency, minimumFractionDigits: exponent, maximumFractionDigits: exponent });
    return formatter.format(decimal as unknown as number);
  } catch {
    return `${decimal} ${currency}`;
  }
}

export interface DraftLineAmounts {
  quantity: number;
  /** The override if the operator typed one, else undefined. */
  unitPrice?: number;
  lineDiscount?: number;
}

/** One draft line's total in minor units — integer arithmetic only (price × quantity − discount). */
export function draftLineTotal(line: DraftLineAmounts, catalogPrice: number | undefined): number {
  const price = line.unitPrice ?? catalogPrice ?? 0;
  const quantity = Number.isSafeInteger(line.quantity) && line.quantity > 0 ? line.quantity : 0;
  return price * quantity - (line.lineDiscount ?? 0);
}
