// @vitest-environment jsdom
//
// End-to-end routing scenario: an App component hosts a Router with three
// routes; we flip the URL via NavigationManager and programmatic history and
// assert the right component renders each time. Proves the runtime pieces
// (Router, NavigationManager, lifecycle, Component) wire together.
//
// Counter and Weather come straight from the transpiler snapshots — real
// output, not hand-written — giving the router test the same ground-truth
// guarantee the counter/weather integration tests already provide.

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { mount, navigationManager, Router, container } from '../index.js';
import { Component } from '../component.js';
import { h } from '../h.js';

import { Counter } from '../../../../tests/Razorshave.Transpiler.Tests/Fixtures/counter/Output.verified.js';
import { Weather } from '../../../../tests/Razorshave.Transpiler.Tests/Fixtures/weather/Output.verified.js';

class Home extends Component {
  render() { return h('h1', { id: 'home' }, 'Home'); }
}

const routes = [
  { pattern: '/',        component: Home },
  { pattern: '/counter', component: Counter },
  { pattern: '/weather', component: Weather },
];

class App extends Component {
  render() { return h(Router, { routes }); }
}

async function flushAsync() {
  await Promise.resolve();
  await Promise.resolve();
  await new Promise(r => requestAnimationFrame(r));
}

describe('Router end-to-end', () => {
  beforeEach(() => {
    window.history.replaceState(null, '', '/');
    container.clear();
    container.register('IWeatherApi', () => ({ getForecastsAsync: async () => [] }));
  });
  afterEach(() => container.clear());

  it('renders the matching component for the initial URL', () => {
    const root = document.createElement('div');
    mount(App, root);
    expect(root.querySelector('#home')).not.toBeNull();
  });

  it('navigateTo() swaps the rendered component on the next frame', async () => {
    window.history.replaceState(null, '', '/');
    const root = document.createElement('div');
    mount(App, root);

    navigationManager.navigateTo('/counter');
    await flushAsync();

    expect(root.querySelector('#home')).toBeNull();
    expect(root.querySelector('button.btn-primary')?.textContent).toBe('Click me');
  });

  it('navigating to a route whose component uses [Inject] resolves services', async () => {
    const root = document.createElement('div');
    mount(App, root);

    navigationManager.navigateTo('/weather');
    await flushAsync();

    // Weather is present with its loading paragraph (forecast array is empty
    // because our fake api returns []).
    expect(root.textContent).toMatch(/Weather/);
  });

  it('popstate (back button) brings the previous route back', async () => {
    const root = document.createElement('div');
    mount(App, root);

    navigationManager.navigateTo('/counter');
    await flushAsync();
    navigationManager.navigateTo('/weather');
    await flushAsync();
    expect(root.textContent).toMatch(/Weather/);

    window.history.back();
    // jsdom synchronously fires popstate from history.back().
    await flushAsync();
    expect(root.querySelector('button.btn-primary')).not.toBeNull();
  });

  it('unknown path falls back to the built-in "not found" message', async () => {
    const root = document.createElement('div');
    mount(App, root);
    navigationManager.navigateTo('/does-not-exist');
    await flushAsync();
    // Default NotFound component renders "Page not found" + the unmatched path.
    expect(root.textContent).toMatch(/Page not found/);
    expect(root.textContent).toContain('/does-not-exist');
  });
});

describe('Router with defaultLayout', () => {
  beforeEach(() => {
    window.history.replaceState(null, '', '/');
    container.clear();
  });

  it('wraps the matched component inside the default layout', () => {
    class Layout extends Component {
      render() {
        return h('div', { id: 'layout-frame' }, this.props.body);
      }
    }
    class Page extends Component {
      render() { return h('main', { id: 'page' }, 'inside'); }
    }
    class Root extends Component {
      render() {
        return h(Router, {
          routes: [{ pattern: '/', component: Page }],
          defaultLayout: Layout,
        });
      }
    }

    const root = document.createElement('div');
    mount(Root, root);

    // Layout surrounds the page — <div id="layout-frame"><main id="page">...
    const layoutFrame = root.querySelector('#layout-frame');
    expect(layoutFrame).not.toBeNull();
    expect(layoutFrame.querySelector('#page')).not.toBeNull();
  });
});
