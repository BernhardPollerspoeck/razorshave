import { mountRoot, patchRoot } from './vdom.js';
import { container } from './container.js';
import { reportRuntimeError } from './errors.js';

// Stack of currently-mounted root components. Child components can't
// rerender themselves (they have no `_container` of their own), so their
// `stateHasChanged` bubbles to the topmost active root and re-renders its
// tree. We use a stack rather than a single slot so multiple `mount()` /
// `unmount()` calls coexist cleanly — common in test suites and micro-
// frontend setups. The topmost entry is the "last mounted" root.
//
// `pushActiveRoot` / `popActiveRoot` are the authoritative mutations;
// `getActiveRoot()` is what stateHasChanged consults.
const _activeRootStack = [];

export function pushActiveRoot(instance) {
  _activeRootStack.push(instance);
}

export function popActiveRoot(instance) {
  const idx = _activeRootStack.lastIndexOf(instance);
  if (idx >= 0) _activeRootStack.splice(idx, 1);
}

export function getActiveRoot() {
  return _activeRootStack.length > 0
    ? _activeRootStack[_activeRootStack.length - 1]
    : null;
}

// Back-compat shim: old code called `setActiveRoot(instance)` as "make
// this the currently active root". With the stack model that's equivalent
// to pushing — but we avoid doubling up if the instance is already on top.
export function setActiveRoot(instance) {
  if (instance === null) return;
  if (getActiveRoot() === instance) return;
  pushActiveRoot(instance);
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
    // Dedupe at the TARGET that will actually rerender, not on `this`.
    // Children bubble to `getActiveRoot()`, and ten child calls in the same
    // frame should produce exactly one root rerender. If we set the flag
    // on `this` (the child) first, a root-level call could still schedule
    // a second rAF for the same frame — one from child, one from root,
    // root rerenders twice.
    const target = this._container ? this : getActiveRoot();
    if (!target || target._renderScheduled) return;
    target._renderScheduled = true;
    schedule(() => {
      target._renderScheduled = false;
      if (target.shouldRender()) target._rerender();
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
  //
  // Idempotent guard: tests or subclasses that accidentally call
  // `_lifecycleStart` a second time would otherwise see `onInit` fire again
  // and kick off a duplicate async-init — this catches the mistake rather
  // than silently running double.
  _lifecycleStart(Ctor) {
    if (this._lifecycleStarted) return;
    this._lifecycleStarted = true;
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

// One-time warning flag so we don't spam the console in Node/SSR runs. The
// microtask fallback changes observable timing (pre-paint vs post-paint):
// `onAfterRender` that reads layout-dependent state will see different
// numbers across environments. The warning makes the silent divergence
// visible once so users can tell browser-behaviour and test-behaviour apart.
let _warnedSchedule = false;

function schedule(cb) {
  if (typeof requestAnimationFrame === 'function') {
    requestAnimationFrame(cb);
    return;
  }
  if (!_warnedSchedule) {
    _warnedSchedule = true;
    // Visible-once — never silent.
    // eslint-disable-next-line no-console
    console.warn(
      '[razorshave] requestAnimationFrame not available — falling back to queueMicrotask. '
      + 'Timing semantics differ from browsers (pre-paint vs post-paint); expected in SSR/Node tests, '
      + 'unexpected anywhere else.'
    );
  }
  queueMicrotask(cb);
}
