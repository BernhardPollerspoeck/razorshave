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

describe('Component comment markers carry the class name', () => {
  it('start and end markers label themselves with the Component class name', () => {
    class MyWidget extends Component {
      render() { return h('span', null, 'x'); }
    }
    const c = document.createElement('div');
    mount(MyWidget, c);

    // Root component has no markers (the container is the boundary); drop a
    // nested component so we can see its markers.
    class Wrapper extends Component {
      render() { return h('div', null, h(MyWidget, {})); }
    }
    const c2 = document.createElement('div');
    mount(Wrapper, c2);

    const walker = document.createTreeWalker(c2, NodeFilter.SHOW_COMMENT);
    const comments = [];
    let n;
    while ((n = walker.nextNode())) comments.push(n.data);

    expect(comments).toContain(' rs:MyWidget ');
    expect(comments).toContain(' /rs:MyWidget ');
  });
});

describe('Component null → content transitions', () => {
  it('component whose render toggles null → content correctly mounts DOM on the next render', async () => {
    // Regression for vdom.js patchComponent null-host skip: before the
    // comment-marker range semantics, host lookup via `firstDom(oldSubtree)`
    // returned null when the previous render was null, and the new subtree
    // was silently dropped — the component's DOM never appeared.
    class Leaf extends Component {
      onInit() { this.show = this.props.show; }
      onPropsChanged() { this.show = this.props.show; }
      render() { return this.show ? h('span', null, this.props.label) : null; }
    }
    class Parent extends Component {
      constructor() { super(); this.show = false; }
      toggle() { this.show = !this.show; this.stateHasChanged(); }
      render() {
        return h('section', null, h(Leaf, { show: this.show, label: 'ready' }));
      }
    }
    const c = document.createElement('div');
    const parent = mount(Parent, c);
    expect(c.querySelector('span')).toBeNull();

    parent.toggle();
    await nextFrame();
    const span = c.querySelector('span');
    expect(span).not.toBeNull();
    expect(span.textContent).toBe('ready');

    parent.toggle();
    await nextFrame();
    expect(c.querySelector('span')).toBeNull();
  });

  it('component siblings around a null-rendering component keep their order', async () => {
    // The comment markers must reserve the component's slot even when the
    // subtree is empty; otherwise adjacent siblings would collapse onto
    // each other and re-rendering the empty one into content would insert
    // in the wrong place.
    class MaybeEmpty extends Component {
      render() { return this.props.show ? h('em', null, 'middle') : null; }
    }
    class Parent extends Component {
      constructor() { super(); this.show = false; }
      toggle() { this.show = !this.show; this.stateHasChanged(); }
      render() {
        return h('div', null,
          h('span', null, 'before'),
          h(MaybeEmpty, { show: this.show }),
          h('span', null, 'after')
        );
      }
    }
    const c = document.createElement('div');
    const parent = mount(Parent, c);
    const spansBefore = Array.from(c.querySelectorAll('span')).map(s => s.textContent);
    expect(spansBefore).toEqual(['before', 'after']);

    parent.toggle();
    await nextFrame();
    expect(c.querySelector('em')?.textContent).toBe('middle');
    // Order check: before → em → after
    const order = Array.from(c.firstChild.children).map(e => e.tagName + ':' + e.textContent);
    expect(order).toEqual(['SPAN:before', 'EM:middle', 'SPAN:after']);

    parent.toggle();
    await nextFrame();
    expect(c.querySelector('em')).toBeNull();
    expect(Array.from(c.querySelectorAll('span')).map(s => s.textContent)).toEqual(['before', 'after']);
  });
});

describe('Mixed keyed + unkeyed siblings in same parent', () => {
  it('static wrappers survive when the keyed loop between them mutates', async () => {
    // Idiomatic Blazor pattern: static header + keyed loop + static footer.
    // The old reconciler switched to keyed-mode as soon as any child had a
    // key and misclassified the unkeyed wrappers as "missing" on each
    // render, remounting them and losing their state.
    class Item extends Component {
      render() { return h('li', null, this.props.label); }
    }
    class List extends Component {
      constructor() { super(); this.ids = ['a', 'b', 'c']; }
      drop(id) { this.ids = this.ids.filter(x => x !== id); this.stateHasChanged(); }
      render() {
        return h('div', null,
          h('h2', null, 'Items'),
          ...this.ids.map(id => h(Item, { key: id, label: id })),
          h('button', null, 'Add')
        );
      }
    }
    const c = document.createElement('div');
    const list = mount(List, c);

    const h2Before = c.querySelector('h2');
    const buttonBefore = c.querySelector('button');
    list.drop('b');
    await nextFrame();

    // Wrappers keep their DOM identity (positional unkeyed match).
    expect(c.querySelector('h2')).toBe(h2Before);
    expect(c.querySelector('button')).toBe(buttonBefore);
    // Keyed loop resolves the delete correctly.
    expect(Array.from(c.querySelectorAll('li')).map(l => l.textContent))
      .toEqual(['a', 'c']);
    // Structural order is still header → items → footer.
    const kids = Array.from(c.firstChild.children);
    expect(kids[0].tagName).toBe('H2');
    expect(kids[kids.length - 1].tagName).toBe('BUTTON');
  });
});

describe('Keyed reorder with multi-root component fragments', () => {
  it('reordering a component that renders a fragment moves all its DOM together', async () => {
    // Regression for vdom.js patchKeyed: before the comment-marker range
    // semantics, only firstDom was moved on reorder — subsequent nodes in
    // a fragment-return stayed behind, shredding the DOM order.
    class Pair extends Component {
      render() {
        return [
          h('span', { class: 'a' }, this.props.id, '-a'),
          h('span', { class: 'b' }, this.props.id, '-b'),
        ];
      }
    }
    class List extends Component {
      constructor() { super(); this.ids = ['x', 'y']; }
      swap() { this.ids = [...this.ids].reverse(); this.stateHasChanged(); }
      render() {
        return h('div', null, ...this.ids.map(id => h(Pair, { key: id, id })));
      }
    }
    const c = document.createElement('div');
    const list = mount(List, c);

    // Before: x-a, x-b, y-a, y-b
    const before = Array.from(c.querySelectorAll('span')).map(s => s.textContent);
    expect(before).toEqual(['x-a', 'x-b', 'y-a', 'y-b']);

    list.swap();
    await nextFrame();

    // After swap: y-a, y-b, x-a, x-b — both of each pair stay adjacent.
    const after = Array.from(c.querySelectorAll('span')).map(s => s.textContent);
    expect(after).toEqual(['y-a', 'y-b', 'x-a', 'x-b']);
  });
});

describe('Lifecycle parity: child components fire onAfterRender + shouldRender', () => {
  it('child component sees onAfterRender(true) on first mount', async () => {
    const calls = [];
    class Leaf extends Component {
      onAfterRender(firstRender) { calls.push(firstRender); }
      render() { return h('span', null, 'leaf'); }
    }
    class Parent extends Component {
      render() { return h('div', null, h(Leaf, {})); }
    }
    const c = document.createElement('div');
    mount(Parent, c);
    expect(calls).toEqual([true]);
  });

  it('child component sees onAfterRender(false) on subsequent patches', async () => {
    const calls = [];
    class Leaf extends Component {
      onAfterRender(firstRender) { calls.push(firstRender); }
      render() { return h('span', null, this.props.label); }
    }
    class Parent extends Component {
      constructor() { super(); this.label = 'a'; }
      change() { this.label = 'b'; this.stateHasChanged(); }
      render() { return h('div', null, h(Leaf, { label: this.label })); }
    }
    const c = document.createElement('div');
    const parent = mount(Parent, c);
    expect(calls).toEqual([true]);

    parent.change();
    await nextFrame();
    expect(calls).toEqual([true, false]);
  });

  it('child shouldRender() === false skips its render and onAfterRender', async () => {
    let renders = 0;
    let afterCalls = 0;
    class Leaf extends Component {
      shouldRender() { return false; }
      render() { renders++; return h('span', null, 'x'); }
      onAfterRender() { afterCalls++; }
    }
    class Parent extends Component {
      constructor() { super(); this.n = 0; }
      bump() { this.n++; this.stateHasChanged(); }
      render() { return h('div', null, h(Leaf, { n: this.n })); }
    }
    const c = document.createElement('div');
    const parent = mount(Parent, c);

    // First mount happens before shouldRender is consulted (that's the
    // initial render). After mount, renders=1, afterCalls=1.
    expect(renders).toBe(1);
    expect(afterCalls).toBe(1);

    parent.bump();
    await nextFrame();
    // Second pass: shouldRender returns false → leaf's render + onAfterRender
    // skipped. Leaf still received the props and onPropsChanged fired, but
    // its subtree wasn't touched.
    expect(renders).toBe(1);
    expect(afterCalls).toBe(1);
  });

  it('onAfterRender firing order is bottom-up (deepest child first)', () => {
    const events = [];
    class Leaf extends Component {
      onAfterRender() { events.push('leaf'); }
      render() { return h('span', null, 'x'); }
    }
    class Middle extends Component {
      onAfterRender() { events.push('middle'); }
      render() { return h('div', null, h(Leaf, {})); }
    }
    class Root extends Component {
      onAfterRender() { events.push('root'); }
      render() { return h('section', null, h(Middle, {})); }
    }
    const c = document.createElement('div');
    mount(Root, c);

    expect(events).toEqual(['leaf', 'middle', 'root']);
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
