// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { currencyExponent, currencyInputStep, draftLineTotal, formatMinorUnits, minorUnitsToDecimalString, parseAmount, parseDecimalToMinorUnits, toDecimalString } from './money';

describe('money — decimal strings become exact integer minor units, never through floating point', () => {
  it('the classic float failures parse exactly: "0.29" → 29, "19.99" → 1999, "1.10" → 110', () => {
    // What the float route produces, to show the inputs are the dangerous ones:
    expect(Math.floor(Number('0.29') * 100)).toBe(28);
    expect(Number('19.99') * 100).not.toBe(1999);
    expect(Math.round(Number('1.005') * 100)).toBe(100);

    expect(parseDecimalToMinorUnits('0.29', 2)).toBe(29);
    expect(parseDecimalToMinorUnits('19.99', 2)).toBe(1999);
    expect(parseDecimalToMinorUnits('1.10', 2)).toBe(110);
    expect(parseDecimalToMinorUnits('249.99', 2)).toBe(24999);
  });

  it('"1.005" is REJECTED for a 2-decimal currency (never rounded to 100 or 101) and is exactly 1005 for a 3-decimal one', () => {
    expect(parseDecimalToMinorUnits('1.005', 2)).toBeUndefined();
    expect(parseAmount('1.005', 'EUR')).toBeUndefined();
    expect(parseAmount('1.005', 'BHD')).toBe(1005);
    expect(parseAmount('1.5', 'BHD')).toBe(1500);
  });

  it('a 0-decimal currency (JPY) takes whole amounts only', () => {
    expect(currencyExponent('JPY')).toBe(0);
    expect(parseAmount('1999', 'JPY')).toBe(1999);
    expect(parseAmount('19.99', 'JPY')).toBeUndefined();
    expect(parseAmount('20.', 'JPY')).toBeUndefined();
  });

  it('reads the exponent from ISO 4217, not an assumed 2', () => {
    expect([currencyExponent('EUR'), currencyExponent('GBP'), currencyExponent('USD'), currencyExponent('JPY'), currencyExponent('KWD')]).toEqual([2, 2, 2, 0, 3]);
    expect(currencyExponent('eur')).toBe(2);
  });

  it.each([
    ['', undefined],
    ['   ', undefined],
    ['-5', undefined],
    ['+5', undefined],
    ['1e3', undefined],
    ['1,000.00', undefined],
    ['NaN', undefined],
    ['20.', undefined],
    ['.', undefined],
    ['abc', undefined],
    ['.99', 99],
    ['007.50', 750],
    ['  20.50  ', 2050],
    ['0', 0],
    ['0.00', 0],
    ['20', 2000],
    ['0.1', 10],
  ])('parseDecimalToMinorUnits(%j, 2) === %j', (input, expected) => {
    expect(parseDecimalToMinorUnits(input, 2)).toBe(expected);
  });

  it('refuses a value beyond the safe-integer range instead of returning an inexact number', () => {
    expect(parseDecimalToMinorUnits('90071992547409.93', 2)).toBeUndefined();
    expect(parseDecimalToMinorUnits('90071992547409.91', 2)).toBe(9007199254740991);
  });

  it('round-trips every cent of a wide range: minor units → decimal string → the same minor units', () => {
    for (let minor = 0; minor < 200_000; minor += 7) {
      const text = minorUnitsToDecimalString(minor, 2);
      expect(parseDecimalToMinorUnits(text, 2)).toBe(minor);
    }
  });

  it('formats minor units to decimal strings digit-wise, honouring the exponent', () => {
    expect(minorUnitsToDecimalString(24999, 2)).toBe('249.99');
    expect(minorUnitsToDecimalString(5, 2)).toBe('0.05');
    expect(minorUnitsToDecimalString(0, 2)).toBe('0.00');
    expect(minorUnitsToDecimalString(-150, 2)).toBe('-1.50');
    expect(minorUnitsToDecimalString(1999, 0)).toBe('1999');
    expect(minorUnitsToDecimalString(1005, 3)).toBe('1.005');
    expect(toDecimalString(24999, 'EUR')).toBe('249.99');
    expect(toDecimalString(24999, 'JPY')).toBe('24999');
  });

  it('formats for humans with the currency symbol and the currency\'s own decimals', () => {
    expect(formatMinorUnits(24999, 'EUR')).toBe('€249.99');
    expect(formatMinorUnits(124250, 'GBP')).toBe('£1,242.50');
    expect(formatMinorUnits(1999, 'JPY')).toBe('JP¥1,999');
    expect(formatMinorUnits(1005, 'BHD')).toBe('BHD\u00a01.005'); // Intl separates a code-style symbol with a no-break space
    // A value that no binary double represents exactly still prints exactly.
    expect(formatMinorUnits(9007199254740991, 'USD')).toBe('US$90,071,992,547,409.91');
  });

  // Backlog id 103: the exponent comes from ISO 4217, not from `Intl`'s CLDR
  // display digits — `Intl.NumberFormat('en', {style:'currency', currency:
  // 'HUF'}).resolvedOptions().maximumFractionDigits` is 0 on this runtime,
  // while ISO 4217's published minor-unit exponent for HUF is 2. Same for
  // IQD (`Intl` 0, ISO 3). HUF, IDR and COP are plausible B2B currencies.
  it('HUF and IQD use their ISO 4217 exponent, not the 0 Intl reports for them', () => {
    expect(currencyExponent('HUF')).toBe(2);
    expect(currencyExponent('IQD')).toBe(3);
    expect(new Intl.NumberFormat('en', { style: 'currency', currency: 'HUF' }).resolvedOptions().maximumFractionDigits).toBe(0);
  });

  it('formats, parses and round-trips HUF (2), IQD (3), JPY (0), BHD (3) and EUR (2) through their own ISO 4217 exponent', () => {
    expect(formatMinorUnits(12345, 'HUF')).toBe('HUF 123.45'); // Intl separates a code-style symbol with a no-break space
    expect(formatMinorUnits(12345, 'IQD')).toBe('IQD 12.345');
    expect(formatMinorUnits(1999, 'JPY')).toBe('JP¥1,999');
    expect(formatMinorUnits(1005, 'BHD')).toBe('BHD 1.005');
    expect(formatMinorUnits(24999, 'EUR')).toBe('€249.99');

    expect(parseAmount('123.45', 'HUF')).toBe(12345);
    expect(parseAmount('123.456', 'HUF')).toBeUndefined(); // HUF has 2 decimals, not 3
    expect(parseAmount('12.345', 'IQD')).toBe(12345);
    expect(parseAmount('1999', 'JPY')).toBe(1999);
    expect(parseAmount('1.005', 'BHD')).toBe(1005);
    expect(parseAmount('249.99', 'EUR')).toBe(24999);

    expect(currencyInputStep('HUF')).toBe('0.01');
    expect(currencyInputStep('IQD')).toBe('0.001');
    expect(currencyInputStep('JPY')).toBe('1');
    expect(currencyInputStep('BHD')).toBe('0.001');
    expect(currencyInputStep('EUR')).toBe('0.01');
  });

  it('a draft line total is integer arithmetic: override over catalogue, minus the line discount', () => {
    expect(draftLineTotal({ quantity: 3, unitPrice: 1999 }, 500)).toBe(5997);
    expect(draftLineTotal({ quantity: 3 }, 500)).toBe(1500);
    expect(draftLineTotal({ quantity: 2, lineDiscount: 50 }, 500)).toBe(950);
    expect(draftLineTotal({ quantity: 0 }, 500)).toBe(0);
    expect(draftLineTotal({ quantity: 2 }, undefined)).toBe(0);
  });
});
