import { mountRoot, patchRoot } from './vdom.js';
import { container } from './container.js';
import { reportRuntimeError } from './errors.js';

// Module-level reference to the mounted root component. Child components
// can't rerender themselves (they have no _container of their own under the
// naive diff), so stateHasChanged bubbles here and re-renders the whole tree
// from the top. mount() sets this; see setActiveRoot.
let _activeRoot = null;

export function setActiveRoot(instance) {
  _activeRoot = instance;
}

// Base class for every transpiled Razor component.
//
// Subclasses override render() to return VDOM (a vnode, an array of vnodes, or
// null). State mutations are applied directly on the instance; the user (or
// the event-handler wrapper in vdom.js) calls stateHasChanged() to queue a
// rerender on the next frame.
//
// Child-instance reuse is handled by the reconciler in vdom.js: it walks the
// previous and next vnode trees side-by-side, preserves component instances
// when the Ctor (and optionally @key) matches at the same slot, and fires
// onDestroy when a component leaves the tree. Two <Counter /> siblings keep
// independent state because they occupy distinct positions in the vnode
// list; no separate Map-by-(Ctor,index) bookkeeping on the instance.

export class Component {
  constructor() {
    this.props = {};
    this._container = null;     // DOM element we render into (root only)
    this._vtree = null;         // previous vnode tree (for diff on next render)
    this._renderScheduled = false;
    this._hasRenderedBefore = false;
  }

  render() {
    return null;
  }

  // Lifecycle hooks (optional overrides, named to match Razorshave-Runtime-API.md).
  onInit()                           {}
  onPropsChanged()                   {}
  onAfterRender(_firstRender)        {}
  onDestroy()                        {}
  shouldRender()                     { return true; }

  // Schedules a rerender on the next frame. If this instance has a _container
  // (it's the mounted root) it re-renders itself; otherwise the request
  // bubbles to the active root — the naive diff rebuilds the subtree from
  // the top and reuses child instances by class so local state survives.
  stateHasChanged() {
    if (this._renderScheduled) return;
    this._renderScheduled = true;
    schedule(() => {
      this._renderScheduled = false;
      const target = this._container ? this : _activeRoot;
      if (target && target.shouldRender()) target._rerender();
    });
  }

  // Returns a stable bound reference to a method on this instance. Used by
  // the transpiler when rewriting `X.Event += this.Handler` — add and remove
  // must hand the event source the SAME function reference, otherwise the
  // unsubscribe silently misses and the listener leaks. Caching by method
  // name guarantees the same bound fn every call.
  _bound(name) {
    if (!this._boundCache) this._boundCache = Object.create(null);
    const cached = this._boundCache[name];
    if (cached) return cached;
    const fn = this[name];
    if (typeof fn !== 'function') return undefined;
    return this._boundCache[name] = fn.bind(this);
  }

  // Resolves the `_injects` manifest (emitted by ClassEmitter for every
  // [Inject] property) against the DI container, assigning each service onto
  // this instance. Called by mount() for the root and by vdom.js for every
  // child component before its first render.
  _resolveInjects(Ctor) {
    const manifest = Ctor._injects;
    if (!manifest) return;
    for (const propName of Object.keys(manifest)) {
      this[propName] = container.resolve(manifest[propName]);
    }
  }

  // Root-level lifecycle: first render synchronously, then kick off
  // onInitializedAsync and schedule a rerender once it resolves so any state
  // set inside it lands on screen. Mirrors Blazor's behaviour where the first
  // paint doesn't wait for async initialisation.
  _lifecycleStart(Ctor) {
    this._resolveInjects(Ctor);
    this.onInit?.();
    this._rerender();
    kickoffAsyncInit(this);
  }

  // Runs the reconciler against the container. First call mounts from
  // scratch; every subsequent call diffs the new vnode tree against the
  // previous one and patches DOM in place. DOM identity is preserved for
  // nodes whose type (and @key) match at the same slot, so focus, caret,
  // scroll position, and running CSS transitions all survive re-renders
  // without any special-case handling in this file.
  _rerender() {
    if (!this._container) return;

    const newVtree = this.render?.() ?? null;
    if (!this._hasRenderedBefore) {
      mountRoot(this._container, newVtree, this);
    } else {
      patchRoot(this._container, this._vtree, newVtree, this);
    }
    this._vtree = newVtree;

    this.onAfterRender(!this._hasRenderedBefore);
    this._hasRenderedBefore = true;
  }
}

// Called by vdom.js whenever a component is first instantiated (root via
// _lifecycleStart, children via the reuse path in renderComponentVnode).
// Bubbles stateHasChanged back when the promise resolves so the UI picks up
// any state the async hook set. Synchronous throws and async rejections are
// routed through reportRuntimeError — we still call stateHasChanged so any
// partial state the hook set before failing paints, and an error boundary
// (once we have one) can render.
export function kickoffAsyncInit(instance) {
  let p;
  try {
    p = instance.onInitializedAsync?.();
  } catch (err) {
    reportRuntimeError(err, { phase: 'onInitializedAsync', component: instance.constructor?.name });
    instance.stateHasChanged?.();
    return;
  }
  if (p && typeof p.then === 'function') {
    p.then(
      () => instance.stateHasChanged?.(),
      (err) => {
        reportRuntimeError(err, { phase: 'onInitializedAsync', component: instance.constructor?.name });
        instance.stateHasChanged?.();
      }
    );
  }
}

// Layouts expose `body` — the RenderFragment the router hands them for the
// currently-matched page. Full wiring lands with the router (5.12).
export class LayoutComponent extends Component {
  get body() {
    return this.props?.body ?? null;
  }
}

function schedule(cb) {
  if (typeof requestAnimationFrame === 'function') {
    requestAnimationFrame(cb);
  } else {
    queueMicrotask(cb);
  }
}
