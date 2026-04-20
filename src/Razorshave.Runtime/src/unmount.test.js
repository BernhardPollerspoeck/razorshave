// @vitest-environment jsdom
//
// Lifecycle for the public `unmount()` handle returned by mount(). Covers
// listener cleanup (no leaks across multiple mounts), onDestroy firing,
// container clearing, and active-root stack hygiene.

import { describe, it, expect, vi } from 'vitest';
import { mount } from './mount.js';
import { Component } from './component.js';
import { h } from './h.js';
import { navigationManager } from './navigation-manager.js';

function nextFrame() { return new Promise(r => requestAnimationFrame(r)); }

describe('unmount()', () => {
  it('removes the DOM from the container', () => {
    class App extends Component {
      render() { return h('p', { class: 'hello' }, 'hi'); }
    }
    const c = document.createElement('div');
    const instance = mount(App, c);
    expect(c.querySelector('p.hello')).not.toBeNull();

    instance.unmount();
    expect(c.querySelector('p.hello')).toBeNull();
    expect(c.children.length).toBe(0);
  });

  it('fires onDestroy on every component in the tree', () => {
    const destroyed = [];
    class Leaf extends Component {
      onInit() { this.name = this.props.name; }
      onDestroy() { destroyed.push(this.name); }
      render() { return h('span', null, this.props.name); }
    }
    class Tree extends Component {
      onDestroy() { destroyed.push('root'); }
      render() {
        return h('div', null, h(Leaf, { name: 'a' }), h(Leaf, { name: 'b' }));
      }
    }
    const c = document.createElement('div');
    const instance = mount(Tree, c);

    instance.unmount();
    // Children first, then root.
    expect(destroyed).toEqual(['a', 'b', 'root']);
  });

  it('unsubscribes from navigation events — later navigateTo does not rerender', async () => {
    let renders = 0;
    class App extends Component {
      render() { renders++; return h('span', null, 'x'); }
    }
    const c = document.createElement('div');
    const instance = mount(App, c);
    expect(renders).toBe(1);

    instance.unmount();
    const before = renders;
    navigationManager.navigateTo('/something-new');
    await nextFrame();
    await nextFrame();
    expect(renders).toBe(before); // no further renders after unmount
  });

  it('calling unmount() twice is a no-op', () => {
    class App extends Component {
      render() { return h('span'); }
    }
    const c = document.createElement('div');
    const instance = mount(App, c);
    instance.unmount();
    expect(() => instance.unmount()).not.toThrow();
  });

  it('active-root stack: second mount becomes active, unmount restores first', async () => {
    // Multiple concurrent mounts: StateHasChanged bubbling should hit the
    // top-of-stack instance. After the second unmounts, the first becomes
    // active again.
    let rendersA = 0, rendersB = 0;
    class A extends Component {
      render() { rendersA++; return h('span'); }
    }
    class B extends Component {
      render() { rendersB++; return h('span'); }
    }
    const cA = document.createElement('div');
    const cB = document.createElement('div');
    const a = mount(A, cA);
    const b = mount(B, cB);
    expect(rendersA).toBe(1);
    expect(rendersB).toBe(1);

    // A child-bubble (no _container) hits active root = b.
    // Forcing through getActiveRoot isn't directly testable, but
    // unmounting B should restore A as target.
    b.unmount();

    // Rerender a via its own stateHasChanged — should work.
    a.stateHasChanged();
    await nextFrame();
    expect(rendersA).toBe(2);

    a.unmount();
  });

  it('a late-resolving onInitializedAsync after unmount does NOT trigger a render', async () => {
    // Regression for the kickoffAsyncInit / unmount race. Previously the
    // resolved promise called stateHasChanged on the detached instance,
    // which reached patchRoot with a null container and threw deep in
    // the reconciler. `_destroyed` short-circuits the bubble.
    let renderCount = 0;
    let releaseAsyncInit;
    const pending = new Promise(r => { releaseAsyncInit = r; });

    class Slow extends Component {
      async onInitializedAsync() { await pending; this.ready = true; }
      render() { renderCount++; return h('p', null, this.ready ? 'ready' : 'loading'); }
    }

    const c = document.createElement('div');
    const inst = mount(Slow, c);
    // First render is synchronous; async-init is in flight.
    expect(renderCount).toBe(1);

    inst.unmount();

    // Release the promise AFTER unmount — the bubble attempt must be
    // swallowed silently.
    releaseAsyncInit();
    await pending;
    await nextFrame();

    // No additional render — render count stays at 1.
    expect(renderCount).toBe(1);
  });
});
