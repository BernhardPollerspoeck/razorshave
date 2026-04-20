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
//   _dom            — DOM node for element / text vnodes
//   _markupNodes    — array of DOM roots for a markup vnode
//   _children       — normalised children (primitives wrapped as text vnodes)
//   _instance       — Component instance for component vnodes
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

// --- public API ---

// Create DOM for a fresh vnode tree. First mount path + one-shot helper used
// by unit tests. Subsequent updates go through `patchRoot`.
export function render(vnode, owner) {
  return createDom(wrapTopLevel(vnode), owner);
}

// Diff `oldVtree` against `newVtree` inside `container`, patching DOM in
// place. Fires onDestroy on components that leave the tree. Returns the
// normalised new tree so the caller can store it for the next patch.
export function patchRoot(container, oldVtree, newVtree, owner) {
  const oldList = asList(oldVtree);
  const newList = asList(newVtree);
  patchChildren(container, oldList, newList, owner);
  return newVtree;
}

// Mount a fresh vnode tree inside `container`, appending the resulting DOM.
// Returns the normalised tree for future patches.
export function mountRoot(container, vtree, owner) {
  const list = asList(vtree);
  for (const v of list) {
    const dom = createDom(v, owner);
    if (dom) container.appendChild(dom);
  }
  return vtree;
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
  instance._childrenArg = vnode.children;
  instance._resolveInjects?.(Ctor);
  instance.onInit?.();
  kickoffAsyncInit(instance);

  const subtree = instance.render?.();
  const wrapped = wrapTopLevel(subtree);
  instance._vtree = wrapped;
  const dom = createDom(wrapped, instance);
  return dom;
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
  try {
    const result = userFn(e);
    if (result && typeof result.then === 'function') await result;
  } catch (err) {
    reportRuntimeError(err, {
      phase: 'event handler',
      component: owner?.constructor?.name,
    });
  } finally {
    owner?.stateHasChanged?.();
  }
}

// --- patch ---

function patchNode(parent, oldVnode, newVnode, owner) {
  if (isVoid(newVnode)) {
    if (!isVoid(oldVnode)) removeFromDom(oldVnode);
    return null;
  }
  if (isVoid(oldVnode)) {
    const node = createDom(newVnode, owner);
    if (node) parent.appendChild(node);
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

  // Type mismatch → replace in place
  const firstOldNode = firstDom(oldVnode);
  const host = firstOldNode?.parentNode ?? parent;
  const anchor = firstOldNode ?? null;
  const newDom = createDom(newVnode, owner);
  if (newDom) host.insertBefore(newDom, anchor);
  removeFromDom(oldVnode);
  return newVnode;
}

function patchElement(oldVnode, newVnode, owner) {
  const el = oldVnode._dom;
  newVnode._dom = el;
  applyProps(el, oldVnode.props, newVnode.props, owner);
  const oldChildren = oldVnode._children ?? [];
  const newChildren = normalizeChildren(newVnode.children);
  newVnode._children = newChildren;
  patchChildren(el, oldChildren, newChildren, owner);
}

function patchComponent(oldVnode, newVnode, owner) {
  const instance = oldVnode._instance;
  newVnode._instance = instance;
  instance.props = newVnode.props || {};
  instance._childrenArg = newVnode.children;
  instance.onPropsChanged?.();

  const newSubtree = wrapTopLevel(instance.render?.());
  const oldSubtree = instance._vtree;
  const host = firstDom(oldSubtree)?.parentNode ?? null;
  if (host) {
    patchSubtree(host, oldSubtree, newSubtree, instance);
  }
  instance._vtree = newSubtree;
}

// Patches a component's own rendered output. Accepts single-vnode OR array
// (fragment) roots. Uses the same keyed/positional engine as element
// children — a fragment is conceptually just children with no wrapping
// element.
function patchSubtree(host, oldSubtree, newSubtree, owner) {
  const oldList = asList(oldSubtree);
  const newList = asList(newSubtree);
  patchChildren(host, oldList, newList, owner);
}

function patchChildren(parent, oldChildren, newChildren, owner) {
  const useKeys = hasAnyKey(oldChildren) || hasAnyKey(newChildren);
  if (useKeys) {
    patchKeyed(parent, oldChildren, newChildren, owner);
  } else {
    patchPositional(parent, oldChildren, newChildren, owner);
  }
}

function hasAnyKey(list) {
  for (const c of list) {
    if (c != null && typeof c === 'object' && c.key != null) return true;
  }
  return false;
}

function patchPositional(parent, oldChildren, newChildren, owner) {
  const common = Math.min(oldChildren.length, newChildren.length);
  for (let i = 0; i < common; i++) {
    patchNode(parent, oldChildren[i], newChildren[i], owner);
  }
  for (let i = common; i < oldChildren.length; i++) {
    removeFromDom(oldChildren[i]);
  }
  for (let i = common; i < newChildren.length; i++) {
    const node = createDom(newChildren[i], owner);
    if (node) parent.appendChild(node);
  }
}

// Keyed diff. Two passes: first build a key→old-index map, then walk the
// new list. For each new child, try to find a matching old child by key —
// on hit, patch in place and move the DOM if its position changed; on miss,
// create fresh. Finally, any old children never claimed get removed.
function patchKeyed(parent, oldChildren, newChildren, owner) {
  const keyToOld = new Map();
  for (let i = 0; i < oldChildren.length; i++) {
    const k = oldChildren[i]?.key;
    if (k != null) keyToOld.set(k, i);
  }
  const claimed = new Array(oldChildren.length).fill(false);

  // anchor tracks where the next DOM node should sit. We use `parent.childNodes`
  // dynamically so reorders inside this pass pick up the latest state.
  let cursor = 0;
  for (let i = 0; i < newChildren.length; i++) {
    const newChild = newChildren[i];
    const newKey = newChild?.key;
    const oldIdx = newKey != null ? keyToOld.get(newKey) : undefined;

    if (oldIdx != null && !claimed[oldIdx]) {
      claimed[oldIdx] = true;
      const oldChild = oldChildren[oldIdx];
      patchNode(parent, oldChild, newChild, owner);

      // Move into position if the matched node isn't already there. We use
      // the resulting DOM node of the patched vnode (not the old one, which
      // may have been torn down on a type mismatch).
      const dom = firstDom(newChild);
      const ref = parent.childNodes[cursor] ?? null;
      if (dom && dom !== ref) {
        parent.insertBefore(dom, ref);
      }
    } else {
      const node = createDom(newChild, owner);
      if (node) {
        const ref = parent.childNodes[cursor] ?? null;
        parent.insertBefore(node, ref);
      }
    }
    cursor += countDomRoots(newChild);
  }

  for (let i = 0; i < oldChildren.length; i++) {
    if (!claimed[i]) removeFromDom(oldChildren[i]);
  }
}

// How many top-level DOM nodes this vnode contributes. Markup can return
// several, components may return a fragment — the keyed cursor needs the
// right number to stay aligned.
function countDomRoots(vnode) {
  if (isVoid(vnode)) return 0;
  if (typeof vnode !== 'object') return 1;
  if (vnode._kind === 'text') return 1;
  if (vnode.type === '__markup__') return vnode._markupNodes?.length ?? 0;
  if (typeof vnode.type === 'function') {
    const sub = vnode._instance?._vtree;
    return countSubRoots(sub);
  }
  return 1;
}

function countSubRoots(sub) {
  if (isVoid(sub)) return 0;
  if (Array.isArray(sub)) {
    let n = 0;
    for (const s of sub) n += countDomRoots(s);
    return n;
  }
  return countDomRoots(sub);
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
    const instance = vnode._instance;
    if (instance) {
      // Remove the component's own subtree (detaches DOM) so nested
      // components see their DOM pulled. Then fire onDestroy on this
      // instance itself.
      removeFromDom(instance._vtree);
      try { instance.onDestroy?.(); }
      catch (err) { reportRuntimeError(err, { phase: 'onDestroy', component: vnode.type?.name }); }
    }
    return;
  }

  // Element — walk children so any nested components see onDestroy without
  // double-detaching their DOM (the element's removeChild below pulls the
  // whole subtree in one go).
  for (const child of vnode._children ?? []) destroyTree(child);
  if (vnode._dom?.parentNode) vnode._dom.parentNode.removeChild(vnode._dom);
}

// Walk a subtree without detaching DOM, calling onDestroy on every component
// we find. Used for the inside of a component about to be removed — its own
// root will be cleared by the caller (removeFromDom on the component vnode
// or by detachment of an ancestor element).
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
// on replace/reorder.
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
  if (typeof vnode.type === 'function') return firstDom(vnode._instance?._vtree);
  return vnode._dom;
}
