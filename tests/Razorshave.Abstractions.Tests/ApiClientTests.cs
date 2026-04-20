using System.Net;
using System.Text.Json.Serialization;

namespace Razorshave.Abstractions.Tests;

/// <summary>
/// Verifies the Blazor-Server dev-host HTTP-round-trip path of
/// <see cref="ApiClient"/>. The JS-side counterpart (api-client.js) has its
/// own tests; these prove the two paths behave identically end-to-end so
/// user code debugged with F5 matches the transpiled-SPA behaviour.
/// </summary>
public sealed class ApiClientTests
{
    // Stub HttpClient handler so tests don't touch the network.
    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBodyText { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBodyText = await request.Content.ReadAsStringAsync(ct);
            return Respond?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class TestClient : ApiClient
    {
        public Action<ApiRequest>? ConfigureHook { get; set; }
        public Action<ApiResponse>? HandleHook { get; set; }

        public TestClient(HttpClient http) : base(http) { }

        public Task<T?> GetJson<T>(string path) => Get<T>(path);
        public Task<T?> PostJson<T>(string path, object body) => Post<T>(path, body);

        protected override Task ConfigureRequestAsync(ApiRequest request)
        {
            ConfigureHook?.Invoke(request);
            return Task.CompletedTask;
        }
        protected override Task HandleResponseAsync(ApiResponse response)
        {
            HandleHook?.Invoke(response);
            return Task.CompletedTask;
        }
    }

    private sealed record Widget([property: JsonPropertyName("id")] int Id, [property: JsonPropertyName("name")] string Name);

    [Fact]
    public async Task Get_deserialises_json_response()
    {
        var handler = new StubHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":42,\"name\":\"gear\"}", System.Text.Encoding.UTF8, "application/json"),
            },
        };
        var client = new TestClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });

        var result = await client.GetJson<Widget>("widgets/42");

        Assert.NotNull(result);
        Assert.Equal(42, result!.Id);
        Assert.Equal("gear", result.Name);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task Post_serialises_body_and_deserialises_response()
    {
        var handler = new StubHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":1,\"name\":\"echo\"}"),
            },
        };
        var client = new TestClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });

        var body = new Widget(1, "echo");
        var result = await client.PostJson<Widget>("widgets", body);

        Assert.Equal("{\"id\":1,\"name\":\"echo\"}", handler.LastBodyText);
        Assert.Equal(1, result!.Id);
        Assert.Equal("application/json", handler.LastRequest!.Content!.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ConfigureRequestAsync_can_inject_headers()
    {
        var handler = new StubHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") } };
        var client = new TestClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") })
        {
            ConfigureHook = req => req.Headers["Authorization"] = "Bearer fake-token",
        };

        await client.GetJson<object>("anything");

        Assert.True(handler.LastRequest!.Headers.Contains("Authorization"));
        Assert.Equal("Bearer fake-token", handler.LastRequest.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task HandleResponseAsync_fires_on_every_response_before_throw()
    {
        var handler = new StubHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("oops"),
            },
        };
        ApiResponse? seen = null;
        var client = new TestClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") })
        {
            HandleHook = r => seen = r,
        };

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetJson<object>("x"));

        Assert.NotNull(seen);
        Assert.Equal(500, seen!.StatusCode);
        Assert.Equal("oops", seen.Body);
        Assert.Equal(500, ex.StatusCode);
        Assert.Same(seen, ex.Response);
    }

    [Fact]
    public async Task Empty_body_deserialises_to_default()
    {
        var handler = new StubHandler
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent(string.Empty),
            },
        };
        var client = new TestClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") });

        var result = await client.GetJson<Widget>("empty");
        Assert.Null(result);
    }
}
