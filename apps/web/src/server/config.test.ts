// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest';
import { cookieSecure, gatewayBaseUrl, sessionPassword } from './config';

const KEYS = ['GATEWAY_BASE_URL', 'GATEWAY_PORT', 'ORDERS_HEALTH_PORT', 'WEB_PORT', 'WEB_SESSION_PASSWORD', 'WEB_COOKIE_SECURE'] as const;
const saved = Object.fromEntries(KEYS.map((key) => [key, process.env[key]]));

afterEach(() => {
  for (const key of KEYS) {
    if (saved[key] === undefined) delete process.env[key];
    else process.env[key] = saved[key];
  }
});

describe('server configuration', () => {
  it('reaches the Gateway on GATEWAY_PORT — and not on a sibling *_PORT (substitution)', () => {
    delete process.env.GATEWAY_BASE_URL;
    process.env.GATEWAY_PORT = '4555';
    process.env.ORDERS_HEALTH_PORT = '4666';
    process.env.WEB_PORT = '4777';
    expect(gatewayBaseUrl()).toBe('http://localhost:4555');
  });

  it('defaults to the .env.example Gateway port, and an explicit GATEWAY_BASE_URL wins (trailing slash dropped)', () => {
    delete process.env.GATEWAY_BASE_URL;
    delete process.env.GATEWAY_PORT;
    expect(gatewayBaseUrl()).toBe('http://localhost:3001');
    process.env.GATEWAY_BASE_URL = 'http://gateway:3001/';
    process.env.GATEWAY_PORT = '9999';
    expect(gatewayBaseUrl()).toBe('http://gateway:3001');
  });

  it('seals with WEB_SESSION_PASSWORD, refuses a short one, and otherwise uses a stable random per-process key', () => {
    process.env.WEB_SESSION_PASSWORD = 'x'.repeat(32);
    expect(sessionPassword()).toBe('x'.repeat(32));
    process.env.WEB_SESSION_PASSWORD = 'too-short';
    expect(() => sessionPassword()).toThrow('WEB_SESSION_PASSWORD must be at least 32 characters long.');
    delete process.env.WEB_SESSION_PASSWORD;
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const generated = sessionPassword();
    expect(generated).toMatch(/^[0-9a-f]{64}$/);
    expect(sessionPassword()).toBe(generated);
    expect(warn).toHaveBeenCalledTimes(1);
  });

  it('the session cookie is Secure unless WEB_COOKIE_SECURE=false', () => {
    delete process.env.WEB_COOKIE_SECURE;
    expect(cookieSecure()).toBe(true);
    process.env.WEB_COOKIE_SECURE = 'false';
    expect(cookieSecure()).toBe(false);
  });
});
