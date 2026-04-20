namespace Razorshave.Abstractions.Tests;

/// <summary>
/// Parity tests for <see cref="InMemoryStore{T}"/> — the Blazor-Server
/// dev-host's backing implementation of <see cref="IStore{T}"/>. Shapes
/// must match the JS-side <c>Store</c> semantics so user code sees
/// identical behaviour in both environments.
/// </summary>
public sealed class InMemoryStoreTests
{
    [Fact]
    public void Set_Get_round_trips_a_value()
    {
        var s = new InMemoryStore<string>();
        s.Set("a", "hello");
        Assert.Equal("hello", s.Get("a"));
    }

    [Fact]
    public void Get_on_missing_key_returns_default()
    {
        var s = new InMemoryStore<int>();
        Assert.Equal(0, s.Get("ghost"));
    }

    [Fact]
    public void Has_reports_presence()
    {
        var s = new InMemoryStore<string>();
        Assert.False(s.Has("k"));
        s.Set("k", "v");
        Assert.True(s.Has("k"));
    }

    [Fact]
    public void Delete_removes_and_notifies_only_when_present()
    {
        var s = new InMemoryStore<string>();
        var notifications = 0;
        s.OnChange += () => notifications++;

        s.Delete("missing");
        Assert.Equal(0, notifications);

        s.Set("a", "x");  // +1
        Assert.Equal(1, notifications);
        s.Delete("a");    // +1
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Count_and_GetAll_reflect_contents()
    {
        var s = new InMemoryStore<int>();
        s.Set("a", 1); s.Set("b", 2); s.Set("c", 3);
        Assert.Equal(3, s.Count);
        Assert.Equal([1, 2, 3], s.GetAll().OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Where_filters_by_predicate()
    {
        var s = new InMemoryStore<int>();
        s.Set("a", 10); s.Set("b", 20); s.Set("c", 30);
        Assert.Equal([20, 30], s.Where(v => v >= 20).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Clear_on_empty_store_does_not_notify()
    {
        var s = new InMemoryStore<string>();
        var notifications = 0;
        s.OnChange += () => notifications++;

        s.Clear();
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void Clear_on_populated_store_drops_all_and_notifies_once()
    {
        var s = new InMemoryStore<int>();
        s.Set("a", 1); s.Set("b", 2);
        var notifications = 0;
        s.OnChange += () => notifications++;

        s.Clear();
        Assert.Equal(0, s.Count);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Batch_collapses_multiple_mutations_into_one_notification()
    {
        var s = new InMemoryStore<int>();
        var notifications = 0;
        s.OnChange += () => notifications++;

        s.Batch(() =>
        {
            s.Set("a", 1);
            s.Set("b", 2);
            s.Delete("a");
        });
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Batch_that_mutates_nothing_does_not_notify()
    {
        var s = new InMemoryStore<int>();
        var notifications = 0;
        s.OnChange += () => notifications++;

        s.Batch(() => { });
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void Nested_batches_flush_only_on_the_outermost_exit()
    {
        var s = new InMemoryStore<int>();
        var notifications = 0;
        s.OnChange += () => notifications++;

        s.Batch(() =>
        {
            s.Set("a", 1);
            s.Batch(() => s.Set("b", 2));
            Assert.Equal(0, notifications);
        });
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void OnChange_recursion_beyond_8_levels_throws_named_diagnostic()
    {
        // Silent stack exhaustion would propagate as "StackOverflowException"
        // from somewhere deep in the reconciler. The guard turns it into an
        // actionable InvalidOperationException naming the actual cause.
        var s = new InMemoryStore<int>();
        s.OnChange += () => s.Set("x", Random.Shared.Next());
        var ex = Assert.Throws<InvalidOperationException>(() => s.Set("x", 0));
        Assert.Contains("recursion", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnChange_listener_can_mutate_once_without_tripping_the_guard()
    {
        // Derived-value pattern: listener fires, writes one derived key, done.
        // Must not throw — the guard is for UNBOUNDED recursion only.
        var s = new InMemoryStore<int>();
        var fired = false;
        s.OnChange += () =>
        {
            if (fired) return;
            fired = true;
            s.Set("derived", 99);
        };
        s.Set("x", 1);
        Assert.Equal(99, s.Get("derived"));
    }
}
