# Razorshave: Runtime-API-Kontrakt

> Die JavaScript-Runtime die mit jeder transpilierten App ausgeliefert wird. Definiert Component-Base-Class, VDOM, Router, DI-Container, Store, ApiClient, Event-System.

**Status:** Design-Draft
**Parent:** RAZORSHAVE.md
**Datum:** 2026-04-19

---

## Überblick

Die Razorshave-Runtime ist ein schlankes JavaScript-Framework das:
- Component-Lifecycle verwaltet
- VDOM-Diffing macht
- Routing handhabt
- Dependency Injection bereitstellt
- Event-System bietet
- HTTP-Client-Abstraktion (ApiClient) bereitstellt
- JS-Interop (IJSRuntime) ermöglicht

Sie wird als **ESM-Modul** gebaut, **tree-shakeable** gebundled mit User-Code, und ist target **kleiner als 100 KB gzipped**.

---

## Component-Base-Class

Alle transpilierten Blazor-Components erben davon.

### Pseudo-API

```js
// runtime/component.js

export class Component {
    // Infrastructure (von Runtime gesetzt)
    _props = {};          // Parameter vom Parent
    _children = null;     // RenderFragment/ChildContent
    _context = null;      // DI-Container, Router, etc.
    _vdom = null;         // letzter gerenderter VDOM-Tree
    _domNode = null;      // aktueller DOM-Node
    _renderScheduled = false;
    
    // Lifecycle (User überschreibt)
    async onInit() {}                    // OnInitialized(Async)
    async onPropsChanged() {}            // OnParametersSet(Async)
    async onAfterRender(firstRender) {}  // OnAfterRender(Async)
    shouldRender() { return true; }      // ShouldRender
    onDestroy() {}                       // Dispose
    
    // Render (abstract, vom Transpiler emittiert)
    render() { throw new Error("not implemented"); }
    
    // State
    stateHasChanged() {
        if (this._renderScheduled) return;
        this._renderScheduled = true;
        requestAnimationFrame(() => {
            this._renderScheduled = false;
            this._rerender();
        });
    }
    
    _rerender() {
        if (!this.shouldRender()) return;
        const newVdom = this.render();
        diff(this._vdom, newVdom, this._domNode);
        this._vdom = newVdom;
        const firstRender = !this._hasRenderedBefore;
        this._hasRenderedBefore = true;
        this.onAfterRender(firstRender);
    }
}
```

### Entscheidungen

- **Auto-StateHasChanged:** nach jedem Event-Handler automatisch (Runtime wrapped Handler)
- **Render-Trigger:** gescheduled auf nächsten `requestAnimationFrame`, batcht mehrere Calls
- **Lifecycle:** vereinfacht, keine async/sync Unterscheidung (Runtime awaited wenn Promise zurückkommt)
- **Props:** live getter (Child liest immer aktuellen Wert vom Parent, nicht eingefroren)
- **EventCallback:** nicht special behandelt in v0.1 (User ruft `stateHasChanged()` im Parent-Handler selbst)
- **Render-Output:** `h()` Hyperscript-Calls

### Property-Injection

Services werden **nach** Construction injiziert, nicht im Constructor (passt zu Blazor's `@inject`).

```js
// Runtime macht beim Instantiieren:
const instance = new Counter();
instance._props = props;
instance._context = parentContext;
instance._injectServices();  // liest [Inject]-Properties, setzt sie aus Container
await instance.onInit();
instance._rerender();
```

Der Transpiler emittiert für `@inject UserService UserSvc`:

```js
class Counter extends Component {
    _injectServices() {
        this.userSvc = this._context.container.resolve('UserService');
    }
}
```

### Props-Handling

Transpiler emittiert für `[Parameter] public int StartValue { get; set; }`:

```js
class Counter extends Component {
    get startValue() { return this._props.StartValue ?? 0; }
}
```

Props sind **live**: wenn Parent rerendert mit neuem Value, Child sieht das automatisch.

---

## Hyperscript (h)

Der Render-Baum wird aus `h(tag, props, ...children)` Calls aufgebaut.

### API

```js
// runtime/h.js

export function h(tagOrComponent, props, ...children) {
    return {
        type: tagOrComponent,      // string | ComponentClass
        props: props || {},
        children: children.flat(),
        key: props?.key || null,
    };
}
```

### Beispiel-Output vom Transpiler

Razor-Markup:
```razor
<button class="btn" @onclick="Increment">Count: @count</button>
```

Transpiliertes JS (in `render()`):
```js
h('button', 
  { class: 'btn', onclick: (e) => this.increment() },
  'Count: ', this.count
)
```

---

## VDOM-Diff

Keyed children, element-level updates, component lifecycle preservation. Inspiration: Preact's Reconciler.

### Diff-Algorithmus (Pseudo-Code)

```js
function diff(oldVNode, newVNode, domNode) {
    // 1. Typ geändert → destroy & recreate
    if (oldVNode?.type !== newVNode?.type) {
        destroyNode(oldVNode, domNode);
        return createNode(newVNode, domNode);
    }
    
    // 2. Text-Node
    if (typeof newVNode === 'string' || typeof newVNode === 'number') {
        if (oldVNode !== newVNode) {
            domNode.textContent = newVNode;
        }
        return;
    }
    
    // 3. Component-Node
    if (typeof newVNode.type === 'function') {
        // Re-use existing component instance, update props
        const instance = oldVNode._instance;
        instance._props = newVNode.props;
        instance.onPropsChanged();
        instance._rerender();
        return;
    }
    
    // 4. Element-Node
    diffAttributes(oldVNode.props, newVNode.props, domNode);
    diffChildren(oldVNode.children, newVNode.children, domNode);
}

function diffChildren(oldChildren, newChildren, domNode) {
    // Keyed children: stable identity based on vnode.key
    // For each new child:
    //   - If matching key found in old → move/update
    //   - If no match → create new node
    // Remove orphaned old children
    // ...
}
```

### Scope v0.1

- Element-Diff: Tag, Attribute, Children
- Keyed Children für Listen (via `@key` aus Blazor)
- Component-Reuse bei selbem Typ
- Event-Listener-Updates

Nicht in v0.1: Fragments, Portals, Suspense, Memoization, Concurrent Rendering.

Aufwand: ~400-700 LOC (Preact-Referenz).

---

## Event-System

### EventArgs-Classes

Alle Blazor-EventArgs-Typen werden als thin Wrapper um native JS-Events nachgebildet. PascalCase-Properties.

```js
// runtime/events.js

export class MouseEventArgs {
    constructor(nativeEvent) { this._e = nativeEvent; }
    get ClientX() { return this._e.clientX; }
    get ClientY() { return this._e.clientY; }
    get ScreenX() { return this._e.screenX; }
    get ScreenY() { return this._e.screenY; }
    get Button() { return this._e.button; }
    get Buttons() { return this._e.buttons; }
    get CtrlKey() { return this._e.ctrlKey; }
    get ShiftKey() { return this._e.shiftKey; }
    get AltKey() { return this._e.altKey; }
    get MetaKey() { return this._e.metaKey; }
    get Type() { return this._e.type; }
    get Detail() { return this._e.detail; }
}

export class KeyboardEventArgs { /* ... */ }
export class ChangeEventArgs { /* ... */ }
export class FocusEventArgs { /* ... */ }
export class TouchEventArgs { /* ... */ }
export class DragEventArgs { /* ... */ }
export class WheelEventArgs { /* ... */ }
export class ClipboardEventArgs { /* ... */ }
export class PointerEventArgs { /* ... */ }
export class ProgressEventArgs { /* ... */ }
export class ErrorEventArgs { /* ... */ }
```

Komplette Liste wie Blazor.

### Event-Handler-Binding

Transpiler emittiert:

```js
// @onclick="IncrementCount"
h('button', { onclick: wrapHandler(this, this.incrementCount, MouseEventArgs) })

// @onclick="@(e => DoThing(e, 5))"
h('button', { onclick: wrapHandler(this, (e) => this.doThing(e, 5), MouseEventArgs) })
```

`wrapHandler` macht:
1. Native Event → wrapped EventArgs
2. Handler aufrufen
3. Nach Handler: `component.stateHasChanged()` automatisch

### Event-Modifier

`@onclick:preventDefault` und `@onclick:stopPropagation` werden vom Transpiler als Wrapper-Function emittiert:

```js
onclick: (e) => { e.preventDefault(); wrapHandler(...)(e); }
```

---

## Router

Client-seitiger Router mit voller Blazor-Syntax-Kompatibilität.

### Route-Syntax (alle unterstützt)

```csharp
@page "/"
@page "/users"
@page "/users/{id}"                 // Parameter
@page "/users/{id:int}"             // mit Type-Constraint
@page "/users/{id:int?}"            // optional
@page "/files/{*path}"              // Catch-all
@page "/posts/{year:int}/{month:int}"  // Multiple
```

Type-Constraints: `int`, `long`, `guid`, `bool`, `datetime`, `decimal`, `double`, `float`, `nonfile`.

### Route-Matching (Pseudo-Code)

```js
// runtime/router.js

class Router {
    routes = [];  // { pattern, component, constraints }
    
    register(pattern, component) {
        const parsed = parseRoute(pattern);  // tokenize, extract params + constraints
        this.routes.push({ pattern: parsed, component });
    }
    
    match(url) {
        for (const route of this.routes) {
            const result = tryMatch(route.pattern, url);
            if (result.matched) {
                return { component: route.component, params: result.params };
            }
        }
        return null;  // 404
    }
    
    async navigate(url) {
        const canNavigate = await this._runLocationChangingHandlers(url);
        if (!canNavigate) return;
        
        history.pushState(null, '', url);
        this._render(url);
        this._emitLocationChanged(url);
    }
    
    _handlePopState() {
        // Back/Forward-Button
        const url = window.location.pathname;
        this._runLocationChangingHandlers(url).then(allowed => {
            if (!allowed) {
                history.pushState(null, '', this._currentUrl);
            } else {
                this._render(url);
            }
        });
    }
}
```

### NavigationManager-API

Voll Blazor-kompatibel, inklusive `RegisterLocationChangingHandler`.

```js
// Exposed as C# API via transpiler-mapping:
class NavigationManager {
    get uri() { return window.location.href; }
    get baseUri() { return document.baseURI; }
    
    navigateTo(uri, options) { router.navigate(uri, options); }
    toAbsoluteUri(relative) { /* ... */ }
    toBaseRelativePath(absolute) { /* ... */ }
    
    // LocationChanged event
    onLocationChanged(handler) { /* subscribe */ }
    
    // LocationChanging handler (blockable)
    registerLocationChangingHandler(handler) { /* ... */ }
}
```

Aufwand Router+NavigationManager: ~600-1000 LOC.

---

## Dependency Injection

**Alles Singleton.** Keine Transient, keine Scopes. Simpel.

### Container-API

```js
// runtime/container.js

export class Container {
    _services = new Map();     // Type → Instance
    _factories = new Map();    // Type → Factory-Function
    
    register(type, factoryOrInstance) {
        if (typeof factoryOrInstance === 'function') {
            this._factories.set(type, factoryOrInstance);
        } else {
            this._services.set(type, factoryOrInstance);
        }
    }
    
    resolve(type) {
        if (this._services.has(type)) {
            return this._services.get(type);
        }
        if (this._factories.has(type)) {
            const factory = this._factories.get(type);
            const instance = factory(this);
            this._services.set(type, instance);  // cache as singleton
            return instance;
        }
        throw new Error(`Service not registered: ${type}`);
    }
}
```

### Registration (vom Transpiler generiert)

Aus User's `Program.cs`:

```csharp
services.AddSingleton<UserService>();
services.AddSingleton<IUserApi, UserApi>();
```

Transpiler emittiert:

```js
container.register('UserService', (c) => new UserService());
container.register('IUserApi', (c) => new UserApi(c.resolve('HttpClient')));
```

---

## Store-Runtime

`IStore<T>` ist eine generische JS-Class. Alle Store-Instanzen (für verschiedene Typen) nutzen dieselbe Class.

### Implementation

```js
// runtime/store.js

export class Store {
    _data = new Map();
    _listeners = new Set();
    _batchDepth = 0;
    _batchDirty = false;
    
    get(key) { return this._data.get(key); }
    
    set(key, value) {
        this._data.set(key, value);
        this._notifyChange();
    }
    
    delete(key) {
        this._data.delete(key);
        this._notifyChange();
    }
    
    getAll() { return Array.from(this._data.values()); }
    
    clear() {
        this._data.clear();
        this._notifyChange();
    }
    
    get count() { return this._data.size; }
    
    has(key) { return this._data.has(key); }
    
    where(predicate) {
        return Array.from(this._data.values()).filter(predicate);
    }
    
    batch(updates) {
        this._batchDepth++;
        try {
            updates();
        } finally {
            this._batchDepth--;
            if (this._batchDepth === 0 && this._batchDirty) {
                this._batchDirty = false;
                this._emitChange();
            }
        }
    }
    
    onChange(handler) {
        this._listeners.add(handler);
        return () => this._listeners.delete(handler);  // unsubscribe
    }
    
    _notifyChange() {
        if (this._batchDepth > 0) {
            this._batchDirty = true;
            return;
        }
        this._emitChange();
    }
    
    _emitChange() {
        for (const listener of this._listeners) listener();
    }
}
```

### User-Usage

```csharp
@inject IStore<User> Users

@code {
    protected override void OnInitialized() {
        Users.OnChange += StateHasChanged;
        Users.Set("1", new User { Id = 1, Name = "Alice" });
    }
    
    public void Dispose() {
        Users.OnChange -= StateHasChanged;
    }
}
```

---

## ApiClient-Runtime

Wrapped `fetch()`. Unterstützt Hooks, Timeout, Retry, Cancellation, FormData.

### Implementation

```js
// runtime/api-client.js

export class ApiClient {
    constructor(baseUrl, config = {}) {
        this.baseUrl = baseUrl;
        this.config = {
            timeout: 30000,              // default 30s
            retryCount: 3,
            retryBackoff: 'exponential', // 1s, 2s, 4s
            ...config,
        };
    }
    
    // Hooks (User überschreibt)
    async configureRequest(request) {}
    async handleResponse(response) {}
    
    async get(path, opts)    { return this._send('GET', path, null, opts); }
    async post(path, body, opts) { return this._send('POST', path, body, opts); }
    async put(path, body, opts)  { return this._send('PUT', path, body, opts); }
    async delete(path, opts)     { return this._send('DELETE', path, null, opts); }
    
    async _send(method, path, body, opts = {}) {
        const request = {
            method,
            path,
            headers: {},
            body,
        };
        
        await this.configureRequest(request);
        
        let lastError;
        const maxAttempts = opts.skipRetry ? 1 : this.config.retryCount;
        
        for (let attempt = 0; attempt < maxAttempts; attempt++) {
            try {
                return await this._sendOnce(request, opts);
            } catch (err) {
                lastError = err;
                if (err.status && err.status < 500) throw err;  // no retry on 4xx
                if (attempt < maxAttempts - 1) {
                    await this._backoff(attempt);
                }
            }
        }
        throw lastError;
    }
    
    async _sendOnce(request, opts) {
        const url = this.baseUrl + request.path;
        const controller = opts.abortSignal 
            ? null 
            : new AbortController();
        const signal = opts.abortSignal || controller.signal;
        const timeout = setTimeout(
            () => controller?.abort(),
            opts.timeout || this.config.timeout
        );
        
        try {
            const isFormData = request.body instanceof FormData;
            const headers = { ...request.headers };
            if (request.body && !isFormData) {
                headers['Content-Type'] = 'application/json';
            }
            
            const fetchResponse = await fetch(url, {
                method: request.method,
                headers,
                body: request.body 
                    ? (isFormData ? request.body : JSON.stringify(request.body))
                    : undefined,
                signal,
            });
            
            clearTimeout(timeout);
            
            const response = {
                statusCode: fetchResponse.status,
                headers: Object.fromEntries(fetchResponse.headers),
                body: await fetchResponse.text(),
            };
            
            await this.handleResponse(response);
            
            if (!fetchResponse.ok) {
                throw new ApiException(response);
            }
            
            return response.body ? JSON.parse(response.body) : null;
        } finally {
            clearTimeout(timeout);
        }
    }
    
    _backoff(attempt) {
        const delay = this.config.retryBackoff === 'exponential'
            ? Math.pow(2, attempt) * 1000
            : 1000;
        return new Promise(r => setTimeout(r, delay));
    }
}

export class ApiException extends Error {
    constructor(response) {
        super(`API error ${response.statusCode}`);
        this.statusCode = response.statusCode;
        this.response = response;
    }
}
```

### Source-Generator-Output für [ApiRoute]

Aus User's Interface:

```csharp
[ApiRoute("api/users")]
public interface IUserApi {
    [Get] Task<User[]> GetAllAsync();
    [Get("{id}")] Task<User> GetByIdAsync(int id);
    [Post] Task<User> CreateAsync(User user);
    [Delete("{id}")] Task DeleteAsync(int id);
}
```

Generierte Implementation (zur Compile-Time, von Source-Generator):

```csharp
public class _UserApi_Generated : ApiClient, IUserApi {
    public _UserApi_Generated(HttpClient http) : base(http) { }
    
    public Task<User[]> GetAllAsync() 
        => Get<User[]>("api/users");
    public Task<User> GetByIdAsync(int id) 
        => Get<User>($"api/users/{id}");
    public Task<User> CreateAsync(User user) 
        => Post<User>("api/users", user);
    public Task DeleteAsync(int id) 
        => Delete($"api/users/{id}");
}
```

Der Transpiler transpiliert dann diese generierte Class wie jeden anderen ApiClient.

---

## IJSRuntime

Für Ad-hoc-JS-Calls.

### API

```js
// runtime/js-runtime.js

export class JSRuntime {
    async invokeAsync(identifier, ...args) {
        const fn = this._resolveFunction(identifier);
        const result = await fn(...args);
        return result;
    }
    
    async invokeVoidAsync(identifier, ...args) {
        await this.invokeAsync(identifier, ...args);
    }
    
    _resolveFunction(identifier) {
        // "localStorage.getItem" → window.localStorage.getItem
        const parts = identifier.split('.');
        let obj = window;
        for (const part of parts.slice(0, -1)) obj = obj[part];
        const fn = obj[parts[parts.length - 1]];
        if (typeof fn !== 'function') {
            throw new Error(`Not a function: ${identifier}`);
        }
        return fn.bind(obj);
    }
}
```

Kein `[JsImport]` in v0. Kein Type-Safe JS-Wrapping. User nutzt IJSRuntime wie in Blazor.

---

## CSS-Pipeline

**Piggy-back auf Microsoft's Blazor-CSS-Pipeline.** Razor-Compiler generiert bereits scoped CSS mit `b-xxxxx` Attributen. Razorshave nimmt den generierten Output und kopiert ihn ins Bundle.

- `Component.razor.css` → vom Razor-Compiler zu scopedem CSS transformiert
- `app.css` + scoped CSS werden zu einem File gebundelt (auch Microsoft-managed)
- Razorshave liest `obj/Debug/net8.0/scopedcss/projectbundle/*.styles.css` und packt's ins `dist/`
- `::deep` Selector funktioniert automatisch

**Null eigener CSS-Parser. Null Selector-Rewriting. Null Maintenance.**

---

## Asset-Handling

**1:1 Copy von `wwwroot/` nach `dist/`.** Keine Optimierung, kein Hashing auf Assets, kein Processing. Wer Image-Optimization will, nutzt eigenen Build-Step vor Razorshave.

---

## Bundle-Format

### Runtime-Packaging

Die Runtime ist **ESM-Modul**, tree-shakeable. User's Razorshave-Build kombiniert:

- User-Code (transpiliert)
- Razorshave-Runtime (nur was genutzt wird)
- decimal.js-light (falls User `decimal` nutzt)

Bundler: **esbuild**, als embedded Binary im CLI-Tool, kein Node.js-Requirement beim User.

### Output-Struktur

```
dist/
├── index.html           ← Entry-Point, referenziert app.[hash].js und app.[hash].css
├── app.[hash].js        ← User-Code + Razorshave-Runtime (gebundled, minified)
├── app.[hash].css       ← Scoped CSS + globales CSS
├── assets/              ← 1:1 aus wwwroot
│   └── ...
└── favicon.ico
```

### Code-Splitting

**Ein Bundle in v0.1.** Kein Route-based Splitting. Alle Pages in einem `app.js`.

**v0.2:** Route-based Splitting auf Roadmap, wenn User-Feedback das rechtfertigt.

### Content-Hashing

- `app.js` und `app.css` kriegen Content-Hash im Filename (via esbuild)
- `index.html` ohne Hash (muss immer frisch geladen werden)
- Assets ohne Hash (nicht transformiert)

---

## Zusammenfassung der Entscheidungen

| Topic | Entscheidung |
|---|---|
| Auto-StateHasChanged | Ja, nach jedem Event-Handler |
| Render-Trigger | Gescheduled (requestAnimationFrame) |
| Lifecycle-Hooks | Vereinfacht, kein async/sync-Split |
| Component-Instantiierung | Property-Injection nach Construction |
| DI-Container | Nur Singleton |
| VDOM-Diff | Keyed children, Preact-Level (~500-700 LOC) |
| Router | Eigener, voller Blazor-Syntax-Support |
| Route-Constraints | Alle (int/long/guid/bool/datetime/decimal/double/float/nonfile) |
| NavigationManager | Voll inkl. LocationChangingHandler |
| Props | Live getter (Parent-Updates sichtbar) |
| EventCallback | Nicht special in v0.1 (normale Function) |
| Render-Output | Hyperscript h() |
| Store-Runtime | Eine generische Class für alle Typen |
| ApiClient | Voll inkl. Base-URL, Timeout, Retry, Cancellation, FormData |
| IJSRuntime | Ja |
| JsImport | Nein (nicht in Roadmap) |
| EventArgs | Alle Blazor-Typen, Wrapper-Classes mit PascalCase-Getter |
| CSS-Pipeline | Piggy-back auf Microsoft's Blazor-Pipeline |
| Asset-Handling | 1:1 copy, keine Transformation |
| Bundler | esbuild, embedded Binary |
| Bundle-Format | Ein Bundle, ESM, Content-Hash auf JS/CSS |
| Code-Splitting | Nicht in v0.1 |

---

## Target Bundle-Size (Runtime)

- Component-Base + h() + Diff: ~15-20 KB gzipped
- Router + NavigationManager: ~8-12 KB gzipped
- DI-Container: ~2 KB gzipped
- Store: ~1-2 KB gzipped
- ApiClient: ~5-8 KB gzipped
- IJSRuntime: ~1 KB gzipped
- EventArgs (alle Typen): ~3-5 KB gzipped
- Utilities: ~5 KB gzipped

**Total Razorshave-Runtime: ~40-55 KB gzipped.**

Plus User-Code plus optional decimal.js-light (~5 KB gzipped).

Mittelgroße Admin-App: **~150-250 KB gzipped total.** Vergleichbar mit React-Apps.

---

## Offene Punkte

Keine aktuellen. Alle Runtime-API-Fragen entschieden.
