'use client';

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { useState, type FormEvent } from 'react';
import { ErrorMessage } from '@/components/error-message';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import type { LoginRequest, SessionInfo } from '@/lib/api-types';
import { apiRequest } from '@/lib/api-client';

/**
 * The login form. It is a real HTML form (`method="post"` to the login route
 * handler), so a submit that happens before React has hydrated still signs in
 * — the password travels in a POST body, never a URL — and the submit button
 * is NEVER disabled waiting for hydration. Once hydrated, the same submit is
 * intercepted and sent as JSON so the error can be shown in place.
 */
export function LoginForm({ initialError, onSignedIn }: { initialError?: string; onSignedIn?: () => void }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [username, setUsername] = useState('operator');
  const [password, setPassword] = useState('');

  const login = useMutation({
    mutationFn: async (credentials: LoginRequest) => (await apiRequest<SessionInfo>('/api/auth/login', { method: 'POST', json: credentials })).data,
    onSuccess: (session) => {
      queryClient.setQueryData(['session'], session);
      if (onSignedIn) {
        onSignedIn();
      } else {
        router.replace('/orders');
        router.refresh();
      }
    },
  });

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    login.mutate({ username, password });
  }

  return (
    <Card className="w-full max-w-sm">
      <CardHeader>
        <CardTitle>Order-To-Cash</CardTitle>
        <CardDescription>Sign in with the operator credentials (GATEWAY_OPERATOR_USERNAME / GATEWAY_OPERATOR_PASSWORD in .env).</CardDescription>
      </CardHeader>
      <CardContent>
        <form method="post" action="/api/auth/login" className="flex flex-col gap-4" onSubmit={submit} data-testid="login-form">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="username">Username</Label>
            <Input id="username" name="username" autoComplete="username" required value={username} onChange={(event) => setUsername(event.target.value)} />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="password">Password</Label>
            <Input id="password" name="password" type="password" autoComplete="current-password" required value={password} onChange={(event) => setPassword(event.target.value)} />
          </div>
          {login.isError ? (
            <ErrorMessage error={login.error} fallback="Sign-in failed." testId="login-error" />
          ) : initialError && login.isIdle ? (
            <p role="alert" className="text-sm text-destructive" data-testid="login-error">
              {initialError}
            </p>
          ) : null}
          <Button type="submit" className="w-full" disabled={login.isPending}>
            {login.isPending ? 'Signing in…' : 'Sign in'}
          </Button>
        </form>
      </CardContent>
    </Card>
  );
}
