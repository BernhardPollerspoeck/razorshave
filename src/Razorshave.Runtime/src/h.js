// Hyperscript factory: builds plain-object VDOM nodes.
//
// Shape: { type, props, children }
//   type     — string (HTML tag), component class, or '__markup__'
//   props    — object, default {}
//   children — array, flattened one level deep so RenderFragment expansions
//              (which return arrays) splice cleanly into the parent's child list

export function h(type, props, ...children) {
  // `key` is reconciler metadata (Razor's `@key`) — hoisted to the vnode top
  // level so keyed-list matching doesn't have to dig into props on every
  // diff step. Pulled out of props here so it doesn't bleed into HTML
  // attributes either.
  const key = props?.key ?? null;
  const cleanProps = props ? stripKey(props) : {};
  return {
    type,
    props: cleanProps,
    children: flattenChildren(children),
    key,
  };
}

function stripKey(props) {
  if (!('key' in props)) return props;
  const { key: _, ...rest } = props;
  return rest;
}

// Wraps a raw HTML string so the renderer can insert it via a <template> clone
// rather than treating it as plain text (which would escape the tags).
export function markup(html) {
  return { type: '__markup__', html };
}

// Iterative flattener — a stack-of-arrays walk rather than recursion so
// pathologically deep nested arrays can't blow the call stack. User code
// rarely produces such trees, but Razor codegen around RenderFragments
// can layer several levels and we'd rather not depend on "rare".
function flattenChildren(children) {
  const out = [];
  const stack = [{ arr: children, i: 0 }];
  while (stack.length > 0) {
    const frame = stack[stack.length - 1];
    if (frame.i >= frame.arr.length) { stack.pop(); continue; }
    const c = frame.arr[frame.i++];
    if (c === null || c === undefined || c === false || c === true) continue;
    if (Array.isArray(c)) {
      stack.push({ arr: c, i: 0 });
    } else {
      out.push(c);
    }
  }
  return out;
}
