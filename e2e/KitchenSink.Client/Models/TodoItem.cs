namespace KitchenSink.Client.Models;

public record TodoItem
{
    public string Id { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public bool Done { get; init; }
}
