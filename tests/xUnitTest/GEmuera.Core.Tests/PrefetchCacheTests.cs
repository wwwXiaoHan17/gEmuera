using GEmuera.Core.Agent.Cache;
using Xunit;

namespace GEmuera.Core.Tests;

public class PrefetchCacheTests
{
    [Fact]
    public void PutThenGet_Hits()
    {
        var cache = new KoujouPrefetchCache();
        cache.Put("TARGET=1|朝|好感档1", "早安，主人。");

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet("TARGET=1|朝|好感档1", out string text));
        Assert.Equal("早安，主人。", text);
    }

    [Fact]
    public void GetUnknownKey_Misses()
    {
        var cache = new KoujouPrefetchCache();
        Assert.False(cache.TryGet("nope", out string text));
        Assert.Equal("", text);
    }

    [Fact]
    public void Clear_EmptiesAll()
    {
        var cache = new KoujouPrefetchCache();
        cache.Put("a", "1");
        cache.Put("b", "2");
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void PutSameKey_Overwrites()
    {
        var cache = new KoujouPrefetchCache();
        cache.Put("k", "old");
        cache.Put("k", "new");
        Assert.True(cache.TryGet("k", out string text));
        Assert.Equal("new", text);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void PutEmptyKey_Ignored()
    {
        var cache = new KoujouPrefetchCache();
        cache.Put("", "x");
        cache.Put(null!, "y");
        Assert.Equal(0, cache.Count);
    }
}
