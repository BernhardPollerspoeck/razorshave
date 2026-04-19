namespace KitchenSink.Client;

/// <summary>
/// Weather-forecast API surface the <c>Weather</c> page consumes via
/// <c>@inject IWeatherApi</c>. Implemented by a Razorshave <c>ApiClient</c>
/// subclass (<see cref="WeatherApi"/>) so the same code path runs in Blazor
/// Server dev and in a transpiled SPA.
/// </summary>
public interface IWeatherApi
{
    Task<WeatherForecast[]> GetForecastsAsync();
}
