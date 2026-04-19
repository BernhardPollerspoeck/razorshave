// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { Component } from './component.js';
import { h } from './h.js';
import { mount } from './mount.js';

describe('mount()', () => {
  it('instantiates the component and renders into the container', () => {
    class App extends Component {
      render() { return h('section', null, 'ok'); }
    }
    const c = document.createElement('div');
    const instance = mount(App, c);

    expect(instance).toBeInstanceOf(App);
    expect(c.querySelector('section').textContent).toBe('ok');
  });

  it('calls onInit before the first render', () => {
    const order = [];
    class App extends Component {
      onInit() { order.push('init'); }
      render() { order.push('render'); return h('p'); }
    }
    mount(App, document.createElement('div'));
    expect(order).toEqual(['init', 'render']);
  });

  it('throws when container is missing', () => {
    class App extends Component {}
    expect(() => mount(App, null)).toThrow(/container/);
  });
});
