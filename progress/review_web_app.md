# Review — phase 16: id 29 `web_app`, id 30 `web_component_tests`, id 96 `gateway_ignores_gateway_port`

**Reviewer session:** 2026-09-16, brief `brief_review_p16.md` (18:28) → this record. Stack started and stopped once; `./quality.sh` run once, in full, with the stack stopped.

## Verdict

| Entry | Verdict | Status after this review |
|---|---|---|
| **id 29 `web_app`** | **REJECTED** — one blocking defect against acceptance bullet 5 (B1) | left `in_review`, as the brief directs for a rejected entry |
| **id 30 `web_component_tests`** | both bullets **met** and verified; not closed, because its own notes say it *"is verified when id 29 closes"* | left `pending` |
| **id 96 `gateway_ignores_gateway_port`** | **APPROVED** | set to `done`; effort record appended to `progress/history.md` |

Two further defects are in the leader's own phase-16 changes (L1, L2). L1 (the `WEB_PORT` override no longer works) breaks the manual-test recipe and should be fixed in the same round as B1. L2 is advisory.

**Full suite:** `./quality.sh` exited 0, run once with the stack stopped and port 3001 held by a dummy listener (details in §9):
- .NET: **2 057 passed / 0 failed / 0 skipped** across 18 projects;
- web: Vitest **189/189** in 16 files, production-build integration **7/7**, line coverage 92.71%.

---

## 1. Blocking defect

### B1 — the place-order page's catalogue failure notice is a generic message, and the one site that bypasses the problem document (id 29 bullet 5)

- **File:** `apps/web/src/features/orders/place-order-form.tsx:186-190`.
  - When any of `/api/catalog/{retailers,companies,products}` fails, the page shows a fixed sentence: *"The catalogue could not be loaded — enter codes by hand …"*.
  - The Gateway's problem document (`detail`, then `title`) is never read, so the user is not told *why*: 503 upstream unavailable, 502 Gateway unreachable, and so on.
- **The contract:** bullet 5 says *"every error shown to a user is THAT error's own text, read from the RFC 9457 problem document's detail (falling back to title) — never a generic message."* The brief asked whether any page reads the problem document some other way. This site reads it in no way at all.
- **It is a known choice, not an oversight.** The guarding test says so: `place-order-form.test.tsx:224`, *"Any real 503 body will do: only the failure matters here, not its words."* Its only assertion on the notice (`:231`) is `toBeInTheDocument()`.
- **The population claim in the record is therefore incomplete.** `impl_web_app.md` §2 bullet 5 says *"Component tests on every page use the same captured bytes"*. The site table below has 12 error-rendering sites; 11 read the problem document and 10 have a test asserting the fixture's own `detail`. This one has neither.
- **No captured fixture covers the catalogue route.** The chain test's population is the fixture directory: 12 files, none for `/catalog/*`. So `error-text-chain.test.ts` cannot see this route either.
- **Parity does not excuse it.** #7 shipped the same generic notice (`apps/web/app/pages/orders/place.vue:253-254`). The bullet was written into #8's contract precisely so that #7's generic-error class would be held here, and it is not limited to the pages #7 happened to get wrong.
- **What must change:**
  - Render the failing catalogue query's error through `ErrorMessage` (or `describeError`) inside the notice, keeping the type-by-hand guidance.
  - Change the test to assert the fixture's own `detail` (`fixtureDetail(...)`) in the notice.
  - Arm it: make the notice ignore the error again, and a named test must fail with a message naming the expected detail.
  - Ideally capture a real `/catalog/*` failure fixture with `scripts/capture-gateway-responses.mjs` (Orders stopped) and add it to the chain test's table.

#### Error-rendering sites (the enumeration behind B1)

Command (run in `apps/web/src`; test files and `generated/` excluded):

```
/usr/bin/grep -rnE "describeError|ErrorMessage|readProblem|\.detail\b|\.title\b|error\.message|\.problem\b|role=\"alert\"|searchParams.*error|stockShortages|isError|\berror\b.*\?" --include='*.ts' --include='*.tsx' . | grep -v '\.test\.' | grep -v '^./generated/'
```

Output: 56 lines. They reduce to the following sites where a user can see an error. Each line of the output is a use, a definition, or one of these sites.

| # | Site | Reads the problem document? | Test asserting the real `detail` |
|---|---|---|---|
| 1 | `login-form.tsx:62` (hydrated login) | yes (`ErrorMessage`) | `login-form.test.tsx:85` |
| 2 | `login-form.tsx:64` (`initialError`, the no-JS path) | yes, in the route: `api/auth/login/route.ts:45` reads `detail ?? title` server-side | `route-handlers.test.ts:104`; live (§6) |
| 3 | `orders-list.tsx:72` | yes | `orders-list.test.tsx:61` |
| 4 | `order-detail-view.tsx:51` | yes | `order-detail-view.test.tsx:106` |
| 5 | `place-order-form.tsx:337` + shortages `:339` | yes | `place-order-form.test.tsx:218,273` |
| 6 | **`place-order-form.tsx:186-190` (catalogue unavailable)** | **no — fixed text** | **none (B1)** |
| 7 | `billing-view.tsx:72` (credits) | yes | `billing-view.test.tsx:99` |
| 8 | `billing-view.tsx:157` (invoices) | yes | `billing-view.test.tsx:91` |
| 9 | `billing-view.tsx:279` (payment) | yes | `billing-view.test.tsx:261` |
| 10 | `stock-view.tsx:87` | yes | `stock-view.test.tsx:45` |
| 11 | `stock-view.tsx:180` (replenish) | yes | `stock-view.test.tsx:126` |
| 12 | `order-detail-view.tsx:98-103` (stream `gave-up` badge) | not applicable: an `EventSource` exposes no response body | n/a |

Every other hit is one of:
- a definition (`problem.ts`, `api-client.ts`, `error-message.tsx`);
- a query flag used for layout (`isError ? null : <Pager …>`);
- the stream fake;
- `order-detail.ts`'s `.detail` field of the read-model document, which is not a problem document;
- `server/gateway.ts:57`, which builds a local 502 problem.

---

## 2. Defects in the leader's own phase-16 changes

### L1 (must fix with B1) — `.env.example`'s new `WEB_PORT=3000` defeats `WEB_PORT=3010 scripts/dev-stack.sh start`

- **Cause:** `scripts/dev-stack.sh` `load_env` runs `set -a; . .env.example; . .env`. Sourcing the file overwrites any value the caller exported.
- **What changed:** until this phase `.env.example` declared no `WEB_PORT` (`git show HEAD:.env.example` has neither `WEB_PORT` nor `GATEWAY_BASE_URL`), so the caller's value survived. It no longer does.
- **Observed live:** the brief asked for `WEB_PORT=3010`. My `WEB_PORT=3010 scripts/dev-stack.sh start` printed `web answers http://localhost:3000/login` and served on 3000.
- **Reproduced without the stack:**

  ```
  WEB_PORT=3010 GATEWAY_PORT=3999 bash -c '…eval load_env from dev-stack.sh…; load_env; echo …'
  → WEB_PORT=3000 GATEWAY_PORT=3001 GATEWAY_BASE_URL=http://localhost:3001
  ```

- **Why it matters:** this machine's port 3000 is intermittently held by another application (`impl_web_app.md` §7). The script then fails with its own advice, *"set WEB_PORT to a free port (e.g. WEB_PORT=3010)"*, and that advice cannot work. The same recipe is in `impl_web_app.md` §12 and in this review's brief.
- **The same mechanism, not new here:** `GATEWAY_PORT` was already in `.env.example` at HEAD, so it was already clobbered.
- **Second effect:** `.env.example` now also sets `GATEWAY_BASE_URL=http://localhost:3001`. That makes `load_env`'s derivation `GATEWAY_BASE_URL="${GATEWAY_BASE_URL:-http://localhost:${GATEWAY_PORT}}"` dead code. A developer who sets `GATEWAY_PORT=3002` in `.env` gets a Gateway on 3002 and a web app still calling 3001.
  - #7's `.env.example:415` sets the same line, so the value is parity.
  - The dead derivation is #8's, and the new comment above it (*"Unset, it is derived from GATEWAY_PORT"*) describes a state the file itself prevents.
- **Fix, either way:**
  - have `load_env` keep values already set in the environment (capture them before sourcing and re-apply them after), or
  - comment out `WEB_PORT` and `GATEWAY_BASE_URL` in `.env.example`.

  Then re-run `WEB_PORT=3010 scripts/dev-stack.sh start` and read the port it reports.

### L2 (advisory) — the `init.sh` superseded-rule scan is narrower than its own comment says

- **The comment claims** that *"the include list keeps the breadth that bug accidentally provided"*.
- **It does not.** The buggy form scanned every file type. The new include list drops **10 tracked files**:
  - `scripts/git-hooks/commit-msg` (a shell script with no extension);
  - `.editorconfig`, `.env.example`, `.gitignore`, `.nvmrc`;
  - `OrderToCash.sln`, `test.runsettings`;
  - `infra/mssql/init/01-create-databases.sql`;
  - `infra/kafka/Dockerfile`, `infra/otel-collector/Dockerfile`.

  The list comes from an extension census of the scanned tree, followed by `git ls-files` on each file that no include pattern matches.
- **Armed:**

  | Plant | `init.sh` exit | Section 5 output |
  |---|---|---|
  | The superseded rule ``money columns `int` `` in an extension-less file `zz_probe_noext` at the root | **0** | ``[OK] no superseded rule text outside progress/`` — **survived** |
  | The same line in `zz_probe.md` (control) | fail | ``[FAIL] superseded rule text still present: "money columns `int`..."`` / `./zz_probe.md` |

  Both probe files were removed; `init.sh` then exited 0.
- **The speed fix and the `--`/`--include` ordering fix are correct**: 1.38 s measured.
- **What to change:** exclude by directory and scan every regular file, or add the missing names. At the least, correct the comment.
- **Also outside the scan, by the same include rule:** `.env.example`, which carries rule-bearing prose, has no included extension. I did not plant it separately; the extension-less probe exercises the same mechanism.

### L3 (advisory) — `.env.example`

- **Misplaced key:** the web section was inserted between `JWT_EXPIRES_IN` and `JWT_ISSUER`, so `JWT_ISSUER=order-to-cash` (`.env.example:242`) now sits under the *"Web app"* heading.
- **Stale ledger row:** the file now commits a default `WEB_SESSION_PASSWORD`. That contradicts ledger row L2 in `impl_web_app.md` (*"no committed default secret (#7 committed one)"*).
  - The code still has no default, and a dev default beside `JWT_SECRET` is this file's own convention.
  - So the row's *"#8 supplied by"* half needs one clause: the default now lives in `.env.example`, not in code, which is exactly where #7's `JWT_SECRET` sits.

---

## 3. Probes run — id 29 / id 30

All web arms followed the same procedure:
1. `cp -p` to `scratchpad/bk/`.
2. Plant the mutation (a Python replace that asserts exactly one occurrence).
3. Run the named test file(s).
4. Restore with `cp -p`, `touch`, and `cmp` (`RESTORED` printed each time).
5. Re-read the planted line (count 0).
6. Re-run green.

Vitest compiles from source, so no stale binary is possible. The production arms rebuilt with `pnpm build`, and the build was redone after restore (§3.4).

| # | Claim probed | Mutation (mine) | Result (verbatim) | Restored, green |
|---|---|---|---|---|
| R1 | bullet 5: an envelope defeats the error text (record's A10) | `relay()` wraps every ≥400 body in `{statusCode, statusMessage, data:{statusCode, data:<problem>}}` — #7's Nitro double-wrap | **24 failed / 189**. Examples: `place-order-form.test.tsx` › *renders that problem's own detail…* → `expected 'Placing the order failed.' to be 'Stock check reports 1 short line(s): …'`; `stream-route.test.ts` › *a refusing Gateway (401) is relayed…* → received the wrapped body. (The record says 23 for its envelope shape; mine differs by one body-equality case.) | yes, 189/189 |
| R2 | bullet 2: per-type de-duplication (record's A1) | `seenTimelineEntryIds = this.seenOrderUpdateIds` (one shared set) | **7 failed / 189**, including `order-stream-client.test.ts` › *R51 — the two frames of ONE fact … (order.updated first)*. The diff names the dropped frame type: `"entries": [] ` vs expected `["evt-shared-1"]`, with `updates` intact. Also `order-detail-view.test.tsx` › *R51 — … BOTH land: status AND timeline* (both orders), and the id-30 disconnect case. | yes, 9/9 in the file |
| R3 | id 30 bullet 2: the resume really uses `Last-Event-ID` (a mutation the record did not run) | on `error`, close and reopen a NEW EventSource (which carries no `Last-Event-ID`) | `id 30 — a forced mid-stream disconnect …` → `expected [ undefined, undefined, undefined ] to deeply equal [ undefined, 'cursor-1t' ]` (and the give-up case fails too) | yes, 9/9 |
| R4 | defect 2 (reconnect storm) guard, A42 | `open`'s deps `[orderId]` → `[orderId, factory]` | `use-order-stream.test.tsx` › *a factory that is a NEW function on every render still opens ONE stream* → `EventSources opened across 6 renders: expected [ …(6) ] to deeply equal [ Array(1) ]` | yes, 28/28 |
| R5 | the request-abort teardown path, A7 (the unit side of the surviving G7b plant) | delete `request.signal.addEventListener('abort', …)` | `stream-route.test.ts` › *closes the upstream Gateway connection when the incoming request is aborted* → `the upstream Gateway connection must close once the incoming request aborts: expected false to be true` | yes, 10/10 |
| R6 | defect 1 (per-bundle session key), P7, **against the production build** | holder → a module-local object; `pnpm build`; `pnpm test:integration` | build exit 0; **1 failed / 7**: *a session sealed by the login route is accepted by the pages …* → `expected '/orders → 307 /login' to be '/orders → 200'` | yes |
| R7 | defect 3 (headers held until first frame), P4, **against the production build** | preamble enqueue removed; build; integration | build exit 0; **2 failed / 7**: *delivers each frame as it is written …* and *forwards Last-Event-ID …* → `Error: no response status/headers within 3000 ms while the Gateway had not yet written a frame — the proxy is holding the response until the first upstream chunk` | yes: rebuilt, then **7/7** integration green |
| R8 | bullet 8: a lint violation **inside a `.tsx` component** fails `./quality.sh` | `<a href="/orders/">Orders</a>` added to `BillingView` (`billing-view.tsx:38`); `QUALITY_ONLY=web ./quality.sh` | **exit 1**: `38:7  error  Do not use an <a> element to navigate to /orders/. Use <Link /> from next/link instead. … @next/next/no-html-link-for-pages` → `[FAIL] apps/web: lint (+ every source file linted) failed (exit 1)` | yes; `pnpm lint` then printed `lint-coverage OK — all 91 source files (82 under src/) are linted with this app's rules.` |

### 3.1 Judgements on the claims the leader could not settle

- **G7b (the surviving gate plant) is genuinely covered, not decorative.**
  - The proxy has two independent teardown paths: `request.signal` abort, and `cancel()` of the relayed body.
  - The production probes P2 and P3 show that each alone tears down the upstream in `next start`.
  - So removing one in production *should* leave the property intact. The integration test's claim is the property (*"closes its Gateway connection when the browser goes away"*), and P6 (both removed) kills it.
  - The `request.signal` path's own existence is guarded at unit level (R5 above, re-armed by me).
  - The one window only `request.signal` covers is a client leaving before the route returns its `Response`. `route.ts` handles that with its `if (request.signal.aborted)` pre-check plus the listener, and R5 guards the listener.
- **Defect 2's guard does not run against the production build, and I accept that.**
  - The defect lived in the effect's dependency list, which is source. The minifier only supplied the trigger: a new function per render.
  - The test injects that trigger directly, and R4 shows it kills the regression deterministically.
  - The other two defects (R6, R7) are guarded against, and fail against, `next build` + `next start`, as the brief requires.
- **The real-server disconnect test really disconnects:**
  - `res.destroy()` 50 ms after the first frames;
  - the server records each request's `last-event-id` header;
  - the test asserts `[undefined, 'cursor-1t']`, i.e. a real `eventsource` package, real sockets, and a genuine resume.
  - R2 and R3 both kill it.

### 3.2 #7's test population — counted independently

- **Files.** Command, run in #7's `apps/web`:

  ```
  find . \( -path ./node_modules -o -path ./.nuxt -o -path ./.output \) -prune -o -type f \( -name '*.spec.*' -o -name '*.test.*' \) -print
  ```

  17 files: the record's 15 plus `e2e/compensation.spec.ts` and `e2e/happy-path.spec.ts`. The two e2e files are Playwright and feature 32's, excluded by path as the record states.
- **Files by content.** Every `.ts`/`.vue` file outside `e2e/` containing `expect(`: exactly the same 15.
- **Assertions.** `expect(` lines per file sum to **213**. `it(`/`test(` cases: **74**. Both match the record.
- **Classification.** §A has 213 rows: 206 PORTED, 4 NOT PORTED (deliberate), 3 NOT APPLICABLE.
- **Every named guard exists.** I parsed every PORTED row's `file › *case*` reference and checked the case title in that file. 208 references checked, 0 missing. 6 rows say *"same case"* and point at a row that is checked.
- **The non-ports are sound.** The four deliberate ones are the hydration-disabled shapes bullet 7 forbids, plus the close/reopen-reference assertion. The three not-applicable ones are the Nitro double-wrap.
- **Implicit assertions** (`getBy*`/`findBy*` outside `expect(`): 109 lines. I sampled them: they are interaction selectors and two standalone `findByText` waits whose property the next `expect` carries. No lost guard.

### 3.3 Ledger spot-check (#7's checkout, both halves)

- **L1 (contract types).**
  - `packages/contracts/scripts/generate.mts:16-25` is `generateAll` (openapi and asyncapi types).
  - `check.mts:26-50` is `checkGenerated` (regenerate into a temp dir and compare).
  - `apps/web/shared/types/gateway.ts:13` is the single `@otc/contracts` importer (content grep: the only one in `apps/web`).
  - `progress/history.md:1111` is the closure review's grep. Correct.
  - The #8 half is correct too: `types:check` is armed (G2), and the ESLint ban is enforced by `lint-coverage.mjs`'s required-rule list.
  - One precision note: #7's drift check also covered the asyncapi types. The web check does not, which is correct for a web app, and the asyncapi half is backlog id 93.
- **L6 (Retry-After).** `server/utils/gateway.ts:65-77` (`gatewayFetchWithStatus`) is correct. `grep -rn -i "retry-after" apps/web/server` returns nothing, so #7 did not forward the header, as the row says.
- **L5 and L16 also checked.**
  - `app/lib/problem.ts:41-47` unwraps `data.data`.
  - `server/utils/gateway.ts:12-20` `forwardProblem` wraps with `createError({ data })`.
  - `server/utils/session.ts:43-46` checks `expiresAt` only.
  - All correct.

### 3.4 Production build state

After R6 and R7 the production build was rebuilt from the restored sources: `build exit 0`, integration 7/7. `dev-stack.sh start` and `./quality.sh` each rebuilt it again.

---

## 4. Probes run — id 96

### 4.1 Arming, re-run by me

- **Procedure:** `cp -p` both files → plant → `dotnet build tests/Gateway.UnitTests --no-incremental` → run the named tests alone → restore → `touch` → `cmp` → re-read the lines → `--no-incremental` rebuild → full unit project.
- **Planted together, safely:** the two plants sit in different files and are read by disjoint tests. The `Configure` tests never call `CreateBuilder`, and the host tests use their own `Configure(port)` delegate, never the environment read.

| # | Family | Mutation | Named test → verbatim failure |
|---|---|---|---|
| R9 | substitution, with a sibling the record did not use | `"GATEWAY_PORT"` → `"PROJECTOR_HEALTH_PORT"` | `Configure_ReadsGatewayPort_FromItsOwnDistinctVariableName_NeverASiblingsKey` → `GatewayOptions.Port must come from GATEWAY_PORT (=13001); got 23006 — the value of PROJECTOR_HEALTH_PORT, so the read is repointed at PROJECTOR_HEALTH_PORT.` |
| R10 | deletion of the binding | `builder.WebHost.UseUrls($"http://+:{port}")` → `_ = port;` | `StartedHost_ListensOnGatewayOptionsPort_WhenNoExplicitUrlIsConfigured` → `Assert.Matches() Failure: Pattern not found in value` / `Value: "http://localhost:5000"`; `CreateBuilder_SetsUrlsToEveryInterfaceOnGatewayOptionsPort_…` → `Expected: "http://+:13001"` / `Actual: null` |

- **R9's message is not the unset-sibling false negative.** It names the substituted key and that key's own value (23006).
- **Restore:** both files `cmp`-identical, and the lines re-read (`GatewayProgramConfiguration.cs:35`, `GatewayHost.cs:136`).
- **Green:** `Gateway.UnitTests` **245/245** after a `--no-incremental` rebuild, which confirms the record's figure.

### 4.2 The precedence decision — judged sound

1. **An explicit `urls` (`--urls`, `ASPNETCORE_URLS`, `DOTNET_URLS`) wins over `GATEWAY_PORT`.**
   - It is the platform's full-URL override (scheme and host as well as port), and silently overriding it would surprise a .NET operator.
   - The record's second reason (the test-host seam) is real but is not what actually keeps test hosts safe. Every test `configure` delegate leaves `Port` null, and that alone keeps them off 3001. The precedence is a second layer. Both layers are armed (A6, A9).
2. **`ASPNETCORE_HTTP_PORTS` / `DOTNET_HTTP_PORTS` do not win.**
   - This is correct and well reasoned: the official ASP.NET Core images default `ASPNETCORE_HTTP_PORTS=8080`, so honouring it would silently defeat the one variable the project declares, and `UseUrls` already outranks it in Kestrel.
   - Guarded by A8.
3. **What the decision leaves open, and why it is acceptable:**
   - A stale `ASPNETCORE_URLS` in a developer shell silently beats `GATEWAY_PORT`. Both `dev-stack.sh`'s header and `.env.example`'s new comment say so.
   - No `appsettings*.json` or `launchSettings.json` exists under `src/Gateway` (`find` returned nothing), so neither a `Kestrel:Endpoints` section nor a `dotnet run` launch profile can override it unseen. The live Gateway's `/proc/<pid>/environ` held `GATEWAY_PORT=3001` and no `ASPNETCORE_*`.
   - `DOTNET_URLS` has no test of its own. It lands on the same configuration key as `ASPNETCORE_URLS`, so this is a note, not a gap.
   - Binding every interface (`http://+:`) matches #7's `app.listen(port)` with no host (`apps/gateway/src/main.ts:30`). The sibling health ports bind `127.0.0.1` because they are probe surfaces.

### 4.3 No in-process host collides on a real port — tested under contention, not only by reading

- **Callers read.** Every `GatewayHost.CreateBuilder` caller either passes `--urls http://127.0.0.1:0` or supplies a delegate that leaves `Port` null. Content grep over `src` and `tests`: 10 direct calls in 5 test files, plus `Program.cs`'s `GatewayHost.Build`, which calls `CreateBuilder` itself:
  - `GatewayTestHost.cs:66`
  - `DocsAndAnonymousRouteHttpTests.cs:57`
  - `OpenApiContractTests.cs:84`
  - `GatewayDispatcherRegistrationTests.cs:51`
  - the 6 `GatewayListenPortTests` hosts, which use ports 0, 13001 and 3001, or null. Only the `Port=0` one starts Kestrel.
  - `Program.cs:9` (`GatewayHost.Build`), the production path and the only caller whose delegate sets a port.
- **Run under contention.** The full `./quality.sh` ran with **port 3001 held by a dummy listener** (`python3`, pid 225543, `*:3001`). Any test host that bound 3001 would have failed with an address-in-use error. `Gateway.IntegrationTests` **70/70**, in 8 min 26 s, including all five `SagaEndToEndVerificationTests`. `Gateway.UnitTests` **245/245**, whose `StartedHost…` test starts a real Kestrel on `Port=0`. The dummy listener was stopped afterwards.

### 4.4 `dev-stack.sh` as the manual-test entry point

- **No workaround left, stale comment gone.**
  - `grep -n ASPNETCORE_URLS scripts/dev-stack.sh` returns only the new header comment (*"An ASPNETCORE_URLS exported in your shell overrides it"*), which is true.
  - The old *"does not read GATEWAY_PORT"* sentence is gone.
  - The `web` branch of `cmd_start_service` now `return`s.
- **`start` brought everything up.** Infra, one build, seed, six services, the Gateway ready on **3001** (`ss`: `*:3001 OrderToCash.Gat`), and the web app built and started.
  - The port was **3000, not the 3010 I asked for** (L1).
- **`stop` stopped everything.** It printed seven `[OK] … stopped` lines and `[OK] nothing left running`. Independently, afterwards:
  - `ps` showed no `OrderToCash.*`, `next-server`, `dotnet run` or `next start` process;
  - `ss -ltnp` showed no listener on 3000–3010.
- **Advisory: the script's own leftover check cannot see two of the three process kinds it starts.**
  - `pgrep -f "dotnet run --no-build --project src/|next (dev|start) --port"`, run while the stack was up, matched only the six `dotnet run` parents.
  - It did not match the six `OrderToCash.*` executables they spawn.
  - It did not match the web server: Next.js rewrites its process title, and `/proc/164016/cmdline` reads `next-server (v16.3.5)`.
  - The kill itself is sound (`setsid`, then a process-group `kill -- -$pid`, and every child shared its parent's PGID), so nothing leaked. But the `[OK] nothing left running` line is only true because the kill worked, never because the check looked.
  - Checking the recorded PGIDs (`pgrep -g`) or the ports would make it a real check.

---

## 5. Live verification (real stack, headless Chrome over CDP, driven from `scratchpad/cdp.mjs` and `cdp2.mjs`)

| Brief item | Observed |
|---|---|
| Bad credentials → the Gateway's own `detail` | hydrated form: `username or password is incorrect`. No-JS form post: `303` → `location: …/login?error=username+or+password+is+incorrect`, and the SSR page renders `data-testid="login-error">username or password is incorrect</p>` |
| Place an order the stock check rejects → shortages render | quantity 999999: error `Stock check reports 1 short line(s): PRD-0001 (requested 999999, available 500)`; shortages `PRD-0001: requested 999999, only 500 available` |
| Order detail → live timeline updates | (a) The demo order ORD-000024 opened with status `stock_reserved`, stream `Live`, 4 entries. 0.5 s later it read `cancelled` with 5 entries (the compensation path), over 1 stream connection. (b) ORD-000018 (`invoiced`, `Live`, 6 entries), payment registered from the page's origin (`201 accepted`): `paid` with 8 entries at 0.7 s, `completed` with 9 at 1.0 s. **SSE frames received after the payment**, from CDP `Network.eventSourceMessageReceived`: `order.updated:533f5c3a, timeline.appended:533f5c3a, order.updated:bd282edb, timeline.appended:bd282edb, order.updated:eb3496a2, timeline.appended:eb3496a2`. That is three facts, each with both frame types sharing one `eventId`, and all landed. **`GET /api/orders/{id}` requests after the payment: 0**, so the update came from the stream, not the backstop. Stream connections opened: 1 |
| Stop, then confirm every process gone before any build | see §4.4 |

---

## 6. The test-matrix text (R55 web half)

- **Accurate as proposed.** Every named file and case exists with exactly that name:
  - `order-detail-view.test.tsx:47` *a 202 renders the NAMED waiting state (not an error, not a 404, not a bare spinner), retries on the Gateway's schedule, then shows the order*;
  - `order-detail-view.test.tsx:157` *R51 — the two frames of one fact share an eventId and BOTH land: status AND timeline (%s)*. This is the right file: the similarly worded *"… ONE fact … BOTH are applied (%s)"* is `order-stream-client.test.ts:99`, a different case;
  - `order-stream-client.test.ts:156` *id 30 — a forced mid-stream disconnect is resumed with Last-Event-ID: the missed frame arrives once, nothing is lost or duplicated*, which the proposed text abbreviates with an ellipsis;
  - `apps/web/tests-integration/stream-proxy.test.ts` exists.
- **Three recommendations for the leader when applying it:**
  1. **Add #7's two-halves caveat** (#7 `specs/shared/test-matrix.md:185`). The sketch says *"renders the waiting state … and fills in from the update stream"*. No single #8 case walks from pending to filled *by a stream frame*.
     - The 202 case fills in by re-reading. By design no stream is open during a 202 (`order-detail-view.test.tsx:61` asserts 0 EventSources), exactly as in #7's `[id].vue:57-60`.
     - The stream half is carried by the R51 case.
     - Two cases prove the two halves, and the row should say so, as #7's does.
  2. **Update the summary row.** `test-matrix.md:78` reads `R50 – R55 | 6 | 5 | 0 | 1` and should become `6 | 6 | 0 | 0`.
  3. **Retire the stale prose.** `test-matrix.md:83` says *"One further row is not yet green: R55's web half … ids 29/30, both still pending"*.
  - Items 2 and 3 are only true once id 29 closes, so apply all three at that close, not now.

---

## 7. Routing — root cause in `specs/shared/`

- **The gap:** `openapi.yaml:56` promises client formatting *"from `currency.decimalPoints`"*, and no REST schema carries that field (`impl_web_app.md` §2, verified: `grep -n -i decimalPoints specs/shared/openapi.yaml` → the one prose hit).
- **Why it needs an artefact:** id 29's bullet 3 is still met (ISO 4217 exponents, armed by A14), so no criterion fails. But the shared spec contradicts itself, and today the only trace is a sentence in `progress/current.md` (*"Awaiting a maintainer decision: SA-5 …"*). That sentence will not outlive the phase.
- **What the leader should file:**
  - a **numbered backlog entry** for the contradiction, and
  - an **SA-5 proposal** written down with the two options: add `decimalPoints` to a REST schema, or reword `openapi.yaml:56`.

  Each must cite the grep above and #7's own hard-coded `/ 100` (`apps/web/app/lib/money.ts:11,62`), which shows #7 never honoured the sentence either.
- **Also for the leader to route:** B1 above, if re-dispatch is not immediate, and advisories L2 and L3 and §4.4.

---

## 8. CHECKPOINTS.md walked

**C1 — harness**
- [x] `AGENTS.md`, `CLAUDE.md`, `CHECKPOINTS.md`, `feature_list.json`, `init.sh` exist.
- [x] `progress/current.md`, `progress/history.md` exist.
- [x] `.claude/agents/` holds the five, plus `premise_checker` and `suite_runner`.
- [x] Every agent declares a model or states that it inherits: 4 carry `model:`; leader, reviewer and spec_author state inheritance in their descriptions.
- [x] `./init.sh` exits 0 (run three times this session; 1.38 s).

**C2 — state**
- [x] At most one `in_progress`: 0, before and after.
- [x] Every status is valid (`done` / `in_review` / `pending`).
- [x] Every `done` feature has passing tests: `./quality.sh` in full, §9.
- [ ] `progress/current.md` describes the active session. It still says *"both `in_review`, one combined review dispatched"*; the leader updates it with this verdict.
- [x] No `blocked` features.

**C3 — architecture**
- [x] Domain purity: `Architecture.Tests` passed inside the full run (§9).
- [x] No cross-service database access. `apps/web` talks only to the Gateway over HTTP (`server/gateway.ts`, `stream-proxy.ts`), and id 96 touches only the Gateway host.
- [x] No shared runtime code beyond `SharedKernel`, `Contracts` and `Cqrs`. `apps/web` is a standalone pnpm project with no `packages/` workspace.
- [x] No `Domain/` reference to `OrderToCash.Cqrs`, according to the architecture suite.
- [x] `SharedKernel` still has zero `PackageReference` entries (no change this phase; the architecture suite is green).
- [x] No `decimal` in domain arithmetic (no domain change). The web app's money is integer minor units, and ESLint bans `parseFloat`/`toFixed` (G3a, A13).
- [x] Every interaction is classifiable: browser → BFF → Gateway is HTTP/SSE, with no new Kafka or NATS interaction.
- [x] No stray debug logging: `no-console` is an error in `apps/web`, and the one `console.warn` (the session-key warning) is deliberate.

**C4 — verification**
- [x] `./quality.sh` passes: exit 0 (§9).
- [x] Domain tests are pure (no domain change; architecture suite green).
- [x] Integration tests use real containers. For the web, the production-build tests use a real `next start` against a local HTTP Gateway double, which is the right level for proxy behaviour. The live run covered the real Gateway.
- [ ] Coverage thresholds: **not applicable to this phase, and unchanged by it.**
  - Section 4 reports per-project line coverage; it enforces nothing until feature 34, as before.
  - Web line coverage is 92.71%, reported and not enforced, as the record states.
- [x] No Jest. Vitest runs the web tests; `@testing-library/jest-dom` is a matchers-only package, the same one #7 used (`apps/web/package.json:39` in #7).

**C5 — close**
- [x] No suspicious untracked files. `apps/web`'s `.next`, `coverage`, `node_modules`, `next-env.d.ts` and `tsconfig.tsbuildinfo` are ignored. My probe files were removed, and my backups are in the scratchpad.
- [x] `progress/history.md` has id 96's entry with its effort record. There is none for id 29, which is rejected.
- [x] `feature_list.json` reflects this verdict: 96 `done`; 29 `in_review` (per the brief); 30 `pending`.
- [ ] The human has been told what was done and how to test it: the leader's job at phase close.
- [x] Claude did not commit. I ran no git command that writes.

**C6 — SDD:** not applicable. All three entries are `sdd: false`. Each carries its ledger in its `impl_*.md`, as `CLAUDE.md` requires.

**C7 — reuse fidelity**
- [x] `specs/shared/` is byte-identical to #7's except `test-matrix.md`. `diff -rq specs/shared <#7>/specs/shared` → only `test-matrix.md` differs.
- [x] No silent fork. The `decimalPoints` contradiction is disclosed and must be routed (§7).
- [x] The `R<n>` claims are genuine (R51, R55, R47/R48 web evidence probed in §3 and §5).
- [ ] n8n and the black-box API script: not exercised by this phase (feature 31 and later).
- [x] Effort records are honest: §10 and `history.md`.
- [ ] README benchmark: a wrap-up item.

---

## 9. The full suite, once

```
./quality.sh   (stack stopped; port 3001 held by a dummy listener for the whole run)
quality exit 0
```

Sections 1–2 (.NET format and build):
- `dotnet format --verify-no-changes`: clean.
- `dotnet build`: 0 warnings, 0 errors.

Section 3 (.NET tests), passed / failed / skipped per project:

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| SharedKernel.UnitTests | 50 | 0 | 0 |
| Cqrs.UnitTests | 23 | 0 | 0 |
| Contracts.UnitTests | 24 | 0 | 0 |
| Gateway.UnitTests | 245 | 0 | 0 |
| Notifications.UnitTests | 111 | 0 | 0 |
| Billing.UnitTests | 262 | 0 | 0 |
| Orders.UnitTests | 500 | 0 | 0 |
| Fulfillment.UnitTests | 146 | 0 | 0 |
| Seed.UnitTests | 44 | 0 | 0 |
| Projector.UnitTests | 120 | 0 | 0 |
| Architecture.Tests | 50 | 0 | 0 |
| Seed.IntegrationTests | 6 | 0 | 0 |
| Notifications.IntegrationTests | 29 | 0 | 0 |
| Fulfillment.IntegrationTests | 64 | 0 | 0 |
| Billing.IntegrationTests | 90 | 0 | 0 |
| Projector.IntegrationTests | 68 | 0 | 0 |
| Gateway.IntegrationTests | 70 | 0 | 0 |
| Orders.IntegrationTests | 155 | 0 | 0 |
| **Total (18 `Passed!` lines, summed)** | **2 057** | **0** | **0** |

- **The count reconciles exactly.** The web-app record's full run had 2 049. Id 96 adds 8 (+2 in `GatewayProgramConfigurationTests`, +6 in `GatewayListenPortTests`): 2 049 + 8 = 2 057.
- **Section 4** reports per-project line coverage, 18 reports, and is not enforced.

Section 5 (web), every step `[OK]`:
- install (frozen lockfile);
- OpenAPI types match `openapi.yaml`;
- lint plus `lint-coverage`;
- typecheck;
- Vitest **16 files, 189 passed** (line coverage 92.71%);
- production build (Next.js 16.3.5; 20 routes);
- integration tests against the production build, **1 file, 7 passed**.

---

## 10. Effort (for the record; id 96's entry is in `history.md`)

- **id 29 / 30 implementer:**
  - 1 session (the record's own claim);
  - brief `brief_p16_web_app.md` written 16:27:20;
  - arming artefacts 17:16–18:11 (`arm.py` 17:16, `arm_results.json` 18:11:38);
  - record first written ≈18:17.
  - So ≈ **1 h 50 min**, which matches the record's ≈16:30 → 18:20.
  - #7's baseline for the same app: **14 implementer passes and 7 review passes, 2 REJECTED**.
- **id 96 implementer:**
  - 1 session, from the filesystem: first arm backup 18:25:22, `id96_enum.txt` 18:29:42, arms through 18:39:21, live start/stop 18:42–18:43, record 18:44:25;
  - dispatched no earlier than the web record (≈18:17).
  - So ≈ **20–27 min**.
- **Review (this session, all three entries):** brief 18:28:33 → ≈19:25 (full `./quality.sh` 19:03 → 19:19). About **1 h** of wall-clock, of which about 16 min was the contended full-suite run and about 10 min the stack start, the browser drive and the stop.

---

## 11. What must change before id 29 is re-reviewed

1. **B1:** make the catalogue-unavailable notice show the failing query's own `detail`, falling back to `title`.
   - Assert the fixture's `detail` in `place-order-form.test.tsx`, replacing the *"only the failure matters here, not its words"* case.
   - Arm it (make the notice ignore the error again → a named test fails naming the expected text) and record the arm.
   - Preferably capture a real `/catalog/*` failure fixture and add it to `error-text-chain.test.ts`'s table.
2. **L1** (leader-owned, same round): make `WEB_PORT=3010 scripts/dev-stack.sh start` actually serve on 3010.
   - Either `load_env` keeps values already exported, or `.env.example` stops setting `WEB_PORT` and `GATEWAY_BASE_URL`.
   - Re-run and read the reported port.
   - Then correct the recipe wherever it is quoted, if the mechanism changes.
3. **The re-review needs only:**
   - B1's arm and the new test;
   - one `pnpm exec vitest run` count;
   - one `dev-stack.sh start` on a non-default `WEB_PORT`.

   Everything else in this record stands unless those changes touch it.

Id 30 closes with id 29 (its own notes). Nothing in its two bullets needs changing.

**Leader routing, not blocking:**
- §7 (the `decimalPoints` contradiction → backlog entry and SA-5 proposal);
- L2 (`init.sh` scan breadth);
- L3 (`.env.example` layout and ledger row L2);
- §4.4 (`dev-stack.sh stop`'s leftover check cannot see `next-server` or the `OrderToCash.*` children);
- §6 (the three matrix edits, at id 29's close).

**State after this review:**
- `feature_list.json`: id 96 `done` (one line changed, file parses); id 29 `in_review` (left alone, as the brief directs for a rejection); id 30 `pending`.
- Counts: 83 done / 1 in_review / 11 pending / 0 in_progress.

---

# Re-review, fix round 1

**Reviewer session:** 2026-09-16, ≈19:35 → 20:20 (first backup 19:39, full `./quality.sh` 19:50 → 20:14, last probe 20:15). Scoped as briefed: I probed the fix and the leader's fixes, and ran the full gate once. Round 1's probes (§3–§5) were not repeated; nothing in this round touched what they proved.

## Verdict

| Entry | Verdict | Status after this review |
|---|---|---|
| **id 29 `web_app`** | **REJECTED**, fix round 2. B1 itself is closed and the product meets bullet 5 today. But the population guard this round presents as the class fix is defeated twice (D1, D2), by working exploits, with the whole suite green. | left `in_progress` (unchanged) |
| **id 30 `web_component_tests`** | bullets still met; closes with id 29 | left `pending` (unchanged) |

The leader's L1, L2 and L4 fixes are verified live, and L3 is correct. Full `./quality.sh`: exit 0 with the stack stopped. .NET **2 057/0/0** in 18 projects; web Vitest **17 files, 211 passed**; production-build integration **7/7**.

## RR1. B1 — closed (probe 1)

| # | Mutation (mine) | Result (verbatim) | Restored |
|---|---|---|---|
| RB1a | The notice's `ErrorMessage` map is replaced by `<p className="text-sm text-destructive">The catalogue could not be loaded — enter codes by hand.</p>` (the plain round-1 shape, no test id) | **4 failed / 33**. Both catalogue cases fail with `Unable to find an element by: [data-testid="catalog-error"]`. Guard: `error-rendering site(s) with no entry in SITES` → `features/orders/place-order-form.tsx \| <p> styled as error \| "The catalogue could not be loaded — enter codes by hand."`; and `SITES entries the source no longer has` → `… \| <ErrorMessage> \| catalog-error` | `cmp` identical; 33/33 |
| RB1b | `<p key={String(error)} data-testid="catalog-error">{CATALOG_FALLBACK}</p>` (keeps the test id, so the text assertion is reached) | **2 failed / 17**. `AssertionError: the catalogue notice must show the Gateway's own detail` / `-   "RPC call to \"catalog.reference.list\" failed: no responder is subscribed to this subject.",` / `+   "The catalogue could not be loaded.",` | `cmp` identical; 17/17 |

**The failure names the expected Gateway `detail`.** B1's site fix is guarded.

**The four fixtures are real captures:**
- each has its own `correlationId` (`e983dc0b…`, `88d4e6e9…`, `f436bbdb…`, `4918b6e2…`);
- `capturedAt` values are 17:27:36Z–17:27:43Z, matching the 19:27 local file mtimes (UTC+2);
- `capturedFrom` matches each route (`GET /catalog/{retailers,companies,products}`, `GET /orders?page=0`);
- the twelve older fixtures keep their 16:53/16:57 mtimes.

**Live check:**
- With the stack up and Orders stopped, `GET http://localhost:3010/api/catalog/products` with a real session returned `503` and `detail` `RPC call to "catalog.reference.list" failed: no responder is subscribed to this subject.`, identical to the fixture.
- With Orders up, it returned `200`.

**Orders is the only responder (enumerated).**
- Command: `find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -nE "SubscribeCoreAsync|SubscribeAsync|\.Subscribe(Core)?\b"`. It returned 12 lines. Classified:
  - `Orders/Presentation/OrdersCreateResponder.cs:69,77,85`: `orders.create`, **`catalog.reference.list`** (`:77`, handled at `:149`), `orders.cancel`.
  - `Billing/Presentation/BillingRpcResponder.cs:124`: a variable subject, bound at `:63-68` to the six `CreditSubjects`/`InvoiceSubjects` constants.
  - `Fulfillment/Presentation/StockRpcResponder.cs:118`: a variable subject, bound at `:57-62` to the six `StockSubjects` constants.
  - `Gateway/Infrastructure/Messaging/NatsStreamSignalSubscriber.cs:72`: `readmodel.order.updated.*` / `readmodel.timeline.appended.*` (`:53-54`), which cannot match `catalog.reference.list`.
  - `StreamEndpoints.cs:77`: an in-process hub, not NATS.
  - `ReadModelBootstrap.cs:41`: a comment.
  - three `KafkaFactStreamSubscriber` `consumer.Subscribe(topics)` lines, which are Kafka, not NATS.
- The subject literal appears in `src` only as `Gateway/…/GatewaySubjects.cs:15` and `Orders/…/RpcSubjects.cs:35`.

## RR2. The population guard — defeated twice (probe 2)

`src/test/error-sites.ts` derives sites from five **expression-level** shapes:
- `<ErrorMessage>`;
- `cond ? a : b`;
- `cond && <jsx>`;
- a JSX text child;
- an element styled as an error.

The question the brief asked was whether a site doing the wrong thing removes itself from the population. **It does no longer by using or avoiding `describeError`/`ErrorMessage`**, which is the escape B1 used, and that part of the fix is real. It still escapes by **statement shape** and by **how the proof case is written**.

### D1 (blocking) — the plainest React error branch is not a site

**Probe.** A new component file `src/features/stock/stock-banner.tsx`, one shape per run. Each calls `useStock(f)` and renders the generic text `Something went wrong`, without `describeError` or `ErrorMessage`. The file was deleted after each run; the guard was then green at 16/16, and `ls src/features/stock` showed only the original files.

| Probe | Shape | Guard (`error-rendering-sites.test.ts`) |
|---|---|---|
| N5 (control) | `<div>{stock.isError ? <p data-testid="probe">Something went wrong</p> : null}</div>` | **1 failed / 16** → `+   "features/stock/stock-banner.tsx \| stock.isError ? then \| probe",` |
| **N1** | `if (stock.isError) return <p data-testid="probe">Something went wrong</p>; return null;` | **16 passed — survived** |
| **N3** | `switch (stock.status) { case 'error': return <p …>Something went wrong</p>; default: return null; }` | **16 passed — survived** |
| **N8** | `const ok = stock.isSuccess \|\| stock.isPending; return <div>{ok \|\| <p …>Something went wrong</p>}</div>;` | **16 passed — survived** |
| N2 | `return stock.isSuccess ? null : <p …>Something went wrong</p>;` | 16 passed — survived |
| N4 | `return !stock.data && !stock.isPending ? <p …>…</p> : null;` | 16 passed — survived |
| **N6** | `src/app/(app)/stock/error.tsx`: `'use client'; export default function StockError() { return <p data-testid="probe">Something went wrong</p>; }` (Next.js's own error-boundary file convention) | **16 passed — survived** |

**Why this blocks.**
- N1 names `isError` **literally** and is the most common way a React component renders a failed query. It is not derived because an `if` statement is not one of the five shapes.
- It is also **not among the residual limits** FR1.8 discloses (deep hook chains, renamed props, non-JSX rendering).
- The module's own doc comment claims *"a site is any JSX that is rendered … under an expression that mentions one of those"*. N1, N3 and N8 each render JSX under such an expression.
- This is the guard the round presents as closing the class B1 belonged to, so its first escape being the plainest shape is the finding this probe was designed to find.

**The current source has no instance** (so bullet 5's *behaviour* holds today).
- Command: `find src \( -name '*.tsx' -o -name '*.ts' \) -not -path 'src/generated/*' -not -path 'src/test/*' -not -name '*.test.*' -print0 | xargs -0 grep -nE "return +(\(|<)|case +'|\|\| *<|\?\? *<|isSuccess|status ===|error\.tsx|not-found"`. It returned 62 lines. Classified:
  - 56 are single top-level `return (`/`return <` of a component, or a `.map` callback's `return (`;
  - `status-badge.tsx:5-6` returns a variant string;
  - `providers.tsx:8` is the 401 redirect;
  - `use-order-detail.ts:25` is the 202 branch in a hook;
  - `gateway.ts:69-70` is server relay code;
  - `billing-view.tsx:198,203` are ternaries on invoice status, not on a failure.
- `find src/app -name error.tsx -o -name global-error.tsx -o -name not-found.tsx` returned nothing.

So D1 is a guard defect, not a product defect.

### D2 (blocking) — a proof case is satisfied by incidental lines, not by its assertion

`caseEvidence` checks ingredients anywhere in the case body: `fixtureDetail` is called, the test-id string appears, and the fixture names appear. It never checks that **the assertion made on that test id compares against `fixtureDetail(…)`**. The CATALOG case contains two incidental lines (`expect(fixtureDetail('catalog-retailers-…')).toBe(fixtureDetail('catalog-products-…'))`, and the same for companies). Those lines alone satisfy `callsFixtureDetail`, whatever the site assertion says.

**Probe VAC2**, two edits:
- source: `error={error}` → `error={null}` in the catalogue `ErrorMessage`, so every catalogue failure now renders the generic fallback, which is exactly B1's behaviour;
- test: the CATALOG case's text assertion becomes `await waitFor(() => expect(within(notice).getAllByTestId('catalog-error')).toHaveLength(1));`, which is round 1's count-only shape.

**Result: 1 failed / 33.**
- The guard (**16/16**) and the proof case itself both passed.
- The only failure was the **unlisted** sibling case *two DIFFERENT catalogue failures are both shown …* → `-   "RPC call to \"catalog.reference.list\" failed: no responder is subscribed to this subject.",` / `+   "The catalogue could not be loaded.",`.
- That sibling is not in `SITES`, so the guard does not protect it: weakening it too would leave the suite fully green with B1 restored.
- Both files were restored (`cmp` identical; `grep -c "error={null}"` → 0).

**Why the implementer's A8 did not find it.** A8 removed the assertion line and put its ingredients into a comment, so the case had no other `fixtureDetail` call. Here the ingredients live in real code that asserts something else. That is attack 9 (the closer half is satisfied and the premise half is stale), and it is the exact assertion shape round 1's B1 rested on.

### The three disclosed residual limits (FR1.8) — each judged acceptable as a disclosed limit

- **Deep hook chains** (two levels): no hook renames an error state today. The one-level expansion catches a direct rename (A6), and the reason given for stopping there (`apiGet → ApiError` would mark every data branch) is correct.
- **Renamed props across components:** `ReadyOrder` receives `connection`, but the gave-up branch is derived through its `'gave-up'` literal. No other component receives a failure from a parent.
- **Non-JSX rendering:** no toast library is a dependency (`package.json`), and there is no `alert(`/`document.title` in `src`.

Leave these as disclosures. D1's shapes are different in kind: they are ordinary single-component code, not cross-component flow.

The success-negated shapes (N2, N4) cannot be found by an error-word match without marking every data branch. It is acceptable to **disclose** them rather than close them, provided the disclosure is added.

### Defeat list, re-run by me

| Attack | Result |
|---|---|
| 1 | Deleting the site's error → RB1a/RB1b caught it. |
| 2 | Corrupting the text → RB1b caught it. |
| 3 | The implementer's A7/A9 are credible. I did not re-run them. |
| 4 | Comments are not nodes, so A8 holds. |
| 5 | TypeScript has no preprocessor. `if (false)` over-includes, which is correct. |
| 7 | Dropping an optional element: the implementer's A3 is credible. I did not re-run it. |
| 8 | A11 holds. |
| **9** | **Defeated: D2.** |
| New shape | **Statement-level rendering (early return / `switch` / `\|\|`) and file-convention rendering (`error.tsx`): D1.** Propose it as a row for the defeat-list table: *"render the failure from a statement, not an expression"*. |

## RR3. Sibling fix and the allowed mismatches (probe 3) — confirmed

- **`orders-list.test.tsx`** now serves `orders-list-bad-page-400` (`capturedFrom: GET /orders?page=0`).
- **`route-handlers.test.ts:201-207`** asserts only three things: status `401`, `response.text()` equal to the fixture's **body bytes**, and a cleared `otc_session` cookie. It asserts no rendered words.
- **`stream-route.test.ts:136-142`** asserts only three things: status `401`, the `content-type`, and byte-equal body. `:34` only serves it. It asserts no words.
- **The route-independence claim holds.** The 401 body's `detail` is `missing bearer token`, and its `title` is `Missing, expired or invalid bearer token`. Neither names a route.
- **Enumeration count.** The FR1.5 command returns **56** lines, the record's figure.

## RR4. The leader's own fixes (probe 4)

- **L1 — verified live.**
  - `WEB_PORT=3010 scripts/dev-stack.sh start` exited 0 and printed `[OK]   web answers http://localhost:3010/login`.
  - `ss -ltnp` showed `*:3010 next-server (v1…)`, `*:3001 OrderToCash.Gat`, and `127.0.0.1:3002–3006` held by the five service health ports.
  - `curl` on `:3010/login` returned `200`.
  - `logs/dev-stack/ports` recorded `3001 3010 3002 3003 3004 3005 3006`.
  - **Minor residue, not blocking:** `export -p` prints read-only variables as `declare -rx`, which the `sed` does not rewrite. A read-only exported variable would be re-declared function-locally, which is harmless. I did not probe this; it is noted only.
- **L2 — verified.**
  - I appended `# The JSON wire shape must match #7 byte for byte` to `scripts/git-hooks/commit-msg`.
  - `./init.sh` then exited **1**, with exactly one failure: `[FAIL]  superseded rule text still present: "The JSON wire shape must match #7 byte for byte..."` / `./scripts/git-hooks/commit-msg`.
  - Restored with `cp -p` (mode kept): `cmp` identical, `grep -c "byte for byte"` → 0.
  - `./init.sh` then exited **0**, printing `[OK] no superseded rule text outside progress/`.
- **L3 — correct.**
  - `JWT_ISSUER` is at `.env.example:219`, above the web heading at `:221`.
  - `GATEWAY_BASE_URL` is commented out (`:244`). The app derives it from `GATEWAY_PORT` itself (`apps/web/src/server/config.ts:10-12`), and so does `load_env`.
  - **Still open, advisory:** the second half of round 1's L3. `impl_web_app.md` §4 ledger row L2 still reads *"no committed default secret"*, while `.env.example:242` commits `WEB_SESSION_PASSWORD`. The implementer can fix that clause in round 2.
- **L4 — verified by arming.** The arms ran from a script file (reason below).

  | Plant | `stop` output | Exit |
  |---|---|---|
  | none (stack already stopped) | `[OK]   nothing left running, no port this stack bound is still held` | 0 |
  | `127.0.0.1:3010` held by `python3 -m http.server`, with `3010` in `logs/dev-stack/ports` | `[WARN] ports this stack bound are still held: 3010`; the ports file is kept | 1 |
  | a process whose argv[0] is `/tmp/x/src/Orders/bin/Debug/net10.0/OrderToCash.Orders` | `[WARN] processes still alive after stop: 588897` (the planted pid) | 1 |
  | the same, but under `…/tests/Orders.IntegrationTests/bin/…/OrderToCash.Orders.dll` (a test host) | `[OK]   nothing left running …` | 0 |

  - The real `stop` after the live run printed six `[OK] … stopped` lines and the clean `[OK]` line. `ss` then showed nothing on 3000–3010, and `pgrep` found no `OrderToCash`, `next-server` or `dotnet run` process.
  - **Advisory: the name check can count its caller.** The first two arm runs reported an extra pid. `/proc/<pid>/cmdline` showed it was **my own tool shell**, whose command line contained the planted path in a heredoc. That is `CLAUDE.md`'s *"a check … is only trustworthy if it cannot count itself"* shape, one level out: `pgrep -f` excludes itself, not its parent. A human's interactive `bash` never matches, so it is harmless. An agent that quotes a service path in the same command as `stop` gets a false `[WARN]` and exit 1. Excluding `$$` and `$PPID` would close it.

## RR5. Counts and the full gate (probe 5)

- **`./quality.sh`**, full, stack stopped, run once (19:50 → 20:14): **exit 0**, ending `[OK] quality.sh finished`.
- **.NET:**
  - `dotnet format --verify-no-changes` clean; `dotnet build` with `0 Warning(s)`.
  - 18 `Passed!` lines, summed: **2 057 passed / 0 failed / 0 skipped**, identical per project to round 1's §9 table. That is expected: the fix round touched no .NET code.
- **Web**, every step `[OK]`:
  - install;
  - OpenAPI types;
  - lint (`lint-coverage OK — all 93 source files (84 under src/)`);
  - typecheck;
  - Vitest **Test Files 17 passed (17) / Tests 211 passed (211)**, coverage Statements 91.61% (776/847), Lines 92.74% (678/731);
  - production build;
  - integration **7 passed (7)**.
- **Reconciliation:** 189 + 22 = 211, and 16 + 1 new file = 17. The guard file runs **16** cases (observed in every guard run above), and `place-order-form.test.tsx` runs **17**. I did not separately re-count the +4 chain rows and the +1 login case; the total closes exactly with them.

## RR6. The test-matrix text (R55 web half) — for the leader to apply at id 29's eventual close

- **File and case names:** all exact.
  - `order-detail-view.test.tsx:47` and `:157` (`it.each` with `(%s)`);
  - `order-stream-client.test.ts:156` (abbreviated with an ellipsis, which is acceptable);
  - `apps/web/tests-integration/stream-proxy.test.ts` exists.
- **Not yet accurate: the caveat is missing.** The quoted text does **not** carry the two-halves caveat I asked for, although the brief says it does. Add, after the case list, something equivalent to #7's `test-matrix.md:185`: *"**Caveat, stated rather than glossed:** the sketch has two halves — renders the waiting state AND fills in from the update stream — and no single case walks pending → filled by an update frame (by design no stream is open during a 202: the 202 case asserts zero EventSources and fills in by re-reading). The first half is the 202 case; the second is carried by the R51 case and the id-30 resume case. Two cases prove the two halves."*
- **The same cell (`test-matrix.md:183`)** also needs two changes:
  - its leading `TODO —` becomes `DONE —`;
  - its closing sentence *"**The web half remains unproven** — owed to `apps/web` (features 29/30, …, both still `pending`), not built by this feature."* is replaced by the web-half text.
- **Row 78** becomes `| 7. \`projector_read_model\` | R50 – R55 | 6 | 6 | 0 | 0 |`.
- **Row 80 (Total)** must move with it: `| **Total** | **R1 – R63** | **63** | **59** | **4** | **0** |`.
- **Row 83:** delete its last sentence, *"One further row is **not yet green**: `R55`'s web half (`web/components/order-detail-pending.spec`) is owed to `apps/web` (features `web_app`/`web_component_tests`, ids 29/30, both still `pending`) and built by neither."*, and replace it with *"No row is **not yet green**: the last, `R55`'s web half, closed with features `web_app`/`web_component_tests` (ids 29/30)."*
- **The paragraph's opening count** (*"Four rows are currently scoped"*) is unchanged.

## CHECKPOINTS.md — changes from round 1's §8 only

- **C2** [x] At most one `in_progress`: exactly one, id 29. Counts are 83 done / 1 in_progress / 12 pending.
- **C2** [ ] `progress/current.md`: the leader updates it with this verdict.
- **C4** [x] `./quality.sh` passes (RR5). [x] No Jest.
- **C4** [ ] **Tests are real — not met for the guard.** D1 and D2 are working exploits against the class guard.
- **C5** [x] Probe files removed. `src/features/stock/stock-banner.tsx` and `src/app/(app)/stock/error.tsx` are absent, and `logs/dev-stack/ports` was removed. Backups are in the scratchpad, and I ran no git command that writes.
- **C5** [ ] No effort record for ids 29/30, because they were rejected.
- **C7** [x] `specs/shared/` untouched by this round.
- Every other box stands as in §8.

## What must change before fix round 2 is re-reviewed

1. **D1:** derive statement-level sites. Arm each shape with its own new-file probe (N1, N3, N8, N6 above); each must fail naming its key.
   - An `if` statement whose condition matches and whose then- or else-branch contains JSX, which covers early returns.
   - A `switch` whose discriminant or case expression matches and whose clause contains JSX.
   - `||` and `??` with a JSX right operand.
   - Files named by Next.js's error conventions (`error.tsx`, `global-error.tsx`, `not-found.tsx`) as sites **by path**.
   - Either close N2/N4 (success-negated branches) or add them to FR1.8 as a disclosed limit with the reason.
   - One way to close that class is a complementary instrument: every JSX text or string literal rendered in `src/` that matches a failure vocabulary (`wrong|fail|could not|cannot|unable|unavailable|error`) is a listed site. It is optional, and it catches generic text whatever its condition is named.
2. **D2:** make a `FixtureProof` require an `expect(…)` whose subject expression names the site's test id **and** whose matcher argument contains `fixtureDetail(…)`. The argument may be a same-case variable bound to a `fixtureDetail(…)` call, or an `it.each` row.
   - Ingredients elsewhere in the case must not count.
   - Arm with VAC2 exactly (source `error={null}`, CATALOG assertion → `toHaveLength(1)`): the guard must fail naming the CATALOG proof.
   - Then run the same check against all 12 proofs, not only CATALOG. The unit is the proof case.
3. **Advisory, same round if cheap:**
   - correct ledger row L2's *"no committed default secret"* clause (round 1's L3);
   - leader-owned: `dev-stack.sh stop`'s name check should exclude its caller (`$$`/`$PPID`).
4. **Re-review needs only:**
   - the D1 and D2 arms re-run;
   - one Vitest count;
   - `QUALITY_ONLY=web ./quality.sh`, since no .NET change is expected.

**Routing (leader):**
- the new defeat-list row (statement-level rendering);
- the three FR1.7 proposed rows;
- the RR6 matrix edits, applied at id 29's close;
- §7's `decimalPoints` backlog entry and SA-5 proposal, if not yet filed.

**State after this review:** `feature_list.json` is untouched by me (id 29 `in_progress`, id 30 `pending`). No effort record was appended.

---

# Re-review, fix round 2

**Reviewer session:** 2026-09-16, ≈22:31 → 22:48 (baseline sweep run 22:32:25; last probe 22:43:29; `QUALITY_ONLY=web ./quality.sh` finished 22:44:40; live check 22:45–22:47). Scope as briefed: I judged the behavioural sweep on its own terms, re-ran my round-2 exploits, attacked the new instrument's premises, re-armed four of the silent-failure fixes, ran the web gate once and checked the live stack. **I did not re-run the .NET suite**, because nothing .NET changed (RR3-5 below). Probe driver: `scratchpad/rr3/arm.py`. For each probe it takes a `copy2` backup, applies an exact-anchor edit or creates probe files, runs ONE named test file, restores with `copyfile` + `utime` and asserts `filecmp.cmp(shallow=False)`. Vitest transforms from source on every run, so no stale binary is possible. Logs are in `scratchpad/rr3/*.log`.

## Verdict

| Entry | Verdict | Status after this review |
|---|---|---|
| **id 29 `web_app`** | **REJECTED**, fix round 3. The sweep is the right instrument. It kills every one of my round-2 exploits, naming the page, the request and the sentinel. But the probes found **a live violation of bullet 5 in the product** (D1), hidden behind an exception the record calls "swept". They also found that the claim's **"never a generic message" half is not asserted at all** (D2). Both are small to fix. D3 must be closed or restated; it need not be closed in full. | left `in_progress` (unchanged) |
| **id 30 `web_component_tests`** | bullets still met; closes with id 29 | left `pending` (unchanged) |

Web gate: `QUALITY_ONLY=web ./quality.sh` **exit 0**. Vitest **18 files / 251 passed**; coverage statements 96.34% (818/849), lines 98.22% (719/732); production build OK; integration **7/7**. Live check: the Gateway's own text is shown on all three pages, and `stop` reported clean.

## RR3-1. My round-2 exploits, re-run against the sweep — all killed

For N1/N3/N8 the component is rendered by a new page `src/app/(app)/probe/page.tsx`. Round 2 used an unrendered component file; a behavioural sweep rightly ignores a component nobody renders. The page's reviewed `EXPECTED_LOAD` entry was added in the same edit, so the only thing left to fail is the behaviour.

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| N1 | `if (stock.isError) return <p data-testid="probe">Something went wrong</p>; return <p>ok</p>;` | 2 failed / 55 | `/probe \| load \| GET /api/stock?page=1&pageSize=20 \| detail \| expected the problem detail "sweep-efa5c2ad6156-19-detail" on screen \| shown instead: Something went wrong` (and `title`) |
| N3 | `switch (stock.status) { case 'error': return <p …>Something went wrong</p>; default: … }` | 2 failed / 55 | `… \| detail \| expected the problem detail "sweep-ef3a36c07132-19-detail" on screen \| shown instead: Something went wrong` (and `title`) |
| N8 | `const ok = stock.isSuccess \|\| stock.isPending; return <div>{ok \|\| <p …>Something went wrong</p>}</div>;` | 2 failed / 55 | `… \| detail \| expected the problem detail "sweep-175b2a3bebb4-19-detail" on screen \| shown instead: Something went wrong` (and `title`) |
| N6 | `src/app/(app)/stock/error.tsx` rendering `<p data-testid="probe">Something went wrong</p>` | 2 failed / 54 | `(app)/stock/error.tsx \| a failed request reaching this boundary \| expected the problem detail "sweep-4c17975f8b85-21-detail" \| shown instead: Something went wrong` (and `title`) |
| VAC2 | `error={error}` → `error={null}` at the catalogue `ErrorMessage` (the sweep does not read component-test assertions, so the second half of VAC2 cannot help it) | 2 failed / 52 | `/orders/place \| load \| GET /api/catalog/companies \| detail \| expected the problem detail "sweep-dab44cd2d091-13-detail" on screen \| shown instead: The catalogue could not be loaded. ⏎ Enter codes by hand meanwhile (…)`. The same for `products` and `retailers`, in both variants |

All restores passed `cmp`, and every created file was removed along with the `probe/` directory. **D1 and D2 of round 2 are closed.** The instrument no longer cares how an error is rendered.

## RR3-2. Attacks on the sweep's own premises

| # | Attack (premise) | Sweep result | Reachable by a user? |
|---|---|---|---|
| B1 | Logout calls `(window as …)['fe' + 'tch']!(…)`: a computed key on `window` rather than `globalThis` (Q3) | **killed**, 3 failed / 52: `/orders \| sign-out: requests that bypassed the api-client seam: expected [ 'fetch /api/auth/logout' ] to deeply equal []`. Also the unresolved-reference scan and the stale `NOT_SHOWN` entry | n/a. P3b's runtime catch holds, and `window` is the stubbed global |
| **G1** | `/stock` shows `{stock.isError ? <p>Something went wrong</p> : null}` **beside** the existing detailed `ErrorMessage` (the claim's "never a generic message" half) | **SURVIVED, 52/52** | yes, by construction: both render under the same `isError` |
| **G2** | A second UI site for a scripted mutation: a "Quick top-up" button on `/orders` calling `useReplenishStock().mutate(…)` and rendering `Something went wrong` on error (Q8, and the brief's "a mutation reachable only through a second interaction step") | **SURVIVED, 52/52** | **yes, measured.** In the same harness, clicking it with `POST /api/stock/replenish` failed gave `"generic": true, "sentinel": false` (`G2r.log`) |
| **G3** | A GET fired only after a click: "Show low stock" on `/orders` mounting `useStock({ belowThreshold: true, … })` with a generic error branch (no population covers interaction-gated reads) | **SURVIVED, 52/52** | **yes, measured:** `"generic": true, "sentinel": false` with `GET /api/stock?belowThreshold=true&page=1&pageSize=5` failed (`G3r.log`) |
| **G4** | A load query that starts 400 ms after mount, with a generic error branch, on `/stock` (Q5, "settled is really settled") | **SURVIVED, 52/52.** Settling ends after 5 × 20 ms of quiet (`page-sweep.tsx:364-366`), so the request is never observed and `EXPECTED_LOAD` still matches | **yes, measured:** the request appears 1 s later with `"generic": true, "sentinel": false` (`G4r.log`) |
| **G6** | `describeError` returns `'Something went wrong'` for `status === 404 \|\| status === 500` | **SURVIVED, 52/52.** The sweep only ever serves 503 and 422 (`page-sweep.tsx:102,104`) | yes: a 404 from `GET /api/orders/{id}` or any 500 would show it |

**Today's tree has no instance of G1–G4**, as these commands show:
- **G1.** A harness run (a temporary `src/app/probe-extra.test.tsx`, since removed) listed, for every page, load request and variant, the text a failure adds that does not contain the sentinel. Output `scratchpad/rr3/extra.txt`, 20 lines. The only such text is:
  - the prefixes `Retailer filters unavailable:`, `Retailer filter unavailable:`, `Could not load credit limits:`, `Could not load invoices:`, `Could not load this order:`, `Could not load orders:` and `Could not load stock:`;
  - the manual-entry help text on `/orders/place`.
- **G2.** Consumers of each `src/hooks/use-*.ts` export, by content, excluding test/generated/hooks paths. `usePlaceOrder`, `useRegisterPayment` and `useReplenishStock` each have exactly **one** consumer (`place-order-form.tsx`, `billing-view.tsx`, `stock-view.tsx`), and each is on a scripted route. `useRetailers` has three consumers, all rendered on load.
- **G3/G4.** `grep -nE "enabled:|setTimeout|setInterval|refetchInterval"` over browser code returns 11 lines. The only interaction-gated read is `use-orders.ts:29` (`useOrderIdByReference`), which the register-payment action drives. The rest are 5 s polls, which repeat load keys, the stream's `enabled`, and comments.

So D3 below is a guard defect, not a product defect.

## RR3-3. The exception list (three entries)

- **SSE `EventSource`** is sound. The browser API exposes no response body, so no problem text can reach it. Its failure UI is proven at `order-detail-view.test.tsx:221` (`Connection lost`) and `use-order-stream.test.tsx:98` (`gave-up`).
- **Native logout** is sound. `app/api/auth/logout/route.ts` makes no upstream call, and the sweep's own case asserts `200` and zero Gateway requests.
- **Native login is NOT sound: see D1.** `NATIVE_POSTS['POST /api/auth/login']` says *"swept below: the real login route turns a Gateway refusal into ?error="*. But the case (`error-text-sweep.test.tsx:351-354`) fails **only** the route's first upstream call, `POST /auth/login`. The route makes a second call, and its failure path is generic.

## RR3-4. The silent-failure fixes, re-armed (4 of 6)

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| S1 | `billing-retailers-error` disabled (`false && retailers.isError`). **The implementer recorded no arm for this one** | 2 failed / 52 | `/billing \| load \| GET /api/catalog/retailers \| detail \| expected the problem detail "sweep-fae9f164239d-1-detail" on screen \| shown instead: (no new text at all)` (and `title`) |
| S2 | `/orders` retailer-filter error disabled | 2 failed / 52 | `/orders \| load \| GET /api/catalog/retailers \| detail \| expected the problem detail "sweep-a9b6b9243af8-9-detail" on screen \| shown instead: (no new text at all)` (and `title`) |
| S3 | The answered-empty order link renders `resolving the order link…` again (`billing-view.test.tsx`) | 1 failed / 17 | `AssertionError: the answered lookup must not still read as working: expected 'Register payment for INV-000027 (ORD-…' not to match /resolving/` |
| S4 | The pager renders `Page ${page?.page ?? current} of ${pages} · ${page?.total ?? 0}` while loading (`orders-list.test.tsx`) | 1 failed / 5 | `AssertionError: while loading, the pager must not claim a count: expected 'Page 1 of 1 · 0 ordersPreviousNext' to match /^Page 1PreviousNext$/` |

## RR3-5. Counts, the web gate, and the unchanged .NET side

- **Web gate.** `QUALITY_ONLY=web ./quality.sh`, run with nothing on ports 3000–3010, exited **0**. Every web step printed `[OK]`: install, OpenAPI types, lint (`lint-coverage OK — all 96 source files (87 under src/)`), typecheck, Vitest `Test Files 18 passed (18) / Tests 251 passed (251)`, production build, and integration `7 passed (7)`.
- **Reconciliation.** 211 − 16 + 1 + 52 + 3 = **251**, which is the figure the gate printed. The factors come from:
  - **16:** my round-2 guard runs, which printed 16 in every run;
  - **1:** `fixture-provenance.test.ts` contains one `it(`;
  - **52:** my baseline sweep run printed 52;
  - **+3:** `billing-view.test.tsx` now runs 17 (S3) and `stock-view.test.tsx` 10 (R3). I did not separately recount their pre-round figures, but the total closes exactly.
- **.NET unchanged.** `find src tests \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \) -not -path '*/bin/*' -not -path '*/obj/*' -newermt '2026-09-16 22:06'` printed **nothing**. The newest `.cs` files are `src/Gateway/GatewayHost.cs` and `GatewayProgramConfiguration.cs`, both at 19:02, before the implementer's full `./quality.sh` (22:06 → 22:22). `git status -- src tests` lists only id 96's five Gateway files. `Directory.Build.props` is dated 00:15. I did not re-run .NET; its 2 057/0/0 is the implementer's reading of a run over this same source.

## RR3-6. Live, briefly

- `WEB_PORT=3010 scripts/dev-stack.sh start`: exit 0, ending `[OK] web answers http://localhost:3010/login`.
- `stop-service Orders`: `[OK] Orders stopped`.
- Headless Chrome over CDP (`scratchpad/rr3/live.mjs`), with a real sign-in (`200`):
  - `GET /api/catalog/products` → `503`, `detail` `RPC call to "catalog.reference.list" failed: no responder is subscribed to this subject.`
  - `/orders/place`: `catalog-error` = `RPC call to "catalog.reference.list" failed: no responder is subscribed to this subject.`
  - `/billing`: `billing-retailers-error` = `Retailer filters unavailable: RPC call to "catalog.reference.list" failed: …`
  - `/orders`: `order-retailer-filter-error` = `Retailer filter unavailable: RPC call to …`
  - On all three pages, `/could not be loaded\.|something went wrong/i` against `innerText` was `false`.
- `scripts/dev-stack.sh stop`: exit 0. It printed five `[OK] … stopped` lines and `[OK] nothing left running, no port this stack bound is still held`. `ss` then showed nothing on 3000–3010, and `pgrep` found no `OrderToCash`/`next-server` process.

## Defects

### D1 (blocking, product) — a no-JavaScript sign-in whose `/auth/me` call fails shows a generic message

`apps/web/src/app/api/auth/login/route.ts:58`:

```ts
return isForm ? redirectToLogin(request, `Sign-in failed (HTTP ${meResponse.status}).`) : relay(meResponse);
```

The JS path relays the problem document; the form path discards it. Line 45 does the right thing for the first call (`problem?.detail ?? problem?.title ?? …`), and line 58 does not do it for the second.

**Probe.** A temporary `src/app/api/probe-me.test.ts`, since removed, used a `FakeGateway` whose `POST /auth/login` answers 200 with a token and whose `GET /auth/me` answers 503 with `detail: 'SENTINEL-ME-DETAIL'`. It called the real route with a form body. Result:

```
-   "error": "SENTINEL-ME-DETAIL",
+   "error": "Sign-in failed (HTTP 503).",
    "path": "/login",
    "status": 303,
```

The login page renders `?error=` verbatim, so the user reads `Sign-in failed (HTTP 503).`: a generic message while the server said something specific. Bullet 5 names exactly that. **Why the sweep did not see it:** the native-post case fails one upstream call chosen by hand (`gateway.on('POST', '/auth/login', …)`), which is a *listed* population. The unit the claim is about is **every upstream call the route makes**, and the route makes two.

### D2 (blocking, guard) — "never a generic message" is not asserted

`error-text-sweep.test.tsx:197,203` asserts only that the sentinel **is** on screen. A page that shows the problem's text **and** a generic message passes (G1, 52/52). Bullet 5 has two halves: *"that error's own text"* and *"never a generic message"*. This is attack 9 (the closer half satisfied, the other half unguarded). The harness already computes the text a failure adds (`newTexts`, `page-sweep.tsx:398`), and today's population of such text is small and enumerated (RR3-2: seven prefixes and one help paragraph).

### D3 (must be closed or restated) — the sweep's populations are keyed on the wrong unit, and two of its premise claims are overstated

**Q8** (*"the action table covers every mutation"*) is armed on **`apiRequest` call sites**: five, in hooks. The claim is about **UI that can trigger a mutation and show its failure**, and hooks are shared.
- G2: a second consumer of a scripted mutation is in no population.
- G3: an interaction-gated **read** is in none either. `mutatingCalls` skips GET by design (`request-sites.ts:182-183`), and the load sweep never sees it.

**Q5** (*"settled is really settled"*) is armed only across a retry delay. G4 shows that a request starting after a 100 ms quiet window is never observed, so it is never in the population.

**G6:** the sweep serves two statuses, so a status-conditioned branch is invisible to it.

FR2.10's first bullet discloses *"the same query with another key"*. It does not disclose G2, G3, G4 or G6.

## What must change before fix round 3 is re-reviewed

1. **D1.**
   - Line 58 must carry the problem's own `detail` → `title` (read the body as line 45 does), with the fallback used only when the body has neither.
   - Change the native-login sweep case from a hand-picked upstream call to **observe-then-fail-each**: run the form post once against an all-success `FakeGateway` and read `gateway.requests` (today `POST /auth/login`, `GET /auth/me`). Then fail each observed upstream call in turn, in both variants, and require the sentinel on the page the 303 names.
   - Arm it by restoring line 58. The failure must name `GET /auth/me` and the sentinel.
   - Also check `app/api/**/route.ts` by content for any other `redirectToLogin`/`localProblem`/literal-message path that drops a problem body, and classify every hit.
2. **D2.** In every failing run, the text the failure adds, minus the text containing the sentinel, must be a subset of a **reviewed literal** set of labels. Today's set is the seven prefixes and the manual-entry help in RR3-2. Check the set both ways (a listed label no run produces is stale). Arm with G1 exactly: the failure must name the page, the request and `Something went wrong`.
3. **D3.** The coordinator should choose between closing it and restating it. Both are acceptable, and neither needs to be expensive. Close cheaply with **derived-vs-literal tables**:
   - (a) the consumer files of every `src/hooks` export that wraps `useMutation`, or that gates a `useQuery` behind `enabled`/interaction, derived by content and compared to a reviewed literal (today: one consumer each, as enumerated in RR3-2). A second consumer then fails loudly until it is scripted. Arm with G2 and G3.
   - (b) after `settle('load')`, keep observing for longer than the longest non-poll timer the app sets, and fail on any new request key that is not a repeat. Otherwise disclose the 100 ms window in FR2.10 with its bound. Arm with G4.
   - (c) G6: disclose in FR2.10, or draw the failing status from the statuses `openapi.yaml` declares for that operation.
   - Whichever is chosen, **correct Q5 and Q8 in FR2.7** so they state the unit they actually cover.
4. **Advisory.** `NATIVE_POSTS['POST /api/auth/login']`'s reason should say what it covers once D1 is fixed.
5. **Re-review needs only:**
   - D1's and D2's arms re-run, and D3's chosen closure checked;
   - one Vitest count;
   - `QUALITY_ONLY=web ./quality.sh`, if no `.cs` changes.

**Not probed, so not claimed:**
- a `fetch` captured at module load, before `vi.stubGlobal`, which would bypass the recorder;
- a parallel-route slot (`@slot/page.tsx`), which `routeName` would name the same as its parent page, so `EXPECTED_LOAD`/`byName` would collide.

Neither has an instance today:
- `find apps/web/src/app -name '@*'` returns nothing;
- the primitive scan found no module-level `fetch`.

## CHECKPOINTS.md — changes from the round-2 list only

- **C2** [x] At most one `in_progress`: exactly one, id 29. Counts are 83 done / 1 in_progress / 12 pending.
- **C2** [ ] `progress/current.md`: the leader updates it with this verdict.
- **C4** [x] `QUALITY_ONLY=web ./quality.sh` passes. [x] The .NET side is unchanged since the implementer's full pass (RR3-5); not re-run. [x] No Jest.
- **C4** [ ] **Tests are real — not met for bullet 5.** D1 is a live product violation that the sweep's hand-picked native case hides; D2 is an unasserted claim half.
- **C5** [x] Probe files removed:
  - `src/app/(app)/probe/`, `src/features/stock/probe-banner.tsx`, `src/app/(app)/stock/error.tsx`, `src/app/probe-reach.test.tsx`, `src/app/probe-extra.test.tsx` and `src/app/api/probe-me.test.ts` are all absent;
  - every mutated file passed `cmp` against its backup;
  - `grep -r "Something went wrong" apps/web/src` returns nothing;
  - the live stack is stopped;
  - I ran no git command that writes.
- **C5** [ ] No effort record for ids 29/30, because they were rejected.
- **C7** [x] `specs/shared/` untouched by this round.
- Every other box stands as in §8.

**Routing (leader):**
- the RR6 matrix edits, still owed at id 29's close;
- a possible defeat-list row: *"serve the failure through a path the population never drives — a second consumer of a shared hook, an interaction-gated read, a request after the settle window, a status the probe never sends"*. D3 is the first instance, and it is the behavioural-sweep counterpart of row 11.

**State after this review:** I left `feature_list.json` untouched (id 29 `in_progress`, id 30 `pending`) and appended no effort record.

---

# Re-review, fix round 3

**Reviewer session:** 2026-09-17, ≈00:12 → 00:40. The first artefact was my driver `scratchpad/rr4/arm4.py`, written at 00:16:03. The arms ran 00:16 → 00:28:05, and `QUALITY_ONLY=web ./quality.sh` finished at 00:30.

**Scope.** This review is bound by the stopping rule recorded in `progress/current.md:3`, which was written before fix round 3 began. I verified exactly the list my previous section required:
- D1;
- D2;
- D3 (a), (b) and (c);
- the Q5/Q8 corrections;
- the `NATIVE_POSTS` advisory;
- one Vitest count and the web gate.

I also checked the one addition the brief named, FR3.7.

**Not re-run:**
- **The .NET suite.** No `.cs`/`.csproj`/`.props` file is newer than 22:06 (command in RR4-5).
- **The implementer's other 30 round-2 arms and W1/S1/L1.** I re-ran the arms for the claims under test and added three probes of my own.

**Arming procedure.** The driver loads the implementer's anchor table (`scratchpad/fr2/arm.py`) only for the review-authored exploits D1 and G1–G6. It adds my own mutations: D1x, G6x and U1x. For each arm it:
1. takes a `copy2` backup;
2. applies an exact-anchor replace, where the anchor must occur once;
3. runs **one** named test file;
4. restores with `copyfile` + `utime`;
5. asserts `filecmp.cmp(shallow=False)`.

After the run, `md5sum -c` against hashes I took **before** any mutation printed `OK` for all six mutated sources. `grep -rn "Something went wrong\|Registering failed\.\|LateStock\|Quick top-up" src` printed nothing. Vitest transforms from source on every run, so there is no stale-binary path.

## Verdict

| Entry | Verdict | Status after this review |
|---|---|---|
| **id 29 `web_app`** | **APPROVED.** Every item on the round-3 list is met, and each named arm fails with a message that names the claim. I found no live product defect: no text or state that today's code shows a user wrongly and that falls inside this feature's bullets. The one behaviour I flag (A1 below) is outside the bullets. It is recorded as an advisory and a disclosed limit, as the stopping rule directs. | `done` |
| **id 30 `web_component_tests`** | **APPROVED**, closing with id 29 as its notes prescribe. Both bullets have stood since round 1 (§8), and this round adds two component-level files. | `done` |

**Web gate.** `QUALITY_ONLY=web ./quality.sh` exited **0**, with nothing running beforehand: `pgrep` found no vitest, next-server, dotnet build/test/format or quality.sh process, and `ss` showed nothing on ports 3000–3010. Every step printed `[OK]`:
- install;
- OpenAPI types match;
- lint (`lint-coverage OK — all 99 source files (90 under src/)`);
- typecheck;
- Vitest **`Test Files 20 passed (20)` / `Tests 264 passed (264)`**, with statements 96.58% (819/848) and lines 98.49% (720/731);
- production build;
- integration **7/7**.

## RR4-1. D1: closed

**Product.** `src/app/api/auth/login/route.ts:43` and `:54` both go through `problemText` (`:71-74`), which returns `detail`, else `title`, else the status literal. Line 73 is as the coordinator quoted it.

**Guard.** The sweep case is now observe-then-fail-each (`error-text-sweep.test.tsx:452-501`). It makes an all-success post and asserts that the observed upstream calls are exactly `['POST /auth/login', 'GET /auth/me']`. Each observed call is then refused with every status `openapi.yaml` declares for it, in both variants. The 303 target page must show the sentinel, and anything added beside it must be a reviewed label.

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| **D1** | line 54 restored to `` `Sign-in failed (HTTP ${meResponse.status}).` `` | 2 failed / 56 | `/login \| native POST /api/auth/login \| upstream GET /auth/me 401 detail \| expected the problem detail "sweep-45573edd99a6-68-detail" on screen \| shown instead: Sign-in failed (HTTP 401).` and the same for `401 title` (`sweep-45573edd99a6-72-title`) |
| **D1x** (mine, payload corruption) | `problemText` reads `title` before `detail` | 1 failed / 56 | `/login \| native POST /api/auth/login \| upstream POST /auth/login 400 detail \| expected the problem detail "sweep-814666764814-65-detail" on screen \| shown instead: decoy-title-814666764814` (also 401 and 429, and `GET /auth/me 401`) |

Both failures name the upstream call, the status and the sentinel. D1x shows that the detail variant carries a **decoy** title, so the order of preference is guarded, not merely the presence of some problem text.

**Route enumeration.** I re-ran FR3.1's command myself:

```
find src/app/api -name 'route.ts' -print0 | xargs -0 grep -nE "redirectToLogin|localProblem|gatewayUnreachable|relay\(|NextResponse\.(json|redirect)|new (Next)?Response\(|failed|Failed|\`[A-Z][a-z]+ [a-z]"
```

It printed 19 lines across 5 of the 12 `route.ts` files (`find src/app/api -name route.ts` lists 12):
- `orders/stream` at `:2, :36, :40, :43`;
- `catalog/[kind]` at `:2, :13`;
- `auth/logout` at `:8`;
- `auth/login` at `:3, 32, 39, 43, 51, 54, 65, 73, 76, 80, 83`;
- `auth/session` at `:8`.

Every hit matches FR3.1's classification table. The table does not list the two `import` lines (`login:3`, `stream:2`), which carry no behaviour. The other seven files go through `proxyToGateway`, whose error paths (`server/gateway.ts:50-58, 62-72, 85`) are a local problem with its own `detail`, the unreachable problem, or a byte-for-byte relay. **No other path drops a problem body.**

**Advisory 4 (`NATIVE_POSTS`)**: closed. `:176-178` now describes the observe-then-fail-each sweep.

## RR4-2. D2: closed

`unlabelled()` (`:257-266`) runs on every failing run, including the native post (`:493-494`). It compares against the reviewed literal `LABELS` (`:188-202`, 13 entries):
- the seven prefixes from my round-3 list;
- the payment form's order-link prefix, which this round's own product fix introduced;
- five fragments of the manual-entry help.

The baseline is "every text node the all-success run **ever** displayed" (a `MutationObserver`, `page-sweep.tsx:380-383`). The both-ways check is at `:561`. I read it but did not arm it; the implementer's L1 is recorded.

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| **G1** | `/stock`: `{stock.isError ? <p>Something went wrong</p> : null}` beside the detailed `ErrorMessage` | 4 failed / 56 (it was 52/52 green in round 3) | `/stock \| load \| GET /api/stock?page=1&pageSize=20 \| 503 detail \| the problem's words are shown, but so is text that is not a reviewed label: "Something went wrong"`, the same for `503 title`, and the `/stock \| replenish` pair |

The failure names the page, the request and the generic text, as round 3 required.

## RR4-3. D3: closed, all three parts

| Part | Arm | Result | Verbatim failure |
|---|---|---|---|
| (a) second consumer of a mutation | **G2** | 1 failed / 56 (was 52/52) | `hook exports / mutating components and who uses them — a new consumer of a mutation or gated read must be scripted in ACTIONS first` → `+ "hooks/use-stock.ts#useReplenishStock mutation \| features/orders/orders-list.tsx, features/stock/stock-view.tsx \| via app/(app)/orders/page.tsx, app/(app)/stock/page.tsx"` |
| (a) interaction-gated read | **G3** | 1 failed / 56 (was 52/52) | the same test → `+ "hooks/use-stock.ts#useStock query \| features/orders/orders-list.tsx, features/stock/stock-view.tsx"` |
| (b) a request after settle | **G4** | 6 failed / 56 (was 52/52) | `/stock: requests that first started only after load had settled (watched for 2500 ms) — review them before adding them to EXPECTED_LOAD` → `+ "load: GET /api/stock?belowThreshold=true&page=1&pageSize=5"`. The sweep then fails the late key: `… 503 detail \| expected the problem detail "sweep-994e1d71502b-19-detail" on screen \| shown instead: (no new text at all)` |
| (c) status-conditioned branch | **G6** (404 \|\| 500) | 7 failed / 56 (was 52/52) | `/orders/[id] \| load \| GET /api/orders/9f1e2d3c-4b5a-4c6d-8e7f-0a1b2c3d4e5f \| 404 detail \| expected the problem detail "sweep-52c40fbb1fc3-7-detail" on screen \| shown instead: Could not load this order: ⏎ Something went wrong` (and title, and the replenish/payment 404s) |
| (c) a different declared status, mine | **G6x**: `describeError` returns `'Registering failed.'` for **409** only | 4 failed / 56 | `/orders/place \| place-order \| POST /api/orders \| 409 detail \| expected the problem detail "sweep-07f3c9a58e50-28-detail" on screen \| shown instead: Registering failed.` |

I used G6x to check that (c) reads each operation's own declared set rather than a widened fixed list: 409 is declared for `POST /orders` and was served there.

**(b)'s bound is sound. Derivation checked:**
- `LATE_WATCH_MS = DEFAULT_PENDING_RETRY_MS + 500` (`page-sweep.tsx:147`) is derived, not hard-coded, so it moves with the constant.
- I enumerated the timers by content: `find src … -not -name '*.test.*' -not -path 'src/test/*' -not -path 'src/generated/*' -not -path 'src/server/*' | xargs grep -nE "setTimeout|setInterval|refetchInterval|retryDelay|Retry-After|retry-after|staleTime|requestAnimationFrame|_MS\b"`. It printed 19 lines (`| wc -l` = 19), and **no browser file calls `setTimeout`, `setInterval` or `requestAnimationFrame`**. What remains is:
  - `DEFAULT_PENDING_RETRY_MS = 2000` (`lib/order-detail.ts:6`), the 202 retry and the only app-set non-poll delay that starts a request. A server `Retry-After` can exceed it, but it only repeats the detail key.
  - `refetchInterval: 5_000` in `use-orders:19`, `use-billing:18,33` and `use-stock:20`, plus the detail backstop `STALE_STATUS_BACKSTOP_MS = 5_000`. These only repeat keys.
  - `staleTime` 2 000 / 60 000, which starts no timer.
  - TanStack's retry: `providers.tsx:22` allows one retry, at the default 1 000 ms first delay.

  2 500 therefore exceeds every non-repeating delay the app sets.
- The premise test at `:577` hard-codes `> 2_000` rather than reading the constant. That is harmless, because the constant is what sets the watch.
- The watch does not run in failing runs, which FR3.5 discloses.

**Q5/Q8 corrections:** done. FR2.7's rows now state their units: Q5 is *the moment the harness stops waiting, per run*; Q8 is *`apiRequest` call sites whose method is not GET*, plus round 3's hook/consumer unit. Each names what round 2 did not cover.

## RR4-4. FR3.7, session expiry: verified, and the 401 exclusion is sound

The two files hold exactly the stated 9 cases: `gateway-relay.test.ts` has 1 + `it.each([403, 500])` = 3, and `providers.test.ts` has 2 + 2 + 2 = 6. Both call the real code: `relay()` from `@/server/gateway` and `makeQueryClient()` from `@/app/providers`, which `Providers` itself uses (`providers.tsx:30`).

| # | Mutation | Result | Verbatim failure |
|---|---|---|---|
| **U1** | `relay`: `if (upstream.status === 401) clearSession(response);` deleted | 1 failed / 3 | `AssertionError: the 401 must clear the session cookie: expected '<no Set-Cookie header: the session wa…' to match /^otc_session=;/` |
| **U4** | `providers`: `&& window.location.pathname !== '/login'` removed | 2 failed / 6 | `AssertionError: on /login a 401 is the sign-in form's own error, not a redirect: expected [ [ '/login' ] ] to deeply equal []` (query and mutation) |
| **U1x** (mine) | `relay` clears on **any** `status >= 400` | 2 failed / 3 | `AssertionError: a 403 must not sign the user out: expected 'otc_session=; Path=/; Max-Age=0; Secu…' to be null`, and the same for 500 |

**My judgement of the argument: it holds.** For a 401 off `/login`, the app's contract is a navigation, not rendered text. The decision is taken in one place, the `QueryCache`/`MutationCache` `onError` of the single client that `Providers` creates for every page, and the sweep's root-layout premise (P11) proves that `Providers` wraps every page. Proving the redirect at that one site, for both caches, covers every page's 401 by construction. Sweeping a sentinel for a page the user is being navigated away from would assert the wrong thing. The one place where a 401 is rendered, `/login`, stays in the sweep with its declared 401s, which D1's arm itself exercised.

## RR4-5. Counts reconciled

- **`264 = 255 + 9`.** The 9 is `gateway-relay.test.ts` 3 plus `providers.test.ts` 6. My U-arm runs printed `(3)` and `(6)` as the file totals, and the gate printed 20 files / 264.
- **`255 = 251 + 4`.** The 251 is round 3's gate total, in which the sweep printed 52. Today the sweep prints **56** in every one of my eight sweep-arm runs. So 251 − 52 + 56 = 255, and 255 + 9 = 264, which is the figure the gate printed.
- **Files: 18 + 2 = 20.** `find src -name '*.test.*'` lists 20.
- **.NET unchanged.** `find src tests \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \) -not -path '*/bin/*' -not -path '*/obj/*' -newermt '2026-09-16 22:06' -print` printed nothing. `Directory.Build.props` is dated 2026-09-14 00:15. I did not re-run .NET; the full-suite figure of record is the 22:06 → 22:22 run (2 057 / 0 / 0).

## Defects

**None blocking.** Every item on the round-3 list is met, and I found no live product defect within the feature's bullets.

### A1 (advisory, outside the bullets): an expired session on a terminal order's page shows "Connection lost", not a sign-in

`order-stream-client.ts:105-111` turns a refused stream into `gave-up`. `order-detail-view.tsx:23,101-104` then shows `Connection lost` with a **Retry connection** button that only reopens the stream (`use-order-stream.ts:78`).

The detail query polls only while the order is non-terminal (`use-order-detail.ts:59-70`). So on a **terminal** order whose session has expired, no query fails with 401 and nothing redirects until a window-focus refetch or a navigation happens. The retry button then loops on a refusal.

This is not wrong text under bullet 5: `EventSource` exposes no body, and round 2 accepted that exception (RR3-3). The connection is also genuinely lost. It is a signed-out state the page does not name, and it is exactly FR3.7's *"one gap, stated"* shape on a second path. **Disclosed limit, carried to phase 19's Playwright suite.** It is left to the leader whether to file it.

### A2 (advisory, record accuracy): FR3's wall-clock end time is impossible

The FR3 brief line says *"to about 00:40 on 2026-09-17"*. `progress/impl_web_app.md` was last written at **00:10:55**, and this review started at about 00:12. The filesystem places fix round 3 at ≈22:50 → 00:03:
- first arm log `D1.log` at 23:02:11;
- the 39-arm run ending with `W1.log` at 23:59:02;
- `quality-web3.log` at 00:03:12.

It places FR3.7 at ≈00:06 → 00:11: the test files at 00:06:30, the U-arms at 00:06:44–00:06:50, and `quality-web4.log` at 00:10:41. The effort record below uses the filesystem figures. This is the phase-14 lesson again: a self-reported wall-clock range is not evidence.

### Disclosed limits carried to phase 19 (not grounds for rejection, per the stopping rule)

- **A second use of a hook inside an already-listed consumer file, behind a click.** Stated by FR3.5; `REQUEST_UNITS` is file-granular.
- **A generic text node that the baseline showed at some moment**, such as a pending label, and that a failing run leaves on screen. It is exempt from D2's check by construction, because the baseline is "ever shown", not "shown at the end".
- **A late request in a failing run.** The watch runs in baselines only.
- **A1 above.**
- **Carried from round 3, still with no instance:** a `fetch` captured at module load, and a parallel-route slot. `find apps/web/src/app -name '@*'` still returns nothing.

## CHECKPOINTS.md: changes from the round-3 list only

- **C2**
  - [x] At most one `in_progress`. After this review there are **none**: 85 done / 0 in_progress / 11 pending (a count after my edit, below).
  - [ ] `progress/current.md` is still the leader's to update with this verdict.
- **C4**
  - [x] `QUALITY_ONLY=web ./quality.sh` passes (20 / 264, integration 7/7).
  - [x] .NET unchanged since the full pass; not re-run.
  - [x] **Tests are real for bullet 5:** D1 and D2 are closed and armed, and D3 is closed and armed.
  - [x] No Jest.
- **C5**
  - [x] Every mutated file matched its pre-mutation md5 and passed `cmp` against its backup. No probe text remains in `src`, I ran no git command that writes, and nothing was left running (the stack was not started this round).
  - [x] Effort records appended for ids 29 and 30.
- **C7**
  - [x] `specs/shared/` is untouched by this round.
- Every other box stands as in §8 and the round-2 and round-3 lists.

## Routing (leader): artefacts owed now that id 29 closes

1. **RR6's `specs/shared/test-matrix.md` edits** (R55's web half: `TODO` → `DONE`, the two-halves caveat, rows 78/80/83) are now **due**. A change to `specs/shared/` is outside my remit and is human-gated. Apply them as the spec edit this closure requires, or file them as a numbered entry. **Do not leave them as this sentence.** The exact text is in RR6 above.
2. **A defeat-list row** (proposed in round 3; D3 was the first instance and this round closed it): *"serve the failure through a path the population never drives — a second consumer of a shared hook, an interaction-gated read, a request after the settle window, a status the probe never sends"*. `CLAUDE.md` is the leader's to edit.
3. **A1**, if the leader judges it worth an entry, for phase 19.
