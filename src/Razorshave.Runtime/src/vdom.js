// Naive vnode → DOM renderer. Each call produces a fresh tree; re-rendering
// on state change replaces the whole subtree via the Component base class.
// Keyed diffing is intentionally deferred — correctness first, perf later.
//
// owner is the Component whose render() produced the vnode. It gets notified
// (stateHasChanged) after every event-handler invocation, giving us Blazor-
// style auto-rerender without the user having to call it manually.

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
    // Component instantiation — class ref in the type slot. We pass down the
    // props, call optional onInit, then render its VDOM like any other node.
    // The component instance itself becomes the owner of event handlers
    // inside its subtree, so interactions inside it trigger only its rerender.
    const Ctor = vnode.type;
    const instance = new Ctor();
    instance.props = vnode.props || {};
    instance._childrenArg = vnode.children;
    instance.onInit?.();
    const sub = instance.render?.();
    return render(sub, instance);
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
