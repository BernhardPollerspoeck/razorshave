namespace Razorshave.Abstractions;

/// <summary>
/// Typed key-value store with change notification. Injected as a singleton; multiple
/// <see cref="IStore{T}"/> instances may coexist, one per value type.
/// </summary>
/// <typeparam name="T">The value type held by this store.</typeparam>
public interface IStore<T>
{
    T? Get(string key);
    void Set(string key, T value);
    void Delete(string key);
    IReadOnlyList<T> GetAll();

    void Clear();
    int Count { get; }
    bool Has(string key);
    IEnumerable<T> Where(Func<T, bool> predicate);

    /// <summary>Runs multiple mutations and emits a single <see cref="OnChange"/> notification afterwards.</summary>
    void Batch(Action updates);

    /// <summary>Fires after any mutation (or once per batch when using <see cref="Batch"/>).</summary>
    event Action OnChange;
}
