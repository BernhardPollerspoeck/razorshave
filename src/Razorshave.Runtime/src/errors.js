// Central runtime error reporting. Before this existed, exceptions inside
// onInitializedAsync were swallowed as unhandled promise rejections and
// exceptions in event handlers surfaced only as a console trace inside
// wrapHandler's async chain — both made post-mortem impossible because the
// component context was gone by the time the error was visible.
//
// Apps can install their own handler (e.g. to forward to an ErrorBoundary or
// a telemetry sink); the default logs to console.error with enough context
// to point at the component + phase that blew up.

let _handler = (err, context) => {
  const where = context?.phase ? ` in ${context.phase}` : '';
  const comp = context?.component ? ` (${context.component})` : '';
  console.error(`[razorshave] Unhandled error${where}${comp}:`, err);
};

export function reportRuntimeError(err, context) {
  try {
    _handler(err, context);
  } catch {
    // A broken handler must never crash the runtime. If the user's replacement
    // throws, drop the secondary error on the floor — the primary one is what
    // matters and we already lost it.
  }
}

export function setErrorHandler(fn) {
  if (typeof fn === 'function') _handler = fn;
}
