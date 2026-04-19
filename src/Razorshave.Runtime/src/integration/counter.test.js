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

// Regression test for the M0 child-state-loss bug: before instance reuse was
// wired into vdom.js, every parent rerender rebuilt the child component tree
// from scratch and wiped Counter's currentCount. The production app hit this
// via `mount(App) → Router → Counter`: clicking the button triggered a root
// rerender, and the UI flashed back to 0. This test locks down the fix.
import { Component } from '../component.js';
import { h } from '../h.js';

describe('Counter nested in a parent component', () => {
  it('preserves currentCount across explicit parent rerenders', async () => {
    class Wrapper extends Component {
      render() { return h(Counter, {}); }
    }
    const container = document.createElement('div');
    const wrapper = mount(Wrapper, container);

    container.querySelector('button.btn-primary').click();
    await nextFrame();
    await nextFrame();
    expect(container.querySelector('p[role="status"]').textContent).toBe('Current count: 1');

    // Force a parent rerender — state must survive.
    wrapper.stateHasChanged();
    await nextFrame();
    expect(container.querySelector('p[role="status"]').textContent).toBe('Current count: 1');

    container.querySelector('button.btn-primary').click();
    await nextFrame();
    await nextFrame();
    expect(container.querySelector('p[role="status"]').textContent).toBe('Current count: 2');
  });
});
