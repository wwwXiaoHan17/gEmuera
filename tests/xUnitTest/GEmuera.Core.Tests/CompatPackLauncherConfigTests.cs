using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>launcher.cfg 兼容包选择的序列化/解析/匹配语义（设计 §5.1 显式启用）。</summary>
public class CompatPackLauncherConfigTests
{
    [Fact]
    public void NormalizeGameKey_TrimsSlashesAndLowercases()
    {
        Assert.Equal("d:\\games\\erafl", CompatPackLauncherConfig.NormalizeGameKey("D:\\Games\\EraFL\\"));
        Assert.Equal("", CompatPackLauncherConfig.NormalizeGameKey(null));
        Assert.Equal("", CompatPackLauncherConfig.NormalizeGameKey("   "));
    }

    [Fact]
    public void ParseSelection_SplitsTrimsDedupsPreservingOrder()
    {
        var paths = CompatPackLauncherConfig.ParseSelection(" a.dll ; ; b.dll ; a.dll ;c.dll");
        Assert.Equal(new[] { "a.dll", "b.dll", "c.dll" }, paths);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" ; ; ")]
    public void ParseSelection_EmptyInputs_ReturnEmptyList(string? packed)
    {
        Assert.Empty(CompatPackLauncherConfig.ParseSelection(packed));
    }

    [Fact]
    public void SerializeSelection_RoundTripsWithParse()
    {
        string packed = CompatPackLauncherConfig.SerializeSelection(new[] { "a.dll", "b.dll" });
        Assert.Equal("a.dll;b.dll", packed);
        Assert.Equal(new[] { "a.dll", "b.dll" }, CompatPackLauncherConfig.ParseSelection(packed));
        Assert.Equal("", CompatPackLauncherConfig.SerializeSelection(Array.Empty<string>()));
        Assert.Equal("", CompatPackLauncherConfig.SerializeSelection(new[] { "  ", null }));
    }

    [Fact]
    public void TryGetSelectionForGame_MatchesNormalizedKey()
    {
        var selections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["d:\\games\\erafl"] = "a.dll;b.dll",
        };

        Assert.True(CompatPackLauncherConfig.TryGetSelectionForGame(selections, "D:\\Games\\EraFL\\", out var paths));
        Assert.Equal(new[] { "a.dll", "b.dll" }, paths);

        Assert.False(CompatPackLauncherConfig.TryGetSelectionForGame(selections, "D:/Games/Other", out _));
        Assert.False(CompatPackLauncherConfig.TryGetSelectionForGame(selections, null, out _));
        Assert.False(CompatPackLauncherConfig.TryGetSelectionForGame(null, "D:/Games/EraFL", out _));
    }
}
