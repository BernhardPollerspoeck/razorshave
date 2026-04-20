import { navigationManager } from './navigation-manager.js';
import { pushActiveRoot, popActiveRoot } from './component.js';
import { destroyTreeAndDom } from './vdom.js';

// Application entry point: instantiate the root component, plug it into the
// container element, run the Blazor-style lifecycle, wire navigation events
// to the root's rerender so nested Routers update on URL change, return the
// live instance.
//
// The returned instance has an `.unmount()` method that cleanly tears the
// root down: unsubscribes from navigation, fires `onDestroy` on every
// component in the subtree, clears the container, and removes the instance
// from the active-root stack.

export function mount(ComponentClass, container, props = {}) {
  if (!container) {
    throw new Error('mount: container element is required');
  }
  const instance = new ComponentClass();
  instance._container = container;
  instance.props = props;
  pushActiveRoot(instance);
  // _lifecycleStart does: resolve [Inject]s → onInit → first render → kick
  // off onInitializedAsync (which triggers another render on completion).
  instance._lifecycleStart(ComponentClass);

  // Route changes trigger a root rerender. Uses `NavigationManager` alone —
  // `popstate` is wired there too, so one subscription covers programmatic
  // navigation AND browser back/forward.
  instance._navUnsubscribe = navigationManager.onLocationChanged(() => {
    instance.stateHasChanged();
  });

  // Returned as a method for ergonomic `mount(App, el).unmount()` patterns.
  // Safe to call multiple times — subsequent calls are no-ops.
  let disposed = false;
  instance.unmount = function unmount() {
    if (disposed) return;
    disposed = true;
    // `_destroyed` is checked by kickoffAsyncInit so a late-resolving
    // onInitializedAsync doesn't schedule a render on a torn-down instance.
    // Must be set BEFORE destroyTreeAndDom in case the tree-walk triggers
    // anything that checks it.
    instance._destroyed = true;
    instance._navUnsubscribe?.();
    instance._navUnsubscribe = null;
    // Fire onDestroy bottom-up for every component in the subtree, then
    // detach the DOM in one pass.
    destroyTreeAndDom(instance._vtree, container);
    instance._vtree = null;
    try { instance.onDestroy?.(); }
    catch { /* user hook shouldn't block unmount cleanup */ }
    // Remove self from the active-root stack so a later mount's
    // stateHasChanged doesn't bubble to a disposed instance.
    popActiveRoot(instance);
  };

  return instance;
}
