import { h, markup, Component, LayoutComponent, EventArgs, MouseEventArgs, KeyboardEventArgs, ChangeEventArgs, FocusEventArgs, ApiClient, ApiException, _isNullOrWhiteSpace, _isNullOrEmpty, _newGuid, _listRemove, PageTitle, NavLink, Router } from '@razorshave/runtime';

export class Counter extends Component {
  render() {
    return [
      h(PageTitle, { 'ChildContent': () => ["Counter"] }),
      markup("\r\n\r\n"),
      markup("<h1>Counter</h1>\r\n\r\n"),
      h("p", { 'role': "status" }, "Current count: ", this.currentCount),
      markup("\r\n\r\n"),
      h("button", { 'class': "btn btn-primary", 'onclick': (e) => this.incrementCount(new MouseEventArgs(e)) }, "Click me")
    ];
  }
  currentCount = 0;
  incrementCount() {
    this.currentCount++;
  }
}
