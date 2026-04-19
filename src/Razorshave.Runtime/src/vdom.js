import { kickoffAsyncInit } from './component.js';

// Naive vnode → DOM renderer. Each call produces a fresh tree; re-rendering
// on state change replaces the whole subtree via the Component base class.
// Keyed DOM diffing is intentionally deferred — correctness first, perf later.
//
// Child-component instance reuse: when a parent re-renders, its previous
// child-component instances are kept around in parent._childInstances (indexed
// by Ctor). renderComponentTree swaps that into _prevChildrenForThisRender
// before calling render(), so any h(Ctor, …) reached inside the tree resolves
// to the reused instance. State on those child components survives.
//
// owner is the Component whose render() produced the vnode. Event handlers
// escape back to it via stateHasChanged() once their handler returns.

export function render(vnode, owner) {
  if (vnode === null || vnode === undefined || vnode === false || vnode === true) {
    return null;
  }

  if (Array.isArray(vnode)) {
    const frag = document.createDocumentFragment();
    for (const v of vnode) {
      const node = render(v, owner);
      if (node) frag.appendChild(node);
    }
    return frag;
  }

  if (typeof vnode === 'string' || typeof vnode === 'number') {
    return document.createTextNode(String(vnode));
  }

  if (vnode.type === '__markup__') {
    // <template>.content gives us a DocumentFragment with real DOM (not just
    // a text node), so `<h1>` inside AddMarkupContent renders as an element.
    const tpl = document.createElement('template');
    tpl.innerHTML = vnode.html;
    return tpl.content;
  }

  if (typeof vnode.type === 'function') {
    const instance = resolveChildInstance(vnode.type, vnode.props, vnode.children, owner);
    return renderComponentTree(instance);
  }

  // Ordinary HTML element
  const el = document.createElement(vnode.type);
  applyProps(el, vnode.props, owner);
  for (const child of vnode.children) {
    const node = render(child, owner);
    if (node) el.appendChild(node);
  }
  return el;
}

// Entry for rendering a Component instance's own tree: swaps in a fresh
// _childInstances map (keeping the previous one around under
// _prevChildrenForThisRender so resolveChildInstance can reuse instances),
// then calls render() on the instance and produces its DOM.
export function renderComponentTree(instance) {
  instance._prevChildrenForThisRender = instance._childInstances;
  instance._childInstances = new Map();
  const tree = instance.render?.();
  return render(tree, instance);
}

// Look up <Ctor> among the parent's previous-render children; reuse if
// present (updating props, firing onPropsChanged), otherwise create fresh and
// kick off its full lifecycle (resolve injects, onInit, async-init).
function resolveChildInstance(Ctor, props, children, owner) {
  const prevMap = owner?._prevChildrenForThisRender;
  const reusable = prevMap?.get(Ctor);

  let instance;
  if (reusable) {
    // Consume so a second h(Ctor, …) in the same render creates a new one
    // instead of colliding with the reused instance.
    prevMap.delete(Ctor);
    instance = reusable;
    instance.props = props || {};
    instance._childrenArg = children;
    instance.onPropsChanged?.();
  } else {
    instance = new Ctor();
    instance.props = props || {};
    instance._childrenArg = children;
    instance._resolveInjects?.(Ctor);
    instance.onInit?.();
    // Child component's async init fires on first instantiation only. When
    // the promise resolves we bubble stateHasChanged up so any state the
    // hook set lands on screen.
    kickoffAsyncInit(instance);
  }

  // Record on the parent so the NEXT render can find this instance.
  if (owner?._childInstances) {
    owner._childInstances.set(Ctor, instance);
  }
  return instance;
}

function applyProps(el, props, owner) {
  for (const key of Object.keys(props)) {
    const value = props[key];
    if (key.startsWith('on') && typeof value === 'function') {
      const eventName = key.slice(2).toLowerCase();
      el.addEventListener(eventName, wrapHandler(value, owner));
    } else if (value === null || value === undefined || value === false) {
      // skip
    } else if (value === true) {
      el.setAttribute(key, '');
    } else {
      el.setAttribute(key, String(value));
    }
  }
}

// Runs the user's event handler and then notifies the owning component so the
// UI refreshes. Async handlers are awaited so a Task-returning C# method
// finishes before the re-render, matching Blazor's behaviour.
function wrapHandler(userHandler, owner) {
  return async function (nativeEvent) {
    try {
      const result = userHandler(nativeEvent);
      if (result && typeof result.then === 'function') {
        await result;
      }
    } finally {
      owner?.stateHasChanged?.();
    }
  };
}
