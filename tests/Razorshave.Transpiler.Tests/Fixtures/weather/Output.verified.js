import { h, markup, Component, LayoutComponent, EventArgs, MouseEventArgs, KeyboardEventArgs, ChangeEventArgs, FocusEventArgs, ApiClient, ApiException, _isNullOrWhiteSpace, _isNullOrEmpty, _newGuid, PageTitle, NavLink, Router } from '@razorshave/runtime';

export class Weather extends Component {
  static _injects = { 'weatherApi': 'IWeatherApi' };
  render() {
    const _c = [];
    _c.push(h(PageTitle, { 'ChildContent': () => ["Weather"] }));
    _c.push(markup("\n\n"));
    _c.push(markup("<h1>Weather</h1>\n\n"));
    _c.push(markup("<p>This component demonstrates showing data.</p>"));
    if (this.forecasts == null) {
      _c.push(markup("<p><em>Loading...</em></p>"));
    } else {
      _c.push(h("table", { 'class': "table" }, markup("<thead><tr><th>Date</th>\n                <th aria-label=\"Temperature in Celsius\">Temp. (C)</th>\n                <th aria-label=\"Temperature in Fahrenheit\">Temp. (F)</th>\n                <th>Summary</th></tr></thead>\n        "), h("tbody", {}, (() => { const _c = []; for (const forecast of this.forecasts) { _c.push(h("tr", {}, h("td", {}, forecast.date.toLocaleDateString()), markup("\n                    "), h("td", {}, forecast.temperatureC), markup("\n                    "), h("td", {}, forecast.temperatureF), markup("\n                    "), h("td", {}, forecast.summary))); } return _c; })())));
    }
    return _c;
  }
  forecasts = null;
  async onInitializedAsync() {
    this.forecasts = await this.weatherApi.getForecastsAsync();
  }
}
