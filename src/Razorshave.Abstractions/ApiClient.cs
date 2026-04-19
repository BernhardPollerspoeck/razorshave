namespace Razorshave.Abstractions;

/// <summary>
/// Base class for typed API clients. Subclasses expose domain-specific methods
/// that call the protected HTTP verbs. Override <see cref="ConfigureRequestAsync"/>
/// / <see cref="HandleResponseAsync"/> to inject auth headers, log, or react to
/// common status codes.
/// </summary>
/// <remarks>
/// This is a placeholder stub for pre-M0 scaffolding. The full HTTP pipeline
/// (timeout, retry, cancellation, FormData) is implemented in the runtime when
/// the transpiler targets this type.
/// </remarks>
public abstract class ApiClient
{
    protected ApiClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    protected HttpClient HttpClient { get; }

    protected virtual Task ConfigureRequestAsync(ApiRequest request) => Task.CompletedTask;
    protected virtual Task HandleResponseAsync(ApiResponse response) => Task.CompletedTask;

    // TODO(M0): Get/Post/Put/Delete will call through HttpClient in dev and be
    // redirected to fetch() by the transpiler in production builds.
    protected Task<T> Get<T>(string path) => throw new NotImplementedException();
    protected Task<T> Post<T>(string path, object? body = null) => throw new NotImplementedException();
    protected Task<T> Put<T>(string path, object? body = null) => throw new NotImplementedException();
    protected Task Delete(string path) => throw new NotImplementedException();
}

public sealed class ApiRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public Dictionary<string, string> Headers { get; } = new();
    public object? Body { get; set; }
}

public sealed class ApiResponse
{
    public required int StatusCode { get; init; }
    public Dictionary<string, string> Headers { get; init; } = new();
    public string? Body { get; init; }
}
