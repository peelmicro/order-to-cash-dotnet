# impl — backlog id 101 `stock_page_repeats_the_product_code` (#8 and #7 web apps)

## Contract read

`feature_list.json` id 101, five acceptance bullets (verbatim, read from disk):

1. each stock row shows the product's name from the catalog the app already reads (`GET /catalog/products`), with the code beside it; a product with no known name shows its code once, never twice
2. a `StockItem.productName`, if a backend ever sends one, still wins over the catalog
3. a catalog failure does not hide the stock table: rows fall back to the code alone, and #8's behavioural error sweep still passes, or is extended if the new request joins the stock page's population
4. the same change in #7's stock page
5. tests in each app cover name-from-catalog, the code-only fallback with no duplicate, and productName precedence; each is armed

Status left at `pending` — the brief explicitly forbids touching `feature_list.json`; the maintainer/reviewer makes the transition.

## Design answers (verified, not assumed)

- **Is `/catalog/products` scoped by company or paginated?** No to both. `specs/shared/openapi.yaml` `GET /catalog/products` takes only `IncludeDisabled`; its 200 schema is `{ items: Product[] }` with no `page`/`PageInfo` — unlike `/stock` (`StockPage`, which *does* carry `page`). No company parameter exists.
- **Can two companies have the same product code with different names?** No. `Product` is a single, company-agnostic catalogue row: `src/Orders/Infrastructure/Persistence/Entities/Product.cs` has no `CompanyCode`, and `src/Orders/Infrastructure/Persistence/Configurations/ProductConfiguration.cs:18` puts a unique index on `Code` — `builder.HasIndex(p => p.Code).IsUnique();`. `StockItem.companyCode` is which company's *stock* the row describes, not a scope on the product.
- **Does one request return every product the stock page can show?** Yes, for enabled products (the only case in scope here): the endpoint is unpaginated and returns the whole non-paginated catalogue; `Product.Code` is globally unique, so a `Map<code, name>` built from one response resolves every stock line's `productCode`. (A stock line for a *disabled* product would not resolve without passing `includeDisabled`, exactly as the existing `useProducts()`/`useProductsQuery()` hooks already accept for the place-order form — out of scope here, not introduced by this change, and it degrades to the pre-existing single-code fallback rather than a duplicate.)

## What was built

Both apps: the product cell now resolves `item.productName ?? catalogNameByCode.get(item.productCode)`, and shows the code **once**, alone, only when neither is known. A new non-blocking banner shows a failed catalog read without hiding the stock table.

- **#8** (`apps/web/src/features/stock/stock-view.tsx`):
  - `useProducts()` (existing hook, already used by the place-order form) is now also called here; `productNameByCode` is a `useMemo`'d `Map<code, name>`.
  - Exported pure helper `productDisplayName(item, catalogNameByCode)` for the precedence rule.
  - Row cell: `data-testid="stock-product"` added; shows `{name} ({code})` when a name is known (backend or catalog), else the bare code.
  - New banner, placed like `billing-view.tsx`'s `retailers.isError` banner (a non-blocking, top-of-page notice, never gating the table): `{products.isError ? <ErrorMessage prefix="Product names unavailable" testId="stock-products-error" .../> : null}`.

- **#7** (`apps/web/app/pages/stock/index.vue`):
  - `useProductsQuery()` (existing composable, already used by `orders/place.vue`) is now also called here; `productNameByCode` is a `computed` `Map`.
  - `productDisplayName(item)` function, same precedence.
  - Row cell: `data-testid="stock-product"` added, `v-if`/`v-else` for name-vs-code-only.
  - New banner mirroring the same non-blocking pattern, `data-testid="stock-products-error"`, `Product names unavailable: {{ describeFetchError(...) }}`.

## The behavioural error sweep (#8) — extended, no exception entry needed

Read `apps/web/src/app/error-text-sweep.test.tsx` and `progress/impl_web_app.md` first, per the brief.

The new `GET /api/catalog/products` request on `/stock` **does** join the sweep's population automatically (it is observed at the `apiRequest` seam like every other read), but three of the sweep's literal, reviewed lists needed updating to describe it correctly — otherwise the sweep's own consistency checks (not the new request's failure) would have failed:

- `EXPECTED_LOAD['/stock']`: added `'GET /api/catalog/products'` (sorted before `GET /api/stock…`).
- `REQUEST_UNITS`: `useProducts` now has two consumers; the line became `hooks/use-catalog.ts#useProducts query | features/orders/place-order-form.tsx, features/stock/stock-view.tsx`.
- `LABELS`: added `'Product names unavailable:'` — the new banner's own reviewed prefix, produced by the load-phase sweep when it fails this key with a 503 (the only failure status `openapi.yaml` declares for `/catalog/products` outside 401).

**No `NOT_SHOWN` exception entry was needed.** I deliberately designed the catalog failure to be *shown* (as a small, non-blocking banner, exactly like the existing `orders-list.tsx`/`billing-view.tsx` retailer-filter-unavailable pattern), rather than silently swallowed — so the failure passes through the sweep's ordinary "the sentinel is on screen, labelled" path instead of needing a documented, unverifiable exception. This is the direct answer to defeat-list row 12 (*"serve the failure through a path the population never drives"*): the catalog failure on `/stock` is now driven by the same load-phase population sweep as every other catalog consumer (`/orders/place`), not a special-cased path nothing exercises. Confirmed by running the full sweep file both with the mutation present (fails, see arming below — not applicable to the sweep itself, but the sweep's own run below is the population check) and clean (56/56 green, including the new label being produced and both `detail`/`title` variants failing `GET /api/catalog/products` on `/stock`).

Ran `apps/web/src/app/error-text-sweep.test.tsx` standalone: **56/56 passed** (88.7s).

## Tests (each app) — 4 new cases, id 101

- `apps/web/src/features/stock/stock-view.test.tsx` — 4 new tests, existing 10 updated to register a (mostly empty) `GET /api/catalog/products` route (the app now makes that request on every render; `routeApi()` throws loudly on an unrouted call).
- `apps/web/app/pages/stock/index.spec.ts` — 4 new tests, existing 9 updated the same way (`registerEndpoint('/api/catalog/products', ...)` before every render).

Cases (both apps, same shape):
1. the name comes from the catalog when the stock line carries none.
2. the code appears only once with no known name — asserted by counting occurrences of the code inside the product cell's own text (`data-testid="stock-product"`), not a substring match on the whole row.
3. `productName` (backend fact) takes precedence over the catalog's `name`.
4. a catalog failure (a real captured 503 in #8 via `catalog-products-upstream-unavailable-503.json`; an equivalent `createError` in #7) still renders the row (falls back to the code), and the new banner shows separately — `stock-error`/`stock-empty` are absent.

## Arming (all four cases, both apps) — CLAUDE.md's protocol

Backup taken before mutating (`cp` to the scratchpad), each mutation applied, the specific named tests run and the FAIL message recorded verbatim, then restored **from the backup** (never `git checkout --`, both apps' `apps/web` trees are entirely untracked in #8 and partly modified-in-place in #7), `touch`ed to force a fresh transform, confirmed by `cmp` **and** by re-reading the changed line, then re-run green.

| # | Attack | #8 result (`stock-view.test.tsx`) | #7 result (`index.spec.ts`) |
|---|---|---|---|
| 1 | restore the old template (`item.productName ?? item.productCode` + unconditional `(code)`) | 2 of 4 new tests FAIL: "the name comes from the catalog" (`Expected: Ration Pack Bundle (catalog) (PRD-0001) / Received: PRD-0001 (PRD-0001)`), "the code appears only once…" (`expected 2 to be 1`) | 3 of 4 FAIL (same two, plus the catalog-failure test, since its row also duplicates the code): messages `expected 'PRD-0001 (PRD-0001)' to be 'Ration Pack Bundle…'`, `expected 2 to be 1`, `expected 'PRD-0001 (PRD-0001)' to be 'PRD-0001'` |
| 2 | ignore the catalog (`productNameByCode` forced to an always-empty `Map`) | 1 FAIL: "the name comes from the catalog" — `Expected: Ration Pack Bundle (catalog) (PRD-0001) / Received: PRD-0001` | 1 FAIL: same test, `expected 'PRD-0001' to be 'Ration Pack Bundle (catalog) (PRD-0001)'` |
| 3 | invert the precedence (`catalogName ?? item.productName`) | 1 FAIL: "productName…takes precedence" — `Expected: On the stock line (PRD-0001) / Received: From the catalog — should lose (PRD-0001)` | 1 FAIL: same test, `expected 'From the catalog — should lose (PRD-0001)' to be 'On the stock line (PRD-0001)'` |
| 4 | substitute a sibling field (`product.description ?? product.name`) | 1 FAIL: "the name comes from the catalog" — `Received: not the display name — a decoy sibling field (PRD-0001)` | 1 FAIL: same test, same message shape |

Arm 4 required strengthening the fixture first: the `product()`/`makeProduct()` factories originally left `description` unset, so the substitution silently fell through to `?? product.name` and the mutation did not fail on the first attempt in #8 (confirmed: ran green with the mutation live). Fixed by giving both factories a `description` that always differs from `name` (`'not the display name — a decoy sibling field'`), confirmed the mutation now fails, then applied the same fixture shape to #7's `makeProduct` from the start (so #7 caught it on the first attempt).

After every restore, the corresponding full test file was re-run green: #8 `stock-view.test.tsx` 14/14, #7 `index.spec.ts` 13/13 (confirmed multiple times across the four restores).

## Defeat list (CLAUDE.md rows 1–12), applied to my own guard

grepped from disk (`CLAUDE.md` lines ~318–333) before writing this, not quoted from memory.

| # | Attack | Applies? | Why |
|---|---|---|---|
| 1 | Delete the behaviour | Yes — this is arm 1 (restore the old template), which is "delete the fix". Ran; failed as recorded. |
| 2 | Corrupt a payload field the test supplied | Yes — the catalog `name`/`description` values are test-supplied; arm 4 is exactly this against the `name` field via a sibling. |
| 3 | Substitute a valid sibling identifier | Yes — arm 4, `description` for `name`. Both are real `Product` fields. |
| 4 | Shadow the pattern from a comment/string | N/A — this is a rendered-DOM assertion (`textContent`), not a source-text scanner; there is no pattern-matching instrument to shadow. |
| 5 | Hide the real thing in a dead region (`#if false`) | N/A — same reason; no conditional-compilation instrument involved (TS/Vue, not C#). |
| 6 | Hide it in a raw/verbatim string | N/A — same reason. |
| 7 | Drop an OPTIONAL element entirely | Considered: `productName` and the catalog `name` are both optional at the type level. Arm 2 ("ignore the catalog") is exactly "the catalog's optional contribution disappears", and it is caught. |
| 8 | Compare a literal to a literal | N/A — no population/tree-walking check here; these are behavioural render assertions against live component output. |
| 9 | Satisfy the closer half of a two-part claim, leave the premise stale | Considered for the "code once" claim, which has two halves ("code is shown" AND "not duplicated"). The test asserts both explicitly (`toMatch(/PRD-0007/)` then the exact count), so satisfying only the first half fails the second. |
| 10 | Let a build-output copy join the population | N/A — no population sweep/build-output scan involved in these four unit tests (the *sweep* file itself already excludes build output by construction, unchanged by this feature). |
| 11 | Write the claim in a form the instrument doesn't recognise | N/A here (behavioural DOM tests, not a syntax scanner) — but this is exactly why the *sweep*-side change (EXPECTED_LOAD/REQUEST_UNITS/LABELS) was done as data the sweep's own tests check for staleness both ways, rather than a special-cased skip. |
| 12 | Serve the failure through a path the population never drives | Yes, directly addressed above: the catalog-failure path on `/stock` is driven by the sweep's ordinary load-phase population (`EXPECTED_LOAD['/stock']`), not a side channel. Confirmed via the full sweep run (56/56, including the label being *produced*, per `producedLabels`/`LABELS` staleness check). |

## Verify (both apps; `./quality.sh` not run, per the brief)

- **#8** `apps/web`: `pnpm exec vitest run` — **21 files / 282 tests**, all passed (baseline 21/278; +4 reconciles exactly to the 4 new tests, no other file's count moved). `pnpm run lint` — clean (`lint-coverage OK — all 102 source files (93 under src/) are linted`). `pnpm run typecheck` — clean. `pnpm run types:check` — clean (`src/generated/openapi.ts` still matches a fresh generation; not touched by this feature).
- **#7** `apps/web`: `pnpm test` — **18 files / 133 tests**, all passed (baseline 18/129; +4 reconciles exactly). `pnpm run lint` — clean (no output, exit 0). `pnpm run typecheck` (`nuxi typecheck`) — clean, exit 0.
- No test run overlapped another (checked `pgrep -fl "dotnet (build|test|format)"`/`vitest` before every run; none of mine ran in the background — each `vitest run`/`pnpm run lint`/`typecheck` invocation was foreground and completed before the next started).

## Scope discipline

Touched only:
- `apps/web/src/features/stock/stock-view.tsx`, `apps/web/src/features/stock/stock-view.test.tsx`, `apps/web/src/app/error-text-sweep.test.tsx` (#8)
- `apps/web/app/pages/stock/index.vue`, `apps/web/app/pages/stock/index.spec.ts` (#7)
- `progress/impl_stock_page_repeats_the_product_code.md` (this file)

Did not touch `specs/shared/`, `CLAUDE.md`, `feature_list.json`, or anything under `Projector`/`Seed` in either repository (the concurrently-worked feature; confirmed via `git status` that only that other work's files show as modified elsewhere in the tree, none touched by me). No git command that writes the index or working tree was run (only `cp` from my own scratchpad backups for restores, plus `touch`); no commit made.
