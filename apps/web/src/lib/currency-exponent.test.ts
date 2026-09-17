// @vitest-environment node
//
// Backlog id 103, fix round 1: the previous cross-repository guard
// (`currency-exponent.parity.test.ts`) parsed the .NET backend's C# source
// as text and was beaten three ways in review (a block comment, an
// `#if false` region, two rows on one line — CLAUDE.md defeat-list rows 4,
// 5, 11). The cross-repository comparison now lives on the .NET side
// (`tests/SharedKernel.UnitTests/CurrencyExponentWebParityTests.cs`), which
// compares the backend's COMPILED table against `./currency-exponents.json`
// — the one committed data file. This file's own job is narrower and stays
// on the web side because only the web side can see it: prove that
// `./currency-exponent.ts` does not silently diverge from the JSON it
// claims to import (defeat-list row 11's "the web module ignoring the
// JSON" premise) by re-reading the raw file independently of the module's
// own `import` statement and comparing the two.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { NON_DEFAULT_EXPONENTS } from './currency-exponent';

const here = path.dirname(fileURLToPath(import.meta.url));
const JSON_PATH = path.resolve(here, './currency-exponents.json');

describe('currency-exponent.ts does not diverge from currency-exponents.json (id 103)', () => {
  it('NON_DEFAULT_EXPONENTS is exactly the JSON file on disk, read independently of the module import', () => {
    const raw: unknown = JSON.parse(readFileSync(JSON_PATH, 'utf8'));
    expect(NON_DEFAULT_EXPONENTS).toEqual(raw);
  });

  it('the JSON file itself has the 26 non-default rows this feature found, including UYI', () => {
    const raw = JSON.parse(readFileSync(JSON_PATH, 'utf8')) as Record<string, number>;
    expect(Object.keys(raw)).toHaveLength(26);
    expect(raw.UYI).toBe(0);
  });
});
