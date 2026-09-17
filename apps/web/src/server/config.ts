import 'server-only';
import { randomBytes } from 'node:crypto';

/**
 * Server-side configuration, read from the environment at REQUEST time (never
 * inlined into a client bundle — this module is `server-only`). `pnpm dev` and
 * `pnpm start` load the repository root `.env` with Node's `--env-file-if-exists`.
 */
export function gatewayBaseUrl(): string {
  const explicit = process.env.GATEWAY_BASE_URL?.trim();
  if (explicit) return explicit.replace(/\/+$/, '');
  const port = process.env.GATEWAY_PORT?.trim() || '3001';
  return `http://localhost:${port}`;
}

/**
 * The generated fallback key lives on `globalThis`, not in a module variable:
 * Next.js bundles route handlers and server components separately, so a
 * module-level variable exists once PER BUNDLE — the login route sealed with
 * one random key and the page layout tried to unseal with another, and every
 * sign-in bounced back to /login. Found in a real browser against `next start`
 * (progress/impl_web_app.md, live verification); guarded by
 * tests-integration/stream-proxy.test.ts, whose server runs on this fallback.
 */
const GENERATED_KEY = Symbol.for('otc.web.generatedSessionPassword');
type KeyHolder = { [GENERATED_KEY]?: string };

/**
 * The key the session cookie is sealed with (iron-session, AES-256-CBC + HMAC).
 * `WEB_SESSION_PASSWORD` (≥ 32 characters) in any real deployment; without it a
 * random per-process key is generated, which only means sessions do not survive
 * a restart of this server.
 */
export function sessionPassword(): string {
  const configured = process.env.WEB_SESSION_PASSWORD;
  if (configured && configured.length >= 32) return configured;
  if (configured) {
    throw new Error('WEB_SESSION_PASSWORD must be at least 32 characters long.');
  }
  const holder = globalThis as KeyHolder;
  if (!holder[GENERATED_KEY]) {
    holder[GENERATED_KEY] = randomBytes(32).toString('hex');
    console.warn('WEB_SESSION_PASSWORD is not set — sealing sessions with a random per-process key (sessions end when this server restarts).');
  }
  return holder[GENERATED_KEY];
}

/** `Secure` on the session cookie. On by default; browsers treat http://localhost as a secure context, so local development works with it on. */
export function cookieSecure(): boolean {
  return process.env.WEB_COOKIE_SECURE !== 'false';
}
