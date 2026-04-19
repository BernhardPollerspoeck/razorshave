// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { Component, LayoutComponent } from './component.js';
import { h } from './h.js';
import { mount } from './mount.js';

function nextFrame() {
  return new Promise(r => requestAnimationFrame(r));
}

describe('Component', () => {
  it('renders initial DOM into the container via mount()', () => {
    class Hello extends Component {
      render() { return h('p', null, 'hi'); }
    }
    const c = document.createElement('div');
    mount(Hello, c);
    expect(c.querySelector('p').textContent).toBe('hi');
  });

  it('rerenders and replaces DOM after stateHasChanged()', async () => {
    class Counter extends Component {
      constructor() { super(); this.count = 0; }
      bump() { this.count++; this.stateHasChanged(); }
      render() { return h('span', null, String(this.count)); }
    }
    const c = document.createElement('div');
    const instance = mount(Counter, c);

    expect(c.querySelector('span').textContent).toBe('0');
    instance.bump();
    await nextFrame();
    expect(c.querySelector('span').textContent).toBe('1');
  });

  it('batches multiple stateHasChanged calls into one render', async () => {
    let renderCount = 0;
    class Spy extends Component {
      render() { renderCount++; return h('div'); }
    }
    const c = document.createElement('div');
    const instance = mount(Spy, c);
    expect(renderCount).toBe(1);

    instance.stateHasChanged();
    instance.stateHasChanged();
    instance.stateHasChanged();
    await nextFrame();

    expect(renderCount).toBe(2);
  });

  it('runs event handlers and triggers a rerender automatically', async () => {
    class Clicker extends Component {
      constructor() { super(); this.clicks = 0; }
      onClick() { this.clicks++; }
      render() {
        return h('button', { onclick: (_e) => this.onClick() }, String(this.clicks));
      }
    }
    const c = document.createElement('div');
    mount(Clicker, c);
    c.querySelector('button').click();
    await nextFrame();
    await nextFrame();
    expect(c.querySelector('button').textContent).toBe('1');
  });

  it('respects shouldRender() returning false', async () => {
    let renders = 0;
    class Frozen extends Component {
      shouldRender() { return false; }
      render() { renders++; return h('p'); }
    }
    const c = document.createElement('div');
    const instance = mount(Frozen, c);
    const first = renders;
    instance.stateHasChanged();
    await nextFrame();
    expect(renders).toBe(first);
  });
});

describe('LayoutComponent', () => {
  it('exposes props.body via the body getter', () => {
    const layout = new LayoutComponent();
    layout.props = { body: 'x' };
    expect(layout.body).toBe('x');
  });
});
