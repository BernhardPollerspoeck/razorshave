// @vitest-environment jsdom
//
// End-to-end validation for Weather.razor after steps 5.11 Phases 1-4:
//   - Weather.verified.js is the snapshot the transpiler currently produces,
//   - the runtime resolves its `_injects` manifest against a stubbed
//     IWeatherApi we register in the container,
//   - onInitializedAsync awaits the stub and the runtime's lifecycle hook
//     calls stateHasChanged so the table re-renders with data.
//
// If this test goes green, the entire Razor component → transpiled JS →
// runtime → mocked DI → rendered DOM chain is wired up correctly.

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { mount, container } from '../index.js';
import { Weather } from '../../../../tests/Razorshave.Transpiler.Tests/Fixtures/weather/Output.verified.js';

// Five JS objects matching the shape the transpiled Weather accesses:
// forecast.date.toLocaleDateString(), forecast.temperatureC, .temperatureF, .summary.
// (The transpiler maps C#'s `DateOnly.ToShortDateString()` to JS's
// `toLocaleDateString()` via StaticMemberRewrites.)
function makeForecast(dateText, c, f, summary) {
  return {
    date: { toLocaleDateString: () => dateText },
    temperatureC: c,
    temperatureF: f,
    summary,
  };
}

const FAKE_FORECASTS = [
  makeForecast('19.04.2026', 18, 64, 'Mild'),
  makeForecast('20.04.2026', 19, 66, 'Mild'),
  makeForecast('21.04.2026', 22, 71, 'Warm'),
  makeForecast('22.04.2026',  8, 46, 'Chilly'),
  makeForecast('23.04.2026', 12, 53, 'Cool'),
];

class FakeWeatherApi {
  constructor(forecasts) { this._forecasts = forecasts; }
  async getForecastsAsync() { return this._forecasts; }
}

async function flushAsync() {
  // Microtask queue drains awaited promises; a following rAF lets the
  // rerender scheduled by stateHasChanged actually run.
  await Promise.resolve();
  await Promise.resolve();
  await new Promise(r => requestAnimationFrame(r));
}

describe('Weather snapshot + runtime', () => {
  beforeEach(() => {
    container.clear();
    container.register('IWeatherApi', () => new FakeWeatherApi(FAKE_FORECASTS));
  });
  afterEach(() => container.clear());

  it('renders the loading state before onInitializedAsync resolves', () => {
    const root = document.createElement('div');
    mount(Weather, root);

    expect(root.textContent).toMatch(/Loading/i);
    expect(root.querySelector('table')).toBeNull();
  });

  it('replaces loading with the forecast table after data loads', async () => {
    const root = document.createElement('div');
    mount(Weather, root);
    await flushAsync();

    const rows = root.querySelectorAll('tbody tr');
    expect(rows.length).toBe(5);

    const firstRowCells = rows[0].querySelectorAll('td');
    expect(firstRowCells[0].textContent).toBe('19.04.2026');
    expect(firstRowCells[1].textContent).toBe('18');
    expect(firstRowCells[2].textContent).toBe('64');
    expect(firstRowCells[3].textContent).toBe('Mild');
  });

  it('resolves IWeatherApi from the DI container', async () => {
    const root = document.createElement('div');
    const instance = mount(Weather, root);
    await flushAsync();

    // The transpiler emits WeatherApi (PascalCase) as `weatherApi` (camelCase)
    // via ClassEmitter's _injects manifest.
    expect(instance.weatherApi).toBeInstanceOf(FakeWeatherApi);
    expect(instance.forecasts).toEqual(FAKE_FORECASTS);
  });

  it('sets document.title via PageTitle child component', () => {
    const root = document.createElement('div');
    mount(Weather, root);
    expect(document.title).toBe('Weather');
  });
});
