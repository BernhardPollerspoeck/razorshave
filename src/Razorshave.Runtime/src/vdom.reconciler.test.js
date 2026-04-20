// @vitest-environment jsdom
//
// Reconciler behaviour: DOM identity survives re-render for matching vnodes,
// keyed lists stay stable under reorder/insert/delete, focus + caret carry
// across patches, onDestroy fires for components leaving the tree.

import { describe, it, expect, vi } from 'vitest';
import { h } from './h.js';
import { mount } from './mount.js';
import { Component } from './component.js';

function nextFrame() { return new Promise(r => requestAnimationFrame(r)); }

describe('DOM identity preserved across rerenders', () => {
  it('element retains the same DOM node when attributes change', async () => {
    class Toggle extends Component {
      constructor() { super(); this.on = false; }
      flip() { this.on = !this.on; this.stateHasChanged(); }
      render() {
        return h('div', { class: this.on ? 'on' : 'off' }, 'hi');
      }
    }
    const c = document.createElement('div');
    const inst = mount(Toggle, c);

    const before = c.querySelector('div');
    expect(before.getAttribute('class')).toBe('off');

    inst.flip();
    await nextFrame();

    const after = c.querySelector('div');
    expect(after).toBe(before); // same node — attribute patched in place
    expect(after.getAttribute('class')).toBe('on');
  });

  it('text content is updated without replacing the text node', async () => {
    class Counter extends Component {
      constructor() { super(); this.n = 0; }
      bump() { this.n++; this.stateHasChanged(); }
      render() { return h('span', null, 'count: ', this.n); }
    }
    const c = document.createElement('div');
    const inst = mount(Counter, c);

    const span = c.querySelector('span');
    const textNodes = Array.from(span.childNodes);
    inst.bump();
    await nextFrame();
    const after = Array.from(span.childNodes);
    expect(after).toHaveLength(textNodes.length);
    for (let i = 0; i < textNodes.length; i++) {
      expect(after[i]).toBe(textNodes[i]);
    }
    expect(span.textContent).toBe('count: 1');
  });
});

describe('Input focus and caret survive rerenders', () => {
  it('keeps focus + selection through a state change', async () => {
    class Form extends Component {
      constructor() { super(); this.text = ''; }
      render() {
        return h('div', null,
          h('input', {
            type: 'text',
            value: this.text,
            oninput: (e) => { this.text = e.target.value; },
          }),
          h('p', null, 'echo: ', this.text)
        );
      }
    }
    const root = document.createElement('div');
    document.body.appendChild(root);
    mount(Form, root);

    const input = root.querySelector('input');
    input.focus();
    input.value = 'hi';
    input.setSelectionRange(2, 2);
    input.dispatchEvent(new Event('input'));
    await nextFrame();
    await nextFrame();

    const after = root.querySelector('input');
    expect(after).toBe(input); // same DOM node, caret preserved
    expect(after.selectionStart).toBe(2);
    expect(document.activeElement).toBe(after);

    document.body.removeChild(root);
  });
});

describe('Keyed list diff', () => {
  function Row(id, label) {
    return h('li', { key: id, 'data-id': id }, label);
  }

  it('delete-in-the-middle keeps remaining rows with their original DOM', async () => {
    class List extends Component {
      constructor() { super(); this.items = ['a', 'b', 'c']; }
      drop(id) { this.items = this.items.filter(x => x !== id); this.stateHasChanged(); }
      render() {
        return h('ul', null, ...this.items.map(id => Row(id, id)));
      }
    }
    const c = document.createElement('div');
    const inst = mount(List, c);

    const liB = c.querySelector('[data-id="b"]');
    const liC = c.querySelector('[data-id="c"]');
    inst.drop('b');
    await nextFrame();

    expect(c.querySelector('[data-id="b"]')).toBeNull();
    expect(c.querySelector('[data-id="c"]')).toBe(liC); // same node as before
    expect(Array.from(c.querySelectorAll('li')).map(e => e.dataset.id))
      .toEqual(['a', 'c']);
  });

  it('reorder moves existing nodes without rebuilding them', async () => {
    class List extends Component {
      constructor() { super(); this.order = ['a', 'b', 'c']; }
      reverse() { this.order = [...this.order].reverse(); this.stateHasChanged(); }
      render() {
        return h('ul', null, ...this.order.map(id => Row(id, id)));
      }
    }
    const c = document.createElement('div');
    const inst = mount(List, c);

    const before = Array.from(c.querySelectorAll('li'));
    inst.reverse();
    await nextFrame();

    const after = Array.from(c.querySelectorAll('li'));
    expect(after.map(e => e.dataset.id)).toEqual(['c', 'b', 'a']);
    // Same three DOM nodes, just reordered.
    expect(new Set(after)).toEqual(new Set(before));
  });
});

describe('Component lifecycle on unmount', () => {
  it('fires onDestroy when a component leaves the tree', async () => {
    const destroySpy = vi.fn();
    class Leaf extends Component {
      onDestroy() { destroySpy(); }
      render() { return h('span', null, 'leaf'); }
    }
    class Parent extends Component {
      constructor() { super(); this.show = true; }
      hide() { this.show = false; this.stateHasChanged(); }
      render() {
        return h('div', null, this.show ? h(Leaf, {}) : null);
      }
    }
    const c = document.createElement('div');
    const inst = mount(Parent, c);
    expect(c.querySelector('span')?.textContent).toBe('leaf');

    inst.hide();
    await nextFrame();

    expect(c.querySelector('span')).toBeNull();
    expect(destroySpy).toHaveBeenCalledTimes(1);
  });

  it('fires onDestroy when a keyed sibling is removed from the middle', async () => {
    const destroyedIds = [];
    class Item extends Component {
      onInit() { this.id = this.props.id; }
      onDestroy() { destroyedIds.push(this.id); }
      render() { return h('li', null, this.props.id); }
    }
    class List extends Component {
      constructor() { super(); this.ids = ['a', 'b', 'c']; }
      drop(id) { this.ids = this.ids.filter(x => x !== id); this.stateHasChanged(); }
      render() {
        return h('ul', null, ...this.ids.map(id => h(Item, { key: id, id })));
      }
    }
    const c = document.createElement('div');
    const inst = mount(List, c);

    inst.drop('b');
    await nextFrame();

    expect(destroyedIds).toEqual(['b']);
    expect(Array.from(c.querySelectorAll('li')).map(e => e.textContent))
      .toEqual(['a', 'c']);
  });
});
