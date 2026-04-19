import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { ApiClient, ApiException } from './api-client.js';

// Each test installs its own fetch stub on globalThis so we can assert the
// outgoing request shape and control the response. Restored in afterEach to
// avoid cross-test contamination.
let originalFetch;
beforeEach(() => { originalFetch = globalThis.fetch; });
afterEach(() => { globalThis.fetch = originalFetch; });

function mockFetch({ status = 200, body = null, ok = undefined } = {}) {
  const spy = vi.fn(async () => ({
    status,
    ok: ok ?? (status >= 200 && status < 300),
    headers: new Map(),
    text: async () => body === null ? '' : typeof body === 'string' ? body : JSON.stringify(body),
  }));
  globalThis.fetch = spy;
  return spy;
}

describe('ApiClient.get', () => {
  it('issues a GET against baseUrl + path and returns the parsed JSON body', async () => {
    const fetch = mockFetch({ body: { answer: 42 } });
    const api = new ApiClient('https://example.test');

    const result = await api.get('/thing');

    expect(fetch).toHaveBeenCalledOnce();
    const [url, init] = fetch.mock.calls[0];
    expect(url).toBe('https://example.test/thing');
    expect(init.method).toBe('GET');
    expect(init.body).toBeUndefined();
    expect(result).toEqual({ answer: 42 });
  });

  it('uses the path verbatim when it is already an absolute URL', async () => {
    const fetch = mockFetch({ body: [1, 2, 3] });
    const api = new ApiClient('https://ignored.test');

    await api.get('https://api.open-meteo.com/v1/forecast?latitude=48');

    expect(fetch.mock.calls[0][0]).toBe('https://api.open-meteo.com/v1/forecast?latitude=48');
  });

  it('returns null on an empty body', async () => {
    mockFetch({ body: null });
    const api = new ApiClient();
    expect(await api.get('/anything')).toBeNull();
  });
});

describe('ApiClient.post', () => {
  it('serialises the body to JSON and adds Content-Type', async () => {
    const fetch = mockFetch({ body: { id: 1 } });
    const api = new ApiClient('https://api.test');

    await api.post('/users', { name: 'Alice' });

    const [, init] = fetch.mock.calls[0];
    expect(init.method).toBe('POST');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(init.body).toBe('{"name":"Alice"}');
  });
});

describe('configureRequest hook', () => {
  it('lets a subclass inject auth headers before the fetch is issued', async () => {
    const fetch = mockFetch({ body: null });

    class AuthedApi extends ApiClient {
      configureRequest(request) {
        request.headers['Authorization'] = 'Bearer xyz';
      }
    }
    await new AuthedApi('https://api.test').get('/me');

    expect(fetch.mock.calls[0][1].headers['Authorization']).toBe('Bearer xyz');
  });
});

describe('handleResponse hook + error flow', () => {
  it('runs handleResponse on every response, success or failure', async () => {
    mockFetch({ status: 200, body: { ok: true } });
    const seen = [];
    class Observer extends ApiClient {
      handleResponse(r) { seen.push(r.statusCode); }
    }
    await new Observer().get('/a');
    expect(seen).toEqual([200]);
  });

  it('throws ApiException on non-2xx and still runs handleResponse first', async () => {
    mockFetch({ status: 401, body: 'nope' });
    const seen = [];
    class Observer extends ApiClient {
      handleResponse(r) { seen.push(r.statusCode); }
    }
    await expect(new Observer().get('/secret')).rejects.toBeInstanceOf(ApiException);
    expect(seen).toEqual([401]);
  });
});
