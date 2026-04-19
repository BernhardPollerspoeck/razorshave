import { render } from './vdom.js';

// Base class for every transpiled Razor component.
//
// Subclasses override render() to return VDOM (a vnode, an array of vnodes, or
// null). State mutations are applied directly on the instance; the user (or
// the event-handler wrapper in vdom.js) calls stateHasChanged() to queue a
// rerender on the next frame.

export class Component {
  constructor() {
    this.props = {};
    this._container = null;     // DOM element we render into
    this._domNodes = [];        // root nodes we currently own
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

  stateHasChanged() {
    if (this._renderScheduled) return;
    this._renderScheduled = true;
    schedule(() => {
      this._renderScheduled = false;
      if (this.shouldRender()) this._rerender();
    });
  }

  // Fresh render → replace. Keyed diffing is a later step; for M0 we nuke the
  // previous subtree and reinsert. Performance is acceptable while there are
  // few components and interactions.
  _rerender() {
    if (!this._container) return;

    for (const node of this._domNodes) {
      if (node.parentNode) node.parentNode.removeChild(node);
    }
    this._domNodes = [];

    const tree = this.render();
    const produced = render(tree, this);
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
