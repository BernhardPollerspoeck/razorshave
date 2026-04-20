using System.Collections.Concurrent;

namespace Razorshave.Abstractions;

/// <summary>
/// Default in-memory implementation of <see cref="IStore{T}"/> used by the Blazor Server dev
/// host. In the transpiled SPA this type is never instantiated — the JS runtime's Store takes
/// its place. Behaviour is deliberately mirrored so user code behaves identically in both
/// contexts (no surprises when toggling between <c>dotnet run</c> and <c>dotnet build -c Razorshave</c>).
/// </summary>
public sealed class InMemoryStore<T> : IStore<T>
{
    private readonly ConcurrentDictionary<string, T> _data = new();
    private int _batchDepth;
    private bool _batchDirty;

    /// <inheritdoc />
    public T? Get(string key) => _data.TryGetValue(key, out var value) ? value : default;

    /// <inheritdoc />
    public void Set(string key, T value)
    {
        _data[key] = value;
        NotifyChange();
    }

    /// <inheritdoc />
    public void Delete(string key)
    {
        if (_data.TryRemove(key, out _)) NotifyChange();
    }

    /// <inheritdoc />
    public bool Has(string key) => _data.ContainsKey(key);

    /// <inheritdoc />
    public IReadOnlyList<T> GetAll() => _data.Values.ToArray();

    /// <inheritdoc />
    public IEnumerable<T> Where(Func<T, bool> predicate) => _data.Values.Where(predicate);

    /// <inheritdoc />
    public void Clear()
    {
        if (_data.IsEmpty) return;
        _data.Clear();
        NotifyChange();
    }

    /// <inheritdoc />
    public int Count => _data.Count;

    /// <inheritdoc />
    public void Batch(Action updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        _batchDepth++;
        try
        {
            updates();
        }
        finally
        {
            _batchDepth--;
            if (_batchDepth == 0 && _batchDirty)
            {
                _batchDirty = false;
                EmitChange();
            }
        }
    }

    /// <inheritdoc />
    public event Action? OnChange;

    private void NotifyChange()
    {
        if (_batchDepth > 0) { _batchDirty = true; return; }
        EmitChange();
    }

    private void EmitChange() => OnChange?.Invoke();
}
