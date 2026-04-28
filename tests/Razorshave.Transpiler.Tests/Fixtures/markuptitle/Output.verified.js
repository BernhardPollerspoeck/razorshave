import { h, markup, Component, LayoutComponent, EventArgs, MouseEventArgs, KeyboardEventArgs, ChangeEventArgs, FocusEventArgs, ApiClient, ApiException, _isNullOrWhiteSpace, _isNullOrEmpty, _newGuid, _listRemove, PageTitle, NavLink, Router } from '@razorshave/runtime';

export class MarkupTitle extends Component {
  render() {
    return [
      h(PageTitle, { 'ChildContent': () => [markup("Verified — Public proof. €19/month.")] }),
      markup("\n\n"),
      markup("<h1>Markup-classified title test</h1>\n\n"),
      markup("<p>This page exists to pin Razor's source-generator behaviour for static\n   title text containing non-ASCII characters (em-dash, euro sign). The\n   transpiler test fixture derived from this page locks down whether the\n   SG routes the content through <code>AddContent</code> or\n   <code>AddMarkupContent</code> — the runtime's PageTitle has to handle\n   both shapes either way.</p>")
    ];
  }
}
