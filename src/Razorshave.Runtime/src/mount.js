// Application entry point: instantiate the root component, plug it into the
// container element, trigger the first render, return the live instance.
//
// The instance is handy for debugging and tests — production code usually
// just mounts and forgets.

export function mount(ComponentClass, container) {
  if (!container) {
    throw new Error('mount: container element is required');
  }
  const instance = new ComponentClass();
  instance._container = container;
  instance.onInit?.();
  instance._rerender();
  return instance;
}
