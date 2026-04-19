import { describe, it, expect } from 'vitest';
import { matchRoute } from './router.js';

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
