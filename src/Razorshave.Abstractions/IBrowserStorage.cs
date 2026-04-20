namespace Razorshave.Abstractions;

/// <summary>
/// Synchronous key-value persistence. Backed by <c>window.localStorage</c> in the transpiled
/// SPA and by an in-memory dictionary in the Blazor Server dev host.
/// </summary>
public interface ILocalStorage : IWebStorage { }

/// <summary>
/// Synchronous key-value persistence scoped to the current browser session. Backed by
/// <c>window.sessionStorage</c> in the transpiled SPA.
/// </summary>
public interface ISessionStorage : IWebStorage { }

/// <summary>
/// Common shape for <see cref="ILocalStorage"/> and <see cref="ISessionStorage"/>.
/// Values are serialised transparently, so <c>Set("user", myUser)</c> round-trips.
/// </summary>
public interface IWebStorage
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
    void Remove(string key);
    bool Has(string key);
    void Clear();
    IReadOnlyList<string> Keys();
    int Count { get; }
}

/// <summary>
/// Cookie access with a Map-like surface. <see cref="Set"/> defaults <c>path=/</c> so cookies
/// are visible across all routes of the SPA.
/// </summary>
public interface ICookieStore
{
    string? Get(string name);
    void Set(string name, string value, CookieOptions? options = null);
    void Remove(string name, CookieOptions? options = null);
    bool Has(string name);
    IReadOnlyDictionary<string, string> GetAll();
}

/// <summary>Options passed to <see cref="ICookieStore.Set"/>.</summary>
public sealed class CookieOptions
{
    public string Path { get; init; } = "/";
    public int? MaxAgeSeconds { get; init; }
    public DateTimeOffset? Expires { get; init; }
    public string? Domain { get; init; }
    public string? SameSite { get; init; }
    public bool Secure { get; init; }
}
