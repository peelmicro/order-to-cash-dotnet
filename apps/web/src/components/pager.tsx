'use client';

import { Button } from '@/components/ui/button';
import type { PageInfo } from '@/lib/api-types';

export function totalPages(page: PageInfo | undefined): number {
  if (!page || page.pageSize <= 0) return 1;
  return Math.max(1, Math.ceil(page.total / page.pageSize));
}

/** Previous / next paging for a `PageInfo`-carrying list. */
export function Pager({ page, current, noun, onChange, testId }: { page: PageInfo | undefined; current: number; noun: string; onChange: (page: number) => void; testId: string }) {
  const pages = totalPages(page);
  return (
    <div className="flex items-center justify-between gap-4" data-testid={testId}>
      <span className="text-sm text-muted-foreground">
        {/* Until the list has answered there is no count to state: "· 0 orders" beside "Loading orders…" would claim an empty list. */}
        {page ? `Page ${page.page} of ${pages} · ${page.total} ${noun}` : `Page ${current}`}
      </span>
      <div className="flex gap-2">
        <Button type="button" variant="outline" size="sm" disabled={current <= 1} onClick={() => onChange(current - 1)} data-testid={`${testId}-prev`}>
          Previous
        </Button>
        <Button type="button" variant="outline" size="sm" disabled={current >= pages} onClick={() => onChange(current + 1)} data-testid={`${testId}-next`}>
          Next
        </Button>
      </div>
    </div>
  );
}
