'use client';

import { useSyncExternalStore } from 'react';

const subscribe = () => () => undefined;

/**
 * An instant rendered in the viewer's locale and time zone. The server renders
 * the ISO string and the client swaps in the local form after hydration, so the
 * two never disagree (a hydration mismatch) about a time zone neither can know.
 */
export function DateTime({ value }: { value: string }) {
  const isClient = useSyncExternalStore(subscribe, () => true, () => false);
  return <time dateTime={value}>{isClient ? new Date(value).toLocaleString() : value}</time>;
}
