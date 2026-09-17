import type { Metadata } from 'next';
import type { ReactNode } from 'react';
import { APP_TITLE } from '@/lib/stack-label';
import { Providers } from './providers';
import './globals.css';

export const metadata: Metadata = {
  title: APP_TITLE,
  description: 'Order lifecycle console — orders, live saga timeline, stock and billing.',
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body className="min-h-screen antialiased">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
