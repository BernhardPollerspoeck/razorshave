// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { PageTitle } from './page-title.js';
import { h, markup } from '../h.js';
import { mount } from '../mount.js';
import { Component } from '../component.js';

function mountTitle(children) {
  class App extends Component {
    render() { return h(PageTitle, { ChildContent: () => children }); }
  }
  mount(App, document.createElement('div'));
}

describe('PageTitle', () => {
  it('writes the rendered ChildContent text into document.title', () => {
    mountTitle(['Hello from Razorshave']);
    expect(document.title).toBe('Hello from Razorshave');
  });

  it('renders no DOM itself', () => {
    class App extends Component {
      render() { return h(PageTitle, { ChildContent: () => ['X'] }); }
    }
    const c = document.createElement('div');
    mount(App, c);
    expect(c.textContent).toBe('');
  });

  // Razor's source generator routes a static-text RenderFragment through
  // `AddMarkupContent` rather than `AddContent` whenever the content is
  // classified as raw markup (HTML entities, embedded inline tags, certain
  // mixed-content shapes). The transpiler faithfully passes that through as
  // `markup(...)`, so PageTitle children can legitimately be markup vnodes —
  // not just plain strings — and the title extractor has to handle both.
  it('extracts text from a markup() vnode child (em-dash + €)', () => {
    mountTitle([markup('Verified — Public proof. €19/month.')]);
    expect(document.title).toBe('Verified — Public proof. €19/month.');
  });

  it('strips tags from markup HTML before assigning to document.title', () => {
    mountTitle([markup('<b>Impressum</b>')]);
    expect(document.title).toBe('Impressum');
  });

  it('decodes HTML entities in markup HTML', () => {
    mountTitle([markup('Foo &amp; Bar')]);
    expect(document.title).toBe('Foo & Bar');
  });

  it('concatenates a mix of string and markup children', () => {
    mountTitle(['Plain ', markup('text — mixed')]);
    expect(document.title).toBe('Plain text — mixed');
  });

  it('extracts text from a plain HTML element vnode child', () => {
    mountTitle([h('span', null, 'NestedTitle')]);
    expect(document.title).toBe('NestedTitle');
  });

  it('coerces numeric children to their string form', () => {
    mountTitle([42, ' items']);
    expect(document.title).toBe('42 items');
  });
});
