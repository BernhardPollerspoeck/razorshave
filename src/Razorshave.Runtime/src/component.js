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
  // Focus-preservation: if the currently-focused element sits inside our
  // subtree, we locate its counterpart in the freshly-rendered tree and
  // splice the live node into that slot before the DOM swap. The live node
  // stays in-document through the whole operation — without this the naive
  // replace would blur the input on every keystroke that fires
  // stateHasChanged (@bind becomes unusable: caret loses position, the
  // :focus CSS transition replays once per character).
  _rerender() {
    if (!this._container) return;

    const active = typeof document !== 'undefined' ? document.activeElement : null;
    const preserving = active && this._container.contains(active);
    const focusPath = preserving ? computePath(active, this._container) : null;

    const produced = renderComponentTree(this);
    if (produced === null) {
      this._container.replaceChildren();
      this._domNodes = [];
      return;
    }

    const roots = produced.nodeType === 11 /* DOCUMENT_FRAGMENT_NODE */
      ? Array.from(produced.childNodes)
      : [produced];

    // Try to keep the focused element identity across the swap. When the
    // focused path resolves to a same-tag counterpart in the new tree, we
    // sync the fresh attributes onto the live node and splice the live node
    // into the new tree in the counterpart's slot.
    let spliced = false;
    if (focusPath && focusPath.length > 0 && focusPath[0] < roots.length) {
      const subPath = focusPath.slice(1);
      const counterpart = resolveSubPath(roots[focusPath[0]], subPath);
      if (counterpart && counterpart.tagName === active.tagName) {
        syncNonDisruptiveAttrs(active, counterpart);
        counterpart.parentNode.replaceChild(active, counterpart);
        spliced = true;
      }
    }

    // Atomic container swap — old subtree out, new subtree (with the spliced
    // live node, if any) in. Using replaceChildren batches the mutation so
    // the focused live node transitions through at most one off-document
    // tick, avoiding the full focus/blur/focus churn of the naive approach.
    this._container.replaceChildren(...roots);
    this._domNodes = roots;

    // Some browsers drop focus when a node is temporarily detached during
    // replaceChild. Re-asserting is cheap and a no-op when focus survived.
    if (spliced && document.activeElement !== active && typeof active.focus === 'function') {
      active.focus();
    }

    this.onAfterRender(!this._hasRenderedBefore);
    this._hasRenderedBefore = true;
  }
}

// Child-index path from `container` down to `node` (inclusive of node).
// Returns null when node is not inside container.
function computePath(node, container) {
  const path = [];
  let cur = node;
  while (cur && cur !== container) {
    const parent = cur.parentNode;
    if (!parent) return null;
    path.unshift(Array.prototype.indexOf.call(parent.childNodes, cur));
    cur = parent;
  }
  return cur === container ? path : null;
}

// Walk `root` by child indices and return the descendant at the end.
function resolveSubPath(root, subPath) {
  let cur = root;
  for (const i of subPath) {
    if (!cur || !cur.childNodes || !cur.childNodes[i]) return null;
    cur = cur.childNodes[i];
  }
  return cur;
}

// Copy attributes from the fresh element to the live element, but skip
// properties that would disrupt editing on inputs. Specifically:
//   * `value` — rewriting it would reset the caret to the end.
//   * `selectionStart` / `selectionEnd` — not a real attribute but some
//     inputs encode caret via properties; leaving them alone keeps caret.
// Event listeners aren't touched; the live node keeps its existing handlers,
// which close over the component instance and therefore still see current
// state. A future diff pass can swap handlers when closures capture loop
// variables.
function syncNonDisruptiveAttrs(live, fresh) {
  const tag = live.tagName;
  const skip = (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT')
    ? new Set(['value'])
    : null;

  // Remove attributes the fresh element no longer has.
  for (const attr of Array.from(live.attributes)) {
    if (skip?.has(attr.name)) continue;
    if (!fresh.hasAttribute(attr.name)) live.removeAttribute(attr.name);
  }
  // Apply fresh attribute values where they differ.
  for (const attr of Array.from(fresh.attributes)) {
    if (skip?.has(attr.name)) continue;
    if (live.getAttribute(attr.name) !== attr.value) {
      live.setAttribute(attr.name, attr.value);
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
