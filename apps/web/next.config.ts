import type { NextConfig } from 'next';

const nextConfig: NextConfig = {
  // The Gateway is the only backend this app talks to, and it does so only
  // from route handlers (src/app/api/**) — never from the browser.
  poweredByHeader: false,
  reactStrictMode: true,
};

export default nextConfig;
