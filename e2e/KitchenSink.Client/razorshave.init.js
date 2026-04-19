// Client-side service registrations for Razorshave's DI container.
//
// For M0 the transpiler only handles Razor component classes; user-authored
// ApiClient subclasses like WeatherApi.cs aren't transpiled yet. Until the
// [Client]-class auto-emit lands (v0.1 scope), we register a JS stand-in
// here — shape-compatible with the Weather.razor component's expectations.
//
// Razorshave's CLI detects this file in the project root and imports it at
// the top of the generated dist/main.js, so the container is primed before
// mount() runs and Weather's `[Inject] IWeatherApi` resolves cleanly.

import { container } from '@razorshave/runtime';

// Real open-meteo call — no API key, CORS-open, matches the C# WeatherApi's
// shape. Returns plain objects whose field names match what the transpiled
// Weather.razor accesses (camelCase: `date`, `temperatureC`, etc.).
class OpenMeteoWeatherApi {
  async getForecastsAsync() {
    const url = 'https://api.open-meteo.com/v1/forecast'
              + '?latitude=48.3&longitude=14.3'
              + '&daily=temperature_2m_max'
              + '&timezone=Europe%2FBerlin'
              + '&forecast_days=5';
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`open-meteo returned HTTP ${response.status}`);
    }
    const data = await response.json();
    const forecasts = [];
    for (let i = 0; i < data.daily.time.length; i++) {
      const dateStr = data.daily.time[i];
      const tempC = Math.round(data.daily.temperature_2m_max[i]);
      forecasts.push({
        date: { toShortDateString: () => dateStr },
        temperatureC: tempC,
        temperatureF: 32 + Math.round(tempC * 9 / 5),
        summary: summaryFor(tempC),
      });
    }
    return forecasts;
  }
}

function summaryFor(tempC) {
  if (tempC < 0)  return 'Freezing';
  if (tempC < 10) return 'Chilly';
  if (tempC < 18) return 'Cool';
  if (tempC < 25) return 'Mild';
  if (tempC < 30) return 'Warm';
  return 'Hot';
}

container.register('IWeatherApi', () => new OpenMeteoWeatherApi());
