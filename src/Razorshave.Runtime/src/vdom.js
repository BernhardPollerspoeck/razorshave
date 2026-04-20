import { kickoffAsyncInit } from './component.js';
import { reportRuntimeError } from './errors.js';

// Keyed VDOM reconciler.
//
// Each `render()` call returns a fresh vnode tree. Rather than tear the DOM
// down and rebuild (correctness-first but UX-hostile — focus lost, CSS
// transitions re-fire, scroll jumps), we diff the new tree against the
// previous one and patch the live DOM in place: matching nodes keep their
// identity, attribute changes are applied, only structurally new subtrees
// get created.
//
// Component DOM boundaries — comment-marker model.
//
// Every mounted component vnode is bracketed by two Comment nodes:
// `<!--rs-->` before the subtree, `<!--/rs-->` after. Keeping the markers
// present even when the component renders `null` means every component
// always has a stable DOM range (`_startMarker` to `_endMarker`), so:
//   * patchComponent can insert new content between the markers even after
//     a render that returned `null` (no "null-host skip" gap),
//   * keyed-list reorder can move the whole range as one unit, picking up
//     multi-root fragment returns for free,
//   * removeFromDom has a deterministic span to tear down.
// The markers cost 2 DOM nodes per mounted component; negligible compared
// to the correctness and simplicity wins.
//
// vnode shapes:
//   primitive string/number   — raw text, normalised to {_kind:'text'} during
//                               mount so the resulting Text node can be
//                               tracked across renders
//   null/undefined/false/true — skipped
//   {type, props, children, key}           — element or component (type is
//                                            a string tag for elements, a
//                                            Ctor function for components)
//   {type:'__markup__', html}              — raw HTML (AddMarkupContent)
//   array                                  — fragment; flattened during
//                                            normalisation
//
// Mount-time properties added to vnodes:
//   _dom                         — DOM node for element / text vnodes
//   _markupNodes                 — array of DOM roots for a markup vnode
//   _children                    — normalised children (primitives wrapped
//                                  as text vnodes)
//   _instance                    — Component instance for component vnodes
//   _startMarker / _endMarker    — Comment nodes bracketing a component's
//                                  DOM subtree
//
// We mutate vnodes intentionally — this matches Preact's approach and keeps
// the bookkeeping off to the side of user code.

// --- normalisation ---

// Flatten a children array: recurse into nested arrays, drop skipped values,
// wrap raw primitives as text vnodes so every surviving child has stable
// identity we can attach `_dom` to.
function normalizeChildren(children) {
  const out = [];
  flatten(children, out);
  return out;
}

function flatten(items, out) {
  for (const c of items) {
    if (c == null || c === false || c === true) continue;
    if (Array.isArray(c)) {
      flatten(c, out);
    } else if (typeof c === 'string' || typeof c === 'number') {
      out.push({ _kind: 'text', value: String(c) });
    } else {
      out.push(c);
    }
  }
}

function isVoid(v) {
  return v == null || v === false || v === true;
}

// Shallow-equals for component props. Identical objects short-circuit;
// otherwise compare every key's reference (no deep walk — cheap and matches
// React/Preact semantics).
function shallowPropsEqual(a, b) {
  if (a === b) return true;
  if (!a || !b) return false;
  const ka = Object.keys(a), kb = Object.keys(b);
  if (ka.length !== kb.length) return false;
  for (const k of ka) if (a[k] !== b[k]) return false;
  return true;
}

// --- public API ---

// Create DOM for a fresh vnode tree. First mount path + one-shot helper used
// by unit tests. Subsequent updates go through `patchRoot`.
export function render(vnode, owner) {
  return createDom(wrapTopLevel(vnode), owner);
}

// Diff `oldVtree` against `newVtree` inside `container`, patching DOM in
// place. Fires onDestroy on components that leave the tree. Returns the
// normalised vnode list the reconciler actually worked on — the caller must
// store this (not the raw render output) so the next patch's oldVtree has
// `_dom` references on every text-vnode. Previously we returned `newVtree`
// verbatim, which for raw strings was a dead `'text'` value that re-wrapped
// into a zero-`_dom` vnode on the next patch, causing the old DOM to linger
// and accumulate on every render.
export function patchRoot(container, oldVtree, newVtree, owner) {
  const oldList = asList(oldVtree);
  const newList = asList(newVtree);
  patchChildren(container, oldList, newList, owner, /* endAnchor */ null);
  return newList;
}

// Mount a fresh vnode tree inside `container`, appending the resulting DOM.
// Returns the normalised vnode list (same contract as patchRoot).
export function mountRoot(container, vtree, owner) {
  const list = asList(vtree);
  for (const v of list) {
    const dom = createDom(v, owner);
    if (dom) container.appendChild(dom);
  }
  return list;
}

function asList(vtree) {
  if (isVoid(vtree)) return [];
  if (Array.isArray(vtree)) return normalizeChildren(vtree);
  if (typeof vtree === 'string' || typeof vtree === 'number') {
    return [{ _kind: 'text', value: String(vtree) }];
  }
  return [vtree];
}

function wrapTopLevel(vnode) {
  if (typeof vnode === 'string' || typeof vnode === 'number') {
    return { _kind: 'text', value: String(vnode) };
  }
  return vnode;
}

// --- create ---

function createDom(vnode, owner) {
  if (isVoid(vnode)) return null;

  if (typeof vnode === 'object' && vnode._kind === 'text') {
    vnode._dom = document.createTextNode(vnode.value);
    return vnode._dom;
  }

  if (Array.isArray(vnode)) {
    const frag = document.createDocumentFragment();
    const normalized = normalizeChildren(vnode);
    for (const child of normalized) {
      const node = createDom(child, owner);
      if (node) frag.appendChild(node);
    }
    return frag;
  }

  if (vnode.type === '__markup__') {
    const tpl = document.createElement('template');
    tpl.innerHTML = vnode.html;
    vnode._markupNodes = Array.from(tpl.content.childNodes);
    return tpl.content;
  }

  if (typeof vnode.type === 'function') {
    return mountComponent(vnode, owner);
  }

  const el = document.createElement(vnode.type);
  vnode._dom = el;
  applyProps(el, null, vnode.props, owner);
  const children = normalizeChildren(vnode.children);
  vnode._children = children;
  for (const child of children) {
    const node = createDom(child, owner);
    if (node) el.appendChild(node);
  }
  return el;
}

function mountComponent(vnode, owner) {
  const Ctor = vnode.type;
  const instance = new Ctor();
  vnode._instance = instance;
  instance.props = vnode.props || {};
  instance._resolveInjects?.(Ctor);
  instance.onInit?.();
  kickoffAsyncInit(instance);

  const subtree = instance.render?.();
  const wrapped = wrapTopLevel(subtree);
  instance._vtree = wrapped;

  // Wrap the subtree in comment markers so the component's DOM range is
  // always identifiable, even when `render()` returned null. Markers carry
  // the class name for DevTools readability; runtime only uses node identity.
  const name = Ctor.name || 'Component';
  const frag = document.createDocumentFragment();
  vnode._startMarker = document.createComment(` rs:${name} `);
  vnode._endMarker = document.createComment(` /rs:${name} `);
  frag.appendChild(vnode._startMarker);
  const subDom = createDom(wrapped, instance);
  if (subDom) frag.appendChild(subDom);
  frag.appendChild(vnode._endMarker);

  // Lifecycle parity with Root: every component sees onAfterRender(true) on
  // mount. Fire bottom-up — our subtree's children have already mounted by
  // the time we reach this line, so their onAfterRender already fired.
  instance._hasRenderedBefore = true;
  try { instance.onAfterRender?.(true); }
  catch (err) { reportRuntimeError(err, { phase: 'onAfterRender', component: name }); }
  return frag;
}

// --- props + listeners ---

function applyProps(el, oldProps, newProps, owner) {
  oldProps = oldProps || {};
  newProps = newProps || {};

  for (const key of Object.keys(oldProps)) {
    if (key in newProps) continue;
    if (key.startsWith('on') && typeof oldProps[key] === 'function') {
      clearListener(el, toEventType(key));
    } else {
      el.removeAttribute(key);
    }
  }

  for (const key of Object.keys(newProps)) {
    const value = newProps[key];
    if (key.startsWith('on')) {
      if (typeof value === 'function') setListener(el, toEventType(key), value, owner);
      else clearListener(el, toEventType(key));
      continue;
    }
    if (value === null || value === undefined || value === false) {
      el.removeAttribute(key);
    } else if (value === true) {
      if (el.getAttribute(key) !== '') el.setAttribute(key, '');
    } else {
      const str = String(value);
      if (el.getAttribute(key) !== str) el.setAttribute(key, str);
    }

    // Form-control property sync: the attribute pokes the initial value; the
    // browser's live state lives on the .value / .checked property. Keep them
    // aligned so controlled inputs reflect the component's state.
    if (key === 'value' && 'value' in el) {
      const target = value == null || value === false ? '' : String(value);
      if (el.value !== target) el.value = target;
    } else if (key === 'checked' && el.type === 'checkbox') {
      el.checked = !!value;
    }
  }
}

function toEventType(propKey) { return propKey.slice(2).toLowerCase(); }

// Preact-style listener attachment. One DOM-level listener per event type
// per element, forwarded through `trampoline` which looks up the current
// user handler fresh on every event. Rerenders that swap closures just
// overwrite the entry — no addEventListener/removeEventListener churn.
function setListener(el, type, userFn, owner) {
  if (!el._rsListeners) {
    el._rsListeners = Object.create(null);
    el._rsListenerAttached = Object.create(null);
  }
  if (!el._rsListenerAttached[type]) {
    el._rsListenerAttached[type] = true;
    el.addEventListener(type, trampoline);
  }
  el._rsListeners[type] = { userFn, owner };
}

function clearListener(el, type) {
  if (el._rsListeners) el._rsListeners[type] = null;
}

async function trampoline(e) {
  const entry = e.currentTarget?._rsListeners?.[e.type];
  if (!entry) return;
  const { userFn, owner } = entry;
  let threw = false;
  try {
    const result = userFn(e);
    if (result && typeof result.then === 'function') await result;
  } catch (err) {
    threw = true;
    reportRuntimeError(err, {
      phase: 'event handler',
      component: owner?.constructor?.name,
    });
  } finally {
    // Event handlers that never touched state (pure side-effects, analytics,
    // a `navigateTo` that itself triggers rerender via nav-listener) can
    // opt out by setting `e.preventRazorshaveRerender = true` inside the
    // handler. Default stays "assume mutation happened" to match Blazor
    // semantics — explicit opt-out rather than opt-in keeps existing
    // user code working. On throw we still rerender so an error-boundary
    // (when we have one) can paint.
    if (threw || !e.preventRazorshaveRerender) {
      owner?.stateHasChanged?.();
    }
  }
}

// --- patch ---

function patchNode(parent, oldVnode, newVnode, owner, endAnchor) {
  if (isVoid(newVnode)) {
    if (!isVoid(oldVnode)) removeFromDom(oldVnode);
    return null;
  }
  if (isVoid(oldVnode)) {
    const node = createDom(newVnode, owner);
    if (node) parent.insertBefore(node, endAnchor);
    return newVnode;
  }

  // Text
  if (typeof oldVnode === 'object' && oldVnode._kind === 'text'
      && typeof newVnode === 'object' && newVnode._kind === 'text') {
    if (oldVnode.value !== newVnode.value) {
      oldVnode._dom.data = newVnode.value;
    }
    newVnode._dom = oldVnode._dom;
    return newVnode;
  }

  // Markup
  if (typeof oldVnode === 'object' && oldVnode.type === '__markup__'
      && typeof newVnode === 'object' && newVnode.type === '__markup__') {
    if (oldVnode.html !== newVnode.html) {
      const oldNodes = oldVnode._markupNodes || [];
      const anchor = oldNodes[oldNodes.length - 1]?.nextSibling ?? null;
      const host = oldNodes[0]?.parentNode ?? parent;
      for (const n of oldNodes) if (n.parentNode) n.parentNode.removeChild(n);
      const tpl = document.createElement('template');
      tpl.innerHTML = newVnode.html;
      const newNodes = Array.from(tpl.content.childNodes);
      newVnode._markupNodes = newNodes;
      for (const n of newNodes) host.insertBefore(n, anchor);
    } else {
      newVnode._markupNodes = oldVnode._markupNodes;
    }
    return newVnode;
  }

  // Element, same tag
  if (typeof oldVnode === 'object' && typeof oldVnode.type === 'string'
      && typeof newVnode === 'object' && oldVnode.type === newVnode.type) {
    patchElement(oldVnode, newVnode, owner);
    return newVnode;
  }

  // Component, same Ctor
  if (typeof oldVnode === 'object' && typeof oldVnode.type === 'function'
      && typeof newVnode === 'object' && oldVnode.type === newVnode.type) {
    patchComponent(oldVnode, newVnode, owner);
    return newVnode;
  }

  // Type mismatch → replace in place. Capture the anchor *before* removal so
  // the replacement lands at the old node's slot rather than being appended.
  const firstOldNode = firstDom(oldVnode);
  const lastOldNode = lastDom(oldVnode);
  const host = firstOldNode?.parentNode ?? parent;
  const anchor = lastOldNode?.nextSibling ?? endAnchor;
  removeFromDom(oldVnode);
  const newDom = createDom(newVnode, owner);
  if (newDom) host.insertBefore(newDom, anchor);
  return newVnode;
}

function patchElement(oldVnode, newVnode, owner) {
  const el = oldVnode._dom;
  newVnode._dom = el;
  applyProps(el, oldVnode.props, newVnode.props, owner);
  const oldChildren = oldVnode._children ?? [];
  const newChildren = normalizeChildren(newVnode.children);
  newVnode._children = newChildren;
  // Element's children exclusively live inside the element — no end anchor
  // needed; appends land at the end of the element naturally.
  patchChildren(el, oldChildren, newChildren, owner, /* endAnchor */ null);
}

function patchComponent(oldVnode, newVnode, owner) {
  const instance = oldVnode._instance;
  newVnode._instance = instance;
  // Markers carry over to the new vnode — same DOM position, same identity.
  newVnode._startMarker = oldVnode._startMarker;
  newVnode._endMarker = oldVnode._endMarker;
  const prevProps = instance.props;
  const nextProps = newVnode.props || {};
  instance.props = nextProps;
  // Only fire onPropsChanged when props actually changed — a parent
  // re-render with unchanged props should not force children into an
  // update cycle. Shallow-equals matches Blazor's `ShouldRender` default
  // and avoids the hidden-dependency of "parent rerenders → every child
  // gets a lifecycle hook". Reference-equal shortcut keeps the hot path free.
  if (prevProps !== nextProps && !shallowPropsEqual(prevProps, nextProps)) {
    instance.onPropsChanged?.();
  }

  // shouldRender gate — matches Blazor's ShouldRender(). When false we keep
  // the previous subtree DOM + vtree untouched; only props + onPropsChanged
  // have been applied, which matches how Blazor handles parameter-set
  // followed by a render-skip.
  if (instance.shouldRender?.() === false) {
    return;
  }

  const newSubtree = wrapTopLevel(instance.render?.());
  const oldSubtree = instance._vtree;
  // Parent is always available via the markers, even if the previous render
  // returned null (no firstDom to look up). Markers sit in the parent DOM
  // from first mount onwards — this is what fixes the null-host skip.
  const parent = newVnode._startMarker.parentNode;
  if (parent) {
    const oldList = asList(oldSubtree);
    const newList = asList(newSubtree);
    patchChildren(parent, oldList, newList, instance, newVnode._endMarker);
  }
  instance._vtree = newSubtree;

  // Post-render hook fires on children just like Blazor. firstRender is
  // false here — the component was already mounted on a previous pass.
  try { instance.onAfterRender?.(false); }
  catch (err) { reportRuntimeError(err, { phase: 'onAfterRender', component: newVnode.type?.name }); }
}

function patchChildren(parent, oldChildren, newChildren, owner, endAnchor) {
  const useKeys = hasAnyKey(oldChildren) || hasAnyKey(newChildren);
  if (useKeys) {
    patchKeyed(parent, oldChildren, newChildren, owner, endAnchor);
  } else {
    patchPositional(parent, oldChildren, newChildren, owner, endAnchor);
  }
}

function hasAnyKey(list) {
  for (const c of list) {
    if (c != null && typeof c === 'object' && c.key != null) return true;
  }
  return false;
}

function patchPositional(parent, oldChildren, newChildren, owner, endAnchor) {
  const common = Math.min(oldChildren.length, newChildren.length);
  for (let i = 0; i < common; i++) {
    patchNode(parent, oldChildren[i], newChildren[i], owner, endAnchor);
  }
  for (let i = common; i < oldChildren.length; i++) {
    removeFromDom(oldChildren[i]);
  }
  for (let i = common; i < newChildren.length; i++) {
    const node = createDom(newChildren[i], owner);
    if (node) parent.insertBefore(node, endAnchor);
  }
}

// Keyed diff — handles mixed keyed and unkeyed siblings uniformly.
//
// Parents often mix static elements with a keyed loop, e.g.
//   <h2>Items</h2> @foreach(...) { <Item @key="item.Id" /> } <button>Add</button>
// The `<h2>` and `<button>` have no `@key`; they live as siblings of the keyed
// items in the same children list. Blazor, React and Vue all support this by
// matching keyed children by key and matching unkeyed children positionally
// against the next unmatched-unkeyed slot.
//
// Algorithm (two passes):
//   1. Assignment: forward-walk new children, claim an old child for each:
//      - keyed new → look up in key→old-index map
//      - unkeyed new → take the next unclaimed unkeyed-old in source order
//   2. Placement: backward-walk new children to compute anchors and move
//      DOM ranges. Each placed child sets the anchor its predecessor must
//      sit before. Unclaimed old children are removed at the end.
function patchKeyed(parent, oldChildren, newChildren, owner, endAnchor) {
  const keyToOld = new Map();
  const unkeyedOldIndices = [];
  for (let i = 0; i < oldChildren.length; i++) {
    const c = oldChildren[i];
    if (c == null) continue;
    if (c.key != null) keyToOld.set(c.key, i);
    else unkeyedOldIndices.push(i);
  }
  const claimed = new Array(oldChildren.length).fill(false);

  // Pass 1 — decide each new child's claim, forward order for positional
  // unkeyed matching to be intuitive (first-unkeyed-new ↔ first-unkeyed-old).
  const assignment = new Array(newChildren.length);
  let unkeyedCursor = 0;
  for (let i = 0; i < newChildren.length; i++) {
    const newChild = newChildren[i];
    const newKey = newChild?.key;
    if (newKey != null) {
      const oldIdx = keyToOld.get(newKey);
      if (oldIdx != null && !claimed[oldIdx]) {
        claimed[oldIdx] = true;
        assignment[i] = oldIdx;
      }
    } else {
      while (unkeyedCursor < unkeyedOldIndices.length) {
        const candidate = unkeyedOldIndices[unkeyedCursor++];
        if (!claimed[candidate]) {
          claimed[candidate] = true;
          assignment[i] = candidate;
          break;
        }
      }
    }
  }

  // Pass 2 — backward walk to place DOM ranges before the running anchor.
  let anchor = endAnchor;
  for (let i = newChildren.length - 1; i >= 0; i--) {
    const newChild = newChildren[i];
    const oldIdx = assignment[i];
    if (oldIdx != null) {
      patchNode(parent, oldChildren[oldIdx], newChild, owner, anchor);
    } else {
      const node = createDom(newChild, owner);
      if (node) parent.insertBefore(node, anchor);
    }
    moveRangeBefore(parent, newChild, anchor);
    anchor = firstDom(newChild) ?? anchor;
  }

  for (let i = 0; i < oldChildren.length; i++) {
    if (!claimed[i]) removeFromDom(oldChildren[i]);
  }
}

// Move every DOM node belonging to `vnode` so they sit consecutively right
// before `anchor`, preserving their relative order.
function moveRangeBefore(parent, vnode, anchor) {
  if (isVoid(vnode)) return;
  const nodes = collectDomRoots(vnode);
  for (const node of nodes) {
    if (node && node !== anchor) parent.insertBefore(node, anchor);
  }
}

// All top-level DOM nodes this vnode occupies, in order. For components we
// walk from startMarker through endMarker inclusive so a single move-op
// transports everything — including nested markup, multi-root fragments,
// and the markers themselves.
function collectDomRoots(vnode) {
  if (isVoid(vnode)) return [];
  if (Array.isArray(vnode)) {
    const out = [];
    for (const c of vnode) {
      const sub = collectDomRoots(c);
      for (const n of sub) out.push(n);
    }
    return out;
  }
  if (typeof vnode !== 'object') return [];
  if (vnode._kind === 'text') return vnode._dom ? [vnode._dom] : [];
  if (vnode.type === '__markup__') {
    return vnode._markupNodes ? [...vnode._markupNodes] : [];
  }
  if (typeof vnode.type === 'function') {
    const start = vnode._startMarker;
    const end = vnode._endMarker;
    if (!start || !end) return [];
    const out = [];
    let cur = start;
    while (cur) {
      out.push(cur);
      if (cur === end) break;
      cur = cur.nextSibling;
    }
    return out;
  }
  return vnode._dom ? [vnode._dom] : [];
}

// --- unmount ---

function removeFromDom(vnode) {
  if (isVoid(vnode)) return;

  if (Array.isArray(vnode)) {
    for (const c of vnode) removeFromDom(c);
    return;
  }

  if (typeof vnode !== 'object') return;

  if (vnode._kind === 'text') {
    if (vnode._dom?.parentNode) vnode._dom.parentNode.removeChild(vnode._dom);
    return;
  }

  if (vnode.type === '__markup__') {
    for (const n of vnode._markupNodes ?? []) {
      if (n.parentNode) n.parentNode.removeChild(n);
    }
    return;
  }

  if (typeof vnode.type === 'function') {
    // Fire onDestroy on any nested components first (so deeper cleanup can
    // still reach the DOM), then detach the whole range at once.
    destroyTree(vnode._instance?._vtree);
    const instance = vnode._instance;
    if (instance) {
      try { instance.onDestroy?.(); }
      catch (err) { reportRuntimeError(err, { phase: 'onDestroy', component: vnode.type?.name }); }
    }
    // Remove everything from startMarker to endMarker inclusive.
    const start = vnode._startMarker;
    const end = vnode._endMarker;
    const parent = start?.parentNode;
    if (parent && end) {
      let cur = start;
      while (cur) {
        const next = cur.nextSibling;
        parent.removeChild(cur);
        if (cur === end) break;
        cur = next;
      }
    }
    return;
  }

  // Element — walk children so nested components see onDestroy without
  // double-detaching their DOM (the element's removeChild below pulls the
  // whole subtree in one go).
  for (const child of vnode._children ?? []) destroyTree(child);
  if (vnode._dom?.parentNode) vnode._dom.parentNode.removeChild(vnode._dom);
}

// Unmount a root tree: fire onDestroy on every component in the subtree
// bottom-up, then detach everything from `container`. Exposed so mount.js
// can hand control back to the caller via `instance.unmount()` without
// reaching into vdom internals.
export function destroyTreeAndDom(vtree, container) {
  destroyTree(vtree);
  if (container) container.replaceChildren();
}

// Walk a subtree without detaching DOM, calling onDestroy on every component
// we find. Used for cleanup ahead of an ancestor tear-down that will remove
// the DOM in a single pass.
//
// Ordering contract — observable to user-code: onDestroy fires BOTTOM-UP
// (deepest child first, root last) and the component's DOM is STILL LIVE
// when the hook runs. User cleanup that reads `this.props` or inspects
// live DOM position works; user cleanup that tries to rerender won't see
// anything because the parent's already scheduled its own teardown.
function destroyTree(vnode) {
  if (isVoid(vnode)) return;
  if (Array.isArray(vnode)) { for (const c of vnode) destroyTree(c); return; }
  if (typeof vnode !== 'object') return;
  if (vnode._kind === 'text') return;
  if (vnode.type === '__markup__') return;
  if (typeof vnode.type === 'function') {
    const instance = vnode._instance;
    if (instance) {
      destroyTree(instance._vtree);
      try { instance.onDestroy?.(); }
      catch (err) { reportRuntimeError(err, { phase: 'onDestroy', component: vnode.type?.name }); }
    }
    return;
  }
  for (const child of vnode._children ?? []) destroyTree(child);
}

// First real DOM node this vnode owns, used to compute the insertion anchor
// on replace/reorder. For a component we return the start marker so callers
// that index into `parent.childNodes` always hit a live node, even when the
// component's own render returned null.
function firstDom(vnode) {
  if (isVoid(vnode)) return null;
  if (Array.isArray(vnode)) {
    for (const c of vnode) {
      const d = firstDom(c);
      if (d) return d;
    }
    return null;
  }
  if (typeof vnode !== 'object') return null;
  if (vnode._kind === 'text') return vnode._dom;
  if (vnode.type === '__markup__') return vnode._markupNodes?.[0] ?? null;
  if (typeof vnode.type === 'function') return vnode._startMarker ?? null;
  return vnode._dom;
}

// Last real DOM node this vnode owns, used to compute where the next
// sibling would sit — paired with firstDom for range-aware replacements.
function lastDom(vnode) {
  if (isVoid(vnode)) return null;
  if (Array.isArray(vnode)) {
    for (let i = vnode.length - 1; i >= 0; i--) {
      const d = lastDom(vnode[i]);
      if (d) return d;
    }
    return null;
  }
  if (typeof vnode !== 'object') return null;
  if (vnode._kind === 'text') return vnode._dom;
  if (vnode.type === '__markup__') {
    const nodes = vnode._markupNodes;
    return nodes && nodes.length > 0 ? nodes[nodes.length - 1] : null;
  }
  if (typeof vnode.type === 'function') return vnode._endMarker ?? null;
  return vnode._dom;
}
