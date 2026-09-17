# Implementation record — id 99 `web_apps_name_their_stack` (#8 half)

Scope: acceptance bullets 1, 3, 4 and 5 (bullet 2 is #7's and is done by another agent in `order-to-cash-nestjs`). `sdd: false`, so there is no spec folder; the acceptance list in `feature_list.json` is the spec. `feature_list.json` was not touched (the brief said so, and the entry itself says its status stays `pending`).

## What was built

The label `#8 · .NET / Next.js` now appears in three places, all read from one definition.

| Place | Where it renders | How |
|---|---|---|
| Header of every signed-in page | `apps/web/src/app/(app)/layout.tsx`: the right-hand header group, before the user name and **Log out** | `<StackLabel />` |
| Login page | `apps/web/src/app/login/page.tsx`: under the sign-in card (`main` is now `flex-col gap-4`) | `<StackLabel />` |
| Browser-tab title | `apps/web/src/app/layout.tsx`: `metadata.title` | `APP_TITLE` = `Order-To-Cash · #8 · .NET / Next.js` |

- The single definition is `apps/web/src/lib/stack-label.ts`, which exports `STACK_LABEL` and `APP_TITLE`, the second built from the first.
- `apps/web/src/features/shell/stack-label.tsx` renders the label as the existing shadcn `Badge` with `variant="outline"`, `font-normal text-muted-foreground`, and `data-testid="stack-label"`. It is small and grey, and it matches the header's existing muted text.
- The header's right-hand group also got `flex-wrap`, so it can wrap at narrow widths.

## Files touched

- new: `apps/web/src/lib/stack-label.ts`
- new: `apps/web/src/features/shell/stack-label.tsx`
- new: `apps/web/src/app/stack-label.test.tsx`
- edited: `apps/web/src/app/(app)/layout.tsx`, `apps/web/src/app/login/page.tsx`, `apps/web/src/app/layout.tsx`
- this record

Nothing under `src/`, `tests/`, `scripts/` or `specs/shared/` was touched, and neither were `feature_list.json` or `CLAUDE.md`. No package was added to `apps/web`. `puppeteer-core@25.11.0` and `iron-session` were installed in the session scratchpad only, for the browser check.

## Tests: `apps/web/src/app/stack-label.test.tsx` (11 cases)

| Acceptance bullet | Case(s) | Unit |
|---|---|---|
| 1 / 4: signed-in header | `/billing`, `/orders`, `/orders/[id]`, `/orders/place`, `/stock` → `shows the label where it belongs: signed-in header` | **page**: each page file on disk is rendered inside its real layout chain by `runRoute` in `src/test/page-sweep.tsx`. The label must be inside `<header>`. |
| 1 / 4: login page | `/login` → `shows the label where it belongs: login` | page; the label must be inside `<main>` |
| 1 / 4: tab title | `names the stack in the browser-tab title (src/app/layout.tsx metadata.title)` | the exported `metadata` |
| 1 | `is the reviewed text` | the definition, checked against a literal written in the test |
| 3: one definition | `has one definition: no other source file writes the label text` | **source file**: every non-test file under `src/`, read from disk (excluding `src/test/` and `*.test.*` by path). The pattern `/\.NET\s*\/\s*Next\|#8\s*·/gi` matches other spellings too, not only the exact label. The expected hits are a literal: exactly the two in `lib/stack-label.ts`. |
| population | `/ (page.tsx)` → `shows the label where it belongs: redirect`; `the public page set names only pages that exist` | The page population is `ROUTE_FILES` (the filesystem glob). The public set `{'/': redirect, '/login': login}` is a literal, and every other page counts as a signed-in page by subtraction. If a page stops being wrapped by the `(app)` layout, it fails this test instead of dropping out of the population. |

No `R<n>` applies: id 99 is a backlog entry, not an EARS requirement, so `specs/shared/test-matrix.md` has no row for it. That file is also read-only for this brief.

### Bullet 5: the behavioural error sweep

`src/app/error-text-sweep.test.tsx` flags text that a *failing* run adds compared with its all-success baseline (`addedTexts`). The label is static and appears in both runs, so it is never added text, and `LABELS` did not need a new entry. No error text was changed: no file that renders an error was edited. The full sweep passed in the full run below.

## Arming table

Method for each arm: `cp` a backup to the scratchpad, apply the mutation, run `pnpm exec vitest run src/app/stack-label.test.tsx`, restore with `cp` + `touch`, then check the restore with `cmp`. All four restores printed `ok`, and the confirming run was `Tests 11 passed (11)`. Vitest transforms from source on every run, so there is no stale binary to rebuild.

| # | Mutation | Result | Failure message (verbatim, first line) |
|---|---|---|---|
| A1 | Delete `<StackLabel />` from `(app)/layout.tsx` | 5 failed / 6 passed | `signed-in header of /billing ((app)/billing/page.tsx): the stack label is missing: expected [] to include 'header: #8 · .NET / Next.js'` (plus one line each for `/orders/[id]`, `/orders`, `/orders/place`, `/stock`) |
| A2 | Delete `<StackLabel />` from `login/page.tsx` | 1 failed / 10 passed | `login page of /login (login/page.tsx): the stack label is missing: expected [] to include 'main: #8 · .NET / Next.js'` |
| A3 | `title: APP_TITLE` → `title: 'Order-To-Cash'` | 1 failed / 10 passed | `browser-tab title: src/app/layout.tsx metadata.title does not contain the stack label: expected 'Order-To-Cash' to contain '#8 · .NET / Next.js'` |
| A4 | Substitute the sibling label: `STACK_LABEL = '#7 · NestJS / Nuxt'` | 9 failed / 2 passed | `src/lib/stack-label.ts: the label is not the reviewed one: expected '#7 · NestJS / Nuxt' to be '#8 · .NET / Next.js'`, plus the title case, all 5 header cases, the login case and the one-definition case |
| A5 | Replace the login `<StackLabel />` with a hard-coded `<span data-testid="stack-label">#8 · .NET / Next.js</span>` (a drifted copy) | 1 failed / 10 passed | `the stack label is written in more than one place (or not in src/lib/stack-label.ts): expected [ 'app/login/page.tsx: #8 ·', …(3) ] to deeply equal [ 'lib/stack-label.ts: #8 ·', …(1) ]` |
| A6 | Move `<StackLabel />` from the header into `<main>` of `(app)/layout.tsx` | 5 failed / 6 passed | `signed-in header of /billing ((app)/billing/page.tsx): the stack label is missing: expected [ 'main: #8 · .NET / Next.js' ] to include 'header: #8 · .NET / Next.js'` (plus one line for each other page) |

Every failure message names the place that lost the label.

### Defeat list (CLAUDE.md's ten attacks)

1. **Delete:** tested by A1, A2 and A3.
2. **Corrupt a supplied value:** A4 changes the value, and the test pins the literal instead of importing it.
3. **Substitute a sibling:** A4 uses #7's label, the one real sibling.
4. **Shadow in a comment or string:** A5's hard-coded copy is caught by the content scan. A comment containing the label would also be caught, because the scan reads raw text and counts it (stricter, by design). The render checks read the DOM, where comments do not appear.
5. **Hide in a dead region:** not applicable to the render checks, because they execute the real layout. A copy inside `{false && …}` would still be found by the content scan.
6. **Hide in a raw string:** the content scan is a regex over raw text with no parser, so it cannot misparse a raw string.
7. **Drop an optional element:** A1 and A2 remove the element entirely, and the expected value is a required list member, not a comparison of values that are present.
8. **Compare a literal to a literal:** the population comes from the filesystem (`ROUTE_FILES`, `readdirSync`), and the walk asserts that it found `lib/stack-label.ts`.
9. **Two-part claim:** the claim is "shown, and in the right place". A6 moves the label and does not delete it.
10. **Build-output copies:** the scan walks `src/` only, and `.next/` and `coverage/` are outside it.

Not armed: moving a page out of the `(app)` group. That changes a real route, and the subtraction design already covers it, since such a page would still be in `ROUTE_FILES` without being in `PUBLIC_PAGES`.

## Verification (this session; `apps/web`)

- `pnpm exec vitest run`: **21 files / 278 tests passed**. Before this change it was 20 / 267. The difference is +1 file and +11 tests, exactly the new file's 11 cases.
- `pnpm run lint`: exit 0 (`lint-coverage OK — all 102 source files (93 under src/)`).
- `pnpm run typecheck`: exit 0.
- `QUALITY_ONLY=web ./quality.sh`: `exit 0`, and every step was OK: install, OpenAPI types, lint, typecheck, Vitest with coverage (21 / 278), production build, and integration tests against that build (1 file / 7 tests). No build or test process of this repository was running when it started. `pgrep` did show Vitest processes belonging to the #7 agent in `order-to-cash-nestjs`, which is a separate checkout that shares nothing with this one.

### Real rendered page (production build from the `quality.sh` run, `next start` on port 3017, headless Chrome through `puppeteer-core`, sealed session cookie, Gateway unreachable)

```
@390px /login        → title "Order-To-Cash · #8 · .NET / Next.js", label in main,   pageOverflowsX false
@390px /orders       → label inHeader true, box [24,64,117,22], pageOverflowsX false, overflowing [], overlaps [], headerHeight 143 (display name "Operator With A Long Display Name")
@390px /orders/place → same
@390px /stock        → same
@390px /billing      → same
@390px /orders       → with display name "Operator": label [24,69,117,22], headerHeight 109, no overflow, no overlap
@1280px all five     → label in header at x=736, headerHeight 57, no overflow, no overlap
```

I looked at the screenshots. At 390 px, the nav wraps onto two lines as before. The badge sits on the next line beside the user name and **Log out**; with a long display name, the badge gets a line to itself. Nothing is clipped. At 1280 px, the header is a single line with the badge just before the user name. On the login page, the badge is centred under the card. The server was stopped afterwards, including the `next-server` child process that the first `kill` left behind. Port 3017 was checked free.

## Ported-idiom ledger

None owed. #8 does not port this from #7. I checked #7's committed tree with `git grep -n -i -E "NestJS ?/ ?Nuxt|#7 ·" HEAD -- apps/web` in `order-to-cash-nestjs`, which returned nothing. #7's header brand (`apps/web/app/layouts/default.vue:21`) and login title (`apps/web/app/pages/login.vue:54`) say only `Order-To-Cash`. Bullet 2 adds the same idea to #7 in parallel, but neither side copies a mechanism from the other.

## Could not do / notes

- The page sweep's harness (`runRoute`) cleans up the DOM before it returns. To check *where* the label renders, the test passes an `ActionScript` that reads the DOM while the page is still mounted. It makes no requests, so it does not disturb the harness.
- The content scan also matches spellings other than the exact label. A future doc comment in `src/` that mentions `.NET / Next` would fail it, which I think is the right trade for bullet 3.
- A `pgrep -fl` check matched its own shell once (`79365 bash`). It was not a build.
