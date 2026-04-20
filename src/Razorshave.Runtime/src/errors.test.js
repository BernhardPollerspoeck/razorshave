// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { reportRuntimeError, setErrorHandler } from './errors.js';
import { Component, kickoffAsyncInit } from './component.js';
import { h } from './h.js';
import { render } from './vdom.js';

describe('error handler plumbing', () => {
  let captured;
  let originalConsoleError;

  beforeEach(() => {
    captured = [];
    setErrorHandler((err, ctx) => captured.push({ err, ctx }));
    originalConsoleError = console.error;
    console.error = vi.fn();
  });

  afterEach(() => {
    setErrorHandler(null); // installing non-function = keep previous; reset to default
    console.error = originalConsoleError;
  });

  it('setErrorHandler routes reportRuntimeError calls to the installed handler', () => {
    const boom = new Error('boom');
    reportRuntimeError(boom, { phase: 'test', component: 'X' });
    expect(captured).toHaveLength(1);
    expect(captured[0].err).toBe(boom);
    expect(captured[0].ctx).toEqual({ phase: 'test', component: 'X' });
  });

  it('a throwing user handler does not crash the runtime', () => {
    setErrorHandler(() => { throw new Error('handler itself threw'); });
    // Must not propagate — the runtime is more important than the report.
    expect(() => reportRuntimeError(new Error('original'), {})).not.toThrow();
  });

  it('synchronous throw from onInitializedAsync is caught and reported', () => {
    class BrokenInit extends Component {
      onInitializedAsync() { throw new Error('sync boom'); }
    }
    const inst = new BrokenInit();
    expect(() => kickoffAsyncInit(inst)).not.toThrow();
    expect(captured).toHaveLength(1);
    expect(captured[0].err.message).toBe('sync boom');
    expect(captured[0].ctx.phase).toBe('onInitializedAsync');
    expect(captured[0].ctx.component).toBe('BrokenInit');
  });

  it('rejected onInitializedAsync promise is caught and reported', async () => {
    class AsyncBroken extends Component {
      async onInitializedAsync() { throw new Error('async boom'); }
    }
    const inst = new AsyncBroken();
    kickoffAsyncInit(inst);
    await Promise.resolve(); await Promise.resolve();
    expect(captured).toHaveLength(1);
    expect(captured[0].err.message).toBe('async boom');
    expect(captured[0].ctx.phase).toBe('onInitializedAsync');
  });

  it('throwing event handler is reported and stateHasChanged still fires', async () => {
    const owner = {
      stateHasChanged: vi.fn(),
      constructor: { name: 'MyComp' },
    };
    const node = render(h('button', {
      onclick: () => { throw new Error('handler boom'); },
    }), owner);
    node.click();
    await Promise.resolve(); await Promise.resolve();

    expect(captured).toHaveLength(1);
    expect(captured[0].err.message).toBe('handler boom');
    expect(captured[0].ctx.phase).toBe('event handler');
    expect(captured[0].ctx.component).toBe('MyComp');
    expect(owner.stateHasChanged).toHaveBeenCalledOnce();
  });

  it('rejected async event handler is reported and stateHasChanged still fires', async () => {
    const owner = {
      stateHasChanged: vi.fn(),
      constructor: { name: 'MyComp' },
    };
    const node = render(h('button', {
      onclick: async () => { throw new Error('async handler boom'); },
    }), owner);
    node.click();
    await Promise.resolve(); await Promise.resolve();

    expect(captured).toHaveLength(1);
    expect(captured[0].err.message).toBe('async handler boom');
    expect(owner.stateHasChanged).toHaveBeenCalledOnce();
  });
});
