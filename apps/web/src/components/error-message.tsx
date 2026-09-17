import { describeError } from '@/lib/problem';

/**
 * Renders THE error's own words — the problem document's `detail`, else its
 * `title` — with `fallback` only when the server said nothing usable. The
 * `data-testid` is the hook tests use to assert the real text is shown.
 */
export function ErrorMessage({ error, prefix, fallback, testId }: { error: unknown; prefix?: string; fallback: string; testId: string }) {
  return (
    <p role="alert" className="text-sm text-destructive" data-testid={testId}>
      {prefix ? `${prefix}: ` : ''}
      {describeError(error, fallback)}
    </p>
  );
}
