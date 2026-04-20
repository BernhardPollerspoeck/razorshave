using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Razorshave.Abstractions;

/// <summary>
/// Base class for typed API clients. Subclasses expose domain-specific methods
/// that call the protected HTTP verbs. Override <see cref="ConfigureRequestAsync"/>
/// / <see cref="HandleResponseAsync"/> to inject auth headers, log, or react to
/// common status codes.
/// </summary>
/// <remarks>
/// <para>
/// The Blazor-Server dev-host uses this implementation directly (via a
/// <see cref="HttpClient"/> injected by DI). In the transpiled SPA the
/// Razorshave transpiler rewrites every <c>Get/Post/Put/Delete</c> call to
/// the runtime's <c>fetch()</c>-backed JS equivalent — the two paths are
/// observationally identical for user code, keeping <c>F5</c> debugging
/// honest.
/// </para>
/// <para>
/// JSON serialisation uses <see cref="JsonSerializerOptions.Web"/> so property
/// names are camelCase on the wire — matching most HTTP APIs and the JS
/// side's <c>JSON.stringify</c> default. <c>[JsonPropertyName]</c> overrides
/// this per-property.
/// </para>
/// </remarks>
public abstract class ApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = JsonSerializerOptions.Web;

    protected ApiClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    protected HttpClient HttpClient { get; }

    /// <summary>
    /// Called before every request. Override to inject headers, rewrite the
    /// path, or swap the body (e.g. adding a correlation ID, attaching a
    /// bearer token, setting a tenant prefix).
    /// </summary>
    protected virtual Task ConfigureRequestAsync(ApiRequest request) => Task.CompletedTask;

    /// <summary>
    /// Called after every response, successful or not. Override to log,
    /// refresh auth tokens on 401, surface tracing IDs, etc.
    /// </summary>
    protected virtual Task HandleResponseAsync(ApiResponse response) => Task.CompletedTask;

    protected Task<T?> Get<T>(string path, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Get, path, body: null, cancellationToken);

    protected Task<T?> Post<T>(string path, object? body = null, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Post, path, body, cancellationToken);

    protected Task<T?> Put<T>(string path, object? body = null, CancellationToken cancellationToken = default)
        => SendAsync<T>(HttpMethod.Put, path, body, cancellationToken);

    protected async Task Delete(string path, CancellationToken cancellationToken = default)
        => await SendAsync<object>(HttpMethod.Delete, path, body: null, cancellationToken).ConfigureAwait(false);

    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var request = new ApiRequest
        {
            Method = method.Method,
            Path = path,
            Body = body,
        };
        await ConfigureRequestAsync(request).ConfigureAwait(false);

        using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), request.Path);
        foreach (var (key, value) in request.Headers)
        {
            // Content-typed headers live on the content object; everything
            // else goes on the request. TryAddWithoutValidation avoids
            // HttpClient's strict header parsing for custom values.
            if (!httpRequest.Headers.TryAddWithoutValidation(key, value)
                && !string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                // Header could not be attached as a request header; likely a
                // content-scoped header that will land on the content below.
                // Content-Type is the one exception — it's handled by
                // StringContent's constructor.
            }
        }

        if (request.Body is not null)
        {
            var json = JsonSerializer.Serialize(request.Body, SerializerOptions);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var httpResponse = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var bodyText = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var response = new ApiResponse
        {
            StatusCode = (int)httpResponse.StatusCode,
            Headers = FlattenHeaders(httpResponse.Headers, httpResponse.Content.Headers),
            Body = bodyText,
        };
        await HandleResponseAsync(response).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new ApiException(response);
        }

        if (string.IsNullOrEmpty(bodyText))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(bodyText, SerializerOptions);
    }

    private static Dictionary<string, string> FlattenHeaders(HttpHeaders headers, HttpContentHeaders contentHeaders)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            result[header.Key] = string.Join(", ", header.Value);
        }
        foreach (var header in contentHeaders)
        {
            result[header.Key] = string.Join(", ", header.Value);
        }
        return result;
    }
}

/// <summary>
/// Mutable request descriptor passed to <see cref="ApiClient.ConfigureRequestAsync"/>.
/// Override points can adjust headers, the body, or the path before the
/// request is dispatched.
/// </summary>
public sealed class ApiRequest
{
    public required string Method { get; init; }
    public required string Path { get; set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public object? Body { get; set; }
}

/// <summary>
/// Immutable response descriptor passed to <see cref="ApiClient.HandleResponseAsync"/>.
/// </summary>
public sealed class ApiResponse
{
    public required int StatusCode { get; init; }
    public required IReadOnlyDictionary<string, string> Headers { get; init; }
    public string? Body { get; init; }
}
