// Base class every transpiled ApiClient subclass extends. Wraps `fetch()`
// with the hooks Razorshave's ApiClient abstraction exposes:
// configureRequest (headers, token injection, tenant tagging) and
// handleResponse (logging, global 401 handling, etc.).
//
// Scope for M0: GET and POST only, JSON in/out. Retry, timeout, cancellation,
// FormData, and the full HTTP-verb set land when a fixture demands them.

export class ApiClient {
  constructor(baseUrl = '') {
    this.baseUrl = baseUrl;
  }

  // Override points for subclasses. Default no-ops so simple clients work
  // without subclassing these two.
  async configureRequest(_request) {}
  async handleResponse(_response) {}

  async get(path)      { return this._send('GET',    path, undefined); }
  async post(path, body) { return this._send('POST',   path, body); }
  async put(path, body)  { return this._send('PUT',    path, body); }
  async delete(path)     { return this._send('DELETE', path, undefined); }

  async _send(method, path, body) {
    const request = {
      method,
      path,
      headers: {},
      body,
    };
    await this.configureRequest(request);

    const headers = { ...request.headers };
    let fetchBody;
    if (request.body !== undefined && request.body !== null) {
      if (!('Content-Type' in headers) && !('content-type' in headers)) {
        headers['Content-Type'] = 'application/json';
      }
      fetchBody = JSON.stringify(request.body);
    }

    const url = isAbsoluteUrl(request.path) ? request.path : this.baseUrl + request.path;
    const fetchResponse = await fetch(url, {
      method: request.method,
      headers,
      body: fetchBody,
    });

    const rawBody = await fetchResponse.text();
    const response = {
      statusCode: fetchResponse.status,
      headers: Object.fromEntries(fetchResponse.headers.entries?.() ?? []),
      body: rawBody,
    };
    await this.handleResponse(response);

    if (!fetchResponse.ok) {
      throw new ApiException(response);
    }
    // Use `response.body` (not the original `rawBody`) so a `handleResponse`
    // hook that unwrapped an envelope, substituted error text with a
    // parseable default, or ran the bytes through a decompressor is
    // honoured. Matches the dev-host `ApiClient`, which also lets the
    // hook rewrite the body before deserialisation.
    return response.body ? JSON.parse(response.body) : null;
  }
}

export class ApiException extends Error {
  constructor(response) {
    super(`Razorshave ApiClient: HTTP ${response.statusCode}`);
    this.statusCode = response.statusCode;
    this.response = response;
  }
}

function isAbsoluteUrl(path) {
  return /^https?:\/\//i.test(path);
}
