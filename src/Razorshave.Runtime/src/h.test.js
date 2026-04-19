import { describe, it, expect } from 'vitest';
import { h, markup } from './h.js';

describe('h()', () => {
  it('produces a vnode with type, props, and children', () => {
    const v = h('div', { class: 'x' }, 'a', 'b');
    expect(v).toEqual({ type: 'div', props: { class: 'x' }, children: ['a', 'b'] });
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
});
