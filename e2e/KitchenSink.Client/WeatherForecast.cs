namespace KitchenSink.Client;

/// <summary>
/// DTO surfaced by <see cref="IWeatherApi"/>. Lives at the top level so both
/// the Razor component and the API client can reference it — the template
/// originally nested this in <c>Weather.razor</c>'s <c>@code</c> block.
/// </summary>
public sealed class WeatherForecast
{
    public DateOnly Date { get; set; }
    public int TemperatureC { get; set; }
    public string? Summary { get; set; }

    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
