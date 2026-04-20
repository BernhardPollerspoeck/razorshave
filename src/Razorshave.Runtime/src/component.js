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
  _rerender() {
    if (!this._container) return;

    for (const node of this._domNodes) {
      if (node.parentNode) node.parentNode.removeChild(node);
    }
    this._domNodes = [];

    const produced = renderComponentTree(this);
    if (produced === null) return;

    // Track roots for the next teardown. DocumentFragments splice their
    // children into the container directly, so snapshot them before append.
    const roots = produced.nodeType === 11 /* DOCUMENT_FRAGMENT_NODE */
      ? Array.from(produced.childNodes)
      : [produced];
    this._container.appendChild(produced);
    this._domNodes = roots;

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
