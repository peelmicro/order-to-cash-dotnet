import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';
import { setApiFetch } from '@/lib/api-client';

// next/navigation needs a mounted App Router; components are rendered without
// one, so the router is a recorder tests can inspect.
export const routerMock = { replace: vi.fn(), push: vi.fn(), refresh: vi.fn(), back: vi.fn(), prefetch: vi.fn() };
vi.mock('next/navigation', () => ({
  // `redirect()` / `notFound()` end a server component by throwing, as Next does; the page sweep reads the thrown digest.
  redirect: (url: string) => {
    throw Object.assign(new Error(`NEXT_REDIRECT ${url}`), { digest: `NEXT_REDIRECT;replace;${url}` });
  },
  notFound: () => {
    throw Object.assign(new Error('NEXT_NOT_FOUND'), { digest: 'NEXT_HTTP_ERROR_FALLBACK;404' });
  },
  useRouter: () => routerMock,
  usePathname: () => '/orders',
  useSearchParams: () => new URLSearchParams(),
}));

afterEach(() => {
  cleanup();
  setApiFetch(undefined);
  vi.useRealTimers();
});
