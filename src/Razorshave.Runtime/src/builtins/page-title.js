import { Component } from '../component.js';

// <PageTitle>Some text</PageTitle> in Razor compiles to the component with
// ChildContent as a RenderFragment. Our transpiler emits that as an arrow
// function that returns an array of strings / vnodes; we pull the text parts
// out and assign them to document.title. Nothing renders into the DOM tree.
//
// The `document.title` mutation lives in `onAfterRender` rather than
// `render()` — render must stay pure so the reconciler can freely call it
// for speculative work, SSR prepasses, or memoisation. A side-effectful
// render would fire those mutations extra times and desync user intent.

export class PageTitle extends Component {
  render() { return null; }

  onAfterRender() {
    const children = this.props?.ChildContent?.() ?? [];
    const text = flatten(children).filter(c => typeof c === 'string').join('');
    if (text && typeof document !== 'undefined') {
      document.title = text;
    }
  }
}

function flatten(arr) {
  const out = [];
  for (const c of arr) {
    if (Array.isArray(c)) out.push(...flatten(c));
    else if (c !== null && c !== undefined) out.push(c);
  }
  return out;
}
