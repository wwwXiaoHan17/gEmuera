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
        Assert.Equal("", CompatPackLauncherConfig.SerializeSelection(new[] { "  ", null! }));
    }

    [Fact]
    public void NormalizeGameKey_CaseSensitivePlatform_PreservesCase()
    {
        // 大小写敏感文件系统（Android/Linux）：仅差大小写的两个游戏根是不同目录，
        // 键不得折叠——否则 A 的包选择会静默套到 B（E2-R4）。
        string keyUpper = CompatPackLauncherConfig.NormalizeGameKey("/tmp/GameA", caseSensitiveFilesystem: true);
        string keyLower = CompatPackLauncherConfig.NormalizeGameKey("/tmp/gamea", caseSensitiveFilesystem: true);
        Assert.NotEqual(keyUpper, keyLower);
        Assert.Equal("/tmp/GameA", keyUpper);
        Assert.Equal("/tmp/gamea", keyLower);

        // 大小写不敏感平台（Windows）仍折叠大小写（既有存储键语义）；去尾斜杠对两种分隔符都成立。
        Assert.Equal("d:\\games\\erafl", CompatPackLauncherConfig.NormalizeGameKey("D:\\Games\\EraFL\\", caseSensitiveFilesystem: false));
        Assert.Equal("d:\\games\\erafl", CompatPackLauncherConfig.NormalizeGameKey("D:\\Games\\EraFL\\", caseSensitiveFilesystem: !OperatingSystem.IsWindows()));
        Assert.Equal("/tmp/GameA", CompatPackLauncherConfig.NormalizeGameKey("/tmp/GameA/", caseSensitiveFilesystem: true));
        Assert.Equal("", CompatPackLauncherConfig.NormalizeGameKey("   ", caseSensitiveFilesystem: true));
        Assert.Equal("", CompatPackLauncherConfig.NormalizeGameKey(null, caseSensitiveFilesystem: false));
    }

    [Fact]
    public void TryGetSelectionForGame_RejectsCaseMismatchOnCaseSensitive()
    {
        var selections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/tmp/GameA"] = "a.dll",
        };

        // 存储键与查询同键（仅尾斜杠差异）→ 命中。
        Assert.True(CompatPackLauncherConfig.TryGetSelectionForGame(
            selections, "/tmp/GameA/", caseSensitiveFilesystem: true, out var paths));
        Assert.Equal(new[] { "a.dll" }, paths);

        // 大小写敏感平台上 /tmp/gamea 是另一个目录，不得命中 /tmp/GameA 的选择。
        Assert.False(CompatPackLauncherConfig.TryGetSelectionForGame(
            selections, "/tmp/gamea", caseSensitiveFilesystem: true, out _));
    }

    [Fact]
    public void ExternalEnvOverride_DoesNotPermanentlyShadowLauncherSelection()
    {
        // stored（launcher 按游戏选择）非空时优先：外部 GEMUERA_COMPAT_PACKS 不得永久
        // 遮蔽 launcher 选择（E2-R3）。
        Assert.Equal("a.dll;b.dll",
            CompatPackLauncherConfig.MergeSelection("a.dll;b.dll", "external.dll"));
        Assert.Equal("a.dll",
            CompatPackLauncherConfig.MergeSelection("a.dll", "external.dll;another.dll"));

        // stored 为空（该游戏无按游戏选择）时外部 env 仅作为诊断输入透传（既有契约）。
        Assert.Equal("external.dll", CompatPackLauncherConfig.MergeSelection(null, "external.dll"));
        Assert.Equal("external.dll", CompatPackLauncherConfig.MergeSelection("", "external.dll"));
        Assert.Equal("external.dll", CompatPackLauncherConfig.MergeSelection("  ;  ", "external.dll"));

        // 两路皆空 → 空串（清除注入，回纯 v24）。
        Assert.Equal("", CompatPackLauncherConfig.MergeSelection(null, null));
        Assert.Equal("", CompatPackLauncherConfig.MergeSelection("", "  ;  "));

        // stored 经 parse→serialize 归一（trim/去空/去重保序）。
        Assert.Equal("a.dll;b.dll", CompatPackLauncherConfig.MergeSelection(" a.dll ; ; b.dll ; a.dll ", null));
    }

    [Fact]
    public void MappedDefault_UsedOnlyWhenStoredAndExternalAreEmpty()
    {
        // 执行设计 §4.5：注入优先级 P0 stored > P1 external > P2 mapped > P3 纯基线。
        Assert.Equal("stored.dll", CompatPackLauncherConfig.MergeSelection("stored.dll", "external.dll", "mapped.dll"));
        Assert.Equal("external.dll", CompatPackLauncherConfig.MergeSelection(null, "external.dll", "mapped.dll"));
        Assert.Equal("mapped.dll", CompatPackLauncherConfig.MergeSelection(null, null, "mapped.dll"));
        Assert.Equal("mapped.dll", CompatPackLauncherConfig.MergeSelection("", "  ;  ", "mapped.dll"));
        Assert.Equal("", CompatPackLauncherConfig.MergeSelection(null, null, null));
    }

    [Fact]
    public void RunnerSwitch_DoesNotInheritPreviousGamePackEnvironment()
    {
        // runner/in-process switch 的非沿用语义（E2-R3）：注入 env 按目标游戏经
        // TryGetSelectionForGame + MergeSelection 管线重算（即 FirstWindow 注入链与
        // ConfigureLegacyRunnerSession 刷新用的同一纯函数管线），切到无选择的游戏 B
        // 时清空，绝不残留上一局 A 的清单。
        var selections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["d:/games/gamea"] = "a-pack.dll",
        };
        const string externalDiagnosticEnv = "external-diagnostic.dll";

        // 第一局：A 有按游戏选择 → 选择优先（外部 env 被遮蔽）。
        Assert.True(CompatPackLauncherConfig.TryGetSelectionForGame(selections, "D:/Games/GameA", out var aPaths));
        string envForA = CompatPackLauncherConfig.MergeSelection(
            CompatPackLauncherConfig.SerializeSelection(aPaths), externalDiagnosticEnv);
        Assert.Equal("a-pack.dll", envForA);

        // 切换：B 无按游戏选择 → 不沿用 A；外部诊断输入透传（仍不是 A 的清单）。
        Assert.False(CompatPackLauncherConfig.TryGetSelectionForGame(selections, "D:/Games/GameB", out var bPaths));
        Assert.Empty(bPaths);
        string envForB = CompatPackLauncherConfig.MergeSelection(
            bPaths.Count > 0 ? CompatPackLauncherConfig.SerializeSelection(bPaths) : null,
            externalDiagnosticEnv);
        Assert.Equal("external-diagnostic.dll", envForB);

        // 无外部诊断输入时切到 B → 完全清空（回纯 v24）："不沿用上一局"的硬断言。
        string envForBWithoutExternal = CompatPackLauncherConfig.MergeSelection(null, null);
        Assert.Equal(string.Empty, envForBWithoutExternal);
    }

    [Fact]
    public void NormalizePackPath_FullPathUnifiesSeparatorsAndTrimsTrailingSlash()
    {
        string expected = "d:" + System.IO.Path.DirectorySeparatorChar
            + "games" + System.IO.Path.DirectorySeparatorChar + "p.dll";
        Assert.Equal(expected, CompatPackLauncherConfig.NormalizePackPath("d:/games/p.dll/"));
        Assert.Equal(expected, CompatPackLauncherConfig.NormalizePackPath("d:\\games\\p.dll\\"));

        // 相对路径经 GetFullPath 绝对化（UI 侧先按启动器根绝对化，这里断言不再相对）。
        string relative = CompatPackLauncherConfig.NormalizePackPath("some-pack.dll");
        Assert.True(System.IO.Path.IsPathRooted(relative));
        Assert.EndsWith("some-pack.dll", relative, StringComparison.Ordinal);

        // 空白 → 空串；非法路径字符不抛异常（fail-closed 校验留给加载器）。
        Assert.Equal("", CompatPackLauncherConfig.NormalizePackPath("   "));
        Assert.Equal("", CompatPackLauncherConfig.NormalizePackPath(null));
        string invalidOnWindows = CompatPackLauncherConfig.NormalizePackPath("a<b.dll");
        Assert.False(invalidOnWindows.EndsWith(System.IO.Path.DirectorySeparatorChar), "尾部斜杠必须去除");
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
        Assert.False(CompatPackLauncherConfig.TryGetSelectionForGame(null!, "D:/Games/EraFL", out _));
    }
}
