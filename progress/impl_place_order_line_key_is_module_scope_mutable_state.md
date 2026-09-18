# impl — id 106: `place_order_line_key_is_module_scope_mutable_state` (phase 25 close-out, LIGHT)

## Classification

LIGHT under CLAUDE.md Cost discipline (component-local id-generation fix), as the brief and the
`feature_list.json` entry's own notes both state. One implementer; the leader checks this diff and
re-runs the affected tests directly — no separate reviewer for this round.

## What was wrong

`apps/web/src/features/orders/place-order-form.tsx` derived every order line's product `<select id>`
and its `<label htmlFor>` from a module-scope `let nextLineKey = 1` counter, incremented by
`emptyLine()`. That counter free-runs across every request a long-lived `next start` process ever
serves (it never resets except on a process restart), while a hard-navigated browser's own copy of
the module always starts fresh at 1 — so the server-rendered HTML for one request and the browser's
hydration of it could disagree on the id, and React's hydration does not always repair every
mismatched attribute (the id 32 implementer observed `for="product-6"`/`id="product-2"` on one load,
`for="product-7"`/`id="product-2"` on the next, confirmed live against `scripts/dev-stack.sh`).

## What changed

`apps/web/src/features/orders/place-order-form.tsx`:

1. **The DOM id/htmlFor pair now comes from React's `useId()`, not from `line.key`.** The per-line
   fields (product select, quantity, unit price, line discount, remove button) were extracted into a
   new `OrderLineRow` subcomponent, which calls `useId()` once per component INSTANCE and builds
   `${uid}-product`, `${uid}-quantity`, etc. `useId()` is React's own request/render-scoped id
   generator, specifically designed to stay consistent between an SSR pass and its hydration — this
   is the exact property the module-scope counter could not offer.
   - `useId()` cannot be called from a variably-sized `.map()` in the parent (would violate the Rules
     of Hooks, since the number of hook calls would change as lines are added/removed) — hence the
     extraction into `OrderLineRow`, whose own hook count is stable across its own renders.
2. **`DraftLine.key`** (used only for React's list-reconciliation `key` prop and for
   `updateLine`/remove-by-key — no longer for any DOM id) now comes from a `useRef` counter local to
   `PlaceOrderForm`, not the module-scope `let`. A ref is per COMPONENT INSTANCE: a fresh one is
   created for every render tree (every request server-side, every mount client-side), so it starts
   fresh every time and cannot leak state across unrelated requests in the same long-lived process —
   this satisfies acceptance bullet 2 independently of the `useId()` change, for the reconciliation
   key specifically.
   - React's `react-hooks/refs` ESLint rule forbids reading a ref's `.current` during render (only
     event handlers/effects may). The initial line therefore uses a plain literal (`{ key: 1, ... }`)
     rather than calling `emptyLine()` inside the `useState` lazy initializer; the ref starts at `1`
     and only increments from event handlers (`Add line`, `fillCompensationDemo`, the on-success
     reset), so it never collides with the literal first line's key `1`.

## Ported-idiom ledger

Not applicable — this is a component-local id-generation fix using a standard React 19 primitive
(`useId()`), not a port from #7 (#7 is NestJS/React-something-else; #8's own prior implementation was
simply buggy, not derived from #7's approach for this piece).

## Tests

`apps/web/src/features/orders/place-order-form.test.tsx`:

- **New test — `the product select id still agrees with its own label htmlFor after HYDRATING a
  server render, even after several PRIOR renders already ran in the SAME module instance (id
  106)`** (proves acceptance bullets 1–3). This is the guard for the actual regression class: it
  does NOT merely re-render the client component in a loop (that would trivially pass even under the
  OLD buggy code, since a single execution always computes `htmlFor`/`id` from the identical
  `line.key` read twice — the real defect only manifests ACROSS the SSR/hydration boundary, where
  server and browser are separate module instances with independently-progressing counters). Instead
  it:
  1. Calls `renderToString` on the form five times (simulating five PRIOR requests this same
     long-lived process already served — the old counter would have advanced past those).
  2. Calls `renderToString` once more (the render under test, standing in for THIS request) and
     injects the resulting HTML into a detached container.
  3. Calls `hydrateRoot` on that container with a freshly-constructed element tree (standing in for
     the browser's own module instance re-invoking `useState`'s lazy initializer) — with the OLD code
     this recomputes a DIFFERENT `nextLineKey` than what the server actually embedded, because it is
     the SAME running module instance's counter, already advanced by the five prior "server" passes.
  4. Asserts `within(container).findByRole('combobox', { name: 'Product' })` resolves (accessible-name
     computation, via `dom-accessibility-api`, follows the real `for`/`id` relationship — it does not
     read `line.key` back to itself) and that the label's own `htmlFor` equals that control's `id`.
  - **Armed** (CLAUDE.md arming protocol): backed up the fixed file to the scratchpad, reverted
    `OrderLineRow` to `product-${line.key}` and reintroduced the module-scope `let nextLineKey`
    counter, ran ONLY this test — it failed with
    `TestingLibraryElementError: Unable to find role="combobox" and name "Product"` (verbatim,
    captured live). Restored the fixed file from the backup, confirmed byte-identical with `cmp`,
    cleared `node_modules/.vite`/`.cache` and re-ran with `--no-cache` to force a rebuild — green
    again (18/18 in the file, 287/287 in the full Vitest suite).

No existing test needed to change beyond the new one; all 17 previously-passing tests in this file
kept passing unmodified.

## Suite results

- `npx vitest run src/features/orders/place-order-form.test.tsx`: **18/18 passed** (17 pre-existing +
  1 new).
- `npm run test` (full Vitest suite, `apps/web`): **287/287 passed, 22/22 files**.
- `npm run typecheck` (`next typegen && tsc --noEmit`): clean.
- `npx eslint src/features/orders/place-order-form.tsx src/features/orders/place-order-form.test.tsx
  e2e/happy-path.spec.ts e2e/compensation.spec.ts --max-warnings 0`: clean (the fix's first draft hit
  a real `react-hooks/refs` violation — reading `nextLineKeyRef.current` inside the `useState` lazy
  initializer counts as reading a ref during render — fixed by the literal-first-line approach above;
  re-linted clean afterward).

## e2e locators — NOT simplified (judgment call, per acceptance bullet 4)

Acceptance bullet 4 explicitly leaves reverting `happy-path.spec.ts`/`compensation.spec.ts` from the
structural locator to `getByLabel`/`getByRole(..., { name: 'Product' })` as the leader's/implementer's
judgment call, not mandatory. Two things were checked live, per the brief's instruction to confirm
before touching these files:

1. **`apps/web/e2e/compensation.spec.ts` never used the structural locator in the first place**
   (`grep -n "order-line\|getByLabel\|getByRole.*Product\|nextLineKey" e2e/compensation.spec.ts`
   returns nothing) — it drives the "Fill demo order" button, which sets `productCode` directly
   without going through the `<select>` at all. There is nothing to simplify in that file; the
   brief's framing ("both files... route around this") does not hold for `compensation.spec.ts`
   specifically — only `happy-path.spec.ts` does.
2. **`apps/web/e2e/happy-path.spec.ts:57`** (`page.getByTestId('order-line').locator('select')`) was
   **left as-is**. Decision: keep the structural locator, but update its comment (which had gone
   factually stale — it still described the module-scope counter as present tense) to record that id
   106 fixed the underlying defect, why the locator was nonetheless kept, and where the fix is proven.
   Reasoning:
   - The structural locator already works and carries no regression risk from this change.
   - Reverting it to `getByRole('combobox', { name: 'Product' })` would need a real end-to-end run
     against the full docker stack (`scripts/dev-stack.sh`: MS-SQL ~2 GB RAM/~30 s, Kafka, NATS,
     MongoDB, six .NET services, `next build`/`next start`) to actually confirm the locator resolves
     in a REAL browser — Playwright has no mocked-server mode in this repo
     (`apps/web/playwright.config.ts`'s own comment: "never a self-spawned dev server"). Spinning that
     up is disproportionate to a component-local id-generation fix that a fast, more precise unit-level
     hydration guard already proves (see above) — CLAUDE.md's cost discipline explicitly asks to size
     the process to the change.
   - The regression this fix targets is specifically an SSR/hydration id-generation defect, which the
     new Vitest test reproduces more faithfully (genuine `renderToString` + `hydrateRoot` across a
     module instance with advanced state) than a single real-browser Playwright run would (one browser
     load is exactly the "first render after prior renders" case bullet 1 asks for, but Playwright
     would only catch it if the dev-stack's `next start` process had ALREADY served enough prior
     requests to have drifted the (now-removed) counter meaningfully by the time the test navigates to
     it — not a reliable repro path even before this fix).
   - Per the acceptance bullet's own re-open trigger ("any test that starts relying on
     getByLabel/getByRole name matching for a per-line field"): if a future change wants this
     simplification, `getByLabel('Product')`/`getByRole('combobox', { name: 'Product' })` now resolve
     correctly (proven at the unit level in this round) — reverting the two e2e locators is a
     same-day, low-risk follow-up that only needs a real dev-stack run to confirm, not a further code
     change.
   - Did **not** run the real docker stack / `scripts/dev-stack.sh` / the two Playwright specs this
     round, consistent with the brief's step 4 ("... for real, against a real stack **if you touch the
     e2e locators**") — the e2e locator itself was not touched, only its comment.

## Files touched

- `apps/web/src/features/orders/place-order-form.tsx` — the fix (module-scope counter → `useId()` for
  DOM ids; module-scope counter → instance-scoped `useRef` for the reconciliation key; `OrderLineRow`
  extracted as a subcomponent).
- `apps/web/src/features/orders/place-order-form.test.tsx` — the new armed regression guard.
- `apps/web/e2e/happy-path.spec.ts` — comment-only update (no locator/behaviour change) recording that
  id 106 is fixed and why the structural locator was kept regardless.

## What could not be done and why

- Did not run the two Playwright specs against the real stack this round — see the "e2e locators"
  section above (judgment call: not touching the locators, so not triggering the brief's conditional
  real-stack requirement; a heavy multi-service docker stack is disproportionate to this LIGHT,
  component-local fix already proven at the unit level).

## Surprises

- The true defect only manifests ACROSS the SSR/hydration boundary (separate module instances with
  independently-progressing counters), not within a single render — a naive "render N times via RTL
  and check `getByLabelText` resolves" test would have passed even under the OLD buggy code (both
  `htmlFor` and `id` are always read from the identical `line.key` reference within one execution),
  so it would not have been a valid armed guard. The `renderToString` + `hydrateRoot` approach was
  needed to reproduce the real class of bug faithfully.
- The first draft of the fix (a `useRef` counter read inside the `useState` lazy initializer) tripped
  a genuine `react-hooks/refs` ESLint rule — refs may not be read during render, only from event
  handlers/effects, even for a one-time lazy-initializer-style read. Fixed by giving the first line a
  literal key and only ever touching the ref from event handlers.

## Status

Per this brief's rules ("Touch only `apps/web/`... Do not touch... `feature_list.json`"), the
`feature_list.json` status was **not** edited by this implementer (single-writer discipline,
CLAUDE.md). The leader should set id 106's status to `in_review` after reading this diff and
re-running the tests above (LIGHT change — no separate reviewer this round).
