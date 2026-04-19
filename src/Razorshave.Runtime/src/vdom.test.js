// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest';
import { h, markup } from './h.js';
import { render } from './vdom.js';

describe('render()', () => {
  it('creates an element with a single string attribute', () => {
    const node = render(h('div', { class: 'x', id: 'y' }));
    expect(node.tagName).toBe('DIV');
    expect(node.getAttribute('class')).toBe('x');
    expect(node.getAttribute('id')).toBe('y');
  });

  it('renders text children as text nodes', () => {
    const node = render(h('p', null, 'hello ', 'world'));
    expect(node.textContent).toBe('hello world');
    expect(node.childNodes).toHaveLength(2);
  });

  it('renders nested elements', () => {
    const node = render(h('div', null, h('span', null, 'inside')));
    expect(node.firstChild.tagName).toBe('SPAN');
    expect(node.firstChild.textContent).toBe('inside');
  });

  it('wires event handlers and auto-triggers owner stateHasChanged', async () => {
    const owner = { stateHasChanged: vi.fn() };
    const handler = vi.fn();

    const node = render(h('button', { onclick: handler }, 'go'), owner);
    node.click();
    // Handler wrapping awaits a potential promise, then calls stateHasChanged.
    // A microtask tick is enough for the synchronous case.
    await Promise.resolve();

    expect(handler).toHaveBeenCalledOnce();
    expect(owner.stateHasChanged).toHaveBeenCalledOnce();
  });

  it('awaits async handlers before triggering rerender', async () => {
    const owner = { stateHasChanged: vi.fn() };
    let resolveIt;
    const handler = vi.fn().mockReturnValue(new Promise(r => { resolveIt = r; }));

    const node = render(h('button', { onclick: handler }), owner);
    node.click();
    await Promise.resolve();

    // Handler is pending — rerender must wait.
    expect(owner.stateHasChanged).not.toHaveBeenCalled();

    resolveIt();
    await Promise.resolve(); await Promise.resolve();
    expect(owner.stateHasChanged).toHaveBeenCalledOnce();
  });

  it('renders an array of vnodes into a fragment', () => {
    const frag = render([h('a', null, '1'), h('b', null, '2')]);
    expect(frag.nodeType).toBe(11); // DOCUMENT_FRAGMENT_NODE
    expect(frag.childNodes).toHaveLength(2);
  });

  it('renders markup() as parsed HTML, not escaped text', () => {
    const frag = render(markup('<b>bold</b>'));
    expect(frag.childNodes).toHaveLength(1);
    expect(frag.firstChild.tagName).toBe('B');
    expect(frag.firstChild.textContent).toBe('bold');
  });

  it('emits an empty string attribute when the value is the empty string', () => {
    // Scoped-CSS markers arrive as '' from the transpiler.
    const node = render(h('div', { 'b-xyz': '' }));
    expect(node.getAttribute('b-xyz')).toBe('');
  });

  it('returns null for null/false/true/undefined vnodes', () => {
    expect(render(null)).toBeNull();
    expect(render(undefined)).toBeNull();
    expect(render(false)).toBeNull();
    expect(render(true)).toBeNull();
  });
});
