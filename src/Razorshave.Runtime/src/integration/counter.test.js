// @vitest-environment jsdom
//
// Ground-truth integration test: we load the committed Counter snapshot — the
// literal file the transpiler produces — and run it against the live runtime.
// If this test passes, we know the whole pipeline wires up correctly: import
// alias resolves, element + component VDOM renders, event handler fires,
// stateHasChanged schedules a rerender that replaces the DOM.

import { describe, it, expect } from 'vitest';
import { mount } from '../index.js';
import { Counter } from '../../../../tests/Razorshave.Transpiler.Tests/Fixtures/counter/Output.verified.js';

function nextFrame() {
  return new Promise(r => requestAnimationFrame(r));
}

describe('Counter snapshot + runtime', () => {
  it('initial render displays count 0 and a Click me button', () => {
    const container = document.createElement('div');
    mount(Counter, container);

    const p = container.querySelector('p[role="status"]');
    expect(p?.textContent).toBe('Current count: 0');

    const button = container.querySelector('button.btn-primary');
    expect(button?.textContent).toBe('Click me');
  });

  it('clicking the button increments and rerenders via stateHasChanged', async () => {
    const container = document.createElement('div');
    mount(Counter, container);

    container.querySelector('button.btn-primary').click();
    await nextFrame();
    await nextFrame();

    expect(container.querySelector('p[role="status"]').textContent).toBe('Current count: 1');

    container.querySelector('button.btn-primary').click();
    container.querySelector('button.btn-primary').click();
    await nextFrame();
    await nextFrame();

    expect(container.querySelector('p[role="status"]').textContent).toBe('Current count: 3');
  });

  it('PageTitle sets document.title from the ChildContent RenderFragment', () => {
    const container = document.createElement('div');
    mount(Counter, container);

    expect(document.title).toBe('Counter');
  });
});
