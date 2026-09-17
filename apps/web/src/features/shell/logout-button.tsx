'use client';

import { useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { apiRequest } from '@/lib/api-client';

/** Signs out. Also a plain form post, so it works before hydration. */
export function LogoutButton() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [pending, setPending] = useState(false);

  return (
    <form
      method="post"
      action="/api/auth/logout"
      onSubmit={async (event) => {
        event.preventDefault();
        setPending(true);
        try {
          // Through the app's one browser client, like every other request (id 29 fix round 2: the error-text sweep observes requests at that seam).
          await apiRequest('/api/auth/logout', { method: 'POST' });
        } catch {
          // Signing out proceeds whatever the answer: the cache is dropped and the user leaves below, and the route itself makes no upstream call that could fail (app/api/auth/logout/route.ts).
        } finally {
          queryClient.clear();
          router.replace('/login');
          router.refresh();
        }
      }}
    >
      <Button type="submit" variant="outline" size="sm" disabled={pending}>
        Log out
      </Button>
    </form>
  );
}
