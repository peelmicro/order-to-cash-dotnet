import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests drive the real stack — `scripts/dev-stack.sh` at the
 * repository root (docker compose infra + the six .NET services, built once,
 * plus `next build`/`next start` for the web app), never a self-spawned dev
 * server: Playwright's own `webServer` option is deliberately not used here.
 * #8 has no `docker-compose.apps.yml` yet (that is phase 23's job), so
 * `scripts/dev-stack.sh` is today's equivalent of #7's fully-composed stack —
 * see `progress/impl_e2e_playwright.md` for why.
 *
 * Base URL: `E2E_BASE_URL` if set, else `http://localhost:${WEB_PORT}`
 * (falls back to 3010, `scripts/dev-stack.sh`'s own default, if `WEB_PORT`
 * itself is unset). Deliberately no hardcoded port in prose here: `.env`'s
 * `WEB_PORT` is the source of truth and it moves between environments, so
 * read it (or export `E2E_BASE_URL` explicitly — see `package.json`'s
 * `test:e2e` script / README) rather than trusting a number written in a
 * comment.
 */
const baseURL = process.env.E2E_BASE_URL ?? `http://localhost:${process.env.WEB_PORT ?? 3010}`;

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: {
    // The saga crosses six services (Orders, Fulfillment, Billing,
    // Notifications, Projector, Gateway) over Kafka/NATS/MongoDB — locally
    // observed to settle in well under a second, but this is a shared dev
    // box, not a dedicated CI runner, so the auto-waiting assertion timeout
    // is generous rather than tight. Never used to paper over a genuine
    // race — see CLAUDE.md and progress/impl_e2e_playwright.md for the
    // synchronisation rule this suite follows (terminal/monotonic state
    // only, never a transient one).
    timeout: 30_000,
  },
  // Deliberately 0: a flaky e2e suite that "passes on retry" teaches people
  // to re-run rather than investigate. If a run is flaky, the fix is better
  // synchronisation (an auto-waiting assertion on terminal/monotonic state),
  // not a retry budget that hides it.
  retries: 0,
  fullyParallel: true,
  reporter: [['list']],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'setup',
      testMatch: /global\.setup\.ts/,
    },
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        // Not './e2e/.auth/operator.json' (#7's path) — see
        // global.setup.ts's STORAGE_STATE comment for why.
        storageState: './test-results/.auth/operator.json',
      },
      dependencies: ['setup'],
    },
  ],
});
