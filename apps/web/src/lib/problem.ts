import type { Problem, StockUnavailableProblem } from '@/lib/api-types';

/**
 * Every error the browser sees from this app's own `/api/*` route handlers is
 * the Gateway's RFC 9457 problem document, forwarded VERBATIM — same status,
 * same body, same `application/problem+json` content type (src/server/gateway.ts).
 * There is no framework envelope in between, so the problem is the parsed body
 * itself, not a field of it.
 *
 * That sentence is the whole of #7's most expensive web defect in reverse:
 * #7's pages read `error.data.detail` while Nitro had wrapped the problem one
 * level deeper, so every error rendered generic text from the day the app was
 * built (review_web_app.md Pass 7). The guard is not this comment — it is the
 * tests that feed REAL Gateway problem bodies (captured from a running #8
 * Gateway, src/test/fixtures/gateway-problems/) through a real route handler
 * and assert the page shows that body's own `detail`.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: Partial<Problem> | undefined,
  ) {
    super(problem?.detail ?? problem?.title ?? `Request failed with status ${status}`);
    this.name = 'ApiError';
  }
}

function isProblemLike(value: unknown): value is Partial<Problem> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) && ('detail' in value || 'title' in value || 'code' in value);
}

/** Parses a non-2xx response body into a problem document, or `undefined` when it is not one (an HTML error page, an empty body, a network proxy's text). */
export async function readProblem(response: Response): Promise<Partial<Problem> | undefined> {
  const text = await response.text().catch(() => '');
  if (!text) return undefined;
  try {
    const parsed: unknown = JSON.parse(text);
    return isProblemLike(parsed) ? parsed : undefined;
  } catch {
    return undefined;
  }
}

/** The one line an error-rendering component shows: the problem's own `detail`, else its `title`, else `fallback` — never a generic message while the server said something specific. */
export function describeError(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    const detail = error.problem?.detail?.trim();
    if (detail) return detail;
    const title = error.problem?.title?.trim();
    if (title) return title;
  }
  return fallback;
}

/** The `shortages` of a 409 `STOCK_UNAVAILABLE` problem, or `undefined` when the error is anything else or carries none. */
export function stockShortages(error: unknown): StockUnavailableProblem['shortages'] | undefined {
  if (!(error instanceof ApiError)) return undefined;
  const shortages = (error.problem as Partial<StockUnavailableProblem> | undefined)?.shortages;
  return Array.isArray(shortages) && shortages.length > 0 ? shortages : undefined;
}
