using System.Text;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 启用清单的过期包改绑判定（<see cref="CompatPackStaleSelectionRebinder"/>）门禁：
///   选中的包对当前引擎不可用时，若 App 内置了同名且可用的包、且不是同一个文件，就改用内置包；
///   否则原样保留（整组包仍会被引擎拒载并回退基线）并给出可见警告。
/// 文件系统与真实清单经委托注入，这里用内存表驱动，专注判定分支。
/// </summary>
public class CompatPackStaleSelectionRebinderTests
{
    const string Stale = @"D:\Packs\pack.gemuera.snake\pack.gemuera.snake.dll";
    const string Bundled = @"D:\gemuera\compat_packs\pack.gemuera.snake\pack.gemuera.snake.dll";
    const string Community = @"D:\Packs\community.foo.dll";

    static readonly CompatPackEngineHeader Engine = new(new CompatVersion(1, 1), new CompatVersion(1, 0),
        "v24pure", new string('a', 64), new HashSet<string>(StringComparer.Ordinal));

    static CompatPackPeekResult Fresh(string packId, string hash)
    {
        string json = "{\"packId\":\"" + packId + "\",\"packVersion\":\"1.0.0\",\"baseProfileId\":\"v24pure\","
            + "\"contractAbi\":\"1.0\",\"engineApi\":\"1.0\",\"capabilities\":[],\"surface\":{},"
            + "\"baseSurfaceHash\":\"" + hash + "\"}";
        return CompatPackManifestPeek.Assess(Parse(json), Engine);
    }

    static CompatPackPeekResult StalePack(string packId) => Fresh(packId, FirstPartyPackBytes.StaleHash);

    static CompatPackManifest Parse(string json)
    {
        Assert.True(CompatPackManifest.TryParse(json, out var manifest, out var errors), string.Join("; ", errors));
        return manifest!;
    }

    static CompatPackPeekResult Unreadable() =>
        CompatPackManifestPeek.Assess(new byte[] { 1, 2, 3 }, Engine);

    sealed class World
    {
        public readonly Dictionary<string, CompatPackPeekResult> Files =
            new(CompatPackLauncherConfig.GameKeyComparer);
        public readonly HashSet<string> BundledPackIds = new(StringComparer.Ordinal);

        public CompatPackSelectionResolution Resolve(params string[] selected) =>
            CompatPackStaleSelectionRebinder.Resolve(
                selected,
                path => Files.TryGetValue(path, out var verdict) ? verdict : Unreadable(),
                packId => BundledPackIds.Contains(packId) ? Bundled : null,
                CompatPackLauncherConfig.GameKeyComparer);
    }

    [Fact]
    public void AllCompatible_PathsUnchanged_NoNotices()
    {
        var world = new World();
        world.Files[Stale] = Fresh("pack.gemuera.snake", Engine.BaselineSurfaceHash);
        world.Files[Community] = Fresh("community.foo", Engine.BaselineSurfaceHash);

        var result = world.Resolve(Stale, Community);

        Assert.Equal(new[] { Stale, Community }, result.Paths);
        Assert.Empty(result.Rebinds);
        Assert.Empty(result.Warnings);
        Assert.False(result.Changed);
    }

    [Fact]
    public void StaleFirstParty_WithUsableBundledCopyElsewhere_Rebinds()
    {
        var world = new World();
        world.Files[Stale] = StalePack("pack.gemuera.snake");
        world.Files[Bundled] = Fresh("pack.gemuera.snake", Engine.BaselineSurfaceHash);
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(Stale);

        Assert.Equal(new[] { Bundled }, result.Paths);
        CompatPackRebindNotice notice = Assert.Single(result.Rebinds);
        Assert.Equal(Stale, notice.OldPath);
        Assert.Equal(Bundled, notice.NewPath);
        Assert.Empty(result.Warnings);
        Assert.Contains("pack.gemuera.snake.dll", notice.Message);
        Assert.Contains("表面哈希", notice.Message);
        Assert.Contains("已改用内置包", notice.Message);
    }

    [Fact]
    public void Rebind_PreservesSelectionOrder_AndOtherPacks()
    {
        var world = new World();
        world.Files[Community] = Fresh("community.foo", Engine.BaselineSurfaceHash);
        world.Files[Stale] = StalePack("pack.gemuera.snake");
        world.Files[Bundled] = Fresh("pack.gemuera.snake", Engine.BaselineSurfaceHash);
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(Community, Stale);

        Assert.Equal(new[] { Community, Bundled }, result.Paths);
    }

    [Fact]
    public void StaleFirstParty_AtTheBundledPathItself_OnlyWarns()
    {
        // 安卓常见：选中的就是安装根里那份（已被用户覆盖成旧版）。换路径解决不了，交释放器处理；这里必须提示而不是静默。
        var world = new World();
        world.Files[Bundled] = StalePack("pack.gemuera.snake");
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(Bundled);

        Assert.Equal(new[] { Bundled }, result.Paths);
        Assert.Empty(result.Rebinds);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("pack.gemuera.snake.dll", warning);
        Assert.Contains("v24 基线", warning);
        Assert.True(result.Changed);
    }

    [Fact]
    public void StaleFirstParty_BundledCopyAlsoStale_OnlyWarns()
    {
        var world = new World();
        world.Files[Stale] = StalePack("pack.gemuera.snake");
        world.Files[Bundled] = StalePack("pack.gemuera.snake");
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(Stale);

        Assert.Equal(new[] { Stale }, result.Paths);
        Assert.Empty(result.Rebinds);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void StaleFirstParty_NotBundled_OnlyWarns()
    {
        var world = new World();
        world.Files[Stale] = StalePack("pack.gemuera.snake");

        var result = world.Resolve(Stale);

        Assert.Equal(new[] { Stale }, result.Paths);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void StaleCommunityPack_IsNeverRebound_EvenIfItsFileNameLooksFirstParty()
    {
        // 内置表按清单里的 packId 查，不按文件名猜：清单说它是 community.foo，文件名再像第一方包也不改绑。
        var world = new World();
        world.Files[@"D:\Packs\community.foo\pack.gemuera.snake.dll"] = StalePack("community.foo");
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(@"D:\Packs\community.foo\pack.gemuera.snake.dll");

        Assert.Equal(new[] { @"D:\Packs\community.foo\pack.gemuera.snake.dll" }, result.Paths);
        Assert.Empty(result.Rebinds);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void MissingFile_NamedAfterABundledPack_RebindsByFileName()
    {
        // 文件不存在时读不到清单：按文件名查内置表（用户删过目录、或路径挪动后的自愈）。
        var world = new World();
        world.Files[Bundled] = Fresh("pack.gemuera.snake", Engine.BaselineSurfaceHash);
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(Stale);

        Assert.Equal(new[] { Bundled }, result.Paths);
        Assert.Single(result.Rebinds);
    }

    [Fact]
    public void MissingFile_WithNonPackFileName_OnlyWarns()
    {
        var world = new World();
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(@"D:\Packs\not a pack.txt");

        Assert.Equal(new[] { @"D:\Packs\not a pack.txt" }, result.Paths);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void BundledCopyAlreadySelected_DoesNotDuplicate()
    {
        var world = new World();
        world.Files[Stale] = StalePack("pack.gemuera.snake");
        world.Files[Bundled] = Fresh("pack.gemuera.snake", Engine.BaselineSurfaceHash);
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(Bundled, Stale);

        Assert.Equal(new[] { Bundled }, result.Paths);
        Assert.Single(result.Rebinds);
    }

    [Fact]
    public void SameFile_DifferentSpelling_IsRecognizedAsTheBundledPath()
    {
        var world = new World();
        world.Files[Bundled] = StalePack("pack.gemuera.snake");
        world.BundledPackIds.Add("pack.gemuera.snake");

        var result = world.Resolve(@"d:\GEMUERA\compat_packs\pack.gemuera.snake\pack.gemuera.snake.dll");

        Assert.Empty(result.Rebinds);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void PeekThrows_TreatedAsUnusable_AndNeverEscapes()
    {
        var result = CompatPackStaleSelectionRebinder.Resolve(
            new[] { Community },
            _ => throw new IOException("locked"),
            _ => null,
            CompatPackLauncherConfig.GameKeyComparer);

        Assert.Equal(new[] { Community }, result.Paths);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("community.foo.dll", warning);
    }
}
