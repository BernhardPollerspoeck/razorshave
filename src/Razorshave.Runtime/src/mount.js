import { navigationManager } from './navigation-manager.js';
import { setActiveRoot } from './component.js';

// Application entry point: instantiate the root component, plug it into the
// container element, run the Blazor-style lifecycle, wire navigation events
// to the root's rerender so nested Routers update on URL change, return the
// live instance.
//
// Also registers the instance as the module's active root so child
// components can bubble stateHasChanged() back to it — the naive diff
// re-renders the whole tree top-down and reuses child instances by class.
//
// The instance is handy for debugging and tests — production code usually
// just mounts and forgets.

export function mount(ComponentClass, container, props = {}) {
  if (!container) {
    throw new Error('mount: container element is required');
  }
  const instance = new ComponentClass();
  instance._container = container;
  instance.props = props;
  setActiveRoot(instance);
  // _lifecycleStart does: resolve [Inject]s → onInit → first render → kick
  // off onInitializedAsync (which triggers another render on completion).
  instance._lifecycleStart(ComponentClass);

  // Route changes trigger a root rerender. The naive diff rebuilds the whole
  // subtree, which is enough for nested Routers to re-evaluate their match
  // without any per-component subscription plumbing.
  instance._navUnsubscribe = navigationManager.onLocationChanged(() => {
    instance.stateHasChanged();
  });
  if (typeof window !== 'undefined') {
    instance._popHandler = () => instance.stateHasChanged();
    window.addEventListener('popstate', instance._popHandler);
  }

  return instance;
}
