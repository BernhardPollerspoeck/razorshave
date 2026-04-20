// Central runtime error reporting. Before this existed, exceptions inside
// onInitializedAsync were swallowed as unhandled promise rejections and
// exceptions in event handlers surfaced only as a console trace inside
// wrapHandler's async chain — both made post-mortem impossible because the
// component context was gone by the time the error was visible.
//
// Apps can install their own handler (e.g. to forward to an ErrorBoundary or
// a telemetry sink); the default logs to console.error with enough context
// to point at the component + phase that blew up.

const DEFAULT_HANDLER = (err, context) => {
  const where = context?.phase ? ` in ${context.phase}` : '';
  const comp = context?.component ? ` (${context.component})` : '';
  console.error(`[razorshave] Unhandled error${where}${comp}:`, err);
};

// Stack-based handler installation: `pushErrorHandler` returns a dispose
// function that pops precisely the handler it installed, even if another
// handler was pushed on top in the meantime. This lets tests capture
// errors without leaking state to the next test, and lets apps compose
// (e.g. "wrap outer default with telemetry" + "temporary overlay for a
// specific subtree") without coordinating manually.
const _handlerStack = [DEFAULT_HANDLER];

export function reportRuntimeError(err, context) {
  const current = _handlerStack[_handlerStack.length - 1];
  try {
    current(err, context);
  } catch {
    // A broken handler must never crash the runtime. If the user's replacement
    // throws, drop the secondary error on the floor — the primary one is what
    // matters and we already lost it.
  }
}

// Push a handler onto the stack; returns a dispose function. The dispose
// is safe-idempotent (calling it twice is a no-op) and removes THIS
// handler specifically even if something pushed later.
export function pushErrorHandler(fn) {
  if (typeof fn !== 'function') return () => {};
  const entry = fn;
  _handlerStack.push(entry);
  let popped = false;
  return () => {
    if (popped) return;
    popped = true;
    const idx = _handlerStack.lastIndexOf(entry);
    if (idx > 0) _handlerStack.splice(idx, 1); // never remove DEFAULT_HANDLER at 0
  };
}

// Back-compat shim. Behaves like the old single-handler setter — pushes a
// fresh entry and drops any previously-pushed handlers so the new fn
// becomes the sole user-installed handler. Prefer `pushErrorHandler` in
// new code because it's composable.
export function setErrorHandler(fn) {
  _handlerStack.length = 1; // keep DEFAULT_HANDLER
  if (typeof fn === 'function') _handlerStack.push(fn);
}
