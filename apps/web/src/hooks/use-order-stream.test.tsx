import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FakeEventSource, fakeEventSourceFactory } from '@/test/fake-event-source';
import { browserEventSourceFactory, useOrderStream, type UseOrderStreamOptions } from './use-order-stream';

function options(overrides: Partial<UseOrderStreamOptions> = {}): UseOrderStreamOptions {
  return {
    orderId: 'ord 1',
    enabled: true,
    seedEventIds: () => ['e0'],
    handlers: { onOrderUpdated: vi.fn(), onTimelineAppended: vi.fn(), onResync: vi.fn() },
    factory: fakeEventSourceFactory,
    ...overrides,
  };
}

describe('useOrderStream — the SSE hook, driven by a fake EventSource', () => {
  beforeEach(() => FakeEventSource.reset());

  it('opens nothing while disabled, then exactly one stream to this app\'s proxy once enabled', () => {
    const { rerender } = renderHook((props: UseOrderStreamOptions) => useOrderStream(props), { initialProps: options({ enabled: false }) });
    expect(FakeEventSource.instances).toHaveLength(0);
    rerender(options({ enabled: true }));
    expect(FakeEventSource.instances.map((s) => s.url)).toEqual(['/api/orders/stream?orderId=ord%201']);
    rerender(options({ enabled: true }));
    expect(FakeEventSource.instances).toHaveLength(1);
  });

  it('reports status transitions and routes each frame type to its own handler, with the snapshot seeded', () => {
    const handlers = { onOrderUpdated: vi.fn(), onTimelineAppended: vi.fn(), onResync: vi.fn() };
    const { result } = renderHook(() => useOrderStream(options({ handlers })));
    expect(result.current.status).toBe('connecting');
    const source = FakeEventSource.latest();
    source.emit('stream.ready', { cursor: 'c0', resumed: false });
    expect(result.current.status).toBe('connected');
    expect(handlers.onResync).toHaveBeenCalledTimes(1);

    source.emit('order.updated', { eventId: 'e0', orderId: 'ord 1', status: 'placed', occurredAt: 't' });
    source.emit('timeline.appended', { eventId: 'e0', orderId: 'ord 1', eventType: 'x', occurredAt: 't', summary: 's' });
    expect(handlers.onOrderUpdated).not.toHaveBeenCalled();
    expect(handlers.onTimelineAppended).not.toHaveBeenCalled();

    source.emit('order.updated', { eventId: 'e1', orderId: 'ord 1', status: 'confirmed', occurredAt: 't' });
    source.emit('timeline.appended', { eventId: 'e1', orderId: 'ord 1', eventType: 'x', occurredAt: 't', summary: 's' });
    expect(handlers.onOrderUpdated.mock.calls.map(([u]) => u.eventId)).toEqual(['e1']);
    expect(handlers.onTimelineAppended.mock.calls.map(([e]) => e.eventId)).toEqual(['e1']);

    source.fail();
    expect(result.current.status).toBe('reconnecting');
    source.emit('ping', { at: 't' });
    expect(result.current.status).toBe('connected');
  });

  it('a factory that is a NEW function on every render still opens ONE stream (the minified production build creates one per render)', () => {
    const handlers = { onOrderUpdated: vi.fn(), onTimelineAppended: vi.fn(), onResync: vi.fn() };
    const { rerender } = renderHook((props: UseOrderStreamOptions) => useOrderStream(props), {
      initialProps: options({ handlers, factory: (url) => fakeEventSourceFactory(url) }),
    });
    for (let i = 0; i < 5; i += 1) {
      rerender(options({ handlers, factory: (url) => fakeEventSourceFactory(url) }));
      FakeEventSource.latest().emit('ping', { at: 't' });
    }
    expect(FakeEventSource.instances.map((s) => s.url), 'EventSources opened across 6 renders').toEqual(['/api/orders/stream?orderId=ord%201']);
  });

  it('with no factory it uses the browser EventSource, once, however often it re-renders', () => {
    const created: string[] = [];
    vi.stubGlobal('EventSource', class {
      readyState = 0;
      constructor(url: string) {
        created.push(url);
      }
      addEventListener() {}
      close() {}
    });
    const { rerender } = renderHook((props: UseOrderStreamOptions) => useOrderStream(props), { initialProps: options({ factory: undefined }) });
    rerender(options({ factory: undefined }));
    rerender(options({ factory: undefined }));
    expect(created, 'browser EventSources opened across 3 renders').toEqual(['/api/orders/stream?orderId=ord%201']);
    vi.unstubAllGlobals();
  });

  it('always calls the LATEST handlers, without reopening the stream', () => {
    const first = { onOrderUpdated: vi.fn(), onTimelineAppended: vi.fn(), onResync: vi.fn() };
    const second = { onOrderUpdated: vi.fn(), onTimelineAppended: vi.fn(), onResync: vi.fn() };
    const { rerender } = renderHook((props: UseOrderStreamOptions) => useOrderStream(props), { initialProps: options({ handlers: first }) });
    rerender(options({ handlers: second }));
    FakeEventSource.latest().emit('order.updated', { eventId: 'e5', orderId: 'ord 1', status: 'paid', occurredAt: 't' });
    expect(first.onOrderUpdated).not.toHaveBeenCalled();
    expect(second.onOrderUpdated).toHaveBeenCalledTimes(1);
    expect(FakeEventSource.instances).toHaveLength(1);
  });

  it('retry closes the old stream and opens a new one; unmount closes it', () => {
    const { result, unmount } = renderHook(() => useOrderStream(options()));
    const first = FakeEventSource.latest();
    first.fail(true);
    expect(result.current.status).toBe('gave-up');
    act(() => result.current.retry());
    expect(first.closed).toBe(true);
    const second = FakeEventSource.latest();
    expect(second).not.toBe(first);
    expect(result.current.status).toBe('connecting');
    unmount();
    expect(second.closed).toBe(true);
  });

  it('the production factory is the browser\'s own EventSource', () => {
    const created: string[] = [];
    vi.stubGlobal('EventSource', class {
      constructor(url: string) {
        created.push(url);
      }
    });
    browserEventSourceFactory('/api/orders/stream?orderId=x');
    expect(created).toEqual(['/api/orders/stream?orderId=x']);
    vi.unstubAllGlobals();
  });
});
