using System.Globalization;
using System.Text.Json.Serialization;

using Razorshave.Abstractions;

namespace KitchenSink.Client;

/// <summary>
/// Fetches a five-day max-temperature forecast from open-meteo.com (no key,
/// CORS-open, keeps the M0 demo self-contained).
/// </summary>
/// <remarks>
/// Marked <see cref="ClientAttribute"/> so Razorshave registers this type in
/// the transpiled SPA's DI container. The implementation reads once, maps the
/// provider's shape to <see cref="WeatherForecast"/>, and falls back to a
/// rudimentary summary string because open-meteo returns numeric temperatures
/// only.
/// </remarks>
[Client]
public sealed class WeatherApi(HttpClient http) : ApiClient(http), IWeatherApi
{
    public async Task<WeatherForecast[]> GetForecastsAsync()
    {
        var response = await Get<OpenMeteoResponse>(
            "https://api.open-meteo.com/v1/forecast?latitude=48.3&longitude=14.3&daily=temperature_2m_max&timezone=Europe%2FBerlin&forecast_days=5");
        if (response == null) return [];

        var forecasts = new WeatherForecast[response.Daily.Time.Length];
        for (var i = 0; i < forecasts.Length; i++)
        {
            forecasts[i] = new WeatherForecast
            {
                Date = DateOnly.Parse(response.Daily.Time[i], CultureInfo.InvariantCulture),
                TemperatureC = (int)response.Daily.Temperature2mMax[i],
                Summary = SummaryFor(response.Daily.Temperature2mMax[i]),
            };
        }
        return forecasts;
    }

    private static string SummaryFor(double tempC) => tempC switch
    {
        < 0  => "Freezing",
        < 10 => "Chilly",
        < 18 => "Cool",
        < 25 => "Mild",
        < 30 => "Warm",
        _    => "Hot",
    };

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("daily")]
        public DailyData Daily { get; set; } = new();
    }

    private sealed class DailyData
    {
        [JsonPropertyName("time")]
        public string[] Time { get; set; } = [];

        [JsonPropertyName("temperature_2m_max")]
        public double[] Temperature2mMax { get; set; } = [];
    }
}
