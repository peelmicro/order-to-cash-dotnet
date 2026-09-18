# Demo GIF capture — sub-task of id 37 (documentation_demo, phase 24), LIGHT

Classification: LIGHT (capture tooling, not product code; no separate
reviewer for this round — the leader checks the output directly, per the
brief).

## What this ports from #7

Source: `../order-to-cash-nestjs/scripts/capture-demo.mjs` (read in full,
~99 lines). Structure kept unchanged: two Playwright browser contexts (one
logs in off-camera, one records only the compensation demo), record to
`.demo-video/`, convert the resulting `.webm` to a GIF via two `ffmpeg`
passes (palette generation, then palette-based encoding), write to
`docs/screenshots/demo-compensation.gif`.

Three adaptations, all pre-identified in the brief and confirmed again here
against #8's own files before writing the script:

1. **Navigation after placing the order.** #7 clicks an "order list" link,
   waits for `/orders`, finds the order by its reference text, then clicks
   it (two steps). #8's success banner instead carries its own direct link
   (`data-testid="accepted-order-link"`) straight to `/orders/{orderId}` —
   confirmed against `apps/web/e2e/compensation.spec.ts` lines 59–64 before
   writing the port. `scripts/capture-demo.mjs` clicks that link directly
   and skips the list page entirely.

2. **"Fill demo order" button text.** #7 uses a regex match
   (`/Fill demo order/`). #8's exact copy is
   `"Fill demo order (.99 → compensation)"` — confirmed against
   `apps/web/e2e/compensation.spec.ts` line 41 — so the port uses
   `getByRole('button', { name: 'Fill demo order (.99 → compensation)' })`,
   an exact string, not a regex.

3. **Env-file reading.** #7's script hand-parses the root `.env` with a
   `readFileSync` + regex loop and injects into `process.env` before
   reading `GATEWAY_OPERATOR_USERNAME`/`PASSWORD`. #8 already has its own
   established convention for a one-off Node script reading the root
   `.env`: Node's native `--env-file-if-exists` flag, used by
   `apps/web/package.json`'s `test:e2e` script
   (`node --env-file-if-exists=../../.env ./node_modules/@playwright/test/cli.js test`).
   `scripts/capture-demo.mjs` therefore reads `process.env` directly with no
   parsing of its own, and the new `media:demo` root `package.json` script
   supplies the flag: `"node --env-file-if-exists=.env scripts/capture-demo.mjs"`.
   Everything else (selectors for `#username`/`#password` on `/login`,
   "Sign in", `getByRole('button', { name: 'Place order', exact: true })`,
   `getByTestId('place-order-success')`, `getByTestId('order-detail-status')`)
   matched #7's script unchanged, confirmed against
   `apps/web/e2e/compensation.spec.ts` and `apps/web/e2e/global.setup.ts`.

No ported-idiom ledger row beyond the above three: this is capture tooling
(a dev script), not a feature under `src/`/`apps/web/` with a design.md, and
the brief scopes the report to "what you adapted and why" rather than a
formal ledger.

## Files touched

- `scripts/capture-demo.mjs` (new) — the ported/adapted script.
- `package.json` (one new script) — `"media:demo": "node --env-file-if-exists=.env scripts/capture-demo.mjs"`.
- `docs/screenshots/demo-compensation.gif` (new binary output).
- `progress/impl_demo_gif_capture.md` (this file).

Nothing else was touched: `apps/web/` source, `src/`, `tests/`,
`specs/shared/`, `CLAUDE.md` and `feature_list.json` are all untouched by
this sub-task, and no git command that writes the index or working tree was
run.

## Pre-flight facts confirmed before running anything

- `ffmpeg` on `PATH`: `/usr/bin/ffmpeg`, version 6.1.1-3ubuntu5+esm13.
- `apps/web/node_modules/.bin/playwright` exists; `chromium-1234` already
  present under `~/.cache/ms-playwright` — no `playwright install` needed.
- Root `.env` exists (7054 bytes, `.env.example` documents
  `GATEWAY_OPERATOR_USERNAME=operator` and a default
  `GATEWAY_OPERATOR_PASSWORD`; the real `.env` supplies the values actually
  used).
- Node `v24.19.0` — supports `--env-file-if-exists` (stable since Node 22).
- No stack already running: `docker ps` returned nothing and
  `./scripts/dev-stack.sh status` reported all six services and `web`
  stopped, before I started.

## Commands run, in order

```
mkdir -p docs/screenshots
pnpm stack:start          # brings up infra (docker compose), builds once,
                           # runs the seed job, starts the six .NET services
                           # and the web app
pnpm media:demo            # node --env-file-if-exists=.env scripts/capture-demo.mjs
pnpm stack:stop             # stops the six .NET services and web (infra
                             # containers deliberately left running by this
                             # script's own design)
pnpm dc:down:infra           # brings the infra containers down too, since
                              # none were running before this task started
                              # and the brief asks for a clean teardown
docker ps                    # empty — confirms teardown
pgrep -fa 'dotnet.*src/(Orders|Fulfillment|Billing|Notifications|Projector|Gateway)'   # empty
pgrep -fa 'next start'        # empty
./scripts/dev-stack.sh status  # all seven stopped
```

### Stack choice and why

`pnpm stack:start` (native `dotnet run` + `next start` against the infra
compose file), not `pnpm dc:up:apps` (the Docker-image build path). Reasons:

- It is the path `apps/web/playwright.config.ts` itself already documents
  as the one e2e (and therefore this UI-driving script) relies on: "the
  root `scripts/dev-stack.sh` ... never a self-spawned dev server".
- It runs the seed job (`dotnet run --project src/Seed`) as part of
  `start`, giving the demo real, freshly-seeded catalogue data (retailers,
  companies, products) without a separate Docker image build step.
- It avoids any Docker image build entirely, so the maintainer's
  `~/.docker/config.json` / `docker-credential-desktop` issue never comes
  up (not applicable here, but noted as the reason this path was
  preferred over `dc:up:apps`, which does build images).

The run succeeded on the first attempt — no selector mismatch, no timing
fix was needed, so the script in `scripts/capture-demo.mjs` above is exactly
what was run.

## GIF output — confirmed non-trivial and confirmed to show the demo

`docs/screenshots/demo-compensation.gif`: **2,586,447 bytes (~2.5 MiB)**,
960×600, GIF89a, **114 frames, 11.4s** (`ffprobe`), not a 1-frame accident.

Console output from the run:

```
recording the .99 compensation demo…
  placed ORD-000007
  wrote .../docs/screenshots/demo-compensation.gif
```

Previewed two extracted frames (`ffmpeg -vf "select=eq(n\,N)"`, viewed then
deleted from the scratchpad, not committed anywhere):

- Frame 10 (early): the `/orders/place` form, mid-fill, "Fill demo order
  (.99 → compensation)" button visible top-right, EUR currency selected.
- Frame 108 (late): the order-detail page for **ORD-000007**, status badge
  **cancelled**, cancellation reason **credit_rejected**, total **€249.99**,
  and the full timeline rendered top to bottom: `order.placed.v1` →
  `stock.reserved.v1` → `credit.rejected.v1` ("simulated_cents_rule") →
  `stock.released.v1` ("1 unit(s) released back to stock (compensation)")
  → `order.cancelled.v1` with **"caused by stock.released.v1"** visible
  under the cancellation entry.

This confirms the GIF genuinely captures the compensation saga end to end,
including the causal-link rendering that is the point of the demo (the same
property `apps/web/e2e/compensation.spec.ts` asserts programmatically).

## Teardown confirmation

- `pnpm stack:stop` reported: web, Gateway, Projector, Notifications,
  Billing, Fulfillment, Orders all stopped, "nothing left running, no port
  this stack bound is still held".
- `pnpm dc:down:infra` removed all infra containers
  (`otcnet-mssql`, `otcnet-mailpit`, `otcnet-prometheus`,
  `otcnet-otel-collector`, `otcnet-jaeger`, `otcnet-kafka`, plus the
  `otcnet-net` network) and any earlier ones already gone from a prior
  state.
- `docker ps` after teardown: empty (no containers of any kind).
- `pgrep -fa` for the six .NET services and for `next start`: no matches.
- `./scripts/dev-stack.sh status`: all seven (six services + web) reported
  `stopped`.

## What I could not do / anything that surprised me

- Nothing was blocked. The script ran clean on the first attempt against a
  freshly-started stack; no selector or timing fix was needed, so there was
  no divergence from the adapted script above.
- One thing worth flagging for the leader, not a blocker: `pnpm stack:stop`
  by design leaves the infra containers running (documented in
  `scripts/dev-stack.sh`'s own header comment, "infra containers left
  running" for faster subsequent restarts). Since nothing was running
  before this sub-task started, I additionally ran `pnpm dc:down:infra` to
  return the machine to its prior state, rather than leaving MSSQL/Kafka/etc
  running as a side effect of a documentation task.

## Traceability

Not applicable — this is capture tooling for phase 24's documentation demo
(id 37), not a spec-backed feature with `R<n>` acceptance criteria or a
`specs/shared/test-matrix.md` row.
