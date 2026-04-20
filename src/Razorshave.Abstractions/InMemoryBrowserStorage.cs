using System.Collections.Concurrent;
using System.Text.Json;

namespace Razorshave.Abstractions;

/// <summary>
/// Default dev-host implementation of <see cref="IWebStorage"/>. Values are JSON-serialised so
/// behaviour matches the JS runtime (which also JSON-serialises into <c>localStorage</c>).
/// </summary>
public class InMemoryWebStorage : IWebStorage
{
    private readonly ConcurrentDictionary<string, string> _data = new();

    /// <inheritdoc />
    public T? Get<T>(string key)
        => _data.TryGetValue(key, out var raw) ? JsonSerializer.Deserialize<T>(raw) : default;

    /// <inheritdoc />
    public void Set<T>(string key, T value)
        => _data[key] = JsonSerializer.Serialize(value);

    /// <inheritdoc />
    public void Remove(string key) => _data.TryRemove(key, out _);

    /// <inheritdoc />
    public bool Has(string key) => _data.ContainsKey(key);

    /// <inheritdoc />
    public void Clear() => _data.Clear();

    /// <inheritdoc />
    public IReadOnlyList<string> Keys() => _data.Keys.ToArray();

    /// <inheritdoc />
    public int Count => _data.Count;
}

/// <inheritdoc cref="ILocalStorage" />
public sealed class InMemoryLocalStorage : InMemoryWebStorage, ILocalStorage { }

/// <inheritdoc cref="ISessionStorage" />
public sealed class InMemorySessionStorage : InMemoryWebStorage, ISessionStorage { }

/// <inheritdoc cref="ICookieStore" />
public sealed class InMemoryCookieStore : ICookieStore
{
    private readonly ConcurrentDictionary<string, string> _data = new();

    /// <inheritdoc />
    public string? Get(string name) => _data.TryGetValue(name, out var v) ? v : null;

    /// <inheritdoc />
    public void Set(string name, string value, CookieOptions? options = null)
    {
        if (options?.MaxAgeSeconds == 0)
        {
            _data.TryRemove(name, out _);
            return;
        }
        _data[name] = value;
    }

    /// <inheritdoc />
    public void Remove(string name, CookieOptions? options = null) => _data.TryRemove(name, out _);

    /// <inheritdoc />
    public bool Has(string name) => _data.ContainsKey(name);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll() => _data.ToArray().ToDictionary(p => p.Key, p => p.Value);
}
