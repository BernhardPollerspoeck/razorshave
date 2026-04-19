import { h, markup, Component, LayoutComponent, EventArgs, MouseEventArgs, KeyboardEventArgs, ChangeEventArgs, FocusEventArgs, PageTitle } from '@razorshave/runtime';

export class Weather extends Component {
  render() {
    return [
      h(PageTitle, { 'ChildContent': () => ["Weather"] }),
      markup("\r\n\r\n"),
      markup("<h1>Weather</h1>\r\n\r\n"),
      markup("<p>This component demonstrates showing data.</p>"),
      /* TODO: unsupported render stmt: IfStatement */
    ];
  }
  forecasts = null;
  async onInitializedAsync() {
    await Task.delay(500);
    let startDate = DateOnly.fromDateTime(DateTime.now);
    let summaries = /* TODO: ImplicitArrayCreationExpression */ null;
    this.forecasts = Enumerable.range(1, 5).select(/* TODO: SimpleLambdaExpression */ null).toArray();
  }
}
