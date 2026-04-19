import { Component } from '../component.js';

// <PageTitle>Some text</PageTitle> in Razor compiles to the component with
// ChildContent as a RenderFragment. Our transpiler emits that as an arrow
// function that returns an array of strings / vnodes; we pull the text parts
// out and assign them to document.title. Nothing renders into the DOM tree.

export class PageTitle extends Component {
  render() {
    const children = this.props?.ChildContent?.() ?? [];
    const text = flatten(children).filter(c => typeof c === 'string').join('');
    if (text && typeof document !== 'undefined') {
      document.title = text;
    }
    return null;
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
