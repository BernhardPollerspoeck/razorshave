import { renderComponentTree } from './vdom.js';
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
// Child-instance reuse: every Component holds a Map<Ctor, Array<instance>> of
// its last render's child components — one array per Ctor, ordered by
// render-occurrence. When the parent re-renders, vdom.js walks the previous
// array at the same (Ctor, index) slot; if found, the instance is reused
// (props updated, state preserved). Keying by index (not just by Ctor) is
// what lets two <Counter /> siblings keep independent state — without it,
// both would collide on the same map key and the second render's counter
// would steal the first's instance.

export class Component {
  constructor() {
    this.props = {};
    this._container = null;     // DOM element we render into (root only)
    this._domNodes = [];        // root nodes we currently own
    this._renderScheduled = false;
    this._hasRenderedBefore = false;

    // Child components from the most recent render, bucketed by Ctor. Vdom.js
    // replaces this with a fresh Map on each render and looks into the
    // previous one via _prevChildrenForThisRender to decide reuse-vs-create.
    this._childInstances = new Map();
    this._prevChildrenForThisRender = null;
    // Per-render counter of how many times each Ctor has appeared so far in
    // the current render pass. Reset at the top of renderComponentTree.
    this._nextChildIndex = new Map();
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

  // Fresh render → replace. Keyed DOM diffing is a later step; for M0 we nuke
  // the previous subtree and reinsert. Child component instances are kept
  // across rerenders via _childInstances so their state persists.
  //
  // A targeted focus-restore runs around the teardown: the naive replace
  // would otherwise lose input focus (and caret position) on every keystroke
  // that fires stateHasChanged — e.g. `@bind` on a text input becomes
  // unusable. Capture the active element's path before teardown, then walk
  // the new tree to the same path and re-focus afterwards.
  _rerender() {
    if (!this._container) return;

    const focusSnapshot = captureFocus(this._container);

    for (const node of this._domNodes) {
      if (node.parentNode) node.parentNode.removeChild(node);
    }
    this._domNodes = [];

    const produced = renderComponentTree(this);
    if (produced === null) {
      restoreFocus(this._container, focusSnapshot);
      return;
    }

    // Track roots for the next teardown. DocumentFragments splice their
    // children into the container directly, so snapshot them before append.
    const roots = produced.nodeType === 11 /* DOCUMENT_FRAGMENT_NODE */
      ? Array.from(produced.childNodes)
      : [produced];
    this._container.appendChild(produced);
    this._domNodes = roots;

    restoreFocus(this._container, focusSnapshot);

    this.onAfterRender(!this._hasRenderedBefore);
    this._hasRenderedBefore = true;
  }
}

// Snapshot the focused element's position inside `container` (as a child-
// index path) plus its caret selection. Returns null when focus is elsewhere
// or not recoverable — the caller then treats the rerender as normal.
function captureFocus(container) {
  if (typeof document === 'undefined') return null;
  const active = document.activeElement;
  if (!active || !container.contains(active)) return null;

  const path = [];
  let cur = active;
  while (cur && cur !== container) {
    const parent = cur.parentNode;
    if (!parent) return null;
    path.unshift(Array.prototype.indexOf.call(parent.childNodes, cur));
    cur = parent;
  }
  return {
    path,
    selectionStart: typeof active.selectionStart === 'number' ? active.selectionStart : null,
    selectionEnd: typeof active.selectionEnd === 'number' ? active.selectionEnd : null,
  };
}

function restoreFocus(container, snapshot) {
  if (!snapshot) return;
  let cur = container;
  for (const i of snapshot.path) {
    if (!cur || !cur.childNodes || !cur.childNodes[i]) return;
    cur = cur.childNodes[i];
  }
  if (!cur || typeof cur.focus !== 'function') return;
  cur.focus();
  if (snapshot.selectionStart !== null && typeof cur.setSelectionRange === 'function') {
    try {
      cur.setSelectionRange(snapshot.selectionStart, snapshot.selectionEnd ?? snapshot.selectionStart);
    } catch {
      // Not every input type supports setSelectionRange (e.g. number, email
      // in some browsers). Silent fallback — the focus itself already made
      // typing recover.
    }
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
