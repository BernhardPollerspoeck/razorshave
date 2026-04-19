// @vitest-environment jsdom
import { describe, it, expect } from 'vitest';
import { PageTitle } from './page-title.js';
import { h } from '../h.js';
import { mount } from '../mount.js';
import { Component } from '../component.js';

describe('PageTitle', () => {
  it('writes the rendered ChildContent text into document.title', () => {
    class App extends Component {
      render() {
        return h(PageTitle, { ChildContent: () => ['Hello from Razorshave'] });
      }
    }
    mount(App, document.createElement('div'));
    expect(document.title).toBe('Hello from Razorshave');
  });

  it('renders no DOM itself', () => {
    class App extends Component {
      render() {
        return h(PageTitle, { ChildContent: () => ['X'] });
      }
    }
    const c = document.createElement('div');
    mount(App, c);
    expect(c.textContent).toBe('');
  });
});
