namespace Razorshave.Abstractions;

/// <summary>
/// Thrown when an <see cref="ApiClient"/> request returns a non-success
/// status code. Mirrors the JS-side <c>ApiException</c> so the same user
/// code handles errors identically in Blazor-Server dev and the transpiled
/// SPA.
/// </summary>
/// <remarks>
/// The full <see cref="ApiResponse"/> (status, headers, body) is attached
/// for inspection. Use <see cref="StatusCode"/> for the common "was it a
/// 4xx / 5xx" check; read <see cref="Response"/>.Body for server-provided
/// error details.
/// </remarks>
public sealed class ApiException : Exception
{
    public ApiException(ApiResponse response)
        : base($"Razorshave ApiClient: HTTP {response.StatusCode}")
    {
        Response = response;
    }

    public ApiResponse Response { get; }

    public int StatusCode => Response.StatusCode;
}
