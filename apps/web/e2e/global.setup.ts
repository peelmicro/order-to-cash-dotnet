import { expect, test as setup } from '@playwright/test';

/**
 * Logs in ONCE, through the real UI (`/login`, the real form, the real
 * `POST /api/auth/login` -> Gateway round trip), and saves the resulting
 * sealed session cookie as Playwright's `storageState`. Every other spec
 * file's `chromium` project depends on this project (see
 * `playwright.config.ts`), so the login flow itself is still exercised for
 * real exactly once per suite run, and every scenario spec starts already
 * authenticated rather than re-deriving the same login twice.
 *
 * Field ids confirmed live against apps/web/src/features/auth/login-form.tsx
 * (`#username`, `#password`, submit button text "Sign in") and
 * apps/web/src/app/login/page.tsx (route `/login`) before writing this.
 *
 * Written under `test-results/` (not `e2e/.auth/`, the path #7 uses) so the
 * sealed session cookie never needs its own `.gitignore` entry — the root
 * `.gitignore`'s existing `test-results/` pattern already covers it, and the
 * brief for this feature scopes edits to apps/web/e2e/,
 * apps/web/playwright.config.ts and apps/web/package.json only.
 */
const STORAGE_STATE = './test-results/.auth/operator.json';

setup('authenticate as the operator', async ({ page }) => {
  const username = process.env.GATEWAY_OPERATOR_USERNAME ?? 'operator';
  const password = process.env.GATEWAY_OPERATOR_PASSWORD;
  if (!password) {
    throw new Error('GATEWAY_OPERATOR_PASSWORD is not set — run via `pnpm --filter web run test:e2e` (loads the root .env) or export it directly.');
  }

  await page.goto('/login');

  const usernameField = page.locator('#username');
  const passwordField = page.locator('#password');
  await usernameField.fill(username);
  await passwordField.fill(password);

  const submit = page.getByRole('button', { name: 'Sign in' });
  await expect(submit).toBeEnabled();
  await submit.click();

  // Terminal evidence of a successful login: real navigation away from
  // /login into the authenticated area ((app)/layout.tsx redirects any
  // unauthenticated request straight back to /login otherwise).
  await expect(page).toHaveURL(/\/orders$/);

  await page.context().storageState({ path: STORAGE_STATE });
});
