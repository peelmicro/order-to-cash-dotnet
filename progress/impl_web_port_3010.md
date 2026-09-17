# Web app default port: 3000 → 3010, in #7 and #8

## Scope

Change the web app's default dev-server port from 3000 to 3010 in both
repositories. `.env.example`, `scripts/dev-stack.sh`, the root `package.json`
and `README.md` in #8 were already changed by the leader before this task
started and were not touched again.

## Direct changes made

### #8 — `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-dotnet`

- `apps/web/package.json` — `dev` and `start` scripts: `--port ${WEB_PORT:-3000}` → `--port ${WEB_PORT:-3010}` (both occurrences).
- `apps/web/src/lib/stack-label.ts` — doc comment "both default to port 3000" → "both default to port 3010" (found by the #8-scoped enumeration below).

### #7 — `/home/juanpabloperez/Work/Projects/Assessments/order-to-cash-nestjs`

- `apps/web/nuxt.config.ts` — `port: Number(process.env.WEB_PORT ?? 3000)` → `?? 3010`, with a one-line comment explaining the reason (port 3000 commonly taken on a dev machine; #7/#8 share every other host port and never run together).
- `.env.example` — `WEB_PORT=3000` → `WEB_PORT=3010`, with the equivalent one-line comment (matching #8's own `.env.example` wording style).
- `README.md` — four occurrences of `http://localhost:3000` for the web UI (Quick Start `open` command, the URL table, `pnpm dev:web` comment, the container URL table) → `3010`. (Note: README already had *other*, unrelated lines using `E2E_BASE_URL=http://localhost:3010` as an override example — those were already correct and untouched.)
- `docker-compose.apps.yml` — the `web` service's `PORT: "${WEB_PORT:-3000}"` and port mapping `"${WEB_PORT:-3000}:${WEB_PORT:-3000}"` → both defaults changed to `3010`.
- `docker-compose.infra.yml` — a comment on the Grafana service explaining why its host port is pinned to 3030 ("to avoid clashing with dev servers that default to 3000") → updated to "3010" so the rationale stays accurate; Grafana's own container port (`3000`, its own internal default) and its healthcheck URL (`http://localhost:3000/api/health`, inside the Grafana container) are unrelated to the web app and were left alone.
- `infra/docker/web/Dockerfile` — a comment explaining privileged-port reasoning ("the port is `${WEB_PORT:-3000}`, and 3010 on this machine") was stale even before this change (it already described this machine's override as 3010); rewritten to state the new default directly: "the port is `${WEB_PORT:-3010}`". The healthcheck's own PORT fallback (`process.env.PORT||3000`) → `||3010`.
- `scripts/capture-demo.mjs` and `scripts/capture-media.mjs` — `env.WEB_PORT ?? 3000` → `?? 3010` (both scripts read `.env`/`process.env` for the actual value; only the fallback used when neither is set changes).
- `apps/web/playwright.config.ts` — doc comment "falls back to the compose default, 3000" → "3010", and the code's own fallback `process.env.WEB_PORT ?? 3000` → `?? 3010`.
- `apps/web/app/lib/stack-label.ts` — doc comment "both default to port 3000" → "both default to port 3010" (this is #7's mirror of the #8 file listed above).

## Enumeration — #7, repository-wide

Command (excludes `node_modules`, `.nuxt`, `.output`, `dist`, `coverage`, `.git`
at the source via `--exclude-dir`; word-boundary pattern to avoid matching
substrings of larger numbers like `30000`):

```
cd order-to-cash-nestjs && grep -rnE "\b3000\b" \
  --exclude-dir=node_modules --exclude-dir=.nuxt --exclude-dir=.output \
  --exclude-dir=dist --exclude-dir=coverage --exclude-dir=.git .
```

Full output (79 lines) is reproduced by classification below. `progress/*.md`
files are historical implementation/review records of past sessions (not
living documentation); they describe what was true when they were written
and were left unchanged, matching how they already record superseded values
elsewhere in the same files (e.g. `progress/review_e2e_playwright.md` already
narrates the port moving between 3000 and 3010 across earlier passes).

| Hit | Classification | Action |
|---|---|---|
| `README.md:23,32,236,361` | web port | **changed** → 3010 |
| `docker-compose.infra.yml:340` (comment, dev-server rationale) | web port (mentioned in a comment about Grafana's own port choice) | **changed** → 3010 |
| `docker-compose.infra.yml:354` `"${GRAFANA_HOST_PORT:-3030}:3000"` | Grafana's own container-internal port | unrelated — left |
| `docker-compose.infra.yml:363` Grafana healthcheck `http://localhost:3000/api/health` | Grafana's own container-internal port | unrelated — left |
| `docker-compose.apps.yml:340,355` | web port (container `PORT` env + port mapping) | **changed** → 3010 |
| `infra/docker/web/Dockerfile:74` (comment) | web port | **changed** → 3010 (rewritten to state the new default directly, since the old comment's "3010 on this machine" is itself now stale) |
| `infra/docker/web/Dockerfile:85` (healthcheck `PORT\|\|3000`) | web port | **changed** → 3010 |
| `scripts/capture-demo.mjs:31` | web port | **changed** → 3010 |
| `scripts/capture-media.mjs:43` | web port | **changed** → 3010 |
| `scripts/capture-media.mjs:162` `waitForTimeout(3000)` | unrelated timeout (3000 ms) | left |
| `apps/web/playwright.config.ts:10,17` | web port (e2e base URL fallback) | **changed** → 3010 |
| `apps/web/app/lib/stack-label.ts:4` | web port (doc comment) | **changed** → 3010 |
| `apps/web/app/lib/order-stream-client.spec.ts` (4 hits) | unrelated `vi.waitFor` timeouts | left |
| `.env.example:407` | web port | **changed** → 3010 (done before the enumeration; comment added) |
| `apps/orders/src/saga-command-retry.integration.spec.ts:105,107,177,179` | unrelated — a NATS test port (`3000`, a throwaway test double's listen port, unrelated to `WEB_PORT`) and a 3000 ms dispatcher timeout | left |
| `apps/orders/src/orders-cancel.integration.spec.ts:282` | unrelated dispatcher `timeoutMs: 3000` | left |
| `apps/gateway/src/saga-e2e-verification.integration.spec.ts:640` | unrelated — `1_500 * 2 = 3000` minor units (money), not a port | left |
| `apps/notifications/src/infrastructure/templates/*.spec.ts` (4 hits) | unrelated — money amounts (`3000` minor units) in fixture data | left |
| `specs/shared/openapi.yaml:72,88` | **specs/shared/ — out of scope by explicit rule.** Also substantively unrelated: this is the Gateway API's own default server URL/port in the shared spec (Gateway, not the web app) | left, untouched per instruction |
| `specs/shared/n8n-workflows.md:93` | **specs/shared/ — out of scope.** Also unrelated: `OTC_GATEWAY_URL` default, the Gateway's port, not the web app's | left, untouched per instruction |
| `progress/*.md` (all remaining hits, ~35 lines across `impl_web_app.md`, `impl_saga_e2e_verification.md`, `impl_infra_compose_apps.md`, `impl_infra_compose_apps_nonroot.md`, `impl_monorepo_scaffold.md`, `impl_e2e_playwright.md`, `impl_sonarqube_quality_gates.md`, `review_*.md` equivalents, `history.md`) | historical session records, not living docs | left unchanged — they narrate what was true at the time each session ran, and several already document the port having moved between 3000 and 3010 across sessions before this change |

No hits under `n8n/` (checked separately: `grep -rn "3000" n8n/` → no output) and no hits under `docs/` (`grep -rn "3000" docs/` → no output).

## Enumeration — #8, `apps/web` only

Command:

```
cd order-to-cash-dotnet && grep -rnE "\b3000\b" \
  --exclude-dir=node_modules --exclude-dir=.next --exclude-dir=dist \
  --exclude-dir=coverage --exclude-dir=.git apps/web
```

| Hit | Classification | Action |
|---|---|---|
| `apps/web/src/lib/stack-label.ts:3` | web port (doc comment) | **changed** → 3010 |
| `apps/web/tests-integration/stream-proxy.test.ts:146,147,212` | unrelated — SSE proxy test timeouts (3000 ms) | left |
| `apps/web/src/lib/order-detail.test.ts:27` | unrelated — a retry-delay assertion (`toBe(3000)`, milliseconds), not a port | left |
| `apps/web/src/features/orders/order-detail-view.test.tsx:70` | unrelated — `findByTestId` timeout (3000 ms) | left |
| `apps/web/src/app/api/orders/stream/stream-route.test.ts:115,124` | unrelated — `vi.waitFor` timeouts | left |
| `apps/web/src/lib/order-stream-client.test.ts` (7 hits) | unrelated — `vi.waitFor` timeouts | left |

No `playwright.config.ts` or compose file exists under #8's `apps/web` (checked: `find apps/web -iname "*playwright*"` → no output; #8's own `docker-compose.infra.yml` has no `WEB_PORT` reference — the web app is run via `scripts/dev-stack.sh`, already changed by the leader).

## Verification

- **#8 `apps/web`**: `pnpm exec vitest run` → **21 files, 278 tests, all passed** (baseline: 21/278 — exact match).
- **#7 `apps/web`**: `pnpm test` (`vitest run`) → **18 files, 129 tests, all passed** (baseline: 18/129 — exact match).
- **#7 `apps/web` lint**: `pnpm run lint` (`eslint apps/web`) → clean, no output, exit 0.
- **#7 `apps/web` typecheck**: `pnpm run typecheck` (`nuxi typecheck`) → exit 0, no errors.
- No e2e run performed, per instruction (`playwright.config.ts` and `docker-compose.apps.yml`/`docker-compose.infra.yml` changed in #7 — noted above, not exercised end-to-end here).
- Counts reconcile exactly against both stated baselines; no discrepancy to explain.

## Rules honoured

- No git command that writes the index or working tree was run in either repo (only `Read`/`Edit`/`Bash` read-only greps and `pnpm` test/lint/typecheck runs); no commit made.
- `feature_list.json`, `specs/shared/` and `CLAUDE.md` were not touched.
- Test runs were executed one at a time, sequentially, never concurrently.
