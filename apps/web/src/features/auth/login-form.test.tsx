import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { NextRequest } from 'next/server';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { POST as loginRoute } from '@/app/api/auth/login/route';
import { setApiFetch } from '@/lib/api-client';
import { FakeGateway, sendRaw } from '@/test/fake-gateway';
import { fixtureDetail, gatewayFixture } from '@/test/gateway-fixtures';
import { deferred, json, renderWithQuery, routeApi } from '@/test/render';
import { routerMock } from '@/test/setup';
import { LoginForm } from './login-form';

describe('LoginForm', () => {
  beforeEach(() => {
    routerMock.replace.mockClear();
  });

  it('is a real form posting to the login route, so a submit before hydration still signs in (no password in a URL)', () => {
    renderWithQuery(<LoginForm />);
    const form = screen.getByTestId('login-form');
    expect(form).toHaveAttribute('method', 'post');
    expect(form).toHaveAttribute('action', '/api/auth/login');
    expect(screen.getByLabelText('Username')).toHaveAttribute('name', 'username');
    expect(screen.getByLabelText('Password')).toHaveAttribute('name', 'password');
  });

  it('the submit button is operable on the very first render — never disabled waiting for hydration (#7 Pass 2a/4)', () => {
    renderWithQuery(<LoginForm />);
    const button = screen.getByRole('button', { name: 'Sign in' });
    expect(button).toBeEnabled();
    expect(button).toHaveAttribute('type', 'submit');
  });

  it('shows a pending state while the request is unresolved, then signs in and navigates to /orders', async () => {
    const response = deferred();
    const calls = routeApi({ 'POST /api/auth/login': () => response.promise });
    renderWithQuery(<LoginForm />);

    await userEvent.type(screen.getByLabelText('Password'), 'secret');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    const pending = await screen.findByRole('button', { name: 'Signing in…' });
    expect(pending).toBeDisabled();
    expect(calls[0]?.body).toEqual({ username: 'operator', password: 'secret' });

    response.release(json({ authenticated: true, username: 'operator' }));
    await waitFor(() => expect(routerMock.replace).toHaveBeenCalledWith('/orders'));
  });

  it('a rejected login shows the problem document\'s own title when it carries no detail (the optional element absent)', async () => {
    routeApi({ 'POST /api/auth/login': () => json({ type: 'about:blank', title: 'Too many sign-in attempts', status: 429, code: 'RATE_LIMITED' }, 429) });
    renderWithQuery(<LoginForm />);
    fireEvent.submit(screen.getByTestId('login-form'));
    expect((await screen.findByTestId('login-error')).textContent).toBe('Too many sign-in attempts');
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeEnabled();
  });

  it('renders an error handed back by a no-JavaScript sign-in (the ?error= of the 303), until the user tries again', () => {
    renderWithQuery(<LoginForm initialError="username or password is incorrect" />);
    expect(screen.getByTestId('login-error')).toHaveTextContent('username or password is incorrect');
  });

  describe('against the real login route handler and a Gateway answering with its REAL captured 401', () => {
    const gateway = new FakeGateway();

    beforeAll(async () => {
      gateway.on('POST', '/auth/login', (_req, res) => {
        const fixture = gatewayFixture('login-bad-credentials-401');
        sendRaw(res, fixture.status, fixture.body, fixture.headers);
      });
      process.env.GATEWAY_BASE_URL = await gateway.start();
    });

    afterAll(async () => {
      await gateway.stop();
    });

    it('shows exactly the Gateway\'s own words — "username or password is incorrect" — not a generic message', async () => {
      setApiFetch((input, init) => loginRoute(new NextRequest(new URL(input, 'http://web.test'), init as ConstructorParameters<typeof NextRequest>[1])));
      renderWithQuery(<LoginForm />);
      await userEvent.type(screen.getByLabelText('Password'), 'wrong');
      await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

      const error = await screen.findByTestId('login-error');
      expect(error.textContent).toBe(fixtureDetail('login-bad-credentials-401'));
      expect(error.textContent).toBe('username or password is incorrect');
      expect(routerMock.replace).not.toHaveBeenCalled();
    });

    it('a no-JavaScript sign-in the Gateway refuses comes back to the form showing that refusal\'s own words (route → ?error= → page)', async () => {
      const response = await loginRoute(new NextRequest('http://web.test/api/auth/login', { method: 'POST', body: 'username=operator&password=wrong', headers: { 'content-type': 'application/x-www-form-urlencoded' } }));
      expect(response.status).toBe(303);
      const handedBack = new URL(response.headers.get('location') ?? '').searchParams.get('error') ?? '<no ?error= on the redirect>';
      renderWithQuery(<LoginForm initialError={handedBack} />);
      expect(screen.getByTestId('login-error').textContent).toBe(fixtureDetail('login-bad-credentials-401'));
    });
  });
});
