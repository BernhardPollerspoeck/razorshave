// @vitest-environment jsdom
//
// Reconciler type-transition matrix. The reconciler has four distinct node
// shapes — text, element, markup, component — plus `null`/`false` (void).
// A single render() can swap between any of them: `cond ? "yes" : <Comp/>`
// changes type from text to component; `cond ? <span/> : null` toggles
// void ↔ element. The reconciler has to drop the old DOM and build the new
// shape without leaving stale listeners, zombie component instances, or
// dangling markers.
//
// This file targets the transitions that actually show up in Blazor code
// rather than exhaustively testing the 6×6 matrix; identity transitions
// (element → element, text → text) are covered by vdom.reconciler.test.js.

import { describe, it, expect } from 'vitest';
import { Component } from './component.js';
import { h, markup } from './h.js';
import { mount } from './mount.js';

function nextFrame() { return new Promise(r => requestAnimationFrame(r)); }

function makeToggle(renderA, renderB) {
  return class Toggle extends Component {
    constructor() { super(); this.a = true; }
    flip() { this.a = !this.a; this.stateHasChanged(); }
    render() { return this.a ? renderA() : renderB(); }
  };
}

describe('Reconciler type transitions', () => {
  it('text → element: replaces text node with the new element in place', async () => {
    const T = makeToggle(() => 'raw', () => h('span', { class: 's' }, 'x'));
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.textContent).toBe('raw');

    inst.flip();
    await nextFrame();
    expect(c.querySelector('span.s')).not.toBeNull();
    expect(c.textContent).toBe('x');
  });

  it('element → text: drops the element, inserts a plain text node', async () => {
    const T = makeToggle(() => h('em', null, 'bold'), () => 'plain');
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('em')).not.toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('em')).toBeNull();
    expect(c.textContent).toBe('plain');
  });

  it('element → markup: swaps a <div> for raw HTML', async () => {
    const T = makeToggle(() => h('p', null, 'p'), () => markup('<b>bold</b>'));
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('p')).not.toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('p')).toBeNull();
    expect(c.querySelector('b')).not.toBeNull();
    expect(c.querySelector('b').textContent).toBe('bold');
  });

  it('markup → element: raw HTML is replaced by a fresh element tree', async () => {
    const T = makeToggle(() => markup('<i>i</i>'), () => h('span', null, 's'));
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('i')).not.toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('i')).toBeNull();
    expect(c.querySelector('span')).not.toBeNull();
  });

  it('null → element: mounts DOM from nothing', async () => {
    // This case is already covered by the `null → content` block in
    // vdom.reconciler.test.js; re-asserted here for matrix completeness.
    const T = makeToggle(() => null, () => h('span', null, 'now'));
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('span')).toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('span').textContent).toBe('now');
  });

  it('element → null: tears down the DOM subtree cleanly', async () => {
    const T = makeToggle(() => h('span', null, 'x'), () => null);
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('span')).not.toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('span')).toBeNull();
  });

  it('element → component: swaps a plain element for a component subtree', async () => {
    class Widget extends Component {
      render() { return h('article', { class: 'widget' }, 'w'); }
    }
    const T = makeToggle(() => h('div', null, 'plain'), () => h(Widget, null));
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('article')).toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('article.widget')).not.toBeNull();
  });

  it('component → element: unmounts component (onDestroy fires), restores plain element', async () => {
    let destroyedCount = 0;
    class Widget extends Component {
      onDestroy() { destroyedCount++; }
      render() { return h('article', null, 'w'); }
    }
    const T = makeToggle(() => h(Widget, null), () => h('div', null, 'plain'));
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('article')).not.toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('article')).toBeNull();
    expect(c.querySelector('div').textContent).toBe('plain');
    expect(destroyedCount).toBe(1);
  });

  it('component → text: component unmounts, its marker range becomes a text node', async () => {
    let destroyed = false;
    class Widget extends Component {
      onDestroy() { destroyed = true; }
      render() { return h('em', null, 'w'); }
    }
    const T = makeToggle(() => h(Widget, null), () => 'plain text');
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('em')).not.toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('em')).toBeNull();
    expect(c.textContent).toBe('plain text');
    expect(destroyed).toBe(true);
  });

  it('fragment (array) → single element: collapses multiple roots into one', async () => {
    const T = makeToggle(
      () => [h('span', null, 'a'), h('span', null, 'b')],
      () => h('div', null, 'one')
    );
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelectorAll('span').length).toBe(2);

    inst.flip();
    await nextFrame();
    expect(c.querySelectorAll('span').length).toBe(0);
    expect(c.querySelector('div')).not.toBeNull();
  });

  it('single element → fragment: expands one root into multiple', async () => {
    const T = makeToggle(
      () => h('div', null, 'one'),
      () => [h('span', null, 'a'), h('span', null, 'b')]
    );
    const c = document.createElement('div');
    const inst = mount(T, c);
    expect(c.querySelector('div')).not.toBeNull();

    inst.flip();
    await nextFrame();
    expect(c.querySelector('div')).toBeNull();
    expect(c.querySelectorAll('span').length).toBe(2);
  });
});
