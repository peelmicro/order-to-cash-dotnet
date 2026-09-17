import { LoginForm } from '@/features/auth/login-form';
import { StackLabel } from '@/features/shell/stack-label';

export const dynamic = 'force-dynamic';

export default async function LoginPage({ searchParams }: { searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { error } = await searchParams;
  return (
    <main className="flex min-h-[80vh] flex-col items-center justify-center gap-4 px-6">
      <LoginForm initialError={typeof error === 'string' ? error : undefined} />
      <StackLabel />
    </main>
  );
}
