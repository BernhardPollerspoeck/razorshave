import { Component } from '../component.js';
import { reportRuntimeError } from '../errors.js';

// <PageTitle>Some text</PageTitle> in Razor compiles to the component with
// ChildContent as a RenderFragment. Our transpiler emits that as an arrow
// function returning the rendered children — strings, markup() vnodes, or
// element vnodes — which we walk to recover plain text and assign to
// `document.title`. Nothing renders into the DOM tree.
//
// The `document.title` mutation lives in `onAfterRender` rather than
// `render()` — render must stay pure so the reconciler can freely call it
// for speculative work, SSR prepasses, or memoisation. A side-effectful
// render would fire those mutations extra times and desync user intent.

export class PageTitle extends Component {
  render() { return null; }

  onAfterRender() {
    let children;
    try {
      children = this.props?.ChildContent?.() ?? [];
    } catch (err) {
      // User-supplied RenderFragment threw. Without this guard the exception
      // would propagate up through the component render pipeline and kill
      // the whole root's rerender. Logged via reportRuntimeError so the
      // handler hierarchy stays consistent with other runtime-surfaced
      // errors; document.title stays on whatever value it held before.
      reportRuntimeError(err, { phase: 'RenderFragment', component: 'PageTitle' });
      return;
    }
    if (typeof document === 'undefined') return;
    const text = extractTitleText(children);
    if (text) document.title = text;
  }
}

// Recovers the text representation of a RenderFragment's output. The shapes
// we have to handle:
//
//   * plain strings        — `<PageTitle>Counter</PageTitle>` → `["Counter"]`
//   * numbers              — `<PageTitle>@count</PageTitle>` may render numerics
//   * markup() vnodes      — Razor's source generator routes static text
//                            through `AddMarkupContent` whenever it classifies
//                            the content as raw markup (HTML entities, inline
//                            tags, certain mixed-content shapes); the
//                            transpiler passes that through as `markup(html)`
//   * element vnodes       — `<PageTitle><span>X</span></PageTitle>` is rare
//                            but valid, so we recurse into children
//   * arrays / nullish     — flatten / skip
//
// For markup vnodes we parse the HTML in a `<template>` and read textContent.
// Two reasons:
//   1. `document.title = "<b>X</b>"` stores the literal tag string — the
//      property setter doesn't strip markup. The browser only strips when
//      reading the value back out of a real `<title>` element. To match
//      Blazor's HeadOutlet semantics we have to do the strip ourselves.
//   2. `<template>` parses HTML into an inert DocumentFragment — `<img src>`
//      / `<script>` inside don't trigger network or execution, which keeps
//      this safe even for adversarial markup payloads.
function extractTitleText(node) {
  if (node === null || node === undefined || node === false || node === true) return '';
  if (typeof node === 'string') return node;
  if (typeof node === 'number') return String(node);
  if (Array.isArray(node)) {
    let out = '';
    for (const c of node) out += extractTitleText(c);
    return out;
  }
  if (node.type === '__markup__') {
    const tmp = document.createElement('template');
    tmp.innerHTML = node.html;
    return tmp.content.textContent ?? '';
  }
  if (typeof node.type === 'string') return extractTitleText(node.children ?? []);
  // Component vnodes inside <PageTitle> would need a full render pass to
  // stringify; that's a render-engine coupling we deliberately avoid here.
  // In practice nobody nests components inside PageTitle — and Blazor's own
  // HeadOutlet collapses them to text via the same renderer pipeline, which
  // we don't have available in `onAfterRender`.
  return '';
}
