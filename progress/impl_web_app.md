# Implementation record — id 29 `web_app` (with id 30 `web_component_tests`)

**Result: PASS — all six slices of the brief are done.** One implementer session, 2026-09-16, ≈16:30 → 18:20 (my own reading of `date` and of `apps/` mtime 16:33; check file mtimes under `apps/web/` and `logs/dev-stack/` before relying on them).

- `./quality.sh`, full run with the stack stopped, **exit 0**. It covered 18 .NET test projects (2049 tests, 0 failed, counted from the 18 `Passed!` lines of that run) and the new web section: 16 Vitest files with **189** tests, 7 integration tests against the production build, and web line coverage of **92.71%**. After that run, section 5's install step changed `--reporter=silent` to `--reporter=append-only` so a lockfile failure prints its reason. `QUALITY_ONLY=web ./quality.sh` was then run again: exit 0, the same 189 + 7.
- `./init.sh` exit 0, both before and after the work.
- **Every web gate was seen to FAIL** on a planted violation (see "Quality-gate arming").
- **44 behavioural arms** were run against the final code. All 44 killed, and every restore was byte-identical (`filecmp`, then `touch`).
- **Three production-only defects** were found by running the real server and a real browser, and fixed. No unit test could have seen any of them, and each now has an armed guard (see "Found by looking").

`feature_list.json` was **not** changed. The brief (§10) says so explicitly, while my standing instructions say to set the status to `in_review`. The brief governs this dispatch, so the leader should make that transition. `specs/shared/test-matrix.md` was **not** edited either (brief §10). The row text I propose for it is in §2, "Discrepancies".

---

## 1. What was built (slices)

| Slice | Delivered |
|---|---|
| 1. Foundation | `apps/web`: a standalone pnpm project (package name `web`, which `init.sh` refers to) using Next.js 16.3.5 (App Router, Turbopack), React 19.3, TypeScript 5.9.3 strict with `noUncheckedIndexedAccess`, Tailwind v4, shadcn/ui (new-york: button, card, input, label, native-select, table, badge, separator), TanStack Query 5.103, and Vitest 5 + React Testing Library + jsdom. Exact versions are pinned. The OpenAPI types are generated from `specs/shared/openapi.yaml` into `src/generated/openapi.ts` (committed) with a `types:check` drift check. Section 5 of `./quality.sh` runs install, types:check, lint (+ lint-coverage), typecheck, test with coverage, build, and the integration tests. |
| 2. Session + BFF | Sealed `httpOnly` session cookie (`iron-session` `sealData`/`unsealData`), `src/server/{config,session,gateway,stream-proxy}.ts` (all `server-only`), and 12 route handlers under `src/app/api/**`. Problem documents are relayed verbatim. The login page is a real HTML form, and its route handler accepts both JSON and a form post. |
| 3. Orders | List with filters, paging and loading/empty/error states. Place order with catalogue-backed selects, a free-text fallback when the catalogue is down, a currency that follows the retailer, currency-aware decimal inputs, a running total, the `.99` compensation demo, `Idempotency-Key`, and 409 shortages. |
| 4. Detail + live timeline | Named R55 waiting state that retries on the Gateway's `Retry-After`. `OrderStreamClient` de-duplicates per frame type and resumes via `Last-Event-ID` (the EventSource does this itself). React hook `useOrderStream`. The SSE proxy route streams unbuffered, forwards `Last-Event-ID`, aborts upstream on disconnect, and sends an SSE-comment preamble. Also: causal links, A1 timeline ordering for live appends, the #7 D2/D7 backstop re-read, and connection status with a manual retry. |
| 5. Stock + billing | Stock list with filters and a delta replenish (409/404 detail shown). Credits card (paged, retailer filter). Invoices (status and retailer filters) with Register payment: decimal→minor units per currency, accepted and duplicate rendered distinctly with the HTTP status, and a link to the order's live timeline. |
| 6. Error sweep + live verification | Twelve REAL Gateway problem documents were captured into `src/test/fixtures/gateway/` and fed through the real route handlers into every error-rendering test. The real stack was driven through the route handlers and a real headless Chrome (§7). |

### Files

Created: all of `apps/web/**` (sources listed by `find apps/web/src apps/web/scripts apps/web/tests-integration -type f`, 100 files, plus `package.json`, `pnpm-lock.yaml`, `pnpm-workspace.yaml`, `tsconfig.json`, `next.config.ts`, `postcss.config.mjs`, `eslint.config.mjs`, `vitest.config.ts`, `vitest.integration.config.ts`, `components.json`). Also created `scripts/dev-stack.sh`. Modified: `quality.sh` (section 5 and the `QUALITY_ONLY=web` switch). `.gitignore` needed nothing: `node_modules/`, `.next/`, `coverage/`, `next-env.d.ts`, `*.tsbuildinfo` and `logs/` were already covered. `git status --short --ignored apps/web` lists exactly those five as ignored.

### Packages (exact versions — for the commit message)

Runtime:
- `next` 16.3.5 — App Router framework, route handlers (the BFF).
- `react` / `react-dom` 19.3.0 — UI runtime.
- `@tanstack/react-query` 5.103.0 — server-state cache, polling, mutations.
- `iron-session` 9.0.1 — seals the session (and the JWT inside it) into an encrypted, authenticated cookie value.
- `server-only` 0.0.1 — makes the build fail if a server module (token, Gateway URL) is imported from client code.
- `radix-ui` 1.6.7 — `Slot` and `Label` primitives used by the shadcn components.
- `class-variance-authority` 0.7.1, `clsx` 2.1.1, `tailwind-merge` 3.7.0 — shadcn class composition.
- `lucide-react` 1.46.0 — the select chevron icon.

Dev:
- `typescript` 5.9.3 — typecheck. Same pin as #7's catalog; TS 7 is not used.
- `@types/node` 24.13.5, `@types/react` 19.3.0, `@types/react-dom` 19.3.0 — typings.
- `tailwindcss` 4.3.3, `@tailwindcss/postcss` 4.3.3 — Tailwind v4.
- `tw-animate-css` 1.4.0 — the shadcn animation utilities.
- `eslint` 9.39.5 + `eslint-config-next` 16.3.5 — lint, with the Next.js, React-hooks and typescript-eslint rules.
- `vitest` 5.0.1, `vite` 8.3.0 (a peer of Vitest 5), `@vitest/coverage-v8` 5.0.1 — test runner and coverage. No Jest.
- `jsdom` 30.0.1, `@testing-library/react` 16.3.3, `@testing-library/dom` 10.4.2, `@testing-library/jest-dom` 7.0.1 (matchers only, used with Vitest), `@testing-library/user-event` 14.6.7 — component tests.
- `eventsource` 5.1.1 — a standards-following EventSource for Node, so the stream client can be tested over real sockets.
- `openapi-typescript` 7.13.0 — generates the wire types from `openapi.yaml`.

pnpm policy (`apps/web/pnpm-workspace.yaml`):
- `allowBuilds: unrs-resolver: false`. Its postinstall only re-checks prebuilt bindings, so it is denied, as #7 denied non-project postinstalls.
- `minimumReleaseAgeExclude` for three versions younger than pnpm 11's default age gate.

The shadcn CLI (`pnpm dlx shadcn@4.21.0`, not a dependency) mis-resolved the `@/lib/utils` alias. It added a bogus npm package `cn@^0.3.0` and imported `cn` from it. I removed the package (`pnpm remove cn`; the lockfile has 0 `cn@` entries) and rewrote the 8 imports to `@/lib/utils`.

---

## 2. Contract bullets → evidence

**Id 29 — 1. Types generated, the only source of wire shapes, drift check fails when stale.**
- `scripts/openapi-types.mjs generate|check` creates `src/generated/openapi.ts`. `check` regenerates in memory and compares byte for byte.
- `src/lib/api-types.ts` is the only importer. ESLint `no-restricted-imports` bans `@/generated/*` everywhere else.
- The check was armed in G2 (quality.sh fails with a `first difference at line 950` message). The import ban was armed in A40 (lint: `'@/generated/openapi' import is restricted …`).
- The single hand-written type is `SessionInfo`. It is this app's own BFF shape, and the one #7 also declared by hand.

**Id 29 — 2. Live SSE with reconnect: nothing lost or duplicated, and the two frame types de-duplicated separately.**
- `src/lib/order-stream-client.ts` keeps two sets, plus a second, reducer-level guard in `applyTimelineAppended`.
- Tests (`src/lib/order-stream-client.test.ts`, real `eventsource` over real HTTP):
  - *id 30 — a forced mid-stream disconnect is resumed with Last-Event-ID …* asserts `[undefined, 'cursor-1t']`, each fact's frames applied exactly once, and exactly one resync.
  - *R51 — the two frames of ONE fact share an eventId and BOTH are applied*, in both wire orders.
- The same shared-eventId case is also tested at page level and hook level.
- Through the production server, `tests-integration` › *a real EventSource, dropped by the Gateway, reconnects THROUGH the proxy and resumes with Last-Event-ID*.
- Live, in Chrome, three facts arrived after a payment, each with both frame types sharing one `eventId`, in both orders. All three landed.
- Arms: A1–A4, A35, A42.

**Id 29 — 3. Register payment: `'19.99'` → 1999 with no floating point, and accepted rendered distinctly from duplicate.**
- `src/lib/money.ts` parses digit-wise and reads the exponent from ISO 4217.
- Tests: `billing-view.test.tsx` › *"19.99" typed by the user reaches the wire as exactly 1999 …*, › *an accepted payment and a duplicate replay are rendered DIFFERENTLY …*, and the route-handler chain case (201, then 200, then the real 422).
- Live: 201 `accepted`, then 200 `duplicate` with `Idempotent-Replay: true`, through the BFF and in Chrome.
- The ESLint money rule bans `parseFloat` and `toFixed` in `src` (G3a).
- Arms: A13–A16.

**Id 29 — 4. A 202 renders a named waiting state that retries — never an error, never a 404, never an endless spinner.**
- `useOrderDetail`: a 202 is a success with a `pending` kind. The retry delay comes from `Retry-After`, then the body's `retryAfterMs`, then a 2 s default, with a 250 ms floor.
- `WaitingForProjection` names the state, shows the Gateway's message and the retry schedule, and has a "Check now" button.
- The BFF forwards `Retry-After`. #7 did not forward it (see the ledger).
- Tests: `order-detail-view.test.tsx` R55 cases, and `route-handlers.test.ts` › *R55 — a 202 … keeps its status AND its Retry-After header*.
- Live: `202` + `retry-after: 2` while the Projector was stopped; 200 about 7 s after it restarted; in Chrome, the page opened on the waiting state.
- Arms: A17–A20.

**Id 29 — 5. Every error shown is that error's own text (`detail`, falling back to `title`), proven against a REAL Gateway response.**
- Route handlers relay the problem document verbatim: status, `application/problem+json`, body bytes (`relay`). `describeError` reads `detail`, then `title`, then the fallback.
- Twelve real Gateway answers were captured by `scripts/capture-gateway-responses.mjs` (8 with the stack up; 4 with Fulfillment and Billing stopped).
- `src/lib/error-text-chain.test.ts` drives each one through browser client → real route handler → fake Gateway serving the captured bytes → `describeError`, and asserts the result equals that fixture's `detail`. Its population is the fixture directory, not the table.
- Component tests on every page use the same captured bytes. Three of them go through the real route handler: login 401, place-order 409, payment 422.
- Live, in Chrome: *"username or password is incorrect"*, *"Stock check reports 1 short line(s): PRD-0001 (requested 999999, available 500)"* with the shortages list, and *"Could not load this order: id: "not-a-uuid" is not a valid id"*.
- Arm A10 re-introduced a Nitro-like envelope (#7's defect shape): 23 failures, each reading `expected 'GENERIC FALLBACK' to be '<the real detail>'`.
- The optional `detail` being absent is covered by A11.

**Id 29 — 6. The JWT never reaches the browser.**
- The session cookie is `HttpOnly; Secure; SameSite=lax`, and its value is iron-sealed.
- Every Gateway call is made in `src/server/gateway.ts` or `stream-proxy.ts` (both `server-only`, armed by A41: build fails), including the stream, which forwards `Last-Event-ID` and aborts upstream on disconnect.
- Tests: `route-handlers.test.ts` › *a JSON login answers identity only …* (the body carries no token and no JWT-shaped string, and neither does the cookie), plus session, tamper and expiry cases. `stream-route.test.ts` covers bearer, `Last-Event-ID`, and both teardown paths.
- `tests-integration` › *closes its Gateway connection when the browser goes away*, against `next start`.
- Live: JWT-shaped strings found in `/api/auth` bodies, in the cookie value, and across every response the browser session received: 0 in each; `document.cookie` does not show `otc_session`.
- Arms: A5–A8, A21–A23, G7b, P2, P3, P6.

**Id 29 — 7. No control disabled or hidden waiting for hydration.**
- Login is `method=post action=/api/auth/login`. A pre-hydration submit signs in, and the password stays in the POST body.
- Every submit button is disabled only while its own request is pending. Place order validates on submit instead of disabling.
- Tests: *the submit button is operable on the very first render*, *Place order is operable on the first render*, and *is a real form posting to the login route*.
- Live, in Chrome with JavaScript **disabled**:
  - Correct password → `/orders`.
  - Wrong password → `/login?error=username+or+password+is+incorrect`, with that text shown.
  - With JavaScript on, the button is not disabled at first paint.
- Arms: A27–A29.

**Id 29 — 8. `apps/web` is wired into `./quality.sh`, and each gate is seen to FAIL.** Section 5, with ten plants (G1–G7b); see "Quality-gate arming". Lint is additionally guarded by `scripts/lint-coverage.mjs`, which failed on #7's exact defect (G3c: 14 of 90 files silently ignored).

**Id 29 — 9. Pages.**
- Login, orders list, place order (retailer, company, product and currency selection, quantity, unit-price override), order detail with live timeline and causal links, stock (list + replenish), billing (invoices with Register payment, credits).
- A 409 `StockUnavailableProblem` renders its shortages: unit test (real captured body through the route handler) and live.
- Screenshots were looked at in §7.

**Id 29 — 10. Loading tests hold the request on an UNRESOLVED promise.** `deferred()` in `src/test/render.tsx` is used by the orders, stock, invoices + credits, detail, login-pending, placing and registering tests. A30, A30b, A30c and A31 replaced each loading branch with `null`, and each test failed with `Unable to find an element by: [data-testid="…-loading"]`.

**Id 30 — 1. Timeline, place-order form, stock and billing tables covered.** `order-detail-view.test.tsx` (21 cases), `place-order-form.test.tsx` (16), `stock-view.test.tsx` (9), `billing-view.test.tsx` (15). These are case counts from each file's run, where `it.each` rows are counted.

**Id 30 — 2. SSE hook tested with a fake EventSource, and the stream client against a REAL local SSE server with a forced disconnect asserting the `Last-Event-ID` resume.**
- `src/hooks/use-order-stream.test.tsx` uses `FakeEventSource` (`src/test/fake-event-source.ts`).
- `src/lib/order-stream-client.test.ts` › *id 30 — a forced mid-stream disconnect …* uses `res.destroy()` with the real `eventsource` package.

### Discrepancies between the brief and the bullets / my instructions
- **Status transition.** Brief §10 forbids touching `feature_list.json`; my standing instructions say to set `in_review`. I followed the brief. **The leader should set id 29 (and id 30, if it closes with 29) to `in_review`.**
- **Test matrix.** Brief §10 forbids editing `specs/shared/`; my standing instructions say to update `test-matrix.md`. I followed the brief. Proposed R55 web-half text, for the leader to apply:

  > **web half DONE** (feature `web_app`): `apps/web/src/features/orders/order-detail-view.test.tsx` › *a 202 renders the NAMED waiting state (not an error, not a 404, not a bare spinner), retries on the Gateway's schedule, then shows the order*, › *R51 — the two frames of one fact share an eventId and BOTH land*; `apps/web/src/lib/order-stream-client.test.ts` › *id 30 — a forced mid-stream disconnect is resumed with Last-Event-ID …*; `apps/web/tests-integration/stream-proxy.test.ts` (production build).

  With that, R55's status can move from TODO to DONE. R51 and R47/R48 gain web evidence too (listed above), but their rows are already DONE.
- **"CurrencyViewPayload carries DecimalPoints — display and input must honour it" (brief §4).** The fact is true of the NATS contract, but **no REST operation in `openapi.yaml` carries `decimalPoints`**. `grep -n -i decimalPoints specs/shared/openapi.yaml` gives one hit, line 56: the prose *"Formatting for humans happens in the client, from `currency.decimalPoints`"*. No schema has the field, and there is no `/catalog/currencies` path. So the web app cannot read it from the Gateway. It honours the exponent from ISO 4217 (`Intl`), with JPY (0) and BHD/KWD (3) tested, and never assumes 2. #7 hard-coded 2 (`apps/web/app/lib/money.ts:11` `format(minorUnits / 100)`, `:62` `(minorUnits / 100).toFixed(2)`). **The root cause is in `specs/shared/`: the prose promises a field no response carries. Per `CLAUDE.md` this should become a backlog entry or an SA-n proposal, not a sentence.** I did not file it (the brief forbids touching `feature_list.json`).

---

## 3. #7's tests, enumerated and classified

Command (run in #7's `apps/web`; `e2e/` is Playwright, feature 32, and excluded by path):

```
find . \( -path ./node_modules -o -path ./.nuxt -o -path ./.output -o -path ./e2e \) -prune -o -name '*.spec.ts' -print | sort
```

Output: 15 files:

```
./app/composables/useOrderDetail.spec.ts
./app/lib/order-stream-client.spec.ts
./app/lib/problem.spec.ts
./app/pages/billing/index.spec.ts
./app/pages/login.spec.ts
./app/pages/orders/[id].spec.ts
./app/pages/orders/index.spec.ts
./app/pages/orders/place.accessibility.spec.ts
./app/pages/orders/place.currency.spec.ts
./app/pages/orders/place.error.spec.ts
./app/pages/orders/place.hydration.spec.ts
./app/pages/orders/place.spec.ts
./app/pages/orders/place.unit-price.spec.ts
./app/pages/stock/index.spec.ts
./server/utils/stream-proxy.spec.ts
```

- `grep -nE "^\s*it\("` over the same set gives 74 cases.
- Every line containing `expect(` gives 213 assertions: 206 PORTED, 4 NOT PORTED (deliberate), 3 NOT APPLICABLE.
- The unit classified is the **assertion** (one row per `expect(` line). Each row names the #8 test that carries the property, or the reason it does not. The table was generated by `scratchpad/enum7.py` from that enumeration, so a missing row would be a missing line, not a missing sentence. It is appended at the end of this record (§A).

The four deliberate non-ports are all one decision, driven by bullet 7:
- #7's two "stays disabled before hydration" assertions: login and place-order hydration.
- #7's form-validity `toBeDisabled`.
- #7's close/reopen-yields-same-reference assertion: #8's form stays open, and determinism is asserted directly.

The three not-applicable assertions cover Nitro's double-wrapped error shape, which #8 does not have. The property they protected is guarded more strongly (§2 bullet 5, A10).

---

## 4. Ported-idiom ledger

Each row reads: "#7 relied on X (#7 file:line); in #8 the property is supplied by Y; guard: Z". The #7 citations were read out of `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs/apps/web` in this session.

| # | Idiom | #7 relied on (file:line) | #8 supplied by | Guard (seen to fail) |
|---|---|---|---|---|
| L1 | Contract types, generated + drift-checked | `packages/contracts/scripts/generate.mts:16-25` (`generateAll`, openapi-typescript into `src/generated`) and `check.mts:26-50` (`checkGenerated`: regenerate to a temp dir, compare); `apps/web/shared/types/gateway.ts:13` the single importer | `apps/web/scripts/openapi-types.mjs generate\|check` → `src/generated/openapi.ts`; `src/lib/api-types.ts` the single importer, enforced by ESLint `no-restricted-imports` (#7 had no enforcement: its single-importer property was a grep in the closure review, #7 `progress/history.md:1111`) — a **strengthening** | G2 (quality.sh `types:check FAILED … first difference at line 950`), A40 (lint error) |
| L2 | Token only server-side, in a sealed httpOnly cookie | h3 `useSession` sealing, `server/utils/session.ts:29-37` (`httpOnly: true`, `sameSite: 'lax'`), password from `runtimeConfig.sessionPassword` with a **hard-coded default** (`nuxt.config.ts:31`: `'otc_web_dev_session_password_change_me_32ch'`) | `iron-session` `sealData`/`unsealData` in `src/server/session.ts`; password `WEB_SESSION_PASSWORD` (≥32 chars, a short one throws) or a random per-process key shared on `globalThis`; the only committed value is the obviously-named development placeholder in `.env.example:242` (`otc_dev_web_session_password_change_me_32`), never a default in code | A21, A22 (route-handlers tests); config test; **P7** (per-bundle key → integration fails, see §6) | **[STALE since 2026-09-16: the leader committed a development default, `WEB_SESSION_PASSWORD=otc_dev_web_session_password_change_me_32`, in `.env.example` — the same discipline as the repository's committed `JWT_SECRET` dev value. It is a documented, obviously-named development placeholder, not a deployment secret, and the app still refuses one shorter than 32 characters.]**
| L3 | Session scoping to one app instance | Nitro runs one server bundle, so a module-level value is one value (the #7 default password was a constant anyway) | Next.js compiles route handlers and server components into **separate bundles** — a module-level variable is one-per-bundle; the generated key therefore lives on `globalThis` (`src/server/config.ts`) | integration › *a session sealed by the login route is accepted by the pages …* (P7: `expected '/orders → 307 /login' to be '/orders → 200'`) |
| L4 | Every Gateway call from the server | `server/utils/gateway.ts:33-43` (`gatewayFetch` attaches the bearer; only server routes import it — convention) | `src/server/gateway.ts` + `stream-proxy.ts` + `config.ts`, all `import 'server-only'` → **the build fails** if client code imports them (a strengthening: #7's was a convention) | A41 (build: *You're importing a module that depends on "server-only"*), A8, route-handlers *Gateway never called* cases |
| L5 | The problem document reaching the page | `server/utils/gateway.ts:12-20` re-throws with `createError({ …, data: error.data })`, Nitro wraps it again, and `app/lib/problem.ts:41-47` unwraps `data.data` | `relay()` in `src/server/gateway.ts` copies status, content type and body bytes — no envelope exists to unwrap; `readProblem` parses the body | A10 (envelope → 23 failures), chain test over 12 real fixtures |
| L6 | 202 and payment 201/200 status forwarding | `gatewayFetchWithStatus` `server/utils/gateway.ts:65-77` + `setResponseStatus` (`server/api/orders/[id].get.ts:14-15`, `server/api/invoices/[id]/payments.post.ts:24-29`); retry from the body, `app/composables/useOrderDetail.ts:114` (`data.pending.retryAfterMs ?? 2000`) — **`Retry-After` is not forwarded** by #7's proxy | `relay()` forwards status and `Retry-After`/`Idempotent-Replay`; `pendingRetryDelayMs` prefers the header (openapi's own header), then the body — a **strengthening** | A19, A17, A18, route-handlers R55 and R47/R48 cases |
| L7 | Stream proxy: `Last-Event-ID` + bearer | `server/utils/stream-proxy.ts:26-40` | `src/server/stream-proxy.ts` `openUpstreamOrderStream` | A5, A5s (substitution), A8; integration *forwards Last-Event-ID through the real server* |
| L8 | Stream proxy: unbuffered passthrough | `sendWebResponse(event, upstream)` (`server/api/orders/stream.get.ts:43`); verified in #7 by reading only (review Pass 3, probe 4) — no executing guard | `relayStream` (a pull-based ReadableStream) + **an SSE-comment preamble**, because Next writes a route handler's headers with its first body chunk; no-transform/X-Accel-Buffering for intermediaries | integration lockstep test (P5: a buffering relay → 4 failures); P4 (no preamble → *no response status/headers within 3000 ms …*, message added after P4) |
| L9 | Stream proxy: abort upstream on disconnect | `event.node.req.on('close', () => controller.abort())` (`server/api/orders/stream.get.ts:29`); no executing guard in #7 | two mechanisms: `request.signal` abort **and** `cancel()` of the relayed body (abort + reader.cancel); probes P2/P3 showed **each alone suffices** in `next start` | A6, A7 (unit); integration *closes its Gateway connection when the browser goes away* (P6: both removed → `timed out after 5000 ms waiting for the proxy to close its Gateway connection …`). G7b (only `request.signal` removed) **survived** — expected, since the other mechanism covers it |
| L10 | Per-type de-duplication and seeding | `app/lib/order-stream-client.ts:54-55` (two Sets), `:65` (`seedSeenEventIds` seeds both) | same design, `src/lib/order-stream-client.ts` | A1, A2, A3 |
| L11 | Stream reconnect identity | Vue composable: `connect()` called once from a `watch` (`app/pages/orders/[id].vue:62-67`); the factory is a prop | React effect keyed on `orderId` only; factory and handlers read through refs — because **the production minifier inlined the default factory into the parameter list** (`factory:i=e=>new EventSource(e)`, read from `.next/static/chunks/330vf3waqjbex.js`), making it a new function every render | A42 (`EventSources opened across 6 renders: expected [ …(6) ] to deeply equal [ Array(1) ]`) |
| L12 | Backstop re-read D2/D7 | closure flags set inside `refetchInterval` (`app/composables/useOrderDetail.ts:112-128`: `observedNonTerminal = true`, `extraPollDone = true`), relying on each evaluation being meaningful once. **Not probed**: how many times vue-query evaluates that callback | idempotent notes keyed on *completed reads* (`src/hooks/use-order-detail.ts`) — the literal port **failed** its own D7 test under `@tanstack/react-query` 5.103.0 (observed: *Unable to find … Credit released*), so the callback is evaluated more than once per update there | A32, A33 |
| L13 | Live append ordering | `.sort((a,b) => a.occurredAt.localeCompare(b.occurredAt))` (`useOrderDetail.ts`, #7 review Pass 8 non-blocking observation: no causal tie-break) | `orderTimeline()` implements openapi `TimelineEntry`'s A1 rule (occurredAt, then cause-before-effect, then eventId) — a **strengthening** | `order-detail.test.ts` orderTimeline cases (an effect whose eventId sorts first still follows its cause) |
| L14 | Money input and display | `app/lib/money.ts` `decimalStringToMinorUnits` (string parse, regex `\d{1,2}`: **2 decimals hard-coded**); display by **float division** `minorUnits / 100` (`:11`) and `(minorUnits / 100).toFixed(2)` (`:62`) | digit-wise parse *and* format, the exponent from ISO 4217; `Intl.NumberFormat.format` given the decimal **string**; ESLint bans `parseFloat`/`toFixed` in `src` — a **strengthening** | A13, A14, G3a |
| L15 | Hydration safety of login | `app/pages/login.vue:70` `:disabled="login.isPending.value \|\| !mounted"` | a real form post handled by the login route (JSON **or** urlencoded, 303) — never disabled for hydration | A27, A28; live no-JS sign-in |
| L16 | Session expiry / Gateway 401 | `requireGatewayToken` `server/utils/session.ts:43-46` (checks `expiresAt`); **a Gateway 401 does not clear the session** (`forwardProblem` leaves it) | `readSession` checks expiry and tamper; `relay()` clears the cookie on an upstream 401; the browser QueryCache sends a 401 to `/login` | A23; session tamper/expiry test |
| L17 | `Idempotency-Key` on place order | `server/api/orders/index.post.ts:11-16` | `forwardRequestHeaders: ['idempotency-key']` | A24 |
| L18 | Catalogue routes | three literal handlers `server/api/catalog/{products,retailers,companies}.get.ts` | one dynamic route with a **literal allow-list** (an unknown kind is a local 404, never a Gateway path) | A26 |
| L19 | Gateway base URL | `nuxt.config.ts:30` `runtimeConfig.gatewayBaseUrl = GATEWAY_BASE_URL ?? http://localhost:${GATEWAY_PORT ?? 3001}`; no guard | `gatewayBaseUrl()` read at request time (`src/server/config.ts`), same precedence | config test *reaches the Gateway on GATEWAY_PORT — and not on a sibling *_PORT*; A39 (substitution → `expected 'http://localhost:4666' to be 'http://localhost:4555'`) |
| L20 | Loading `.env` | `package.json` scripts `dotenv -e ../../.env -- nuxt …` (the `dotenv-cli` package) | `node --env-file-if-exists=../../.env` (Node 24, no package); `scripts/dev-stack.sh` also exports `.env.example` then `.env` | **none owed as a unit guard**: exercised by every live run in §7; a missing file is not an error by design |

What the ledger found that no rule forced: L3, L8's preamble, L11 and L12. #7's framework supplied each of these properties implicitly, and the literal translation lost them. Three of the four were found only by running the production build in a real browser.

---

## 5. Arming

### Behavioural arms (44, on the final code)

Harness: `scratchpad/arm.py`. For each arm it copies the file to a backup, plants one mutation (the old text must occur exactly once), runs the named test files with Vitest's JSON reporter, restores from the backup, and proves the restore with `filecmp.cmp(shallow=False)` plus `touch`. Vitest compiles from source on each run, so there is no stale-binary hazard, and the touch is kept anyway. All 44 arms killed and all 44 restores were identical. The full table is appended (§B).

A40 and A41 were run by hand (copy, plant, `pnpm lint` / `pnpm build`, copy back, `cmp`):
- **A40**: an `import type { components } from '@/generated/openapi'` in `orders-list.tsx` → `pnpm lint` exit 1: `17:1  error  '@/generated/openapi' import is restricted from being used by a pattern. Import wire shapes from @/lib/api-types — it is the only importer of the generated OpenAPI types  no-restricted-imports`. Restore confirmed with `cmp`.
- **A41**: `stock-view.tsx` (client) imports `@/server/config` → `pnpm build` exit 1: `Error: You're importing a module that depends on "server-only". …`. Restore confirmed with `cmp`.

### Production-build probes (P1–P7, `scratchpad/probe.py`)

Each probe plants several edits, runs `pnpm build` and `pnpm test:integration`, then restores.

| Probe | Planted (production only) | Integration result | What it established |
|---|---|---|---|
| P1 | `Cache-Control` without `no-transform` (header assertion removed) | 7/7 green (5 tests at the time) | Next 16.3.5 `next start` did not buffer the stream without `no-transform` |
| P1b | same, and assert `content-encoding` | fails with `PROBE content-encoding: expected undefined to be 'PROBE'` | **no compression was applied at all** to `text/event-stream`, even with `Accept-Encoding: gzip, deflate, br` |
| P2 | neither `request.signal` nor `upstreamAbort.abort` (but `reader.cancel` kept) | green | cancelling the relayed reader alone tears down the upstream |
| P3 | only `request.signal` kept | green | `request.signal` does abort on client disconnect in `next start` |
| P4 | no preamble | 2 failed (then: `Test timed out in 60000ms`) | **the headers are held until the first body chunk**; the named message was added afterwards |
| P5 | a relay that batches 3 chunks | 4 failed | the lockstep test detects buffering |
| P6 | no teardown at all | 1 failed: `timed out after 5000 ms waiting for the proxy to close its Gateway connection after the client left` | the disconnect guard bites |
| P7 | generated session key per bundle | 1 failed: `expected '/orders → 307 /login' to be '/orders → 200'` | guard for L3 |

A defect in my own harness must be recorded. `probe.py` first restored multi-edit backups **in application order**, so P7 (two edits to the same file) restored to the state *after* its first edit, and `config.ts` was briefly left with `const holder = moduleLocalHolder;` while "restored True" was printed. Each backup had been compared only to itself. I caught it by reading the line (`grep -n "const holder"`), restored from the pre-probe backup, confirmed with `cmp`, and fixed the harness to restore in reverse order. Earlier probes (P1–P6) planted at most one edit per file, and a residue sweep of `src tests-integration package.json eslint.config.mjs` for every planted token found nothing. Only P7's result had been taken with both edits in place, so it stands.

---

## 6. Quality-gate arming (`QUALITY_ONLY=web ./quality.sh`, `scratchpad/gate_arm.py`)

| Gate | Plant | quality.sh | Verbatim failure |
|---|---|---|---|
| G1 install | `"left-pad": "1.3.0"` added to `package.json` only | exit 1, `[FAIL]  apps/web: install (frozen lockfile) failed (exit 1)` | `specifiers in the lockfile don't match specifiers in package.json:` / `* 1 dependencies were added: left-pad@1.3.0` (first run had `--reporter=silent`, which hid it; step changed to `append-only` and G1 re-armed) |
| G2 types:check | `detailText?: string;` inserted into the committed `src/generated/openapi.ts` | exit 1 | `types:check FAILED — src/generated/openapi.ts is stale against specs/shared/openapi.yaml.` / `first difference at line 950:` / `committed: "            detailText?: string;"` / `fresh:     "            instance?: string;"` |
| G3a lint | `parseFloat(digits)` in `money.ts` | exit 1 | `58:17  error  Unexpected use of 'parseFloat'. Money is integer minor units — parse decimal strings with parseDecimalToMinorUnits (src/lib/money.ts)  no-restricted-globals` |
| G3b lint (a **.tsx component**) | conditional hook in `orders-list.tsx` | exit 1 | `25:26  error  React Hook "useRetailers" is called conditionally. React Hooks must be called in the exact same order in every component render  react-hooks/rules-of-hooks` |
| G3c lint-coverage (#7's defect) | `'src/features/**'` added to `globalIgnores` — ESLint itself then exits 0 | exit 1 | `lint-coverage FAILED — 14 of 90 source files are not really linted:` / `src/features/auth/login-form.test.tsx: ignored by ESLint` / … |
| G4 typecheck | `export const typeProbe: number = 'not a number';` in `utils.ts` (ESLint does not see it) | exit 2 | `src/lib/utils.ts(4,14): error TS2322: Type 'string' is not assignable to type 'number'.` |
| G5 test | float money (`Math.floor(Number(trimmed) * 10 ** exponent)`) | exit 1 | `AssertionError: expected 28 to be 29` (and `expected 1004 to be 1005`, `expected 202 to be 203`, the billing 19.99 case) |
| G6 build | `useState` in the server component `src/app/(app)/stock/page.tsx` (passes lint, typecheck, tests) | exit 1 | `Error: You're importing a module that depends on \`useState\` into a React Server Component module. This API is only available in Client Components. …` |
| G7 integration | production-only `Cache-Control: no-cache` | exit 1 | `AssertionError: expected 'no-cache' to be 'no-cache, no-transform'` — the header assertion, **not** a buffering failure (P1/P1b show Next did not buffer) |
| G7b integration | production-only removal of the `request.signal` listener | **exit 0 — survived** | the `cancel()` path still tears down (P2/P3); P6 is the arm that kills this gate on this claim |

Every restore was byte-identical (`filecmp`), and the green run after the last change is recorded at the top.

---

## 7. Live verification (real stack, through the route handlers)

Stack: `scripts/dev-stack.sh start`:
- infra via `docker compose up -d --no-build`, falling back to a normal `up -d`;
- one `dotnet build` of the solution, then `dotnet run --no-build` for Seed and the six services, with PID and log files under `logs/dev-stack/`;
- `ASPNETCORE_URLS` binds the Gateway to `GATEWAY_PORT`, because **the Gateway does not read `GATEWAY_PORT`** (`grep -rn GATEWAY_PORT src --include=*.cs` has no hit); **[SUPERSEDED 2026-09-16 by backlog id 96: the Gateway now reads `GATEWAY_PORT` and listens on it, and `dev-stack.sh` no longer sets `ASPNETCORE_URLS` — see `progress/impl_gateway_ignores_gateway_port.md`. True when written.]**
- then `next build` and `next start`.

The web app ran on **port 3010**. During the session another application on this machine (a Nuxt app from `/home/juanpabloperez/Work/Projects/EdiEz/ediez-app-2024`, pid 3628535) took port 3000, and my `next start` died with `EADDRINUSE` while the script's readiness probe was answered by that other app. `dev-stack.sh` now checks the port first and waits on its own PID. `scripts/dev-stack.sh stop` ended with `[OK]   nothing left running`, and `pgrep` confirmed it.

### Through the route handlers (curl)

```
$ POST /api/auth/login (JSON)
HTTP/1.1 200 OK
set-cookie: otc_session=<sealed, redacted>; Path=/; Expires=Thu, 17 Sep 2026 03:34:53 GMT; Max-Age=43200; Secure; HttpOnly; SameSite=lax
{"authenticated":true,"username":"operator","displayName":"Order-To-Cash Operator","roles":["operator"]}
$ body scan for a JWT in any /api/auth response
0
$ the sealed cookie value itself contains no JWT:
0
$ POST /api/auth/login (plain HTML form post, as before hydration) — wrong password
HTTP/1.1 303 See Other
location: http://localhost:3010/login?error=username+or+password+is+incorrect
$ GET /api/orders/not-a-uuid (a real validation error)
HTTP/1.1 400 Bad Request
content-type: application/problem+json
{"type":"about:blank","title":"The request was malformed or failed schema validation","status":400,"detail":"id: \"not-a-uuid\" is not a valid id","code":"VALIDATION_FAILED",…,"errors":[{"field":"id","message":"\"not-a-uuid\" is not a valid id"}]}
$ POST /api/orders quantity 999999 (a real 409 with shortages)
HTTP/1.1 409 Conflict
content-type: application/problem+json
{"type":"about:blank","title":"Insufficient stock at acceptance","status":409,"detail":"Stock check reports 1 short line(s): PRD-0001 (requested 999999, available 500)","code":"STOCK_UNAVAILABLE",…,"shortages":[{"productCode":"PRD-0001","requested":999999,"available":500,"sufficient":false}]}
$ SSE response headers
HTTP/1.1 200 OK
cache-control: no-cache, no-transform
content-type: text/event-stream; charset=utf-8
$ SSE frames received (first lines):
: connected

event: stream.ready
data: {"cursor":"1789572893947-7","resumed":false}

id: 1789572895271-8
event: order.updated
data: {"eventId":"1e02ffed-f550-456e-ad51-8b9f9271e6b2","orderId":"fb47f42c-…","orderReference":"ORD-000014","status":"placed",…}

id: 1789572895271-9
event: timeline.appended
data: {"eventId":"1e02ffed-f550-456e-ad51-8b9f9271e6b2","causationId":"f30adb6f-…","eventType":"order.placed.v1",…}
$ reconnect with Last-Event-ID: 1789572895271-9
: connected

event: stream.ready
data: {"cursor":"1789572895271-9","resumed":true}

id: 1789572896012-10
event: order.updated
…
  frames replayed after the cursor: 10
$ scripts/dev-stack.sh stop-service Projector
$ POST /api/orders   → {"orderId":"5744809a-…","orderReference":"ORD-000015",…,"projectionPending":true}
$ GET /api/orders/{id} while the Projector is down
HTTP/1.1 202 Accepted
retry-after: 2
{"orderId":"5744809a-…","status":"projection_pending","message":"The order was accepted and is not projected yet. Subscribe to /orders/stream or retry.","retryAfterMs":2000}
$ scripts/dev-stack.sh start-service Projector
  after ~7s: HTTP 200 {"orderId":"5744809a-…","orderReference":"ORD-000015",…}
$ POST /api/invoices/a29638fd-…/payments  {"paymentReference":"PAY-LIVE-173551","amount":{"amount":4590,"currency":"EUR"},…}
HTTP/1.1 201 Created
{"outcome":"accepted","paymentReference":"PAY-LIVE-173551","invoiceReference":"INV-000012","orderReference":"ORD-000015","invoiceStatus":"paid",…}
$ the same request again
HTTP/1.1 200 OK
idempotent-replay: true
{"outcome":"duplicate",…}
$ a different amount on a now-paid invoice (a real rejection)
HTTP/1.1 409 Conflict
{"type":"about:blank","title":"Invoice already paid","status":409,"detail":"Invoice 'INV-000012' has already been paid.","code":"INVOICE_ALREADY_PAID",…}
```

(The two frames of ORD-000014's first fact carry the same `eventId` `1e02ffed-…`. That is trap 2, live in #8.)

### Real browser (headless Chrome via `puppeteer-core` 25.11.0 in the scratchpad only; final run, after the fixes)

```
1. no-JS sign-in → url http://localhost:3010/orders | password in URL: false
   no-JS wrong password → url http://localhost:3010/login?error=username+or+password+is+incorrect | shown: username or password is incorrect
2. submit disabled at first paint: false | url after wrong password: http://localhost:3010/login | shown: username or password is incorrect
   signed in → url http://localhost:3010/orders | document.cookie exposes the session: false
3. orders rows: 20 | pager: Page 1 of 3 · 49 orders
4. place order @390px — longest retailer "Leroy Merlin España (LeroyMerlinEs)": {"headerOverlap":false,"lineOverlap":false,"pageOverflowsX":false,…,"unitPlaceholderFits":true,…}
4. place order @700px — … {"headerOverlap":false,"lineOverlap":false,"pageOverflowsX":false,…,"unitPlaceholderFits":true,…}
4. place order @1000px — … same, all false/true
4. place order @1280px — … same, all false/true
   409 shown: Stock check reports 1 short line(s): PRD-0001 (requested 999999, available 500) | shortages: PRD-0001: requested 999999, only 500 available
   demo fill unit price: 249.99 | running total: €249.99
5. placed: Order ORD-000023 accepted. Follow it on its live timeline.
   paid while watching: 201 accepted | frames received live after payment: [["timeline.appended","1aafe3be-…"],["order.updated","1aafe3be-…"],["order.updated","e192fb6f-…"],["timeline.appended","e192fb6f-…"],["timeline.appended","4b8b1310-…"],["order.updated","4b8b1310-…"]] | eventIds carried by BOTH frame types: 3
   now: {"status":"completed","stream":"Live","entries":[…9 entries…, ["payment.received.v1",null],["credit.released.v1","payment.received.v1"],["order.completed.v1","credit.released.v1"]]} | new stream requests while watching: 0
6. /orders/not-a-uuid shows: Could not load this order: id: "not-a-uuid" is not a valid id
7. stock first row on hand before: 509 | outcome: Added 3 units to PRD-0001 — on hand is now 512.
8. payment prefill: ["PAY-2026-09-16-000016","18.49"]
   first submit: Payment PAY-2026-09-16-000016 recorded (HTTP 201) — invoice INV-000016 is now paid.
   second submit: Payment PAY-2026-09-16-000016 was already recorded (HTTP 200) — nothing new was created; this was an idempotent replay.
   @390px /orders|/orders/place|/stock|/billing: elements overflowing the viewport outside scrollable tables: 0 each
9. responses whose body contained a JWT: []
```

I looked at the screenshots of the place-order page at 700 px, the live detail page, and billing after the duplicate. There is no overlap or clipping; the causal links render; the waiting-state and outcome copy reads correctly.

### Found by looking — defects no unit test could see (all fixed, all guarded)

1. **Every sign-in bounced back to `/login`** under `next start`. The generated session key was a module variable, which exists once per bundle, so the login route sealed with one key and the page layout unsealed with another. The web log printed the "not set" warning twice. Fix: the key lives on `globalThis`. Guard: an integration test running without `WEB_SESSION_PASSWORD` (P7).
2. **A reconnect storm.** The detail page opened hundreds of EventSources per second (`count: 441` in 4.5 s) in the production build only. The minifier inlined the default factory into the destructuring default, so every render produced a new function, and that function was an effect dependency. Diagnosis: a debug build with `console.warn` on the effect, and reading `.next/static/chunks/330vf3waqjbex.js`. Fix: the stream depends only on `orderId`; the factory is read through a ref. Guard: A42. After the fix, live: `count: 1`, `status: Live`, 0 new stream requests while watching.
3. **The SSE response held its headers until the first upstream frame** (found by the integration test's first run). Fix: the SSE-comment preamble. Guard: P4.

Four further fixes came from looking:
- The payment outcome vanished when the paid invoice dropped out of an "issued" list. The form now lives outside the table (A43).
- The header overflowed at 390 px; it now wraps.
- The unit-price placeholder was clipped at 700 px; the line grid now uses 2 columns until `lg`.
- A `puppeteer` selector clicked **Log out** (the first `button[type=submit]` on the page). That was my probe's bug, not the app's.

---

## 8. Defeat list (CLAUDE.md's ten attacks) against my guards

- **1 Delete the behaviour** — ran (A2–A9, A11, A12, A17, A19, A21–A24, A30–A33, A35, A41, G-series).
- **2 Corrupt a supplied field** — ran:
  - A15: payment amount and currency replaced;
  - A36: delta replaced by target;
  - A13: float route;
  - A21: extra fields in the body;
  - A34: a fabricated cause.
- **3 Substitute a valid sibling** — ran:
  - A5s: `last-event-id` → `idempotency-key`;
  - A25: `/credits` → `/invoices`;
  - A39: `GATEWAY_PORT` → `ORDERS_HEALTH_PORT`.
  - The route-handler tests assert each call site's own path, header and query, not only the mechanism.
- **4 / 5 / 6 Comment, dead region, raw string shadowing** — apply only to the text-scanning guard. `lint-coverage.mjs` does not scan text: it asks ESLint's own resolver (`isPathIgnored`, `calculateConfigForFile`) about each file, so a comment or string cannot satisfy it. The money and import rules are ESLint AST rules, which ignore comments and strings by construction. Not otherwise applicable: every other guard executes code.
- **7 Drop an optional element** — ran: A11 (no `detail` → title), `stockShortages` with none or empty, `readProblem` with non-problem bodies, and the no-factory hook test.
- **8 Literal compared to literal** — checked:
  - The fixture chain test's population is read from the directory and compared with the case table, so a new fixture without a case fails.
  - `lint-coverage`'s population is the filesystem, and its exemptions are a literal list.
- **9 Premise half stale** — checked: the R55 claim is guarded on both halves (status forwarded **and** header forwarded; waiting state **and** schedule honoured).
- **10 Build output joins the population** — `lint-coverage` excludes `.next`, `node_modules`, `coverage` and `generated` **by directory name while walking**, never by filtering its output. ESLint's own ignores are separate, and a file they skip is reported.

A reusable finding: in this agent shell `grep` is a function that runs **ugrep with `--ignore-files`** (`type grep`), so it silently skips gitignored paths. None of my sweeps targeted ignored paths, but a sweep that must see `node_modules` or `.next` has to use `/usr/bin/grep`.

---

## 9. Decisions (and why)

- **`NativeSelect` instead of a Radix `Select`.** It is shadcn's own component, accessible, hydration-safe and testable in jsdom. It removes #7's keyboard-open and double-`pointerup` test gymnastics, and a native select cannot render its menu over a neighbour. Local change: a `wrapperClassName` prop, because the upstream wrapper is `w-fit`, which is what lets a long option widen the control.
- **Progressive login** (form post plus JSON) instead of disabled-until-hydrated. This follows bullet 7 and #7's security reason together.
- **Place order validates on submit** rather than disabling the button: a disabled button explains nothing.
- **Error relay verbatim**, with no BFF envelope, so there is nothing to unwrap (L5).
- **`WEB_SESSION_PASSWORD`** is optional, with a per-process random fallback shared on `globalThis`. No default secret is committed (#7 committed one). **Recommendation:** add `WEB_SESSION_PASSWORD` and `WEB_PORT` to `.env.example`. That file is outside my scope.
- **Web coverage is reported, not enforced** — the same rule as section 4 (feature 34 owns the gates).
- **`QUALITY_ONLY=web`** exists so each web gate can be armed without a 30-minute .NET run. Unset, every section runs.
- **Backstop re-read** uses idempotent "completed reads" notes, because React Query evaluates `refetchInterval` more than once per update (L12).
- **A test seam `backstopMs`** on `OrderDetailView` (like #7's `streamFactory` prop) so the D2/D7 tests use real timers.
- **Currency** is a select of the currencies the catalogue mentions, and follows the retailer on each retailer change. This is #7's behaviour; #7's own "manual override is clobbered on re-select" limitation remains.

## 10. Observations for the leader (not fixed — outside scope)

1. **`openapi.yaml` promises `currency.decimalPoints`, and no REST response carries it** (§2). Route it as a backlog entry or SA-n.
2. **The Gateway does not read `GATEWAY_PORT`.** No `.cs` file references it; `dev-stack.sh` works around this with `ASPNETCORE_URLS`. The README's run recipe should say so. The brief says the coordinator will rewrite the README. **[SUPERSEDED 2026-09-16 by backlog id 96: the Gateway now reads `GATEWAY_PORT` and listens on it, and `dev-stack.sh` no longer sets `ASPNETCORE_URLS` — see `progress/impl_gateway_ignores_gateway_port.md`. True when written.]**
3. **`init.sh` §5's superseded-rule scan passes its `--include` filters after `--`.** GNU grep 3.11 treats them as file operands (`/usr/bin/grep: --include=*.md: No such file or directory`, errors hidden by `2>/dev/null`), so the scan covers every file type under `.`. Its path filter (`^\./(…node_modules/…)`) also does not exclude `apps/web/node_modules` (606 MB) or `apps/web/.next` (224 MB). Measured: one pattern over the repo takes 6.7 s, against 5.5 s with `apps/web` excluded; `init.sh` now takes 32 s for 5 rules. It still passes. A superseded phrase that happened to appear in a third-party package file would now fail `init.sh`.
4. **Extra wire fields beyond `openapi.yaml`** (not contradictions; the schemas do not forbid additional properties):
   - `StockUnavailableProblem.shortages[]` carries `sufficient`;
   - `OrderStreamUpdate.totals` carries `currency`.
5. **Port 3000 is taken on this machine** by another project, so use `WEB_PORT=3010 scripts/dev-stack.sh start`. The script now refuses to start on a busy port.
6. The Projector's summary text renders minor units as whole numbers ("Credit hold of 24 990 EUR approved" for €249.90). This is parity with #7 (`apps/projector/src/domain/summaries.ts:56`); I note it only because the page shows it.

## 11. What remains

Nothing in this feature's contract. The leader-owned items are:
- the `in_review` transition for ids 29 and 30;
- the R55 web-half row;
- routing observation 1 (and 2 and 3 if wanted).

## 12. How to test by hand

```bash
WEB_PORT=3010 scripts/dev-stack.sh start      # infra, build, seed, six services, web (prod build)
# open http://localhost:3010 — operator / GATEWAY_OPERATOR_PASSWORD from .env.example
#  - Place order → "Fill demo order" → Place order → follow the link: waiting state, then the live timeline (compensation path)
#  - Place order with quantity 999999 → the Gateway's own 409 text and the shortages list
#  - Billing → Status "issued" → Register payment → Submit twice → "recorded (HTTP 201)" then "already recorded (HTTP 200)"
#  - /orders/not-a-uuid → the Gateway's validation text
scripts/dev-stack.sh stop-service Projector   # place an order, open it → "Waiting for this order to appear"
scripts/dev-stack.sh start-service Projector  # … it fills in by itself
scripts/dev-stack.sh stop                     # stops web + services; infrastructure stays up
QUALITY_ONLY=web ./quality.sh                 # the web gates only; ./quality.sh runs everything
```

---

## §A. #7 assertion enumeration (generated; one row per `expect(` line)

Counted: 213 expect lines in 15 files; NOT APPLICABLE: 3, NOT PORTED (deliberate): 4, PORTED: 206
| #7 file:line (case at line) | assertion | classification | #8 guard or reason |
|---|---|---|---|
| `app/composables/useOrderDetail.spec.ts:51` | `expect(result?.kind).toBe('ready');` | PORTED | `src/lib/order-detail.test.ts` › *appends a new entry, carrying its causationId* and › *an entry that arrives out of order is placed by occurredAt* |
| `app/composables/useOrderDetail.spec.ts:53` | `expect(result.detail.events.map((e) => e.eventId)).toEqual(['evt-0', 'evt-1']);` | PORTED | `src/lib/order-detail.test.ts` › *appends a new entry, carrying its causationId* and › *an entry that arrives out of order is placed by occurredAt* |
| `app/composables/useOrderDetail.spec.ts:70` | `expect(result.detail.events).toHaveLength(1);` | PORTED | `src/lib/order-detail.test.ts` › *a redelivered entry (eventId already present) returns the SAME document, unchanged* |
| `app/composables/useOrderDetail.spec.ts:82` | `expect(result?.kind).toBe('pending');` | PORTED | `src/lib/order-detail.test.ts` › *is a no-op while the answer is still projection-pending, and for an empty cache* |
| `app/composables/useOrderDetail.spec.ts:102` | `expect(result.detail.status).toBe('confirmed');` | PORTED | `src/lib/order-detail.test.ts` › *patches status, references, totals, cancellationReason and updatedAt* |
| `app/composables/useOrderDetail.spec.ts:103` | `expect(result.detail.references?.despatchReference).toBe('DES-000001');` | PORTED | `src/lib/order-detail.test.ts` › *patches status, references, totals, cancellationReason and updatedAt* |
| `app/composables/useOrderDetail.spec.ts:104` | `expect(result.detail.updatedAt).toBe('2026-08-27T10:10:00.000Z');` | PORTED | `src/lib/order-detail.test.ts` › *patches status, references, totals, cancellationReason and updatedAt* |
| `app/lib/order-stream-client.spec.ts:64` | `await vi.waitFor(() => expect(received.length).toBeGreaterThan(0), { timeout: 3000 });` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — a redelivered order.updated (same type, same eventId) is applied exactly once* (extended to a redelivered timeline.appended) |
| `app/lib/order-stream-client.spec.ts:67` | `expect(received).toHaveLength(1);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — a redelivered order.updated (same type, same eventId) is applied exactly once* (extended to a redelivered timeline.appended) |
| `app/lib/order-stream-client.spec.ts:68` | `expect(received[0]!.eventId).toBe('evt-dup-1');` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — a redelivered order.updated (same type, same eventId) is applied exactly once* (extended to a redelivered timeline.appended) |
| `app/lib/order-stream-client.spec.ts:120` | `expect(orderUpdates.length).toBeGreaterThan(0);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (order.updated first)* |
| `app/lib/order-stream-client.spec.ts:121` | `expect(timelineEntries.length).toBeGreaterThan(0);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (order.updated first)* |
| `app/lib/order-stream-client.spec.ts:123` | `expect(orderUpdates).toHaveLength(1);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (order.updated first)* |
| `app/lib/order-stream-client.spec.ts:124` | `expect(timelineEntries).toHaveLength(1);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (order.updated first)* |
| `app/lib/order-stream-client.spec.ts:125` | `expect(orderUpdates[0]!.eventId).toBe(sharedEventId);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (order.updated first)* |
| `app/lib/order-stream-client.spec.ts:126` | `expect(timelineEntries[0]!.eventId).toBe(sharedEventId);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (order.updated first)* |
| `app/lib/order-stream-client.spec.ts:168` | `expect(orderUpdates.length).toBeGreaterThan(0);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (timeline.appended first)* |
| `app/lib/order-stream-client.spec.ts:169` | `expect(timelineEntries.length).toBeGreaterThan(0);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (timeline.appended first)* |
| `app/lib/order-stream-client.spec.ts:171` | `expect(orderUpdates).toHaveLength(1);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (timeline.appended first)* |
| `app/lib/order-stream-client.spec.ts:172` | `expect(timelineEntries).toHaveLength(1);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (timeline.appended first)* |
| `app/lib/order-stream-client.spec.ts:173` | `expect(orderUpdates[0]!.eventId).toBe(sharedEventId);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (timeline.appended first)* |
| `app/lib/order-stream-client.spec.ts:174` | `expect(timelineEntries[0]!.eventId).toBe(sharedEventId);` | PORTED | `src/lib/order-stream-client.test.ts` › *R51 — the two frames of ONE fact share an eventId and BOTH are applied (timeline.appended first)* |
| `app/lib/order-stream-client.spec.ts:195` | `await vi.waitFor(() => expect(resyncCalls).toBe(1), { timeout: 3000 });` | PORTED | `src/lib/order-stream-client.test.ts` › *stream.ready resumed:false asks the page to re-fetch; resumed:true does not* |
| `app/lib/order-stream-client.spec.ts:243` | `await vi.waitFor(() => expect(received.map((u) => u.eventId)).toEqual(['evt-1', 'evt-2']), { timeout: 5000 });` | PORTED | `src/lib/order-stream-client.test.ts` › *id 30 — a forced mid-stream disconnect is resumed with Last-Event-ID …* |
| `app/lib/order-stream-client.spec.ts:244` | `expect(observedLastEventIdOnSecondConnect).toBe('cursor-1');` | PORTED | `src/lib/order-stream-client.test.ts` › *id 30 — a forced mid-stream disconnect is resumed with Last-Event-ID …* |
| `app/lib/order-stream-client.spec.ts:246` | `expect(resyncCalls).toBe(1);` | PORTED | `src/lib/order-stream-client.test.ts` › *id 30 — a forced mid-stream disconnect is resumed with Last-Event-ID …* |
| `app/lib/order-stream-client.spec.ts:247` | `expect(statuses).toContain('reconnecting');` | PORTED | `src/lib/order-stream-client.test.ts` › *id 30 — a forced mid-stream disconnect is resumed with Last-Event-ID …* |
| `app/lib/problem.spec.ts:33` | `expect(problem?.detail).toBe('"not-a-uuid" is not a valid order id');` | NOT APPLICABLE | Nitro's double wrap does not exist in #8: route handlers relay the Gateway body verbatim (`src/server/gateway.ts` `relay`). The property it protected — the page reads the REAL problem document — is guarded by `src/app/api/route-handlers.test.ts` › *relays the REAL Gateway problem … unchanged*, `src/lib/error-text-chain.test.ts` (all 12 captured fixtures), and arm A10 (re-introducing an envelope → 23 failures) |
| `app/lib/problem.spec.ts:34` | `expect(problem?.code).toBe('VALIDATION_FAILED');` | NOT APPLICABLE | Nitro's double wrap does not exist in #8: route handlers relay the Gateway body verbatim (`src/server/gateway.ts` `relay`). The property it protected — the page reads the REAL problem document — is guarded by `src/app/api/route-handlers.test.ts` › *relays the REAL Gateway problem … unchanged*, `src/lib/error-text-chain.test.ts` (all 12 captured fixtures), and arm A10 (re-introducing an envelope → 23 failures) |
| `app/lib/problem.spec.ts:40` | `expect(problem?.detail).toBe('No invoice with that id');` | NOT APPLICABLE | no nested-vs-flat choice exists in #8 (the body IS the problem); `src/lib/problem.test.ts` › *readProblem parses the response body itself* |
| `app/lib/problem.spec.ts:44` | `expect(problemFromFetchError({ data: { some: 'unrelated shape' } })).toBeUndefined();` | PORTED | `src/lib/problem.test.ts` › *readProblem returns undefined for a body that is not a problem document* and › *uses the fallback only when the server said nothing usable* |
| `app/lib/problem.spec.ts:45` | `expect(problemFromFetchError(null)).toBeUndefined();` | PORTED | `src/lib/problem.test.ts` › *readProblem returns undefined for a body that is not a problem document* and › *uses the fallback only when the server said nothing usable* |
| `app/lib/problem.spec.ts:46` | `expect(problemFromFetchError(undefined)).toBeUndefined();` | PORTED | `src/lib/problem.test.ts` › *readProblem returns undefined for a body that is not a problem document* and › *uses the fallback only when the server said nothing usable* |
| `app/lib/problem.spec.ts:47` | `expect(problemFromFetchError('not even an object')).toBeUndefined();` | PORTED | `src/lib/problem.test.ts` › *readProblem returns undefined for a body that is not a problem document* and › *uses the fallback only when the server said nothing usable* |
| `app/lib/problem.spec.ts:53` | `expect(describeFetchError(nitroWrappedFetchError({ detail: 'Insufficient stock at acceptance for PRD-0001',…` | PORTED | `src/lib/problem.test.ts` › *describeError returns the problem's own detail*, › *an absent detail falls back to the title*, › *uses the fallback only when …* |
| `app/lib/problem.spec.ts:56` | `expect(describeFetchError(nitroWrappedFetchError({ title: 'Insufficient stock' }), 'fallback')).toBe('Insuf…` | PORTED | `src/lib/problem.test.ts` › *describeError returns the problem's own detail*, › *an absent detail falls back to the title*, › *uses the fallback only when …* |
| `app/lib/problem.spec.ts:57` | `expect(describeFetchError(nitroWrappedFetchError({}), 'fallback')).toBe('fallback');` | PORTED | `src/lib/problem.test.ts` › *describeError returns the problem's own detail*, › *an absent detail falls back to the title*, › *uses the fallback only when …* |
| `app/lib/problem.spec.ts:58` | `expect(describeFetchError(new Error('network down'), 'fallback')).toBe('fallback');` | PORTED | `src/lib/problem.test.ts` › *describeError returns the problem's own detail*, › *an absent detail falls back to the title*, › *uses the fallback only when …* |
| `app/pages/billing/index.spec.ts:101` | `expect(within(row).getByText('INV-000027')).toBeTruthy();` | PORTED | `src/features/billing/billing-view.test.tsx` › *renders invoice and credit amounts as decimals from minor units, never the raw integer* |
| `app/pages/billing/index.spec.ts:102` | `expect(within(row).getByText('ORD-000042')).toBeTruthy();` | PORTED | `src/features/billing/billing-view.test.tsx` › *renders invoice and credit amounts as decimals from minor units, never the raw integer* |
| `app/pages/billing/index.spec.ts:104` | `expect(totalCell.textContent).toContain('249.99');` | PORTED | `src/features/billing/billing-view.test.tsx` › *renders invoice and credit amounts as decimals from minor units, never the raw integer* |
| `app/pages/billing/index.spec.ts:105` | `expect(totalCell.textContent).not.toContain('24999');` | PORTED | `src/features/billing/billing-view.test.tsx` › *renders invoice and credit amounts as decimals from minor units, never the raw integer* |
| `app/pages/billing/index.spec.ts:127` | `expect(held.textContent).toContain('249.99');` | PORTED | `src/features/billing/billing-view.test.tsx` › *renders invoice and credit amounts as decimals …* (credit-held) |
| `app/pages/billing/index.spec.ts:128` | `expect(held.textContent).not.toContain('24999');` | PORTED | `src/features/billing/billing-view.test.tsx` › *renders invoice and credit amounts as decimals …* (credit-held) |
| `app/pages/billing/index.spec.ts:145` | `expect(screen.getByTestId('invoices-loading')).toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *shows each table's loading state while its request is UNRESOLVED, then its empty state* |
| `app/pages/billing/index.spec.ts:147` | `expect(screen.queryByTestId('invoices-error')).not.toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *shows each table's loading state while its request is UNRESOLVED, then its empty state* |
| `app/pages/billing/index.spec.ts:148` | `expect(screen.queryByText('No invoices match these filters.')).not.toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *shows each table's loading state while its request is UNRESOLVED, then its empty state* |
| `app/pages/billing/index.spec.ts:156` | `expect(screen.queryByTestId('invoices-loading')).not.toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *shows each table's loading state while its request is UNRESOLVED, then its empty state* |
| `app/pages/billing/index.spec.ts:159` | `expect(emptyText).toBeTruthy();` | PORTED | `src/features/billing/billing-view.test.tsx` › *shows each table's loading state while its request is UNRESOLVED, then its empty state* |
| `app/pages/billing/index.spec.ts:160` | `expect(screen.queryByTestId('invoices-error')).not.toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *shows each table's loading state while its request is UNRESOLVED, then its empty state* |
| `app/pages/billing/index.spec.ts:177` | `expect(referenceInput.value).toMatch(/^PAY-\d{4}-\d{2}-\d{2}-000027$/);` | PORTED | `src/features/billing/billing-view.test.tsx` › *suggests a reference shaped like the spec example …*; the overwrite reaching the wire is in › *"19.99" typed by the user …* (`PAY-OPERATOR-1`) |
| `app/pages/billing/index.spec.ts:180` | `expect(referenceInput.value).toBe('PAY-OPERATOR-OVERRIDE');` | PORTED | `src/features/billing/billing-view.test.tsx` › *suggests a reference shaped like the spec example …*; the overwrite reaching the wire is in › *"19.99" typed by the user …* (`PAY-OPERATOR-1`) |
| `app/pages/billing/index.spec.ts:210` | `await waitFor(() => expect(capturedBody).toBeDefined());` | PORTED | `src/features/billing/billing-view.test.tsx` › *"19.99" typed by the user reaches the wire as exactly 1999 minor units …* |
| `app/pages/billing/index.spec.ts:211` | `expect(capturedBody?.amount.amount).toBe(1999);` | PORTED | `src/features/billing/billing-view.test.tsx` › *"19.99" typed by the user reaches the wire as exactly 1999 minor units …* |
| `app/pages/billing/index.spec.ts:212` | `expect(capturedBody?.amount.currency).toBe('EUR');` | PORTED | `src/features/billing/billing-view.test.tsx` › *"19.99" typed by the user reaches the wire as exactly 1999 minor units …* |
| `app/pages/billing/index.spec.ts:247` | `expect(screen.queryByTestId('payment-outcome-duplicate')).toBeNull();` | PORTED | `src/features/billing/billing-view.test.tsx` › *an accepted payment and a duplicate replay are rendered DIFFERENTLY …* and the route-handler chain case › *accepted, then duplicate, then the real mismatch detail* |
| `app/pages/billing/index.spec.ts:256` | `expect((screen.getByTestId('payment-reference-input') as HTMLInputElement).value).toBe(firstReference);` | NOT PORTED (deliberate) | #8's payment form stays open after an outcome, so the replay is a second submit of the same reference rather than a close/reopen; the suggestion's determinism is asserted directly in `src/features/billing/billing-view.test.tsx` › *suggests a reference shaped like the spec example …* (`suggestPaymentReference` with a fixed date) |
| `app/pages/billing/index.spec.ts:261` | `expect(duplicateOutcome.textContent).toMatch(/already recorded/i);` | PORTED | `src/features/billing/billing-view.test.tsx` › *an accepted payment and a duplicate replay are rendered DIFFERENTLY …* and the route-handler chain case › *accepted, then duplicate, then the real mismatch detail* |
| `app/pages/billing/index.spec.ts:262` | `expect(screen.queryByTestId('payment-outcome-accepted')).toBeNull();` | PORTED | `src/features/billing/billing-view.test.tsx` › *an accepted payment and a duplicate replay are rendered DIFFERENTLY …* and the route-handler chain case › *accepted, then duplicate, then the real mismatch detail* |
| `app/pages/billing/index.spec.ts:263` | `expect(callCount).toBe(2);` | PORTED | `src/features/billing/billing-view.test.tsx` › *an accepted payment and a duplicate replay are rendered DIFFERENTLY …* and the route-handler chain case › *accepted, then duplicate, then the real mismatch detail* |
| `app/pages/billing/index.spec.ts:289` | `expect(errorEl.textContent).toMatch(/payment amount 100 does not match invoice total 24999/i);` | PORTED | `src/features/billing/billing-view.test.tsx` › *a rejected remittance shows the Gateway's own words (real 422), and not on another invoice's form* |
| `app/pages/billing/index.spec.ts:295` | `expect(screen.queryByTestId('payment-error')).not.toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *a rejected remittance shows the Gateway's own words (real 422), and not on another invoice's form* |
| `app/pages/billing/index.spec.ts:315` | `expect(errorEl.textContent).toMatch(/no responder is subscribed to this subject/i);` | PORTED | `src/features/billing/billing-view.test.tsx` › *Billing being down shows each list's own real 503 words, independently* |
| `app/pages/billing/index.spec.ts:316` | `expect(screen.queryByText('No invoices match these filters.')).not.toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *Billing being down shows each list's own real 503 words, independently* |
| `app/pages/billing/index.spec.ts:317` | `expect(screen.queryByTestId('invoices-loading')).not.toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *Billing being down shows each list's own real 503 words, independently* |
| `app/pages/billing/index.spec.ts:334` | `expect(errorEl.textContent).toMatch(/rpc call to "billing\.credit\.list" timed out/i);` | PORTED | `src/features/billing/billing-view.test.tsx` › *a failed credit list does not take the invoice list with it* |
| `app/pages/billing/index.spec.ts:335` | `expect(screen.queryByText('No credit lines match this filter.')).not.toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *a failed credit list does not take the invoice list with it* |
| `app/pages/billing/index.spec.ts:351` | `expect(rows.length).toBe(20);` | PORTED | `src/features/billing/billing-view.test.tsx` › *credit limits page server-side and filter by retailer; …* |
| `app/pages/billing/index.spec.ts:352` | `expect(screen.getByText(/page 1 of 2/i)).toBeTruthy();` | PORTED | `src/features/billing/billing-view.test.tsx` › *credit limits page server-side and filter by retailer; …* |
| `app/pages/billing/index.spec.ts:358` | `expect(rowsAfter.length).toBe(5);` | PORTED | `src/features/billing/billing-view.test.tsx` › *credit limits page server-side and filter by retailer; …* |
| `app/pages/billing/index.spec.ts:374` | `expect(lastRetailerCode).toBeUndefined();` | PORTED | `src/features/billing/billing-view.test.tsx` › *credit limits page server-side and filter by retailer; …* (retailerCode on the wire) |
| `app/pages/billing/index.spec.ts:386` | `await waitFor(() => expect(lastRetailerCode).toBe('CarrefourEs'));` | PORTED | `src/features/billing/billing-view.test.tsx` › *credit limits page server-side and filter by retailer; …* (retailerCode on the wire) |
| `app/pages/billing/index.spec.ts:402` | `expect(await screen.findAllByLabelText('Retailer')).toHaveLength(2);` | PORTED | `src/features/billing/billing-view.test.tsx` › *credit limits page server-side …* resolves both `Retailer` selects and `Status` by label (`getAllByLabelText('Retailer')` destructured into two, `getByLabelText('Status')`) |
| `app/pages/billing/index.spec.ts:403` | `expect(screen.getByLabelText('Status')).toBeInTheDocument();` | PORTED | `src/features/billing/billing-view.test.tsx` › *credit limits page server-side …* resolves both `Retailer` selects and `Status` by label (`getAllByLabelText('Retailer')` destructured into two, `getByLabelText('Status')`) |
| `app/pages/billing/index.spec.ts:417` | `expect(headers).toHaveLength(6 + 8); // credit-limits table + invoices table` | PORTED | `src/features/billing/billing-view.test.tsx` › *exposes real column headers on both tables* (14, `scope=col`) |
| `app/pages/billing/index.spec.ts:418` | `headers.forEach((header) => expect(header).toHaveAttribute('scope', 'col'));` | PORTED | `src/features/billing/billing-view.test.tsx` › *exposes real column headers on both tables* (14, `scope=col`) |
| `app/pages/login.spec.ts:54` | `expect(button).toBeDisabled();` | NOT PORTED (deliberate) | #7 asserted the button STAYS DISABLED before hydration — its fix for a pre-hydration native GET that put the password in the URL. Bullet 7 forbids a control disabled waiting for hydration, so #8 fixes the same defect the other way: the form is `method=post action=/api/auth/login`, so a pre-hydration submit signs in with the password in a POST body. Guarded by `src/features/auth/login-form.test.tsx` › *is a real form posting to the login route …* (arm A28) and `src/app/api/route-handlers.test.ts` › *a plain HTML form post (before hydration) signs in with a 303 …*, and live (§ Live verification) |
| `app/pages/login.spec.ts:71` | `expect(button).not.toBeDisabled();` | PORTED | `src/features/auth/login-form.test.tsx` › *the submit button is operable on the very first render …* (arm A27) |
| `app/pages/login.spec.ts:72` | `expect(button).toHaveTextContent('Sign in');` | PORTED | `src/features/auth/login-form.test.tsx` › *the submit button is operable on the very first render …* (arm A27) |
| `app/pages/login.spec.ts:100` | `expect(errorEl.textContent).toMatch(/username or password is incorrect/i);` | PORTED | `src/features/auth/login-form.test.tsx` › *shows exactly the Gateway's own words …* — through the real login route handler and the REAL captured 401 |
| `app/pages/login.spec.ts:101` | `expect(errorEl.textContent).not.toMatch(/^login failed\.?$/i);` | PORTED | `src/features/auth/login-form.test.tsx` › *shows exactly the Gateway's own words …* — through the real login route handler and the REAL captured 401 |
| `app/pages/orders/[id].spec.ts:116` | `expect(within(pending).getByText(/waiting for this order to appear/i)).toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a 202 renders the NAMED waiting state …* |
| `app/pages/orders/[id].spec.ts:117` | `expect(within(pending).getByText(/not projected yet/i)).toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a 202 renders the NAMED waiting state …* |
| `app/pages/orders/[id].spec.ts:118` | `expect(screen.queryByTestId('order-detail-error')).not.toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a 202 renders the NAMED waiting state …* |
| `app/pages/orders/[id].spec.ts:126` | `expect(await screen.findByText('ORD-000001')).toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *renders header, total, items, references and the timeline from the GET* |
| `app/pages/orders/[id].spec.ts:127` | `expect(screen.getByTestId('order-detail-status')).toHaveTextContent('placed');` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *renders header, total, items, references and the timeline from the GET* |
| `app/pages/orders/[id].spec.ts:129` | `expect(within(timeline).getAllByTestId('timeline-entry')).toHaveLength(1);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *renders header, total, items, references and the timeline from the GET* |
| `app/pages/orders/[id].spec.ts:130` | `expect(within(timeline).getByText('Order placed')).toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *renders header, total, items, references and the timeline from the GET* |
| `app/pages/orders/[id].spec.ts:139` | `await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *R51 — a live timeline.appended is rendered once; its redelivery adds nothing* |
| `app/pages/orders/[id].spec.ts:146` | `expect(within(screen.getByTestId('order-timeline')).getAllByTestId('timeline-entry')).toHaveLength(2);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *R51 — a live timeline.appended is rendered once; its redelivery adds nothing* |
| `app/pages/orders/[id].spec.ts:152` | `expect(within(screen.getByTestId('order-timeline')).getAllByTestId('timeline-entry')).toHaveLength(2);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *R51 — a live timeline.appended is rendered once; its redelivery adds nothing* |
| `app/pages/orders/[id].spec.ts:162` | `await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a live order.updated moves the status badge …* |
| `app/pages/orders/[id].spec.ts:168` | `await waitFor(() => expect(screen.getByTestId('order-detail-status')).toHaveTextContent('confirmed'));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a live order.updated moves the status badge …* |
| `app/pages/orders/[id].spec.ts:184` | `expect(screen.getByTestId('order-detail-status')).toHaveTextContent('placed');` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *stream.ready resumed:false re-fetches the order …* |
| `app/pages/orders/[id].spec.ts:185` | `await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *stream.ready resumed:false re-fetches the order …* |
| `app/pages/orders/[id].spec.ts:186` | `expect(callCount).toBe(1);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *stream.ready resumed:false re-fetches the order …* |
| `app/pages/orders/[id].spec.ts:190` | `await waitFor(() => expect(callCount).toBe(2));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *stream.ready resumed:false re-fetches the order …* |
| `app/pages/orders/[id].spec.ts:191` | `await waitFor(() => expect(screen.getByTestId('order-detail-status')).toHaveTextContent('confirmed'));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *stream.ready resumed:false re-fetches the order …* |
| `app/pages/orders/[id].spec.ts:215` | `expect(screen.getByTestId('order-detail-status')).toHaveTextContent('paid');` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D2 — a fact whose frames are lost entirely still reaches the page …* (the terminal status is reachable only through a re-read, so `callCount ≥ 2` is implied; arm A32) |
| `app/pages/orders/[id].spec.ts:216` | `await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D2 — a fact whose frames are lost entirely still reaches the page …* (the terminal status is reachable only through a re-read, so `callCount ≥ 2` is implied; arm A32) |
| `app/pages/orders/[id].spec.ts:217` | `expect(callCount).toBe(1);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D2 — a fact whose frames are lost entirely still reaches the page …* (the terminal status is reachable only through a re-read, so `callCount ≥ 2` is implied; arm A32) |
| `app/pages/orders/[id].spec.ts:228` | `await waitFor(() => expect(screen.getByTestId('order-detail-status')).toHaveTextContent('completed'));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D2 — a fact whose frames are lost entirely still reaches the page …* (the terminal status is reachable only through a re-read, so `callCount ≥ 2` is implied; arm A32) |
| `app/pages/orders/[id].spec.ts:229` | `expect(callCount).toBeGreaterThanOrEqual(2);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D2 — a fact whose frames are lost entirely still reaches the page …* (the terminal status is reachable only through a re-read, so `callCount ≥ 2` is implied; arm A32) |
| `app/pages/orders/[id].spec.ts:246` | `expect(screen.getByTestId('order-detail-status')).toHaveTextContent('completed');` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D2 — an order that is terminal on first read is never re-read* |
| `app/pages/orders/[id].spec.ts:247` | `expect(callCount).toBe(1);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D2 — an order that is terminal on first read is never re-read* |
| `app/pages/orders/[id].spec.ts:251` | `expect(callCount).toBe(1);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D2 — an order that is terminal on first read is never re-read* |
| `app/pages/orders/[id].spec.ts:283` | `expect(screen.getByTestId('order-detail-status')).toHaveTextContent('paid');` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:284` | `await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:285` | `expect(callCount).toBe(1);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:294` | `await waitFor(() => expect(screen.getByTestId('order-detail-status')).toHaveTextContent('completed'));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:298` | `expect(within(screen.getByTestId('order-timeline')).getAllByTestId('timeline-entry')).toHaveLength(1);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:304` | `await waitFor(() => expect(screen.getByText('Credit released')).toBeInTheDocument());` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:305` | `expect(within(screen.getByTestId('order-timeline')).getAllByTestId('timeline-entry')).toHaveLength(2);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:306` | `expect(callCount).toBe(2);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:311` | `expect(callCount).toBe(2);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *D7 — after a terminal transition this page watched, ONE more read …* (arm A33) |
| `app/pages/orders/[id].spec.ts:329` | `expect(entries).toHaveLength(2);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *an entry whose causationId names an entry here links to it; …* |
| `app/pages/orders/[id].spec.ts:333` | `expect(causation).toHaveTextContent(/caused by/i);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *an entry whose causationId names an entry here links to it; …* |
| `app/pages/orders/[id].spec.ts:334` | `expect(within(causation).getByTestId('timeline-causation-link')).toHaveTextContent('order.placed.v1');` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *an entry whose causationId names an entry here links to it; …* |
| `app/pages/orders/[id].spec.ts:335` | `expect(within(causation).getByTestId('timeline-causation-link')).toHaveAttribute('href', '#timeline-entry-e…` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *an entry whose causationId names an entry here links to it; …* |
| `app/pages/orders/[id].spec.ts:339` | `expect(within(originEntry).queryByTestId('timeline-causation')).not.toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *an entry whose causationId names an entry here links to it; …* |
| `app/pages/orders/[id].spec.ts:359` | `expect(within(entry).queryByTestId('timeline-causation')).not.toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *an entry whose causationId names an entry here links to it; one naming nothing here … renders no link* (arm A34) |
| `app/pages/orders/[id].spec.ts:360` | `expect(screen.queryByTestId('order-detail-error')).not.toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *an entry whose causationId names an entry here links to it; one naming nothing here … renders no link* (arm A34) |
| `app/pages/orders/[id].spec.ts:370` | `expect(within(entry).queryByTestId('timeline-causation')).not.toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › same case, the origin entry (no causationId) |
| `app/pages/orders/[id].spec.ts:371` | `expect(within(entry).getByText('Order placed')).toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › same case, the origin entry (no causationId) |
| `app/pages/orders/[id].spec.ts:380` | `await waitFor(() => expect(FakeEventSource.instances).toHaveLength(1));` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a live frame carrying a causationId renders the same link as a loaded entry* |
| `app/pages/orders/[id].spec.ts:397` | `expect(within(causation).getByTestId('timeline-causation-link')).toHaveTextContent('order.placed.v1');` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a live frame carrying a causationId renders the same link as a loaded entry* |
| `app/pages/orders/[id].spec.ts:415` | `expect(errorEl.textContent).toMatch(/order id must be a valid uuid/i);` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a failed load shows the Gateway's own words (real order-unknown-404 / order-malformed-id-400)* |
| `app/pages/orders/[id].spec.ts:416` | `expect(screen.queryByTestId('order-detail-loading')).not.toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a failed load shows the Gateway's own words (real order-unknown-404 / order-malformed-id-400)* |
| `app/pages/orders/[id].spec.ts:417` | `expect(screen.queryByTestId('order-detail-pending')).not.toBeInTheDocument();` | PORTED | `src/features/orders/order-detail-view.test.tsx` › *a failed load shows the Gateway's own words (real order-unknown-404 / order-malformed-id-400)* |
| `app/pages/orders/index.spec.ts:55` | `expect((await screen.findAllByText('ORD-000001')).length).toBeGreaterThan(0);` | PORTED | `src/features/orders/orders-list.test.tsx` › *renders order rows with a link to each order …* |
| `app/pages/orders/index.spec.ts:71` | `expect(screen.getByTestId('orders-loading')).toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state …* (arm A30) |
| `app/pages/orders/index.spec.ts:73` | `expect(screen.queryByTestId('orders-error')).not.toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state …* (arm A30) |
| `app/pages/orders/index.spec.ts:74` | `expect(screen.queryByText('No orders match these filters.')).not.toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state …* (arm A30) |
| `app/pages/orders/index.spec.ts:82` | `expect(screen.queryByTestId('orders-loading')).not.toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state …* (arm A30) |
| `app/pages/orders/index.spec.ts:85` | `expect(emptyText).toBeTruthy();` | PORTED | `src/features/orders/orders-list.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state …* (arm A30) |
| `app/pages/orders/index.spec.ts:86` | `expect(screen.queryByTestId('orders-error')).not.toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state …* (arm A30) |
| `app/pages/orders/index.spec.ts:96` | `expect(screen.queryByTestId('orders-error')).not.toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › same case, empty half |
| `app/pages/orders/index.spec.ts:112` | `expect(errorEl.textContent).toMatch(/rpc call to "order\.list" timed out/i);` | PORTED | `src/features/orders/orders-list.test.tsx` › *a failed list shows the Gateway's own detail (real captured 400) …* |
| `app/pages/orders/index.spec.ts:113` | `expect(screen.queryByText('No orders match these filters.')).not.toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *a failed list shows the Gateway's own detail (real captured 400) …* |
| `app/pages/orders/index.spec.ts:114` | `expect(screen.queryByTestId('orders-loading')).not.toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *a failed list shows the Gateway's own detail (real captured 400) …* |
| `app/pages/orders/index.spec.ts:131` | `expect(await screen.findByLabelText('Status')).toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *the status and retailer filters reach the query …* (resolved by `getByLabelText('Status'/'Retailer')`) |
| `app/pages/orders/index.spec.ts:132` | `expect(screen.getByLabelText('Retailer')).toBeInTheDocument();` | PORTED | `src/features/orders/orders-list.test.tsx` › *the status and retailer filters reach the query …* (resolved by `getByLabelText('Status'/'Retailer')`) |
| `app/pages/orders/index.spec.ts:145` | `expect(headers.map((h) => h.textContent?.trim())).toEqual(['Reference', 'Date', 'Retailer', 'Company', 'Sta…` | PORTED | `src/features/orders/orders-list.test.tsx` › *exposes labelled filters and real column headers* |
| `app/pages/orders/index.spec.ts:146` | `headers.forEach((header) => expect(header).toHaveAttribute('scope', 'col'));` | PORTED | `src/features/orders/orders-list.test.tsx` › *exposes labelled filters and real column headers* |
| `app/pages/orders/place.accessibility.spec.ts:49` | `expect(await screen.findByLabelText('Retailer')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *when the catalogue cannot be loaded, the codes can still be typed by hand* (every field resolved by its label in the fallback branch) |
| `app/pages/orders/place.accessibility.spec.ts:50` | `expect(screen.getByLabelText('Company')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *when the catalogue cannot be loaded, the codes can still be typed by hand* (every field resolved by its label in the fallback branch) |
| `app/pages/orders/place.accessibility.spec.ts:51` | `expect(screen.getByLabelText('Currency')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *when the catalogue cannot be loaded, the codes can still be typed by hand* (every field resolved by its label in the fallback branch) |
| `app/pages/orders/place.accessibility.spec.ts:52` | `expect(screen.getByLabelText('Product')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *when the catalogue cannot be loaded, the codes can still be typed by hand* (every field resolved by its label in the fallback branch) |
| `app/pages/orders/place.accessibility.spec.ts:53` | `expect(screen.getByLabelText('Quantity')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *when the catalogue cannot be loaded, the codes can still be typed by hand* (every field resolved by its label in the fallback branch) |
| `app/pages/orders/place.accessibility.spec.ts:54` | `expect(screen.getByLabelText('Unit price override')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *when the catalogue cannot be loaded, the codes can still be typed by hand* (every field resolved by its label in the fallback branch) |
| `app/pages/orders/place.accessibility.spec.ts:55` | `expect(screen.getByLabelText('Line discount')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *when the catalogue cannot be loaded, the codes can still be typed by hand* (every field resolved by its label in the fallback branch) |
| `app/pages/orders/place.accessibility.spec.ts:56` | `expect(screen.getByLabelText('Notes')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *when the catalogue cannot be loaded, the codes can still be typed by hand* (every field resolved by its label in the fallback branch) |
| `app/pages/orders/place.accessibility.spec.ts:71` | `expect(await screen.findByRole('combobox', { name: 'Retailer' })).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *offers retailer, company and product from the catalogue, and every control is reachable by its label* |
| `app/pages/orders/place.accessibility.spec.ts:72` | `expect(await screen.findByRole('combobox', { name: 'Company' })).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *offers retailer, company and product from the catalogue, and every control is reachable by its label* |
| `app/pages/orders/place.accessibility.spec.ts:73` | `expect(await screen.findByRole('combobox', { name: 'Product' })).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *offers retailer, company and product from the catalogue, and every control is reachable by its label* |
| `app/pages/orders/place.accessibility.spec.ts:74` | `expect(screen.getByLabelText('Currency')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *offers retailer, company and product from the catalogue, and every control is reachable by its label* |
| `app/pages/orders/place.accessibility.spec.ts:75` | `expect(screen.getByLabelText('Quantity')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *offers retailer, company and product from the catalogue, and every control is reachable by its label* |
| `app/pages/orders/place.accessibility.spec.ts:76` | `expect(screen.getByLabelText('Unit price override')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *offers retailer, company and product from the catalogue, and every control is reachable by its label* |
| `app/pages/orders/place.accessibility.spec.ts:77` | `expect(screen.getByLabelText('Line discount')).toBeInTheDocument();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *offers retailer, company and product from the catalogue, and every control is reachable by its label* |
| `app/pages/orders/place.currency.spec.ts:35` | `expect(currencyInput.value).toBe('EUR');` | PORTED | `src/features/orders/place-order-form.test.tsx` › *the currency follows the selected retailer …* (arm A37) |
| `app/pages/orders/place.currency.spec.ts:57` | `await waitFor(() => expect(currencyInput.value).toBe('GBP'));` | PORTED | `src/features/orders/place-order-form.test.tsx` › *the currency follows the selected retailer …* (arm A37) |
| `app/pages/orders/place.error.spec.ts:52` | `expect(errorEl.textContent).toMatch(/insufficient stock for 1 line\(s\) at acceptance/i);` | PORTED | `src/features/orders/place-order-form.test.tsx` › *renders that problem's own detail and its per-product shortages* — through the real route handler and the REAL captured 409 (arm A12) |
| `app/pages/orders/place.error.spec.ts:55` | `expect(shortages.textContent).toMatch(/prd-0001/i);` | PORTED | `src/features/orders/place-order-form.test.tsx` › *renders that problem's own detail and its per-product shortages* — through the real route handler and the REAL captured 409 (arm A12) |
| `app/pages/orders/place.error.spec.ts:56` | `expect(shortages.textContent).toMatch(/requested 50/i);` | PORTED | `src/features/orders/place-order-form.test.tsx` › *renders that problem's own detail and its per-product shortages* — through the real route handler and the REAL captured 409 (arm A12) |
| `app/pages/orders/place.error.spec.ts:57` | `expect(shortages.textContent).toMatch(/only 12 available/i);` | PORTED | `src/features/orders/place-order-form.test.tsx` › *renders that problem's own detail and its per-product shortages* — through the real route handler and the REAL captured 409 (arm A12) |
| `app/pages/orders/place.error.spec.ts:80` | `expect(errorEl.textContent).toMatch(/no responder is subscribed to this subject/i);` | PORTED | `src/features/orders/place-order-form.test.tsx` › *a Gateway 503 is shown in its own words (real captured body)* |
| `app/pages/orders/place.error.spec.ts:81` | `expect(errorEl.textContent).not.toMatch(/placing the order failed\./i);` | PORTED | `src/features/orders/place-order-form.test.tsx` › *a Gateway 503 is shown in its own words (real captured body)* |
| `app/pages/orders/place.hydration.spec.ts:41` | `expect(button).toBeDisabled();` | NOT PORTED (deliberate) | #7 asserted Place order STAYS disabled before hydration. Bullet 7 forbids that shape, and this form carries no secret, so #8 keeps it operable: `src/features/orders/place-order-form.test.tsx` › *Place order is operable on the first render …* (arm A29) |
| `app/pages/orders/place.spec.ts:31` | `expect(button).toHaveTextContent('Place order');` | PORTED | `src/features/orders/place-order-form.test.tsx` › *Place order is operable on the first render …* and › *shows "Placing…" while the request is UNRESOLVED, then re-enables* |
| `app/pages/orders/place.spec.ts:32` | `expect(button).not.toHaveTextContent('Placing');` | PORTED | `src/features/orders/place-order-form.test.tsx` › *Place order is operable on the first render …* and › *shows "Placing…" while the request is UNRESOLVED, then re-enables* |
| `app/pages/orders/place.spec.ts:42` | `expect(button).toBeDisabled();` | NOT PORTED (deliberate) | #7's form-validity guard kept the button disabled until retailer and company were filled. #8 keeps it enabled and says what is missing on submit — a disabled button explains nothing (`src/features/orders/place-order-form.test.tsx` › *an incomplete order says what is missing and sends nothing*) |
| `app/pages/orders/place.spec.ts:50` | `expect(button).not.toBeDisabled();` | PORTED | `src/features/orders/place-order-form.test.tsx` › *Place order is operable on the first render …* — except the first assertion, see its own line |
| `app/pages/orders/place.spec.ts:51` | `expect(button).toHaveTextContent('Place order');` | PORTED | `src/features/orders/place-order-form.test.tsx` › *Place order is operable on the first render …* — except the first assertion, see its own line |
| `app/pages/orders/place.unit-price.spec.ts:34` | `expect(unitPriceInput.value).toBe('249.99');` | PORTED | `src/features/orders/place-order-form.test.tsx` › *"Fill demo order" pre-fills 249.99 as a decimal (not 24999) …* |
| `app/pages/orders/place.unit-price.spec.ts:35` | `expect(unitPriceInput.value).not.toBe('24999');` | PORTED | `src/features/orders/place-order-form.test.tsx` › *"Fill demo order" pre-fills 249.99 as a decimal (not 24999) …* |
| `app/pages/orders/place.unit-price.spec.ts:72` | `await waitFor(() => expect(capturedBody).toBeDefined());` | PORTED | `src/features/orders/place-order-form.test.tsx` › *"19.99" typed as a unit-price override reaches the wire as exactly 1999 …* (arm A13) |
| `app/pages/orders/place.unit-price.spec.ts:73` | `expect(capturedBody?.lines?.[0]?.unitPrice).toBe(1999);` | PORTED | `src/features/orders/place-order-form.test.tsx` › *"19.99" typed as a unit-price override reaches the wire as exactly 1999 …* (arm A13) |
| `app/pages/orders/place.unit-price.spec.ts:105` | `await waitFor(() => expect(capturedBody).toBeDefined());` | PORTED | `src/features/orders/place-order-form.test.tsx` › same case (`5.50` → 550) |
| `app/pages/orders/place.unit-price.spec.ts:106` | `expect(capturedBody?.lines?.[0]?.lineDiscount).toBe(550);` | PORTED | `src/features/orders/place-order-form.test.tsx` › same case (`5.50` → 550) |
| `app/pages/stock/index.spec.ts:56` | `expect(within(row).getByTestId('stock-units').textContent).toContain('40');` | PORTED | `src/features/stock/stock-view.test.tsx` › *renders on-hand, reserved and available …* |
| `app/pages/stock/index.spec.ts:57` | `expect(within(row).getByTestId('stock-available').textContent).toContain('30');` | PORTED | `src/features/stock/stock-view.test.tsx` › *renders on-hand, reserved and available …* |
| `app/pages/stock/index.spec.ts:70` | `expect(lastBelowThreshold).toBeUndefined();` | PORTED | `src/features/stock/stock-view.test.tsx` › *the low-stock toggle and the text filters reach the query* |
| `app/pages/stock/index.spec.ts:74` | `await waitFor(() => expect(lastBelowThreshold).toBe('true'));` | PORTED | `src/features/stock/stock-view.test.tsx` › *the low-stock toggle and the text filters reach the query* |
| `app/pages/stock/index.spec.ts:89` | `expect(screen.getByTestId('stock-loading')).toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state* (arm A30b) |
| `app/pages/stock/index.spec.ts:91` | `expect(screen.queryByTestId('stock-error')).not.toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state* (arm A30b) |
| `app/pages/stock/index.spec.ts:92` | `expect(screen.queryByText('No stock lines match these filters.')).not.toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state* (arm A30b) |
| `app/pages/stock/index.spec.ts:100` | `expect(screen.queryByTestId('stock-loading')).not.toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state* (arm A30b) |
| `app/pages/stock/index.spec.ts:103` | `expect(emptyText).toBeTruthy();` | PORTED | `src/features/stock/stock-view.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state* (arm A30b) |
| `app/pages/stock/index.spec.ts:104` | `expect(screen.queryByTestId('stock-error')).not.toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *shows the loading state while the request is UNRESOLVED, then the empty state* (arm A30b) |
| `app/pages/stock/index.spec.ts:132` | `await waitFor(() => expect(capturedBody).toBeDefined());` | PORTED | `src/features/stock/stock-view.test.tsx` › *sends the typed amount as units to ADD …* (the exact wire body, so #7's two `not.toBe` lines are subsumed; arm A36) |
| `app/pages/stock/index.spec.ts:133` | `expect(capturedBody?.companyCode).toBe('IBERFOODS');` | PORTED | `src/features/stock/stock-view.test.tsx` › *sends the typed amount as units to ADD …* (the exact wire body, so #7's two `not.toBe` lines are subsumed; arm A36) |
| `app/pages/stock/index.spec.ts:134` | `expect(capturedBody?.lines).toEqual([{ productCode: 'PRD-0001', units: 110 }]);` | PORTED | `src/features/stock/stock-view.test.tsx` › *sends the typed amount as units to ADD …* (the exact wire body, so #7's two `not.toBe` lines are subsumed; arm A36) |
| `app/pages/stock/index.spec.ts:138` | `expect(capturedBody?.lines[0]?.units).not.toBe(40);` | PORTED | `src/features/stock/stock-view.test.tsx` › *sends the typed amount as units to ADD …* (the exact wire body, so #7's two `not.toBe` lines are subsumed; arm A36) |
| `app/pages/stock/index.spec.ts:139` | `expect(capturedBody?.lines[0]?.units).not.toBe(150);` | PORTED | `src/features/stock/stock-view.test.tsx` › *sends the typed amount as units to ADD …* (the exact wire body, so #7's two `not.toBe` lines are subsumed; arm A36) |
| `app/pages/stock/index.spec.ts:155` | `expect(form.textContent).toMatch(/adds.*on-hand.*delta.*not a target level/is);` | PORTED | `src/features/stock/stock-view.test.tsx` › same case (delta text) |
| `app/pages/stock/index.spec.ts:173` | `expect(errorEl.textContent).toMatch(/could not load stock/i);` | PORTED | `src/features/stock/stock-view.test.tsx` › *Fulfillment being down shows the Gateway's own 503 words (real captured body) …* (stronger: #7 matched only `/could not load stock/`) |
| `app/pages/stock/index.spec.ts:176` | `expect(screen.queryByText('No stock lines match these filters.')).not.toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *Fulfillment being down shows the Gateway's own 503 words (real captured body) …* (stronger: #7 matched only `/could not load stock/`) |
| `app/pages/stock/index.spec.ts:177` | `expect(screen.queryByTestId('stock-loading')).not.toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *Fulfillment being down shows the Gateway's own 503 words (real captured body) …* (stronger: #7 matched only `/could not load stock/`) |
| `app/pages/stock/index.spec.ts:205` | `expect(errorEl.textContent).toMatch(/no stock line for iberfoods\/prd-0001/i);` | PORTED | `src/features/stock/stock-view.test.tsx` › *a rejected replenish shows the Gateway's own words (real 404), and the error does not follow the form to another line* |
| `app/pages/stock/index.spec.ts:214` | `expect(screen.queryByTestId('replenish-error')).not.toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *a rejected replenish shows the Gateway's own words (real 404), and the error does not follow the form to another line* |
| `app/pages/stock/index.spec.ts:226` | `expect(await screen.findByLabelText('Company')).toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *the low-stock toggle and the text filters reach the query* (by label) |
| `app/pages/stock/index.spec.ts:227` | `expect(screen.getByLabelText('Product')).toBeInTheDocument();` | PORTED | `src/features/stock/stock-view.test.tsx` › *the low-stock toggle and the text filters reach the query* (by label) |
| `app/pages/stock/index.spec.ts:240` | `expect(headers).toHaveLength(7);` | PORTED | `src/features/stock/stock-view.test.tsx` › *exposes real column headers* (7, the last an sr-only `Actions`) |
| `app/pages/stock/index.spec.ts:241` | `expect(headers.slice(0, 6).map((h) => h.textContent?.trim())).toEqual(['Company', 'Product', 'On hand', 'Re…` | PORTED | `src/features/stock/stock-view.test.tsx` › *exposes real column headers* (7, the last an sr-only `Actions`) |
| `app/pages/stock/index.spec.ts:242` | `headers.forEach((header) => expect(header).toHaveAttribute('scope', 'col'));` | PORTED | `src/features/stock/stock-view.test.tsx` › *exposes real column headers* (7, the last an sr-only `Actions`) |
| `server/utils/stream-proxy.spec.ts:44` | `expect(response.ok).toBe(true);` | PORTED | `src/app/api/orders/stream/stream-route.test.ts` › *attaches the bearer token and forwards orderId and the browser's Last-Event-ID verbatim* (arms A5, A5s) and `tests-integration/stream-proxy.test.ts` › *forwards Last-Event-ID through the real server* |
| `server/utils/stream-proxy.spec.ts:45` | `expect(received?.headers['last-event-id']).toBe('1755511234901-18');` | PORTED | `src/app/api/orders/stream/stream-route.test.ts` › *attaches the bearer token and forwards orderId and the browser's Last-Event-ID verbatim* (arms A5, A5s) and `tests-integration/stream-proxy.test.ts` › *forwards Last-Event-ID through the real server* |
| `server/utils/stream-proxy.spec.ts:50` | `expect(received?.headers['last-event-id']).toBeUndefined();` | PORTED | `src/app/api/orders/stream/stream-route.test.ts` › *sends no Last-Event-ID and no orderId when the browser sent none* |
| `server/utils/stream-proxy.spec.ts:55` | `expect(received?.headers.authorization).toBe('Bearer the-real-jwt');` | PORTED | `src/app/api/orders/stream/stream-route.test.ts` › *attaches the bearer token …* (arm A8) |
| `server/utils/stream-proxy.spec.ts:60` | `expect(received?.url).toBe('/orders/stream?orderId=ord-abc');` | PORTED | `src/app/api/orders/stream/stream-route.test.ts` › *attaches the bearer token and forwards orderId …* |
| `server/utils/stream-proxy.spec.ts:65` | `expect(received?.url).toBe('/orders/stream');` | PORTED | `src/app/api/orders/stream/stream-route.test.ts` › *sends no Last-Event-ID and no orderId …* |
| `server/utils/stream-proxy.spec.ts:70` | `expect(received?.url).toBe('/orders/stream?orderId=ord-abc');` | PORTED | `src/app/api/orders/stream/stream-route.test.ts` › *attaches the bearer token and forwards orderId and … Last-Event-ID* (both together) |
| `server/utils/stream-proxy.spec.ts:71` | `expect(received?.headers['last-event-id']).toBe('cursor-9');` | PORTED | `src/app/api/orders/stream/stream-route.test.ts` › *attaches the bearer token and forwards orderId and … Last-Event-ID* (both together) |

## §B. Behavioural arm table (final code; generated from `arm_results.json`)

Each row: the claim's unit, the single planted mutation, the named test files' totals while armed, the FIRST failing test and its first message line (truncated at 300 characters by the harness), and the byte comparison after restore. A40/A41 are in §5.

| Arm | Claim (unit) | File | Mutation | Result | First failing test › verbatim message | Restored (cmp) |
|---|---|---|---|---|---|---|
| A1 | order.updated and timeline.appended are de-duplicated SEPARATELY | `src/lib/order-stream-client.ts` | one shared set (seenTimelineEntryIds aliased to seenOrderUpdateIds) | 7 failed / 37 total | *useOrderStream — the SSE hook, driven by a fake EventSource reports status transitions and routes each frame type to its own handler, with the snapshot seeded* › `AssertionError: expected [] to deeply equal [ 'e1' ]` | identical |
| A2 | a redelivered frame of the same type is applied once (R51) | `src/lib/order-stream-client.ts` | delete the order.updated redelivery check | 3 failed / 16 total | *useOrderStream — the SSE hook, driven by a fake EventSource reports status transitions and routes each frame type to its own handler, with the snapshot seeded* › `AssertionError: expected "vi.fn()" to not be called at all, but actually been called 1 times` | identical |
| A3 | the snapshot seeds BOTH per-type sets | `src/lib/order-stream-client.ts` | seed only the order-update set | 2 failed / 37 total | *useOrderStream — the SSE hook, driven by a fake EventSource reports status transitions and routes each frame type to its own handler, with the snapshot seeded* › `AssertionError: expected "vi.fn()" to not be called at all, but actually been called 1 times` | identical |
| A4 | stream.ready resumed:false triggers a re-fetch | `src/lib/order-stream-client.ts` | delete the resync call | 4 failed / 37 total | *useOrderStream — the SSE hook, driven by a fake EventSource reports status transitions and routes each frame type to its own handler, with the snapshot seeded* › `AssertionError: expected "vi.fn()" to be called 1 times, but got 0 times` | identical |
| A5 | Last-Event-ID is forwarded to the Gateway | `src/server/stream-proxy.ts` | drop the Last-Event-ID header | 1 failed / 10 total | *GET /api/orders/stream — the SSE proxy attaches the bearer token and forwards orderId and the browser's Last-Event-ID verbatim* › `AssertionError: expected undefined to be '1755511234901-18' // Object.is equality` | identical |
| A5s | Last-Event-ID is read from the right incoming header (substitution) | `src/app/api/orders/stream/route.ts` | read idempotency-key instead of last-event-id | 1 failed / 10 total | *GET /api/orders/stream — the SSE proxy attaches the bearer token and forwards orderId and the browser's Last-Event-ID verbatim* › `AssertionError: expected undefined to be '1755511234901-18' // Object.is equality` | identical |
| A6 | the upstream request is aborted when the browser stops reading | `src/server/stream-proxy.ts` | cancel() no longer aborts the upstream | 1 failed / 10 total | *GET /api/orders/stream — the SSE proxy closes the upstream Gateway connection when the browser stops reading (response body cancelled)* › `AssertionError: the upstream Gateway connection must close once the browser stops reading: expected false to be true // Object.is equality` | identical |
| A7 | the upstream request is aborted when the incoming request aborts | `src/app/api/orders/stream/route.ts` | drop the request.signal listener | 1 failed / 10 total | *GET /api/orders/stream — the SSE proxy closes the upstream Gateway connection when the incoming request is aborted* › `AssertionError: the upstream Gateway connection must close once the incoming request aborts: expected false to be true // Object.is equality` | identical |
| A8 | the bearer token is attached server-side on the stream | `src/server/stream-proxy.ts` | no Authorization header | 1 failed / 10 total | *GET /api/orders/stream — the SSE proxy attaches the bearer token and forwards orderId and the browser's Last-Event-ID verbatim* › `AssertionError: expected undefined to be 'Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiO…' // Object.is equality` | identical |
| A9 | an error shows the problem's detail, not a generic message | `src/lib/problem.ts` | describeError ignores detail | 25 failed / 92 total | *the error a user sees is the Gateway's own detail — every captured answer, through the real route handler login-bad-credentials-401* › `AssertionError: expected 'Bad credentials' to be 'username or password is incorrect' // Object.is equality` | identical |
| A10 | the problem document reaches the browser WITHOUT an envelope (#7's defect shape) | `src/server/gateway.ts` | wrap non-2xx bodies in a Nitro-like envelope | 23 failed / 75 total | *the error a user sees is the Gateway's own detail — every captured answer, through the real route handler login-bad-credentials-401* › `AssertionError: expected 'GENERIC FALLBACK' to be 'username or password is incorrect' // Object.is equality` | identical |
| A11 | no detail → the title is shown (attack 7, optional element absent) | `src/lib/problem.ts` | delete the title fallback | 2 failed / 13 total | *problem documents — the error the user sees is THAT error's own text an absent detail falls back to the title — the optional element simply missing (defeat-list attack 7)* › `AssertionError: expected 'fallback' to be 'The owning context is unreachable' // Object.is equality` | identical |
| A12 | shortages of a 409 are rendered | `src/lib/problem.ts` | stockShortages always undefined | 2 failed / 23 total | *problem documents — the error the user sees is THAT error's own text stockShortages reads a 409 STOCK_UNAVAILABLE problem's shortages, and nothing else* › `AssertionError: expected undefined to deeply equal [ { productCode: 'PRD-0001', …(2) } ]` | identical |
| A13 | "19.99" -> 1999 without floating point | `src/lib/money.ts` | float route: Math.round(Number(x) * 10**exp) | 6 failed / 57 total | *money — decimal strings become exact integer minor units, never through floating point the classic float failures parse exactly: "0.29" → 29, "19.99" → 1999, "1.10" → 110* › `AssertionError: expected 28 to be 29 // Object.is equality` | identical |
| A14 | the exponent comes from the currency, not an assumed 2 | `src/lib/money.ts` | hard-code 2 | 8 failed / 57 total | *money — decimal strings become exact integer minor units, never through floating point "1.005" is REJECTED for a 2-decimal currency (never rounded to 100 or 101) and is exactly 1005 for a 3-decimal one* › `AssertionError: expected undefined to be 1005 // Object.is equality` | identical |
| A15 | the payment amount is the TYPED amount, in the invoice currency (corruption) | `src/features/billing/billing-view.tsx` | send the invoice total and a literal EUR | 2 failed / 15 total | *BillingView — Register payment (R47/R48) "19.99" typed by the user reaches the wire as exactly 1999 minor units, in the invoice currency, from the operator* › `AssertionError: expected { …(4) } to match object { …(3) }` | identical |
| A16 | accepted and duplicate payments render distinctly (R47/R48) | `src/features/billing/billing-view.tsx` | treat every outcome as accepted | 2 failed / 15 total | *BillingView — Register payment (R47/R48) an accepted payment and a duplicate replay are rendered DIFFERENTLY, each saying what happened* › `Error: Unable to find an element by: [data-testid="payment-outcome-duplicate"]` | identical |
| A17 | a 202 is a named waiting state, not a ready document | `src/hooks/use-order-detail.ts` | treat 202 as ready | 3 failed / 21 total | *OrderDetailView — R55 honest waiting state a 202 renders the NAMED waiting state (not an error, not a 404, not a bare spinner), retries on the Gateway's schedule, then shows the order* › `Error: Unable to find an element by: [data-testid="order-detail-pending"]` | identical |
| A18 | the retry follows the Gateway's schedule | `src/hooks/use-order-detail.ts` | ignore the hint, poll every 50 ms | 1 failed / 21 total | *OrderDetailView — R55 honest waiting state waits the Gateway's interval before asking again — no tight loop* › `AssertionError: GET /api/orders/{id} calls within 200 ms of a 202 that asked for 400 ms: expected 5 to be 1 // Object.is equality` | identical |
| A19 | Retry-After survives the proxy | `src/server/gateway.ts` | stop forwarding retry-after | 1 failed / 25 total | *authenticated proxying — every Gateway call is made here, with the token attached here R55 — a 202 projection-pending answer keeps its status AND its Retry-After header* › `AssertionError: expected null to be '2' // Object.is equality` | identical |
| A20 | no stream is opened while the answer is still 202 | `src/features/orders/order-detail-view.tsx` | enable the stream immediately | 4 failed / 21 total | *OrderDetailView — R55 honest waiting state a 202 renders the NAMED waiting state (not an error, not a 404, not a bare spinner), retries on the Gateway's schedule, then shows the order* › `AssertionError: expected [ FakeEventSource{ …(4) } ] to have a length of +0 but got 1` | identical |
| A21 | the login response body never carries the token | `src/app/api/auth/login/route.ts` | return the whole session | 1 failed / 25 total | *login — the JWT never reaches the browser a JSON login answers identity only; the token is sealed into an httpOnly cookie and appears in no body* › `AssertionError: expected { authenticated: true, …(5) } to deeply equal { authenticated: true, …(3) }` | identical |
| A22 | the session cookie is httpOnly | `src/server/session.ts` | httpOnly false on the session cookie | 3 failed / 25 total | *login — the JWT never reaches the browser a JSON login answers identity only; the token is sealed into an httpOnly cookie and appears in no body* › `AssertionError: expected 'otc_session=Fe26.2*1*38f3f4498d302441…' to match /HttpOnly/i` | identical |
| A23 | a Gateway 401 clears the session | `src/server/gateway.ts` | keep the session | 1 failed / 25 total | *authenticated proxying — every Gateway call is made here, with the token attached here a Gateway 401 (token expired) is relayed and the session cookie is cleared* › `AssertionError: expected '<no Set-Cookie header: the session wa…' to match /^otc_session=;.*Max-Age=0/i` | identical |
| A24 | Idempotency-Key is forwarded on POST /orders | `src/app/api/orders/route.ts` | forward nothing | 1 failed / 25 total | *authenticated proxying — every Gateway call is made here, with the token attached here POST /api/orders forwards the body bytes and the Idempotency-Key, and relays the 201* › `AssertionError: expected undefined to be '6b3f1d0e-3a9f-4a53-9b61-0c7d9a1f2e10' // Object.is equality` | identical |
| A25 | /api/credits maps to the Gateway's /credits (substitution) | `src/app/api/credits/route.ts` | point it at /invoices | 1 failed / 25 total | *authenticated proxying — every Gateway call is made here, with the token attached here invoices, credits and stock map to their own Gateway paths* › `AssertionError: expected [ '/invoices?status=issued', …(2) ] to deeply equal [ '/invoices?status=issued', …(2) ]` | identical |
| A26 | the catalogue proxies only its literal collections | `src/app/api/catalog/[kind]/route.ts` | drop the allow-list | 1 failed / 25 total | *authenticated proxying — every Gateway call is made here, with the token attached here the catalogue proxies only its three literal collections* › `AssertionError: expected 599 to be 404 // Object.is equality` | identical |
| A27 | the login button is operable on first render | `src/features/auth/login-form.tsx` | disabled by an always-truthy value (#7's shape) | 4 failed / 6 total | *LoginForm the submit button is operable on the very first render — never disabled waiting for hydration (#7 Pass 2a/4)* › `Error: expect(element).toBeEnabled()` | identical |
| A28 | the login form still signs in before hydration | `src/features/auth/login-form.tsx` | no action on the form | 1 failed / 6 total | *LoginForm is a real form posting to the login route, so a submit before hydration still signs in (no password in a URL)* › `Error: expect(element).toHaveAttribute("action", "/api/auth/login") // element.getAttribute("action") === "/api/auth/login"` | identical |
| A29 | Place order is operable on first render | `src/features/orders/place-order-form.tsx` | disabled by the catalogue loading | 1 failed / 16 total | *PlaceOrderForm Place order is operable on the first render — never disabled waiting for hydration or for the catalogue* › `Error: expect(element).toBeEnabled()` | identical |
| A30 | the orders loading state is rendered | `src/features/orders/orders-list.tsx` | render nothing while loading | 1 failed / 5 total | *OrdersList shows the loading state while the request is UNRESOLVED, then the empty state — three distinct renderings* › `Error: Unable to find an element by: [data-testid="orders-loading"]` | identical |
| A31 | the order detail loading state is rendered | `src/features/orders/order-detail-view.tsx` | render nothing while loading | 1 failed / 21 total | *OrderDetailView — R55 honest waiting state shows the loading state while the first request is UNRESOLVED* › `Error: Unable to find an element by: [data-testid="order-detail-loading"]` | identical |
| A30b | the stock loading state is rendered | `src/features/stock/stock-view.tsx` | render nothing while loading | 1 failed / 9 total | *StockView — the stock table shows the loading state while the request is UNRESOLVED, then the empty state* › `Error: Unable to find an element by: [data-testid="stock-loading"]` | identical |
| A30c | the invoices loading state is rendered | `src/features/billing/billing-view.tsx` | render nothing while loading | 1 failed / 15 total | *BillingView — invoices and credit limits shows each table's loading state while its request is UNRESOLVED, then its empty state* › `Error: Unable to find an element by: [data-testid="invoices-loading"]` | identical |
| A32 | the backstop re-reads a live order (D2) | `src/hooks/use-order-detail.ts` | no backstop | 1 failed / 21 total | *OrderDetailView — the backstop re-read (#7 D2/D7) D2 — a fact whose frames are lost entirely still reaches the page, by re-reading while the order is live* › `Error: expect(element).toHaveTextContent()` | identical |
| A33 | ONE catch-up read after a watched terminal transition (D7) | `src/hooks/use-order-detail.ts` | never catch up | 1 failed / 21 total | *OrderDetailView — the backstop re-read (#7 D2/D7) D7 — after a terminal transition this page watched, ONE more read lands the lost sibling entry, then reading stops* › `Error: Unable to find an element with the text: Credit released. This could be because the text is broken up by multiple elements. In this case, you can provide a function for your text matcher to make your matcher more flexible.` | identical |
| A34 | a causal link resolves only within this timeline | `src/lib/order-detail.ts` | assume the cause exists | 2 failed / 33 total | *causingEntry — causal links resolve only within this order's timeline resolves a causationId naming an entry here, and nothing for an absent or unresolvable one* › `AssertionError: expected { eventId: 'req-command-id', …(3) } to be undefined` | identical |
| A35 | a redelivered timeline entry is not appended twice (reducer layer) | `src/lib/order-detail.ts` | drop the reducer check | 1 failed / 12 total | *R51 — applyTimelineAppended appends once, in timeline order a redelivered entry (eventId already present) returns the SAME document, unchanged* › `AssertionError: a redelivered entry must not be appended again: expected [ 'e0', 'e0' ] to deeply equal [ 'e0' ]` | identical |
| A36 | replenish sends a DELTA, not a target | `src/features/stock/stock-view.tsx` | send the resulting level | 1 failed / 9 total | *StockView — replenish is a delta sends the typed amount as units to ADD (never the resulting level), and says so on the form* › `AssertionError: expected [ { companyCode: 'IBERFOODS', …(1) } ] to deeply equal [ { companyCode: 'IBERFOODS', …(1) } ]` | identical |
| A37 | the retailer drives the currency | `src/features/orders/place-order-form.tsx` | ignore the retailer's currency | 2 failed / 16 total | *PlaceOrderForm the currency follows the selected retailer (a GBP retailer makes the order GBP)* › `Error: expect(element).toHaveValue(GBP)` | identical |
| A38 | selects cannot overflow their grid cell (trap 9) | `src/components/ui/native-select.tsx` | drop the wrapper width | 1 failed / 16 total | *PlaceOrderForm a long option cannot widen its select past its grid cell (the overlap #7 found in a real browser)* › `Error: expect(element).toHaveClass("w-full min-w-0")` | identical |
| A39 | the Gateway is found through GATEWAY_PORT, not a sibling *_PORT (substitution) | `src/server/config.ts` | read ORDERS_HEALTH_PORT | 1 failed / 4 total | *server configuration reaches the Gateway on GATEWAY_PORT — and not on a sibling *_PORT (substitution)* › `AssertionError: expected 'http://localhost:4666' to be 'http://localhost:4555' // Object.is equality` | identical |
| A42 | the stream is reopened only when the ORDER changes, never because the factory is a new function (production minifier finding) | `src/hooks/use-order-stream.ts` | depend on the factory identity again | 1 failed / 7 total | *useOrderStream — the SSE hook, driven by a fake EventSource a factory that is a NEW function on every render still opens ONE stream (the minified production build creates one per render)* › `AssertionError: EventSources opened across 6 renders: expected [ …(6) ] to deeply equal [ Array(1) ]` | identical |
| A43 | a payment outcome survives the paid invoice leaving the list (browser finding) | `src/features/billing/billing-view.tsx` | show the form only while its invoice is in the current page of the list | 1 failed / 15 total | *BillingView — Register payment (R47/R48) the outcome stays on screen when the paid invoice drops out of an "issued" list on the next read* › `TestingLibraryElementError: Unable to find an element by: [data-testid="payment-outcome-accepted"]` | identical |

---

## Fix round 1 — review B1 (the catalogue notice), and the population that let it through

**Brief:** fix round 1 for id 29 after `progress/review_web_app.md` REJECTED it on B1. Scope was `apps/web/**` plus this record. No change to `feature_list.json` (the leader holds it), `src/`, `specs/shared/`, `scripts/dev-stack.sh`, `init.sh` or `.env.example`. **Wall-clock**, from the filesystem: first artefact (the backup of `place-order-form.tsx`) at 19:26:39, fixtures captured 19:27:36–19:27:43, `QUALITY_ONLY=web ./quality.sh` finished about 19:41, record written about 19:43. That is about 20 minutes, plus the reading done before the first artefact.

### FR1.1 Captured fixtures (real Gateway, `WEB_PORT=3010 scripts/dev-stack.sh start`)

**Who answers the catalogue:** it was determined from source, not assumed. The Gateway's `GET /catalog/{kind}` (`src/Gateway/Presentation/Endpoints/CatalogEndpoints.cs`) dispatches `ListCatalogQuery` to the NATS subject `catalog.reference.list` (`src/Gateway/Application/Rpc/GatewaySubjects.cs:15`). The only responder for that subject is **Orders**: `src/Orders/Presentation/OrdersCreateResponder.cs:149` `HandleCatalogReferenceListAsync`. So the capture ran with `scripts/dev-stack.sh stop-service Orders`.

`scripts/capture-gateway-responses.mjs` gained three things:
- an `--orders-down` mode;
- an `--only=a,b` filter, so a re-run does not rewrite the twelve existing fixtures (their mtimes are unchanged: 16:53/16:57);
- one new capture in the normal set.

| Fixture | `capturedFrom` | Status | `detail` |
|---|---|---|---|
| `catalog-retailers-upstream-unavailable-503` | `GET /catalog/retailers` | 503 | `RPC call to "catalog.reference.list" failed: no responder is subscribed to this subject.` |
| `catalog-companies-upstream-unavailable-503` | `GET /catalog/companies` | 503 | same |
| `catalog-products-upstream-unavailable-503` | `GET /catalog/products` | 503 | same |
| `orders-list-bad-page-400` | `GET /orders?page=0` | 400 | `page: "0" is not a valid page (expected an integer between 1 and 2147483647)` |

The fourth fixture exists because of the sibling enumeration (FR1.4). The orders **list** test was serving a fixture captured from `GET /orders/not-a-uuid`.
- `GET /orders` is served by the Gateway itself from Mongo (`OrdersEndpoints.cs:20` → `MongoOrderReadModel`), so there is no responder to stop.
- The list route's reachable refusal is its own page validation (`RequestParsing.ParsePageParams`).

All four fixtures are registered in `src/test/gateway-fixtures.ts`. They are also rows in `error-text-chain.test.ts`'s table: the directory now has 16 files and the table 16 rows, and the floor moved from 12 to 16. So each new fixture also goes through the real route handler: `app/api/catalog/[kind]/route.ts` and `app/api/orders/route.ts` GET.

### FR1.2 The site fix — `src/features/orders/place-order-form.tsx`

- `catalogErrors`: each failing catalogue query's error, de-duplicated by the text `describeError` gives it. Three identical 503s read once; two different failures are both shown.
- The notice (`data-testid="catalog-unavailable"`) now renders one `<ErrorMessage testId="catalog-error">` per distinct error. That is the same `describeError` path (`detail`, then `title`, then fallback) every other page uses.
- The fallback, `The catalogue could not be loaded.`, is used only when the body carries no problem document.
- The type-by-hand guidance is kept, as a separate `catalog-manual-entry` line.

**Live check** (stack up on `WEB_PORT=3010`, Orders stopped). Headless Chrome was driven over CDP with a real session cookie from `POST /api/auth/login` and navigated to `/orders/place`. It read this from the page:

```
{"errors":["RPC call to \"catalog.reference.list\" failed: no responder is subscribed to this subject."],
 "all":"RPC call to \"catalog.reference.list\" failed: no responder is subscribed to this subject.Enter codes by hand meanwhile (e.g. retailer CarrefourEs, company IBERFOODS, product PRD-0001).",
 "url":"http://localhost:3010/orders/place"}
```

The stack was then stopped (`dev-stack.sh stop` reported `nothing left running, no port this stack bound is still held`) before any test or build.

### FR1.3 The tests

- **`place-order-form.test.tsx`**:
  - The case *"when the catalogue cannot be loaded, the page shows the Gateway's own words (real captured /catalog/\* 503s) and the codes can still be typed by hand"* replaces the *"Any real 503 body will do"* case.
    - It serves the three **catalogue** fixtures on their own routes.
    - It asserts that the notice's `catalog-error` texts equal `[fixtureDetail('catalog-products-upstream-unavailable-503')]`, with a message.
    - It asserts that the three captured details are equal, which is why one line is expected.
    - It still types codes by hand and places the order.
  - New case: *"two DIFFERENT catalogue failures are both shown: the real 503's own words, and the fallback only for a body that carries no problem"*. It pairs the real retailers 503 with an HTML 502 on products and asserts `[detail, 'The catalogue could not be loaded.']`.
    - A first draft used a non-catalogue fixture for the second failure and asserted its text. That is the very sibling shape this round forbids, so it was replaced before any run.
- **`orders-list.test.tsx`**: the list-failure case now serves and asserts `orders-list-bad-page-400`, which was captured from `GET /orders`. Its title names the route.
- **`login-form.test.tsx`**: new case *"a no-JavaScript sign-in the Gateway refuses comes back to the form showing that refusal's own words (route → ?error= → page)"*.
  - A form-encoded POST goes to the real login route, against a FakeGateway that answers with the captured 401.
  - The case reads the 303's `?error=` and renders `<LoginForm initialError=…>`.
  - The text must equal `fixtureDetail('login-bad-credentials-401')`.
  - Before this, the `initialError` site was proven only in two halves: a route test compared against a literal, and a component test fed a literal.

### FR1.4 The population guard — `src/lib/error-rendering-sites.test.ts` + `src/test/error-sites.ts`

**The defect it closes:** `error-text-chain.test.ts`'s population is the fixture directory, so a site with no fixture cannot enter it. The guard's population is now the **rendering sites**.
- **Expected set:** a literal list, `SITES`, of 65 keys.
- **Actual set:** derived from the source by content.
- **Both directions are asserted:** `derived − listed = ∅` names any new site, and `listed − derived = ∅` names stale entries. A floor also requires derived ≥ listed.

**How sites are derived** (`deriveErrorSites`):
- **Instrument.** It uses the TypeScript parser (`typescript` 5.9.3, already a devDependency) over every `.ts`/`.tsx` under `src/`. It excludes **by path**: `generated/`, `test/` and `*.test.*`. That is 59 files.
- **What identifies a site in this codebase.** Every failure reaches a component in one of three ways: as a TanStack `isError`/`error` state, as the no-JS `initialError` prop, or as the stream's `gave-up` status. Local validation is kept in `…Problem` state. So an expression is error-bearing when its text matches `/error|fail|gave-up|problem/i` **after expansion**, which works like this:
  - through same-file declarations (initialisers, and destructured property names), transitively;
  - through a `useState` value's **setter calls** (their arguments, plus the enclosing `if` conditions, `catch`, and the handler name such as `onError`, up to the handler boundary);
  - **one level** into top-level functions/consts of any scanned file, comment-stripped with the TS printer. Deeper expansion would reach `apiGet → ApiError` from every query and mark every data branch.
- **Five shapes, each with a key.**
  - `<ErrorMessage>` uses: `file | <ErrorMessage> | testId`.
  - Conditionals whose **branch contains** JSX: `file | cond ? then/else | first testid`. The first draft required the branch to *be* JSX, which missed `result && !registerPayment.isError ? (a ? <p/> : <p/>)`; found from the printed population, fixed before arming.
  - `cond && <jsx>`.
  - JSX text children with no JSX inside: `file | {expr} | enclosing testid`.
  - Elements **styled as an error** (`role="alert"` or a literal `destructive` class), whatever their condition is named.
- **Why so broad.** A false positive costs one reasoned line in `SITES`; a false negative is a site nobody reviews.

**What each literal entry must carry:**
- **A `FixtureProof`** names a test file, a case title, the asserted test id, the site's Gateway **route**, and its fixtures. Each proof (12 distinct) is checked from the case's own AST, so comments cannot satisfy it:
  - the case exists;
  - it calls `fixtureDetail(…)`;
  - it contains the test id as a string literal;
  - it contains each fixture name (an `it.each` table counts);
  - each fixture's `capturedFrom` matches the site's route (`sameRoute`: `{…}`/`*` match one segment; the query string is ignored; the method is split off at the first space only, because `{issued invoice}` contains a space);
  - every literal `fixtureDetail` argument in the case is one of the site's own fixtures.
- **A `NotAGatewayError`** carries a reason. The classes are:
  - client-side validation;
  - the non-error branch (loading, rows, pager);
  - select-vs-input choice;
  - the SSE connection state (an EventSource exposes no body);
  - `ErrorMessage` itself;
  - the login button label, which is derived only because the mutation calls `apiRequest`, whose body throws `ApiError`. An earlier reason I wrote claimed an error handler; I checked, found none, and corrected it.

**The population, as derived now (65 keys):**
- 26 are Gateway-error sites, mapped to 12 cases.
- 39 are classified not-a-Gateway-error.
- Both figures were counted from the `SITES` literal with a script, not by eye.
- In the review's numbering, sites 1–11 are all here, B1 (site 6) now as `<ErrorMessage> | catalog-error` + `catalogFailed ? then | catalog-unavailable`, and site 12 as `STREAM`.

Every key and its classification is the `SITES` literal in `src/lib/error-rendering-sites.test.ts`.

### FR1.5 Sibling enumeration — every fixture use against the route it stubs

Command (in `apps/web`, excluding by path):

```
find src tests-integration -type f \( -name '*.ts' -o -name '*.tsx' \) -not -path '*/node_modules/*' -print0 | xargs -0 grep -nE "fixtureResponse\(|fixtureDetail\(|gatewayFixture\(|relayFixture\("
```

**Before the fix**, this command returned the list in the review. The mismatched hits were:
- `place-order-form.test.tsx:225-227`: `GET /api/catalog/*` served `credits-list-upstream-unavailable-503` (captured `GET /credits`). No text was asserted there (B1), but a `FixtureProof` would now reject it. **Fixed** (FR1.3).
- `orders-list.test.tsx:58,61`: `GET /api/orders` served `order-malformed-id-400` (captured `GET /orders/not-a-uuid`), **and the case asserted its text**. **Fixed** with a fixture captured from the list route (FR1.1/FR1.3).

**After the fix**, the output has 56 lines (counted with `| wc -l`).
- **17 are definitions and helper plumbing:**
  - `src/test/gateway-fixtures.ts:39,44,45,51,52`;
  - `src/test/error-sites.ts:285,355,371,372` (comments);
  - `error-rendering-sites.test.ts:14,180,182,193`;
  - `error-text-chain.test.ts:55`;
  - `route-handlers.test.ts:32,34,193`.
- **The other 39 are serving or asserting uses**, covered by the rows below. A row may cover several lines: the `.on(...)` handler lines `place-order-form.test.tsx:265`, `login-form.test.tsx:68` and `billing-view.test.tsx:278` fall under the row for their `.on` call.

One row per serving or asserting use:

| Hit | Route stubbed | Fixture (`capturedFrom`) | Verdict |
|---|---|---|---|
| `error-text-chain.test.ts:84` | each `CASES` row's `gatewayPath` | 16 rows, each paired literally | match: every row's `gatewayPath` equals or wildcards its `capturedFrom` path; `orders-list-bad-page-400` ↔ `/orders`, query ignored |
| `login-form.test.tsx:85` | `POST /auth/login` (`.on`, `:67`) | `login-bad-credentials-401` (`POST /auth/login`) | match, text asserted |
| `login-form.test.tsx:95` | same FakeGateway | same | match, text asserted (new case) |
| `route-handlers.test.ts:86,91` | `POST /auth/login` | `login-bad-credentials-401` | match, relay bytes |
| `route-handlers.test.ts:105` | `POST /auth/login` | same | match, `?error=` asserted |
| `route-handlers.test.ts:194` (`it.each`, `:185-190`) | 6 literal pairs | `order-malformed-id-400` ↔ `/orders/not-a-uuid`; `order-unknown-404` ↔ `/orders/0d0e…`; `place-order-no-lines-400`, `place-order-stock-unavailable-409` ↔ `POST /orders`; `replenish-unknown-product-404` ↔ `POST /stock/replenish`; `payment-amount-mismatch-422` ↔ `POST /invoices/inv-1/payments` | all 6 match (classified by hand: non-literal call) |
| `route-handlers.test.ts:202,205` | `GET /stock` | `orders-without-token-401` (`GET /orders`) | **MISMATCH, allowed**: the Gateway's token check answers before routing, so the 401 is route-independent; the case asserts byte relay and cookie clearing, not words about `/stock`. Listed in `MISMATCHED_FIXTURE_USES` with that reason |
| `stream-route.test.ts:34,141` | raw `createServer` answering `/orders/stream` | `orders-without-token-401` (`GET /orders`) | **mismatch, allowed**, same reason: relay bytes only. Not mechanised (the route is not a literal), so classified here |
| `place-order-form.test.tsx:214,218` | `POST /api/orders` | `place-order-upstream-unavailable-503` (`POST /orders`) | match, text asserted |
| `place-order-form.test.tsx:224-234` | `GET /api/catalog/{retailers,companies,products}` | the three catalogue fixtures | match, text asserted (**fixed**) |
| `place-order-form.test.tsx:249,256` | `GET /api/catalog/retailers` | `catalog-retailers-…` | match, text asserted (new case) |
| `place-order-form.test.tsx:290` | `POST /orders` (`.on`, `:264`) | `place-order-stock-unavailable-409` | match, text asserted |
| `order-detail-view.test.tsx:103,106` | `[DETAIL_PATH]` = `GET /api/orders/${ORDER_ID}` | `order-unknown-404`, `order-malformed-id-400` (`GET /orders/{…}`) | match by segment; covered by the `DETAIL` proof, not by the literal-use scan (non-literal fixture argument) |
| `orders-list.test.tsx:58,61` | `GET /api/orders` | `orders-list-bad-page-400` (`GET /orders?page=0`) | match, text asserted (**fixed**) |
| `billing-view.test.tsx:89,91` | `GET /api/invoices` | `invoices-list-…` (`GET /invoices`) | match, text asserted |
| `billing-view.test.tsx:97,99` | `GET /api/credits` | `credits-list-…` (`GET /credits`) | match, text asserted |
| `billing-view.test.tsx:256,261` | `POST /api/invoices/inv-1/payments` | `payment-amount-mismatch-422` (`POST /invoices/{issued invoice}/payments`) | match, text asserted |
| `billing-view.test.tsx:278` | `POST /invoices/inv-1/payments` (`.on`) | same | match |
| `stock-view.test.tsx:42,45` | `GET /api/stock` | `stock-list-…` (`GET /stock`) | match, text asserted |
| `stock-view.test.tsx:119,126` | `POST /api/stock/replenish` | `replenish-unknown-product-404` | match, text asserted |

**The check is also mechanised** in the guard's last case (`fixtureUses`). It reads object-literal route-table entries (including a computed key resolved to a same-file template) and `x.on('M', '/path', …)` calls that serve a **literal** fixture name. It found 17 uses, exactly one mismatched (the reasoned `/stock` 401), and asserts that the mismatch set equals `MISMATCHED_FIXTURE_USES`. Non-literal pairings (the `it.each` tables, `createServer`, `error-text-chain`'s table) are the hand-classified rows above.

### FR1.6 Arming table (final code; the last change to either guard file came before these runs)

**Procedure** (driver script in the session scratchpad), for each arm:
1. `cp` a backup and mutate by exact-anchor replace.
2. Run the named test files.
3. Restore with `shutil.copyfile`, then `os.utime` (touch).
4. Confirm with `filecmp.cmp(shallow=False)`.

**Runs:** all 12 arms ran after the last guard edit (a type-only fix to `attributeValue` and a non-null assertion).
- A8's first re-run reported an anchor miss, because I had since added a message to the asserted line, so the file was **not** mutated. Its anchor was then fixed and A8 re-ran.
- A1, A7, A11 and A12 were re-run once more after the assertion messages were added. The messages below are from those runs.

**Cleanup:** after the arms, `grep -rn "useStockBroken\|stock-generic\|stock-note\|setNote" src` shows only the doc comment in `error-sites.ts:30`. The full suite is 211/211 green.

| # | Mutation | Files run | Result | Verbatim failure (names the claim) |
|---|---|---|---|---|
| A1 | **B1 restored**: the notice renders the old fixed sentence (as `catalog-error`) | place-order + guard | 4 failed / 33 | `- "RPC call to \"catalog.reference.list\" failed: no responder is subscribed to this subject."` / `+ "The catalogue could not be loaded — enter codes by hand (e.g. retailer CarrefourEs, company IBERFOODS, product PRD-0001)."` (the catalogue case, message *the catalogue notice must show the Gateway's own detail*); guard: `SITES entries the source no longer has — the list is stale` → `features/orders/place-order-form.tsx \| <ErrorMessage> \| catalog-error`; `the scanner derived FEWER sites than the reviewed list holds — it has lost sites: expected 64 to be greater than or equal to 65` |
| A2 | new site `{stock.isError ? <p data-testid="stock-generic">Something went wrong</p> : null}` | guard | 1 failed / 16 | `error-rendering site(s) with no entry in SITES — add a fixture-backed case, or classify it` → `features/stock/stock-view.tsx \| stock.isError ? then \| stock-generic` |
| A3 | the same site with **no test id** (attack 7) | guard | 1 failed / 16 | … → `features/stock/stock-view.tsx \| stock.isError ? then \| <no testid>` |
| A4 | error **laundered into state** behind `if (stock.isError …) setNote('Stock is unavailable')`, rendered as `note ? <p>{note}</p>` (no error styling) | guard | 1 failed / 16 | … → `stock-view.tsx \| note ? then \| stock-note`, `stock-view.tsx \| {note} \| stock-note` |
| A5 | laundered through `mutate(…, { onError: () => setNote('Could not add units') })` | guard | 1 failed / 16 | same two keys |
| A6 | error state **renamed inside a hook in another file** (`useStockBroken` in `hooks/use-stock.ts` returns `useStock(f).isError`; view renders `broken ? <p>…</p>`) | guard | 1 failed / 16 | … → `features/stock/stock-view.tsx \| broken ? then \| stock-broken` |
| A7 | **substitution**: the catalogue case serves `credits-list-upstream-unavailable-503` again (the original B1 test's fixture) | place-order + guard | 2 failed / 33 | `fixture(s) served for a route they were NOT captured from (file \| route \| fixture)` → `features/orders/place-order-form.test.tsx \| GET /catalog/{companies,products,retailers} \| credits-list-upstream-unavailable-503`; component: `- "RPC call to \"catalog.reference.list\" …"` / `+ "RPC call to \"billing.credit.list\" …"` |
| A8 | the case asserts only `toBeInTheDocument()` (the original B1 assertion), with a **comment** carrying `fixtureDetail('catalog-…')` and `'catalog-error'` (attack 4) | place-order + guard | 1 failed / 33 (the component case passes, which is the point) | proof case *…when the catalogue cannot be loaded…: calls fixtureDetail, asserts on its test id…* → `- "callsFixtureDetail": true, + false` / `- "namesTestId": true, + false` |
| A9 | orders-list case back on `order-malformed-id-400` (text consistent, so the component case passes) | orders-list + guard | 2 failed / 21 | proof: `"fixture": "orders-list-bad-page-400", - "inCase": true, + false`; sibling: → `features/orders/orders-list.test.tsx \| GET /orders \| order-malformed-id-400` |
| A10 | a listed entry deleted (`catalogFailed ? then \| catalog-unavailable`) | guard | 1 failed / 16 | `error-rendering site(s) with no entry in SITES…` → `features/orders/place-order-form.tsx \| catalogFailed ? then \| catalog-unavailable` |
| A11 | derivation returns `[]` (attack 8: literal against literal) | guard | 2 failed / 16 | `the scanner derived FEWER sites than the reviewed list holds — it has lost sites: expected 0 to be greater than or equal to 65`; stale list of all 65 |
| A12 | same-file expansion disabled | guard | 2 failed / 16 | `… expected 56 to be greater than or equal to 65`; stale: `login-form.tsx \| {login.isPending …}`, `order-detail-view.tsx \| {CONNECTION_LABEL[connection]}`, six `*Usable ? then/else`, `shortages ? then \| place-order-shortages`. Note: `catalogFailed ? then` is **not** among them, because the name itself matches `/fail/i`; the arm is about expansion in general, not about B1's name |

### FR1.7 Defeat list (CLAUDE.md's ten) against this round's guards

| # | Attack | Verdict |
|---|---|---|
| 1 | Delete the behaviour | A1 (site), A10, A11 |
| 2 | Corrupt a payload field the test supplied | A1 and A7: the rendered text differs from the fixture's `detail`, and the component case names both |
| 3 | Substitute a valid sibling identifier | A7 (a sibling fixture on the catalogue routes), A9 (a sibling orders fixture on the list route) |
| 4 | Shadow from a comment/string | A8; structurally, the guard reads AST nodes, and a comment is not a node |
| 5 | Dead region | Not applicable as conditional compilation: TypeScript has no preprocessor. Code under `if (false)` is still parsed and derived, so the failure direction is over-inclusion (a listed false positive), never a hidden site |
| 6 | Raw/verbatim string | Not applicable: the parser classifies template and string literals; text that *reads* like JSX inside a string is not rendered, so it is correctly not a site |
| 7 | Drop an optional element | A3 (no test id); a proof whose case lacks the test id fails `namesTestId` (A8) |
| 8 | Literal compared to literal | A11: the derivation is asserted non-empty and ≥ the list, in both subtraction directions |
| 9 | Closer half satisfied, premise stale | The `listed − derived` direction (A1, A11). Each proof re-reads the case body on every run rather than trusting the title |
| 10 | Build output joins the population | Not applicable: the scan walks `src/` only, and `find src -type f -not -name '*.ts' -not -name '*.tsx' -not -name '*.json' -not -name '*.css'` returns nothing (`.next/`, `coverage/` and `node_modules/` are outside `src/`) |

**Proposed new rows for the leader** (I may not edit `CLAUDE.md`), each paid for in this round:
- (a) **Launder the failure through state:** a setter call under an error condition, or in an `onError` handler (A4, A5).
- (b) **Rename the failure in another file:** a hook that returns `isError` under another name (A6).
- (c) **A branch that is itself a conditional:** the first draft of the deriver missed it (FR1.4).

### FR1.8 Residual limits (stated, not closed)

- **Deep hook chains.** Cross-file expansion is **one level**. A hook that returns another hook's renamed error state two levels down (`useA → useB → {broken: q.isError}`) would escape. Deeper expansion marks every data branch through `apiGet → ApiError`.
- **Props renamed across components.** A parent passing `show={q.isError}` to a child that renders `show ? <p>generic</p> : null` would escape: the child's condition text has no error word, and props are not followed across components. No such component exists today; every error is rendered in the component that owns the query.
- **Non-JSX rendering** (`alert()`, `document.title`, a toast library) is not scanned. None exists in `src/` today.
- **Name-based expansion** can over-include when a local name shadows another. That only adds listed false positives.

### FR1.9 Files touched this round

- **Source:** `apps/web/src/features/orders/place-order-form.tsx`.
- **Tests:**
  - `apps/web/src/features/orders/place-order-form.test.tsx`
  - `apps/web/src/features/orders/orders-list.test.tsx`
  - `apps/web/src/features/auth/login-form.test.tsx`
  - `apps/web/src/lib/error-text-chain.test.ts`
  - `apps/web/src/lib/error-rendering-sites.test.ts` (**new**)
- **Test support:** `apps/web/src/test/gateway-fixtures.ts`, `apps/web/src/test/error-sites.ts` (**new**).
- **Fixtures (new):** `apps/web/src/test/fixtures/gateway/{catalog-retailers,catalog-companies,catalog-products}-upstream-unavailable-503.json`, `orders-list-bad-page-400.json`.
- **Script:** `apps/web/scripts/capture-gateway-responses.mjs` (`--orders-down`, `--only=`, `orders-list-bad-page-400`).
- **Record:** this section.

### FR1.10 Counts and gates (read off runs in this session, stack stopped)

- **`pnpm exec vitest run`:** **17 files, 211 tests, 211 passed**. The last count was 16 files, 189 tests, so the delta is +1 file and +22 tests, which reconciles exactly:
  - new guard file: +16 (3 fixed cases + 12 proof cases + 1 sibling case);
  - `error-text-chain`: +4 (one per new fixture);
  - place-order: +1 (two different failures; the rewritten catalogue case replaces the old one);
  - login: +1 (no-JS chain).
  - Total: 16 + 4 + 1 + 1 = 22.
- **Other web gates:** `pnpm run lint` exit 0 (`lint-coverage OK — all 93 source files (84 under src/)`); `pnpm run typecheck` exit 0 (it first failed on two type errors in `error-sites.ts`, fixed before the final arms); `pnpm run types:check` exit 0.
- **`QUALITY_ONLY=web ./quality.sh`:** `quality.sh finished` [OK], with the stack stopped and `QUALITY_ONLY=web` (so .NET sections 1–4 were skipped, as the brief allows). Results: install, OpenAPI types, lint, typecheck, Vitest 211/211 with coverage (statements 91.61%, lines 92.74%), production build, and integration 7/7 against the build.
- **`./init.sh`:** exit 0.
- **Not done:** the full .NET `./quality.sh` — this round touched no .NET code. `specs/shared/test-matrix.md` is untouched (read-only here; the review's §6 routes its edits to id 29's close).

## Fix round 2 — the error-text guard replaced by a behavioural sweep

**Brief:** fix round 2 for id 29 after re-review round 2 (`review_web_app.md` §RR2) defeated fix round 1's syntax guard twice (D1: statement-level and file-convention rendering; D2: a proof case satisfied by incidental lines). The coordinator decided to replace the instrument rather than extend it (CLAUDE.md defeat list row 11). Scope: `apps/web/**` and this record. No change to `feature_list.json`, `CLAUDE.md`, `specs/shared/`, `src/`, `scripts/`, `init.sh` or `.env.example`. **Wall-clock**, from the filesystem and run output: the first session was cut off by a rate limit while reading, before any edit. In the resumed session: first sweep run 21:46:12, arms 21:49–22:05, full `./quality.sh` started 22:06. That is about 30 minutes of implementation, plus the reading before it.

### FR2.1 What was built

- **`src/app/error-text-sweep.test.tsx`** (new, 52 tests): the behavioural sweep.
- **`src/test/page-sweep.tsx`** (new): the harness. It holds the route-file population, the layout-chain renderer, the request recorder, the settle loop, the sentinels and the success bodies.
- **`src/test/request-sites.ts`** (new): two structural premises, read with the TypeScript parser. It finds request primitives outside the api-client, and every mutating `apiRequest` call.
- **`src/test/fixture-uses.ts`** + **`src/lib/fixture-provenance.test.ts`** (new, 1 test): the part of round 1's guard that is kept (FR2.6).
- **Deleted:** `src/lib/error-rendering-sites.test.ts` and `src/test/error-sites.ts`.
- **Product changes:**
  - `features/shell/logout-button.tsx`: the logout request now goes through `apiRequest` instead of a direct `fetch`. A failure is caught, and signing out proceeds exactly as before (the old `fetch` did not throw on an HTTP error either).
  - `features/orders/orders-list.tsx` and `features/billing/billing-view.tsx`: **three failures that were silently swallowed are now shown in their own words.** The sweep found all three on its first run (FR2.5).
- **`src/test/setup.ts`:** the `next/navigation` mock gains `redirect`/`notFound`, which throw with Next's digest shape. The sweep reads that digest, so `src/app/page.tsx` (a redirect) is swept rather than listed.
- **Record:** the §4 ledger row L2 no longer says *"no committed default secret"*. That was the re-review's advisory on L3.

### FR2.2 Design

**1. The population is the filesystem, read twice.**
- `import.meta.glob('/src/app/**/{page,layout,template,error,global-error,not-found,global-not-found,forbidden,unauthorized,loading,default}.{tsx,ts,jsx,js,mdx}')` gives loadable modules.
- A `readdirSync` walk of `src/app` gives the same set by file name.
- The test asserts that the two sets are equal, in both directions.
- The kinds are handled as follows:
  - **page-like** (`page`, `not-found`, `loading`, …): rendered and swept.
  - **`layout`:** rendered as part of every page's chain.
  - **`error` / `global-error`:** rendered with a failure.
  - **Anything else** (today only `template` could appear) fails unless it is in `NOT_RENDERABLE`, which is an empty literal.
- A test also fails if `pages/`, `src/pages/` or an `app/` beside `src/` exists, because that would be a router the walk does not see.

**2. The real page is rendered, not a mapping to its root.**
- The harness renders the page's own default export. A server component is `await`ed as a function; a `'use client'` file is rendered as an element.
- The page is wrapped, from the inside out, in each enclosing segment's `error` boundary and then its `layout`, as Next.js does (an `error` file does not wrap its own segment's layout).
- The whole tree is rendered inside the real `Providers` from `app/providers.tsx`: the app's own QueryClient, with its 5xx retry and its 401 redirect.
- Rendering the page itself removes the "page → root mapping" premise; the file under test is the page.
- The root layout is not rendered, because it is `<html><body>`. Instead, a test checks its React element tree: it is exactly `html > body > Providers > children`, so the harness sees what a browser sees.
- Dynamic segments take values from a literal `PARAM_VALUES`. A segment with no value fails by name.

**3. Requests are observed.**
- `setApiFetch` installs a recorder. It keys each request as `METHOD /path?sorted-query`, separately for the load phase and the action phase, and answers from a table of realistic success bodies. The shapes are the component tests' factories.
- A request with no success body is reported as `unrouted` and fails the baseline.
- In the same run, `fetch`, `XMLHttpRequest`, `WebSocket` and `navigator.sendBeacon` are replaced by recorders. **Any request that bypasses the seam is recorded and fails the test**, even when it is too obfuscated for the static scan (arm P3b).
- `EventSource` is replaced by `FakeEventSource`, and the URLs it opens are recorded.
- The native `form[action]` posts on screen are recorded as well.

**4. Each request is failed in turn, in two variants.**
- **`detail` variant:** HTTP **503**, which the app retries once, so this variant also exercises the retry path. `detail` is the sentinel, and `title` is a **decoy** (`decoy-title-<nonce>`), so a page that shows the title while a detail exists fails (arm P8).
- **`title` variant:** HTTP **422**, not retried. `detail` is **absent** and `title` is the sentinel (defeat-list attack 7).
- Both bodies are a real captured problem document (`stock-list-upstream-unavailable-503`) with those fields replaced, so the keys match what the Gateway sends.
- The assertion is `document.body.textContent` **contains the sentinel**.
- A failure line reads `route | load-or-action | request | variant | expected the problem <variant> "<sentinel>" on screen | shown instead: <text the failing run shows that its baseline did not>`.

**5. Actions.**
- `ACTIONS` is one table with five scripts:
  - `/login` sign-in;
  - `/orders/place` place-order;
  - `/stock` replenish;
  - `/billing` register-payment;
  - `/orders` sign-out (the logout button lives in the `(app)` layout, which is rendered).
- Every request an action causes is failed in turn in the action phase, exactly like a load request. That covers the POST, its invalidation refetches, and the by-reference lookup after a payment.

**6. Settling.**
- A run waits until five consecutive 20 ms ticks, each inside `act`, find all three of these at zero: the recorder's in-flight count, `QueryClient.isFetching()`, and `isMutating()`. The limit is 20 s.
- `isFetching()` stays non-zero while a retry delay is pending. That is why the 503 variant is asserted *after* the retry, which a dedicated premise test checks (it must take ≥ 900 ms and show the sentinel).

**7. Literal, reviewed lists.** Each one is checked in both directions.
- `EXPECTED_LOAD` and `EXPECTED_ACTION`: these check the **observer**. The population itself comes from observation.
- `PRIMITIVE_EXCEPTIONS`, with exact counts.
- `STREAMS`.
- `NOT_SHOWN`: each entry carries a `verify` that proves its reason, and a listed entry whose sentinel *is* shown fails as stale.
- `NATIVE_POSTS`.
- `NOT_RENDERABLE`: empty.

### FR2.3 Populations and how each is derived

| Population | Derived by | Today |
|---|---|---|
| Route files | the glob, checked against a disk walk | 9: `layout.tsx`, `page.tsx`, `login/page.tsx`, `(app)/layout.tsx`, `(app)/{billing,orders,orders/place,orders/[id],stock}/page.tsx` |
| Pages swept | the route files of a page-like kind | 7: `/` (a redirect to `/orders`, so it renders nothing and makes no request), `/login`, `/orders`, `/orders/place`, `/orders/[id]`, `/stock`, `/billing` |
| Load requests | **observed** | `/orders` 2, `/orders/place` 3, `/orders/[id]` 1, `/stock` 1, `/billing` 3. **10 in total, each swept in 2 variants** |
| Action requests | **observed** after each script | sign-in 1, place-order 1, replenish 2, register-payment 4, sign-out 1. **9 in total, 2 variants each** |
| Mutating calls in source | `mutatingCalls()`. It finds every `apiRequest` call whose method is not GET, however `apiRequest` is bound (declaration, renamed import, namespace import). Any other reference to the name (an alias, or the function passed along) and any options it cannot read (a spread, a non-literal method, a non-object) is **unresolved, which is a failure** | 5: `use-billing.ts` `POST /api/invoices/{}/payments`, `use-orders.ts` `POST /api/orders`, `use-stock.ts` `POST /api/stock/replenish`, `login-form.tsx` `POST /api/auth/login`, `logout-button.tsx` `POST /api/auth/logout`. Each is matched by a request some scripted action makes |
| Request primitives outside `lib/api-client.ts` | `requestPrimitives()`, run on the TypeScript AST of browser code (all of `src/` except `generated/`, `test/`, `server/` and `*.test.*`, excluded by path). It flags: an identifier `fetch`/`EventSource`/`XMLHttpRequest`/`WebSocket`/`sendBeacon`/`axios`/`ky`/`ofetch`; the same names as a string element-access key; an import of a request-client module; a JSX `action`/`formAction` | 3, all listed: `hooks/use-order-stream.ts \| EventSource` (1), and the two `form action`s (login, logout) |
| Native form posts | **observed** as `form[action]` in each page's settled DOM | `POST /api/auth/login` (`/login`), `POST /api/auth/logout` (every `(app)` page) |
| Streams | **observed** as `EventSource` opens | `/orders/[id] \| /api/orders/stream` |

**Deviations from the brief, each deliberate:**
- `PRIMITIVE_EXCEPTIONS` has **three** entries, not the one the brief expected. I made a JSX `action=` attribute a request primitive, because a native form post is a request the seam never sees. The two existing ones are covered by `NATIVE_POSTS`.
- Route handlers (`src/app/api/**`) are **scanned**, not excluded. The brief excluded only `src/server/**`, and nothing in them hits, so the stricter scope costs nothing today.

### FR2.4 Literal exceptions, with reasons

| Entry | Reason | How the reason is checked |
|---|---|---|
| `NOT_SHOWN['/orders \| sign-out \| POST /api/auth/logout']` | Signing out proceeds whatever the answer: the cache is cleared and the user is sent to `/login`. The route makes no upstream call that could fail | `verify` asserts `routerMock.replace('/login')` after the failure (arm P9). A separate test calls the real logout route and asserts `200` and zero Gateway calls |
| `STREAMS['/orders/[id] \| /api/orders/stream']` | An EventSource exposes no response body, so there is no problem text to show. Its failure is the connection badge and the manual retry | Proven in `order-detail-view.test.tsx` / `use-order-stream.test.tsx`. The staleness test requires that the stream is actually observed |
| `PRIMITIVE_EXCEPTIONS` (3) | the stream above, and two native form posts | exact counts; the form posts are swept via `NATIVE_POSTS` |
| `NATIVE_POSTS['POST /api/auth/login']` | covered by a scenario | **Swept:** the real login route is called with a form body against a `FakeGateway` that refuses with the sentinel (both variants). The 303's `?error=` is followed to the page it names, and that page must show the sentinel |
| `NATIVE_POSTS['POST /api/auth/logout']` | the route never answers with a problem | the logout route test above |
| `NOT_RENDERABLE` | (empty) | — |

### FR2.5 What the new instrument found on its first run (fixed)

**All three are real silent failures that round 1's guard could not see.** They were not error-rendering *sites* at all, because nothing was rendered.

1. **`/orders`: a failed `GET /api/catalog/retailers` showed nothing.** The retailer filter simply had no options.
   - Now: `order-retailer-filter-error` shows `Retailer filter unavailable: <detail>`.
   - Arm P12 removes it again, and the sweep fails with `shown instead: (no new text at all)`.
2. **`/billing`: the same request, the same silence.**
   - Now: `billing-retailers-error` under the heading.
3. **`/billing`, after a payment: a failed order-link lookup (`GET /api/orders?orderReference=…`) left `resolving the order link…` on screen forever.**
   - Now: `view-order-link-error` shows `the order link could not be resolved: <detail>`.
   - Arm P13 removes it, and the failure reads `shown instead: resolving the order link…`.

**Nothing else needed a product change.** The existing component tests still pass unchanged (full suite 248/248).

**Observation first left unfixed, then fixed in FR2.13** at the coordinator's direction: when the by-reference lookup *succeeds* with no match (`data === null`), the payment form also showed `resolving the order link…` forever.

### FR2.6 The fate of the fix-round-1 syntax guard

**Deleted: the 65-key `SITES` list, the site deriver and the 12 proof cases.** The sweep dominates them for this claim: it does not care how a failure is rendered, and D1/D2 are exactly the ways a syntax guard loses. Keeping a guard known to be beatable would give false confidence.

**Kept: the fixture-provenance check**, moved to `src/test/fixture-uses.ts` + `src/lib/fixture-provenance.test.ts`, 1 test, with the `MISMATCHED_FIXTURE_USES` literal unchanged.
- **What it proves that the sweep does not:** where component and route tests use **real captured Gateway responses** (bullet 5's *"proven against a REAL Gateway response"* half), each capture is served on the route it was captured from. That was round 1's arms A7/A9.
- The sweep uses synthetic sentinels in a real problem-document shape, so it says nothing about the captures.
- The real-response half remains carried by `error-text-chain.test.ts` (fixture directory → real route handler → page text) and the per-page fixture cases.
- Its `declarations` helper was rewritten as a plain same-file initialiser map, which is all `templateText` needs. Its limits (literal pairings only) are unchanged from FR1.5.

### FR2.7 The sweep's premises, and the arm for each

| # | Premise | Arm(s) |
|---|---|---|
| Q1 | The glob sees every App Router UI file | P1 |
| Q2 | The page is rendered as the browser sees it: layout chain, root providers, params | P2, P11, P14 |
| Q3 | Every browser request goes through `setApiFetch` (content search, including `EventSource` and form posts), and any that does not is caught | M10, P3, P3b |
| Q4 | The recorder really records, so the population cannot shrink silently | P7 |
| Q5 | **Unit: the moment the harness stops waiting, per run.** Round 2 covered only a run stopping while a request is in flight or a 5xx retry is pending (P4, P4b); a request that starts after the 100 ms quiet window was NOT covered (review G4). **Fix round 3** adds a 2 500 ms late watch to every baseline (load and action), and fails on any new key first requested during it (G4) | P4, P4b; round 3: G4, W1 |
| Q6 | Sentinels are unique and random, cannot equal any app text, and are absent before any failure | P5 (plus the baseline nonce check) |
| Q7 | The `detail` variant distinguishes detail from title, and the `title` variant has `detail` absent | P8, M7, M4b |
| Q8 | **Unit, as round 2 built it: `apiRequest` CALL SITES whose method is not GET** — every such call is made by some scripted action, and aliasing cannot hide one. It did **not** cover the unit the claim is about, *every UI way to trigger a mutation or an interaction-gated read* (review G2, G3). **Fix round 3** adds that unit: every hook export and every self-mutating component, with its consumer files and the files rendering those, derived by content and compared to a reviewed literal | M9, P6, P6b, P6c; round 3: G2, G3 |
| Q9 | The exception lists are honest: reasons verified, stale entries fail | P9, P10 |
| Q10 | A page that shows *another* request's error is caught (substitution) | P15 |

### FR2.8 Arming table

**Procedure.** The driver is `scratchpad/fr2/arm.py`. For each arm, it:
1. `copy2`-backs-up each file, mutates it by an exact-anchor replace (the anchor must occur exactly once) or creates the probe files;
2. runs `pnpm exec vitest run src/app/error-text-sweep.test.tsx`;
3. restores with `copyfile` + `utime`;
4. asserts `filecmp.cmp(shallow=False)`, and asserts that created files are gone (and that the `probe/` directory is removed).

**Freshness.** Vitest transforms from source on every run, so there is no stale binary.
- The last source change before the final runs was the type-only change to the glob line in `page-sweep.tsx` (the generic was removed and `load` is now cast).
- P1, which mutates that line, was re-run after it.
- P1 and P11 were also re-run after their assertion messages were added. Their rows quote those runs.

**Aftermath.**
- After the arms, `ls src/app/(app)` shows `billing layout.tsx orders stock`; `src/features/stock` has only its two files; `src/hooks` has its original seven.
- The sweep is 52/52 and the full suite is 248/248.
- The verbatim failure lines are quoted below; full logs are in `scratchpad/fr2/arms/*.log`.

**The ten mandatory arms:**

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| M1 | **B1 restored**: the catalogue notice renders `<p className="text-sm text-destructive">The catalogue could not be loaded — enter codes by hand.</p>` | 2 failed / 52 | `/orders/place \| load \| GET /api/catalog/companies \| detail \| expected the problem detail "sweep-9cdd616e43c9-13-detail" on screen \| shown instead: The catalogue could not be loaded — enter codes by hand. ⏎ Enter codes by hand meanwhile (…)`. The same for `products` and `retailers`, and all three again in the `title` variant |
| M2 | **D1a**: new `app/(app)/probe/page.tsx` → `StockBanner` doing `if (stock.isError) return <p data-testid="probe">Something went wrong</p>;` | 4 failed / 55 | `/probe \| load \| GET /api/stock?belowThreshold=true&page=1&pageSize=5 \| detail \| expected the problem detail "sweep-63bd3fc3d3fa-19-detail" on screen \| shown instead: Something went wrong` (and `title`). Also `pages with no EXPECTED_LOAD entry: … "/probe"` and `/probe: the requests observed on load differ from EXPECTED_LOAD` |
| M3 | **D1b**: the same, with `switch (stock.status) { case 'error': return <p …>Something went wrong</p>; … }` | 4 failed / 55 | `/probe \| load \| GET /api/stock?belowThreshold=true&page=1&pageSize=5 \| detail \| expected the problem detail "sweep-9c158b51836e-19-detail" on screen \| shown instead: Something went wrong` (and `title`) |
| M4 | **D1c** (the reviewer's N8): `const ok = stock.isSuccess \|\| stock.isPending; return <div>{ok \|\| <p …>Something went wrong</p>}</div>;` | 4 failed / 55 | `… \| detail \| expected the problem detail "sweep-e22de66f2745-19-detail" on screen \| shown instead: Something went wrong` (and `title`) |
| M4b | **D1c′**: `{(stock.error as ApiError \| null)?.problem?.detail \|\| (stock.isError ? 'Something went wrong' : 'Low stock')}`. The fallback renders only when `detail` is absent | 3 failed / 55 (the `detail` variant passes, **correctly**) | `/probe \| load \| GET /api/stock?belowThreshold=true&page=1&pageSize=5 \| title \| expected the problem title "sweep-d4d8a132667a-20-title" on screen \| shown instead: Something went wrong` |
| M5 | **D1d** (the reviewer's N6): `app/(app)/stock/error.tsx` returning `<p data-testid="probe">Something went wrong</p>` | 2 failed / 54 | `(app)/stock/error.tsx \| a failed request reaching this boundary \| expected the problem detail "sweep-6fb7241c02c1-21-detail" \| shown instead: Something went wrong` (and `title`) |
| M6 | **D2** (the reviewer's exact mutation): `error={error}` → `error={null}` at the catalogue `ErrorMessage` | 2 failed / 52 | `/orders/place \| load \| GET /api/catalog/companies \| detail \| expected the problem detail "sweep-251d86679286-13-detail" on screen \| shown instead: The catalogue could not be loaded. ⏎ Enter codes by hand meanwhile (…)`. Also `products` and `retailers`, in both variants |
| M7 | **Attack 7**: the stock site renders `Could not load stock: {problem?.detail ?? 'the request failed'}`, ignoring `title` | 2 failed / 52 | `/stock \| load \| GET /api/stock?page=1&pageSize=20 \| title \| expected the problem title "sweep-d511e68fba7b-20-title" on screen \| shown instead: Could not load stock: ⏎ the request failed`. Also `/stock \| replenish \| GET /api/stock?page=1&pageSize=20 \| title \| …` (the refetch after a replenish) |
| M8 | **Mutation-path generic**: the payment form renders `<p data-testid="payment-error">Registering the payment failed.</p>` | 2 failed / 52 | `/billing \| register-payment \| POST /api/invoices/inv-1/payments \| detail \| expected the problem detail "sweep-9f42222cde20-32-detail" on screen \| shown instead: Cancel ⏎ Registering the payment failed.` (and `title`) |
| M9 | **An unscripted mutation**: `useCancelOrder` in `hooks/use-orders.ts` calling ``apiRequest(`/api/orders/${…}/cancel`, { method: 'POST' })`` | 1 failed / 52 | `mutating requests no scripted action makes — add an ACTIONS entry that makes each one` → `"hooks/use-orders.ts:40 POST /api/orders/{}/cancel"` |
| M10 | **A new browser `fetch(`**: `export function pingCatalog() { return fetch('/api/catalog/products'); }` in `hooks/use-catalog.ts` | 1 failed / 52 | `request primitives outside lib/api-client.ts — route the request through apiRequest, or list it with a reason: expected [ 'hooks/use-catalog.ts:8 fetch' ] to deeply equal []` |

**Premise arms:**

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| P1 | The glob narrowed to `/src/app/*/**/…`, which drops the root-level files | 3 failed / 49 | `route files the glob and the disk disagree on` → `"onDiskNotGlobbed": [ "layout.tsx", "page.tsx" ]`; `the root layout (src/app/layout.tsx) is not in the swept population`; `EXPECTED_LOAD entries with no page: … "/"` |
| P2 | The layout chain is not rendered | 6 failed / 52 | `TestingLibraryElementError: Unable to find an accessible element with the role "button" and name "Log out"` (sign-out: runs / detail / title). Also the action-population test (the logout call has no observed request), the staleness test (`POST /api/auth/logout` is not observed), and `every native post …: expected [ 'POST /api/auth/login' ] to deeply equal [ 'POST /api/auth/login', …(1) ]` |
| P3 | Logout goes back to a direct `fetch` (the pre-round seam gap; a GET decoy is kept through `apiRequest`) | 6 failed / 52 | `/orders \| sign-out \| GET /api/auth/logout \| requests bypassed the api-client seam: fetch /api/auth/logout`; `requests with no success body: … "GET /api/auth/logout"`. The static guard also names `features/shell/logout-button.tsx:… fetch` |
| P3b | **Obfuscated**: ``(globalThis as …)['fe' + 'tch']!('/api/auth/logout', …)`` — the static scan cannot see a computed key | 3 failed / 52 | `/orders \| sign-out: requests that bypassed the api-client seam: expected [ 'fetch /api/auth/logout' ] to deeply equal []`. Also the stale `NOT_SHOWN` entry, and the mutation-scan floor (4 < 5) |
| P4 | `settle` returns at once | 8 failed / 52 | `/billing \| load \| GET /api/credits?page=1&pageSize=20 \| detail \| expected the problem detail "sweep-4b109a88632a-2-detail" on screen \| shown instead: Loading credit limits… ⏎ 0`; `/orders/[id] \| … \| shown instead: Loading order…` |
| P4b | `settle` ignores the QueryClient (in-flight count only) | 8 failed / 52 | the same shape: `shown instead: Loading credit limits… ⏎ 0`, `shown instead: Loading order…`. The 503 variant is read during the retry delay |
| P5 | `sentinel()` returns `the request failed` (a real fallback string) | 1 failed / 52 | `malformed sentinels: expected [ 'the request failed', …(41) ] to deeply equal []` |
| P6 | The `replenish` action renamed (its reviewed entry no longer applies) | 1 failed / 52 | `/stock \| replenish-disabled: reviewed requests the action no longer makes: … "(no EXPECTED_ACTION entry)"` |
| P6b | New `hooks/use-probe.ts`: `import { apiRequest as send }` … `send(…, { method: 'DELETE' })` | 1 failed / 52 | `mutating requests no scripted action makes …` → `"hooks/use-probe.ts:3 DELETE /api/orders/{}/archive"` |
| P6c | New `hooks/use-probe.ts`: `const send = apiRequest;` | 1 failed / 52 | `apiRequest calls this scan cannot classify` → `"hooks/use-probe.ts:3: apiRequest is referenced other than by a direct call (aliased or passed along)"` |
| P7 | The recorder no longer records (attack 8) | 12 failed / 52 | `/billing: the requests observed on load differ from EXPECTED_LOAD: expected [] to deeply equal [ 'GET /api/catalog/retailers', …(2) ]` (and every other page) |
| P8 | `describeError` reads `title` where it read `detail` (every site shows title first) | 10 failed / 52 | `/billing \| load \| GET /api/credits?page=1&pageSize=20 \| detail \| expected the problem detail "sweep-f12d2384b9a9-2-detail" on screen \| shown instead: Could not load credit limits: ⏎ decoy-title-f12d2384b9a9` |
| P9 | A failed sign-out returns early without leaving for `/login` | 2 failed / 52 | `a failed sign-out must still leave for /login: expected "vi.fn()" to be called with arguments: [ '/login' ]` |
| P10 | A bogus `NOT_SHOWN` entry | 1 failed / 52 | `stale exception entries` → `"/stock \| load \| GET /api/stock/gone"` |
| P11 | The root layout wraps `Providers` in a `<div>` | 1 failed / 52 | `the root layout wraps pages in something the harness does not render` → `"children": false, "providers": false` |
| P12 | This round's `/orders` retailer-filter error removed | 2 failed / 52 | `/orders \| load \| GET /api/catalog/retailers \| detail \| expected the problem detail "sweep-4e3bf5ef4c46-9-detail" on screen \| shown instead: (no new text at all)` (and `title`) |
| P13 | This round's order-link error disabled | 2 failed / 52 | `/billing \| register-payment \| GET /api/orders?orderReference=ORD-000042&page=1&pageSize=1 \| detail \| expected the problem detail "sweep-fcc582c33e8f-31-detail" on screen \| shown instead: resolving the order link…` (and `title`) |
| P14 | `PARAM_VALUES` renamed to `orderId` | 6 failed / 52 | `Error: sweep: no PARAM_VALUES entry for the dynamic segment "[id]" — add one` |
| P15 | **Substitution**: the orders list shows `retailers.error` instead of `orders.error` | 2 failed / 52 | `/orders \| load \| GET /api/orders?page=1&pageSize=20 \| detail \| expected the problem detail "sweep-3ca5ea27a97f-10-detail" on screen \| shown instead: Could not load orders: ⏎ the request failed` (and `title`) |

### FR2.9 Defeat list (CLAUDE.md's eleven), against this round's instruments

| # | Attack | Verdict |
|---|---|---|
| 1 | Delete the behaviour | M1, M6, M8, P12, P13 |
| 2 | Corrupt a field the test supplied | The sentinel is test-supplied. M7 (title ignored), P8 (title shown instead of detail, caught by the decoy) and M4b (a fallback when detail is absent) |
| 3 | Substitute a valid sibling | P15: another request's error on the same page. The sentinel is unique per request, so a sibling's words can never satisfy it. In the static scans: P6b (a renamed binding) |
| 4 | Shadow from a comment or string | **The sweep cannot be beaten this way:** it reads rendered text, and comments do not render. A string cannot hold the sentinel either, because its nonce is random per run and a test asserts it occurs nowhere in `src/`. **The static scans** read AST nodes, so a comment mentioning `fetch`/`EventSource` is not a hit (`use-order-stream.ts:62` is one, and it is correctly not counted). A request smuggled through a string (`['fe'+'tch']`) is caught behaviourally (P3b) |
| 5 | Dead region | TypeScript has no preprocessor. Code under `if (false)` is still parsed, so the static scans over-include (a loud failure). The sweep executes only live code, which is the code the user sees |
| 6 | Raw/verbatim string | The same as 4. Template-literal paths are parsed as `TemplateExpression` (P6b's path is a template) |
| 7 | Drop an optional element | The `title` variant drops `detail` (M4b, M7). A missing `PARAM_VALUES` entry fails (P14). A page with no reviewed list fails (M2) |
| 8 | Literal against literal | P7: the observed population is compared to `EXPECTED_LOAD`, and both are non-trivial. P1: the glob is compared to a disk walk |
| 9 | Closer half satisfied, premise stale | Every exception list is checked both ways: `NOT_SHOWN` verify (P9), staleness (P10), exact primitive counts, a listed-but-shown entry fails, and `EXPECTED_*` in both directions (P6) |
| 10 | Build output joins the population | **Not applicable:** the glob and the walk are rooted at `src/app`, and the scans at `src/`. `.next/`, `coverage/` and `node_modules/` lie outside both |
| 11 | A form the instrument does not recognise | **This round's reason to exist.** M2 (early return), M3 (`switch`), M4 (`\|\|`), M4b (`\|\|` fallback), M5 (`error.tsx`). All fail, because the sweep never recognises forms: it reads what is on screen. P3b shows the one place a form still matters (a computed `fetch` key defeats the static scan), and it is caught behaviourally |

### FR2.10 Residual limits (stated, not closed)

- **UI states reachable only by an unscripted interaction.** Changing a filter or a page issues the *same* query with another key; its error branch is the one the load sweep exercised, but that key is not failed separately. The same applies to the detail page's 202 **Check now**, and to 409 shortages (the problem text itself is swept; the shortages list is proven in `place-order-form.test.tsx`).
- **Requests fired after the settle window**, such as the 5 s polls. They repeat load keys, which are swept at load.
- **Presence, not visibility.** The assertion reads `textContent`. A sentinel hidden by CSS would pass; jsdom has no layout. No component hides error text.
- **The root layout is checked as an element tree, not rendered.**
- **Obfuscated primitives in code no scripted render reaches** escape both the static scan (computed keys) and the behavioural one. Reaching them needs a deliberate `['fe'+'tch']` on a path no page loads and no action takes.
- **`src/server/**` is excluded from the primitive scan by path.** Every non-test file there (`config.ts`, `gateway.ts`, `session.ts`, `stream-proxy.ts`) imports `server-only`, which fails in a client bundle.
- **The observed population depends on the success bodies.** A page that makes further requests only for data shapes the table does not produce, such as an empty list, is not driven into those states.

### FR2.11 Files touched this round

- **Product:** `apps/web/src/features/shell/logout-button.tsx`, `apps/web/src/features/orders/orders-list.tsx`, `apps/web/src/features/billing/billing-view.tsx`; and, in FR2.13, `apps/web/src/features/stock/stock-view.tsx` and `apps/web/src/components/pager.tsx`.
- **Tests changed in FR2.13:** `billing-view.test.tsx` (+2), `stock-view.test.tsx` (+1), `orders-list.test.tsx` (two assertions added to the existing loading case).
- **Test support:**
  - `apps/web/src/test/setup.ts`
  - `apps/web/src/test/page-sweep.tsx` (**new**)
  - `apps/web/src/test/request-sites.ts` (**new**)
  - `apps/web/src/test/fixture-uses.ts` (**new**, extracted)
- **Tests:** `apps/web/src/app/error-text-sweep.test.tsx` (**new**), `apps/web/src/lib/fixture-provenance.test.ts` (**new**).
- **Deleted:** `apps/web/src/lib/error-rendering-sites.test.ts`, `apps/web/src/test/error-sites.ts`.
- **Record:** this section, and the §4 row L2 clause.

### FR2.12 Counts and gates (read off runs in this session; no service running)

**`pnpm exec vitest run`: 18 files, 248 tests, all passed.** Against the baseline of 17 files / 211 tests, the delta reconciles exactly:
- tests: 211 − 16 (the deleted guard's cases) + 1 (`fixture-provenance`) + 52 (the sweep) = **248**;
- files: 17 − 1 + 2 = **18**.

**The sweep's 52 cases:**
- 5 population cases;
- 7 pages × 3 = 21;
- 1 boundary-population case (there are no boundary files today, so no per-file cases);
- 5 actions × 3 = 15;
- 4 native-post cases;
- 6 premise cases.

Total: 5 + 21 + 1 + 15 + 4 + 6 = 52. The sweep takes about 28 s of the suite.

**Other web gates:**
- `pnpm run lint`: exit 0 (`lint-coverage OK — all 96 source files (87 under src/)`).
- `pnpm run typecheck`: exit 0. It first failed on `import.meta.glob`'s generic (TS2558); the generic was replaced by a cast on `load`, and P1 was re-armed afterwards.
- `pnpm run types:check`: exit 0.

**`./quality.sh`**, full, stack stopped, run once (22:06 → 22:22): **exit 0**, ending `[OK] quality.sh finished`.
- **.NET:** `0 Warning(s)`, `0 Error(s)`. 18 `Passed!` lines, summed to **2 057**, with 0 `Failed!` lines. This round changed no .NET code.
- **Web:** Vitest **18 files, 248 passed**. Coverage is statements **96.34%** (816/847) and lines **98.22%** (718/731), up from 91.61% / 92.74%, because the sweep now renders pages and layouts no test rendered before. The production-build integration run is **7 passed**.

**`./init.sh`:** exit 0, with no `FAIL` line.

**Not done:** `specs/shared/test-matrix.md` is untouched (read-only; RR6 routes its edits to id 29's close). `feature_list.json` is untouched, as the brief requires.

### FR2.13 An answered lookup that reads as still working (bullet 4) — fixed, with its siblings

**Coordinator direction:** fix the defect FR2.5 left as an observation, and check apps/web for the same shape. That shape is *"pending" and *"answered but empty"* sharing one branch, so an answered state reads as still working.

**1. The defect, `features/billing/billing-view.tsx` (the payment form's order link).**
- **Before:** the last branch, `<span>resolving the order link…</span>`, covered both "the lookup is still pending" and "the lookup answered with no order". A successful empty lookup therefore said *resolving* forever.
- **After:** the states are separate:
  - error → the existing `ErrorMessage`, unchanged;
  - `isPending` → `view-order-link-resolving`, *resolving the order link…*;
  - an order id → the link;
  - otherwise → `view-order-link-not-found`, *no order was found for ORD-000042, so there is no timeline to link to.*
- **Also fixed:** the enclosing element changed from `<p>` to `<div>`. The error branch I added earlier this round had put `ErrorMessage`'s `<p>` inside a `<p>`, which is invalid DOM nesting.

**2. Tests** (`billing-view.test.tsx`, +2):
- *while the order-link lookup is UNRESOLVED the form says it is resolving the link, and nothing else*:
  - the lookup is held on a `deferred()` promise, and the test asserts the resolving text and the absence of the not-found text and of the link;
  - it then releases the promise and asserts the link appears and *resolving* is gone.
- *an order-link lookup that ANSWERS with no order says no order was found — it never stays on "resolving"*:
  - the test first waits until the lookup request has actually been made;
  - it then waits, up to 2 s, for the form's text to stop matching `/resolving/`, with the message *the answered lookup must not still read as working*;
  - finally it asserts the named message.

**3. The shape, enumerated by content** (in `apps/web`, excluding by path; run on the final tree):

```
find src -type f \( -name '*.ts' -o -name '*.tsx' \) -not -path 'src/generated/*' -not -path 'src/test/*' -not -name '*.test.*' -print0 | xargs -0 grep -nE "isPending|isLoading|isFetching|isIdle|fetchStatus|\.data \?|\.data\) \?|Loading|loading|resolving|Checking|checking|…'|…<|…\}|Waiting|waiting"
```

**54 lines** (`wc -l`). One line or group per hit:

| Hit(s) | Classification |
|---|---|
| `billing-view.tsx:297-299,304` | **the defect above, now fixed** (pending → resolving; the answered-empty branch is at `:304`) |
| `stock-view.tsx:154` **in the pre-fix run** (matched by `…'`; the fixed lines `:154-159` no longer match) | **REAL SIBLING, fixed.** The replenish outcome read `on hand is now {… ?? '…'}`, so an ANSWERED replenish whose `items` omitted the line showed a permanent ellipsis. It now reads *— the answer did not include this line's new on-hand level.* |
| `pager.tsx:17` | **The inverse sibling, fixed.** It was found by reading the component that the lists hand `x.data?.page` to; the command did **not** list the pager before the fix. The pre-fix `Page {page?.page ?? current} of {pages} · {page?.total ?? 0} {noun}` claimed **"· 0 orders" while the list still read "Loading orders…"**, so a pending list was presented as an answered empty one. It now shows only `Page {current}` until there is a page. Today's hit is the fix's comment |
| `billing-view.tsx:74-76, 159-161`; `orders-list.tsx:74-76`; `stock-view.tsx:88-90`; `order-detail-view.tsx:52-54` | correct: an `isError ? … : isPending ? <loading> : data.items.length === 0 ? <empty> : <rows>` chain. Loading is reachable only while pending, and empty and error have their own branches |
| `billing-view.tsx:152`; `orders-list.tsx:69`; `stock-view.tsx:83` | correct: `isFetching && !isPending ? 'refreshing…'`, shown only during a background refetch, and it clears when the refetch settles |
| `billing-view.tsx:270-271`; `place-order-form.tsx:373-374`; `stock-view.tsx:177-178` (`:172-173` before the fix); `login-form.tsx:68-69` | correct: a mutation button label `isPending ? 'Adding…'/'Placing…'/'Registering…'/'Signing in…'`. It returns to its idle label on success and on error |
| `login-form.tsx:63` | `initialError && login.isIdle`: the no-JS error, not a loading state |
| `order-detail-view.tsx:57, 66, 69, 72, 73` | the R55 waiting state. Reached only on an answered **202**; it is named, schedules a retry, and `checking…` follows `isFetching` |
| `order-detail-view.tsx:20, 22` | the stream badge labels `Connecting…`/`Reconnecting…`, driven by the client's status. `gave-up` has its own label and a retry button (proven in `order-detail-view.test.tsx`) |
| `place-order-form.tsx:81, 85-87, 217, 232, 273`; `orders-list.tsx:61`; `billing-view.tsx:39-40` | `x.data ?? []` for option lists, not a loading message. An answered empty catalogue falls back to typed inputs (`*Usable`); a failed one is shown (FR2.5) |
| `use-order-detail.ts:33`; `login-form.tsx:18`; `orders-list.tsx:20`; `order-detail-view.tsx:29`; `app/api/orders/[id]/route.ts:9` | comments |

**The sibling fixes' tests:**
- `stock-view.test.tsx` (+1): *an answered replenish that omits this line says so — never an ellipsis that reads as still working*. It asserts the whole outcome text.
- `orders-list.test.tsx`: the existing UNRESOLVED loading case now also asserts that the pager reads exactly `Page 1` (with the message *while loading, the pager must not claim a count*), and that after the empty answer it reads `Page 1 of 1 · 0 orders`.

**4. Arms** (the same driver; restored with `copyfile` + `utime`, and `filecmp` asserted):

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| R1 | **The single `else` restored**: the `isPending` branch removed and the not-found branch rendering `resolving the order link…` again | 1 failed / 17 | `AssertionError: the answered lookup must not still read as working: expected 'Register payment for INV-000027 (ORD-…' not to match /resolving/`, where Received is `"… The order completes once the saga catches up — resolving the order link…"` |
| R2 | The pending branch removed, so pending shows the not-found text (the pending test is not vacuous) | 1 failed / 17 | `TestingLibraryElementError: Unable to find an element by: [data-testid="view-order-link-resolving"]` (in *while the order-link lookup is UNRESOLVED …*) |
| R3 | The replenish outcome's ellipsis restored (`' — on hand is now ….'`) | 1 failed / 10 | `AssertionError: the answered replenish must not read as unfinished` / `Expected: "Added 5 units to PRD-0001 — the answer did not include this line's new on-hand level."` / `Received: "Added 5 units to PRD-0001 — on hand is now …."` |
| R4 | The pager claims `Page 1 of 1 · 0 orders` while loading | 1 failed / 5 | `AssertionError: while loading, the pager must not claim a count: expected 'Page 1 of 1 · 0 ordersPreviousNext' to match /^Page 1PreviousNext$/` |

After the arms, each suite was green again (17/17 billing, and the full suite below).

**5. Counts:**
- `pnpm exec vitest run`: **18 files, 251 passed**, which is 248 + 3 new cases (2 billing + 1 stock; the orders-list change added assertions, not cases).
- `pnpm run lint`: exit 0 (`lint-coverage OK — all 96 source files (87 under src/)`).
- `pnpm run typecheck`: exit 0.
- `QUALITY_ONLY=web ./quality.sh`: **exit 0**, ending `[OK] quality.sh finished`. Vitest 18 files / 251 passed, with coverage statements 96.34% (818/849) and lines 98.22% (719/732); production build; integration 7/7. The .NET side is unchanged since the full run at 22:22.

## Fix round 3 — D1 (a no-JS sign-in bug), D2 ("never a generic message"), D3 (the sweep's populations)

**Brief:** fix round 3 for id 29, after the round-3 review (`review_web_app.md` § "Re-review, fix round 2"). The scope is exactly D1, D2 and D3(a)(b)(c), the Q5/Q8 corrections and the `NATIVE_POSTS` advisory; nothing else was widened. Files touched: `apps/web/**` and this record. **No `.cs`, `.csproj` or `.props` file changed:** `find src tests \( -name '*.cs' -o -name '*.csproj' \) -not -path '*/bin/*' -not -path '*/obj/*' -newermt '2026-09-16 22:06'` printed nothing. **Wall-clock:** from about 23:05 on 2026-09-16 to about 00:40 on 2026-09-17, of which about 60 minutes was the full re-arming run (39 arms × about 90 s).

### FR3.1 D1 — `app/api/auth/login/route.ts`

**Fixed.** Both refused upstream calls in the no-JS form path, `POST /auth/login` and `GET /auth/me`, now go through one helper, `problemText(response)`. It returns the problem's `detail`, else its `title`, and uses `Sign-in failed (HTTP n).` only when the body carries neither. Before the fix, line 58 always used the literal for `/auth/me`.

**The native sign-in case is now observe-then-fail-each.** The form post is made once against a `FakeGateway` that answers everything successfully (`writeSession` is mocked, because jsdom cannot seal a cookie; sealing is proven in `route-handlers.test.ts`). The upstream calls are then read from `gateway.requests`: they are `['POST /auth/login', 'GET /auth/me']`, which is also asserted. Each call is then refused in turn:
- with **every status `openapi.yaml` declares for it**: login 400, 401 and 429; me 401;
- in both variants;
- the 303's `?error=` is followed, and the page it names must show the sentinel, with nothing beside it that is not a reviewed label (D2).

**`NATIVE_POSTS['POST /api/auth/login']`** now describes exactly that.

**Content search over the route handlers** (all 12 `src/app/api/**/route.ts` files):

```
find src/app/api -name 'route.ts' -print0 | xargs -0 grep -nE "redirectToLogin|localProblem|gatewayUnreachable|relay\(|NextResponse\.(json|redirect)|new (Next)?Response\(|failed|Failed|\`[A-Z][a-z]+ [a-z]"
```

One line per hit (line numbers are the fixed file's):

| Hit | Classification |
|---|---|
| `auth/login/route.ts:32` `localProblem(400, … 'The login request body could not be read.')` | the route's OWN refusal of an unreadable body. There is no upstream problem to drop, and the problem it writes is itself `detail`-bearing |
| `auth/login/route.ts:39, 51` `redirectToLogin(request, gatewayUnreachableDetail(error))` / `gatewayUnreachable(error)` | the Gateway could not be reached, so there is no response body. The text names the reason (`error.message`) |
| `auth/login/route.ts:43` | the first refusal, via `problemText`. **Swept** |
| `auth/login/route.ts:54` | **D1, fixed**, via `problemText`. **Swept** (`GET /auth/me`) |
| `auth/login/route.ts:65` | the success path (303 to `/orders`, or JSON session info) |
| `auth/login/route.ts:73` | `problemText`'s own last-resort fallback, used only when the body has no `detail` and no `title` |
| `auth/login/route.ts:76, 80, 83` | helper definitions (`gatewayUnreachableDetail`, `redirectToLogin`) |
| `auth/logout/route.ts:8` | the success body; no upstream call |
| `auth/session/route.ts:8` | session info; no upstream call |
| `catalog/[kind]/route.ts:2, 13` | an unknown catalogue name is refused locally with its own `detail`. Otherwise `proxyToGateway` |
| `orders/stream/route.ts:2, 36, 40, 43` | unreachable → `gatewayUnreachable`; a refused stream → `relay(upstream)` (byte-for-byte); success → the relayed stream |

**The other seven route files** (`credits`, `invoices`, `invoices/[id]/payments`, `orders`, `orders/[id]`, `stock`, `stock/replenish`) produced no hit themselves. Each calls `proxyToGateway` (`server/gateway.ts:83-110`), whose error paths are `notSignedIn()` (a local problem with its own detail), `gatewayUnreachable(error)`, or `relay(upstream)`, which copies the Gateway's body byte for byte.

**No other path drops a problem body.**

### FR3.2 D2 — "never a generic message", asserted

- **What is checked.** In every failing run (load, action, native sign-in, and the boundary files), the text nodes the run ends with **that its all-success baseline never showed at any moment** are collected, minus any text containing the sentinel. Each must be a key of the reviewed literal `LABELS`.
- **How "never showed" is recorded.** A `MutationObserver` records every text node the baseline ever displays: the form before submit, pending labels, the outcome. Comparing against the baseline's *final* screen instead flagged a still-open form's `Cancel`/`Add units` as "added"; a snapshot at the first action request was timing-dependent.
- **`LABELS`** holds 13 entries:
  - the review's **seven** prefixes;
  - `the order link could not be resolved:`, this round's order-link error prefix, which the payment action produces;
  - the manual-entry help, as its **five** text-node fragments (the `<code>` elements split it). The other codes, `IBERFOODS` and `PRD-0001`, are already on screen as options.
- **Checked both ways.** One test requires every sweep in the file to have run, then requires every label to have been produced by some run; a label nothing produces is stale.

### FR3.3 D3 — closed, all three parts

**(a) Who can trigger a request after load.**
- `requestUnits()` (`src/test/request-sites.ts`) derives, with the TypeScript parser:
  - every export of `src/hooks/*`, with its kind (`mutation` if it contains `useMutation(`, `gated-query` if a `useQuery({ … enabled … })`, `query`, `value`) and its consumer files, from import declarations resolved by specifier (`@/…` and relative; renamed, namespace and default imports; type-only imports ignored);
  - every non-hook browser file that itself calls `useMutation`, a gated `useQuery` or a mutating `apiRequest`, as `file#*`, whose consumers are the files importing anything from it.
- For `mutation` and `gated-query` units it also lists the files that render those consumers (`via`), so a second page rendering the same component is a second way in.
- The result is compared, line for line, to the reviewed literal `REQUEST_UNITS` (19 lines).
- **Query hooks are included, not only mutations and gated reads.** G3's interaction gate is a conditional *mount*, which no hook-level property shows. What does show is a **new consumer** of `useStock`.

**(b) Requests that start late.**
- Every baseline (7 pages, 5 actions) keeps recording for **`LATE_WATCH_MS` = 2 500 ms** after `settle`, then settles again. It fails on any key first requested in that window that the phase had not already requested. The load and action tests assert that check **before** the population literal, so the failure names the late mechanism.
- **How the bound was set.** I searched the app's timers by content (`setTimeout|setInterval|refetchInterval|retryDelay|Retry-After|staleTime|_MS|Ms` over browser code):
  - the only app-set **non-poll** timer that starts a request is the 202 retry. Its default is `DEFAULT_PENDING_RETRY_MS` = 2 000 (`lib/order-detail.ts:6`), and the bound is computed as that + 500.
  - a Gateway `Retry-After` can be longer, but it comes from the server, not the app, and it only ever **repeats** the detail key.
  - TanStack's 5xx retry delay is 1 s.
  - `refetchInterval: 5_000` (orders, stock, invoices, credits, and the detail backstop) only repeats keys.
  - `staleTime` starts no timer.
  - the stream client sets no timer (its reconnects are the `EventSource`'s own).
- **The sweep's success bodies never answer 202, so the 202 retry itself never fires in the harness.** The bound is set from it anyway, so that a future non-poll delay up to that length is caught.
- **Newly observed, because the watch now outlasts a stale time.** On `/orders`, sign-out clears the cache, and the page refetches `GET /api/catalog/retailers` and `GET /api/orders?…` while the user is being sent to `/login`. Both are now in `NOT_SHOWN` with that reason, and their `verify` asserts `router.replace('/login')`.

**(c) Failure statuses come from `openapi.yaml`.**
- `src/test/openapi-statuses.ts` reads `src/generated/openapi.ts` with the TypeScript parser: `paths` → `operations["id"]` → `responses`' numeric keys. `pnpm types:check` keeps that file equal to a fresh generation from the spec.
- Each failing request is served **every** declared status ≥ 400. The browser path minus `/api` is matched to the operation's template.
- **401 is omitted except on `/login`**, because everywhere else the app's answer to a 401 is `redirectOnSignedOut` in `app/providers.tsx`, a navigation rather than text. **That redirect is not proven by any test today** (a `grep` for it in test files finds nothing). It is recorded here and not widened into this round.
- A web-only route (`POST /api/auth/logout`) draws from the literal `WEB_ONLY_STATUSES` (`[500]`, with the reason). A test asserts that no such entry has a real operation, and that three sample look-ups equal the spec: order detail `[404]`, login on `/login` `[400, 401, 429]`, payments `[400, 404, 409, 422, 503]`.
- A request matching no operation throws, naming itself.
- The error-boundary cases use the union of declared statuses.

**The Q5 and Q8 corrections** are applied in FR2.7's premise table.

### FR3.4 Arming table (this round; the same driver, `scratchpad/fr2/arm.py`)

**Procedure.** Each arm backs files up with `copy2`, mutates them by an exact-anchor replace (the anchor must occur exactly once), runs `pnpm exec vitest run src/app/error-text-sweep.test.tsx`, restores with `copyfile` + `utime`, and asserts `filecmp.cmp(shallow=False)`.

**Runs.**
- All 39 arms (these 9 plus every round-2 arm) ran after the last change to the harness, except one: I then moved the `late` assertion ahead of the population assertion (a test-file-only reorder, so G4's failure names the late mechanism). G4 and W1 were re-run after it, and their rows quote those runs.
- After the run, `ls src/app/(app)` shows `billing layout.tsx orders stock` and `src/features/stock` shows its two files. The full suite is 255/255.

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| **D1** | `login/route.ts`: the `/auth/me` refusal is answered with `` `Sign-in failed (HTTP ${meResponse.status}).` `` again | 2 failed / 56 | `/login \| native POST /api/auth/login \| upstream GET /auth/me 401 detail \| expected the problem detail "sweep-61b160723f98-68-detail" on screen \| shown instead: Sign-in failed (HTTP 401).` and `… upstream GET /auth/me 401 title \| expected the problem title "sweep-61b160723f98-72-title" on screen \| shown instead: Sign-in failed (HTTP 401).` |
| **G1** | `/stock`: `{stock.isError ? <p>Something went wrong</p> : null}` rendered **beside** the existing detailed `ErrorMessage` | 4 failed / 56 | `/stock \| load \| GET /api/stock?page=1&pageSize=20 \| 503 detail \| the problem's words are shown, but so is text that is not a reviewed label: "Something went wrong"` (and `503 title`; also `/stock \| replenish` for its refetch) |
| **G2** | `/orders`: a "Quick top-up" button calling `useReplenishStock().mutate(…)`, rendering `Something went wrong` on error | 1 failed / 56 | `hook exports / mutating components and who uses them — a new consumer of a mutation or gated read must be scripted in ACTIONS first` → `+ "hooks/use-stock.ts#useReplenishStock mutation \| features/orders/orders-list.tsx, features/stock/stock-view.tsx \| via app/(app)/orders/page.tsx, app/(app)/stock/page.tsx"` |
| **G3** | `/orders`: a "Show low stock" button mounting `LowStock` (`useStock({ belowThreshold: true, page: 1, pageSize: 5 })`, generic error) | 1 failed / 56 | the same test → `+ "hooks/use-stock.ts#useStock query \| features/orders/orders-list.tsx, features/stock/stock-view.tsx"` |
| **G4** | `/stock`: a `LateStock` child (`useStock({ belowThreshold: true, … })`, generic error) mounted by a 400 ms `setTimeout` after mount | 6 failed / 56 | `/stock: requests that first started only after load had settled (watched for 2500 ms) — review them before adding them to EXPECTED_LOAD` → `+ "load: GET /api/stock?belowThreshold=true&page=1&pageSize=5"`. Also the same for `/stock \| replenish`, and the sweep then fails the late key: `/stock \| load \| GET /api/stock?belowThreshold=true&page=1&pageSize=5 \| 503 detail \| expected the problem detail "sweep-2497478f9a2b-19-detail" on screen \| shown instead: (no new text at all)` |
| W1 (control) | G4 **plus** `LATE_WATCH_MS = 0` | 5 failed / 56 | The `/stock` load test **no longer** fails, so without the watch the load population misses the late request. It is caught only incidentally, because the replenish action outlasts 400 ms (`/stock \| replenish: requests the action makes that EXPECTED_ACTION does not list`), and by the bound premise `expected 0 to be greater than 2000` |
| **G6** | `describeError` returns `'Something went wrong'` for `status === 404 \|\| status === 500` | 7 failed / 56 | `/orders/[id] \| load \| GET /api/orders/9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f \| 404 detail \| expected the problem detail "sweep-6bcbea5f8618-7-detail" on screen \| shown instead: Could not load this order: ⏎ Something went wrong` (and `404 title`; also `/stock \| replenish … 404`, `/billing \| register-payment … 404`) |
| S1 | Statuses hand-picked (`[503, 422]`) instead of read from the spec | 1 failed / 56 | `AssertionError: expected [ 503, 422 ] to deeply equal [ 404 ]` (the status premise) |
| L1 | A reviewed label that nothing produces (`'Something else entirely:'`) | 1 failed / 56 | `LABELS no failing run produced — stale: expected [ 'Something else entirely:' ] to deeply equal []` |

**The round-2 arms, re-run against this round's sweep: 30 of 30 still fail** (`scratchpad/fr2/arms/run3.out`):

| Arms | Failed / total (each) |
|---|---|
| M1, M6 | 3 / 56 |
| M2, M3, M4 | 5 / 59 |
| M4b | 4 / 59 |
| M5 | 2 / 58 |
| M7, M8, M9, M10 | 2 / 56 |
| P1 | 3 / 53 |
| P2 | 7 / 56 |
| P3 | 8 / 56 |
| P3b | 6 / 56 |
| P4, P4b | 14 / 56 |
| P5, P6, P10, P11 | 1 / 56 |
| P6b, P6c | 2 / 56 |
| P7 | 13 / 56 |
| P8 | 10 / 56 |
| P9, P12, P13, P15 | 3 / 56 |
| P14 | 7 / 56 |

**Why the counts rose.** Where an arm removes the problem's words, the both-ways label test now also fails, because that label is no longer produced; the round-2 messages are otherwise unchanged in shape.

### FR3.5 What this round does not claim

- **The 401 → `/login` redirect** (`app/providers.tsx`) is excluded from the page runs with its reason. *(Superseded: FR3.7 now proves it on its own.)*
- **`REQUEST_UNITS` sees consumers at file granularity.** A second *use* of a hook inside a file that already consumes it (for example, a second mount of `useStock` in `stock-view.tsx` behind a click) changes no line. G4's `LateStock` is exactly that, and is caught by the late watch only because it mounts on a timer. **The same shape behind a click in an already-listed consumer file remains unguarded.**
- **The late watch** is 2 500 ms, per baseline. Failing runs do not watch.

### FR3.6 Counts (read off runs this session)

- **`pnpm exec vitest run`:** **18 files, 255 passed**, which is 251 + 4. The sweep went from 52 to 56 cases (+ the label both-ways test, + the request-units test, + the status-source test, + the late-watch bound test). The sweep file now takes about 88 s.
- **`pnpm run lint`:** exit 0 (`lint-coverage OK — all 97 source files (88 under src/)`; +1 for `src/test/openapi-statuses.ts`).
- **`pnpm run typecheck`:** exit 0.
- **`pnpm run types:check`:** OK.
- **`QUALITY_ONLY=web ./quality.sh`:** **exit 0**, ending `[OK] quality.sh finished`. Vitest **18 files / 255 passed**; coverage statements 96.46% (818/848), lines 98.35% (719/731); production build OK; integration **7/7**. The .NET side is unchanged (no `.cs` file is newer than the full run of 22:06).
- **Files touched this round:**
  - product: `apps/web/src/app/api/auth/login/route.ts`;
  - test support: `apps/web/src/test/page-sweep.tsx` and `apps/web/src/test/request-sites.ts`, plus `apps/web/src/test/openapi-statuses.ts` (**new**);
  - test: `apps/web/src/app/error-text-sweep.test.tsx`;
  - record: this section, and FR2.7's Q5/Q8 rows.

### FR3.7 Session expiry (401 → signed out → `/login`) — now tested on its own

**Coordinator direction:** close FR3.5's "not claimed" item. The behaviour is live code that every user reaches when the session expires. Scope: tests only, with no product change.

**Tests (+9 cases, 2 new files):**
- **`src/server/gateway-relay.test.ts`** (node). It calls `relay()` directly with a problem body in the shape of the real captured 401.
  - *an upstream 401 is relayed with a Set-Cookie that EXPIRES the otc_session cookie* asserts that the header matches `^otc_session=;`, `Max-Age=0` and `Path=/ … HttpOnly`, each with its own message.
  - *an upstream 403 / 500 is relayed and the session cookie is NOT touched* asserts `set-cookie` is `null`.
  - (`route-handlers.test.ts:201` already proved the 401 case through a proxied route. It had no negative case and did not call `relay` itself.)
- **`src/app/providers.test.ts`** (node).
  - It calls the real `makeQueryClient()` and fails a query with `client.fetchQuery(…)`, and a mutation with `getMutationCache().build(…).execute()`, each with an `ApiError`.
  - `window` is a stubbed stand-in with `location.pathname` and a `vi.fn()` `assign`, because jsdom's `location.assign` cannot be observed. The code reads `window` at call time, so the stand-in is what it sees.
  - The cases:
    - a **query** 401 → `assign` called exactly `[['/login']]`;
    - a **mutation** 401 → the same;
    - a query 403 and a mutation 403 → `[]`;
    - a query 401 and a mutation 401 with `pathname === '/login'` → `[]` (no loop).

**Arms** (same driver; `copyfile` + `utime` restore, `filecmp` asserted; after the run, `grep` shows `gateway.ts:70` `if (upstream.status === 401) clearSession(response);` and `providers.tsx:8` / `:18` intact):

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| U1 | `relay`: the `clearSession(response)` call deleted | 1 failed / 9 | `AssertionError: the 401 must clear the session cookie: expected '<no Set-Cookie header: the session wa…' to match /^otc_session=;/` |
| U2 | `relay`: clears on **403** instead of 401 | 2 failed / 9 | the same 401 failure, plus `AssertionError: a 403 must not sign the user out: expected 'otc_session=; Path=/; Max-Age=0; Secu…' to be null` |
| U2b | `providers`: redirects on **403** instead of 401 | 4 failed / 9 | `a failed query with 401 must navigate to /login: expected [] to deeply equal [ [ '/login' ] ]`, the same for the mutation, and `a 403 must leave the user where they are: expected [ [ '/login' ] ] to deeply equal []` (query and mutation) |
| U3 | `providers`: the `MutationCache` handler removed | 1 failed / 9 | `AssertionError: a failed mutation with 401 must navigate to /login: expected [] to deeply equal [ [ '/login' ] ]` |
| U4 | `providers`: the `/login` guard removed | 2 failed / 9 | `AssertionError: on /login a 401 is the sign-in form's own error, not a redirect: expected [ [ '/login' ] ] to deeply equal []` (query and mutation) |

**Is leaving 401 out of the page sweep still right? Yes**, for three reasons:
1. **The sweep's claim is the wrong one for a 401.** It asserts that a failure is *rendered* in its own words, and the app's contract for a 401 off `/login` is the opposite: *leave the page*. A full navigation, which jsdom cannot perform, replaces whatever was rendered. Asserting a sentinel there would test text the user is being taken away from.
2. **The redirect is now proven where it is actually decided.** Every browser query and mutation in the app goes through the one `QueryClient` that `Providers` creates with `makeQueryClient()`, and the sweep's root-layout test asserts that `Providers` wraps every page. So the two cases above (query and mutation) cover every page's 401 by construction, rather than page by page.
3. **The one place where a 401 IS rendered is still swept.** On `/login` the redirect is suppressed and the form shows the refusal, and there 401 stays in the sweep: the `sign-in` action with its declared 401, and the native post's upstream 401s.

**One gap, stated:** the logout button's `apiRequest` bypasses the query client, so a 401 there triggers no redirect. It needs none, because sign-out navigates to `/login` whatever the answer, which FR2.4's `verify` asserts.

**Counts:**
- `pnpm exec vitest run`: **20 files, 264 passed**, which is 255 + 9 (relay: 3 cases; providers: 6 cases), with 18 + 2 files.
- `pnpm run lint`: exit 0.
- `pnpm run typecheck`: exit 0.
- **`QUALITY_ONLY=web ./quality.sh`:** **exit 0**. Lint coverage reports `all 99 source files (90 under src/)`. Vitest **20 files / 264 passed**; coverage statements 96.58% (819/848), lines 98.49% (720/731); production build OK; integration **7/7**. No `.cs` file changed.
- **Files touched:** `apps/web/src/server/gateway-relay.test.ts` (**new**), `apps/web/src/app/providers.test.ts` (**new**), and this record (FR3.5's line, and FR3.7).
