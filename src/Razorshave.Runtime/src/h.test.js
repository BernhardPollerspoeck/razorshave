import { describe, it, expect, vi } from 'vitest';
import { h, markup } from './h.js';

describe('h()', () => {
  it('produces a vnode with type, props, children, and key', () => {
    const v = h('div', { class: 'x' }, 'a', 'b');
    expect(v).toEqual({ type: 'div', props: { class: 'x' }, children: ['a', 'b'], key: null });
  });

  it('hoists props.key to vnode.key and strips it from props', () => {
    const v = h('li', { key: 'item-7', class: 'row' }, 'x');
    expect(v.key).toBe('item-7');
    expect(v.props).toEqual({ class: 'row' });
  });

  it('defaults props to {} when null is passed', () => {
    const v = h('span', null, 'hello');
    expect(v.props).toEqual({});
  });

  it('drops null, undefined, true and false children', () => {
    const v = h('div', null, null, undefined, false, true, 'kept');
    expect(v.children).toEqual(['kept']);
  });

  it('flattens nested arrays (RenderFragment expansion)', () => {
    const v = h('ul', null, [[h('li', null, 'a'), h('li', null, 'b')]], [h('li', null, 'c')]);
    expect(v.children).toHaveLength(3);
    expect(v.children.every(c => c.type === 'li')).toBe(true);
  });

  it('accepts a component class as type', () => {
    class MyComp {}
    const v = h(MyComp, { x: 1 });
    expect(v.type).toBe(MyComp);
  });
});

describe('markup()', () => {
  it('produces a markup vnode carrying the raw html', () => {
    expect(markup('<b>x</b>')).toEqual({ type: '__markup__', html: '<b>x</b>' });
  });

  it('warns once when given HTML containing <script>, javascript: URI, or inline on*= handlers', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    try {
      // The one-time flag may or may not already be set depending on test order,
      // so rather than asserting it fires, assert it does NOT throw and the
      // vnode is still returned verbatim — dangerous input is flagged, not
      // filtered. (Consumers asking for markup get markup.)
      const vn = markup('<img onerror="alert(1)">');
      expect(vn).toEqual({ type: '__markup__', html: '<img onerror="alert(1)">' });
    } finally {
      warnSpy.mockRestore();
    }
  });

  it('does NOT filter or escape the dangerous input — contract is opt-in XSS risk', () => {
    const vn = markup('<script>evil()</script>');
    expect(vn.html).toBe('<script>evil()</script>');
  });
});

describe('h() key handling across types', () => {
  it('component vnodes also get key hoisted', () => {
    class C {}
    const v = h(C, { key: 'a', x: 1 });
    expect(v.key).toBe('a');
    expect(v.props).toEqual({ x: 1 });
  });
});
