// @vitest-environment jsdom
import { describe, it, expect, beforeEach } from 'vitest';
import { matchRoute, Router } from './router.js';
import { mount } from './mount.js';
import { Component } from './component.js';
import { h } from './h.js';

describe('matchRoute', () => {
  it('matches an exact literal path with empty params', () => {
    expect(matchRoute('/counter', '/counter')).toEqual({});
  });

  it('matches the root path', () => {
    expect(matchRoute('/', '/')).toEqual({});
  });

  it('extracts a single parameter', () => {
    expect(matchRoute('/users/{id}', '/users/42')).toEqual({ id: '42' });
  });

  it('extracts multiple parameters', () => {
    expect(matchRoute('/posts/{year}/{slug}', '/posts/2026/razorshave-m0'))
      .toEqual({ year: '2026', slug: 'razorshave-m0' });
  });

  it('returns null when segment counts differ', () => {
    expect(matchRoute('/users', '/users/42')).toBeNull();
    expect(matchRoute('/users/{id}', '/users')).toBeNull();
  });

  it('returns null when a literal segment does not match', () => {
    expect(matchRoute('/users/profile', '/users/admin')).toBeNull();
  });

  it('decodes URI-encoded parameter values', () => {
    expect(matchRoute('/search/{q}', '/search/hello%20world'))
      .toEqual({ q: 'hello world' });
  });

  it('empty string input behaves the same as /', () => {
    expect(matchRoute('/', '')).toEqual({});
  });
});

describe('Router specificity ordering', () => {
  beforeEach(() => { window.history.replaceState(null, '', '/users/new'); });

  it('prefers literal segments over parameters regardless of registration order', () => {
    // Regression for silent-fail: user registers `/users/{id}` FIRST and
    // `/users/new` SECOND. Old Router matched in source order, routing
    // `/users/new` to the parameterised handler with id='new'. Now the
    // specificity sort moves the literal pattern ahead.
    class Specific extends Component { render() { return h('span', { id: 'specific' }, 'new page'); } }
    class Generic extends Component { render() { return h('span', { id: 'generic' }, 'id: ' + this.props.id); } }
    class App extends Component {
      render() {
        return h(Router, {
          routes: [
            { pattern: '/users/{id}', component: Generic },  // less specific, declared first
            { pattern: '/users/new', component: Specific },  // more specific, declared second
          ],
        });
      }
    }
    const c = document.createElement('div');
    mount(App, c);
    expect(c.querySelector('#specific')).not.toBeNull();
    expect(c.querySelector('#generic')).toBeNull();
  });
});
