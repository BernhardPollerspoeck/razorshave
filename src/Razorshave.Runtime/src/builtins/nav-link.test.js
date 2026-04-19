// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest';
import { NavLink } from './nav-link.js';
import { mount } from '../mount.js';
import { Component } from '../component.js';
import { h } from '../h.js';
import { navigationManager } from '../navigation-manager.js';

beforeEach(() => { window.history.replaceState(null, '', '/'); });

function mountWith(href, childText = 'Go') {
  class Host extends Component {
    render() {
      return h(NavLink, { Href: href, class: 'nav-link', ChildContent: () => [childText] });
    }
  }
  const root = document.createElement('div');
  mount(Host, root);
  return { root, anchor: root.querySelector('a') };
}

describe('NavLink', () => {
  it('renders an anchor with the given href and child content', () => {
    const { anchor } = mountWith('/counter', 'Counter');
    expect(anchor.getAttribute('href')).toBe('/counter');
    expect(anchor.textContent).toBe('Counter');
  });

  it('adds the `active` class when the current path matches href', () => {
    window.history.replaceState(null, '', '/counter');
    const { anchor } = mountWith('/counter');
    expect(anchor.className).toContain('active');
  });

  it('does not add active when path does not match', () => {
    window.history.replaceState(null, '', '/weather');
    const { anchor } = mountWith('/counter');
    expect(anchor.className).not.toContain('active');
  });

  it('root link matches only the exact root path, not any sub-path', () => {
    window.history.replaceState(null, '', '/counter');
    const { anchor } = mountWith('/');
    expect(anchor.className).not.toContain('active');
  });

  it('prefix matches so /counter/5 keeps the /counter link active', () => {
    window.history.replaceState(null, '', '/counter/5');
    const { anchor } = mountWith('/counter');
    expect(anchor.className).toContain('active');
  });

  it('normalises relative hrefs — "counter" matches pathname "/counter"', () => {
    // Blazor's NavMenu template uses relative hrefs; we shouldn't require
    // the user to add leading slashes for active-class detection to work.
    window.history.replaceState(null, '', '/counter');
    const { anchor } = mountWith('counter');
    expect(anchor.className).toContain('active');
  });

  it('relative href "" matches root path "/"', () => {
    window.history.replaceState(null, '', '/');
    const { anchor } = mountWith('');
    expect(anchor.className).toContain('active');
  });

  it('clicking a relative-href NavLink navigates to the slash-prefixed path', () => {
    const { anchor } = mountWith('weather');
    const event = new MouseEvent('click', { button: 0, cancelable: true, bubbles: true });
    anchor.dispatchEvent(event);
    expect(navigationManager.pathname).toBe('/weather');
  });

  it('left-click navigates via NavigationManager and prevents page load', () => {
    const { anchor } = mountWith('/weather');
    const event = new MouseEvent('click', { button: 0, cancelable: true, bubbles: true });
    anchor.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    expect(navigationManager.pathname).toBe('/weather');
  });

  it('ctrl-click falls through to the browser (no SPA hijack)', () => {
    const { anchor } = mountWith('/weather');
    const event = new MouseEvent('click', { button: 0, ctrlKey: true, cancelable: true, bubbles: true });
    anchor.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
  });
});
