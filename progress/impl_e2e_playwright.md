# impl_e2e_playwright — feature 32 (`e2e_playwright`, phase 19)

## What was built

- `apps/web/playwright.config.ts` — ported from #7's own config (same
  `baseURL` derivation, `expect.timeout: 30_000`, `retries: 0`, `setup`
  project the `chromium` project depends on, `trace: 'retain-on-failure'`,
  `screenshot: 'only-on-failure'`), with one path change (see "Design
  choices" below).
- `apps/web/e2e/global.setup.ts` — logs in once through the real UI
  (`/login`, `#username`/`#password`, "Sign in"), saves `storageState`.
- `apps/web/e2e/happy-path.spec.ts` — Scenario 1 + 3: places a non-`.99`
  order (CarrefourEs / ALBIONFOODS / PRD-0006 × 2 = 12.90), waits for
  `invoiced`, registers payment through the billing UI, waits for `paid` on
  the invoice row, then `completed` on the order.
- `apps/web/e2e/compensation.spec.ts` — Scenario 2: clicks "Fill demo order
  (.99 → compensation)", waits for `cancelled`, asserts the timeline shows
  `stock.released.v1` before `order.cancelled.v1`, and that the cancellation
  entry's causation link reads `stock.released.v1`.
- `apps/web/package.json` — added `"@playwright/test": "1.62.1"` (dev
  dependency) and a `test:e2e` script.

## Design choices

**Stack driven.** `scripts/dev-stack.sh start` (docker compose infra + the
six .NET services built once + `next build`/`next start` for web) — #8 has
no `docker-compose.apps.yml` yet (phase 23's job per the brief), so this is
today's equivalent of #7's fully-composed stack. Verified end to end: `stop`
leaves zero of the stack's own processes and zero of its own ports held
(confirmed by `pgrep`/`docker ps` after every run in this session).

**Invocation.** `pnpm run test:e2e` inside `apps/web`, or directly
`node --env-file-if-exists=../../.env ./node_modules/@playwright/test/cli.js test`.
**Not** `./node_modules/.bin/playwright test` — pnpm's `.bin` shim for
`@playwright/test`'s own CLI is a POSIX shell script (`#!/usr/bin/env node`
wrapper), and running it as `node <shim>` throws `SyntaxError: Invalid or
unexpected token` immediately (confirmed live: the very first run attempt).
Calling `@playwright/test/cli.js` directly sidesteps the shim and lets
`node --env-file-if-exists` do its job, matching the pattern #8's own
`dev`/`start` scripts already use for `next`'s CLI entry point (no
`dotenv-cli` dependency added — see ledger below).

**Not joined into `./quality.sh`.** #7 keeps `test:e2e` out of its own
`quality.sh`/`package.json` `"quality"` script too (`grep -n "e2e\|playwright"
quality.sh` in the #7 checkout returns nothing) — same precedent followed
here. An e2e suite that spins up docker compose + six real services doesn't
belong in the fast local gate; `quality.sh`'s own header confirms coverage
gating is deliberately out of scope for feature 34, and e2e is out of scope
for this script entirely by the same reasoning (Testcontainers-style
integration, never mocked, never part of the fast loop).

**`storageState` path.** `./test-results/.auth/operator.json`, not
`./e2e/.auth/operator.json` (#7's path — #7's `.gitignore` carries an
explicit `apps/web/e2e/.auth/` entry for it, `.gitignore:28` in the #7
checkout). #8's root `.gitignore` already ignores `test-results/`
(`.gitignore:46`) with no leading `/`, so writing the sealed session cookie
under `test-results/` needs no new `.gitignore` entry — and `.gitignore` is
outside this feature's touch scope (`apps/web/e2e/`,
`apps/web/playwright.config.ts`, `apps/web/package.json` only). Recorded here
as the one config path deviation from #7.

**Browser install.** `node ./node_modules/@playwright/test/cli.js install
chromium` from `apps/web` (no `--with-deps`: that path needs `sudo`, which
this environment doesn't have — confirmed live, `sudo: a password is
required`). Chromium alone launched and ran both specs cleanly with no
missing shared-library errors, so `--with-deps` was not needed on this
machine; a machine that lacks Chromium's runtime libraries would need it,
which is out of this feature's control. Not currently automated/repeatable
via a checked-in script — a README/AGENTS.md note is the leader's call per
the brief, not mine to add.

**Package version pinned at `1.62.1`, not `1.63.0`.** `1.63.0` (the `latest`
dist-tag at the time of this work, 2026-09-17) requires Chromium revision
`1243`; `curl`-ing `cdn.playwright.dev`'s build URL for that revision
returned `HTTP 400` / body `GatewayExceptionResponse` (confirmed live,
several retries, several URL variants). `1.62.1` — the exact version #7
pins — needs revision `1234`, which downloaded cleanly (two ~150–190 MiB
zips, full progress bars, `INSTALLATION_COMPLETE` marker written). This
reads as the CDN not yet having finished publishing a same-day release's
browser artifacts, not a defect in #8. Recommendation: re-verify the CDN
before ever bumping past `1.62.1`.

## Ported-idiom ledger

| # | #7 relied on X (file:line, #7 checkout) | In #8 that property is supplied by Y |
|---|---|---|
| 1 | Nuxt/Vue's custom `<Select>` dropdown, driven via `getByTestId('...-trigger').click()` + `getByRole('option', {name})` — `apps/web/e2e/happy-path.spec.ts:28-33` | Native `<select>` elements (`NativeSelect`/`NativeSelectOption`, `apps/web/src/components/ui/native-select.tsx`) driven via Playwright's `selectOption`, scoped by `getByRole('combobox', {name})` for the two static-id selects and by `getByTestId('order-line').locator('select')` for the per-line product select (guard: see Finding below) |
| 2 | An "order list" link inside the success banner (`page.getByRole('link', {name:'order list'}).click()` then locating the row by reference) — `apps/web/e2e/happy-path.spec.ts` (both specs, around the success-banner block) | `accepted-order-link` (`apps/web/src/features/orders/place-order-form.tsx:365`), a direct link from the success banner straight to `/orders/{orderId}` — one hop, no intermediate list lookup |
| 3 | `dotenv-cli` (`"test:e2e": "dotenv -e ../../.env -- playwright test"`, #7 `apps/web/package.json:17`, `dotenv-cli: catalog:` pinned via the pnpm catalog) | Node 24's native `--env-file-if-exists` flag, invoked directly against `@playwright/test/cli.js` — the same convention #8's own `dev`/`start` scripts already use (`apps/web/package.json`), so no new dependency was added |
| 4 | **[Correction, review C1 — the original row was FALSE in both halves.]** Nothing: #7's own test reads `page.getByTestId('payment-form')`, page-level, not row-scoped (`../order-to-cash-nestjs/apps/web/e2e/happy-path.spec.ts:74`), and #7's markup puts `payment-form` in a sibling `<TableRow>`, not nested inside `invoice-row` (`../order-to-cash-nestjs/apps/web/app/pages/billing/index.vue:357`). `invoiceRow.getByTestId('payment-form')` would not have resolved in #7 either. There is no idiom here to port — #8's `page.getByTestId('payment-form')` (`apps/web/e2e/happy-path.spec.ts:97`) is character-for-character #7's line 74. | (n/a — see left) |

| 5 | The order-detail page's backstop refetch while non-terminal, plus one poll after an observed terminal transition (`../order-to-cash-nestjs/apps/web/app/composables/useOrderDetail.ts:112`, #7's D1/D2/D7), so a lost SSE frame cannot leave `toHaveText('completed')` polling a permanently stale DOM | `apps/web/src/hooks/use-order-detail.ts:59-71`, shipped with the web feature (id 29) and documented there at `:13-21` — confirmed present, not re-derived by this feature |

## Finding (not fixed — outside this feature's touch scope)

**The product line's `<label for>` and its `<select id>` can disagree.**
Confirmed live, twice, on a freshly loaded `/orders/place`:
`for="product-6"` / `id="product-2"` on the first load, `for="product-7"` /
`id="product-2"` on the second (reproduced with a throwaway Playwright
script against the running stack, since deleted). `place-order-form.tsx`
derives both the label's `htmlFor` and the select's `id` from a
module-scope, mutable `let nextLineKey = 1;` counter incremented by
`emptyLine()`. This counter free-runs across **every** SSR render this
long-lived `next start` process has ever served (the module is loaded once
per server process, not once per request), while the browser's own fresh JS
context recomputes it starting from 1 on every hard navigation — so the
server-embedded label text and the client's own post-hydration `<select>`
mount (which only appears once the catalogue query resolves, i.e. after
hydration, as a fresh mount replacing the SSR fallback `<Input>`) can end up
disagreeing, and React does not always repair the stale `for` attribute.
This breaks `getByLabel('Product')` / `getByRole('combobox', {name:
'Product'})` — and, more importantly, breaks the label/control association
for real assistive technology, independent of Playwright.

This is a genuine defect in `apps/web/src/features/orders/place-order-form.tsx`,
outside `apps/web/e2e/`, `apps/web/playwright.config.ts` and
`apps/web/package.json` — the only files this brief authorises touching
(`src/` fix authorised only when a defect makes an acceptance bullet
*impossible*, and only after stopping to report it first). It does not make
either acceptance bullet impossible: a real operator clicking through the UI
with a mouse is unaffected (the `for`/`id` link only matters for
label-click-to-focus and screen readers), and the test itself was written to
route around it with a structural locator
(`getByTestId('order-line').locator('select')` — there is exactly one
`<select>` per order line) rather than depend on the broken association.
**Disposition: not fixed here; recommend a backlog entry for
`place-order-form.tsx`'s `nextLineKey` counter (module-scope mutable state
read during SSR is not safe across a long-lived server process — the fix is
almost certainly deriving the key from something request/render-scoped, e.g.
`useId()`, rather than a free-running module-level counter).** Re-open
trigger: any test that starts relying on `getByLabel`/`getByRole` name
matching for a per-line field.

## Assertion inventory (#7's `expect(` calls, all three files, 31 total)

`global.setup.ts` (2, both ported):
1. `expect(submit).toBeEnabled()` — ported.
2. `expect(page).toHaveURL(/\/orders$/)` — ported.

`happy-path.spec.ts` (14):
1. `expect(submit).toBeEnabled()` [place order] — ported.
2. `expect(success).toBeVisible()` — ported.
3. `expect(orderReference, ...).toBeTruthy()` — ported.
4. `expect(page).toHaveURL(/\/orders$/)` [after clicking "order list"] —
   **deliberately not ported**: #8's success banner has no intermediate
   "order list" link/page to navigate through (idiom #2 in the ledger); the
   URL this checked doesn't exist in #8's flow.
5. `expect(orderLink).toBeVisible()` [list-row link] — **ported, adapted**:
   #8 asserts `accepted-order-link` visibility instead (same intent — "the
   success banner's own link genuinely works" — different locator/route,
   ledger #2).
6. `expect(page).toHaveURL(/\/orders\/[0-9a-f-]+$/)` — ported.
7. `expect(order-detail-status).toHaveText('invoiced')` — ported.
8. `expect(invoiceRow).toBeVisible()` — ported.
9. `expect(paymentForm).toBeVisible()` — ported verbatim [corrected,
   review C1: #7's own assertion is page-level too, `page.getByTestId('payment-form')`
   at `../order-to-cash-nestjs/apps/web/e2e/happy-path.spec.ts:74`; the
   original ledger row 4 falsely called this an adaptation].
10. `expect(submitPayment).toBeEnabled()` — ported.
11. `expect(payment-outcome-accepted).toBeVisible()` — ported.
12. `expect(invoiceRow.getByText('paid', {exact:true})).toBeVisible()` —
    **ported, strengthened**: #8 uses `invoiceRow.getByTestId('invoice-status')`
    (a dedicated testid #8 has and #7 didn't use here), same assertion
    intent with a more precise locator.
13. `expect(viewOrderLink).toBeVisible()` — ported.
14. `expect(order-detail-status).toHaveText('completed')` — ported (armed —
    see below).

`compensation.spec.ts` (15):
1. `expect(running-total).toContainText('249.99')` — ported.
2. `expect(submit).toBeEnabled()` — ported.
3. `expect(success).toBeVisible()` — ported.
4. `expect(orderReference, ...).toBeTruthy()` — ported.
5. `expect(page).toHaveURL(/\/orders$/)` — **deliberately not ported**, same
   reason as happy-path's #4.
6. `expect(orderLink).toBeVisible()` — **ported, adapted**, same reason as
   happy-path's #5.
7. `expect(page).toHaveURL(/\/orders\/[0-9a-f-]+$/)` — ported.
8. `expect(order-detail-status).toHaveText('cancelled')` — ported.
9. `expect(getByText('credit_rejected', {exact:true})).toBeVisible()` —
   ported verbatim (confirmed live: `src/Fulfillment/Presentation/Rpc/StockRequestValidator.cs:26`
   carries the same `"credit_rejected"` reason string #7 uses).
10. `expect(stockReleasedIndex, ...).toBeGreaterThanOrEqual(0)` — ported.
11. `expect(orderCancelledIndex, ...).toBeGreaterThanOrEqual(0)` — ported.
12. `expect(stockReleasedIndex, ...).toBeLessThan(orderCancelledIndex)` —
    ported (armed — see below).
13. `expect(causation).toBeVisible()` — ported.
14. `expect(causation).toContainText('caused by')` — ported.
15. `expect(cancelledEntry.getByTestId('timeline-causation-link')).toHaveText('stock.released.v1')` — ported.

**Totals: 31 source assertions; 29 ported (25 verbatim/near-verbatim, 4
adapted for a genuine structural difference); 2 deliberately not ported (both
`toHaveURL(/\/orders$/)` mid-flow checks, not applicable — #8's flow never
visits that intermediate page); 0 not applicable for any other reason.
[Corrected, review C1: item 9 was misclassified as adapted; it is
verbatim.]**

## Arm table (verbatim)

### Arm 1 — happy path reaches `completed`, not merely `invoiced`

Protocol: `cp` backup, mutate the FINAL assertion's expected string from
`'completed'` to `'invoiced'`, run the ONE named test, record the failure
verbatim, restore, `cmp`, re-run green.

```
$ cp apps/web/e2e/happy-path.spec.ts /tmp/happy-path.spec.ts.bak
$ sed -i "s/toHaveText('completed', { timeout: 30_000 });/toHaveText('invoiced', { timeout: 30_000 });/" apps/web/e2e/happy-path.spec.ts   # (last occurrence only, line 122)
$ node --env-file-if-exists=../../.env ./node_modules/@playwright/test/cli.js test e2e/happy-path.spec.ts
```

Verbatim failure:
```
  1) [chromium] › e2e/happy-path.spec.ts:19:1 › an order with a non-.99 total reaches completed, and the invoice explicitly flips to paid

    Error: expect(locator).toHaveText(expected) failed

    Locator:  getByTestId('order-detail-status')
    Expected: "invoiced"
    Received: "completed"
    Timeout:  30000ms

    Call log:
      - Expect "toHaveText" with timeout 30000ms
      - waiting for getByTestId('order-detail-status')
        62 × locator resolved to <span ... data-testid="order-detail-status" ...>completed</span>
           - unexpected value "completed"
```

The mutated assertion (expecting the WRONG, earlier status `invoiced`) failed
because the real saga had already advanced past it — 62 consecutive polls
over 30s all read `completed`, never `invoiced`. This proves the original
assertion (`toHaveText('completed')`) is not vacuous: it distinguishes the
saga's genuine terminal state from the earlier status this same test itself
waits on at line 84, and a defect that stalled the saga at `invoiced` instead
of advancing it to `completed` would be caught.

```
$ cp /tmp/happy-path.spec.ts.bak apps/web/e2e/happy-path.spec.ts
$ cmp /tmp/happy-path.spec.ts.bak apps/web/e2e/happy-path.spec.ts && echo "cmp: identical, restore confirmed"
cmp: identical, restore confirmed
$ node --env-file-if-exists=../../.env ./node_modules/@playwright/test/cli.js test   # 3 passed
```

### Arm 2 — compensation ordering (`stock.released.v1` before `order.cancelled.v1`)

Protocol: `cp` backup, reverse the comparison (`toBeLessThan` →
`toBeGreaterThan`), run the ONE named test, record the failure verbatim,
restore, `cmp`, re-run green.

```
$ cp apps/web/e2e/compensation.spec.ts /tmp/compensation.spec.ts.bak
$ sed -i "106s/toBeLessThan(orderCancelledIndex)/toBeGreaterThan(orderCancelledIndex)/" apps/web/e2e/compensation.spec.ts
$ node --env-file-if-exists=../../.env ./node_modules/@playwright/test/cli.js test e2e/compensation.spec.ts
```

Verbatim failure:
```
  1) [chromium] › e2e/compensation.spec.ts:38:1 › a .99 order is cancelled with credit_rejected, and the timeline shows both compensation steps in order with the causal link rendered

    Error: stock.released.v1 must appear BEFORE order.cancelled.v1 in the rendered timeline

    expect(received).toBeGreaterThan(expected)

    Expected: > 4
    Received:   3

      106 |   expect(stockReleasedIndex, 'stock.released.v1 must appear BEFORE order.cancelled.v1 in the rendered timeline').toBeGreaterThan(orderCancelledIndex);
```

The mutated (reversed) comparison failed against the real, unchanged saga —
`stockReleasedIndex` (3) genuinely precedes `orderCancelledIndex` (4) in the
rendered timeline every time. This proves the ORIGINAL `toBeLessThan`
assertion has power: an inversion (cancellation rendered before release)
would flip these indices and the original assertion would fail exactly the
way this deliberately-reversed one did.

```
$ cp /tmp/compensation.spec.ts.bak apps/web/e2e/compensation.spec.ts
$ cmp /tmp/compensation.spec.ts.bak apps/web/e2e/compensation.spec.ts && echo "cmp: identical, restore confirmed"
cmp: identical, restore confirmed
$ node --env-file-if-exists=../../.env ./node_modules/@playwright/test/cli.js test   # 3 passed
```

**On D4 (compensation.spec.ts's own comment, ported from #7).** #7's
`compensation.spec.ts` explains that this ordering assertion is a
*probabilistic* guard against amendment A1's defect class (a random
`eventId` tie-break, `bf59af9` in #7): it catches a full, deterministic
inversion (which Arm 2 above confirms it does), but is not guaranteed to
catch every instance of a random tie-break, because the `.99` credit
refusal that drives this scenario is itself deterministic. The
*deterministic* guard for that defect class is feature 31's black-box
`assertCausalOrder` (checked for ALL timeline entries, not just this one
pair) — this e2e test is a real-browser confirmation layered on top of that
guard, not a replacement for it. Cited, not re-derived: I did not re-probe
`assertCausalOrder` itself as part of this feature (out of scope — feature
31 owns and armed that guard).

## Defeat-list statement

Applicable rows, judged for these specs specifically (most rows target
guards written into source, not e2e assertions against a real, running
system — the brief's own guidance that rows 1, 9 and 12 are the relevant
ones for e2e specs matched what I found):

- **Row 1 (delete the behaviour).** Every terminal-state wait
  (`invoiced`/`cancelled`/`completed`/`paid`) is, by construction, what
  fails first and loudest if the underlying saga step it depends on is
  deleted or broken — a 30s timeout with the locator's last-seen value in
  the failure message, not a silent pass. Not separately armed beyond Arm 1
  (which is exactly this row for the `completed` claim) and Arm 2 (this row
  applied to the ordering claim, since "no ordering" and "wrong ordering"
  both defeat `toBeLessThan`).
- **Row 9 (satisfy the closer half, leave the premise stale).** This is
  exactly what Arm 2 targets: the "closer half" (both facts present in the
  timeline, asserted separately by the two `toBeGreaterThanOrEqual(0)`
  checks) could be satisfied while the ORDERING premise is stale (wrong
  order) — Arm 2 proves the ordering assertion does not let that pass.
- **Row 12 (serve the failure through a path the population never
  drives).** Directly relevant to the Finding above: the original
  `getByLabel`/`getByRole(name)` locator for the product select would have
  "passed" by accident on a fresh install (before the server's counter had
  drifted) and only failed non-deterministically depending on how many
  prior SSR requests the running `next start` process had served — a path a
  real population of manual test runs could easily miss. Rewriting to the
  structural `getByTestId('order-line').locator('select')` locator removes
  that non-determinism from the TEST (not from the underlying defect, which
  is reported above, not fixed).
- Rows 2/3/7/8/10/11 (corrupt a field, substitute a sibling identifier, drop
  an optional element, dead region, raw string, literal-vs-literal) were
  judged not to apply here: these specs assert against a real running
  system's real output, not a hand-supplied fixture a mutation could
  corrupt independent of the system under test, and there is no dead/raw
  region or optional element in play. Row 4/5/6 (shadow in a comment,
  `#if`, syntax the instrument doesn't recognise) do not apply — Playwright
  loads and runs the literal `.spec.ts` file, no such indirection exists.

## Verify — counts

- Fresh `scripts/dev-stack.sh start` (docker compose infra + 6 .NET services
  + web, prod mode), then `pnpm run test:e2e`.
- One genuine environment blocker hit and worked around (not a code defect,
  not touched via `src/`): `Projector` and `Notifications` failed to start
  (`System.IO.IOException: The configured user limit (128) on the number of
  inotify instances has been reached...`) — this workstation's per-user
  `fs.inotify.max_user_instances` (128) was exhausted by the desktop
  environment and other running apps, unrelated to #8. Raising the sysctl
  needs `sudo`, unavailable here (same as the Playwright `--with-deps`
  attempt). Worked around by exporting
  `DOTNET_hostBuilder__reloadConfigOnChange=false` in my own shell before
  calling `scripts/dev-stack.sh start-service <name>` — this only disables
  ASP.NET Core's `appsettings.json` hot-reload file watcher (which is what
  was consuming the inotify instances), and required no edit to any file
  (dev-stack.sh's `load_env` applies the caller's exported environment last,
  so this needed no change to the script itself, which is out of this
  feature's touch scope anyway).
- Selector fixes needed during development (both now in the final files, not
  separate from the counts below): `getByLabel` → `getByRole('combobox',
  {name})` for Retailer/Company (the fallback-`<Input>`-during-load race),
  and → `getByTestId('order-line').locator('select')` for Product (the
  label/id-mismatch defect, reported above).
- **Runs against the fully-up stack, after the fixes above, counting only
  runs of the real two-spec suite (not the two deliberate single-spec arming
  runs, listed separately):**
  1. Run 3 (first clean attempt after fixes) — 3 passed (2 specs + setup),
     5.8s.
  2. Run 4 — 3 passed, 6.1s.
  3. Run 5 — 3 passed, 5.3s.
  4. Final run (after both arms restored and `cmp`-confirmed identical) — 3
     passed, 4.8s.
  - **4 consecutive full green runs, 0 flaky.**
- **Arming runs (both expected to fail, and did — see Arm table above):**
  1. Arm 1 (happy-path.spec.ts only, `completed`→`invoiced` mutation) — 1
     failed (expected), verbatim failure recorded above.
  2. Arm 2 (compensation.spec.ts only, `toBeLessThan`→`toBeGreaterThan`
     mutation) — 1 failed (expected), verbatim failure recorded above.
- Stack torn down cleanly at the end: `scripts/dev-stack.sh stop` reported
  "nothing left running, no port this stack bound is still held"; `docker
  compose -f docker-compose.infra.yml down` removed every container this
  session started; confirmed after both with `pgrep -af "dotnet run|next
  (dev|start)|OrderToCash\."` (no matches beyond the grep command itself)
  and `docker ps` (empty).

## `feature_list.json`

Only id 32's `status` line changed, `"pending"` → `"in_review"` (the
`"in_progress"` intermediate value was already set by the leader before this
session started, confirmed via `git diff` before I touched anything).
`git diff feature_list.json` shows exactly that one line.

## What I could not do / did not do

- Did not touch `apps/web/src/features/orders/place-order-form.tsx` to fix
  the label/id defect found above — outside this feature's touch scope, and
  it does not block either acceptance bullet (see Finding).
- Did not add a root `package.json` `test:e2e` shortcut or a
  `docker-compose.apps.yml`-based invocation — the brief is explicit that
  the latter is phase 23's job, and the former (a root shortcut) is outside
  the touch scope this brief authorised (`apps/web/package.json` only).
- Did not modify `.gitignore` — avoided by choosing the `test-results/`
  storage-state path instead (see Design choices).
- Left the Chromium browser installed under `~/.cache/ms-playwright/`
  (machine-global, not part of the repo) — `INSTALLATION_COMPLETE` markers
  present for `chromium-1234` and `chromium_headless_shell-1234`.
