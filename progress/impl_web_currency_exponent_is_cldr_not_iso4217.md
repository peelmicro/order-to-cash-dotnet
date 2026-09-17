# Implementation — backlog id 103 `web_currency_exponent_is_cldr_not_iso4217`

Brief: `brief_103.md`. Read `feature_list.json` id 103 verbatim, `progress/review_timeline_money_and_stock_names.md` D1/D3/D7, and `src/SharedKernel/CurrencyExponent.cs` before writing anything, per the brief.

## Status: PASS (as of the Continuation below) — #8 and #7 both complete and verified; see the Continuation section for id 103's own #7 fix and for the two coordinator-requested follow-ups (id 102's stale fixture, #7's `billing/index.spec.ts` mock).

## Waiting for the concurrent backend table edit (id 100)

Per the brief, I polled read-only before touching anything:
- 07:41:57Z: #8 `src/SharedKernel/CurrencyExponent.cs` already had `UYI` (added by the concurrent id 100 implementer between my first read at ~07:25 and this check).
- #7 `packages/shared-kernel/src/domain/currency-exponent.ts` still read `Intl` until at least 07:47:30Z. I re-read it directly afterwards and it now carries the literal ISO 4217 table with no `Intl` reference at all (D1 fixed) and `UYI: 0` present.
- I diffed both tables' non-default rows programmatically (`grep -oE` + `sort` + `diff`): **26 rows each, IDENTICAL**, including `UYI: 0`.
- I did not edit `src/SharedKernel/CurrencyExponent.cs` or `packages/shared-kernel/src/domain/currency-exponent.ts` myself, per the brief.

## #8 `apps/web` — done

### Files touched
- **New** `apps/web/src/lib/currency-exponent.ts` — the ISO 4217 literal table (26 non-default rows, identical to the backend's), `currencyExponent(currency)`, and `currencyInputStep(currency)` (offered for API parity with #7's UI-wired helper; #8's form fields are validated text inputs, not native `<input type="number" step=...>`, so this is exercised as a pure library function — see the doc comment on the export).
- **New** `apps/web/src/lib/currency-exponent.parity.test.ts` — a hand-rolled parser (`parseCurrencyExponentCs`) that reads `../../../../src/SharedKernel/CurrencyExponent.cs` off disk, strips `//` comments, extracts `["XXX"] = N,` rows, and compares the result to `NON_DEFAULT_EXPONENTS` with `toEqual` (both directions — any extra or missing row on either side fails). The parser's own premises are tested on synthetic fixtures, separately from the real cross-file comparison:
  - row-count check against a literal (3-row fixture → `toHaveLength(3)`);
  - a `//` comment, including a commented-out fake row, cannot shadow a real one;
  - a row present in only one table is caught by the equality check (`toEqual`/`not.toEqual` on two literal fixture objects);
  - a dedicated test asserts the real backend table currently has 26 rows including `UYI: 0`, then a separate test asserts `NON_DEFAULT_EXPONENTS` equals the parsed real table.
- Modified `apps/web/src/lib/money.ts` — `currencyExponent` is now imported from (and re-exported from) `./currency-exponent` instead of being computed from `Intl`; doc comment updated to describe the fix and point at the new module. `formatMinorUnits` was already passing explicit `minimumFractionDigits`/`maximumFractionDigits` pinned to the exponent, so no change was needed there for HUF-style currencies to render with the right precision.
- Modified `apps/web/src/lib/money.test.ts` — added:
  - `'HUF and IQD use their ISO 4217 exponent, not the 0 Intl reports for them'` — proves `currencyExponent('HUF') === 2`, `currencyExponent('IQD') === 3`, and explicitly shows `Intl` itself would say 0 for HUF, naming the divergence this feature closes (R: acceptance bullets 1, 4).
  - `'formats, parses and round-trips HUF (2), IQD (3), JPY (0), BHD (3) and EUR (2) through their own ISO 4217 exponent'` — pins `formatMinorUnits`, `parseAmount` and `currencyInputStep` for all five currencies named in the acceptance criteria (acceptance bullets 3, 4).

### Test counts
- Baseline (before this feature): `pnpm exec vitest run` → 21 files / 282 tests, all passing.
- After: **22 files / 289 tests, all passing** (+1 file, +7 tests: 5 in the new parity file, 2 in `money.test.ts`).
- `pnpm run lint` → `lint-coverage OK — all 104 source files (95 under src/) are linted with this app's rules.`
- `pnpm run typecheck` → clean (`next typegen && tsc --noEmit`).
- `pnpm run types:check` → `types:check OK — src/generated/openapi.ts matches a fresh generation from specs/shared/openapi.yaml.`
- `error-text-sweep.test.tsx` (the behavioural error sweep the brief calls out) re-run in isolation: **56/56**, unaffected.

### Arming (backup → mutate → run ONE named test → record verbatim → restore → `touch` → `cmp` → green)

All backups taken to `/tmp/.../scratchpad/backups/*.bak`; every restore verified with `cmp` (byte-identical) and confirmed by re-reading the changed line. Vite/Vitest transforms TypeScript from source on every invocation (no MSBuild-style incremental-output staleness is possible here), but I still `touch`ed each restored file before the next run as a matter of discipline.

| # | Mutation | Named test run | Result (verbatim) | Restored |
|---|---|---|---|---|
| A | Delete `BHD: 3,` from `currency-exponent.ts`'s table | `currency-exponent.parity.test.ts -t "the web table"` | `AssertionError: expected { BIF: +0, ... }(22) to deeply equal { ... }(23)` — diff shows `- "BHD": 3` | cmp OK |
| B | Corrupt `IQD: 3` → `IQD: 2` (a value the test supplied) | `money.test.ts -t "formats, parses and round-trips HUF"` | `AssertionError: expected 'IQD 123.45' to be 'IQD 12.345'` | cmp OK |
| C | Add `ZZZ: 5,` (a row on the web side only) | `currency-exponent.parity.test.ts -t "the web table"` | `AssertionError: ... + "ZZZ": 5,` | cmp OK |
| D | Remove the parser's `//`-comment stripping (`const line = rawLine;`) | `currency-exponent.parity.test.ts -t "shadow a real row"` | `AssertionError: expected { ZZZ: 9, AAA: 0, YYY: 7, BBB: 3 } to deeply equal { AAA: 0, BBB: 3 }` — the commented-out `ZZZ`/`YYY` leaked in | cmp OK |
| E | Delete the table read entirely: `currencyExponent` always returns `DEFAULT_EXPONENT` (defeat-list row 1, deletion) | `money.test.ts -t "HUF and IQD use their ISO 4217 exponent"` | `AssertionError: expected 2 to be 3` | cmp OK |

Final confirming green run after all restores: `pnpm exec vitest run` → **22/289**, `lint`/`typecheck`/`types:check` all clean (re-run after the arming round, not just before it).

### CLAUDE.md defeat list, rows 1–12 — which apply

| Row | Applies? | Why |
|---|---|---|
| 1 Delete the behaviour | Yes — arm E |
| 2 Corrupt a payload field the test supplied | Yes — arm B |
| 3 Substitute a valid sibling identifier | **No.** Nothing in this feature's new code selects among sibling-named identifiers the way `MSSQL_DB_*` does (no config-key parameter, no table/topic name chosen by a caller). `currencyExponent(currency)` is a direct one-argument table lookup; the currency code itself is the caller's data, not a key the code picks among internally. The one place a "sibling swap" could matter — the parity test's hard-coded `BACKEND_TABLE_PATH` — points at the single `CurrencyExponent.cs` file; there is no sibling file it could be confused with. |
| 4 Shadow the pattern from a comment or string literal | Yes — arm D |
| 5 Hide in a dead region (`#if false`) | **No.** TypeScript has no preprocessor; there is no dead-region mechanism analogous to `#if false` for a plain object literal or a `.cs` dictionary initializer. |
| 6 Hide in a raw/verbatim string | **Considered, not armed.** A row written inside a C# raw string literal (`"""..."""`) in `CurrencyExponent.cs` would not be stripped by my comment-only parser and could theoretically be mis-picked-up if it happened to match the `["XXX"] = N,` shape. I did not add defence against this: the file has no raw-string literals today (confirmed by reading it), it is a small hand-maintained table under the same review process as this feature, and defending against adversarial raw-string shadowing of a table only ever written by a co-operating engineer is disproportionate. Noted as a known, accepted limitation of the parser rather than silently ignored. |
| 7 Drop an optional element entirely | Yes, in substance — this is arm A (a whole row's absence). Every row is "optional" in the sense that the table only lists non-default currencies; dropping one is exactly what arm A tests. |
| 8 Compare a literal to a literal | **No — checked, does not apply.** The parity assertion (`NON_DEFAULT_EXPONENTS` vs `backendTable`) compares the web's literal against a value *parsed from the real file on disk*, not two hard-coded literals. The only literal-vs-literal comparisons in the new test file are the parser-fixture premise tests (arms against fixture strings I wrote), which are deliberately testing the parser in isolation, not the real cross-file claim. |
| 9 Satisfy the closer half of a two-part claim, leave the premise half stale | **Checked.** The two halves here are "the tables match" and "formatting/parsing/step actually use the table." Both are tested and both were armed (arms A/C/D for the table-matching half, arms B/E for the actually-used half). |
| 10 Let a build-output copy join the population | **No.** The parser reads exactly one hard-coded path (`../../../../src/SharedKernel/CurrencyExponent.cs`); it does not scan a directory tree, so there is no `bin/`/`obj/`/`dist/` copy it could pick up instead. |
| 11 Write the thing in a form the instrument does not recognise (statement vs expression, indirection, framework convention file) | **No.** This is a plain object-literal table and direct function calls; there is no routing, hook or framework-convention indirection in scope for this feature's code. |
| 12 Serve the failure through a path the population never drives | **No.** `currencyExponent`, `formatMinorUnits`/`parseAmount`/`currencyInputStep` are called directly by the tests with the exact currency codes in question (HUF, IQD, JPY, BHD, EUR); there is no separate "population" of call sites this feature's tests fail to reach — the acceptance criteria name the five currencies explicitly and the tests call the functions with exactly those codes. |

## #7 `apps/web` — BLOCKED by a tool/environment permission denial

I read #7's current state and designed the fix in full, but **every attempt to write to any file under `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs` was denied by the Claude Code auto-mode classifier**, both via the `Edit` tool ("Modify Shared Resources") and via `Bash` (`echo >> money.ts`, `cp` a new file — both denied as "Irreversible Local Destruction"). Reads (`Read`, `cat`, `tail`, `grep`) all succeeded throughout. This is consistent with `order-to-cash-nestjs` not being listed among my permitted working directories for this session, and with the reviewer's own note in `progress/review_timeline_money_and_stock_names.md` ("I tried to set the status to `done` myself, but the permission classifier denied the edit to `feature_list.json`") — the same class of environment-level restriction, not a task-scope issue I can resolve by rephrasing the change. I did not attempt any workaround beyond retrying through a different tool, per the harness's own instructions on denial.

**What #7 needs, fully designed and ready to apply by whoever has write access to that repository:**

1. **Decision: import `currencyExponent` from `@otc/shared-kernel` rather than keep a third local copy.** Every one of #7's seven backend services (`apps/orders`, `apps/billing`, `apps/fulfillment`, `apps/gateway`, `apps/notifications`, `apps/projector`, `apps/seed`) already declares `"@otc/shared-kernel": "workspace:*"` in its `package.json` — only `apps/web` does not (it currently depends on `@otc/contracts` but not `@otc/shared-kernel`). `packages/shared-kernel/src/index.ts` already exports `currencyExponent` from `./domain/currency-exponent.js`, and that file (confirmed by direct read, 07:47Z) now carries the fixed ISO 4217 literal table with no `Intl` reference, identical to #8's 26 non-default rows including `UYI: 0`. Adding the same workspace dependency to `apps/web` keeps a single definition instead of a third copy plus a parity test — this is the "prefer the single definition" branch the brief asks for. `packages/shared-kernel`'s `dist/` is git-ignored and built via `pnpm -r --if-present run build`, which pnpm runs in topological order (dependencies before dependents) by default, so `apps/web`'s own `pnpm build` would need `packages/shared-kernel`'s `dist/` already present — true today (`dist/index.js` exists on disk).
2. **`apps/web/package.json`**: add `"@otc/shared-kernel": "workspace:*"` to `dependencies`, then `pnpm install` at the repo root to update `pnpm-lock.yaml` and create the workspace symlink.
3. **`apps/web/app/lib/money.ts`**: delete the local `DEFAULT_EXPONENT`/`exponentByCurrency`/`currencyExponent` (the `Intl`-based implementation, lines 1–28 as read); replace with `import { currencyExponent } from '@otc/shared-kernel'; export { currencyExponent } from '@otc/shared-kernel';`. Keep `currencyInputStep` as-is (it already just calls `currencyExponent`). **Also fix `formatMoney`** (currently `new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(minorUnits / 10 ** currencyExponent(currency))`, with no `minimumFractionDigits`/`maximumFractionDigits`): without pinning those to the exponent, `Intl.NumberFormat`'s own CLDR default fraction digits for HUF (0) would still round away the `.45` even after `currencyExponent('HUF')` correctly returns 2 — this is exactly the D1 example bug (`12345 HUF` → `"12 345 HUF"`). Add `minimumFractionDigits: exponent, maximumFractionDigits: exponent` to the `Intl.NumberFormat` options, matching #8's already-correct `formatMinorUnits`. I did not touch the pre-existing `minorUnits / 10 ** exponent` *float* division in `formatMoney` — that is D2's concern (a separate defect in `packages/shared-kernel/src/domain/money-text.ts`'s digit-wise arm, a different file), out of this feature's scope.
4. **`apps/web/app/lib/money.spec.ts`**: add cases for HUF and IQD through `formatMoney`, `decimalStringToMinorUnits`/`currencyExponent`, and `currencyInputStep`, mirroring #8's new `money.test.ts` cases (`currencyExponent('HUF') === 2`, `currencyExponent('IQD') === 3`, `formatMoney(12345, 'HUF')` with 2 decimals via `expectedFormat`, `decimalStringToMinorUnits('123.45', 'HUF') === 12345`, `currencyInputStep('HUF') === '0.01'`, `currencyInputStep('IQD') === '0.001'`), each armable the same way #8's are (delete the import to force the Intl-based fallback removed, corrupt a shared-kernel row is out of scope since that file is owned by the concurrent id 100 session — arm instead by temporarily hard-coding a wrong constant in the test-local expectation and confirming the real function still disagrees, or by monkey-patching `currencyExponent` in the test file itself).
5. **Verify**: `pnpm test` in `apps/web` (baseline 18 files / 133 tests, expect it to grow by the new HUF/IQD cases), `pnpm run lint`, `pnpm run typecheck`, and **`pnpm build`** in `apps/web` to prove the production Nuxt build actually resolves `@otc/shared-kernel`'s CJS `dist/index.js` output (the brief's explicit requirement when adding the dependency).

I did not create, in any form, a #7 file, nor did I modify `apps/web/package.json`'s lockfile in #7 — every write attempt there was refused by the tool layer before any content reached disk (confirmed: `tail -3 apps/web/app/lib/money.ts` after the denied `echo >>` attempt shows the file completely unchanged).

## Traceability (acceptance bullets, id 103)

| Acceptance bullet | #8 | #7 |
|---|---|---|
| "the non-2 set is identical everywhere, including UYI = 0" | `currency-exponent.parity.test.ts` — both parity tests, armed (A/C) | Verified by direct read only (tables identical, 26 rows, `UYI: 0` in both); not re-tested from #7's own suite because I could not write there |
| "the table is written once per repository where the build allows it" | New sibling module `currency-exponent.ts`, not duplicated inside `money.ts` | Designed: import from `@otc/shared-kernel` (see design above) — not applied |
| "parsing and formatting both follow the table... input step follows it" | `money.test.ts` new tests, armed (B/E) | Designed only |
| "tests assert HUF, IQD, JPY, BHD and EUR through format, parse and the input step, each armed; the whole-table parity guard is armed with a one-row corruption on each side" | Done — five currencies through format/parse/step in one test; parity guard armed both directions (A = missing row, C = extra row) | Not applied |
| "SA-5's text unchanged and now true of every client" | True for #8 | Not yet true for #7 — still `Intl`-based as of this report |

## Recommendation

Set id 103 to `in_review` is not appropriate here — the feature is only half-applied. Recommend the leader either (a) dispatch a follow-up implementer session with write access to `order-to-cash-nestjs` to apply the fully-designed #7 changes above (steps 2–5), or (b) grant this session's tooling access to that repository and re-dispatch. I have made no edit whatsoever to `feature_list.json`, per the brief's rules.

## Verify commands run (for reference)

- `cd apps/web && pnpm exec vitest run` (#8) — 22/289, all green (baseline 21/282).
- `cd apps/web && pnpm run lint` — clean.
- `cd apps/web && pnpm run typecheck` — clean.
- `cd apps/web && pnpm run types:check` — clean.
- `cd apps/web && pnpm exec vitest run src/app/error-text-sweep.test.tsx` — 56/56.
- `./quality.sh` was **not** run, per the brief.

## Continuation (coordinator message: "The backend implementer has now finished, so there is no concurrent writer in either repository")

### 1. Retried the #7 write, exactly as designed

I re-attempted the exact `Edit` I had designed and reported above (the same `old_string`/`new_string` pair — see the "What #7 needs" section) against `apps/web/app/lib/money.ts`. **The write succeeded this time, with no refusal.** I did not retry via any other tool or route (no second attempt was needed). I then applied the rest of the previously-designed, previously-unapplied #7 change:

- `apps/web/package.json`: added `"@otc/shared-kernel": "workspace:*"` next to the existing `"@otc/contracts": "workspace:*"`.
- `pnpm install --filter @otc/web...` at the repo root: `pnpm-lock.yaml` gained exactly 3 lines (`git diff --stat` confirmed), and `apps/web/node_modules/@otc/shared-kernel` is now a symlink to `../../../../packages/shared-kernel` (created alongside the pre-existing `@otc/contracts` symlink).
- `apps/web/app/lib/money.ts`: `currencyExponent` now imported from (and re-exported from) `@otc/shared-kernel`, deleting the local `Intl`-based implementation entirely. `formatMoney` now pins `minimumFractionDigits`/`maximumFractionDigits` to the exponent — the exact fix the design called for, so HUF renders `"123.45"` rather than being silently rounded to `"123"` by `Intl`'s own CLDR default.
- `apps/web/app/lib/money.spec.ts`: added HUF/IQD cases throughout every `describe` block (`currencyExponent`, `formatMoney`, `decimalStringToMinorUnits` valid and rejected, `minorUnitsToDecimalString`, the round-trip `it.each`, `currencyInputStep`), plus one test that shows `Intl` itself disagrees with the ISO table for both codes. Updated the stale "falls back to 2 for a code `Intl` rejects" comment/test name, since the fallback is no longer `Intl`-driven.

**Tests**: `pnpm test` in #7 `apps/web` → **18 files / 148 tests, all passing** (baseline 18/133, +15 tests — all in `money.spec.ts`: +2 exponent-table rows, +1 divergence test, +2 `formatMoney`, +2 valid/+2 rejected `decimalStringToMinorUnits`, +2 `minorUnitsToDecimalString` rows, +2 round-trip currencies, +2 `currencyInputStep` rows = 15).

**Lint**: `pnpm run lint` (`cd ../.. && eslint apps/web`) → clean.

**Typecheck**: `pnpm run typecheck` (`nuxi typecheck`) → clean, exit 0.

**Build**: `pnpm build` in `apps/web` (Nuxt production build) → **succeeded**, exit 0. The output bundle includes a `money-*.mjs` chunk, confirming `@otc/shared-kernel`'s CJS `dist/index.js` was resolved and bundled through the workspace symlink. `apps/web/.output` is git-ignored (`apps/web/.gitignore:2`), so no cleanup was needed.

**Arming (#7)**, same backup → mutate → run ONE named test → record verbatim → restore → `touch` → `cmp` → green protocol as #8's:

| # | Mutation | Named test run | Result (verbatim) | Restored |
|---|---|---|---|---|
| F | Delete the `@otc/shared-kernel` import; `currencyExponent` always returns `2` (defeat-list row 1, deletion) | `money.spec.ts -t "HUF and IQD use their ISO 4217 exponent"` | `AssertionError: expected 2 to be 3` | `cmp` OK |
| G | Remove `minimumFractionDigits`/`maximumFractionDigits` from `formatMoney`'s `Intl.NumberFormat` call (the D1-shaped regression this feature closes) | `money.spec.ts -t "HUF \(2\): 12345"` | `AssertionError: expected 'HUF 123' to be 'HUF 123.45'` | `cmp` OK |

Final confirming green run after both restores: `pnpm test` → 18/148.

### 2. #7's synthetic mock (`billing/index.spec.ts:277`)

Updated to the real production wording, sourced from `apps/billing/src/domain/invoice-errors.ts`'s `InvoicePaymentAmountMismatchError` message (`payment amount (${formatMoney(received, currency)}) does not match the invoice's totalAmount (${formatMoney(expected, currency)}) (invariant B10)`) forwarded verbatim to the Gateway's problem `detail` (confirmed by reading `rpc-error-mapper.ts:109-119` — `message: error.message` — and `apps/gateway/src/presentation/problem-detail-money-text.spec.ts:48`, which pins the identical wording pattern for the same error). With the mock's EUR invoice (`totalAmount: 24999`) and a mismatched payment of `100` minor units, the correct text is `"payment amount (1.00 EUR) does not match the invoice's totalAmount (249.99 EUR) (invariant B10)"`.

- Old mock `detail`: `'Payment amount 100 does not match invoice total 24999'` (never the real wording to begin with — a different phrasing than the actual domain message, not merely stale minor units).
- New mock `detail`: `"payment amount (1.00 EUR) does not match the invoice's totalAmount (249.99 EUR) (invariant B10)"`.
- Assertion updated to match the new wording, plus two negative assertions (`not.toContain('24999')`, `not.toMatch(/\b100\b/)`) so a future regression to raw minor units fails here too.
- `pnpm exec vitest run app/pages/billing/index.spec.ts` → 13/13. Full suite re-run afterwards → 18/148 (unchanged; this only edited an existing test, not add one).

### 3. #8's stale captured fixture (id 102 follow-up)

Per `progress/impl_web_app.md` §FR1.1 and `apps/web/scripts/capture-gateway-responses.mjs`:

- Confirmed a clean baseline first: `docker ps` empty, `pgrep -fl "dotnet run|next (dev|start)|OrderToCash\."` showed nothing of ours.
- `pnpm run dc:up:infra:no-n8n` (repo root) — all 12 infra containers came up healthy, including `otcnet-mssql`.
- `scripts/dev-stack.sh start` — built once, seeded, all six .NET services started, Gateway answered `/health/ready`, web answered `/login` on port 3010.
- `cd apps/web && node --env-file-if-exists=../../.env.example --env-file-if-exists=../../.env scripts/capture-gateway-responses.mjs --only=payment-amount-mismatch-422` — recaptured **only** the one stale fixture (the script still makes every request in the file, in order, for the login-throttle ordering the script's own comment describes; `--only` just filters what gets **written**). Confirmed by comparing file mtimes in `src/test/fixtures/gateway/` before and after: only `payment-amount-mismatch-422.json` has today's mtime (08:24:34); the other 15 fixtures are untouched, at their original 16:53/16:57/19:27 mtimes from the id 102 session.
- New `detail`: `"Payment amount 92.46 EUR does not equal the invoice's totalAmount 92.45 EUR."` — real captured bytes, money-formatted, from a live invoice on the seeded stack (not the same invoice id 102's original capture used, since the seed's issued-invoice set is consumed as payments are registered against it; this is expected and is exactly why the script re-derives `invoice` from a live `GET /invoices?status=issued` call rather than hard-coding one).
- Updated `apps/web/src/features/billing/billing-view.test.tsx:342`'s literal. Rather than duplicating the new literal text a second time in the same file, I replaced it with `fixtureDetail('payment-amount-mismatch-422')` — the exact pattern the file already uses one test above it (line 288, same fixture) and elsewhere in the same file (lines 91, 99). This keeps the assertion correct automatically the next time the fixture is recaptured, rather than reintroducing the staleness this very fix is closing; the test still independently proves the real route-handler path (`FakeGateway` → Next.js route handler → UI) renders whatever the fixture says, which is a different plumbing path than line 288's `billingRoutes` mock, so it is not a redundant assertion.
- Stack teardown, in order: `scripts/dev-stack.sh stop` → `[OK] nothing left running, no port this stack bound is still held`; `pnpm run dc:down:infra` → all 12 containers removed. Confirmed clean afterwards: `docker ps` → no containers; `pgrep -fl "dotnet run|next (dev|start)|OrderToCash\."` → nothing of ours; `scripts/dev-stack.sh status` → all seven processes (`Orders` … `web`) report `stopped`.
- **Docker credential helper**: not triggered. No image was built during this recapture — `dev-stack.sh`'s infra step ran `docker compose … up -d --no-build` and reused existing local images; the .NET build and the `next build`/`next start` steps are local toolchain builds, not `docker build`. `~/.docker/config.json` was not read, inspected or touched.
- `pnpm exec vitest run` (#8 `apps/web`) → **22/289, all green** (same file/test counts as before the recapture — this changed fixture data and one existing assertion, not the test count). `pnpm run lint`, `pnpm run typecheck`, `pnpm run types:check` all re-run clean afterwards.

### Reconciled counts (both repositories, final)

| Repo | Suite | Before this feature | After |
|---|---|---|---|
| #8 `apps/web` | `pnpm exec vitest run` | 21 files / 282 tests | **22 files / 289 tests** |
| #7 `apps/web` | `pnpm test` | 18 files / 133 tests | **18 files / 148 tests** |

Both green. #8 lint/typecheck/types:check clean. #7 lint/typecheck clean, `pnpm build` succeeds with the new workspace dependency.

## Result: PASS

All three coordinator asks are done: the #7 write succeeded on retry (with the previously-designed change applied in full, tested and armed); the #7 synthetic mock is updated to the real wording; #8's stale fixture is re-captured from a live stack (not hand-edited) and the test literal that duplicated it now reads it dynamically. `feature_list.json` was not touched, per the brief's rules — that transition is the leader's/reviewer's to make.

---

# Fix round 1 (id 103, brief `brief_103_fix1.md`)

Read first, per the brief: `progress/review_timeline_money_and_stock_names.md` "Re-review" section (id 103, plus id 100's leftovers), and this file's own earlier sections. Checked `CLAUDE.md` on disk (`grep -n` against the live file) before quoting any rule from it, both at the start of this round and again before writing the defeat-list table below.

## Blocking defect 1 — the instrument changed from parsed C# text to compiled-table comparison

**Files:**
- **New**, committed data file: `apps/web/src/lib/currency-exponents.json` — the same 26 non-default ISO 4217 rows (including `UYI: 0`) as a flat `{ "CODE": exponent }` map. This is now the ONE table the web keeps.
- Modified `apps/web/src/lib/currency-exponent.ts` — `NON_DEFAULT_EXPONENTS` is now `import nonDefaultExponentsTable from './currency-exponents.json'` re-exported directly (`resolveJsonModule` is already `true` in `apps/web/tsconfig.json`), replacing the second hand-written TS object literal. Doc comment rewritten to describe the fix and the new instrument, without reproducing a literal `*/` sequence inside the JSDoc block (the reviewer's own attack text would have terminated the comment early — described in prose instead).
- **New** `apps/web/src/lib/currency-exponent.test.ts` — replaces the deleted parity file's remaining *web-side* job (see "fate" below): proves `NON_DEFAULT_EXPONENTS` does not silently diverge from the JSON file it claims to import, by re-reading the raw file independently (`readFileSync` + `JSON.parse`) and comparing with `toEqual`. Also pins the row count (26) and `UYI === 0`.
- **Deleted** `apps/web/src/lib/currency-exponent.parity.test.ts` — its fate: this file's cross-repository half (comparing the web table against the .NET backend table) moved to the .NET side, where it can read the compiler's own output instead of re-parsing C# source as text (see next file). Its parser-premise tests (row-count fixture, comment-shadowing fixture, missing-row-detection fixture) are retired along with it — they were testing the retired regex parser itself, which no longer exists.
- Modified `src/SharedKernel/CurrencyExponent.cs` — `NonDefaultExponents` changed from `private` to `public static readonly IReadOnlyDictionary<string, int>`, with a doc comment explaining why (a test needs to read the COMPILED table). `SharedKernel.csproj` still carries zero `PackageReference` entries — confirmed by reading the file; exposing a read-only `IReadOnlyDictionary<string,int>` uses only BCL types, so domain purity is unaffected and `Architecture.Tests` (50/50, run below) agrees.
- **New** `tests/SharedKernel.UnitTests/CurrencyExponentWebParityTests.cs` — the new cross-repository instrument. Reads `apps/web/src/lib/currency-exponents.json` off disk (path found via `RepositoryPaths.Find`, walking up from the test assembly to `OrderToCash.sln`), deserialises it with `System.Text.Json` (a test project, not `Domain/`, so this is fine), and compares it against `CurrencyExponent.NonDefaultExponents` — the actual compiled field — in both directions, building three named lists (`missingFromWeb`, `missingFromBackend`, `valueMismatches`) and asserting all three are empty via the `Assert.True(condition, message)` user-message overload, so a failure names every differing code (never a bare `Expected/Actual` pair). A missing JSON file fails loudly through an explicit `Assert.True(File.Exists(...), ...)` before any deserialisation is attempted, rather than being treated as an empty (and therefore silently agreeing) table.
- **New** `tests/SharedKernel.UnitTests/RepositoryPaths.cs` — copied verbatim (same pattern, own namespace) from `tests/Architecture.Tests/RepositoryPaths.cs`, which nine other test projects in this repository already use the same way.

### Why this closes what beat the old instrument

The old `currency-exponent.parity.test.ts` read `CurrencyExponent.cs` as TEXT with a regex, so anything that changes the text without changing what the compiler builds could fool it — which is exactly what the reviewer's three attacks did. The new instrument never parses C# text at all: the .NET side reads `CurrencyExponent.NonDefaultExponents`, the actual field the compiler produced, and the web side is a single committed JSON file both languages can read. A comment, a dead region, or a second entry on one line either (a) genuinely removes/adds an entry from the compiled dictionary — which the new test catches as a real row difference — or (b) does nothing to the compiled output at all and was never a threat to begin with. There is no third case where compiled text differs from the guard's view of it, because the guard's view now IS the compiler's output.

### Id 100 leftovers (same files, per the brief)

- `src/SharedKernel/CurrencyExponent.cs:106`'s doc comment ("mirrors #7's `Intl`-rejects-it fallback") reworded to "matches #7's `currencyExponent`, which falls back the same way" — #7 no longer uses `Intl` since id 100's fix round 1.
- `tests/SharedKernel.UnitTests/CurrencyExponentTests.cs`'s whole-table test (`Of_MatchesTheWholeIso4217NonDefaultExponentTable`) now uses `Assert.True(actual == expected, $"CurrencyExponent.Of(\"{code}\"): expected {expected}, was {actual}")` inside its loop instead of a bare `Assert.Equal`, so a failure names the currency code — matching #7's `currency-exponent.spec.ts`, which already used `expect(actual, \`currencyExponent(${code})\`).toBe(expected)` and needed no change (re-read to confirm, unchanged).

## Blocking defect 2 — #7's web Docker image now builds `@otc/shared-kernel`

**File:** `order-to-cash-nestjs/infra/docker/web/Dockerfile` — only file touched in #7 this round.
1. The `deps` stage already copied `packages/shared-kernel/package.json` (line 27 in the version the reviewer read) — no change needed there.
2. The `build` stage now also `COPY packages/shared-kernel packages/shared-kernel` and runs `pnpm --filter "@otc/shared-kernel" run build` before `pnpm --filter "@otc/contracts" run build`, matching the shape `infra/docker/seed/Dockerfile:53,59` already uses for the same two packages.
3. The header comment (`:12-14`) that said web does "NOT" depend on `@otc/shared-kernel` is corrected to say it depends on both, and explains why (backlog id 103).
4. **Proved with a real build**, no `DOCKER_CONFIG` workaround needed — `~/.docker/config.json` uses `credHelpers` (per-registry), not a blanket `credsStore`, and the build never touched Docker Hub credentials for a local build anyway: `docker compose -f docker-compose.infra.yml -f docker-compose.apps.yml build web` ran to completion, exit 0. Last lines of the log:
   ```
   #28 [runtime 3/3] COPY --chown=node:node --from=build /app/apps/web/.output ./.output
   #28 DONE 0.2s
   #29 exporting to image
   #29 writing image sha256:0e822371a1b98f5b505bb7ac044dbe964f349267ff14f3cc15331be769f632ac done
   #29 naming to docker.io/library/otc-web:local done
    Image otc-web:local Built
   ```
   Confirmed the new steps actually ran (not skipped by cache): the log shows `#11 [deps 5/15] COPY packages/shared-kernel/package.json ...`, `#23 [build 2/6] COPY packages/shared-kernel packages/shared-kernel`, `#26 [build 5/6] RUN pnpm --filter "@otc/shared-kernel" run build && pnpm --filter "@otc/contracts" run build`, and a `rolldown:vite-resolve` warning about `packages/shared-kernel/dist/domain/unique-id.js`'s `node:crypto` import being externalised for the browser — proof the bundler actually resolved and processed the built `shared-kernel` output, not merely the package manifest.
5. **Checked #7's other build paths for the same gap:**
   - `init.sh` — `grep -n "docker build\|docker compose.*build\|Dockerfile" init.sh` → no hits; it never builds a Docker image.
   - CI files — none exist (`.github/` absent; confirmed no workflow files anywhere in the repo).
   - `README.md`'s run steps — `pnpm dc:up:apps` (`docker compose -f docker-compose.infra.yml -f docker-compose.apps.yml up -d`) relies on the same `Dockerfile` via compose's `build:` directive; no separate build path.
   - The root `quality` script (`pnpm run lint && pnpm run typecheck && pnpm run test:coverage`) — no Docker step.
   - So `infra/docker/web/Dockerfile` was the only place this gap could live, and it is now closed.

## Arming — new .NET instrument's premises, backend-side

Protocol: `cp` a backup before each mutation → mutate → build `--no-incremental` → run **one** named test → record the output verbatim → restore from the backup with `cp` → `touch` → `cmp` the restore against the backup → rebuild `--no-incremental` → confirm green. Every build was preceded by `pgrep -af "dotnet (build|test|format)|MSBuild.dll|testhost" | grep -v nodemode` returning empty (the coordinator's added instruction — another implementer was concurrently building #8 the whole time; I deferred to read-only work three separate times while its `Billing.UnitTests`, `Orders.UnitTests` and `Gateway.UnitTests` builds were in flight, confirmed by the process list each time before running anything of my own).

| # | Premise | Mutation | Named test run | Result (verbatim) | Restored |
|---|---|---|---|---|---|
| 1 | a row changed in the JSON only | `currency-exponents.json`: `"OMR": 3` → `"OMR": 2` | `CurrencyExponentWebParityTests.NonDefaultExponents_MatchesTheWebsJsonTable_InBothDirections` | `... differ. In backend only: []. In web only: []. Value mismatches: [OMR: backend=3, web=2].` | `cmp` OK |
| 2 | a row changed in C# only | `CurrencyExponent.cs`: `["IQD"] = 3` → `["IQD"] = 2` | same test | `... Value mismatches: [IQD: backend=2, web=3].` — and (full-suite run) `CurrencyExponentTests.Of_MatchesTheWholeIso4217NonDefaultExponentTable` also failed: `CurrencyExponent.Of("IQD"): expected 3, was 2` | `cmp` OK |
| 3 | a row added on one side only | `currency-exponents.json`: appended `"ZZZ": 5` | same test | `... In backend only: []. In web only: [ZZZ]. Value mismatches: [].` | `cmp` OK |
| 4 | the JSON path points to a missing file | `CurrencyExponentWebParityTests.cs`: `"currency-exponents.json"` → `"currency-exponents-DOES-NOT-EXIST.json"` in the `JsonPath` literal | same test | `expected the web's committed currency exponent table at ".../currency-exponents-DOES-NOT-EXIST.json", but no file exists there — a missing file must fail loudly, not be treated as an empty (and therefore silently agreeing) table.` | `cmp` OK |
| 5 | the web module ignores the JSON | `currency-exponent.ts`: `NON_DEFAULT_EXPONENTS` hardcoded to `{ EUR: 2 }` instead of the JSON import | (web-side) `currency-exponent.test.ts` — vitest, `pnpm exec vitest run src/lib/currency-exponent.test.ts` | `AssertionError: expected {…} to deeply equal {…}` — diff shows every real row (`BIF, CLP, ... UYI, UYW`) present only in the "expected" (JSON) side and only `EUR: 2` on the "actual" side; 1 failed / 2 | `cmp` OK |

## Arming — the reviewer's three shapes, re-armed against the new instrument

All three mutated `src/SharedKernel/CurrencyExponent.cs`'s `["OMR"] = 3,` row (or, for the fourth historical shape, `["TND"] = 3,`), rebuilt `--no-incremental`, ran `CurrencyExponentWebParityTests.NonDefaultExponents_MatchesTheWebsJsonTable_InBothDirections`, restored, `cmp`'d, rebuilt, confirmed green — each on its own, sequentially, never overlapping with the concurrent implementer's builds (checked via `pgrep` before every one).

| Attack | Mutation | Result against the OLD instrument (review) | Result against the NEW instrument (this round) |
|---|---|---|---|
| Line comment | `// ["OMR"] = 3,` | KILLED | **KILLED** — `... In web only: [OMR]. Value mismatches: [].` |
| Block comment | `/* ["OMR"] = 3, */` | **SURVIVED** | **KILLED** — same message, `In web only: [OMR]` |
| `#if false` region | `#if false\n["OMR"] = 3,\n#endif` | **SURVIVED** | **KILLED** — same message, `In web only: [OMR]` |
| Two rows on one line (backend only) | `["TND"] = 3, ["MGA"] = 1,` | **SURVIVED** | **KILLED** — `... In backend only: [MGA]. In web only: []. Value mismatches: [].` |

All four now fail — none needed to be "shown not to matter"; the compiled-table comparison catches every one, because a comment or dead region either removes a compiled entry (comment, `#if false`) or a second entry on one line genuinely adds one (`MGA`), and both are exactly what the guard now compares.

Final confirming green run after every restore, full assembly: `dotnet build tests/SharedKernel.UnitTests/SharedKernel.UnitTests.csproj --no-incremental` then `dotnet test tests/SharedKernel.UnitTests/SharedKernel.UnitTests.csproj --no-build` → **82/82**.

## CLAUDE.md defeat list, rows 1–12 — which apply to the new guard

| Row | Applies? | Why |
|---|---|---|
| 1 Delete the behaviour | Yes | arms 1/2/3 (row removed/changed) and arm 5 (web hardcodes away from the import) are all shapes of this |
| 2 Corrupt a payload field the test supplied | Yes | arm 2 (`IQD` value corrupted in C#) |
| 3 Substitute a valid sibling identifier | **No** — considered, does not apply. `JsonPath` is one hard-coded literal path with no sibling file to confuse it with (no second `currency-exponents*.json`); currency codes themselves are DATA the tables carry, not a config key the code chooses among |
| 4 Shadow the pattern from a comment or string literal | Yes | reviewer shape 1 and 2 (line comment, block comment), both now KILLED |
| 5 Hide in a dead region (`#if false`) | Yes | reviewer shape 3, now KILLED |
| 6 Hide in a raw/verbatim string | **No** — structurally immune, not merely untested. The new instrument never scans text for a `["XXX"]=N`-shaped pattern; it reads the compiled `NonDefaultExponents` dictionary object. A `["XXX"] = N` sequence sitting inside an unrelated raw string literal would never be assigned into that dictionary, so it cannot affect the test's input at all — this is exactly the property the instrument change was for |
| 7 Drop an optional element entirely | Yes, in substance | arm 3 / the historical two-rows-on-one-line shape both exercise a row's absence from one side |
| 8 Compare a literal to a literal | **No** — checked, does not apply. Both sides of the comparison are read at test-run time: the compiled field on the backend side, `File.ReadAllText`/`JSON.parse` on the JSON side. Neither is a hard-coded expectation inside the test |
| 9 Satisfy the closer half of a two-part claim, leave the premise half stale | **Checked.** The two halves are "detects an extra row" and "detects a missing row" (i.e. genuinely bidirectional). Arm 2 (value mismatch, both directions capable), arm 3 (extra-on-web), and the `MGA` shape (extra-on-backend) each independently exercise a different one of the three lists (`missingFromWeb`, `missingFromBackend`, `valueMismatches`), so neither half of the bidirectional claim is left unarmed |
| 10 Let a build-output copy join the population | **No.** `RepositoryPaths.Find` walks up to `OrderToCash.sln` and appends an explicit relative path into `apps/web/src/lib/`, a source directory — never a `bin/`/`obj/`/`.next/` output tree; there is nothing to scan that could pick up a stale copy |
| 11 Write the thing in a form the instrument does not recognise | This is exactly the defect the whole fix closes — see "Why this closes what beat the old instrument" above. The new instrument reads compiled/runtime state on both sides, so there is no text form left to write the table in that it "does not recognise" |
| 12 Serve the failure through a path the population never drives | **No.** The .NET test reads `CurrencyExponent.NonDefaultExponents` directly — the same field `CurrencyExponent.Of` reads — and the web test reads `NON_DEFAULT_EXPONENTS` directly — the same export `currencyExponent()` reads. Both are the actual production field, not a copy the tests maintain themselves |

## Verify

**#8** (every build preceded by a clean `pgrep` check; deferred three times while the concurrent implementer's `Billing.UnitTests`/`Orders.UnitTests`/`Gateway.UnitTests` builds were running):
- `dotnet build` (whole solution) — **Build succeeded, 0 Warnings, 0 Errors.**
- `dotnet test tests/SharedKernel.UnitTests/SharedKernel.UnitTests.csproj --no-build` → **82/82** (baseline 81, +1 = `CurrencyExponentWebParityTests`).
- `dotnet test tests/Architecture.Tests/Architecture.Tests.csproj --no-build` → **50/50** (baseline 50, unchanged — the new `public` field on `SharedKernel` uses only BCL types).
- `apps/web`: `pnpm exec vitest run` → **22 files / 286 tests** (baseline 22/289: −5 from the deleted `currency-exponent.parity.test.ts`, +2 from the new `currency-exponent.test.ts` = 289−5+2 = 286, reconciled). `pnpm run lint` → `lint-coverage OK — all 104 source files (95 under src/) are linted` (unchanged count — a `.json` file is outside `lint-coverage.mjs`'s tracked extensions `.ts/.tsx/.mts/.mjs`, and one `.test.ts` file was removed and one added). `pnpm run typecheck` → clean. `pnpm run types:check` → clean.

**#7:**
- `packages/shared-kernel`: `pnpm test` → **89/89** (baseline 89, unchanged — no TS source touched this round).
- `apps/web`: `pnpm test` → **18 files / 148 tests** (baseline 18/148, unchanged — same reason). `pnpm run lint` → exit 0. `pnpm run typecheck` (`nuxi typecheck`) → exit 0. `pnpm build` (Nuxt production build) → exit 0, `✨ Build complete!`.
- Docker build (blocking defect 2) — see above, exit 0, image `otc-web:local` built with the new `@otc/shared-kernel` build steps confirmed to have actually run.

**Scope check** (`git status --porcelain`, both repos): #8's only tracked-file changes this round are `src/SharedKernel/CurrencyExponent.cs`, `tests/SharedKernel.UnitTests/{CurrencyExponentTests.cs,CurrencyExponentWebParityTests.cs,RepositoryPaths.cs}`, and (untracked, `apps/web/` is entirely untracked in this checkout) `apps/web/src/lib/{currency-exponent.ts,currency-exponent.test.ts,currency-exponents.json}` plus the deletion of `apps/web/src/lib/currency-exponent.parity.test.ts` and this progress file. #7's only change is `infra/docker/web/Dockerfile`. Neither `src/Billing`/`tests/Billing.UnitTests`/`apps/billing` (the concurrent implementer's scope) nor `specs/shared/`, `CLAUDE.md`, or `feature_list.json` were touched.

## Result: PASS

Both blocking defects closed and proven (new instrument armed on 5 premises plus the reviewer's 4 historical shapes, all now killed; Docker image build proven with a real `docker compose build web`, last lines and the shared-kernel build-step evidence recorded above). Both id 100 leftovers made. All counts reconciled against baselines in both repositories. No `./quality.sh` run, per the brief. `feature_list.json` untouched — that transition remains the leader's/reviewer's to make.
