import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import type { ReactNode } from 'react';
import { LogoutButton } from '@/features/shell/logout-button';
import { NavLinks } from '@/features/shell/nav-links';
import { StackLabel } from '@/features/shell/stack-label';
import { readSession } from '@/server/session';

export const dynamic = 'force-dynamic';

/** Every page in this group needs a live session; the check runs on the server, before anything renders. */
export default async function AppLayout({ children }: { children: ReactNode }) {
  const session = await readSession({ cookies: await cookies() });
  if (!session) redirect('/login');

  return (
    <div className="min-h-screen bg-background">
      <header className="border-b">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-x-4 gap-y-2 px-6 py-3">
          <NavLinks />
          <div className="flex flex-wrap items-center gap-3">
            <StackLabel />
            <span className="text-sm text-muted-foreground" data-testid="signed-in-as">
              {session.displayName ?? session.username}
            </span>
            <LogoutButton />
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-6xl px-6 py-8">{children}</main>
    </div>
  );
}
