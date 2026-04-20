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

  // Regression for v0.1 Item 1: before instance reuse was keyed by
  // (Ctor, occurrence-index), two <Counter /> siblings collided on the same
  // parent._childInstances.get(Ctor) slot. The second Counter stole the
  // first's instance on rerender, so clicking one bumped both counters in
  // lockstep and state was effectively shared.
  it('two sibling <Counter /> instances keep independent state', async () => {
    class TwoCounters extends Component {
      render() {
        return h('div', null,
          h('section', { 'data-idx': '0' }, h(Counter, {})),
          h('section', { 'data-idx': '1' }, h(Counter, {}))
        );
      }
    }
    const container = document.createElement('div');
    const wrapper = mount(TwoCounters, container);

    const status = () => Array.from(container.querySelectorAll('p[role="status"]')).map(p => p.textContent);
    const buttons = () => container.querySelectorAll('button.btn-primary');

    expect(status()).toEqual(['Current count: 0', 'Current count: 0']);

    // Click the first counter twice, second counter once.
    buttons()[0].click();
    buttons()[0].click();
    buttons()[1].click();
    await nextFrame();
    await nextFrame();
    expect(status()).toEqual(['Current count: 2', 'Current count: 1']);

    // Force a parent rerender — both independent states must survive.
    wrapper.stateHasChanged();
    await nextFrame();
    expect(status()).toEqual(['Current count: 2', 'Current count: 1']);

    buttons()[1].click();
    await nextFrame();
    await nextFrame();
    expect(status()).toEqual(['Current count: 2', 'Current count: 2']);
  });
});
