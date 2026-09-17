'use client';

import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { ApiError } from '@/lib/problem';

type Navigate = (url: string) => void;

// A full navigation on purpose: the session is gone, so every cached query is stale and the server layout must re-run.
// `location.assign`, not `useRouter`: this runs in a QueryCache callback, outside any component, where useRouter is unavailable.
const browserNavigate: Navigate = (url) => window.location.assign(url);
let navigateSignedOut: Navigate = browserNavigate;

/** Test seam: observe the sign-out navigation in a DOM test, where jsdom's `location.assign` cannot be observed. */
export function setSignedOutNavigation(next: Navigate | undefined): void {
  navigateSignedOut = next ?? browserNavigate;
}

function redirectOnSignedOut(error: unknown): void {
  if (error instanceof ApiError && error.status === 401 && typeof window !== 'undefined' && window.location.pathname !== '/login') {
    navigateSignedOut('/login');
  }
}

export function makeQueryClient(): QueryClient {
  return new QueryClient({
    queryCache: new QueryCache({ onError: redirectOnSignedOut }),
    mutationCache: new MutationCache({ onError: redirectOnSignedOut }),
    defaultOptions: {
      queries: {
        // A 4xx will not change by asking again; a 5xx/network failure gets one retry before the error is shown.
        retry: (failureCount, error) => !(error instanceof ApiError && error.status < 500) && failureCount < 1,
        staleTime: 2_000,
      },
    },
  });
}

export function Providers({ children }: { children: ReactNode }) {
  const [queryClient] = useState(makeQueryClient);
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
