import { act } from '@testing-library/react';
import type { EventSourceLike } from '@/lib/order-stream-client';

/**
 * A controllable stand-in for the browser's EventSource: the test decides
 * which frames arrive and when. Only the transport is fake — the client's
 * de-duplication and reconnection logic runs unmodified (and is proven over a
 * real socket in src/lib/order-stream-client.test.ts).
 */
export class FakeEventSource implements EventSourceLike {
  static instances: FakeEventSource[] = [];
  readyState = 0;
  closed = false;
  private readonly listeners = new Map<string, ((event: { data?: unknown }) => void)[]>();

  constructor(readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  static reset(): void {
    FakeEventSource.instances = [];
  }

  static latest(): FakeEventSource {
    const latest = FakeEventSource.instances.at(-1);
    if (!latest) throw new Error('no EventSource was opened');
    return latest;
  }

  addEventListener(type: string, listener: (event: { data?: unknown }) => void): void {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  close(): void {
    this.closed = true;
    this.readyState = 2;
  }

  /** Delivers one frame, as the browser would dispatch it. */
  emit(type: string, data: unknown): void {
    if (this.closed) throw new Error(`frame ${type} emitted on a closed EventSource`);
    this.readyState = 1;
    act(() => {
      for (const listener of this.listeners.get(type) ?? []) listener({ data: JSON.stringify(data) });
    });
  }

  /** A transport error; `closed` models the browser giving up (readyState CLOSED). */
  fail(closed = false): void {
    this.readyState = closed ? 2 : 0;
    act(() => {
      for (const listener of this.listeners.get('error') ?? []) listener({});
    });
  }
}

export const fakeEventSourceFactory = (url: string): EventSourceLike => new FakeEventSource(url);
