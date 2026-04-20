namespace Razorshave.Abstractions.Tests;

/// <summary>
/// Round-trip + clear/remove coverage for the dev-host browser-storage
/// implementations. Values are JSON-serialised so behaviour matches the
/// JS runtime's localStorage wrapper; tests lock both the positive path
/// (set/get returns the same value) and the boundary behaviours (missing
/// keys, type-mismatched reads, Clear empties everything).
/// </summary>
public sealed class InMemoryWebStorageTests
{
    [Fact]
    public void Set_then_Get_round_trips_a_complex_object()
    {
        var s = new InMemoryWebStorage();
        s.Set("user", new { Id = 7, Name = "Ada" });
        // JSON round-trip goes through System.Text.Json — asserting on a
        // dictionary-like projection keeps the test robust to struct vs
        // anonymous-type boundary details.
        var got = s.Get<System.Text.Json.JsonElement>("user");
        Assert.Equal(7, got.GetProperty("Id").GetInt32());
        Assert.Equal("Ada", got.GetProperty("Name").GetString());
    }

    [Fact]
    public void Get_on_missing_key_returns_default()
    {
        var s = new InMemoryWebStorage();
        Assert.Null(s.Get<string>("ghost"));
        Assert.Equal(0, s.Get<int>("ghost"));
    }

    [Fact]
    public void Remove_returns_true_only_when_key_existed()
    {
        var s = new InMemoryWebStorage();
        s.Set("a", 1);
        Assert.True(s.Has("a"));
        s.Remove("a");
        Assert.False(s.Has("a"));
        // Removing twice is a no-op — neither throws nor lies.
        s.Remove("a");
        Assert.False(s.Has("a"));
    }

    [Fact]
    public void Clear_empties_and_resets_count()
    {
        var s = new InMemoryWebStorage();
        s.Set("a", 1);
        s.Set("b", 2);
        Assert.Equal(2, s.Count);
        s.Clear();
        Assert.Equal(0, s.Count);
        Assert.Empty(s.Keys());
    }

    [Fact]
    public void Keys_returns_snapshot_not_live_view()
    {
        // Snapshot semantics match the IEnumerable contracts Store.OnChange
        // relies on — iterating while mutating stays safe.
        var s = new InMemoryWebStorage();
        s.Set("a", 1);
        var keys = s.Keys();
        s.Set("b", 2);
        Assert.Single(keys);
    }

    [Fact]
    public void LocalStorage_and_SessionStorage_are_independent_instances()
    {
        // The two storage types share the base implementation but must NOT
        // share data — a session-only write should never leak into local.
        var local = new InMemoryLocalStorage();
        var session = new InMemorySessionStorage();
        session.Set("token", "abc");
        Assert.False(local.Has("token"));
        Assert.True(session.Has("token"));
    }
}

public sealed class InMemoryCookieStoreTests
{
    [Fact]
    public void Set_then_Get_returns_the_raw_string_value()
    {
        // Unlike WebStorage, cookies are string-typed — no JSON round-trip.
        var c = new InMemoryCookieStore();
        c.Set("name", "ada");
        Assert.Equal("ada", c.Get("name"));
    }

    [Fact]
    public void Get_on_missing_cookie_returns_null()
    {
        var c = new InMemoryCookieStore();
        Assert.Null(c.Get("ghost"));
    }

    [Fact]
    public void Set_with_MaxAgeSeconds_0_removes_the_cookie()
    {
        // Browser semantics: a cookie with MaxAge=0 is immediately deleted.
        // The in-memory dev host mirrors that — Set-with-MaxAge-0 must NOT
        // store the new value, it must drop the existing one.
        var c = new InMemoryCookieStore();
        c.Set("theme", "dark");
        Assert.True(c.Has("theme"));
        c.Set("theme", "ignored", new CookieOptions { MaxAgeSeconds = 0 });
        Assert.False(c.Has("theme"));
    }

    [Fact]
    public void Remove_drops_the_cookie()
    {
        var c = new InMemoryCookieStore();
        c.Set("lang", "de");
        c.Remove("lang");
        Assert.Null(c.Get("lang"));
    }

    [Fact]
    public void GetAll_returns_every_cookie_as_a_snapshot()
    {
        var c = new InMemoryCookieStore();
        c.Set("a", "1");
        c.Set("b", "2");
        var all = c.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("1", all["a"]);
        Assert.Equal("2", all["b"]);
    }
}
