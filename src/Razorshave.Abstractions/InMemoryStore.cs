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
    private int _batchDirty; // 0 = clean, 1 = dirty (atomic-compatible int)

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
        // Interlocked so concurrent Batch() calls don't race on the depth
        // counter — a race there would either skip or duplicate the
        // coalesced notification, breaking the "single OnChange per outer
        // batch" contract.
        var depth = Interlocked.Increment(ref _batchDepth);
        var threw = false;
        try
        {
            updates();
        }
        catch
        {
            threw = true;
            throw;
        }
        finally
        {
            var depthAfter = Interlocked.Decrement(ref _batchDepth);
            if (depthAfter == 0)
            {
                // Swap dirty flag to 0 atomically; emit only if we were the
                // ones who cleared a dirty state AND no exception propagated.
                var wasDirty = Interlocked.Exchange(ref _batchDirty, 0) == 1;
                if (wasDirty && !threw) EmitChange();
            }
            _ = depth; // suppress unused-local warning
        }
    }

    /// <inheritdoc />
    public event Action? OnChange;

    private void NotifyChange()
    {
        if (Volatile.Read(ref _batchDepth) > 0)
        {
            Interlocked.Exchange(ref _batchDirty, 1);
            return;
        }
        EmitChange();
    }

    private void EmitChange() => OnChange?.Invoke();
}
